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
            if (prefs.userId.isBlank()) {
                prefs.userId = "android_" + UUID.randomUUID().toString().replace("-", "").take(12)
            }

            // If we don't have a valid access token, auto-sign up or login a guest account
            if (prefs.accessToken.isBlank()) {
                CoroutineScope(Dispatchers.IO).launch {
                    try {
                        val email = "guest_${prefs.userId}@lumabrowser.win"
                        val password = "LumaGuestSecret2026!_${prefs.userId}"

                        val signupBody = JSONObject().apply {
                            put("email", email)
                            put("password", password)
                        }.toString().toRequestBody("application/json".toMediaType())

                        val signupReq = Request.Builder()
                            .url("$SUPABASE_URL/auth/v1/signup")
                            .post(signupBody)
                            .addHeader("apikey", SUPABASE_PUBLISHABLE_KEY)
                            .addHeader("Content-Type", "application/json")
                            .build()

                        val resp = httpClient.newCall(signupReq).execute()
                        val respStr = resp.body?.string() ?: ""

                        if (resp.isSuccessful) {
                            val json = JSONObject(respStr)
                            val token = json.optString("access_token", "")
                            if (token.isNotBlank()) {
                                prefs.accessToken = token
                                prefs.refreshToken = json.optString("refresh_token", "")
                            }
                        } else {
                            // Maybe already signed up, try password login
                            val loginBody = JSONObject().apply {
                                put("email", email)
                                put("password", password)
                            }.toString().toRequestBody("application/json".toMediaType())

                            val loginReq = Request.Builder()
                                .url("$SUPABASE_URL/auth/v1/token?grant_type=password")
                                .post(loginBody)
                                .addHeader("apikey", SUPABASE_PUBLISHABLE_KEY)
                                .addHeader("Content-Type", "application/json")
                                .build()

                            val loginResp = httpClient.newCall(loginReq).execute()
                            val loginStr = loginResp.body?.string() ?: ""
                            if (loginResp.isSuccessful) {
                                val json = JSONObject(loginStr)
                                val token = json.optString("access_token", "")
                                if (token.isNotBlank()) {
                                    prefs.accessToken = token
                                    prefs.refreshToken = json.optString("refresh_token", "")
                                }
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
