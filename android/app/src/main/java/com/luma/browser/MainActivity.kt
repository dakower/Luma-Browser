package com.luma.browser

import android.annotation.SuppressLint
import android.app.DownloadManager
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.text.Editable
import android.text.TextWatcher
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.View
import android.view.WindowInsetsController
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import android.graphics.BitmapFactory
import android.util.Base64
import android.webkit.*
import android.widget.*
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.google.android.material.bottomsheet.BottomSheetBehavior
import com.google.android.material.bottomsheet.BottomSheetDialog
import com.google.android.material.materialswitch.MaterialSwitch
import com.luma.browser.ai.AiMessage
import com.luma.browser.ai.LumaAiService
import com.luma.browser.browser.AdBlocker
import com.luma.browser.browser.PageTools
import com.luma.browser.storage.LumaPreferences
import com.luma.browser.support.LumaSupportService
import com.luma.browser.support.SupportChatMessage
import com.luma.browser.tabs.LumaTab
import com.luma.browser.ui.*
import kotlinx.coroutines.*
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder
import java.util.*

/**
 * MainActivity — Luma Browser for Android.
 * iOS Safari-style bottom navigation bar, harmonious home screen,
 * non-blocking real-time LumaAI assistant, and complete desktop feature parity.
 */
class MainActivity : AppCompatActivity() {

    // ====== TABS ======
    private val tabs = mutableListOf<LumaTab>()
    private var activeTab: LumaTab? = null

    // ====== SERVICES ======
    private val aiService = LumaAiService()
    private val supportService = LumaSupportService()
    private val prefs by lazy { LumaPreferences.get() }

    // ====== SUPPORT IMAGE ATTACHMENT ======
    private var onSupportImagePicked: ((Uri) -> Unit)? = null
    private val pickSupportImageLauncher = registerForActivityResult(
        ActivityResultContracts.GetContent()
    ) { uri: Uri? ->
        uri?.let { onSupportImagePicked?.invoke(it) }
    }

    // ====== AI CHAT STATE ======
    private val aiMessages = mutableListOf<AiMessage>()
    private var aiAdapter: AiChatAdapter? = null
    private var aiStreaming = false

    // ====== JOBS ======
    private var supportPollJob: Job? = null

    // ====== GESTURE SWIPE ======
    private var touchStartX = 0f

    // ====== VIEW REFS ======
    private lateinit var webView: WebView
    private lateinit var pageProgress: ProgressBar
    private lateinit var homeDashboard: ScrollView
    private lateinit var homeHeadline: TextView
    private lateinit var homeSearchShell: LinearLayout
    private lateinit var homeSearchBox: EditText
    private lateinit var homeSearchBtn: FrameLayout
    private lateinit var homePillsContainer: LinearLayout

    // Safari-style Bottom Bar
    private lateinit var bottomBar: LinearLayout
    private lateinit var bottomAddressPill: LinearLayout
    private lateinit var bottomBarLogo: ImageView
    private lateinit var bottomAddressBar: TextView
    private lateinit var btnReload: ImageButton
    private lateinit var btnBottomAi: FrameLayout
    private lateinit var btnTabCount: FrameLayout
    private lateinit var tabCountText: TextView
    private lateinit var btnMenu: ImageButton

    // Safari-style Search & Navigation Overlay
    private lateinit var searchOverlay: LinearLayout
    private lateinit var overlaySearchInput: EditText
    private lateinit var overlaySearchClear: ImageButton
    private lateinit var overlaySearchCancel: TextView
    private lateinit var searchSuggestionsRecycler: RecyclerView
    private lateinit var searchSuggestionAdapter: SearchSuggestionAdapter
    private var searchDebounceJob: Job? = null

    // Reader Mode
    private lateinit var readerOverlay: LinearLayout
    private lateinit var readerTitle: TextView
    private lateinit var readerByline: TextView
    private lateinit var readerBody: TextView
    private lateinit var btnReaderClose: TextView

