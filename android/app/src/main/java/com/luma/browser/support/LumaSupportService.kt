package com.luma.browser.support

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
import java.util.UUID
import java.util.concurrent.TimeUnit

data class SupportChatMessage(
    var id: String = UUID.randomUUID().toString(),
    val isUser: Boolean,
    val text: String,
    val imageBase64List: List<String> = emptyList(),
    val imageUrls: List<String> = emptyList(),
    val timestamp: Long = System.currentTimeMillis()
) {
    val imageBase64: String? get() = imageBase64List.firstOrNull()
    val imageUrl: String? get() = imageUrls.firstOrNull()
    val hasImages: Boolean get() = imageBase64List.isNotEmpty() || imageUrls.isNotEmpty()
}

/**
 * LumaSupportService — direct support chat with creator dakower.
 * Connects to /functions/v1/luma-support which relays directly to Telegram bot.
 */
class LumaSupportService {

    private val client = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(35, TimeUnit.SECONDS)
        .build()

    var threadId: String? = null

    suspend fun sendMessage(
        text: String,
        attachments: List<Pair<String, String>> = emptyList() // List of (base64, mime)
    ): String? = withContext(Dispatchers.IO) {
        val prefs = LumaPreferences.get()
        val gid = prefs.userId.ifBlank { prefs.deviceGuestId }
        val name = prefs.displayName.ifBlank { "Пользователь Android" }

        val body = JSONObject().apply {
            put("action", "send")
            put("guestId", gid)
            put("displayName", name)
            put("version", "${LumaApp.APP_VERSION}-Android")
            put("body", text)
            if (attachments.isNotEmpty()) {
                val arr = JSONArray()
                attachments.forEachIndexed { idx, (b64, mime) ->
                    arr.put(JSONObject().apply {
                        put("name", "photo_${idx + 1}.jpg")
                        put("mime", mime.ifBlank { "image/jpeg" })
                        put("data", b64)
                    })
                }
                put("attachments", arr)
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
                    val msgId = json.optString("messageId", "")
                    if (msgId.isNotBlank()) return@withContext msgId
                }
                "ok"
            } else null
        } catch (_: Exception) { null }
    }

    suspend fun pollMessages(): List<SupportChatMessage> = withContext(Dispatchers.IO) {
        val prefs = LumaPreferences.get()
        val gid = prefs.userId.ifBlank { prefs.deviceGuestId }

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
                var bodyText = if (obj.isNull("body")) "" else obj.optString("body", "")
                val foundUrls = mutableListOf<String>()

                val urlsArr = obj.optJSONArray("image_urls")
                if (urlsArr != null) {
                    for (i in 0 until urlsArr.length()) {
                        val u = urlsArr.optString(i, "")
                        if (u.isNotBlank() && u != "null" && (u.startsWith("http://") || u.startsWith("https://"))) {
                            foundUrls.add(u)
                        }
                    }
                }

                if (foundUrls.isEmpty() && !obj.isNull("image_url")) {
                    val single = obj.optString("image_url", "").trim()
                    if (single.isNotBlank() && single != "null" && (single.startsWith("http://") || single.startsWith("https://"))) {
                        foundUrls.add(single)
                    }
                }

                val regex = Regex("""\[image:(https?://[^\]]+)\]""")
                val matches = regex.findAll(bodyText).toList()
                if (matches.isNotEmpty()) {
                    for (m in matches) {
                        val extracted = m.groupValues[1]
                        if (extracted.isNotBlank() && extracted != "null" && !foundUrls.contains(extracted)) {
                            foundUrls.add(extracted)
                        }
                    }
                    bodyText = regex.replace(bodyText, "").trim()
                }

                SupportChatMessage(
                    id = obj.optString("id", UUID.randomUUID().toString()),
                    isUser = obj.optString("sender_type", "user") == "user",
                    text = bodyText,
                    imageUrls = foundUrls,
                    timestamp = System.currentTimeMillis()
                )
            }
        } catch (_: Exception) { emptyList() }
    }
}
