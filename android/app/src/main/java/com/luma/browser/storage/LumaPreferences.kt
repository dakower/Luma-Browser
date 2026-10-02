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
 *
 * Fully protected against ClassCastException or type mismatch across app updates.
 */
class LumaPreferences private constructor(context: Context) {

    private val prefs: SharedPreferences = try {
        context.getSharedPreferences("luma_prefs_v2", Context.MODE_PRIVATE)
    } catch (_: Exception) {
        // Fallback to in-memory/default prefs if storage cannot be accessed
        context.applicationContext.getSharedPreferences("luma_prefs_fallback", Context.MODE_PRIVATE)
    }

    companion object {
        @Volatile private var instance: LumaPreferences? = null
        fun get(): LumaPreferences = instance ?: synchronized(this) {
            instance ?: LumaPreferences(LumaApp.instance).also { instance = it }
        }
    }

    private fun safeGetString(key: String, def: String): String {
        return try {
            prefs.getString(key, def) ?: def
        } catch (_: Exception) {
            try {
                prefs.all[key]?.toString() ?: def
            } catch (_: Exception) {
                def
            }
        }
    }

    private fun safeGetBoolean(key: String, def: Boolean): Boolean {
        return try {
            prefs.getBoolean(key, def)
        } catch (_: Exception) {
            try {
                when (val v = prefs.all[key]) {
                    is Boolean -> v
                    is String -> v.toBooleanStrictOrNull() ?: def
                    is Number -> v.toInt() != 0
                    else -> def
                }
            } catch (_: Exception) {
                def
            }
        }
    }

    private fun safeGetInt(key: String, def: Int): Int {
        return try {
            prefs.getInt(key, def)
        } catch (_: Exception) {
            try {
                when (val v = prefs.all[key]) {
                    is Number -> v.toInt()
                    is String -> v.toIntOrNull() ?: def
                    else -> def
                }
            } catch (_: Exception) {
                def
            }
        }
    }

    private fun safeSet(action: SharedPreferences.Editor.() -> Unit) {
        try {
            val editor = prefs.edit()
            editor.action()
            editor.apply()
        } catch (_: Exception) {}
    }

    // ====== AUTHENTICATION & USER ======
    var deviceGuestId: String
        get() {
            var id = safeGetString("device_guest_id", "")
            if (id.isBlank()) {
                id = "guest_" + java.util.UUID.randomUUID().toString().replace("-", "").take(12)
                safeSet { putString("device_guest_id", id) }
            }
            return id
        }
        set(v) = safeSet { putString("device_guest_id", v) }

    var userId: String
        get() = safeGetString("user_id", "")
        set(v) = safeSet { putString("user_id", v) }

    var accessToken: String
        get() = safeGetString("access_token", "")
        set(v) = safeSet { putString("access_token", v) }

    var refreshToken: String
        get() = safeGetString("refresh_token", "")
        set(v) = safeSet { putString("refresh_token", v) }

    var displayName: String
        get() = safeGetString("display_name", "")
        set(v) = safeSet { putString("display_name", v) }

    var email: String
        get() = safeGetString("email", "")
        set(v) = safeSet { putString("email", v) }

    val isLoggedIn: Boolean
        get() = accessToken.isNotBlank() && email.isNotBlank()

    // ====== SEARCH & NAVIGATION ======
    var searchEngine: String
        get() = safeGetString("search_engine", "https://www.google.com/search?q={q}")
        set(v) = safeSet { putString("search_engine", v) }

    var searchEngineName: String
        get() = safeGetString("search_engine_name", "Google")
        set(v) = safeSet { putString("search_engine_name", v) }

    // ====== HOME TAB CUSTOMIZATION ======
    var homeSearchShape: String // "pill", "rounded", "square"
        get() = safeGetString("home_search_shape", "pill")
        set(v) = safeSet { putString("home_search_shape", v) }

    var homeSearchStyle: String // "glass", "solid", "outline", "glow"
        get() = safeGetString("home_search_style", "glass")
        set(v) = safeSet { putString("home_search_style", v) }

    var homeClockVisible: Boolean
        get() = safeGetBoolean("home_clock_visible", true)
        set(v) = safeSet { putBoolean("home_clock_visible", v) }

