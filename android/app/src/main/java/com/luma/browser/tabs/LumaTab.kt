package com.luma.browser.tabs

import android.graphics.Bitmap

data class LumaTab(
    val id: String = java.util.UUID.randomUUID().toString(),
    var url: String = "",
    var title: String = "Новая вкладка",
    var favicon: Bitmap? = null,
    var isHome: Boolean = true,
    var isLoading: Boolean = false,
    var progress: Int = 0,
    var canGoBack: Boolean = false,
    var canGoForward: Boolean = false,
    var isIncognito: Boolean = false
)
