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

data class AiMessage(val role: String, val content: String)

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

        // Ensure session exists
        if (prefs.accessToken.isBlank()) {
            LumaApp.instance.ensureSession()
            // Wait up to 2 seconds for token
            var tries = 0
            while (prefs.accessToken.isBlank() && tries < 20) {
                kotlinx.coroutines.delay(100)
                tries++
            }
        }

        val token = prefs.accessToken

        val messagesArr = JSONArray()
        messagesArr.put(JSONObject().apply {
            put("role", "system")
            put("content", systemPrompt)
        })
        for (msg in messages) {
            if (msg.content.isNotBlank()) {
                messagesArr.put(JSONObject().apply {
                    put("role", msg.role)
                    put("content", msg.content)
                })
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
