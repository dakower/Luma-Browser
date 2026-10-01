package com.luma.browser.support

import com.luma.browser.LumaApp
import com.luma.browser.storage.LumaPreferences
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.util.UUID
import java.util.concurrent.TimeUnit

data class SupportChatMessage(
    val id: String = UUID.randomUUID().toString(),
    val isUser: Boolean,
    val text: String,
    val imageBase64: String? = null,
    val imageUrl: String? = null,
    val timestamp: Long = System.currentTimeMillis()
)

/**
 * LumaSupportService — direct support chat with creator dakower.
 * Connects to /functions/v1/luma-support which relays directly to Telegram bot.
 */
class LumaSupportService {

    private val client = OkHttpClient.Builder()
        .connectTimeout(12, TimeUnit.SECONDS)
        .readTimeout(25, TimeUnit.SECONDS)
        .build()

    var threadId: String? = null

    suspend fun sendMessage(
        text: String,
        imageBase64: String? = null,
        imageName: String? = null,
        imageMime: String? = null
    ): Boolean = withContext(Dispatchers.IO) {
        val prefs = LumaPreferences.get()
        val gid = prefs.userId.ifBlank { "android_guest" }
        val name = prefs.displayName.ifBlank { "Пользователь Android" }

        val body = JSONObject().apply {
            put("action", "send")
            put("guestId", gid)
            put("displayName", name)
            put("version", "${LumaApp.APP_VERSION}-Android")
            put("body", text)
            if (!imageBase64.isNullOrBlank()) {
                put("attachment", JSONObject().apply {
                    put("name", imageName ?: "image.jpg")
                    put("mime", imageMime ?: "image/jpeg")
                    put("data", imageBase64)
                })
            }
            if (!threadId.isNullOrBlank()) {
                put("threadId", threadId)
            }
        }

        val request = Request.Builder()
            .url("${LumaApp.SUPABASE_URL}/functions/v1/luma-support")
            .post(body.toString().toRequestBody("application/json".toMediaType()))
            .addHeader("apikey", LumaApp.SUPABASE_PUBLISHABLE_KEY)
            .addHeader("Content-Type", "application/json")
            .build()

        try {
            val response = client.newCall(request).execute()
            if (response.isSuccessful) {
                val respBody = response.body?.string()
                if (respBody != null) {
                    val json = JSONObject(respBody)
                    val tid = json.optString("threadId", "")
                    if (tid.isNotBlank()) threadId = tid
                }
                true
            } else false
        } catch (_: Exception) { false }
    }

    suspend fun pollMessages(): List<SupportChatMessage> = withContext(Dispatchers.IO) {
        val prefs = LumaPreferences.get()
        val gid = prefs.userId.ifBlank { "android_guest" }

        val body = JSONObject().apply {
            put("action", "poll")
            put("guestId", gid)
            if (!threadId.isNullOrBlank()) {
                put("threadId", threadId)
            }
        }

        val request = Request.Builder()
            .url("${LumaApp.SUPABASE_URL}/functions/v1/luma-support")
            .post(body.toString().toRequestBody("application/json".toMediaType()))
            .addHeader("apikey", LumaApp.SUPABASE_PUBLISHABLE_KEY)
            .addHeader("Content-Type", "application/json")
            .build()

        try {
            val response = client.newCall(request).execute()
            if (!response.isSuccessful) return@withContext emptyList()
            val respStr = response.body?.string() ?: return@withContext emptyList()
            val json = JSONObject(respStr)
            val tid = json.optString("threadId", "")
            if (tid.isNotBlank()) threadId = tid

            val msgs = json.optJSONArray("messages") ?: return@withContext emptyList()
            (0 until msgs.length()).map { idx ->
                val obj = msgs.getJSONObject(idx)
                var bodyText = obj.optString("body", "")
                var imgUrl = obj.optString("image_url", "").ifBlank { null }
                if (imgUrl == null) {
                    val match = Regex("""\[image:(https?://[^\]]+)\]""").find(bodyText)
                    if (match != null) {
                        imgUrl = match.groupValues[1]
                        bodyText = bodyText.replace(match.value, "").trim()
                    }
                }
                SupportChatMessage(
                    id = obj.optString("id", UUID.randomUUID().toString()),
                    isUser = obj.optString("sender_type", "user") == "user",
                    text = bodyText,
                    imageUrl = imgUrl,
                    timestamp = System.currentTimeMillis()
                )
            }
        } catch (_: Exception) { emptyList() }
    }
}
