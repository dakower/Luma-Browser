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
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.View
import android.view.WindowInsetsController
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import android.webkit.*
import android.widget.*
import androidx.appcompat.app.AppCompatActivity
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.google.android.material.bottomsheet.BottomSheetDialog
import com.luma.browser.ai.AiMessage
import com.luma.browser.ai.LumaAiService
import com.luma.browser.browser.AdBlocker
import com.luma.browser.storage.LumaPreferences
import com.luma.browser.support.LumaSupportService
import com.luma.browser.support.SupportChatMessage
import com.luma.browser.tabs.LumaTab
import com.luma.browser.ui.AiChatAdapter
import com.luma.browser.ui.SupportChatAdapter
import com.luma.browser.ui.TabsAdapter
import kotlinx.coroutines.*
import org.json.JSONArray
import org.json.JSONObject
import java.net.URLEncoder
import java.util.*

/**
 * MainActivity — Luma Browser for Android.
 * Built 1-in-1 from official Luma design references:
 * - media_1790866441976.png: Home tab with centered Luma brand, capsule search, pills, floating LumaAI button
 * - media_1790866502361.png: Desktop LumaAI panel ported directly to mobile bottom sheet
 * - media_1790866469852.png: Official Luma squircle gradient logo applied everywhere
 */
class MainActivity : AppCompatActivity() {

    // ====== TABS ======
    private val tabs = mutableListOf<LumaTab>()
    private var activeTab: LumaTab? = null

    // ====== SERVICES ======
    private val aiService = LumaAiService()
    private val supportService = LumaSupportService()
    private val prefs by lazy { LumaPreferences.get() }

    // ====== AI CHAT STATE ======
    private val aiMessages = mutableListOf<AiMessage>()
    private var aiAdapter: AiChatAdapter? = null
    private var aiStreaming = false

    // ====== JOBS ======
    private var supportPollJob: Job? = null

    // ====== GESTURE SWIPE ======
    private var touchStartX = 0f

    // ====== VIEW REFS ======
    private lateinit var topBar: LinearLayout
    private lateinit var topBarLogo: ImageView
    private lateinit var webView: WebView
    private lateinit var homeDashboard: ScrollView
    private lateinit var addressBar: EditText
    private lateinit var btnReload: ImageButton
    private lateinit var btnMenu: ImageButton
    private lateinit var btnTabCount: FrameLayout
    private lateinit var tabCountText: TextView
    private lateinit var pageProgress: ProgressBar
    private lateinit var homeSearchBox: EditText
    private lateinit var homeSearchBtn: FrameLayout
    private lateinit var btnFloatingAi: LinearLayout

