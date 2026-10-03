package com.luma.browser.ai

import com.luma.browser.LumaApp
import com.luma.browser.storage.LumaPreferences
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.ConnectionPool
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedReader
import java.io.InputStreamReader
import java.net.SocketException
import java.net.SocketTimeoutException
import java.net.UnknownHostException
import java.util.concurrent.TimeUnit

data class AiMessage(
    val role: String,
    val content: String,
    val imageBase64: String? = null,
    val imageBitmap: android.graphics.Bitmap? = null
)

/**
 * LumaAiService — connects to /functions/v1/luma-assistant (SSE streaming).
 * Handles Supabase session auth, auto token refresh on 401, and robust SSE stream parsing.
 */
class LumaAiService {

    private val client = OkHttpClient.Builder()
        .connectTimeout(25, TimeUnit.SECONDS)
        .readTimeout(90, TimeUnit.SECONDS)
        .writeTimeout(60, TimeUnit.SECONDS)
        .retryOnConnectionFailure(true)
        .connectionPool(ConnectionPool(5, 2, TimeUnit.MINUTES))
        .build()

    suspend fun refreshAuthToken(): String? = withContext(Dispatchers.IO) {
        val prefs = LumaPreferences.get()
        val refresh = prefs.refreshToken
        if (refresh.isBlank()) return@withContext null
        try {
            val body = JSONObject().apply {
                put("refresh_token", refresh)
            }
            val req = Request.Builder()
                .url("${LumaApp.SUPABASE_URL}/auth/v1/token?grant_type=refresh_token")
                .post(body.toString().toRequestBody("application/json".toMediaType()))
                .addHeader("apikey", LumaApp.SUPABASE_PUBLISHABLE_KEY)
                .addHeader("Content-Type", "application/json")
                .build()
            client.newCall(req).execute().use { resp ->
                if (!resp.isSuccessful) return@withContext null
                val json = JSONObject(resp.body?.string() ?: return@withContext null)
                val newAccess = json.optString("access_token", "")
                val newRefresh = json.optString("refresh_token", "")
                if (newAccess.isNotBlank()) {
                    prefs.accessToken = newAccess
                    if (newRefresh.isNotBlank()) prefs.refreshToken = newRefresh
                    android.util.Log.d("LumaAiService", "Token refreshed successfully")
                    newAccess
                } else null
            }
        } catch (e: Exception) {
            android.util.Log.e("LumaAiService", "Failed to refresh token: ${e.message}")
            null
        }
    }

