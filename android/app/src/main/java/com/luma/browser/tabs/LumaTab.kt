package com.luma.browser.tabs

import android.graphics.Bitmap
import java.util.UUID

data class LumaTab(
    val id: String = UUID.randomUUID().toString(),
    var spaceId: String = "main",
    var url: String = "",
    var title: String = "Новая вкладка",
    var favicon: Bitmap? = null,
    var isHome: Boolean = true,
    var isLoading: Boolean = false,
    var progress: Int = 0,
    var canGoBack: Boolean = false,
    var canGoForward: Boolean = false,
    var isIncognito: Boolean = false,
    var lastActiveTime: Long = System.currentTimeMillis()
)