    var homeDateVisible: Boolean
        get() = safeGetBoolean("home_date_visible", true)
        set(v) = safeSet { putBoolean("home_date_visible", v) }

    var homeHeadlineVisible: Boolean
        get() = safeGetBoolean("home_headline_visible", true)
        set(v) = safeSet { putBoolean("home_headline_visible", v) }

    var homePillsVisible: Boolean
        get() = safeGetBoolean("home_pills_visible", true)
        set(v) = safeSet { putBoolean("home_pills_visible", v) }

    var activeSpaceId: String
        get() = safeGetString("active_space_id", "main")
        set(v) = safeSet { putString("active_space_id", v) }

    // ====== PRIVACY & SECURITY ======
    var adBlockEnabled: Boolean
        get() = safeGetBoolean("adblock_enabled", true)
        set(v) = safeSet { putBoolean("adblock_enabled", v) }

    var trackerBlockEnabled: Boolean
        get() = safeGetBoolean("tracker_block_enabled", true)
        set(v) = safeSet { putBoolean("tracker_block_enabled", v) }

    var httpsOnly: Boolean
        get() = safeGetBoolean("https_only", false)
        set(v) = safeSet { putBoolean("https_only", v) }

    var javascriptEnabled: Boolean
        get() = safeGetBoolean("javascript_enabled", true)
        set(v) = safeSet { putBoolean("javascript_enabled", v) }

    var desktopMode: Boolean
        get() = safeGetBoolean("desktop_mode", false)
        set(v) = safeSet { putBoolean("desktop_mode", v) }

    // ====== APPEARANCE & THEMES ======
    var appTheme: String // "dark", "light", "system"
        get() = safeGetString("app_theme", "dark")
        set(v) = safeSet { putString("app_theme", v) }

    // ====== LUMAAI ======
    var aiModel: String // "fast", "pro"
        get() = safeGetString("ai_model", "fast")
        set(v) = safeSet { putString("ai_model", v) }

    var aiIncludeTabContext: Boolean
        get() = safeGetBoolean("ai_include_tab_context", true)
        set(v) = safeSet { putBoolean("ai_include_tab_context", v) }

    var userRole: String
        get() = safeGetString("user_role", "user")
        set(v) = safeSet { putString("user_role", v) }

    var isUnlimitedAi: Boolean
        get() = safeGetBoolean("is_unlimited_ai", false)
        set(v) = safeSet { putBoolean("is_unlimited_ai", v) }

    var aiQuotaUsed: Int
        get() = safeGetInt("ai_quota_used", 0)
        set(v) = safeSet { putInt("ai_quota_used", v) }

    var aiQuotaDate: String
        get() = safeGetString("ai_quota_date", "")
        set(v) = safeSet { putString("ai_quota_date", v) }

    fun getRemainingAiQuota(): Int {
        if (isUnlimitedAi || userRole.equals("admin", ignoreCase = true)) return 999
        val today = java.text.SimpleDateFormat("yyyy-MM-dd", java.util.Locale.getDefault()).format(java.util.Date())
        if (aiQuotaDate != today) {
            aiQuotaDate = today
            aiQuotaUsed = 0
        }
        return (15 - aiQuotaUsed).coerceAtLeast(0)
    }

    fun consumeAiQuota(): Boolean {
        if (isUnlimitedAi || userRole.equals("admin", ignoreCase = true)) return true
        val rem = getRemainingAiQuota()
        if (rem <= 0) return false
        aiQuotaUsed += 1
        return true
    }

    // ====== DATA STORES (JSON) ======
    var bookmarksJson: String
        get() = safeGetString("bookmarks_json", "[]")
        set(v) = safeSet { putString("bookmarks_json", v) }

    var historyJson: String
        get() = safeGetString("history_json", "[]")
        set(v) = safeSet { putString("history_json", v) }

    var spacesJson: String
        get() = safeGetString("spaces_json", "")
        set(v) = safeSet { putString("spaces_json", v) }

    var savedChatsJson: String
        get() = safeGetString("saved_chats_json", "[]")
        set(v) = safeSet { putString("saved_chats_json", v) }
}