    suspend fun syncQuotaAndRole(): Boolean = withContext(Dispatchers.IO) {
        val prefs = LumaPreferences.get()
        if (!prefs.isLoggedIn) return@withContext false
        val token = prefs.accessToken
        val uid = prefs.userId
        if (token.isBlank()) return@withContext false

        try {
            // 1. Check get_my_assistant_quota
            val quotaReq = Request.Builder()
                .url("${LumaApp.SUPABASE_URL}/rest/v1/rpc/get_my_assistant_quota")
                .post("{}".toRequestBody("application/json".toMediaType()))
                .addHeader("apikey", LumaApp.SUPABASE_PUBLISHABLE_KEY)
                .addHeader("Authorization", "Bearer $token")
                .addHeader("Content-Type", "application/json")
                .build()
            client.newCall(quotaReq).execute().use { quotaResp ->
                if (quotaResp.isSuccessful) {
                    val qJson = JSONObject(quotaResp.body?.string() ?: "{}")
                    if (qJson.optBoolean("unlimited", false)) {
                        prefs.isUnlimitedAi = true
                        return@withContext true
                    }
                }
            }

            // 2. Check get_my_beta_access
            val betaReq = Request.Builder()
                .url("${LumaApp.SUPABASE_URL}/rest/v1/rpc/get_my_beta_access")
                .post("{}".toRequestBody("application/json".toMediaType()))
                .addHeader("apikey", LumaApp.SUPABASE_PUBLISHABLE_KEY)
                .addHeader("Authorization", "Bearer $token")
                .addHeader("Content-Type", "application/json")
                .build()
            client.newCall(betaReq).execute().use { betaResp ->
                if (betaResp.isSuccessful) {
                    val bJson = JSONObject(betaResp.body?.string() ?: "{}")
                    val isBeta = bJson.optBoolean("has_beta", false)
                    val isAdmin = bJson.optBoolean("is_admin", false)
                    val status = bJson.optString("beta_status", "")
                    if (isBeta || isAdmin || status == "approved") {
                        prefs.isUnlimitedAi = true
                        if (isAdmin) prefs.userRole = "admin"
                        return@withContext true
                    }
                }
            }

            // 3. Check profiles table directly
            if (uid.isNotBlank()) {
                val profReq = Request.Builder()
                    .url("${LumaApp.SUPABASE_URL}/rest/v1/profiles?id=eq.$uid&select=*")
                    .addHeader("apikey", LumaApp.SUPABASE_PUBLISHABLE_KEY)
                    .addHeader("Authorization", "Bearer $token")
                    .get()
                    .build()
                client.newCall(profReq).execute().use { profResp ->
                    if (profResp.isSuccessful) {
                        val arr = JSONArray(profResp.body?.string() ?: "[]")
                        if (arr.length() > 0) {
                            val p = arr.getJSONObject(0)
                            val role = p.optString("role", "user")
                            prefs.userRole = role
                            val isVip = p.optBoolean("is_vip", false)
                            val unl = p.optBoolean("unlimited_ai", false)
                            if (role.equals("admin", true) || role.equals("tester", true) || role.equals("beta", true) || isVip || unl) {
                                prefs.isUnlimitedAi = true
                                return@withContext true
                            }
                        }
                    }
                }
            }
        } catch (_: Exception) {}
        false
    }

    private fun explainError(code: Int, rawBody: String): String {
        val errorJson = try { JSONObject(rawBody) } catch (_: Exception) { null }
        val errCode = errorJson?.optString("error", "") ?: ""
        val msg = errorJson?.optString("message", "") ?: ""
        val hint = when {
            code == 401 -> "Войдите в аккаунт Luma ID, чтобы пользоваться LumaAI."
            code == 429 || errCode == "daily_limit_reached" -> "Дневной лимит запросов LumaAI исчерпан (15 в день). Лимит обновится после полуночи UTC."
            code == 413 -> "Изображение или контекст слишком большие."
            code == 422 -> "Выбранная модель не смогла обработать изображение."
            code >= 500 -> "Сервис LumaAI временно недоступен. Попробуйте снова через минуту."
            else -> "Не удалось выполнить запрос LumaAI (код $code)."
        }
        return if (msg.isNotBlank() && !hint.contains(msg)) "$hint $msg" else hint
    }

