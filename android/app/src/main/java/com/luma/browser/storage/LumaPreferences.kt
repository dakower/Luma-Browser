package com.luma.browser.storage

import android.content.Context
import android.content.SharedPreferences
import com.luma.browser.LumaApp

/**
 * Full Luma Preferences — all settings mirroring desktop Luma (BrowserState.cs):
 * - Search engine configuration
 * - Home tab personalization (search shape, style, toggles)
 * - Privacy & Ad blocking
 * - Appearance & Themes
 * - LumaAI preferences
 */
class LumaPreferences private constructor(context: Context) {

    private val prefs: SharedPreferences = context.getSharedPreferences("luma_prefs_v2", Context.MODE_PRIVATE)

    companion object {
        @Volatile private var instance: LumaPreferences? = null
        fun get(): LumaPreferences = instance ?: synchronized(this) {
            instance ?: LumaPreferences(LumaApp.instance).also { instance = it }
        }
    }

    // ====== AUTHENTICATION & USER ======
    var userId: String
        get() = prefs.getString("user_id", "") ?: ""
        set(v) = prefs.edit().putString("user_id", v).apply()

    var accessToken: String
        get() = prefs.getString("access_token", "") ?: ""
        set(v) = prefs.edit().putString("access_token", v).apply()

    var refreshToken: String
        get() = prefs.getString("refresh_token", "") ?: ""
        set(v) = prefs.edit().putString("refresh_token", v).apply()

    var displayName: String
        get() = prefs.getString("display_name", "Пользователь Luma") ?: "Пользователь Luma"
        set(v) = prefs.edit().putString("display_name", v).apply()

    var email: String
        get() = prefs.getString("email", "") ?: ""
        set(v) = prefs.edit().putString("email", v).apply()

    // ====== SEARCH & NAVIGATION ======
    var searchEngine: String
        get() = prefs.getString("search_engine", "https://www.google.com/search?q={q}") ?: "https://www.google.com/search?q={q}"
        set(v) = prefs.edit().putString("search_engine", v).apply()

    var searchEngineName: String
        get() = prefs.getString("search_engine_name", "Google") ?: "Google"
        set(v) = prefs.edit().putString("search_engine_name", v).apply()

    // ====== HOME TAB CUSTOMIZATION ======
    var homeSearchShape: String // "pill", "rounded", "square"
        get() = prefs.getString("home_search_shape", "pill") ?: "pill"
        set(v) = prefs.edit().putString("home_search_shape", v).apply()

    var homeSearchStyle: String // "glass", "solid", "outline", "glow"
        get() = prefs.getString("home_search_style", "glass") ?: "glass"
        set(v) = prefs.edit().putString("home_search_style", v).apply()

    var homeClockVisible: Boolean
        get() = prefs.getBoolean("home_clock_visible", true)
        set(v) = prefs.edit().putBoolean("home_clock_visible", v).apply()

    var homeDateVisible: Boolean
        get() = prefs.getBoolean("home_date_visible", true)
        set(v) = prefs.edit().putBoolean("home_date_visible", v).apply()

    var homeHeadlineVisible: Boolean
        get() = prefs.getBoolean("home_headline_visible", true)
        set(v) = prefs.edit().putBoolean("home_headline_visible", v).apply()

    var homePillsVisible: Boolean
        get() = prefs.getBoolean("home_pills_visible", true)
        set(v) = prefs.edit().putBoolean("home_pills_visible", v).apply()

    var activeSpaceId: String
        get() = prefs.getString("active_space_id", "main") ?: "main"
        set(v) = prefs.edit().putString("active_space_id", v).apply()

    // ====== PRIVACY & SECURITY ======
    var adBlockEnabled: Boolean
        get() = prefs.getBoolean("adblock_enabled", true)
        set(v) = prefs.edit().putBoolean("adblock_enabled", v).apply()

    var trackerBlockEnabled: Boolean
        get() = prefs.getBoolean("tracker_block_enabled", true)
        set(v) = prefs.edit().putBoolean("tracker_block_enabled", v).apply()

    var httpsOnly: Boolean
        get() = prefs.getBoolean("https_only", false)
        set(v) = prefs.edit().putBoolean("https_only", v).apply()

    var javascriptEnabled: Boolean
        get() = prefs.getBoolean("javascript_enabled", true)
        set(v) = prefs.edit().putBoolean("javascript_enabled", v).apply()

    var desktopMode: Boolean
        get() = prefs.getBoolean("desktop_mode", false)
        set(v) = prefs.edit().putBoolean("desktop_mode", v).apply()

    // ====== LUMAAI ======
    var aiModel: String // "fast", "pro"
        get() = prefs.getString("ai_model", "fast") ?: "fast"
        set(v) = prefs.edit().putString("ai_model", v).apply()

    var aiIncludeTabContext: Boolean
        get() = prefs.getBoolean("ai_include_tab_context", true)
        set(v) = prefs.edit().putBoolean("ai_include_tab_context", v).apply()

    // ====== DATA STORES (JSON) ======
    var bookmarksJson: String
        get() = prefs.getString("bookmarks_json", "[]") ?: "[]"
        set(v) = prefs.edit().putString("bookmarks_json", v).apply()

    var historyJson: String
        get() = prefs.getString("history_json", "[]") ?: "[]"
        set(v) = prefs.edit().putString("history_json", v).apply()

    var spacesJson: String
        get() = prefs.getString("spaces_json", "") ?: ""
        set(v) = prefs.edit().putString("spaces_json", v).apply()

    var savedChatsJson: String
        get() = prefs.getString("saved_chats_json", "[]") ?: "[]"
        set(v) = prefs.edit().putString("saved_chats_json", v).apply()
}