    @SuppressLint("SetJavaScriptEnabled", "ClickableViewAccessibility")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Dark status & navigation bars
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.insetsController?.setSystemBarsAppearance(0,
                WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS or
                WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS)
        }

        setContentView(R.layout.activity_main)

        // Window insets
        ViewCompat.setOnApplyWindowInsetsListener(findViewById(R.id.topBar)) { v, insets ->
            val systemBars = insets.getInsets(WindowInsetsCompat.Type.statusBars())
            v.setPadding(v.paddingLeft, systemBars.top + 4, v.paddingRight, v.paddingBottom)
            insets
        }

        bindViews()
        setupWebView()
        setupAddressBar()
        setupHomeDashboard()

        // Background AdBlocker init
        lifecycleScope.launch {
            AdBlocker.init(applicationContext)
        }

        // Open initial tab
        openNewTab()

        // Handle external intent URL
        intent?.data?.toString()?.let { url ->
            if (url.startsWith("http")) navigate(url)
        }
    }

    private fun bindViews() {
        topBar = findViewById(R.id.topBar)
        topBarLogo = findViewById(R.id.topBarLogo)
        webView = findViewById(R.id.webView)
        homeDashboard = findViewById(R.id.homeDashboard)
        addressBar = findViewById(R.id.addressBar)
        btnReload = findViewById(R.id.btnReload)
        btnMenu = findViewById(R.id.btnMenu)
        btnTabCount = findViewById(R.id.btnTabCount)
        tabCountText = findViewById(R.id.tabCountText)
        pageProgress = findViewById(R.id.pageProgress)
        homeSearchBox = findViewById(R.id.homeSearchBox)
        homeSearchBtn = findViewById(R.id.homeSearchBtn)
        btnFloatingAi = findViewById(R.id.btnFloatingAi)

        topBarLogo.setOnClickListener { showHome() }
        btnReload.setOnClickListener {
            if (activeTab?.isLoading == true) webView.stopLoading()
            else webView.reload()
        }
        btnMenu.setOnClickListener { showMenuSheet() }
        btnTabCount.setOnClickListener { showTabsSheet() }
        btnFloatingAi.setOnClickListener { showAiSheet() }
    }

    @SuppressLint("SetJavaScriptEnabled", "ClickableViewAccessibility")
    private fun setupWebView() {
        webView.settings.apply {
            javaScriptEnabled = true
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
            setUserAgentString(getMobileUserAgent())
            cacheMode = WebSettings.LOAD_DEFAULT
        }

        webView.webViewClient = LumaWebViewClient()
        webView.webChromeClient = LumaWebChromeClient()

        // Download manager integration
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

        // Swipe back / forward gestures
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
            addressBar.setText(sanitizeUrl(url))
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
            addressBar.setText(sanitizeUrl(url))

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
            url.startsWith("luma://settings") -> navigate("file:///android_asset/settings.html")
            url.startsWith("luma://search") -> navigate("file:///android_asset/search.html")
            url.startsWith("luma://assistant") -> showAiSheet()
            url.startsWith("luma://welcome") -> navigate("file:///android_asset/welcome.html")
            url.startsWith("luma://support") -> showSupportSheet()
            url.startsWith("luma://history") -> showHistorySheet()
            url.startsWith("luma://bookmarks") -> showBookmarks()
            else -> showHome()
        }
    }

    private fun setupAddressBar() {
        addressBar.setOnEditorActionListener { _, actionId, event ->
            if (actionId == EditorInfo.IME_ACTION_GO || event?.keyCode == KeyEvent.KEYCODE_ENTER) {
                navigateFromInput(addressBar.text.toString().trim())
                hideKeyboard()
                true
            } else false
        }

        addressBar.setOnFocusChangeListener { _, hasFocus ->
            if (hasFocus) {
                addressBar.selectAll()
            } else {
                if (activeTab?.isHome == true) {
                    addressBar.setText("Новая вкладка")
                } else {
                    activeTab?.url?.let { addressBar.setText(sanitizeUrl(it)) }
                }
            }
        }
    }

    private fun setupHomeDashboard() {
        homeSearchBox.setOnEditorActionListener { _, actionId, event ->
            if (actionId == EditorInfo.IME_ACTION_GO || event?.keyCode == KeyEvent.KEYCODE_ENTER) {
                navigateFromInput(homeSearchBox.text.toString().trim())
                hideKeyboard()
                true
            } else false
        }
        homeSearchBtn.setOnClickListener {
            navigateFromInput(homeSearchBox.text.toString().trim())
            hideKeyboard()
        }

        // Quick action pills from mockup
        findViewById<View>(R.id.pillLumaAI).setOnClickListener { showAiSheet() }
        findViewById<View>(R.id.pillHistory).setOnClickListener { showHistorySheet() }
        findViewById<View>(R.id.pillSettings).setOnClickListener {
            navigate("file:///android_asset/settings.html")
        }
        findViewById<View>(R.id.pillAccount).setOnClickListener {
            navigate("file:///android_asset/settings.html")
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
        tabCountText.text = tabs.size.toString()
    }

    private fun closeTab(tab: LumaTab) {
        val idx = tabs.indexOf(tab)
        tabs.remove(tab)
        if (tabs.isEmpty()) {
            openNewTab()
        } else if (tab == activeTab) {
            val newIdx = (idx - 1).coerceAtLeast(0)
            setActiveTab(tabs[newIdx])
        }
        updateTabCount()
    }

    private fun updateTabCount() {
        tabCountText.text = tabs.size.toString()
    }

    fun navigate(url: String) {
        showWebView()
        webView.loadUrl(url)
        activeTab?.let {
            it.url = url
            it.isHome = false
        }
        addressBar.setText(sanitizeUrl(url))
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
        if (url.startsWith("file:///android_asset/")) {
            val page = url.removePrefix("file:///android_asset/").removeSuffix(".html")
            return "luma://$page"
        }
        return url.removePrefix("https://").removePrefix("http://")
    }

    private fun showHome() {
        webView.visibility = View.GONE
        homeDashboard.visibility = View.VISIBLE
        btnFloatingAi.visibility = View.VISIBLE
        addressBar.setText("Новая вкладка")
        btnReload.visibility = View.GONE
        activeTab?.isHome = true
    }

    private fun showWebView() {
        homeDashboard.visibility = View.GONE
        btnFloatingAi.visibility = View.GONE
        webView.visibility = View.VISIBLE
    }

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
                if (tabs.isEmpty()) dialog.dismiss()
            }
        )
        recycler.adapter = adapter

        view.findViewById<Button>(R.id.btnNewTab).setOnClickListener {
            openNewTab()
            dialog.dismiss()
        }
        view.findViewById<Button>(R.id.btnCloseAll).setOnClickListener {
            tabs.clear()
            openNewTab()
            dialog.dismiss()
        }

        dialog.show()
    }

    // ===== LUMAAI BOTTOM SHEET (replicates media_1790866502361.png) =====
    private fun showAiSheet() {
        val dialog = BottomSheetDialog(this, R.style.Luma_BottomSheet)
        val view = layoutInflater.inflate(R.layout.sheet_ai, null)
        dialog.setContentView(view)

        val subtitle = view.findViewById<TextView>(R.id.aiTabSubtitle)
        if (activeTab?.isHome == false && !activeTab?.title.isNullOrBlank()) {
            subtitle.text = activeTab?.title
        } else {
            subtitle.text = "Нет активной вкладки"
        }

        val recycler = view.findViewById<RecyclerView>(R.id.aiMessages)
        val emptyBox = view.findViewById<View>(R.id.aiEmptyBox)
        val actionsSection = view.findViewById<View>(R.id.aiActionsSection)
        val recentSection = view.findViewById<View>(R.id.aiRecentSection)

        recycler.layoutManager = LinearLayoutManager(this)
        aiAdapter = AiChatAdapter(aiMessages)
        recycler.adapter = aiAdapter

        fun updateChatVisibility() {
            if (aiMessages.isNotEmpty()) {
                recycler.visibility = View.VISIBLE
                emptyBox.visibility = View.GONE
                actionsSection.visibility = View.GONE
                recentSection.visibility = View.GONE
            } else {
                recycler.visibility = View.GONE
                emptyBox.visibility = View.VISIBLE
                actionsSection.visibility = View.VISIBLE
                recentSection.visibility = View.VISIBLE
            }
        }
        updateChatVisibility()

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
            aiMessages.clear()
            aiAdapter?.clear()
            updateChatVisibility()
        }

        fun sendUserQuery(prompt: String) {
            updateChatVisibility()
            sendAiMessage(prompt, input, recycler)
        }

        // Smart Action Cards (from mockup)
        val currentUrl = activeTab?.url ?: ""
        view.findViewById<View>(R.id.cardActionSummarize).setOnClickListener {
            sendUserQuery("Кратко выдели главное и сделай суммаризацию: $currentUrl")
        }
        view.findViewById<View>(R.id.cardActionExplain).setOnClickListener {
            sendUserQuery("Объясни простыми словами сложные места: $currentUrl")
        }
        view.findViewById<View>(R.id.cardActionPlan).setOnClickListener {
            sendUserQuery("Преврати эту страницу в план действий и задач: $currentUrl")
        }
        view.findViewById<View>(R.id.cardActionImprove).setOnClickListener {
            sendUserQuery("Перепиши яснее и лучше: $currentUrl")
        }

        // Chips suggestions (from mockup)
        view.findViewById<View>(R.id.chipExample).setOnClickListener {
            sendUserQuery("Приведи наглядный пример к теме")
        }
        view.findViewById<View>(R.id.chipDetails).setOnClickListener {
            sendUserQuery("Объясни подробнее с деталями")
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

    private fun sendAiMessage(text: String, input: EditText, recycler: RecyclerView) {
        if (aiStreaming) return
        input.text?.clear()
        hideKeyboard()

        val userMsg = AiMessage("user", text)
        aiMessages.add(userMsg)
        aiAdapter?.addMessage(userMsg)
        recycler.smoothScrollToPosition(aiMessages.size - 1)

        val assistantMsg = AiMessage("assistant", "")
        aiMessages.add(assistantMsg)
        aiAdapter?.addMessage(assistantMsg)

        aiStreaming = true
        val systemPrompt = buildSystemPrompt()

        lifecycleScope.launch {
            aiService.streamChat(
                messages = aiMessages.dropLast(1),
                systemPrompt = systemPrompt,
                model = prefs.aiModel,
                onChunk = { chunk ->
                    withContext(Dispatchers.Main) {
                        aiAdapter?.appendToLastAssistant(chunk)
                        recycler.smoothScrollToPosition(aiMessages.size - 1)
                    }
                },
                onDone = {
                    withContext(Dispatchers.Main) { aiStreaming = false }
                },
                onError = { err ->
                    withContext(Dispatchers.Main) {
                        aiStreaming = false
                        aiAdapter?.appendToLastAssistant("\n\n[Ошибка: $err]")
                    }
                }
            )
        }
    }

    private fun buildSystemPrompt(): String {
        val pageTitle = activeTab?.title ?: ""
        val pageUrl = activeTab?.url ?: ""
        return buildString {
            appendLine("Ты — LumaAI, интеллектуальный ассистент браузера Luma.")
            appendLine("Никогда не упоминай сторонние компании или поставщиков модели. Отвечай по-русски, грамотно, емко и структурировано.")
            if (pageTitle.isNotBlank() && !activeTab?.isHome!!) {
                appendLine("\nЗаголовок текущей вкладки: $pageTitle")
                if (pageUrl.isNotBlank() && !pageUrl.startsWith("about:") && !pageUrl.startsWith("file://")) {
                    appendLine("URL текущей вкладки: $pageUrl")
                }
            }
        }
    }

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
            Toast.makeText(this, "Режим чтения включен", Toast.LENGTH_SHORT).show()
            dialog.dismiss()
        }

        view.findViewById<LinearLayout>(R.id.menuDesktop).setOnClickListener {
            prefs.desktopMode = !prefs.desktopMode
            webView.settings.setUserAgentString(
                if (prefs.desktopMode) getDesktopUserAgent() else getMobileUserAgent()
            )
            webView.reload()
            dialog.dismiss()
        }

        view.findViewById<LinearLayout>(R.id.menuSettings).setOnClickListener {
            dialog.dismiss()
            navigate("file:///android_asset/settings.html")
        }

        view.findViewById<LinearLayout>(R.id.menuSupport).setOnClickListener {
            dialog.dismiss()
            showSupportSheet()
        }

        dialog.show()
    }

    private fun addToHistory(url: String, title: String) {
        try {
            val arr = JSONArray(prefs.historyJson)
            arr.put(0, JSONObject().apply {
                put("url", url)
                put("title", title)
                put("ts", System.currentTimeMillis())
            })
            while (arr.length() > 100) arr.remove(arr.length() - 1)
            prefs.historyJson = arr.toString()
        } catch (_: Exception) {}
    }

    private fun showHistorySheet() {
        val dialog = BottomSheetDialog(this, R.style.Luma_BottomSheet)
        val view = layoutInflater.inflate(R.layout.sheet_tabs, null)
        dialog.setContentView(view)

        view.findViewById<TextView>(R.id.tabsRecycler).visibility = View.GONE
        val newTabBtn = view.findViewById<Button>(R.id.btnNewTab)
        newTabBtn.text = "Очистить"
        newTabBtn.setOnClickListener {
            prefs.historyJson = "[]"
            dialog.dismiss()
            Toast.makeText(this, "История очищена", Toast.LENGTH_SHORT).show()
        }

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

    private fun showBookmarks() {
        val dialog = BottomSheetDialog(this, R.style.Luma_BottomSheet)
        val view = layoutInflater.inflate(R.layout.sheet_tabs, null)
        dialog.setContentView(view)
        dialog.show()
    }

    private fun showFindInPage() {
        val inputView = EditText(this)
        inputView.hint = "Найти на странице..."
        androidx.appcompat.app.AlertDialog.Builder(this)
            .setTitle("Поиск")
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

    // ===== REAL-TIME SUPPORT CHAT =====
    private fun showSupportSheet() {
        val dialog = BottomSheetDialog(this, R.style.Luma_BottomSheet)
        val view = layoutInflater.inflate(R.layout.sheet_support, null)
        dialog.setContentView(view)

        val recycler = view.findViewById<RecyclerView>(R.id.supportMessages)
        val input = view.findViewById<EditText>(R.id.supportInput)
        val sendBtn = view.findViewById<FrameLayout>(R.id.supportSendBtn)

        recycler.layoutManager = LinearLayoutManager(this).apply { stackFromEnd = true }
        val supportMessages = mutableListOf<SupportChatMessage>()
        val adapter = SupportChatAdapter(supportMessages)
        recycler.adapter = adapter

        adapter.addMessage(SupportChatMessage(
            isUser = false,
            text = "Привет! Я создатель Luma. Напиши любой вопрос или идею — отвечу прямо сюда!"
        ))

        // Auto-poll messages from Telegram every 2.5 seconds
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
        }

        fun doSend() {
            val text = input.text.toString().trim()
            if (text.isBlank()) return
            input.text?.clear()
            val userMsg = SupportChatMessage(isUser = true, text = text)
            adapter.addMessage(userMsg)
            recycler.smoothScrollToPosition(supportMessages.size - 1)

            lifecycleScope.launch {
                supportService.sendMessage(text)
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
        if (webView.visibility == View.VISIBLE && webView.canGoBack()) {
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
