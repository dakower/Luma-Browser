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
    val videoBase64List: List<String> = emptyList(),
    val videoUrls: List<String> = emptyList(),
    val videoThumbnailBitmaps: List<android.graphics.Bitmap> = emptyList(),
    val timestamp: Long = System.currentTimeMillis()
) {
    val imageBase64: String? get() = imageBase64List.firstOrNull()
    val imageUrl: String? get() = imageUrls.firstOrNull()
    val videoUrl: String? get() = videoUrls.firstOrNull()
    val hasImages: Boolean get() = imageBase64List.isNotEmpty() || imageUrls.isNotEmpty()
    val hasVideos: Boolean get() = videoBase64List.isNotEmpty() || videoUrls.isNotEmpty() || videoThumbnailBitmaps.isNotEmpty()
    val isVideo: Boolean get() = hasVideos
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
                    val isVid = mime.startsWith("video") || mime.contains("mp4") || mime.contains("webm")
                    val ext = if (isVid) (if (mime.contains("webm")) "webm" else "mp4") else "jpg"
                    arr.put(JSONObject().apply {
                        put("name", if (isVid) "video_${idx + 1}.$ext" else "photo_${idx + 1}.$ext")
                        put("mime", mime.ifBlank { if (isVid) "video/mp4" else "image/jpeg" })
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
                val foundVideoUrls = mutableListOf<String>()

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

                val vidArr = obj.optJSONArray("video_urls")
                if (vidArr != null) {
                    for (i in 0 until vidArr.length()) {
                        val u = vidArr.optString(i, "")
                        if (u.isNotBlank() && u != "null" && (u.startsWith("http://") || u.startsWith("https://"))) {
                            foundVideoUrls.add(u)
                        }
                    }
                }

                if (foundVideoUrls.isEmpty() && !obj.isNull("video_url")) {
                    val single = obj.optString("video_url", "").trim()
                    if (single.isNotBlank() && single != "null" && (single.startsWith("http://") || single.startsWith("https://"))) {
                        foundVideoUrls.add(single)
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

                val vidRegex = Regex("""\[video:(https?://[^\]]+)\]""")
                val vidMatches = vidRegex.findAll(bodyText).toList()
                if (vidMatches.isNotEmpty()) {
                    for (m in vidMatches) {
                        val extracted = m.groupValues[1]
                        if (extracted.isNotBlank() && extracted != "null" && !foundVideoUrls.contains(extracted)) {
                            foundVideoUrls.add(extracted)
                        }
                    }
                    bodyText = vidRegex.replace(bodyText, "").trim()
                }

                SupportChatMessage(
                    id = obj.optString("id", UUID.randomUUID().toString()),
                    isUser = obj.optString("sender_type", "user") == "user",
                    text = bodyText,
                    imageUrls = foundUrls,
                    videoUrls = foundVideoUrls,
                    timestamp = System.currentTimeMillis()
                )
            }
        } catch (_: Exception) { emptyList() }
    }
}
