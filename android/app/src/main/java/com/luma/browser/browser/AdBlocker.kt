package com.luma.browser.browser

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.BufferedReader
import java.io.InputStreamReader

/**
 * AdBlocker — offline ad, tracker and telemetry blocker.
 * Loads 70,000+ domains from assets/adblock/hosts.txt into an in-memory hash set.
 */
object AdBlocker {

    private val blockedHosts = HashSet<String>(80000)
    private var isLoaded = false

    suspend fun init(context: Context) = withContext(Dispatchers.IO) {
        if (isLoaded) return@withContext
        try {
            val stream = context.assets.open("adblock/hosts.txt")
            BufferedReader(InputStreamReader(stream)).use { reader ->
                var line: String?
                while (reader.readLine().also { line = it } != null) {
                    val trimmed = line?.trim() ?: continue
                    if (trimmed.isEmpty() || trimmed.startsWith("#")) continue
                    val parts = trimmed.split(Regex("\\s+"))
                    if (parts.size >= 2) {
                        val host = parts[1].lowercase()
                        if (host != "localhost" && host != "broadcasthost") {
                            blockedHosts.add(host)
                        }
                    }
                }
            }
            isLoaded = true
        } catch (_: Exception) {
            // Fallback hardcoded list if asset not available
            blockedHosts.addAll(listOf(
                "doubleclick.net", "google-analytics.com", "googletagmanager.com",
                "pagead2.googlesyndication.com", "ads.yandex.ru", "an.yandex.ru",
                "mc.yandex.ru", "counter.yadro.ru", "ads.facebook.com", "adnxs.com"
            ))
        }
    }

    fun isAd(host: String): Boolean {
        val cleanHost = host.lowercase().removePrefix("www.")
        if (blockedHosts.contains(cleanHost)) return true
        // Check parent domains
        var dotIdx = cleanHost.indexOf('.')
        while (dotIdx != -1 && dotIdx < cleanHost.length - 1) {
            val parent = cleanHost.substring(dotIdx + 1)
            if (blockedHosts.contains(parent)) return true
            dotIdx = cleanHost.indexOf('.', dotIdx + 1)
        }
        return false
    }

    val totalBlockedRules: Int
        get() = blockedHosts.size
}
