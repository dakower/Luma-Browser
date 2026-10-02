package com.luma.browser.ai

import com.luma.browser.LumaApp
import com.luma.browser.storage.LumaPreferences
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedReader
import java.io.InputStreamReader
import java.util.concurrent.TimeUnit

data class AiMessage(
    val role: String,
    val content: String,
    val imageBase64: String? = null,
    val imageBitmap: android.graphics.Bitmap? = null
)

/**
 * LumaAiService — connects to /functions/v1/luma-assistant (SSE streaming).
 * Handles Supabase session auth and direct SSE line-by-line reading.
 */
class LumaAiService {

    private val client = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(60, TimeUnit.SECONDS)
        .build()

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

        val token = prefs.accessToken

        val messagesArr = JSONArray()
        messagesArr.put(JSONObject().apply {
            put("role", "system")
            put("content", systemPrompt)
        })
        for (msg in messages) {
            if (msg.content.isNotBlank() || !msg.imageBase64.isNullOrBlank()) {
                val item = JSONObject().apply {
                    put("role", msg.role)
                    if (msg.imageBase64.isNullOrBlank()) {
                        put("content", msg.content)
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
                                put("url", "data:image/jpeg;base64,${msg.imageBase64}")
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

        val requestBuilder = Request.Builder()
            .url("${LumaApp.SUPABASE_URL}/functions/v1/luma-assistant")
            .post(body.toString().toRequestBody("application/json".toMediaType()))
            .addHeader("apikey", LumaApp.SUPABASE_PUBLISHABLE_KEY)
            .addHeader("Content-Type", "application/json")
            .addHeader("Accept", "text/event-stream")

        if (token.isNotBlank()) {
            requestBuilder.addHeader("Authorization", "Bearer $token")
        }

        try {
            val response = client.newCall(requestBuilder.build()).execute()
            if (!response.isSuccessful) {
                val errBody = response.body?.string() ?: ""
                val msg = try {
                    JSONObject(errBody).optString("message", "HTTP ${response.code}")
                } catch (_: Exception) {
                    "HTTP ${response.code}: $errBody"
                }
                onError(msg)
                return@withContext
            }

            val inputStream = response.body?.byteStream()
            if (inputStream == null) {
                onError("Пустой ответ от сервера")
                return@withContext
            }

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

            onDone()
        } catch (e: Exception) {
            onError(e.message ?: "Ошибка сети")
        }
    }
}
