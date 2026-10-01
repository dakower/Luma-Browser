package com.luma.browser.storage

import android.content.Context
import android.content.SharedPreferences
import com.luma.browser.LumaApp

/**
 * LumaPreferences — persistent settings storage mirroring BrowserState.cs from desktop.
 */
class LumaPreferences private constructor(context: Context) {

    private val prefs: SharedPreferences = context.getSharedPreferences("luma_prefs", Context.MODE_PRIVATE)

    companion object {
        @Volatile private var instance: LumaPreferences? = null
        fun get() = instance ?: synchronized(this) {
            instance ?: LumaPreferences(LumaApp.instance).also { instance = it }
        }
    }

    // Core settings
    var accessToken: String get() = prefs.getString("access_token", "") ?: ""
        set(v) = prefs.edit().putString("access_token", v).apply()

    var refreshToken: String get() = prefs.getString("refresh_token", "") ?: ""
        set(v) = prefs.edit().putString("refresh_token", v).apply()

    var displayName: String get() = prefs.getString("display_name", "Гость") ?: "Гость"
        set(v) = prefs.edit().putString("display_name", v).apply()

    var email: String get() = prefs.getString("email", "") ?: ""
        set(v) = prefs.edit().putString("email", v).apply()

    var searchEngine: String get() = prefs.getString("search_engine", "https://www.google.com/search?q={q}") ?: "https://www.google.com/search?q={q}"
        set(v) = prefs.edit().putString("search_engine", v).apply()

    var adBlockEnabled: Boolean get() = prefs.getBoolean("adblock", true)
        set(v) = prefs.edit().putBoolean("adblock", v).apply()

    var aiModel: String get() = prefs.getString("ai_model", "fast") ?: "fast"
        set(v) = prefs.edit().putString("ai_model", v).apply()

    // Bookmarks (stored as JSON array string)
    var bookmarksJson: String get() = prefs.getString("bookmarks", "[]") ?: "[]"
        set(v) = prefs.edit().putString("bookmarks", v).apply()

    // History (stored as JSON array string, max 1000 entries)
    var historyJson: String get() = prefs.getString("history", "[]") ?: "[]"
        set(v) = prefs.edit().putString("history", v).apply()

    // Homepage / new tab
    var homeUrl: String get() = prefs.getString("home_url", "") ?: ""
        set(v) = prefs.edit().putString("home_url", v).apply()

    // User ID (for support chat)
    var userId: String get() = prefs.getString("user_id", "") ?: ""
        set(v) = prefs.edit().putString("user_id", v).apply()

    var desktopMode: Boolean get() = prefs.getBoolean("desktop_mode", false)
        set(v) = prefs.edit().putBoolean("desktop_mode", v).apply()
}
