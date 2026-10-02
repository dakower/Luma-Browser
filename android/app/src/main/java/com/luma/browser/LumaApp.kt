package com.luma.browser

import android.app.Application
import android.util.Log
import com.luma.browser.storage.LumaPreferences
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.io.File
import java.util.Date
import java.util.UUID

class LumaApp : Application() {

    companion object {
        lateinit var instance: LumaApp
            private set

        const val SUPABASE_URL = "https://ejwjlifyfnhgsbvsjefd.supabase.co"
        const val SUPABASE_PUBLISHABLE_KEY = "sb_publishable_2B84eUvPBBY4W4zYmbWbFQ_ZU0ot7A4"
        const val APP_VERSION = "2.1.4"
    }

    private val httpClient = OkHttpClient()

    override fun onCreate() {
        super.onCreate()
        instance = this

        // Global crash handler: records crash details into filesDir/crash_log.txt
        val originalHandler = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, throwable ->
            try {
                Log.e("LumaCrash", "FATAL CRASH on thread ${thread.name}", throwable)
                val crashFile = File(filesDir, "crash_log.txt")
                val logEntry = "CRASH on [${thread.name}] at ${Date()}:\n" + throwable.stackTraceToString() + "\n\n"
                crashFile.appendText(logEntry)
            } catch (_: Exception) {}

            originalHandler?.uncaughtException(thread, throwable)
        }

        // Auto initialize / verify guest authentication session safely
        try {
            ensureSession()
        } catch (e: Exception) {
            Log.e("LumaApp", "ensureSession failed safely", e)
        }
    }

    fun ensureSession() {
        try {
            val prefs = LumaPreferences.get()
            // Clear any legacy guest credentials so only real logged-in users have access
            if (prefs.email.startsWith("guest_") || (prefs.accessToken.isNotBlank() && prefs.email.isBlank())) {
                prefs.accessToken = ""
                prefs.refreshToken = ""
                prefs.email = ""
                prefs.displayName = ""
            }

            // Only refresh session if user is genuinely logged in
            if (prefs.isLoggedIn && prefs.refreshToken.isNotBlank()) {
                CoroutineScope(Dispatchers.IO).launch {
                    try {
                        val body = JSONObject().apply {
                            put("refresh_token", prefs.refreshToken)
                        }.toString().toRequestBody("application/json".toMediaType())

                        val req = Request.Builder()
                            .url("$SUPABASE_URL/auth/v1/token?grant_type=refresh_token")
                            .post(body)
                            .addHeader("apikey", SUPABASE_PUBLISHABLE_KEY)
                            .addHeader("Content-Type", "application/json")
                            .build()

                        val resp = httpClient.newCall(req).execute()
                        val respStr = resp.body?.string() ?: ""
                        if (resp.isSuccessful) {
                            val json = JSONObject(respStr)
                            val token = json.optString("access_token", "")
                            if (token.isNotBlank()) {
                                prefs.accessToken = token
                                prefs.refreshToken = json.optString("refresh_token", prefs.refreshToken)
                            }
                        }
                    } catch (_: Exception) {}
                }
            }
        } catch (e: Exception) {
            Log.e("LumaApp", "ensureSession exception", e)
        }
    }
}