    suspend fun streamChat(
        messages: List<AiMessage>,
        systemPrompt: String,
        model: String = "fast",
        onChunk: suspend (String) -> Unit,
        onDone: suspend () -> Unit,
        onError: suspend (String) -> Unit
    ) = withContext(Dispatchers.IO) {

        val prefs = LumaPreferences.get()

        // Strict login requirement
        if (!prefs.isLoggedIn) {
            onError("Для использования LumaAI необходимо войти в аккаунт Luma ID.")
            return@withContext
        }

        var token = prefs.accessToken

        // Ensure we never send more than 10 history items to prevent request payload bloat
        val limitedMessages = if (messages.size > 10) messages.takeLast(10) else messages

        // Keep image only in the last message (or at most last 2 image messages) to prevent huge repeating payloads
        val imageMessagesCount = limitedMessages.count { !it.imageBase64.isNullOrBlank() }
        var imageCounter = 0

        val messagesArr = JSONArray()
        messagesArr.put(JSONObject().apply {
            put("role", "system")
            put("content", systemPrompt)
        })
        for (msg in limitedMessages) {
            val hasImage = !msg.imageBase64.isNullOrBlank()
            val includeImage = hasImage && (++imageCounter > (imageMessagesCount - 2))

            if (msg.content.isNotBlank() || includeImage) {
                val item = JSONObject().apply {
                    put("role", msg.role)
                    if (!includeImage || msg.imageBase64.isNullOrBlank()) {
                        put("content", msg.content.ifBlank { "..." })
                    } else {
                        val parts = JSONArray()
                        if (msg.content.isNotBlank()) {
                            parts.put(JSONObject().apply {
                                put("type", "text")
                                put("text", msg.content)
                            })
                        }
                        parts.put(JSONObject().apply {
                            put("type", "image_url")
                            put("image_url", JSONObject().apply {
                                val cleanB64 = msg.imageBase64.substringAfter("base64,")
                                put("url", "data:image/jpeg;base64,$cleanB64")
                            })
                        })
                        put("content", parts)
                    }
                }
                messagesArr.put(item)
            }
        }

        val body = JSONObject().apply {
            put("model", if (model == "pro") "pro" else "fast")
            put("stream", true)
            put("messages", messagesArr)
        }

        fun makeRequest(tok: String): Request {
            val builder = Request.Builder()
                .url("${LumaApp.SUPABASE_URL}/functions/v1/luma-assistant")
                .post(body.toString().toRequestBody("application/json".toMediaType()))
                .addHeader("apikey", LumaApp.SUPABASE_PUBLISHABLE_KEY)
                .addHeader("Content-Type", "application/json")
                .addHeader("Accept", "text/event-stream")
            if (tok.isNotBlank()) {
                builder.addHeader("Authorization", "Bearer $tok")
            }
            return builder.build()
        }

        try {
            var response = client.newCall(makeRequest(token)).execute()

            // If JWT expired (401), auto-refresh and retry once
            if (response.code == 401) {
                response.close()
                val freshToken = refreshAuthToken()
                if (!freshToken.isNullOrBlank()) {
                    token = freshToken
                    response = client.newCall(makeRequest(token)).execute()
                }
            }

            if (!response.isSuccessful) {
                val errBody = response.body?.string() ?: ""
                response.close()
                onError(explainError(response.code, errBody))
                return@withContext
            }

            // Sync server quota header
            val unlimitedHeader = response.header("X-Luma-Quota-Unlimited")
            if (unlimitedHeader.equals("true", ignoreCase = true)) {
                prefs.isUnlimitedAi = true
            }

            val bodyStream = response.body
            if (bodyStream == null) {
                response.close()
                onError("Пустой ответ от сервера")
                return@withContext
            }

            bodyStream.byteStream().use { inputStream ->
                BufferedReader(InputStreamReader(inputStream, Charsets.UTF_8)).use { reader ->
                    var line: String?
                    while (reader.readLine().also { line = it } != null) {
                        val l = line?.trim() ?: continue
                        if (l.isEmpty()) continue
                        if (l.startsWith("data:")) {
                            val data = l.substring(5).trim()
                            if (data == "[DONE]") break
                            try {
                                val json = JSONObject(data)
                                val choices = json.optJSONArray("choices") ?: continue
                                if (choices.length() > 0) {
                                    val delta = choices.getJSONObject(0).optJSONObject("delta")
                                    val text = delta?.optString("content", "") ?: ""
                                    if (text.isNotEmpty() && text != "null") {
                                        onChunk(text)
                                    }
                                }
                            } catch (_: Exception) {}
                        }
                    }
                }
            }
            response.close()
            onDone()
        } catch (e: Exception) {
            val readableMsg = when (e) {
                is SocketTimeoutException -> "Время ожидания ответа ассистента истекло."
                is UnknownHostException -> "Нет подключения к интернету."
                is SocketException -> "Соединение с сервером прервано. Повторите запрос."
                else -> e.message ?: "Ошибка сети"
            }
            onError(readableMsg)
        }
    }
}