    @SuppressLint("SetJavaScriptEnabled", "ClickableViewAccessibility")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Dark system bars
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.insetsController?.setSystemBarsAppearance(0,
                WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS or
                WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS)
        }

        setContentView(R.layout.activity_main)

        // Insets handling for bottom bar
        ViewCompat.setOnApplyWindowInsetsListener(findViewById(R.id.bottomBar)) { v, insets ->
            val systemBars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            v.setPadding(v.paddingLeft, v.paddingTop, v.paddingRight, systemBars.bottom.coerceAtLeast(0) + 4)
            insets
        }

        bindViews()
        setupWebView()
        setupAddressBar()
        setupHomeDashboard()
        applyHomeCustomizations()

        // Background offline adblocker
        lifecycleScope.launch {
            AdBlocker.init(applicationContext)
        }

        // Open initial tab
        openNewTab()

        // External intent URL
        intent?.data?.toString()?.let { url ->
            if (url.startsWith("http")) navigate(url)
        }
    }

    private fun bindViews() {
        webView = findViewById(R.id.webView)
        pageProgress = findViewById(R.id.pageProgress)
        homeDashboard = findViewById(R.id.homeDashboard)
        homeHeadline = findViewById(R.id.homeHeadline)
        homeSearchShell = findViewById(R.id.homeSearchShell)
        homeSearchBox = findViewById(R.id.homeSearchBox)
        homeSearchBtn = findViewById(R.id.homeSearchBtn)
        homePillsContainer = findViewById(R.id.homePillsContainer)

        bottomBar = findViewById(R.id.bottomBar)
        bottomAddressPill = findViewById(R.id.bottomAddressPill)
        bottomBarLogo = findViewById(R.id.bottomBarLogo)
        bottomAddressBar = findViewById(R.id.bottomAddressBar)
        btnReload = findViewById(R.id.btnReload)
        btnBottomAi = findViewById(R.id.btnBottomAi)
        btnTabCount = findViewById(R.id.btnTabCount)
        tabCountText = findViewById(R.id.tabCountText)
        btnMenu = findViewById(R.id.btnMenu)

        readerOverlay = findViewById(R.id.readerOverlay)
        readerTitle = findViewById(R.id.readerTitle)
        readerByline = findViewById(R.id.readerByline)
        readerBody = findViewById(R.id.readerBody)
        btnReaderClose = findViewById(R.id.btnReaderClose)

        searchOverlay = findViewById(R.id.searchOverlay)
        overlaySearchInput = findViewById(R.id.overlaySearchInput)
        overlaySearchClear = findViewById(R.id.overlaySearchClear)
        overlaySearchCancel = findViewById(R.id.overlaySearchCancel)
        searchSuggestionsRecycler = findViewById(R.id.searchSuggestionsRecycler)

        searchSuggestionAdapter = SearchSuggestionAdapter(
            onItemClick = { sugg ->
                val target = sugg.targetUrl.ifBlank { sugg.title }
                navigateFromInput(target)
                closeSearchOverlay()
            },
            onFillClick = { sugg ->
                overlaySearchInput.setText(sugg.title)
                overlaySearchInput.setSelection(sugg.title.length)
            }
        )
        searchSuggestionsRecycler.layoutManager = LinearLayoutManager(this)
        searchSuggestionsRecycler.adapter = searchSuggestionAdapter

        bottomBarLogo.setOnClickListener { showHome() }
        btnReload.setOnClickListener {
            if (activeTab?.isLoading == true) webView.stopLoading()
            else webView.reload()
        }
        btnBottomAi.setOnClickListener { showAiSheet() }
        btnTabCount.setOnClickListener { showTabsSheet() }
        btnMenu.setOnClickListener { showMenuSheet() }
        btnReaderClose.setOnClickListener { readerOverlay.visibility = View.GONE }
    }

    private fun applyHomeCustomizations() {
        homeHeadline.visibility = if (prefs.homeHeadlineVisible) View.VISIBLE else View.GONE
        homePillsContainer.visibility = if (prefs.homePillsVisible) View.VISIBLE else View.GONE

        when (prefs.homeSearchShape) {
            "square" -> homeSearchShell.setBackgroundResource(R.drawable.bg_surface_raised)
            "rounded" -> homeSearchShell.setBackgroundResource(R.drawable.bg_glass_card)
            else -> homeSearchShell.setBackgroundResource(R.drawable.bg_glass_pill)
        }
    }

    @SuppressLint("SetJavaScriptEnabled", "ClickableViewAccessibility")
    private fun setupWebView() {
        webView.settings.apply {
            javaScriptEnabled = prefs.javascriptEnabled
            domStorageEnabled = true
            databaseEnabled = true
            allowFileAccess = true
            allowContentAccess = true
            setSupportZoom(true)
            builtInZoomControls = true
            displayZoomControls = false
            setSupportMultipleWindows(true)
            mixedContentMode = WebSettings.MIXED_CONTENT_COMPATIBILITY_MODE
            mediaPlaybackRequiresUserGesture = false
            loadWithOverviewMode = true
            useWideViewPort = true
            setUserAgentString(if (prefs.desktopMode) getDesktopUserAgent() else getMobileUserAgent())
            cacheMode = WebSettings.LOAD_DEFAULT
        }

        webView.webViewClient = LumaWebViewClient()
        webView.webChromeClient = LumaWebChromeClient()

        webView.setDownloadListener { url, userAgent, contentDisposition, mimetype, _ ->
            try {
                val request = DownloadManager.Request(Uri.parse(url)).apply {
                    setMimeType(mimetype)
                    addRequestHeader("User-Agent", userAgent)
                    setDescription("Загрузка через Luma")
                    setTitle(URLUtil.guessFileName(url, contentDisposition, mimetype))
                    setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED)
                    setDestinationInExternalPublicDir(
                        Environment.DIRECTORY_DOWNLOADS,
                        URLUtil.guessFileName(url, contentDisposition, mimetype)
                    )
                }
                (getSystemService(Context.DOWNLOAD_SERVICE) as DownloadManager).enqueue(request)
                Toast.makeText(this, "Загрузка началась", Toast.LENGTH_SHORT).show()
            } catch (_: Exception) {
                startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)))
            }
        }

        // Swipe navigation
        webView.setOnTouchListener { _, event ->
            when (event.action) {
                MotionEvent.ACTION_DOWN -> touchStartX = event.x
                MotionEvent.ACTION_UP -> {
                    val dx = event.x - touchStartX
                    if (dx > 140 && webView.canGoBack()) {
                        webView.goBack()
                        return@setOnTouchListener true
                    }
                    if (dx < -140 && webView.canGoForward()) {
                        webView.goForward()
                        return@setOnTouchListener true
                    }
                }
            }
            false
        }
    }

    private inner class LumaWebViewClient : WebViewClient() {
        override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
            val url = request.url.toString()
            if (url.startsWith("luma://")) {
                handleInternalScheme(url)
                return true
            }
            if (url.startsWith("tel:") || url.startsWith("mailto:")) {
                startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)))
                return true
            }
            return false
        }

        override fun shouldInterceptRequest(view: WebView, request: WebResourceRequest): WebResourceResponse? {
            if (prefs.adBlockEnabled) {
                val host = request.url.host ?: return null
                if (AdBlocker.isAd(host)) {
                    return WebResourceResponse("text/plain", "utf-8", null)
                }
            }
            return null
        }

        override fun onPageStarted(view: WebView, url: String, favicon: Bitmap?) {
            activeTab?.let { tab ->
                tab.url = url
                tab.isLoading = true
                tab.isHome = false
            }
            bottomAddressBar.setText(sanitizeUrl(url))
            pageProgress.visibility = View.VISIBLE
            btnReload.visibility = View.VISIBLE
            btnReload.setImageResource(R.drawable.ic_close)
        }

        override fun onPageFinished(view: WebView, url: String) {
            activeTab?.let { tab ->
                tab.url = url
                tab.title = view.title ?: url
                tab.isLoading = false
                tab.canGoBack = view.canGoBack()
                tab.canGoForward = view.canGoForward()
            }
            pageProgress.visibility = View.INVISIBLE
            btnReload.visibility = View.VISIBLE
            btnReload.setImageResource(R.drawable.ic_reload)
            bottomAddressBar.setText(sanitizeUrl(url))

            val title = view.title ?: url
            if (url.isNotBlank() && !url.startsWith("about:") && !url.startsWith("file:///android_asset")) {
                addToHistory(url, title)
            }
        }

        override fun onReceivedError(view: WebView, request: WebResourceRequest, error: WebResourceError) {
            if (request.isForMainFrame) {
                activeTab?.isLoading = false
                pageProgress.visibility = View.INVISIBLE
                btnReload.setImageResource(R.drawable.ic_reload)
            }
        }
    }

    private inner class LumaWebChromeClient : WebChromeClient() {
        override fun onProgressChanged(view: WebView, newProgress: Int) {
            pageProgress.progress = newProgress
            if (newProgress == 100) pageProgress.visibility = View.INVISIBLE
        }

        override fun onReceivedTitle(view: WebView, title: String) {
            activeTab?.title = title
        }

        override fun onReceivedIcon(view: WebView, icon: Bitmap) {
            activeTab?.favicon = icon
        }
    }

    private fun handleInternalScheme(url: String) {
        when {
            url.startsWith("luma://home") -> showHome()
            url.startsWith("luma://settings") -> showSettingsSheet()
            url.startsWith("luma://assistant") -> showAiSheet()
            url.startsWith("luma://support") -> showSupportSheet()
            url.startsWith("luma://history") -> showHistorySheet()
            url.startsWith("luma://bookmarks") -> showBookmarksSheet()
            else -> showHome()
        }
    }

    private fun setupAddressBar() {
        bottomAddressPill.setOnClickListener { openSearchOverlay() }
        bottomAddressBar.setOnClickListener { openSearchOverlay() }

        overlaySearchCancel.setOnClickListener { closeSearchOverlay() }
        overlaySearchClear.setOnClickListener {
            overlaySearchInput.text?.clear()
            loadInitialSuggestions()
        }

        overlaySearchInput.setOnEditorActionListener { _, actionId, event ->
            if (actionId == EditorInfo.IME_ACTION_GO || event?.keyCode == KeyEvent.KEYCODE_ENTER) {
                val text = overlaySearchInput.text.toString().trim()
                if (text.isNotBlank()) {
                    navigateFromInput(text)
                    closeSearchOverlay()
                }
                true
            } else false
        }

        overlaySearchInput.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {
                val query = s?.toString()?.trim().orEmpty()
                overlaySearchClear.visibility = if (query.isNotEmpty()) View.VISIBLE else View.GONE
                searchDebounceJob?.cancel()
                if (query.isEmpty()) {
                    loadInitialSuggestions()
                } else {
                    searchDebounceJob = lifecycleScope.launch {
                        delay(160)
                        performSearchSuggestions(query)
                    }
                }
            }
            override fun afterTextChanged(s: Editable?) {}
        })
    }

    private fun setupHomeDashboard() {
        homeSearchShell.setOnClickListener { openSearchOverlay() }
        homeSearchBox.setOnClickListener { openSearchOverlay() }
        homeSearchBtn.setOnClickListener { openSearchOverlay() }
        homeSearchBox.isFocusable = false
        homeSearchBox.isClickable = true

        // Quick pills on home screen
        findViewById<View>(R.id.pillHistory).setOnClickListener { showHistorySheet() }
        findViewById<View>(R.id.pillSettings).setOnClickListener { showSettingsSheet() }
        findViewById<View>(R.id.pillBookmarks).setOnClickListener { showBookmarksSheet() }
        findViewById<View>(R.id.pillSupport).setOnClickListener { showSupportSheet() }
    }

    private fun openSearchOverlay() {
        searchOverlay.visibility = View.VISIBLE
        val currentUrl = if (activeTab?.isHome == false) activeTab?.url.orEmpty() else ""
        overlaySearchInput.setText(currentUrl)
        if (currentUrl.isNotEmpty()) {
            overlaySearchInput.selectAll()
        }
        overlaySearchInput.requestFocus()

        val imm = getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager
        overlaySearchInput.postDelayed({
            imm.showSoftInput(overlaySearchInput, InputMethodManager.SHOW_IMPLICIT)
        }, 120)

        if (currentUrl.isBlank()) {
            loadInitialSuggestions()
        } else {
            lifecycleScope.launch {
                performSearchSuggestions(currentUrl)
            }
        }
    }

    private fun closeSearchOverlay() {
        searchOverlay.visibility = View.GONE
        hideKeyboard()
        searchDebounceJob?.cancel()
    }

    private fun loadInitialSuggestions() {
        val list = mutableListOf<SearchSuggestion>()

        // 1. Open tabs
        val otherTabs = tabs.filter { it != activeTab && !it.isHome && it.url.isNotBlank() }
        for (tab in otherTabs.take(3)) {
            list.add(
                SearchSuggestion(
                    title = tab.title.ifBlank { tab.url },
                    subtitle = "Открытая вкладка • ${sanitizeUrl(tab.url)}",
                    targetUrl = tab.url,
                    type = SuggestionType.OPEN_TAB,
                    iconRes = R.drawable.ic_tabs
                )
            )
        }

        // 2. Recent history
        try {
            val arr = JSONArray(prefs.historyJson)
            val historyCount = arr.length().coerceAtMost(6)
            for (i in 0 until historyCount) {
                val obj = arr.getJSONObject(i)
                val url = obj.optString("url", "")
                val title = obj.optString("title", url)
                if (url.isNotBlank()) {
                    list.add(
                        SearchSuggestion(
                            title = title.ifBlank { url },
                            subtitle = sanitizeUrl(url),
                            targetUrl = url,
                            type = SuggestionType.HISTORY,
                            iconRes = R.drawable.ic_history
                        )
                    )
                }
            }
        } catch (_: Exception) {}

        // 3. Quick shortcuts
        if (list.size < 6) {
            val shortcuts = listOf(
                Triple("YouTube", "https://youtube.com", "Видеохостинг"),
                Triple("ВКонтакте", "https://vk.com", "Социальная сеть"),
                Triple("Telegram Web", "https://web.telegram.org", "Мессенджер"),
                Triple("Google", "https://google.com", "Поисковая система"),
                Triple("GitHub", "https://github.com", "Платформа разработки")
            )
            for ((title, url, sub) in shortcuts) {
                if (list.none { it.targetUrl == url }) {
                    list.add(
                        SearchSuggestion(
                            title = title,
                            subtitle = sub,
                            targetUrl = url,
                            type = SuggestionType.BOOKMARK,
                            iconRes = R.drawable.ic_bookmark
                        )
                    )
                }
            }
        }

        searchSuggestionAdapter.submitList(list)
    }

    private suspend fun performSearchSuggestions(query: String) {
        val list = mutableListOf<SearchSuggestion>()

        // 1. Search engine action
        val engineName = if (prefs.searchEngine.contains("google")) "Google" else if (prefs.searchEngine.contains("yandex")) "Яндекс" else "DuckDuckGo"
        list.add(
            SearchSuggestion(
                title = query,
                subtitle = "Искать в $engineName",
                targetUrl = query,
                type = SuggestionType.SEARCH_ENGINE,
                iconRes = R.drawable.ic_search
            )
        )

        // 2. If it's a domain/URL
        val looksLikeUrl = query.contains(".") && !query.contains(" ")
        if (looksLikeUrl) {
            val cleanUrl = if (query.startsWith("http://") || query.startsWith("https://")) query else "https://$query"
            list.add(
                SearchSuggestion(
                    title = query,
                    subtitle = "Перейти по адресу",
                    targetUrl = cleanUrl,
                    type = SuggestionType.SUGGESTION,
                    iconRes = R.drawable.ic_globe
                )
            )
        }

        // 3. Match against local history
        try {
            val arr = JSONArray(prefs.historyJson)
            var matched = 0
            for (i in 0 until arr.length()) {
                if (matched >= 4) break
                val obj = arr.getJSONObject(i)
                val url = obj.optString("url", "")
                val title = obj.optString("title", "")
                if (url.contains(query, ignoreCase = true) || title.contains(query, ignoreCase = true)) {
                    list.add(
                        SearchSuggestion(
                            title = title.ifBlank { url },
                            subtitle = "История • ${sanitizeUrl(url)}",
                            targetUrl = url,
                            type = SuggestionType.HISTORY,
                            iconRes = R.drawable.ic_history
                        )
                    )
                    matched++
                }
            }
        } catch (_: Exception) {}

        // 4. Remote live Google suggestions
        val remoteSuggestions = withContext(Dispatchers.IO) {
            fetchGoogleSuggestions(query)
        }
        for (sugg in remoteSuggestions) {
            if (list.none { it.title.equals(sugg, ignoreCase = true) }) {
                list.add(
                    SearchSuggestion(
                        title = sugg,
                        subtitle = null,
                        targetUrl = sugg,
                        type = SuggestionType.SUGGESTION,
                        iconRes = R.drawable.ic_search
                    )
                )
            }
        }

        withContext(Dispatchers.Main) {
            searchSuggestionAdapter.submitList(list)
        }
    }

    private fun fetchGoogleSuggestions(query: String): List<String> {
        return try {
            val encoded = URLEncoder.encode(query, "UTF-8")
            val url = URL("https://suggestqueries.google.com/complete/search?client=firefox&q=$encoded")
            val conn = url.openConnection() as HttpURLConnection
            conn.connectTimeout = 1500
            conn.readTimeout = 1500
            conn.setRequestProperty("User-Agent", "Mozilla/5.0")
            if (conn.responseCode == 200) {
                val json = conn.inputStream.bufferedReader().use { it.readText() }
                val arr = JSONArray(json)
                if (arr.length() > 1) {
                    val list = mutableListOf<String>()
                    val suggArr = arr.getJSONArray(1)
                    for (i in 0 until suggArr.length().coerceAtMost(8)) {
                        list.add(suggArr.getString(i))
                    }
                    list
                } else emptyList()
            } else emptyList()
        } catch (_: Exception) {
            emptyList()
        }
    }

    fun openNewTab(url: String? = null) {
        val tab = LumaTab(isHome = url == null)
        tabs.add(tab)
        setActiveTab(tab)
        updateTabCount()
        if (url != null) navigate(url)
        else showHome()
    }

    private fun setActiveTab(tab: LumaTab) {
        activeTab = tab
        if (tab.isHome) {
            showHome()
        } else {
            showWebView()
            tab.url.let { if (it.isNotBlank()) webView.loadUrl(it) }
        }
        updateTabCount()
    }

    private fun closeTab(tab: LumaTab) {
        val idx = tabs.indexOf(tab)
        tabs.remove(tab)
        if (tabs.isEmpty()) {
            openNewTab()
        } else if (tab == activeTab) {
            val newIdx = (idx - 1).coerceAtLeast(0).coerceAtMost(tabs.size - 1)
            setActiveTab(tabs[newIdx])
        }
        updateTabCount()
    }

    private fun updateTabCount() {
        tabCountText.text = tabs.size.toString()
    }

    fun navigate(url: String) {
        readerOverlay.visibility = View.GONE
        showWebView()
        webView.loadUrl(url)
        activeTab?.let {
            it.url = url
            it.isHome = false
        }
        bottomAddressBar.setText(sanitizeUrl(url))
        btnReload.visibility = View.VISIBLE
    }

    private fun navigateFromInput(input: String) {
        if (input.isBlank()) return
        if (input.startsWith("luma://")) {
            handleInternalScheme(input)
            return
        }
        val url = buildUrl(input)
        navigate(url)
        homeSearchBox.text?.clear()
    }

    private fun buildUrl(input: String): String {
        if (input.startsWith("http://") || input.startsWith("https://") || input.startsWith("file://")) return input
        if (input.startsWith("www.") || (input.contains(".") && !input.contains(" "))) {
            return "https://$input"
        }
        val engine = prefs.searchEngine
        val encoded = URLEncoder.encode(input, "UTF-8")
        return engine.replace("{q}", encoded)
    }

    private fun sanitizeUrl(url: String): String {
        if (url == "about:blank") return ""
        return url.removePrefix("https://").removePrefix("http://")
    }

    private fun showHome() {
        readerOverlay.visibility = View.GONE
        webView.visibility = View.GONE
        homeDashboard.visibility = View.VISIBLE
        bottomAddressBar.setText("Новая вкладка")
        btnReload.visibility = View.GONE
        activeTab?.isHome = true
    }

    private fun showWebView() {
        readerOverlay.visibility = View.GONE
        homeDashboard.visibility = View.GONE
        webView.visibility = View.VISIBLE
    }

    // ===== TABS BOTTOM SHEET =====
    private fun showTabsSheet() {
        val dialog = BottomSheetDialog(this, R.style.Luma_BottomSheet)
        val view = layoutInflater.inflate(R.layout.sheet_tabs, null)
        dialog.setContentView(view)

        val recycler = view.findViewById<RecyclerView>(R.id.tabsRecycler)
        recycler.layoutManager = LinearLayoutManager(this)

        val adapter = TabsAdapter(tabs, activeTab?.id,
            onTabClick = { tab ->
                setActiveTab(tab)
                dialog.dismiss()
            },
            onTabClose = { tab ->
                closeTab(tab)
                updateTabCount()
            }
        )
        recycler.adapter = adapter

        view.findViewById<TextView>(R.id.btnNewTab).setOnClickListener {
            openNewTab()
            dialog.dismiss()
        }
        view.findViewById<Button>(R.id.btnCloseAll).setOnClickListener {
            tabs.clear()
            openNewTab()
            dialog.dismiss()
        }
        view.findViewById<View>(R.id.btnTabsClose).setOnClickListener { dialog.dismiss() }

        dialog.show()
    }

    // ===== LUMAAI BOTTOM SHEET (Non-blocking, smooth scrolling) =====
    private fun showAiSheet() {
        val dialog = BottomSheetDialog(this, R.style.Luma_BottomSheet)
        val view = layoutInflater.inflate(R.layout.sheet_ai, null)
        dialog.setContentView(view)

        // Expand sheet so it doesn't jitter on soft input
        dialog.behavior.state = BottomSheetBehavior.STATE_EXPANDED
        dialog.behavior.skipCollapsed = true

        val subtitle = view.findViewById<TextView>(R.id.aiTabSubtitle)
        if (activeTab?.isHome == false && !activeTab?.title.isNullOrBlank()) {
            subtitle.text = activeTab?.title
        } else {
            subtitle.text = "Контекст активной вкладки"
        }

        val recycler = view.findViewById<RecyclerView>(R.id.aiMessages)
        val emptyBox = view.findViewById<View>(R.id.aiEmptyBox)

        val layoutManager = LinearLayoutManager(this)
        recycler.layoutManager = layoutManager
        recycler.itemAnimator = null
        aiAdapter = AiChatAdapter(aiMessages)
        recycler.adapter = aiAdapter

        fun updateUiState() {
            emptyBox.visibility = if (aiMessages.isNotEmpty()) View.GONE else View.VISIBLE
        }
        updateUiState()

        val input = view.findViewById<EditText>(R.id.aiInput)
        val sendBtn = view.findViewById<FrameLayout>(R.id.aiSendBtn)
        val btnClose = view.findViewById<FrameLayout>(R.id.btnAiClose)
        val btnNewChat = view.findViewById<FrameLayout>(R.id.btnAiNewChat)
        val modelPill = view.findViewById<TextView>(R.id.aiModelPill)

        modelPill.text = if (prefs.aiModel == "pro") "LumaAI Pro" else "LumaAI быстрый"
        modelPill.setOnClickListener {
            prefs.aiModel = if (prefs.aiModel == "pro") "fast" else "pro"
            modelPill.text = if (prefs.aiModel == "pro") "LumaAI Pro" else "LumaAI быстрый"
            Toast.makeText(this, "Модель: ${modelPill.text}", Toast.LENGTH_SHORT).show()
        }

        btnClose.setOnClickListener { dialog.dismiss() }
        btnNewChat.setOnClickListener {
            aiAdapter?.clear()
            updateUiState()
        }

        fun sendUserQuery(prompt: String) {
            sendAiMessage(prompt, input, recycler, emptyBox)
        }

        // Smart Actions
        view.findViewById<View>(R.id.cardActionSummarize).setOnClickListener {
            sendUserQuery("Кратко выдели главное и сделай структурированную суммаризацию этой страницы")
        }
        view.findViewById<View>(R.id.cardActionExplain).setOnClickListener {
            sendUserQuery("Объясни простыми и понятными словами сложные места этой страницы")
        }
        view.findViewById<View>(R.id.cardActionPlan).setOnClickListener {
            sendUserQuery("Преврати содержание этой страницы в пошаговый план действий и список задач")
        }
        view.findViewById<View>(R.id.cardActionImprove).setOnClickListener {
            sendUserQuery("Улучши, структурируй и перепиши яснее данный текст")
        }

        // Quick chips
        view.findViewById<View>(R.id.chipExample).setOnClickListener {
            sendUserQuery("Приведи наглядный пример к теме")
        }
        view.findViewById<View>(R.id.chipDetails).setOnClickListener {
            sendUserQuery("Объясни подробнее с ключевыми деталями")
        }
        view.findViewById<View>(R.id.chipCompare).setOnClickListener {
            sendUserQuery("Сравни основные варианты и альтернативы")
        }
        view.findViewById<View>(R.id.chipSoundcloud).setOnClickListener {
            dialog.dismiss()
            navigate("https://soundcloud.com")
        }

        sendBtn.setOnClickListener {
            val text = input.text.toString().trim()
            if (text.isNotBlank()) sendUserQuery(text)
        }

        input.setOnEditorActionListener { _, actionId, _ ->
            if (actionId == EditorInfo.IME_ACTION_SEND) {
                val text = input.text.toString().trim()
                if (text.isNotBlank()) sendUserQuery(text)
                true
            } else false
        }

        dialog.show()
    }

    private fun sendAiMessage(text: String, input: EditText, recycler: RecyclerView, emptyBox: View) {
        if (aiStreaming) return
        input.text?.clear()
        hideKeyboard()
        emptyBox.visibility = View.GONE

        val userMsg = AiMessage("user", text)
        aiAdapter?.addMessage(userMsg)
        recycler.smoothScrollToPosition(aiMessages.size - 1)

        val assistantMsg = AiMessage("assistant", "")
        aiAdapter?.addMessage(assistantMsg)
        recycler.smoothScrollToPosition(aiMessages.size - 1)

        aiStreaming = true

        lifecycleScope.launch {
            val pageContext = if (activeTab?.isHome == false) {
                PageTools.extractPageContext(webView)
            } else null

            val systemPrompt = buildSystemPrompt(pageContext)

            aiService.streamChat(
                messages = aiMessages.dropLast(1),
                systemPrompt = systemPrompt,
                model = prefs.aiModel,
                onChunk = { chunk ->
                    withContext(Dispatchers.Main) {
                        aiAdapter?.appendToLastAssistant(chunk)
                    }
                },
                onDone = {
                    withContext(Dispatchers.Main) {
                        aiStreaming = false
                        recycler.scrollToPosition(aiMessages.size - 1)
                    }
                },
                onError = { err ->
                    withContext(Dispatchers.Main) {
                        aiStreaming = false
                        aiAdapter?.appendToLastAssistant("\n\n[Ошибка: $err]")
                        recycler.scrollToPosition(aiMessages.size - 1)
                    }
                }
            )
        }
    }

    private fun buildSystemPrompt(context: com.luma.browser.browser.PageContext?): String = buildString {
        appendLine("Ты — LumaAI, интеллектуальный ассистент браузера Luma.")
        appendLine("Никогда не упоминай сторонние компании или поставщиков модели. Отвечай по-русски, грамотно, емко и структурировано.")
        if (context != null) {
            if (context.title.isNotBlank()) appendLine("\nЗаголовок активной вкладки: ${context.title}")
            if (context.url.isNotBlank()) appendLine("URL активной вкладки: ${context.url}")
            if (context.selection.isNotBlank()) appendLine("\nВыделенный пользователем текст:\n${context.selection}")
            if (context.text.isNotBlank()) appendLine("\nСодержимое страницы:\n${context.text}")
        }
    }

    // ===== MENU SHEET =====
    private fun showMenuSheet() {
        val dialog = BottomSheetDialog(this, R.style.Luma_BottomSheet)
        val view = layoutInflater.inflate(R.layout.sheet_menu, null)
        dialog.setContentView(view)

        view.findViewById<TextView>(R.id.menuPageTitle).text = activeTab?.title ?: "Новая вкладка"
        view.findViewById<TextView>(R.id.menuPageUrl).text = sanitizeUrl(activeTab?.url ?: "")

        val currentUrl = activeTab?.url ?: ""
        val isHttps = currentUrl.startsWith("https://")
        view.findViewById<ImageView>(R.id.menuSecurity).setImageResource(
            if (isHttps) R.drawable.ic_lock else R.drawable.ic_globe
        )

        view.findViewById<LinearLayout>(R.id.menuShare).setOnClickListener {
            if (currentUrl.isNotBlank()) {
                startActivity(Intent.createChooser(Intent(Intent.ACTION_SEND).apply {
                    type = "text/plain"
                    putExtra(Intent.EXTRA_TEXT, currentUrl)
                    putExtra(Intent.EXTRA_SUBJECT, activeTab?.title ?: "")
                }, "Поделиться"))
            }
            dialog.dismiss()
        }

        view.findViewById<LinearLayout>(R.id.menuBookmark).setOnClickListener {
            if (currentUrl.isNotBlank()) {
                addBookmark(currentUrl, activeTab?.title ?: currentUrl)
                Toast.makeText(this, "Закладка сохранена", Toast.LENGTH_SHORT).show()
            }
            dialog.dismiss()
        }

        view.findViewById<LinearLayout>(R.id.menuFind).setOnClickListener {
            dialog.dismiss()
            showFindInPage()
        }

        view.findViewById<LinearLayout>(R.id.menuDownload).setOnClickListener {
            if (currentUrl.isNotBlank()) {
                downloadPage(currentUrl)
            }
            dialog.dismiss()
        }

        view.findViewById<LinearLayout>(R.id.menuReader).setOnClickListener {
            dialog.dismiss()
            toggleReaderMode()
        }

        view.findViewById<LinearLayout>(R.id.menuTranslate).setOnClickListener {
            dialog.dismiss()
            PageTools.injectTranslation(webView, "ru")
            Toast.makeText(this, "Перевод страницы запущен", Toast.LENGTH_SHORT).show()
        }

        view.findViewById<LinearLayout>(R.id.menuDesktop).setOnClickListener {
            prefs.desktopMode = !prefs.desktopMode
            webView.settings.setUserAgentString(
                if (prefs.desktopMode) getDesktopUserAgent() else getMobileUserAgent()
            )
            webView.reload()
            dialog.dismiss()
            Toast.makeText(this, if (prefs.desktopMode) "Режим ПК" else "Мобильный режим", Toast.LENGTH_SHORT).show()
        }

        view.findViewById<LinearLayout>(R.id.menuSettings).setOnClickListener {
            dialog.dismiss()
            showSettingsSheet()
        }

        view.findViewById<LinearLayout>(R.id.menuSupport).setOnClickListener {
            dialog.dismiss()
            showSupportSheet()
        }

        dialog.show()
    }

    // ===== READER MODE =====
    private fun toggleReaderMode() {
        if (activeTab?.isHome == true) {
            Toast.makeText(this, "Режим чтения недоступен на главной вкладке", Toast.LENGTH_SHORT).show()
            return
        }
        lifecycleScope.launch {
            val article = PageTools.extractReaderContent(webView)
            if (article.text.isNotBlank()) {
                readerTitle.text = article.title
                readerByline.text = article.byline
                readerBody.text = article.text
                readerOverlay.visibility = View.VISIBLE
            } else {
                Toast.makeText(this@MainActivity, "Не удалось извлечь статью со страницы", Toast.LENGTH_SHORT).show()
            }
        }
    }

    // ===== SETTINGS SHEET =====
    private fun showSettingsSheet() {
        val dialog = BottomSheetDialog(this, R.style.Luma_BottomSheet)
        val view = layoutInflater.inflate(R.layout.sheet_settings, null)
        dialog.setContentView(view)

        view.findViewById<View>(R.id.btnSettingsClose).setOnClickListener { dialog.dismiss() }

        val btnGoogle = view.findViewById<TextView>(R.id.btnSearchGoogle)
        val btnYandex = view.findViewById<TextView>(R.id.btnSearchYandex)
        val btnDuck = view.findViewById<TextView>(R.id.btnSearchDuck)
        val btnBing = view.findViewById<TextView>(R.id.btnSearchBing)
        val engineBtns = mapOf("Google" to btnGoogle, "Яндекс" to btnYandex, "DuckDuckGo" to btnDuck, "Bing" to btnBing)

        fun updateEngineSelection(name: String) {
            engineBtns.forEach { (eng, btn) ->
                if (eng == name) {
                    btn.setBackgroundResource(R.drawable.bg_surface_raised)
                    btn.setTextColor(0xFFFFFFFF.toInt())
                } else {
                    btn.background = null
                    btn.setTextColor(0xFF716C82.toInt())
                }
            }
        }
        updateEngineSelection(prefs.searchEngineName)

        btnGoogle.setOnClickListener {
            prefs.searchEngine = "https://www.google.com/search?q={q}"
            prefs.searchEngineName = "Google"
            updateEngineSelection("Google")
        }
        btnYandex.setOnClickListener {
            prefs.searchEngine = "https://yandex.ru/search/?text={q}"
            prefs.searchEngineName = "Яндекс"
            updateEngineSelection("Яндекс")
        }
        btnDuck.setOnClickListener {
            prefs.searchEngine = "https://duckduckgo.com/?q={q}"
            prefs.searchEngineName = "DuckDuckGo"
            updateEngineSelection("DuckDuckGo")
        }
        btnBing.setOnClickListener {
            prefs.searchEngine = "https://www.bing.com/search?q={q}"
            prefs.searchEngineName = "Bing"
            updateEngineSelection("Bing")
        }

        // Shape buttons
        val shapePill = view.findViewById<TextView>(R.id.btnShapePill)
        val shapeRounded = view.findViewById<TextView>(R.id.btnShapeRounded)
        val shapeSquare = view.findViewById<TextView>(R.id.btnShapeSquare)
        val shapeBtns = mapOf("pill" to shapePill, "rounded" to shapeRounded, "square" to shapeSquare)

        fun updateShapeSelection(shape: String) {
            shapeBtns.forEach { (s, btn) ->
                if (s == shape) {
                    btn.setBackgroundResource(R.drawable.bg_surface_raised)
                    btn.setTextColor(0xFFFFFFFF.toInt())
                } else {
                    btn.background = null
                    btn.setTextColor(0xFF716C82.toInt())
                }
            }
        }
        updateShapeSelection(prefs.homeSearchShape)

        shapePill.setOnClickListener {
            prefs.homeSearchShape = "pill"
            updateShapeSelection("pill")
            applyHomeCustomizations()
        }
        shapeRounded.setOnClickListener {
            prefs.homeSearchShape = "rounded"
            updateShapeSelection("rounded")
            applyHomeCustomizations()
        }
        shapeSquare.setOnClickListener {
            prefs.homeSearchShape = "square"
            updateShapeSelection("square")
            applyHomeCustomizations()
        }

        val switchHeadline = view.findViewById<MaterialSwitch>(R.id.switchHeadline)
        val switchPills = view.findViewById<MaterialSwitch>(R.id.switchPills)
        val switchAdBlock = view.findViewById<MaterialSwitch>(R.id.switchAdBlock)
        val switchTrackers = view.findViewById<MaterialSwitch>(R.id.switchTrackers)

        switchHeadline.isChecked = prefs.homeHeadlineVisible
        switchPills.isChecked = prefs.homePillsVisible
        switchAdBlock.isChecked = prefs.adBlockEnabled
        switchTrackers.isChecked = prefs.trackerBlockEnabled

        switchHeadline.setOnCheckedChangeListener { _, isChecked ->
            prefs.homeHeadlineVisible = isChecked
            applyHomeCustomizations()
        }
        switchPills.setOnCheckedChangeListener { _, isChecked ->
            prefs.homePillsVisible = isChecked
            applyHomeCustomizations()
        }
        switchAdBlock.setOnCheckedChangeListener { _, isChecked ->
            prefs.adBlockEnabled = isChecked
        }
        switchTrackers.setOnCheckedChangeListener { _, isChecked ->
            prefs.trackerBlockEnabled = isChecked
        }

        view.findViewById<Button>(R.id.btnClearData).setOnClickListener {
            prefs.historyJson = "[]"
            webView.clearCache(true)
            webView.clearHistory()
            Toast.makeText(this, "История и кэш очищены", Toast.LENGTH_SHORT).show()
        }

        val btnModelFast = view.findViewById<TextView>(R.id.btnModelFast)
        val btnModelPro = view.findViewById<TextView>(R.id.btnModelPro)
        fun updateModelBtns(m: String) {
            if (m == "pro") {
                btnModelPro.setBackgroundResource(R.drawable.bg_surface_raised)
                btnModelPro.setTextColor(0xFFFFFFFF.toInt())
                btnModelFast.background = null
                btnModelFast.setTextColor(0xFF716C82.toInt())
            } else {
                btnModelFast.setBackgroundResource(R.drawable.bg_surface_raised)
                btnModelFast.setTextColor(0xFFFFFFFF.toInt())
                btnModelPro.background = null
                btnModelPro.setTextColor(0xFF716C82.toInt())
            }
        }
        updateModelBtns(prefs.aiModel)

        btnModelFast.setOnClickListener {
            prefs.aiModel = "fast"
            updateModelBtns("fast")
        }
        btnModelPro.setOnClickListener {
            prefs.aiModel = "pro"
            updateModelBtns("pro")
        }

        dialog.show()
    }

    // ===== HISTORY SHEET =====
    private fun showHistorySheet() {
        val dialog = BottomSheetDialog(this, R.style.Luma_BottomSheet)
        val view = layoutInflater.inflate(R.layout.sheet_history, null)
        dialog.setContentView(view)

        val recycler = view.findViewById<RecyclerView>(R.id.historyRecycler)
        val emptyText = view.findViewById<TextView>(R.id.historyEmpty)
        val searchInput = view.findViewById<EditText>(R.id.historySearchInput)
        val btnClear = view.findViewById<TextView>(R.id.btnHistoryClear)
        val btnClose = view.findViewById<View>(R.id.btnHistoryClose)

        recycler.layoutManager = LinearLayoutManager(this)

        val items = mutableListOf<JSONObject>()
        try {
            val arr = JSONArray(prefs.historyJson)
            for (i in 0 until arr.length()) items.add(arr.getJSONObject(i))
        } catch (_: Exception) {}

        if (items.isEmpty()) emptyText.visibility = View.VISIBLE

        val adapter = HistoryAdapter(items,
            onItemClick = { url ->
                dialog.dismiss()
                navigate(url)
            },
            onItemDelete = { obj ->
                try {
                    val arr = JSONArray(prefs.historyJson)
                    val newArr = JSONArray()
                    for (i in 0 until arr.length()) {
                        val item = arr.getJSONObject(i)
                        if (item.optString("url") != obj.optString("url")) {
                            newArr.put(item)
                        }
                    }
                    prefs.historyJson = newArr.toString()
                } catch (_: Exception) {}
            }
        )
        recycler.adapter = adapter

        searchInput.addTextChangedListener(object : android.text.TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {
                adapter.filter(s?.toString() ?: "")
            }
            override fun afterTextChanged(s: android.text.Editable?) {}
        })

        btnClear.setOnClickListener {
            prefs.historyJson = "[]"
            items.clear()
            adapter.filter("")
            emptyText.visibility = View.VISIBLE
            Toast.makeText(this, "История очищена", Toast.LENGTH_SHORT).show()
        }

        btnClose.setOnClickListener { dialog.dismiss() }
        dialog.show()
    }

    // ===== BOOKMARKS SHEET =====
    private fun showBookmarksSheet() {
        val dialog = BottomSheetDialog(this, R.style.Luma_BottomSheet)
        val view = layoutInflater.inflate(R.layout.sheet_bookmarks, null)
        dialog.setContentView(view)

        val recycler = view.findViewById<RecyclerView>(R.id.bookmarkRecycler)
        val emptyText = view.findViewById<TextView>(R.id.bookmarkEmpty)
        val searchInput = view.findViewById<EditText>(R.id.bookmarkSearchInput)
        val btnAddCurrent = view.findViewById<TextView>(R.id.btnBookmarkAddCurrent)
        val btnClose = view.findViewById<View>(R.id.btnBookmarkClose)

        recycler.layoutManager = LinearLayoutManager(this)

        val items = mutableListOf<JSONObject>()
        try {
            val arr = JSONArray(prefs.bookmarksJson)
            for (i in 0 until arr.length()) items.add(arr.getJSONObject(i))
        } catch (_: Exception) {}

        if (items.isEmpty()) emptyText.visibility = View.VISIBLE

        val adapter = BookmarksAdapter(items,
            onItemClick = { url ->
                dialog.dismiss()
                navigate(url)
            },
            onItemDelete = { obj ->
                try {
                    val arr = JSONArray(prefs.bookmarksJson)
                    val newArr = JSONArray()
                    for (i in 0 until arr.length()) {
                        val item = arr.getJSONObject(i)
                        if (item.optString("url") != obj.optString("url")) {
                            newArr.put(item)
                        }
                    }
                    prefs.bookmarksJson = newArr.toString()
                } catch (_: Exception) {}
            }
        )
        recycler.adapter = adapter

        searchInput.addTextChangedListener(object : android.text.TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {
                adapter.filter(s?.toString() ?: "")
            }
            override fun afterTextChanged(s: android.text.Editable?) {}
        })

        btnAddCurrent.setOnClickListener {
            val url = activeTab?.url ?: ""
            if (url.isNotBlank() && activeTab?.isHome == false) {
                addBookmark(url, activeTab?.title ?: url)
                items.add(0, JSONObject().apply { put("url", url); put("title", activeTab?.title ?: url) })
                adapter.filter("")
                emptyText.visibility = View.GONE
                Toast.makeText(this, "Закладка добавлена", Toast.LENGTH_SHORT).show()
            } else {
                Toast.makeText(this, "Откройте страницу, чтобы добавить закладку", Toast.LENGTH_SHORT).show()
            }
        }

        btnClose.setOnClickListener { dialog.dismiss() }
        dialog.show()
    }

    private fun addBookmark(url: String, title: String) {
        try {
            val arr = JSONArray(prefs.bookmarksJson)
            arr.put(JSONObject().apply {
                put("url", url)
                put("title", title)
                put("ts", System.currentTimeMillis())
            })
            prefs.bookmarksJson = arr.toString()
        } catch (_: Exception) {}
    }

    private fun addToHistory(url: String, title: String) {
        try {
            val arr = JSONArray(prefs.historyJson)
            arr.put(0, JSONObject().apply {
                put("url", url)
                put("title", title)
                put("ts", System.currentTimeMillis())
            })
            while (arr.length() > 200) arr.remove(arr.length() - 1)
            prefs.historyJson = arr.toString()
        } catch (_: Exception) {}
    }

    private fun showFindInPage() {
        val inputView = EditText(this)
        inputView.hint = "Найти на странице..."
        androidx.appcompat.app.AlertDialog.Builder(this)
            .setTitle("Поиск на странице")
            .setView(inputView)
            .setPositiveButton("Искать") { _, _ ->
                webView.findAllAsync(inputView.text.toString())
            }
            .setNegativeButton("Закрыть") { _, _ ->
                webView.clearMatches()
            }
            .show()
    }

    private fun downloadPage(url: String) {
        try {
            val request = DownloadManager.Request(Uri.parse(url)).apply {
                setTitle("Загрузка Luma")
                setDescription(url)
                setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED)
                setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, "download")
            }
            (getSystemService(Context.DOWNLOAD_SERVICE) as DownloadManager).enqueue(request)
            Toast.makeText(this, "Загрузка началась", Toast.LENGTH_SHORT).show()
        } catch (_: Exception) {
            startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)))
        }
    }

    private fun processImageUriToBase64(uri: Uri): Pair<Bitmap, String>? {
        return try {
            contentResolver.openInputStream(uri)?.use { stream ->
                val original = BitmapFactory.decodeStream(stream) ?: return null
                val maxDim = 1280
                val width = original.width
                val height = original.height
                val scaled = if (width > maxDim || height > maxDim) {
                    val ratio = width.toFloat() / height.toFloat()
                    val (newW, newH) = if (ratio > 1f) {
                        maxDim to (maxDim / ratio).toInt()
                    } else {
                        (maxDim * ratio).toInt() to maxDim
                    }
                    Bitmap.createScaledBitmap(original, newW, newH, true)
                } else original

                val out = ByteArrayOutputStream()
                scaled.compress(Bitmap.CompressFormat.JPEG, 82, out)
                val bytes = out.toByteArray()
                val b64 = Base64.encodeToString(bytes, Base64.NO_WRAP)
                scaled to b64
            }
        } catch (_: Exception) {
            null
        }
    }

    // ===== REAL-TIME CREATOR SUPPORT CHAT =====
    private fun showSupportSheet() {
        val dialog = BottomSheetDialog(this, R.style.Luma_BottomSheet)
        val view = layoutInflater.inflate(R.layout.sheet_support, null)
        dialog.setContentView(view)

        val recycler = view.findViewById<RecyclerView>(R.id.supportMessages)
        val input = view.findViewById<EditText>(R.id.supportInput)
        val sendBtn = view.findViewById<FrameLayout>(R.id.supportSendBtn)
        val attachBtn = view.findViewById<ImageButton>(R.id.supportAttachBtn)

        val attachmentBar = view.findViewById<LinearLayout>(R.id.supportAttachmentBar)
        val attachmentThumb = view.findViewById<ImageView>(R.id.supportAttachmentThumb)
        val attachmentName = view.findViewById<TextView>(R.id.supportAttachmentName)
        val attachmentSize = view.findViewById<TextView>(R.id.supportAttachmentSize)
        val attachmentRemove = view.findViewById<ImageButton>(R.id.supportAttachmentRemove)

        var currentImageBase64: String? = null

        recycler.layoutManager = LinearLayoutManager(this).apply { stackFromEnd = true }
        val supportMessages = mutableListOf<SupportChatMessage>()
        val adapter = SupportChatAdapter(supportMessages) { clickedMsg ->
            if (!clickedMsg.imageUrl.isNullOrBlank()) {
                try {
                    startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(clickedMsg.imageUrl)))
                } catch (_: Exception) {}
            }
        }
        recycler.adapter = adapter

        adapter.addMessage(SupportChatMessage(
            isUser = false,
            text = "Привет! Я создатель Luma. Напиши любой вопрос или баг-репорт — отвечу прямо сюда!"
        ))

        attachBtn.setOnClickListener {
            onSupportImagePicked = { uri ->
                val result = processImageUriToBase64(uri)
                if (result != null) {
                    val (bitmap, b64) = result
                    currentImageBase64 = b64
                    attachmentThumb.setImageBitmap(bitmap)
                    attachmentName.text = "Изображение выбрано"
                    attachmentSize.text = "${(b64.length * 3 / 4) / 1024} КБ"
                    attachmentBar.visibility = View.VISIBLE
                } else {
                    Toast.makeText(this, "Не удалось прочитать изображение", Toast.LENGTH_SHORT).show()
                }
            }
            pickSupportImageLauncher.launch("image/*")
        }

        attachmentRemove.setOnClickListener {
            currentImageBase64 = null
            attachmentBar.visibility = View.GONE
        }

        supportPollJob = lifecycleScope.launch {
            while (isActive) {
                val incoming = supportService.pollMessages()
                if (incoming.isNotEmpty()) {
                    withContext(Dispatchers.Main) {
                        for (msg in incoming) {
                            if (supportMessages.none { it.id == msg.id }) {
                                adapter.addMessage(msg)
                                recycler.smoothScrollToPosition(supportMessages.size - 1)
                            }
                        }
                    }
                }
                delay(2500L)
            }
        }

        dialog.setOnDismissListener {
            supportPollJob?.cancel()
            onSupportImagePicked = null
        }

        fun doSend() {
            val text = input.text.toString().trim()
            val imageB64 = currentImageBase64
            if (text.isBlank() && imageB64.isNullOrBlank()) return

            input.text?.clear()
            currentImageBase64 = null
            attachmentBar.visibility = View.GONE

            val userMsg = SupportChatMessage(
                isUser = true,
                text = text,
                imageBase64 = imageB64
            )
            adapter.addMessage(userMsg)
            recycler.smoothScrollToPosition(supportMessages.size - 1)

            lifecycleScope.launch {
                supportService.sendMessage(
                    text = text,
                    imageBase64 = imageB64,
                    imageName = "image.jpg",
                    imageMime = "image/jpeg"
                )
            }
        }

        sendBtn.setOnClickListener { doSend() }
        input.setOnEditorActionListener { _, actionId, _ ->
            if (actionId == EditorInfo.IME_ACTION_SEND) {
                doSend()
                true
            } else false
        }

        dialog.show()
    }

    private fun getMobileUserAgent() =
        "Mozilla/5.0 (Linux; Android ${Build.VERSION.RELEASE}; ${Build.MODEL}) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36 Luma/2.1.4"

    private fun getDesktopUserAgent() =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 Luma/2.1.4"

    private fun hideKeyboard() {
        val imm = getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager
        imm.hideSoftInputFromWindow(window.decorView.windowToken, 0)
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        if (searchOverlay.visibility == View.VISIBLE) {
            closeSearchOverlay()
        } else if (readerOverlay.visibility == View.VISIBLE) {
            readerOverlay.visibility = View.GONE
        } else if (webView.visibility == View.VISIBLE && webView.canGoBack()) {
            webView.goBack()
        } else if (webView.visibility == View.VISIBLE) {
            showHome()
        } else {
            @Suppress("DEPRECATION")
            super.onBackPressed()
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        supportPollJob?.cancel()
        webView.destroy()
    }
}
