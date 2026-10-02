package com.luma.browser

import android.annotation.SuppressLint
import android.app.DownloadManager
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Rect
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.os.Handler
import android.os.Looper
import android.view.PixelCopy
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
import android.view.Gravity
import androidx.activity.result.PickVisualMediaRequest
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
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
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

    // ====== SUPPORT IMAGE ATTACHMENT (GALLERY MULTI-PICK) ======
    private var onSupportImagesPicked: ((List<Uri>) -> Unit)? = null
    private val pickSupportImagesLauncher = registerForActivityResult(
        ActivityResultContracts.PickMultipleVisualMedia(10)
    ) { uris: List<Uri> ->
        if (uris.isNotEmpty()) {
            onSupportImagesPicked?.invoke(uris)
        }
    }

    // ====== FULLSCREEN CUSTOM VIEW (HTML5 Video) ======
    private lateinit var customViewContainer: FrameLayout
    private var customView: View? = null
    private var customViewCallback: WebChromeClient.CustomViewCallback? = null

    // ====== FILE CHOOSER FOR <input type="file"> ======
    private var fileChooserCallback: ValueCallback<Array<Uri>>? = null
    private val fileChooserLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (fileChooserCallback == null) return@registerForActivityResult
        val uris = WebChromeClient.FileChooserParams.parseResult(result.resultCode, result.data)
        fileChooserCallback?.onReceiveValue(uris)
        fileChooserCallback = null
    }

    // ====== AI IMAGE ATTACHMENT ======
    private var onAiImagePicked: ((Uri) -> Unit)? = null
    private val pickAiImageLauncher = registerForActivityResult(
        ActivityResultContracts.PickVisualMedia()
    ) { uri: Uri? ->
        if (uri != null) {
            onAiImagePicked?.invoke(uri)
        }
    }

    // ====== AI CHAT STATE ======
    private val aiMessages = mutableListOf<AiMessage>()
    private var aiAdapter: AiChatAdapter? = null
    private var aiStreaming = false

    // ====== JOBS ======
    private var supportPollJob: Job? = null

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
    private lateinit var btnBottomBack: FrameLayout
    private lateinit var bottomAddressPill: LinearLayout
    private lateinit var bottomBarLogo: ImageView
    private lateinit var bottomAddressBar: TextView
    private lateinit var btnReload: ImageButton
    private lateinit var btnBottomAi: FrameLayout
    private lateinit var btnTabCount: FrameLayout
    private lateinit var tabCountText: TextView
    private lateinit var btnMenu: ImageButton

    // Safari-style Search & Navigation Overlay
    private lateinit var searchOverlay: View
    private lateinit var overlaySearchInput: EditText
    private lateinit var overlaySearchClear: ImageButton
    private lateinit var overlaySearchCancel: View
    private lateinit var searchSuggestionsRecycler: RecyclerView
    private lateinit var searchSuggestionAdapter: SearchSuggestionAdapter
    private var searchDebounceJob: Job? = null

    // Safari-style Dynamic Bar Collapse
    private var isBottomBarCollapsed = false
    private var bottomBarExpandJob: Job? = null
    private var collapseAnimator: android.animation.ValueAnimator? = null

    // Safari-style Fullscreen Tab Switcher
    private lateinit var tabSwitcherOverlay: FrameLayout
    private lateinit var tabSwitcherTitle: TextView
    private lateinit var tabSwitcherRecycler: RecyclerView
    private lateinit var tabSwitcherSearchInput: EditText
    private lateinit var tabSwitcherCountInfo: TextView
    private lateinit var btnTabSwitcherNew: FrameLayout
    private lateinit var btnTabSwitcherDone: TextView
    private lateinit var btnTabSwitcherCloseAll: TextView
    private lateinit var safariTabsAdapter: SafariTabsAdapter

    // Reader Mode
    private lateinit var readerOverlay: LinearLayout
    private lateinit var readerTitle: TextView
    private lateinit var readerByline: TextView
    private lateinit var readerBody: TextView
    private lateinit var btnReaderClose: TextView

    // Root Layout
    private lateinit var mainRootLayout: FrameLayout

    // Custom History Tab
    private lateinit var historyDashboard: LinearLayout
    private lateinit var historyTabTitle: TextView
    private lateinit var btnHistoryTabClear: TextView
    private lateinit var historyTabSearchCapsule: LinearLayout
    private lateinit var historyTabSearchInput: EditText
    private lateinit var historyTabEmptyView: View
    private lateinit var historyTabRecycler: RecyclerView
    private lateinit var historyTabAdapter: HistoryAdapter

    // Custom Downloads Tab
    private lateinit var downloadsDashboard: LinearLayout
    private lateinit var downloadsTabTitle: TextView
    private lateinit var btnDownloadsOpenFolder: TextView
    private lateinit var downloadsTabEmptyView: View
    private lateinit var downloadsTabRecycler: RecyclerView
    private lateinit var downloadsTabAdapter: DownloadsAdapter

    // Circle to Search
    private lateinit var circleSearchOverlay: FrameLayout
    private lateinit var circleSearchView: CircleToSearchView
    private lateinit var btnCircleSearchClose: View

    @SuppressLint("SetJavaScriptEnabled", "ClickableViewAccessibility")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        try {
            // Dark system bars
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                try {
                    window.insetsController?.setSystemBarsAppearance(0,
                        WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS or
                        WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS)
                } catch (_: Exception) {}
            }

            setContentView(R.layout.activity_main)

            // Insets handling for bottom bar
            findViewById<View>(R.id.bottomBar)?.let { bBar ->
                ViewCompat.setOnApplyWindowInsetsListener(bBar) { v, insets ->
                    val systemBars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
                    v.setPadding(v.paddingLeft, v.paddingTop, v.paddingRight, systemBars.bottom.coerceAtLeast(0) + 4)
                    insets
                }
            }

            bindViews()
            setupWebView()
            setupAddressBar()
            setupHomeDashboard()
            applyHomeCustomizations()
            applyAppTheme()

            // Background offline adblocker
            lifecycleScope.launch {
                try {
                    AdBlocker.init(applicationContext)
                } catch (_: Exception) {}
            }

            // Open initial tab
            openNewTab()

            // External intent URL
            intent?.data?.toString()?.let { url ->
                if (url.startsWith("http")) navigate(url)
            }
        } catch (t: Throwable) {
            android.util.Log.e("LumaMain", "CRITICAL ERROR in onCreate", t)
            try {
                val crashFile = java.io.File(filesDir, "crash_log.txt")
                crashFile.appendText("CRASH in onCreate: " + t.stackTraceToString() + "\n\n")
            } catch (_: Exception) {}
        }
    }

    override fun onNewIntent(intent: Intent?) {
        super.onNewIntent(intent)
        setIntent(intent)
        intent?.data?.toString()?.let { url ->
            if (url.startsWith("http://") || url.startsWith("https://")) {
                openNewTab(url)
            }
        }
    }

    private fun bindViews() {
        customViewContainer = findViewById(R.id.customViewContainer)
        webView = findViewById(R.id.webView)
        pageProgress = findViewById(R.id.pageProgress)
        homeDashboard = findViewById(R.id.homeDashboard)
        homeHeadline = findViewById(R.id.homeHeadline)
        homeSearchShell = findViewById(R.id.homeSearchShell)
        homeSearchBox = findViewById(R.id.homeSearchBox)
        homeSearchBtn = findViewById(R.id.homeSearchBtn)
        homePillsContainer = findViewById(R.id.homePillsContainer)

        bottomBar = findViewById(R.id.bottomBar)
        btnBottomBack = findViewById(R.id.btnBottomBack)
        bottomAddressPill = findViewById(R.id.bottomAddressPill)
        bottomBarLogo = findViewById(R.id.bottomBarLogo)
        bottomAddressBar = findViewById(R.id.bottomAddressBar)
        btnReload = findViewById(R.id.btnReload)
        btnBottomAi = findViewById(R.id.btnBottomAi)
        btnTabCount = findViewById(R.id.btnTabCount)
        tabCountText = findViewById(R.id.tabCountText)
        btnMenu = findViewById(R.id.btnMenu)

        // Safari-style Fullscreen Tab Switcher Views
        tabSwitcherOverlay = findViewById(R.id.tabSwitcherOverlay)
        tabSwitcherTitle = findViewById(R.id.tabSwitcherTitle)
        tabSwitcherRecycler = findViewById(R.id.tabSwitcherRecycler)
        tabSwitcherSearchInput = findViewById(R.id.tabSwitcherSearchInput)
        tabSwitcherCountInfo = findViewById(R.id.tabSwitcherCountInfo)
        btnTabSwitcherNew = findViewById(R.id.btnTabSwitcherNew)
        btnTabSwitcherDone = findViewById(R.id.btnTabSwitcherDone)
        btnTabSwitcherCloseAll = findViewById(R.id.btnTabSwitcherCloseAll)

        tabSwitcherRecycler.layoutManager = androidx.recyclerview.widget.GridLayoutManager(this, 2)
        safariTabsAdapter = SafariTabsAdapter(
            allTabs = tabs,
            activeTabId = activeTab?.id,
            onTabClick = { tab ->
                setActiveTab(tab)
                closeTabSwitcher()
            },
            onTabClose = { tab ->
                closeTab(tab)
                safariTabsAdapter.activeTabId = activeTab?.id
                safariTabsAdapter.syncTabs()
                updateTabSwitcherCount()
            }
        )
        tabSwitcherRecycler.adapter = safariTabsAdapter

        btnTabSwitcherNew.setOnClickListener {
            openNewTab()
            closeTabSwitcher()
        }
        btnTabSwitcherDone.setOnClickListener {
            closeTabSwitcher()
        }
        btnTabSwitcherCloseAll.setOnClickListener {
            tabs.clear()
            openNewTab()
            closeTabSwitcher()
        }
        tabSwitcherSearchInput.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {
                safariTabsAdapter.filter(s?.toString().orEmpty())
            }
            override fun afterTextChanged(s: Editable?) {}
        })

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
        findViewById<View>(R.id.searchOverlayBackdrop)?.setOnClickListener { closeSearchOverlay() }

        val overlayBottomBar = findViewById<View>(R.id.searchOverlayBottomBar)
        if (overlayBottomBar != null) {
            ViewCompat.setOnApplyWindowInsetsListener(overlayBottomBar) { v, insets ->
                val sysBars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
                v.setPadding(v.paddingLeft, v.paddingTop, v.paddingRight, sysBars.bottom.coerceAtLeast(0) + 8)
                insets
            }
        }

        searchSuggestionAdapter = SearchSuggestionAdapter(
            onItemClick = { sugg ->
                if (sugg.type == SuggestionType.INFO || sugg.targetUrl.isBlank()) return@SearchSuggestionAdapter
                val matchingTab = tabs.find { !it.isHome && it.url == sugg.targetUrl }
                if (matchingTab != null) {
                    setActiveTab(matchingTab)
                    closeSearchOverlay()
                } else {
                    val target = sugg.targetUrl.ifBlank { sugg.title }
                    navigateFromInput(target)
                    closeSearchOverlay()
                }
            },
            onFillClick = { sugg ->
                overlaySearchInput.setText(sugg.title)
                overlaySearchInput.setSelection(sugg.title.length)
            }
        )
        searchSuggestionsRecycler.layoutManager = LinearLayoutManager(this)
        searchSuggestionsRecycler.adapter = searchSuggestionAdapter

        mainRootLayout = findViewById(R.id.mainRootLayout)

        // Custom History Tab
        historyDashboard = findViewById(R.id.historyDashboard)
        historyTabTitle = findViewById(R.id.historyTabTitle)
        btnHistoryTabClear = findViewById(R.id.btnHistoryTabClear)
        historyTabSearchCapsule = findViewById(R.id.historyTabSearchCapsule)
        historyTabSearchInput = findViewById(R.id.historyTabSearchInput)
        historyTabEmptyView = findViewById(R.id.historyTabEmptyView)
        historyTabRecycler = findViewById(R.id.historyTabRecycler)

        historyTabRecycler.layoutManager = LinearLayoutManager(this)
        historyTabAdapter = HistoryAdapter(
            allItems = mutableListOf(),
            isLight = prefs.appTheme == "light",
            onItemClick = { url ->
                navigate(url)
            },
            onItemDelete = { entry ->
                try {
                    val arr = JSONArray(prefs.historyJson)
                    val newArr = JSONArray()
                    for (i in 0 until arr.length()) {
                        val item = arr.getJSONObject(i)
                        if (item.optString("url") != entry.url) {
                            newArr.put(item)
                        }
                    }
                    prefs.historyJson = newArr.toString()
                    historyTabEmptyView.visibility = if (newArr.length() == 0) View.VISIBLE else View.GONE
                } catch (_: Exception) {}
            }
        )
        historyTabRecycler.adapter = historyTabAdapter

        historyTabSearchInput.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {
                historyTabAdapter.filter(s?.toString().orEmpty())
            }
            override fun afterTextChanged(s: Editable?) {}
        })

        btnHistoryTabClear.setOnClickListener {
            prefs.historyJson = "[]"
            historyTabAdapter.updateData(emptyList())
            historyTabEmptyView.visibility = View.VISIBLE
            Toast.makeText(this, "История очищена", Toast.LENGTH_SHORT).show()
        }

        // Custom Downloads Tab
        downloadsDashboard = findViewById(R.id.downloadsDashboard)
        downloadsTabTitle = findViewById(R.id.downloadsTabTitle)
        btnDownloadsOpenFolder = findViewById(R.id.btnDownloadsOpenFolder)
        downloadsTabEmptyView = findViewById(R.id.downloadsTabEmptyView)
        downloadsTabRecycler = findViewById(R.id.downloadsTabRecycler)

        downloadsTabRecycler.layoutManager = LinearLayoutManager(this)
        downloadsTabAdapter = DownloadsAdapter(
            allItems = mutableListOf(),
            isLight = prefs.appTheme == "light",
            onItemClick = { item ->
                try {
                    val uri = Uri.parse(item.uriString)
                    val intent = Intent(Intent.ACTION_VIEW).apply {
                        setDataAndType(uri, item.mediaType.ifBlank { "*/*" })
                        addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                        addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                    }
                    startActivity(intent)
                } catch (_: Exception) {
                    Toast.makeText(this, "Не удалось открыть файл", Toast.LENGTH_SHORT).show()
                }
            },
            onShareClick = { item ->
                try {
                    val uri = Uri.parse(item.uriString)
                    val share = Intent(Intent.ACTION_SEND).apply {
                        type = item.mediaType.ifBlank { "*/*" }
                        putExtra(Intent.EXTRA_STREAM, uri)
                        addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                    }
                    startActivity(Intent.createChooser(share, "Поделиться файлом"))
                } catch (_: Exception) {
                    Toast.makeText(this, "Не удалось поделиться файлом", Toast.LENGTH_SHORT).show()
                }
            },
            onDeleteClick = { item ->
                try {
                    val dm = getSystemService(Context.DOWNLOAD_SERVICE) as? DownloadManager
                    dm?.remove(item.id)
                    loadDownloadsTabData()
                } catch (_: Exception) {}
            }
        )
        downloadsTabRecycler.adapter = downloadsTabAdapter

        btnDownloadsOpenFolder.setOnClickListener {
            try {
                startActivity(Intent(DownloadManager.ACTION_VIEW_DOWNLOADS))
            } catch (_: Exception) {
                Toast.makeText(this, "Не удалось открыть папку загрузок", Toast.LENGTH_SHORT).show()
            }
        }

        btnBottomBack.setOnClickListener {
            if ((::historyDashboard.isInitialized && historyDashboard.visibility == View.VISIBLE) ||
                (::downloadsDashboard.isInitialized && downloadsDashboard.visibility == View.VISIBLE)) {
                showHome()
            } else if (webView.visibility == View.VISIBLE && webView.canGoBack()) {
                webView.goBack()
            } else if (webView.visibility == View.VISIBLE) {
                showHome()
            }
            updateBottomBarState()
        }
        bottomBarLogo.setOnClickListener { showHome() }
        btnReload.setOnClickListener {
            if (activeTab?.isLoading == true) webView.stopLoading()
            else webView.reload()
        }
        btnBottomAi.setOnClickListener {
            if (!prefs.isLoggedIn) {
                showAccountPromptForAi("Для использования LumaAI войдите в аккаунт Luma ID")
            } else {
                showAiSheet()
            }
        }
        btnBottomAi.setOnLongClickListener {
            if (!prefs.isLoggedIn) {
                showAccountPromptForAi("Для использования «Обвести для поиска» войдите в аккаунт Luma ID")
            } else {
                startCircleToSearch()
            }
            true
        }
        btnTabCount.setOnClickListener { openTabSwitcher() }
        btnMenu.setOnClickListener { showMenuSheet() }
        btnReaderClose.setOnClickListener { readerOverlay.visibility = View.GONE }

        // Circle to Search Overlay setup
        circleSearchOverlay = findViewById(R.id.circleSearchOverlay)
        circleSearchView = findViewById(R.id.circleSearchView)
        btnCircleSearchClose = findViewById(R.id.btnCircleSearchClose)

        circleSearchView.onSelectionComplete = { cropped ->
            handleCircleSearchCapture(cropped)
        }
        circleSearchView.onDismiss = {
            dismissCircleToSearch()
        }
        btnCircleSearchClose.setOnClickListener {
            dismissCircleToSearch()
        }

        updateBottomBarState()
    }

    private fun applyHomeCustomizations() {
        homeHeadline.visibility = if (prefs.homeHeadlineVisible) View.VISIBLE else View.GONE
        homePillsContainer.visibility = if (prefs.homePillsVisible) View.VISIBLE else View.GONE

        when (prefs.homeSearchShape) {
            "square" -> homeSearchShell.setBackgroundResource(R.drawable.bg_glass_card)
            "rounded" -> homeSearchShell.setBackgroundResource(R.drawable.bg_glass_card)
            else -> homeSearchShell.setBackgroundResource(R.drawable.bg_glass_search)
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
            downloadFile(url, mimetype, contentDisposition)
        }

        webView.setOnLongClickListener {
            val result = webView.hitTestResult
            val extra = result.extra
            when (result.type) {
                WebView.HitTestResult.SRC_ANCHOR_TYPE,
                WebView.HitTestResult.SRC_IMAGE_ANCHOR_TYPE -> {
                    if (!extra.isNullOrBlank()) {
                        showLinkContextMenu(extra)
                        return@setOnLongClickListener true
                    }
                }
                WebView.HitTestResult.IMAGE_TYPE -> {
                    if (!extra.isNullOrBlank()) {
                        showImageContextMenu(extra)
                        return@setOnLongClickListener true
                    }
                }
            }
            false
        }

        webView.setOnScrollChangeListener { _, _, scrollY, _, oldScrollY ->
            val delta = scrollY - oldScrollY
            if (scrollY <= 10) {
                expandBottomBar()
            } else if (delta > 20 && scrollY > 60) {
                collapseBottomBar()
            } else if (delta < -20) {
                expandBottomBar()
            }
            resetInactivityTimer()
        }

        webView.setOnTouchListener { _, event ->
            if (event.action == MotionEvent.ACTION_DOWN || event.action == MotionEvent.ACTION_MOVE) {
                resetInactivityTimer()
            }
            false
        }
    }

    private fun downloadFile(url: String, mimetype: String = "*/*", contentDisposition: String? = null) {
        if (url.startsWith("blob:") || url.startsWith("data:")) {
            Toast.makeText(this, "Формат URL загрузки не поддерживается напрямую", Toast.LENGTH_SHORT).show()
            return
        }
        try {
            val cookie = CookieManager.getInstance().getCookie(url)
            val filename = URLUtil.guessFileName(url, contentDisposition, mimetype)
            val request = DownloadManager.Request(Uri.parse(url)).apply {
                setMimeType(mimetype)
                addRequestHeader("User-Agent", webView.settings.userAgentString)
                if (!cookie.isNullOrBlank()) {
                    addRequestHeader("Cookie", cookie)
                }
                setDescription("Загрузка через Luma")
                setTitle(filename)
                setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED)
                setDestinationInExternalPublicDir(
                    Environment.DIRECTORY_DOWNLOADS,
                    filename
                )
            }
            (getSystemService(Context.DOWNLOAD_SERVICE) as DownloadManager).enqueue(request)
            Toast.makeText(this, "Загрузка началась", Toast.LENGTH_SHORT).show()
            if (activeTab?.isDownloads == true) {
                lifecycleScope.launch {
                    delay(600)
                    loadDownloadsTabData()
                }
            }
        } catch (_: Exception) {
            try {
                startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)))
            } catch (_: Exception) {
                Toast.makeText(this, "Не удалось начать загрузку", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun showLinkContextMenu(url: String) {
        val options = arrayOf("Открыть в новой вкладке", "Копировать ссылку", "Поделиться ссылкой")
        android.app.AlertDialog.Builder(this)
            .setTitle(url)
            .setItems(options) { _, which ->
                when (which) {
                    0 -> openNewTab(url)
                    1 -> {
                        val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
                        val clip = android.content.ClipData.newPlainText("URL", url)
                        clipboard.setPrimaryClip(clip)
                        Toast.makeText(this, "Ссылка скопирована", Toast.LENGTH_SHORT).show()
                    }
                    2 -> {
                        val shareIntent = Intent(Intent.ACTION_SEND).apply {
                            type = "text/plain"
                            putExtra(Intent.EXTRA_TEXT, url)
                        }
                        startActivity(Intent.createChooser(shareIntent, "Поделиться ссылкой"))
                    }
                }
            }
            .show()
    }

    private fun showImageContextMenu(imageUrl: String) {
        val options = arrayOf("Открыть изображение в новой вкладке", "Копировать ссылку на изображение", "Сохранить изображение")
        android.app.AlertDialog.Builder(this)
            .setTitle("Изображение")
            .setItems(options) { _, which ->
                when (which) {
                    0 -> openNewTab(imageUrl)
                    1 -> {
                        val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
                        val clip = android.content.ClipData.newPlainText("Image URL", imageUrl)
                        clipboard.setPrimaryClip(clip)
                        Toast.makeText(this, "Ссылка скопирована", Toast.LENGTH_SHORT).show()
                    }
                    2 -> downloadFile(imageUrl, "image/*")
                }
            }
            .show()
    }

    private fun hideCustomView() {
        val cv = customView ?: return
        customViewContainer.removeView(cv)
        customViewContainer.visibility = View.GONE
        customView = null
        customViewCallback?.onCustomViewHidden()
        customViewCallback = null
        @Suppress("DEPRECATION")
        window.decorView.systemUiVisibility = View.SYSTEM_UI_FLAG_VISIBLE
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
            updateBottomBarState(url)
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
            updateBottomBarState(url)
            view.postDelayed({
                captureTabSnapshot(activeTab)
            }, 350)

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
            val current = pageProgress.progress
            if (newProgress < 100) {
                if (pageProgress.visibility != View.VISIBLE) {
                    pageProgress.visibility = View.VISIBLE
                    pageProgress.alpha = 1f
                }
                android.animation.ObjectAnimator.ofInt(pageProgress, "progress", current, newProgress).apply {
                    duration = 180
                    interpolator = android.view.animation.DecelerateInterpolator()
                    start()
                }
            } else {
                android.animation.ObjectAnimator.ofInt(pageProgress, "progress", current, 100).apply {
                    duration = 120
                    start()
                }
                pageProgress.animate().alpha(0f).setDuration(260).withEndAction {
                    pageProgress.visibility = View.INVISIBLE
                    pageProgress.alpha = 1f
                    pageProgress.progress = 0
                }.start()
            }
        }

        override fun onReceivedTitle(view: WebView, title: String) {
            activeTab?.title = title
        }

        override fun onReceivedIcon(view: WebView, icon: Bitmap) {
            activeTab?.favicon = icon
        }

        override fun onShowCustomView(view: View?, callback: CustomViewCallback?) {
            if (customView != null) {
                callback?.onCustomViewHidden()
                return
            }
            customView = view
            customViewCallback = callback
            customViewContainer.addView(view)
            customViewContainer.visibility = View.VISIBLE
            @Suppress("DEPRECATION")
            window.decorView.systemUiVisibility = (
                View.SYSTEM_UI_FLAG_FULLSCREEN
                or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                or View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
            )
        }

        override fun onHideCustomView() {
            hideCustomView()
        }

        override fun onCreateWindow(
            view: WebView?,
            isDialog: Boolean,
            isUserGesture: Boolean,
            resultMsg: android.os.Message?
        ): Boolean {
            val href = view?.handler?.obtainMessage()
            view?.requestFocusNodeHref(href)
            val url = href?.data?.getString("url")
            if (!url.isNullOrBlank()) {
                openNewTab(url)
                return true
            }
            val tempWebView = WebView(this@MainActivity)
            tempWebView.webViewClient = object : WebViewClient() {
                override fun shouldOverrideUrlLoading(view: WebView?, request: WebResourceRequest?): Boolean {
                    val targetUrl = request?.url?.toString()
                    if (!targetUrl.isNullOrBlank()) {
                        openNewTab(targetUrl)
                    }
                    return true
                }
            }
            val transport = resultMsg?.obj as? WebView.WebViewTransport
            transport?.webView = tempWebView
            resultMsg?.sendToTarget()
            return true
        }

        override fun onShowFileChooser(
            webView: WebView?,
            filePathCallback: ValueCallback<Array<Uri>>?,
            fileChooserParams: FileChooserParams?
        ): Boolean {
            fileChooserCallback?.onReceiveValue(null)
            fileChooserCallback = filePathCallback
            val intent = fileChooserParams?.createIntent() ?: Intent(Intent.ACTION_GET_CONTENT).apply {
                type = "*/*"
                addCategory(Intent.CATEGORY_OPENABLE)
            }
            try {
                fileChooserLauncher.launch(intent)
            } catch (e: Exception) {
                fileChooserCallback?.onReceiveValue(null)
                fileChooserCallback = null
                return false
            }
            return true
        }
    }

    private fun handleInternalScheme(url: String) {
        when {
            url.startsWith("luma://home") -> showHome()
            url.startsWith("luma://settings") -> showSettingsSheet()
            url.startsWith("luma://assistant") -> showAiSheet()
            url.startsWith("luma://support") -> showSupportSheet()
            url.startsWith("luma://history") -> openHistoryTab()
            url.startsWith("luma://downloads") -> openDownloadsTab()
            url.startsWith("luma://bookmarks") -> showBookmarksSheet()
            else -> showHome()
        }
    }

    private fun setupAddressBar() {
        val pillSwipeDetector = android.view.GestureDetector(this, object : android.view.GestureDetector.SimpleOnGestureListener() {
            override fun onSingleTapConfirmed(e: MotionEvent): Boolean {
                if (isBottomBarCollapsed) {
                    expandBottomBar()
                }
                openSearchOverlay()
                return true
            }

            override fun onFling(e1: MotionEvent?, e2: MotionEvent, velocityX: Float, velocityY: Float): Boolean {
                if (e1 == null) return false
                val dx = e2.x - e1.x
                val dy = e2.y - e1.y
                if (Math.abs(dx) > Math.abs(dy) && Math.abs(dx) > 70 && Math.abs(velocityX) > 280) {
                    if (dx < 0) {
                        // Swipe left -> Next Tab (Safari signature)
                        switchTabRelative(+1)
                        expandBottomBar()
                    } else {
                        // Swipe right -> Previous Tab (Safari signature)
                        switchTabRelative(-1)
                        expandBottomBar()
                    }
                    return true
                }
                return false
            }
        })

        bottomAddressPill.setOnTouchListener { _, event ->
            pillSwipeDetector.onTouchEvent(event)
            true
        }

        overlaySearchCancel.setOnClickListener { closeSearchOverlay() }
        overlaySearchClear.setOnClickListener {
            overlaySearchInput.text?.clear()
            loadInitialSuggestions()
        }

        searchSuggestionsRecycler.addOnScrollListener(object : RecyclerView.OnScrollListener() {
            override fun onScrollStateChanged(recyclerView: RecyclerView, newState: Int) {
                if (newState == RecyclerView.SCROLL_STATE_DRAGGING) {
                    hideKeyboard()
                }
            }
        })

        // Swipe-down to dismiss search overlay (Safari style)
        val searchSwipeDetector = android.view.GestureDetector(this, object : android.view.GestureDetector.SimpleOnGestureListener() {
            override fun onFling(e1: MotionEvent?, e2: MotionEvent, velocityX: Float, velocityY: Float): Boolean {
                if (e1 != null && velocityY > 600 && (e2.y - e1.y) > 50) {
                    if (!searchSuggestionsRecycler.canScrollVertically(-1)) {
                        closeSearchOverlay()
                        return true
                    }
                }
                return false
            }
        })
        searchSuggestionsRecycler.addOnItemTouchListener(object : RecyclerView.SimpleOnItemTouchListener() {
            override fun onInterceptTouchEvent(rv: RecyclerView, e: MotionEvent): Boolean {
                searchSwipeDetector.onTouchEvent(e)
                return false
            }
        })
        searchOverlay.setOnTouchListener { _, event ->
            searchSwipeDetector.onTouchEvent(event)
            false
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

        findViewById<View>(R.id.btnHomeAccount)?.setOnClickListener { showAccountSheet() }
        updateHomeAccountButton()

        // Quick pills on home screen
        findViewById<View>(R.id.pillHistory)?.setOnClickListener { openHistoryTab() }
        findViewById<View>(R.id.pillBookmarks)?.setOnClickListener { showBookmarksSheet() }
        findViewById<View>(R.id.pillDownloads)?.setOnClickListener { openDownloadsTab() }
        findViewById<View>(R.id.pillSettings)?.setOnClickListener { showSettingsSheet() }
        findViewById<View>(R.id.pillSupport)?.setOnClickListener { showSupportSheet() }

        // Home History Section
        findViewById<View>(R.id.btnHomeAllHistory)?.setOnClickListener { openHistoryTab() }
        populateHomeHistory()
    }

    private fun updateHomeAccountButton() {
        val text = findViewById<TextView>(R.id.homeAccountText) ?: return
        if (prefs.isLoggedIn) {
            text.text = prefs.displayName.ifBlank { prefs.email.substringBefore('@') }
        } else {
            text.text = "Войти"
        }
    }

    private fun populateHomeHistory() {
        val container = findViewById<LinearLayout>(R.id.homeHistoryList) ?: return
        val emptyView = findViewById<View>(R.id.homeHistoryEmptyView) ?: return
        val themeMode = prefs.appTheme
        val isLight = when (themeMode) {
            "light" -> true
            "dark" -> false
            else -> {
                val nightMode = resources.configuration.uiMode and android.content.res.Configuration.UI_MODE_NIGHT_MASK
                nightMode != android.content.res.Configuration.UI_MODE_NIGHT_YES
            }
        }

        val historyList = mutableListOf<Pair<String, String>>() // title, url
        // 1. Visited non-home open tabs
        for (t in tabs.filter { !it.isHome && it.url.isNotBlank() }.reversed()) {
            if (historyList.none { it.second == t.url }) {
                val title = t.title.ifBlank { extractDomain(t.url) }
                historyList.add(title to t.url)
            }
            if (historyList.size >= 4) break
        }
        // 2. Visited history from prefs
        if (historyList.size < 4) {
            try {
                val arr = JSONArray(prefs.historyJson)
                for (i in 0 until arr.length()) {
                    val obj = arr.getJSONObject(i)
                    val u = obj.optString("url")
                    val t = obj.optString("title")
                    if (u.isNotBlank() && historyList.none { it.second == u }) {
                        val title = t.ifBlank { extractDomain(u) }
                        historyList.add(title to u)
                    }
                    if (historyList.size >= 4) break
                }
            } catch (_: Exception) {}
        }

        container.removeAllViews()

        if (historyList.isEmpty()) {
            emptyView.visibility = View.VISIBLE
            container.visibility = View.GONE
        } else {
            emptyView.visibility = View.GONE
            container.visibility = View.VISIBLE

            val titleColor = android.graphics.Color.parseColor(if (isLight) "#000000" else "#FFFFFF")
            val urlColor = android.graphics.Color.parseColor(if (isLight) "#6C6C70" else "#8E8E93")
            val arrowColor = android.graphics.Color.parseColor(if (isLight) "#8E8E93" else "#5E5970")
            val dividerColor = android.graphics.Color.parseColor(if (isLight) "#E5E5EA" else "#22FFFFFF")

            for (i in 0 until historyList.size) {
                val (title, url) = historyList[i]
                val itemView = layoutInflater.inflate(R.layout.item_home_history, container, false)
                val itemTitle = itemView.findViewById<TextView>(R.id.itemHistoryTitle)
                val itemUrl = itemView.findViewById<TextView>(R.id.itemHistoryUrl)
                val itemArrow = itemView.findViewById<ImageView>(R.id.itemHistoryArrow)
                val itemDivider = itemView.findViewById<View>(R.id.itemHistoryDivider)
                val itemRow = itemView.findViewById<View>(R.id.itemHistoryRow)

                itemTitle?.text = title
                itemTitle?.setTextColor(titleColor)

                itemUrl?.text = extractDomain(url)
                itemUrl?.setTextColor(urlColor)

                itemArrow?.setColorFilter(arrowColor)
                itemDivider?.setBackgroundColor(dividerColor)

                if (i == historyList.size - 1) {
                    itemDivider?.visibility = View.GONE
                }

                itemRow?.setOnClickListener {
                    navigate(url)
                }

                container.addView(itemView)
            }
        }
    }

    private fun openSearchOverlay() {
        val rawUrl = if (activeTab?.isHome == false) activeTab?.url.orEmpty() else ""
        val displayUrl = if (rawUrl.isNotBlank()) {
            try {
                java.net.URLDecoder.decode(rawUrl, "UTF-8")
            } catch (_: Exception) {
                rawUrl
            }
        } else ""
        overlaySearchInput.setText(displayUrl)
        if (displayUrl.isNotEmpty()) {
            overlaySearchInput.selectAll()
        }

        // Hide main bottomBar so it NEVER shows or overlaps behind the search bar over the keyboard!
        bottomBar.animate().cancel()
        bottomBar.animate()
            .alpha(0f)
            .setDuration(150)
            .withEndAction {
                bottomBar.visibility = View.INVISIBLE
            }
            .start()

        searchOverlay.alpha = 0f
        searchOverlay.visibility = View.VISIBLE
        searchOverlay.animate()
            .alpha(1f)
            .setDuration(180)
            .start()

        val searchBottomBar = findViewById<View>(R.id.searchOverlayBottomBar)
        searchBottomBar?.translationY = 80f
        searchBottomBar?.animate()
            ?.translationY(0f)
            ?.setDuration(220)
            ?.setInterpolator(android.view.animation.DecelerateInterpolator())
            ?.start()

        overlaySearchInput.requestFocus()
        val imm = getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager
        overlaySearchInput.postDelayed({
            imm.showSoftInput(overlaySearchInput, InputMethodManager.SHOW_IMPLICIT)
        }, 100)

        if (rawUrl.isBlank()) {
            loadInitialSuggestions()
        } else {
            lifecycleScope.launch {
                performSearchSuggestions(rawUrl)
            }
        }
    }

    private fun closeSearchOverlay() {
        hideKeyboard()
        searchDebounceJob?.cancel()

        // Bring main bottomBar back smoothly
        bottomBar.visibility = View.VISIBLE
        bottomBar.animate().cancel()
        bottomBar.animate()
            .alpha(1f)
            .setDuration(180)
            .start()

        val searchBottomBar = findViewById<View>(R.id.searchOverlayBottomBar)
        searchBottomBar?.animate()
            ?.translationY(60f)
            ?.setDuration(160)
            ?.start()

        searchOverlay.animate()
            .alpha(0f)
            .setDuration(180)
            .withEndAction {
                searchOverlay.visibility = View.GONE
                searchBottomBar?.translationY = 0f
            }
            .start()
    }

    private fun loadInitialSuggestions() {
        val list = mutableListOf<SearchSuggestion>()

        // 1. All open tabs
        val openTabs = tabs.filter { !it.isHome && it.url.isNotBlank() }
        for (tab in openTabs) {
            val isCurrent = tab == activeTab
            list.add(
                SearchSuggestion(
                    title = tab.title.ifBlank { tab.url },
                    subtitle = if (isCurrent) "Текущая вкладка • ${sanitizeUrl(tab.url)}" else "Открытая вкладка • ${sanitizeUrl(tab.url)}",
                    targetUrl = tab.url,
                    type = SuggestionType.OPEN_TAB,
                    iconRes = R.drawable.ic_tabs
                )
            )
        }

        // 2. Visited history
        try {
            val arr = JSONArray(prefs.historyJson)
            for (i in 0 until arr.length()) {
                val obj = arr.getJSONObject(i)
                val url = obj.optString("url", "")
                val title = obj.optString("title", url)
                if (url.isNotBlank() && list.none { it.targetUrl == url }) {
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

        // 3. If empty -> show "История пуста"
        if (list.isEmpty()) {
            list.add(
                SearchSuggestion(
                    title = "История пуста",
                    subtitle = "Здесь появятся ваши открытые вкладки и посещённые сайты",
                    targetUrl = "",
                    type = SuggestionType.INFO,
                    iconRes = R.drawable.ic_history
                )
            )
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
        if (activeTab != null) {
            captureTabSnapshot(activeTab)
            if (!activeTab!!.isHome && !activeTab!!.isCustomInternalTab) {
                val bundle = Bundle()
                webView.saveState(bundle)
                activeTab!!.webViewState = bundle
            }
        }
        val tab = LumaTab(isHome = url == null)
        tabs.add(tab)
        setActiveTab(tab)
        updateTabCount()
        if (url != null) navigate(url)
        else showHome()
    }

    private fun setActiveTab(tab: LumaTab) {
        if (activeTab != null && activeTab != tab) {
            captureTabSnapshot(activeTab)
            if (!activeTab!!.isHome && !activeTab!!.isCustomInternalTab) {
                val bundle = Bundle()
                webView.saveState(bundle)
                activeTab!!.webViewState = bundle
            }
        }
        activeTab = tab
        when {
            tab.isHistory -> showHistoryTab()
            tab.isDownloads -> showDownloadsTab()
            tab.isHome -> showHome()
            else -> {
                showWebView()
                if (tab.webViewState != null) {
                    webView.restoreState(tab.webViewState!!)
                } else if (tab.url.isNotBlank()) {
                    webView.loadUrl(tab.url)
                }
            }
        }
        updateTabCount()
    }

    fun openHistoryTab() {
        val current = activeTab
        if (current != null && current.isHome) {
            current.isHome = false
            current.isHistory = true
            current.isDownloads = false
            current.title = "История"
            current.url = "luma://history"
            showHistoryTab()
            return
        }
        val existing = tabs.find { it.isHistory }
        if (existing != null) {
            setActiveTab(existing)
            return
        }
        if (activeTab != null) {
            captureTabSnapshot(activeTab)
            if (!activeTab!!.isHome && !activeTab!!.isCustomInternalTab) {
                val bundle = Bundle()
                webView.saveState(bundle)
                activeTab!!.webViewState = bundle
            }
        }
        val tab = LumaTab(
            title = "История",
            url = "luma://history",
            isHome = false,
            isHistory = true,
            isDownloads = false
        )
        tabs.add(tab)
        setActiveTab(tab)
    }

    fun openDownloadsTab() {
        val current = activeTab
        if (current != null && current.isHome) {
            current.isHome = false
            current.isHistory = false
            current.isDownloads = true
            current.title = "Загрузки"
            current.url = "luma://downloads"
            showDownloadsTab()
            return
        }
        val existing = tabs.find { it.isDownloads }
        if (existing != null) {
            setActiveTab(existing)
            return
        }
        if (activeTab != null) {
            captureTabSnapshot(activeTab)
            if (!activeTab!!.isHome && !activeTab!!.isCustomInternalTab) {
                val bundle = Bundle()
                webView.saveState(bundle)
                activeTab!!.webViewState = bundle
            }
        }
        val tab = LumaTab(
            title = "Загрузки",
            url = "luma://downloads",
            isHome = false,
            isHistory = false,
            isDownloads = true
        )
        tabs.add(tab)
        setActiveTab(tab)
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
        updateTabSwitcherCount()
    }

    private fun updateTabSwitcherCount() {
        if (!::tabSwitcherCountInfo.isInitialized) return
        val count = tabs.size
        val word = when {
            count % 10 == 1 && count % 100 != 11 -> "вкладка"
            count % 10 in 2..4 && (count % 100 !in 12..14) -> "вкладки"
            else -> "вкладок"
        }
        tabSwitcherCountInfo.text = "$count $word"
    }

    private fun openTabSwitcher() {
        hideKeyboard()
        captureTabSnapshot(activeTab)
        safariTabsAdapter.activeTabId = activeTab?.id
        safariTabsAdapter.syncTabs()
        updateTabSwitcherCount()

        tabSwitcherOverlay.alpha = 0f
        tabSwitcherOverlay.scaleX = 0.96f
        tabSwitcherOverlay.scaleY = 0.96f
        tabSwitcherOverlay.visibility = View.VISIBLE
        tabSwitcherOverlay.animate()
            .alpha(1f)
            .scaleX(1f)
            .scaleY(1f)
            .setDuration(220)
            .setInterpolator(android.view.animation.DecelerateInterpolator())
            .start()
    }

    private fun closeTabSwitcher() {
        tabSwitcherOverlay.animate()
            .alpha(0f)
            .scaleX(0.96f)
            .scaleY(0.96f)
            .setDuration(180)
            .setInterpolator(android.view.animation.AccelerateInterpolator())
            .withEndAction {
                tabSwitcherOverlay.visibility = View.GONE
                tabSwitcherSearchInput.text?.clear()
            }
            .start()
    }

    private fun captureTabSnapshot(tab: LumaTab?) {
        if (tab == null) return
        try {
            if (tab.isHome) {
                val w = (homeDashboard.width.takeIf { it > 0 } ?: 360)
                val h = (homeDashboard.height.takeIf { it > 0 } ?: 600)
                val targetW = 320
                val targetH = ((320f * h) / w).toInt().coerceIn(240, 560)
                val bm = Bitmap.createBitmap(targetW, targetH, Bitmap.Config.ARGB_8888)
                val canvas = android.graphics.Canvas(bm)
                val scale = targetW.toFloat() / w
                canvas.scale(scale, scale)
                homeDashboard.draw(canvas)
                tab.thumbnail = bm
            } else if (webView.width > 0 && webView.height > 0) {
                val targetW = 360
                val targetH = ((360f * webView.height) / webView.width).toInt().coerceIn(240, 640)
                val bm = Bitmap.createBitmap(targetW, targetH, Bitmap.Config.ARGB_8888)
                val canvas = android.graphics.Canvas(bm)
                val scale = targetW.toFloat() / webView.width
                canvas.scale(scale, scale)
                webView.draw(canvas)
                tab.thumbnail = bm
            }
        } catch (_: Exception) {}
    }

    private fun switchTabRelative(delta: Int) {
        if (tabs.isEmpty()) return
        val idx = tabs.indexOf(activeTab)
        if (idx < 0) return
        val nextIdx = idx + delta
        if (nextIdx in 0 until tabs.size) {
            captureTabSnapshot(activeTab)
            setActiveTab(tabs[nextIdx])
            Toast.makeText(this, tabs[nextIdx].title.ifBlank { "Вкладка ${nextIdx + 1}" }, Toast.LENGTH_SHORT).show()
        } else if (nextIdx >= tabs.size && delta > 0) {
            openNewTab()
            Toast.makeText(this, "Новая вкладка", Toast.LENGTH_SHORT).show()
        }
    }

    private fun applyAppTheme() {
        try {
            val themeMode = prefs.appTheme
            val isLight = when (themeMode) {
                "light" -> true
                "dark" -> false
                else -> {
                    val nightMode = resources.configuration.uiMode and android.content.res.Configuration.UI_MODE_NIGHT_MASK
                    nightMode != android.content.res.Configuration.UI_MODE_NIGHT_YES
                }
            }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            val appearance = if (isLight) {
                WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS or WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS
            } else 0
            window.insetsController?.setSystemBarsAppearance(
                appearance,
                WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS or WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS
            )
        }

        val rootLayout = findViewById<FrameLayout>(android.R.id.content)
        val addressText = findViewById<TextView>(R.id.bottomAddressBar)
        val tabCountTxt = findViewById<TextView>(R.id.tabCountText)

        if (isLight) {
            val themeBgColor = android.graphics.Color.parseColor("#F2F2F7")
            try {
                window.navigationBarColor = themeBgColor
                window.statusBarColor = themeBgColor
            } catch (_: Exception) {}
            if (::mainRootLayout.isInitialized) {
                mainRootLayout.setBackgroundColor(themeBgColor)
            }
            rootLayout?.setBackgroundColor(themeBgColor)
            homeDashboard.setBackgroundColor(themeBgColor)

            if (::historyDashboard.isInitialized) {
                historyDashboard.setBackgroundColor(themeBgColor)
                historyTabTitle.setTextColor(android.graphics.Color.parseColor("#000000"))
                btnHistoryTabClear.setTextColor(android.graphics.Color.parseColor("#7468C7"))
                historyTabSearchCapsule.setBackgroundResource(R.drawable.bg_safari_address_pill_light)
                historyTabSearchInput.setTextColor(android.graphics.Color.parseColor("#000000"))
                historyTabSearchInput.setHintTextColor(android.graphics.Color.parseColor("#8E8E93"))
                findViewById<ImageView>(R.id.historyTabSearchIcon)?.setColorFilter(android.graphics.Color.parseColor("#000000"))
                findViewById<TextView>(R.id.historyTabEmptyTitle)?.setTextColor(android.graphics.Color.parseColor("#000000"))
                findViewById<TextView>(R.id.historyTabEmptySubtitle)?.setTextColor(android.graphics.Color.parseColor("#8E8E93"))
                findViewById<ImageView>(R.id.historyTabEmptyIcon)?.setColorFilter(android.graphics.Color.parseColor("#8E8E93"))
                if (::historyTabAdapter.isInitialized) {
                    historyTabAdapter.isLight = true
                    historyTabAdapter.notifyDataSetChanged()
                }
            }

            if (::downloadsDashboard.isInitialized) {
                downloadsDashboard.setBackgroundColor(themeBgColor)
                downloadsTabTitle.setTextColor(android.graphics.Color.parseColor("#000000"))
                btnDownloadsOpenFolder.setTextColor(android.graphics.Color.parseColor("#7468C7"))
                findViewById<TextView>(R.id.downloadsTabEmptyTitle)?.setTextColor(android.graphics.Color.parseColor("#000000"))
                findViewById<TextView>(R.id.downloadsTabEmptySubtitle)?.setTextColor(android.graphics.Color.parseColor("#8E8E93"))
                findViewById<ImageView>(R.id.downloadsTabEmptyIcon)?.setColorFilter(android.graphics.Color.parseColor("#8E8E93"))
                if (::downloadsTabAdapter.isInitialized) {
                    downloadsTabAdapter.isLight = true
                    downloadsTabAdapter.notifyDataSetChanged()
                }
            }

            // Safari Bottom Bar (Clean floating glass islands, no background bar)
            bottomBar.background = null
            bottomAddressPill.setBackgroundResource(R.drawable.bg_safari_address_pill_light)
            addressText?.setTextColor(android.graphics.Color.parseColor("#000000"))
            tabCountTxt?.setTextColor(android.graphics.Color.parseColor("#000000"))
            tabCountTxt?.setBackgroundResource(R.drawable.bg_safari_tab_badge_light)

            // Bottom bar buttons: Light oval ripples, NO black circles!
            btnBottomBack.setBackgroundResource(R.drawable.bg_safari_btn_light)
            btnBottomAi.setBackgroundResource(R.drawable.bg_safari_btn_light)
            btnTabCount.setBackgroundResource(R.drawable.bg_safari_btn_light)
            findViewById<View>(R.id.btnMenuFrame)?.setBackgroundResource(R.drawable.bg_safari_btn_light)

            val iconColor = android.graphics.Color.parseColor("#000000")
            findViewById<ImageView>(R.id.iconBottomBack)?.setColorFilter(iconColor)
            findViewById<ImageButton>(R.id.btnMenu)?.setColorFilter(iconColor)
            findViewById<ImageButton>(R.id.btnReload)?.setColorFilter(iconColor)
            findViewById<ImageView>(R.id.bottomBarLock)?.setColorFilter(iconColor)

            // Bottom Search Overlay Scrim (iOS Safari Light)
            findViewById<View>(R.id.searchOverlay)?.setBackgroundColor(android.graphics.Color.parseColor("#44000000"))
            findViewById<View>(R.id.searchOverlayCapsule)?.setBackgroundResource(R.drawable.bg_safari_address_pill_light)
            overlaySearchInput.setTextColor(android.graphics.Color.parseColor("#000000"))
            overlaySearchInput.setHintTextColor(android.graphics.Color.parseColor("#8E8E93"))
            findViewById<ImageView>(R.id.overlaySearchIcon)?.setColorFilter(iconColor)
            findViewById<ImageButton>(R.id.overlaySearchClear)?.setColorFilter(iconColor)
            overlaySearchCancel.setBackgroundResource(R.drawable.bg_safari_btn_light)
            findViewById<ImageView>(R.id.iconOverlaySearchCancel)?.setColorFilter(iconColor)

            // Home Header & Branding
            findViewById<View>(R.id.homeStatusChip)?.setBackgroundResource(R.drawable.bg_light_pill)
            findViewById<TextView>(R.id.homeStatusText)?.setTextColor(android.graphics.Color.parseColor("#000000"))
            findViewById<View>(R.id.btnHomeAccount)?.setBackgroundResource(R.drawable.bg_light_pill)
            findViewById<TextView>(R.id.homeAccountText)?.setTextColor(android.graphics.Color.parseColor("#000000"))
            findViewById<ImageView>(R.id.homeAccountIcon)?.setColorFilter(iconColor)

            findViewById<View>(R.id.homeLogoBadge)?.setBackgroundResource(R.drawable.bg_light_card)
            homeHeadline.setTextColor(android.graphics.Color.parseColor("#000000"))
            findViewById<TextView>(R.id.homeSubtitle)?.setTextColor(android.graphics.Color.parseColor("#6C6C70"))

            // Home Search Capsule
            homeSearchShell.setBackgroundResource(R.drawable.bg_light_card)
            homeSearchBox.setTextColor(android.graphics.Color.parseColor("#000000"))
            homeSearchBox.setHintTextColor(android.graphics.Color.parseColor("#8E8E93"))
            findViewById<ImageView>(R.id.homeSearchIcon)?.setColorFilter(iconColor)
            homeSearchBtn.setBackgroundResource(R.drawable.bg_light_pill)
            findViewById<ImageView>(R.id.homeSearchArrow)?.setColorFilter(iconColor)

            // Home Action Pills
            val pillBg = R.drawable.bg_light_pill
            findViewById<View>(R.id.pillHistory)?.setBackgroundResource(pillBg)
            findViewById<View>(R.id.pillBookmarks)?.setBackgroundResource(pillBg)
            findViewById<View>(R.id.pillDownloads)?.setBackgroundResource(pillBg)
            findViewById<View>(R.id.pillSettings)?.setBackgroundResource(pillBg)

            findViewById<TextView>(R.id.textPillHistory)?.setTextColor(iconColor)
            findViewById<TextView>(R.id.textPillBookmarks)?.setTextColor(iconColor)
            findViewById<TextView>(R.id.textPillDownloads)?.setTextColor(iconColor)
            findViewById<TextView>(R.id.textPillSettings)?.setTextColor(iconColor)

            findViewById<ImageView>(R.id.iconPillHistory)?.setColorFilter(iconColor)
            findViewById<ImageView>(R.id.iconPillBookmarks)?.setColorFilter(iconColor)
            findViewById<ImageView>(R.id.iconPillDownloads)?.setColorFilter(iconColor)
            findViewById<ImageView>(R.id.iconPillSettings)?.setColorFilter(iconColor)

            // Home History Section Card
            findViewById<View>(R.id.homeHistorySection)?.setBackgroundResource(R.drawable.bg_light_card)
            findViewById<TextView>(R.id.homeHistoryHeaderTitle)?.setTextColor(android.graphics.Color.parseColor("#8E8E93"))
            findViewById<TextView>(R.id.homeHistoryEmptyText)?.setTextColor(android.graphics.Color.parseColor("#8E8E93"))
            findViewById<ImageView>(R.id.homeHistoryEmptyIcon)?.setColorFilter(android.graphics.Color.parseColor("#8E8E93"))

            if (::tabSwitcherOverlay.isInitialized) {
                tabSwitcherOverlay.setBackgroundColor(android.graphics.Color.parseColor("#F0F2F2F7"))
                tabSwitcherTitle.setTextColor(android.graphics.Color.parseColor("#000000"))
                tabSwitcherCountInfo.setTextColor(android.graphics.Color.parseColor("#000000"))
            }
        } else {
            val themeBgColor = android.graphics.Color.parseColor("#0B0912")
            try {
                window.navigationBarColor = themeBgColor
                window.statusBarColor = themeBgColor
            } catch (_: Exception) {}
            if (::mainRootLayout.isInitialized) {
                mainRootLayout.setBackgroundColor(themeBgColor)
            }
            rootLayout?.setBackgroundColor(themeBgColor)
            homeDashboard.setBackgroundResource(R.drawable.bg_home_atmosphere)

            if (::historyDashboard.isInitialized) {
                historyDashboard.setBackgroundColor(themeBgColor)
                historyTabTitle.setTextColor(android.graphics.Color.parseColor("#FFFFFF"))
                btnHistoryTabClear.setTextColor(android.graphics.Color.parseColor("#A9A2D8"))
                historyTabSearchCapsule.setBackgroundResource(R.drawable.bg_safari_address_pill_dark)
                historyTabSearchInput.setTextColor(android.graphics.Color.parseColor("#FFFFFF"))
                historyTabSearchInput.setHintTextColor(android.graphics.Color.parseColor("#716C82"))
                findViewById<ImageView>(R.id.historyTabSearchIcon)?.clearColorFilter()
                findViewById<TextView>(R.id.historyTabEmptyTitle)?.setTextColor(android.graphics.Color.parseColor("#FFFFFF"))
                findViewById<TextView>(R.id.historyTabEmptySubtitle)?.setTextColor(android.graphics.Color.parseColor("#8E8E93"))
                findViewById<ImageView>(R.id.historyTabEmptyIcon)?.setColorFilter(android.graphics.Color.parseColor("#8E8E93"))
                if (::historyTabAdapter.isInitialized) {
                    historyTabAdapter.isLight = false
                    historyTabAdapter.notifyDataSetChanged()
                }
            }

            if (::downloadsDashboard.isInitialized) {
                downloadsDashboard.setBackgroundColor(themeBgColor)
                downloadsTabTitle.setTextColor(android.graphics.Color.parseColor("#FFFFFF"))
                btnDownloadsOpenFolder.setTextColor(android.graphics.Color.parseColor("#A9A2D8"))
                findViewById<TextView>(R.id.downloadsTabEmptyTitle)?.setTextColor(android.graphics.Color.parseColor("#FFFFFF"))
                findViewById<TextView>(R.id.downloadsTabEmptySubtitle)?.setTextColor(android.graphics.Color.parseColor("#8E8E93"))
                findViewById<ImageView>(R.id.downloadsTabEmptyIcon)?.setColorFilter(android.graphics.Color.parseColor("#8E8E93"))
                if (::downloadsTabAdapter.isInitialized) {
                    downloadsTabAdapter.isLight = false
                    downloadsTabAdapter.notifyDataSetChanged()
                }
            }

            // Safari Bottom Bar (Clean floating glass islands, no background bar)
            bottomBar.background = null
            bottomAddressPill.setBackgroundResource(R.drawable.bg_safari_address_pill_dark)
            addressText?.setTextColor(android.graphics.Color.parseColor("#FFFFFF"))
            tabCountTxt?.setTextColor(android.graphics.Color.parseColor("#FFFFFF"))
            tabCountTxt?.setBackgroundResource(R.drawable.bg_safari_tab_badge)

            btnBottomBack.setBackgroundResource(R.drawable.bg_safari_btn_dark)
            btnBottomAi.setBackgroundResource(R.drawable.bg_safari_btn_dark)
            btnTabCount.setBackgroundResource(R.drawable.bg_safari_btn_dark)
            findViewById<View>(R.id.btnMenuFrame)?.setBackgroundResource(R.drawable.bg_safari_btn_dark)

            findViewById<ImageView>(R.id.iconBottomBack)?.clearColorFilter()
            findViewById<ImageButton>(R.id.btnMenu)?.clearColorFilter()
            findViewById<ImageButton>(R.id.btnReload)?.clearColorFilter()
            findViewById<ImageView>(R.id.bottomBarLock)?.clearColorFilter()

            // Bottom Search Overlay Scrim (iOS Safari Dark)
            findViewById<View>(R.id.searchOverlay)?.setBackgroundColor(android.graphics.Color.parseColor("#66000000"))
            findViewById<View>(R.id.searchOverlayCapsule)?.setBackgroundResource(R.drawable.bg_safari_address_pill_dark)
            overlaySearchInput.setTextColor(android.graphics.Color.parseColor("#FFFFFF"))
            overlaySearchInput.setHintTextColor(android.graphics.Color.parseColor("#716C82"))
            findViewById<ImageView>(R.id.overlaySearchIcon)?.clearColorFilter()
            findViewById<ImageButton>(R.id.overlaySearchClear)?.clearColorFilter()
            overlaySearchCancel.setBackgroundResource(R.drawable.bg_safari_btn_dark)
            findViewById<ImageView>(R.id.iconOverlaySearchCancel)?.clearColorFilter()

            // Home Header & Branding
            findViewById<View>(R.id.homeStatusChip)?.setBackgroundResource(R.drawable.bg_glass_pill)
            findViewById<TextView>(R.id.homeStatusText)?.setTextColor(android.graphics.Color.parseColor("#C7C3D1"))
            findViewById<View>(R.id.btnHomeAccount)?.setBackgroundResource(R.drawable.bg_glass_pill)
            findViewById<TextView>(R.id.homeAccountText)?.setTextColor(android.graphics.Color.parseColor("#FFFFFF"))
            findViewById<ImageView>(R.id.homeAccountIcon)?.clearColorFilter()

            findViewById<View>(R.id.homeLogoBadge)?.setBackgroundResource(R.drawable.bg_glass_card)
            homeHeadline.setTextColor(android.graphics.Color.parseColor("#FFFFFF"))
            findViewById<TextView>(R.id.homeSubtitle)?.setTextColor(android.graphics.Color.parseColor("#787389"))

            // Home Search Capsule
            homeSearchShell.setBackgroundResource(R.drawable.bg_glass_search)
            homeSearchBox.setTextColor(android.graphics.Color.parseColor("#FFFFFF"))
            homeSearchBox.setHintTextColor(android.graphics.Color.parseColor("#716C82"))
            findViewById<ImageView>(R.id.homeSearchIcon)?.clearColorFilter()
            homeSearchBtn.setBackgroundResource(R.drawable.bg_glass_pill)
            findViewById<ImageView>(R.id.homeSearchArrow)?.clearColorFilter()

            // Home Action Pills
            val pillBg = R.drawable.bg_glass_pill
            findViewById<View>(R.id.pillHistory)?.setBackgroundResource(pillBg)
            findViewById<View>(R.id.pillBookmarks)?.setBackgroundResource(pillBg)
            findViewById<View>(R.id.pillDownloads)?.setBackgroundResource(pillBg)
            findViewById<View>(R.id.pillSettings)?.setBackgroundResource(pillBg)

            val textMuted = android.graphics.Color.parseColor("#C7C3D1")
            findViewById<TextView>(R.id.textPillHistory)?.setTextColor(textMuted)
            findViewById<TextView>(R.id.textPillBookmarks)?.setTextColor(textMuted)
            findViewById<TextView>(R.id.textPillDownloads)?.setTextColor(textMuted)
            findViewById<TextView>(R.id.textPillSettings)?.setTextColor(textMuted)

            findViewById<ImageView>(R.id.iconPillHistory)?.clearColorFilter()
            findViewById<ImageView>(R.id.iconPillBookmarks)?.clearColorFilter()
            findViewById<ImageView>(R.id.iconPillDownloads)?.clearColorFilter()
            findViewById<ImageView>(R.id.iconPillSettings)?.clearColorFilter()

            // Home History Section Card
            findViewById<View>(R.id.homeHistorySection)?.setBackgroundResource(R.drawable.bg_glass_card)
            findViewById<TextView>(R.id.homeHistoryHeaderTitle)?.setTextColor(android.graphics.Color.parseColor("#8E8E93"))
            findViewById<TextView>(R.id.homeHistoryEmptyText)?.setTextColor(android.graphics.Color.parseColor("#8E8E93"))
            findViewById<ImageView>(R.id.homeHistoryEmptyIcon)?.setColorFilter(android.graphics.Color.parseColor("#8E8E93"))

            if (::tabSwitcherOverlay.isInitialized) {
                tabSwitcherOverlay.setBackgroundColor(android.graphics.Color.parseColor("#E60A0812"))
                tabSwitcherTitle.setTextColor(android.graphics.Color.parseColor("#FFFFFF"))
                tabSwitcherCountInfo.setTextColor(android.graphics.Color.parseColor("#FFFFFF"))
            }
        }

        populateHomeHistory()
        } catch (e: Exception) {
            android.util.Log.e("LumaTheme", "applyAppTheme error safely caught", e)
        }
    }

    fun navigate(url: String) {
        if (url.startsWith("luma://")) {
            handleInternalScheme(url)
            return
        }
        readerOverlay.visibility = View.GONE
        showWebView()
        webView.loadUrl(url)
        activeTab?.let {
            it.url = url
            it.isHome = false
            it.isHistory = false
            it.isDownloads = false
        }
        updateBottomBarState(url)
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

    private fun extractDomain(url: String): String {
        if (url.isBlank() || url == "about:blank" || url.startsWith("luma://") || url.startsWith("file://")) {
            return "Стартовая страница"
        }
        return try {
            val uri = Uri.parse(url)
            val host = uri.host?.removePrefix("www.")
            if (!host.isNullOrBlank()) host else url.removePrefix("https://").removePrefix("http://").substringBefore('/')
        } catch (_: Exception) {
            url.removePrefix("https://").removePrefix("http://").substringBefore('/')
        }
    }

    private fun updateBottomBarState(url: String? = null) {
        val currentTab = activeTab
        val isHistory = currentTab?.isHistory == true || (::historyDashboard.isInitialized && historyDashboard.visibility == View.VISIBLE)
        val isDownloads = currentTab?.isDownloads == true || (::downloadsDashboard.isInitialized && downloadsDashboard.visibility == View.VISIBLE)
        val isHome = (currentTab?.isHome != false && !isHistory && !isDownloads) || (webView.visibility != View.VISIBLE && !isHistory && !isDownloads)
        val canBack = (webView.visibility == View.VISIBLE && webView.canGoBack()) || isHistory || isDownloads

        btnBottomBack.isEnabled = canBack || (!isHome)
        findViewById<ImageView>(R.id.iconBottomBack)?.alpha = if (canBack || !isHome) 1.0f else 0.35f

        val bottomLock = findViewById<ImageView>(R.id.bottomBarLock)

        if (isBottomBarCollapsed) {
            bottomBarLogo.visibility = View.GONE
            btnReload.visibility = View.GONE
            bottomLock?.visibility = View.GONE
            if (isHistory) {
                bottomAddressBar.text = "История"
            } else if (isDownloads) {
                bottomAddressBar.text = "Загрузки"
            } else {
                val targetUrl = url ?: currentTab?.url.orEmpty()
                bottomAddressBar.text = extractDomain(targetUrl).ifBlank { "Luma" }
            }
            return
        }

        if (isHistory) {
            bottomAddressBar.text = "История"
            bottomBarLogo.visibility = View.GONE
            btnReload.visibility = View.GONE
            bottomLock?.visibility = View.GONE
        } else if (isDownloads) {
            bottomAddressBar.text = "Загрузки"
            bottomBarLogo.visibility = View.GONE
            btnReload.visibility = View.GONE
            bottomLock?.visibility = View.GONE
        } else if (isHome) {
            bottomAddressBar.text = "Стартовая страница"
            bottomBarLogo.visibility = View.VISIBLE
            btnReload.visibility = View.GONE
            bottomLock?.visibility = View.GONE
        } else {
            val targetUrl = url ?: currentTab?.url.orEmpty()
            bottomAddressBar.text = extractDomain(targetUrl)
            bottomBarLogo.visibility = View.GONE
            btnReload.visibility = View.VISIBLE
            if (targetUrl.startsWith("https://")) {
                bottomLock?.visibility = View.VISIBLE
            } else {
                bottomLock?.visibility = View.GONE
            }
        }
    }

    private fun collapseBottomBar() {
        if (isBottomBarCollapsed || activeTab?.isHome != false || activeTab?.isHistory == true || activeTab?.isDownloads == true || webView.visibility != View.VISIBLE) return
        isBottomBarCollapsed = true

        val density = resources.displayMetrics.density
        val shiftX = 46f * density

        val domain = extractDomain(activeTab?.url.orEmpty()).ifBlank { "Luma" }
        bottomAddressBar.text = domain

        val textWidth = bottomAddressBar.paint.measureText(domain)
        val compactWidth = (textWidth + 36f * density).toInt().coerceIn((120f * density).toInt(), (190f * density).toInt())

        val screenW = resources.displayMetrics.widthPixels
        val fullWidth = screenW - (208f * density).toInt()
        val startW = if (bottomAddressPill.width > 0) bottomAddressPill.width else fullWidth
        val startTransX = bottomAddressPill.translationX

        val buttons = listOfNotNull(
            btnBottomBack,
            btnBottomAi,
            btnTabCount,
            findViewById<View>(R.id.btnMenuFrame)
        )
        for (btn in buttons) {
            btn.isEnabled = false
        }

        collapseAnimator?.cancel()
        collapseAnimator = android.animation.ValueAnimator.ofFloat(0f, 1f).apply {
            duration = 240
            interpolator = android.view.animation.DecelerateInterpolator(1.5f)
            addUpdateListener { va ->
                val t = va.animatedValue as Float
                val w = (startW + (compactWidth - startW) * t).toInt()
                val lp = bottomAddressPill.layoutParams as LinearLayout.LayoutParams
                lp.width = w
                lp.weight = 0f
                bottomAddressPill.layoutParams = lp

                bottomAddressPill.translationX = startTransX + (shiftX - startTransX) * t

                btnBottomBack.translationX = - (25f * density) * t
                btnBottomBack.alpha = (1f - t).coerceIn(0f, 1f)

                val rightTrans = (25f * density) * t
                val rightAlpha = (1f - t).coerceIn(0f, 1f)
                btnBottomAi.translationX = rightTrans
                btnBottomAi.alpha = rightAlpha
                btnTabCount.translationX = rightTrans
                btnTabCount.alpha = rightAlpha
                findViewById<View>(R.id.btnMenuFrame)?.let {
                    it.translationX = rightTrans
                    it.alpha = rightAlpha
                }

                bottomBarLogo.alpha = (1f - t).coerceIn(0f, 1f)
                btnReload.alpha = (1f - t).coerceIn(0f, 1f)
            }
            addListener(object : android.animation.AnimatorListenerAdapter() {
                override fun onAnimationEnd(animation: android.animation.Animator) {
                    if (isBottomBarCollapsed) {
                        bottomBarLogo.visibility = View.GONE
                        btnReload.visibility = View.GONE
                        for (btn in buttons) {
                            btn.visibility = View.INVISIBLE
                        }
                    }
                }
            })
            start()
        }

        resetInactivityTimer()
    }

    private fun expandBottomBar() {
        if (!isBottomBarCollapsed) return
        isBottomBarCollapsed = false
        bottomBarExpandJob?.cancel()

        val density = resources.displayMetrics.density

        val screenW = resources.displayMetrics.widthPixels
        val fullWidth = screenW - (208f * density).toInt()
        val currentW = bottomAddressPill.width
        val startTransX = bottomAddressPill.translationX

        val buttons = listOfNotNull(
            btnBottomBack,
            btnBottomAi,
            btnTabCount,
            findViewById<View>(R.id.btnMenuFrame)
        )
        for (btn in buttons) {
            btn.visibility = View.VISIBLE
            btn.isEnabled = true
        }

        updateBottomBarState()

        collapseAnimator?.cancel()
        collapseAnimator = android.animation.ValueAnimator.ofFloat(0f, 1f).apply {
            duration = 240
            interpolator = android.view.animation.DecelerateInterpolator(1.5f)
            addUpdateListener { va ->
                val t = va.animatedValue as Float
                val w = (currentW + (fullWidth - currentW) * t).toInt()
                val lp = bottomAddressPill.layoutParams as LinearLayout.LayoutParams
                lp.width = w
                lp.weight = 0f
                bottomAddressPill.layoutParams = lp

                bottomAddressPill.translationX = startTransX * (1f - t)

                btnBottomBack.translationX = - (25f * density) * (1f - t)
                btnBottomBack.alpha = t

                val rightTrans = (25f * density) * (1f - t)
                btnBottomAi.translationX = rightTrans
                btnBottomAi.alpha = t
                btnTabCount.translationX = rightTrans
                btnTabCount.alpha = t
                findViewById<View>(R.id.btnMenuFrame)?.let {
                    it.translationX = rightTrans
                    it.alpha = t
                }

                bottomBarLogo.alpha = t
                btnReload.alpha = t
            }
            addListener(object : android.animation.AnimatorListenerAdapter() {
                override fun onAnimationEnd(animation: android.animation.Animator) {
                    if (!isBottomBarCollapsed) {
                        val lp = bottomAddressPill.layoutParams as LinearLayout.LayoutParams
                        lp.width = 0
                        lp.weight = 1f
                        bottomAddressPill.layoutParams = lp
                        bottomAddressPill.translationX = 0f
                        for (btn in buttons) {
                            btn.translationX = 0f
                            btn.alpha = 1f
                        }
                    }
                }
            })
            start()
        }
    }

    private fun resetInactivityTimer() {
        bottomBarExpandJob?.cancel()
        if (!isBottomBarCollapsed) return
        bottomBarExpandJob = lifecycleScope.launch {
            delay(3000)
            if (isBottomBarCollapsed) {
                expandBottomBar()
            }
        }
    }

    private fun showHome() {
        expandBottomBar()
        readerOverlay.visibility = View.GONE
        webView.visibility = View.GONE
        if (::historyDashboard.isInitialized) historyDashboard.visibility = View.GONE
        if (::downloadsDashboard.isInitialized) downloadsDashboard.visibility = View.GONE
        homeDashboard.visibility = View.VISIBLE
        btnReload.visibility = View.GONE
        activeTab?.let {
            it.isHome = true
            it.isHistory = false
            it.isDownloads = false
            it.title = "Стартовая страница"
            it.url = "luma://home"
        }
        updateHomeAccountButton()
        populateHomeHistory()
        updateBottomBarState()
    }

    private fun showWebView() {
        expandBottomBar()
        readerOverlay.visibility = View.GONE
        homeDashboard.visibility = View.GONE
        if (::historyDashboard.isInitialized) historyDashboard.visibility = View.GONE
        if (::downloadsDashboard.isInitialized) downloadsDashboard.visibility = View.GONE
        webView.visibility = View.VISIBLE
        activeTab?.let {
            it.isHome = false
            it.isHistory = false
            it.isDownloads = false
        }
        updateBottomBarState()
    }

    private fun showHistoryTab() {
        expandBottomBar()
        readerOverlay.visibility = View.GONE
        webView.visibility = View.GONE
        homeDashboard.visibility = View.GONE
        if (::downloadsDashboard.isInitialized) downloadsDashboard.visibility = View.GONE
        if (::historyDashboard.isInitialized) historyDashboard.visibility = View.VISIBLE
        btnReload.visibility = View.GONE
        activeTab?.let {
            it.isHome = false
            it.isHistory = true
            it.isDownloads = false
            it.title = "История"
            it.url = "luma://history"
        }
        loadHistoryTabData()
        updateBottomBarState()
    }

    private fun showDownloadsTab() {
        expandBottomBar()
        readerOverlay.visibility = View.GONE
        webView.visibility = View.GONE
        homeDashboard.visibility = View.GONE
        if (::historyDashboard.isInitialized) historyDashboard.visibility = View.GONE
        if (::downloadsDashboard.isInitialized) downloadsDashboard.visibility = View.VISIBLE
        btnReload.visibility = View.GONE
        activeTab?.let {
            it.isHome = false
            it.isHistory = false
            it.isDownloads = true
            it.title = "Загрузки"
            it.url = "luma://downloads"
        }
        loadDownloadsTabData()
        updateBottomBarState()
    }

    private fun loadHistoryTabData() {
        if (!::historyTabAdapter.isInitialized) return
        val list = mutableListOf<HistoryEntry>()
        try {
            val arr = JSONArray(prefs.historyJson)
            for (i in 0 until arr.length()) {
                val obj = arr.getJSONObject(i)
                val url = obj.optString("url")
                val title = obj.optString("title")
                val ts = obj.optLong("ts", System.currentTimeMillis())
                if (url.isNotBlank()) {
                    list.add(HistoryEntry(url = url, title = title, timestamp = ts))
                }
            }
        } catch (_: Exception) {}
        historyTabAdapter.updateData(list)
        if (::historyTabEmptyView.isInitialized) {
            historyTabEmptyView.visibility = if (list.isEmpty()) View.VISIBLE else View.GONE
        }
    }

    private fun loadDownloadsTabData() {
        if (!::downloadsTabAdapter.isInitialized) return
        lifecycleScope.launch(Dispatchers.IO) {
            val list = mutableListOf<DownloadItem>()
            try {
                val dm = getSystemService(Context.DOWNLOAD_SERVICE) as? DownloadManager
                if (dm != null) {
                    val query = DownloadManager.Query()
                    dm.query(query)?.use { cursor ->
                        val idCol = cursor.getColumnIndex(DownloadManager.COLUMN_ID)
                        val titleCol = cursor.getColumnIndex(DownloadManager.COLUMN_TITLE)
                        val totalBytesCol = cursor.getColumnIndex(DownloadManager.COLUMN_TOTAL_SIZE_BYTES)
                        val statusCol = cursor.getColumnIndex(DownloadManager.COLUMN_STATUS)
                        val uriCol = cursor.getColumnIndex(DownloadManager.COLUMN_LOCAL_URI)
                        val mimeCol = cursor.getColumnIndex(DownloadManager.COLUMN_MEDIA_TYPE)
                        val lastModCol = cursor.getColumnIndex(DownloadManager.COLUMN_LAST_MODIFIED_TIMESTAMP)

                        while (cursor.moveToNext()) {
                            val id = if (idCol != -1) cursor.getLong(idCol) else 0L
                            val title = if (titleCol != -1) cursor.getString(titleCol).orEmpty() else "Файл"
                            val totalBytes = if (totalBytesCol != -1) cursor.getLong(totalBytesCol) else 0L
                            val status = if (statusCol != -1) cursor.getInt(statusCol) else DownloadManager.STATUS_SUCCESSFUL
                            val uri = if (uriCol != -1) cursor.getString(uriCol) else ""
                            val mime = if (mimeCol != -1) cursor.getString(mimeCol) else ""
                            val lastMod = if (lastModCol != -1) cursor.getLong(lastModCol) else System.currentTimeMillis()

                            list.add(
                                DownloadItem(
                                    id = id,
                                    title = title.ifBlank { "Файл $id" },
                                    uriString = uri.orEmpty(),
                                    totalBytes = totalBytes,
                                    status = status,
                                    mediaType = mime.orEmpty(),
                                    lastModified = lastMod
                                )
                            )
                        }
                    }
                }
            } catch (e: Exception) {
                android.util.Log.e("LumaDownloads", "Error querying DownloadManager", e)
            }
            withContext(Dispatchers.Main) {
                downloadsTabAdapter.updateData(list)
                if (::downloadsTabEmptyView.isInitialized) {
                    downloadsTabEmptyView.visibility = if (list.isEmpty()) View.VISIBLE else View.GONE
                }
            }
        }
    }


    // ===== BOTTOM SHEET SMOOTH DISMISS & ANTI-SNAP-BACK BEHAVIOR =====
    private fun configureBottomSheet(dialog: BottomSheetDialog, onDismissAction: (() -> Unit)? = null) {
        dialog.behavior.apply {
            isHideable = true
            skipCollapsed = true
            state = BottomSheetBehavior.STATE_EXPANDED
        }
        dialog.setOnShowListener {
            val bottomSheet = dialog.findViewById<FrameLayout>(com.google.android.material.R.id.design_bottom_sheet) ?: return@setOnShowListener
            val behavior = BottomSheetBehavior.from(bottomSheet)
            behavior.isHideable = true
            behavior.skipCollapsed = true
            behavior.state = BottomSheetBehavior.STATE_EXPANDED

            behavior.addBottomSheetCallback(object : BottomSheetBehavior.BottomSheetCallback() {
                private var lastSlideOffset = 1f

                override fun onStateChanged(sheet: View, newState: Int) {
                    if (newState == BottomSheetBehavior.STATE_HIDDEN) {
                        dialog.dismiss()
                        onDismissAction?.invoke()
                    } else if (newState == BottomSheetBehavior.STATE_COLLAPSED) {
                        behavior.state = BottomSheetBehavior.STATE_HIDDEN
                    }
                }

                override fun onSlide(sheet: View, slideOffset: Float) {
                    // Prevent returning/snapping back when doing a downward swipe:
                    // If user moves the sheet downward past 0.70 slide offset, smoothly transition directly to hidden.
                    if (slideOffset < 0.70f && slideOffset < lastSlideOffset && behavior.state == BottomSheetBehavior.STATE_DRAGGING) {
                        behavior.state = BottomSheetBehavior.STATE_HIDDEN
                    }
                    lastSlideOffset = slideOffset
                }
            })
        }
    }

    // ===== TABS OVERVIEW (SAFARI FULLSCREEN) =====
    private fun showTabsSheet() {
        openTabSwitcher()
    }

    // ===== CIRCLE TO SEARCH & VISUAL AI =====
    private fun showAccountPromptForAi(customMessage: String? = null) {
        Toast.makeText(
            this,
            customMessage ?: "Для использования LumaAI необходимо войти в аккаунт Luma ID.",
            Toast.LENGTH_LONG
        ).show()
        showAccountSheet()
    }

    private fun captureScreen(onReady: (Bitmap) -> Unit) {
        val decorView = window.decorView
        val width = decorView.width
        val height = decorView.height
        if (width <= 0 || height <= 0) return

        fun drawFallback(): Bitmap {
            val bmp = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
            val canvas = Canvas(bmp)
            decorView.draw(canvas)
            return bmp
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val bmp = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
            val location = IntArray(2)
            decorView.getLocationInWindow(location)
            try {
                PixelCopy.request(
                    window,
                    Rect(location[0], location[1], location[0] + width, location[1] + height),
                    bmp,
                    { copyResult ->
                        if (copyResult == PixelCopy.SUCCESS) {
                            onReady(bmp)
                        } else {
                            onReady(drawFallback())
                        }
                    },
                    Handler(Looper.getMainLooper())
                )
            } catch (_: Exception) {
                onReady(drawFallback())
            }
        } else {
            onReady(drawFallback())
        }
    }

    private fun startCircleToSearch() {
        if (!prefs.isLoggedIn) {
            showAccountPromptForAi("Для использования «Обвести для поиска» войдите в аккаунт Luma ID.")
            return
        }
        closeSearchOverlay()
        captureScreen { screenBmp ->
            circleSearchView.reset()
            circleSearchView.screenshotBitmap = screenBmp
            circleSearchOverlay.visibility = View.VISIBLE
        }
    }

    private fun dismissCircleToSearch() {
        circleSearchOverlay.visibility = View.GONE
        circleSearchView.reset()
        circleSearchView.screenshotBitmap = null
    }

    private fun handleCircleSearchCapture(cropped: Bitmap) {
        dismissCircleToSearch()
        showAiSheet(initialImage = cropped, initialPrompt = "Что это? Подробно опиши, найди информацию и объясни выделенное.")
    }

    private fun uriToBitmap(uri: Uri): Bitmap? {
        return try {
            contentResolver.openInputStream(uri)?.use { stream ->
                val options = BitmapFactory.Options().apply {
                    inJustDecodeBounds = true
                }
                BitmapFactory.decodeStream(stream, null, options)
                var sampleSize = 1
                val maxDim = 1280
                while (options.outWidth / sampleSize > maxDim || options.outHeight / sampleSize > maxDim) {
                    sampleSize *= 2
                }
                contentResolver.openInputStream(uri)?.use { stream2 ->
                    val decodeOptions = BitmapFactory.Options().apply {
                        inSampleSize = sampleSize
                    }
                    BitmapFactory.decodeStream(stream2, null, decodeOptions)
                }
            }
        } catch (_: Exception) {
            null
        }
    }

    private fun bitmapToBase64(bitmap: Bitmap): String {
        val baos = ByteArrayOutputStream()
        bitmap.compress(Bitmap.CompressFormat.JPEG, 85, baos)
        return Base64.encodeToString(baos.toByteArray(), Base64.NO_WRAP)
    }

    // ===== LUMAAI BOTTOM SHEET (Non-blocking, smooth scrolling) =====
    private fun showAiSheet(initialImage: Bitmap? = null, initialPrompt: String? = null) {
        if (!prefs.isLoggedIn) {
            showAccountPromptForAi("Для использования LumaAI войдите в аккаунт Luma ID.")
            return
        }

        val dialog = BottomSheetDialog(this, R.style.Luma_BottomSheet)
        val view = layoutInflater.inflate(R.layout.sheet_ai, null)
        dialog.setContentView(view)
        configureBottomSheet(dialog)

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
        val attachmentBtn = view.findViewById<ImageButton>(R.id.aiAttachment)
        val sendBtn = view.findViewById<FrameLayout>(R.id.aiSendBtn)
        val btnClose = view.findViewById<FrameLayout>(R.id.btnAiClose)
        val btnNewChat = view.findViewById<FrameLayout>(R.id.btnAiNewChat)
        val modelPill = view.findViewById<TextView>(R.id.aiModelPill)

        // Circle to Search button in AI sheet
        view.findViewById<View>(R.id.btnAiCircleSearch)?.setOnClickListener {
            dialog.dismiss()
            startCircleToSearch()
        }
        view.findViewById<View>(R.id.cardActionCircleSearch)?.setOnClickListener {
            dialog.dismiss()
            startCircleToSearch()
        }

        // Auth banner
        val authBanner = view.findViewById<View>(R.id.aiAuthRequiredBanner)
        val btnLoginPrompt = view.findViewById<View>(R.id.btnAiLoginPrompt)
        if (!prefs.isLoggedIn) {
            authBanner?.visibility = View.VISIBLE
            btnLoginPrompt?.setOnClickListener {
                dialog.dismiss()
                showAccountSheet()
            }
        } else {
            authBanner?.visibility = View.GONE
        }

        var pendingImageBitmap: Bitmap? = initialImage
        var pendingImageBase64: String? = initialImage?.let { bitmapToBase64(it) }

        if (pendingImageBitmap != null) {
            attachmentBtn.setColorFilter(0xFF8B5CF6.toInt())
        }

        onAiImagePicked = { uri ->
            val bmp = uriToBitmap(uri)
            if (bmp != null) {
                pendingImageBitmap = bmp
                pendingImageBase64 = bitmapToBase64(bmp)
                attachmentBtn.setColorFilter(0xFF8B5CF6.toInt())
                Toast.makeText(this, "Изображение прикреплено", Toast.LENGTH_SHORT).show()
            }
        }
        attachmentBtn.setOnClickListener {
            pickAiImageLauncher.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly))
        }

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

        val quotaBadge = view.findViewById<TextView>(R.id.aiQuotaBadge)
        fun updateQuotaBadge() {
            if (prefs.isUnlimitedAi || prefs.userRole.equals("admin", ignoreCase = true)) {
                quotaBadge.text = "✨ ∞"
                quotaBadge.setTextColor(0xFF7EE787.toInt())
            } else {
                val rem = prefs.getRemainingAiQuota()
                quotaBadge.text = "$rem/15"
                quotaBadge.setTextColor(if (rem > 3) 0xFFFFB86C.toInt() else 0xFFFF6B6B.toInt())
            }
        }
        updateQuotaBadge()
        quotaBadge.setOnClickListener { showAccountSheet() }

        fun sendUserQuery(prompt: String) {
            if (!prefs.isLoggedIn) {
                dialog.dismiss()
                showAccountPromptForAi("Для общения с LumaAI войдите в аккаунт Luma ID.")
                return
            }
            val imgBmp = pendingImageBitmap
            val imgB64 = pendingImageBase64
            pendingImageBitmap = null
            pendingImageBase64 = null
            attachmentBtn.clearColorFilter()

            sendAiMessage(
                text = prompt,
                input = input,
                recycler = recycler,
                emptyBox = emptyBox,
                imageBitmap = imgBmp,
                imageBase64 = imgB64,
                onQuotaChanged = { updateQuotaBadge() }
            )
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

        if (!initialPrompt.isNullOrBlank()) {
            sendUserQuery(initialPrompt)
        }
    }

    private fun sendAiMessage(
        text: String,
        input: EditText,
        recycler: RecyclerView,
        emptyBox: View,
        imageBitmap: Bitmap? = null,
        imageBase64: String? = null,
        onQuotaChanged: (() -> Unit)? = null
    ) {
        if (!prefs.isLoggedIn) {
            showAccountPromptForAi("Для использования LumaAI войдите в аккаунт Luma ID.")
            return
        }
        if (aiStreaming) return

        // Quota check (15 free requests per day unless unlimited)
        if (!prefs.consumeAiQuota()) {
            input.text?.clear()
            hideKeyboard()
            emptyBox.visibility = View.GONE
            val userMsg = AiMessage("user", text, imageBase64, imageBitmap)
            aiAdapter?.addMessage(userMsg)
            val limitMsg = AiMessage(
                "assistant",
                "⚠️ **Дневной лимит запросов к LumaAI исчерпан (0/15)**\n\nВы использовали все 15 бесплатных запросов на сегодня. Для получения безлимитного доступа обратитесь к создателю в чате поддержки или войдите в Luma ID."
            )
            aiAdapter?.addMessage(limitMsg)
            recycler.smoothScrollToPosition(aiMessages.size - 1)
            onQuotaChanged?.invoke()
            return
        }
        onQuotaChanged?.invoke()

        input.text?.clear()
        hideKeyboard()
        emptyBox.visibility = View.GONE

        val userMsg = AiMessage("user", text, imageBase64, imageBitmap)
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
                        val lastIdx = aiMessages.indexOfLast { it.role == "assistant" }
                        if (lastIdx >= 0 && aiMessages[lastIdx].content.isBlank()) {
                            aiMessages[lastIdx] = aiMessages[lastIdx].copy(content = "Готово.")
                            aiAdapter?.notifyItemChanged(lastIdx)
                        }
                        recycler.scrollToPosition(aiMessages.size - 1)
                    }
                },
                onError = { err ->
                    withContext(Dispatchers.Main) {
                        aiStreaming = false
                        aiAdapter?.appendToLastAssistant("\n\n*Ошибка: $err*")
                        recycler.scrollToPosition(aiMessages.size - 1)
                    }
                }
            )
        }
    }

    // ===== LUMA ID / ACCOUNT SHEET =====
    private fun showAccountSheet() {
        val dialog = BottomSheetDialog(this, R.style.Luma_BottomSheet)
        val view = layoutInflater.inflate(R.layout.sheet_account, null)
        dialog.setContentView(view)
        configureBottomSheet(dialog)

        val btnClose = view.findViewById<View>(R.id.btnAccountClose)
        val displayNameText = view.findViewById<TextView>(R.id.accountDisplayName)
        val emailText = view.findViewById<TextView>(R.id.accountEmailText)
        val statusBadge = view.findViewById<TextView>(R.id.accountStatusBadge)
        val quotaText = view.findViewById<TextView>(R.id.accountQuotaText)
        val quotaType = view.findViewById<TextView>(R.id.accountQuotaType)

        val loginForm = view.findViewById<View>(R.id.accountLoginForm)
        val inputEmail = view.findViewById<EditText>(R.id.accountInputEmail)
        val inputPassword = view.findViewById<EditText>(R.id.accountInputPassword)
        val btnLogin = view.findViewById<Button>(R.id.btnAccountLogin)
        val btnRegister = view.findViewById<Button>(R.id.btnAccountRegister)
        val authStatus = view.findViewById<TextView>(R.id.accountAuthStatus)

        val loggedInSection = view.findViewById<View>(R.id.accountLoggedInSection)
        val btnLogout = view.findViewById<Button>(R.id.btnAccountLogout)

        fun updateUi() {
            val isLoggedIn = prefs.isLoggedIn
            if (isLoggedIn) {
                displayNameText.text = prefs.displayName.ifBlank { prefs.email.substringBefore('@') }
                emailText.text = prefs.email
                val isAdmin = prefs.userRole.equals("admin", ignoreCase = true)
                if (isAdmin) {
                    statusBadge.text = "Создатель"
                    statusBadge.setTextColor(0xFFFF7EB3.toInt())
                } else if (prefs.isUnlimitedAi) {
                    statusBadge.text = "VIP • Безлимит"
                    statusBadge.setTextColor(0xFF7EE787.toInt())
                } else {
                    statusBadge.text = "Пользователь"
                    statusBadge.setTextColor(0xFFB490FF.toInt())
                }

                if (isAdmin || prefs.isUnlimitedAi) {
                    quotaText.text = "Безлимитный доступ (∞)"
                    quotaText.setTextColor(0xFF7EE787.toInt())
                    quotaType.text = "Безлимит"
                } else {
                    val rem = prefs.getRemainingAiQuota()
                    quotaText.text = "Осталось: $rem / 15 сегодня"
                    quotaText.setTextColor(0xFFFFB86C.toInt())
                    quotaType.text = "Базовый (15/день)"
                }

                loginForm.visibility = View.GONE
                loggedInSection.visibility = View.VISIBLE
            } else {
                displayNameText.text = "Вход в Luma ID"
                emailText.text = "Войдите со своим логином и паролем"
                statusBadge.text = "Не авторизован"
                statusBadge.setTextColor(0xFFA29DB8.toInt())

                val rem = prefs.getRemainingAiQuota()
                quotaText.text = "Осталось: $rem / 15 сегодня"
                quotaText.setTextColor(0xFFFFB86C.toInt())
                quotaType.text = "Базовый (15/день)"

                loginForm.visibility = View.VISIBLE
                loggedInSection.visibility = View.GONE
            }
            updateHomeAccountButton()
        }
        updateUi()

        btnClose.setOnClickListener { dialog.dismiss() }

        btnLogout.setOnClickListener {
            prefs.accessToken = ""
            prefs.refreshToken = ""
            prefs.userId = ""
            prefs.email = ""
            prefs.displayName = ""
            prefs.userRole = "user"
            prefs.isUnlimitedAi = false
            Toast.makeText(this, "Вы вышли из аккаунта", Toast.LENGTH_SHORT).show()
            updateUi()
        }

        btnLogin.setOnClickListener {
            val email = inputEmail.text.toString().trim()
            val pass = inputPassword.text.toString().trim()
            if (email.isBlank() || pass.isBlank()) {
                authStatus.text = "Введите email и пароль"
                authStatus.visibility = View.VISIBLE
                return@setOnClickListener
            }
            authStatus.visibility = View.GONE
            btnLogin.isEnabled = false
            btnLogin.text = "Вход..."

            lifecycleScope.launch(Dispatchers.IO) {
                try {
                    val body = JSONObject().apply {
                        put("email", email)
                        put("password", pass)
                    }
                    val req = Request.Builder()
                        .url("${LumaApp.SUPABASE_URL}/auth/v1/token?grant_type=password")
                        .header("apikey", LumaApp.SUPABASE_PUBLISHABLE_KEY)
                        .header("Content-Type", "application/json")
                        .post(body.toString().toRequestBody("application/json".toMediaType()))
                        .build()
                    val resp = OkHttpClient().newCall(req).execute()
                    val respBody = resp.body?.string() ?: ""

                    if (resp.isSuccessful) {
                        val json = JSONObject(respBody)
                        val token = json.optString("access_token")
                        val refresh = json.optString("refresh_token")
                        val userObj = json.optJSONObject("user")
                        val uid = userObj?.optString("id") ?: ""
                        val uemail = userObj?.optString("email") ?: email

                        prefs.accessToken = token
                        prefs.refreshToken = refresh
                        prefs.userId = uid
                        prefs.email = uemail

                        try {
                            val profReq = Request.Builder()
                                .url("${LumaApp.SUPABASE_URL}/rest/v1/profiles?id=eq.$uid&select=*")
                                .header("apikey", LumaApp.SUPABASE_PUBLISHABLE_KEY)
                                .header("Authorization", "Bearer $token")
                                .get()
                                .build()
                            val profResp = OkHttpClient().newCall(profReq).execute()
                            val profBody = profResp.body?.string() ?: ""
                            val profArr = JSONArray(profBody)
                            if (profArr.length() > 0) {
                                val prof = profArr.getJSONObject(0)
                                val dName = prof.optString("display_name", "")
                                if (dName.isNotBlank()) prefs.displayName = dName
                                val role = prof.optString("role", "user")
                                prefs.userRole = role
                                val isVip = prof.optBoolean("is_vip", false)
                                val unlimited = prof.optBoolean("unlimited_ai", false)
                                if (role.equals("admin", ignoreCase = true) || isVip || unlimited) {
                                    prefs.isUnlimitedAi = true
                                }
                            }
                        } catch (_: Exception) {}

                        withContext(Dispatchers.Main) {
                            btnLogin.isEnabled = true
                            btnLogin.text = "Войти"
                            Toast.makeText(this@MainActivity, "Добро пожаловать, ${prefs.displayName}!", Toast.LENGTH_SHORT).show()
                            updateUi()
                        }
                    } else {
                        withContext(Dispatchers.Main) {
                            btnLogin.isEnabled = true
                            btnLogin.text = "Войти"
                            authStatus.text = "Неверный логин или пароль"
                            authStatus.visibility = View.VISIBLE
                        }
                    }
                } catch (e: Exception) {
                    withContext(Dispatchers.Main) {
                        btnLogin.isEnabled = true
                        btnLogin.text = "Войти"
                        authStatus.text = "Ошибка подключения: ${e.message}"
                        authStatus.visibility = View.VISIBLE
                    }
                }
            }
        }

        btnRegister.setOnClickListener {
            val email = inputEmail.text.toString().trim()
            val pass = inputPassword.text.toString().trim()
            if (email.isBlank() || pass.length < 6) {
                authStatus.text = "Email и пароль (мин. 6 символов) обязательны"
                authStatus.visibility = View.VISIBLE
                return@setOnClickListener
            }
            authStatus.visibility = View.GONE
            btnRegister.isEnabled = false
            btnRegister.text = "Создание..."

            lifecycleScope.launch(Dispatchers.IO) {
                try {
                    val body = JSONObject().apply {
                        put("email", email)
                        put("password", pass)
                    }
                    val req = Request.Builder()
                        .url("${LumaApp.SUPABASE_URL}/auth/v1/signup")
                        .header("apikey", LumaApp.SUPABASE_PUBLISHABLE_KEY)
                        .header("Content-Type", "application/json")
                        .post(body.toString().toRequestBody("application/json".toMediaType()))
                        .build()
                    val resp = OkHttpClient().newCall(req).execute()
                    withContext(Dispatchers.Main) {
                        btnRegister.isEnabled = true
                        btnRegister.text = "Регистрация"
                        if (resp.isSuccessful) {
                            Toast.makeText(this@MainActivity, "Аккаунт создан! Проверьте почту или войдите", Toast.LENGTH_LONG).show()
                        } else {
                            authStatus.text = "Не удалось зарегистрировать: ошибка сервера"
                            authStatus.visibility = View.VISIBLE
                        }
                    }
                } catch (e: Exception) {
                    withContext(Dispatchers.Main) {
                        btnRegister.isEnabled = true
                        btnRegister.text = "Регистрация"
                        authStatus.text = "Ошибка: ${e.message}"
                        authStatus.visibility = View.VISIBLE
                    }
                }
            }
        }

        dialog.show()
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
        configureBottomSheet(dialog)

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
            lifecycleScope.launch {
                PageTools.translatePage(webView, "ru") { status ->
                    Toast.makeText(this@MainActivity, status, Toast.LENGTH_SHORT).show()
                }
            }
        }

        view.findViewById<LinearLayout>(R.id.menuCircleSearch)?.setOnClickListener {
            dialog.dismiss()
            if (!prefs.isLoggedIn) {
                showAccountPromptForAi("Для использования «Обвести для поиска» войдите в аккаунт Luma ID.")
            } else {
                startCircleToSearch()
            }
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

        view.findViewById<LinearLayout>(R.id.menuHistory)?.setOnClickListener {
            dialog.dismiss()
            openHistoryTab()
        }

        view.findViewById<LinearLayout>(R.id.menuDownload)?.setOnClickListener {
            dialog.dismiss()
            openDownloadsTab()
        }

        val menuTheme = view.findViewById<LinearLayout>(R.id.menuTheme)
        val menuThemeValue = view.findViewById<TextView>(R.id.menuThemeValue)
        val menuThemeIcon = view.findViewById<ImageView>(R.id.menuThemeIcon)
        if (prefs.appTheme == "light") {
            menuThemeValue?.text = "Светлая"
            menuThemeIcon?.setImageResource(R.drawable.ic_sun)
        } else {
            menuThemeValue?.text = "Тёмная"
            menuThemeIcon?.setImageResource(R.drawable.ic_moon)
        }
        menuTheme?.setOnClickListener {
            if (prefs.appTheme == "light") {
                prefs.appTheme = "dark"
                menuThemeValue?.text = "Тёмная"
                menuThemeIcon?.setImageResource(R.drawable.ic_moon)
            } else {
                prefs.appTheme = "light"
                menuThemeValue?.text = "Светлая"
                menuThemeIcon?.setImageResource(R.drawable.ic_sun)
            }
            applyAppTheme()
            Toast.makeText(this, if (prefs.appTheme == "light") "Светлая тема включена" else "Тёмная тема включена", Toast.LENGTH_SHORT).show()
            dialog.dismiss()
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
        configureBottomSheet(dialog)

        view.findViewById<View>(R.id.btnSettingsClose).setOnClickListener { dialog.dismiss() }

        view.findViewById<View>(R.id.btnSettingsHistory)?.setOnClickListener {
            dialog.dismiss()
            openHistoryTab()
        }

        view.findViewById<View>(R.id.btnSettingsDownloads)?.setOnClickListener {
            dialog.dismiss()
            openDownloadsTab()
        }

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

        // Theme selection
        val btnThemeDark = view.findViewById<TextView>(R.id.btnThemeDark)
        val btnThemeLight = view.findViewById<TextView>(R.id.btnThemeLight)
        val btnThemeSystem = view.findViewById<TextView>(R.id.btnThemeSystem)
        val themeBtns = mapOf("dark" to btnThemeDark, "light" to btnThemeLight, "system" to btnThemeSystem)

        fun updateThemeSelection(mode: String) {
            themeBtns.forEach { (m, btn) ->
                if (m == mode) {
                    btn?.setBackgroundResource(R.drawable.bg_surface_raised)
                    btn?.setTextColor(0xFFFFFFFF.toInt())
                } else {
                    btn?.background = null
                    btn?.setTextColor(0xFF716C82.toInt())
                }
            }
        }
        updateThemeSelection(prefs.appTheme)

        btnThemeDark?.setOnClickListener {
            prefs.appTheme = "dark"
            updateThemeSelection("dark")
            applyAppTheme()
        }
        btnThemeLight?.setOnClickListener {
            prefs.appTheme = "light"
            updateThemeSelection("light")
            applyAppTheme()
        }
        btnThemeSystem?.setOnClickListener {
            prefs.appTheme = "system"
            updateThemeSelection("system")
            applyAppTheme()
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
        configureBottomSheet(dialog)

        val recycler = view.findViewById<RecyclerView>(R.id.historyRecycler)
        val emptyText = view.findViewById<TextView>(R.id.historyEmpty)
        val searchInput = view.findViewById<EditText>(R.id.historySearchInput)
        val btnClear = view.findViewById<TextView>(R.id.btnHistoryClear)
        val btnClose = view.findViewById<View>(R.id.btnHistoryClose)

        recycler.layoutManager = LinearLayoutManager(this)

        val items = mutableListOf<HistoryEntry>()
        try {
            val arr = JSONArray(prefs.historyJson)
            for (i in 0 until arr.length()) {
                val obj = arr.getJSONObject(i)
                val u = obj.optString("url")
                val t = obj.optString("title")
                val ts = obj.optLong("ts", System.currentTimeMillis())
                if (u.isNotBlank()) {
                    items.add(HistoryEntry(url = u, title = t, timestamp = ts))
                }
            }
        } catch (_: Exception) {}

        if (items.isEmpty()) emptyText.visibility = View.VISIBLE

        val adapter = HistoryAdapter(items,
            isLight = prefs.appTheme == "light",
            onItemClick = { url ->
                dialog.dismiss()
                navigate(url)
            },
            onItemDelete = { entry ->
                try {
                    val arr = JSONArray(prefs.historyJson)
                    val newArr = JSONArray()
                    for (i in 0 until arr.length()) {
                        val item = arr.getJSONObject(i)
                        if (item.optString("url") != entry.url) {
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
        configureBottomSheet(dialog)

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
            val newArr = JSONArray()
            newArr.put(JSONObject().apply {
                put("url", url)
                put("title", title)
                put("ts", System.currentTimeMillis())
            })
            for (i in 0 until arr.length()) {
                val item = arr.getJSONObject(i)
                if (item.optString("url") != url && newArr.length() < 200) {
                    newArr.put(item)
                }
            }
            prefs.historyJson = newArr.toString()
            runOnUiThread {
                if (activeTab?.isHome == true) {
                    populateHomeHistory()
                }
                if (activeTab?.isHistory == true) {
                    loadHistoryTabData()
                }
            }
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
        configureBottomSheet(dialog)

        val recycler = view.findViewById<RecyclerView>(R.id.supportMessages)
        val input = view.findViewById<EditText>(R.id.supportInput)
        val sendBtn = view.findViewById<FrameLayout>(R.id.supportSendBtn)
        val attachBtn = view.findViewById<ImageButton>(R.id.supportAttachBtn)

        val attachmentBar = view.findViewById<LinearLayout>(R.id.supportAttachmentBar)
        val attachmentCount = view.findViewById<TextView>(R.id.supportAttachmentCount)
        val attachmentClearAll = view.findViewById<ImageButton>(R.id.supportAttachmentClearAll)
        val attachmentList = view.findViewById<LinearLayout>(R.id.supportAttachmentList)

        val pendingBitmaps = mutableListOf<Bitmap>()
        val pendingBase64 = mutableListOf<String>()

        fun renderAttachmentBar() {
            attachmentList.removeAllViews()
            if (pendingBitmaps.isEmpty()) {
                attachmentBar.visibility = View.GONE
                return
            }
            attachmentBar.visibility = View.VISIBLE
            attachmentCount.text = "Прикреплено фото: ${pendingBitmaps.size}"

            val density = resources.displayMetrics.density
            val thumbSize = (56 * density).toInt()
            val margin = (6 * density).toInt()

            pendingBitmaps.forEachIndexed { index, bmp ->
                val frame = FrameLayout(this).apply {
                    layoutParams = LinearLayout.LayoutParams(thumbSize, thumbSize).apply {
                        marginEnd = margin
                    }
                }
                val iv = ImageView(this).apply {
                    layoutParams = FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT)
                    scaleType = ImageView.ScaleType.CENTER_CROP
                    setImageBitmap(bmp)
                    setBackgroundResource(R.drawable.bg_surface_raised)
                }
                val closeBtn = ImageView(this).apply {
                    val closeSize = (20 * density).toInt()
                    layoutParams = FrameLayout.LayoutParams(closeSize, closeSize).apply {
                        gravity = Gravity.TOP or Gravity.END
                        topMargin = (2 * density).toInt()
                        rightMargin = (2 * density).toInt()
                    }
                    setImageResource(R.drawable.ic_close)
                    setBackgroundResource(R.drawable.bg_circle_badge)
                    setPadding((4 * density).toInt(), (4 * density).toInt(), (4 * density).toInt(), (4 * density).toInt())
                    setOnClickListener {
                        if (index < pendingBitmaps.size && index < pendingBase64.size) {
                            pendingBitmaps.removeAt(index)
                            pendingBase64.removeAt(index)
                            renderAttachmentBar()
                        }
                    }
                }
                frame.addView(iv)
                frame.addView(closeBtn)
                attachmentList.addView(frame)
            }
        }

        attachmentClearAll.setOnClickListener {
            pendingBitmaps.clear()
            pendingBase64.clear()
            renderAttachmentBar()
        }

        recycler.layoutManager = LinearLayoutManager(this).apply { stackFromEnd = true }
        val supportMessages = mutableListOf<SupportChatMessage>()
        val adapter = SupportChatAdapter(supportMessages) { urlOrB64, isBase64 ->
            if (!isBase64) {
                try {
                    startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(urlOrB64)))
                } catch (_: Exception) {}
            }
        }
        recycler.adapter = adapter

        adapter.addMessage(SupportChatMessage(
            isUser = false,
            text = "Привет! Я создатель Luma. Напиши любой вопрос или баг-репорт — отвечу прямо сюда!"
        ))

        attachBtn.setOnClickListener {
            onSupportImagesPicked = { uris ->
                for (uri in uris) {
                    val result = processImageUriToBase64(uri)
                    if (result != null) {
                        pendingBitmaps.add(result.first)
                        pendingBase64.add(result.second)
                    }
                }
                renderAttachmentBar()
            }
            try {
                pickSupportImagesLauncher.launch(
                    PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly)
                )
            } catch (_: Exception) {
                Toast.makeText(this, "Не удалось открыть галерею", Toast.LENGTH_SHORT).show()
            }
        }

        supportPollJob = lifecycleScope.launch {
            while (isActive) {
                val incoming = supportService.pollMessages()
                if (incoming.isNotEmpty()) {
                    withContext(Dispatchers.Main) {
                        for (msg in incoming) {
                            // Skip if this message is already shown
                            if (supportMessages.any { it.id == msg.id }) continue

                            // If server returned a user message matching our local optimistic message, link it
                            if (msg.isUser) {
                                val localMatch = supportMessages.find {
                                    it.isUser && it.id.startsWith("local_") && it.text == msg.text
                                }
                                if (localMatch != null) {
                                    localMatch.id = msg.id
                                    continue
                                }
                            }

                            // Otherwise add incoming message (creator reply or initial chat history)
                            adapter.addMessage(msg)
                            recycler.smoothScrollToPosition(supportMessages.size - 1)
                        }
                    }
                }
                delay(2500L)
            }
        }

        dialog.setOnDismissListener {
            supportPollJob?.cancel()
            onSupportImagesPicked = null
        }

        fun doSend() {
            val text = input.text.toString().trim()
            val toSendB64 = pendingBase64.toList()
            if (text.isBlank() && toSendB64.isEmpty()) return

            input.text?.clear()
            pendingBitmaps.clear()
            pendingBase64.clear()
            renderAttachmentBar()

            val localId = "local_" + UUID.randomUUID().toString()
            val userMsg = SupportChatMessage(
                id = localId,
                isUser = true,
                text = text,
                imageBase64List = toSendB64
            )
            adapter.addMessage(userMsg)
            recycler.smoothScrollToPosition(supportMessages.size - 1)

            val attachments = toSendB64.map { it to "image/jpeg" }
            lifecycleScope.launch {
                val serverMsgId = supportService.sendMessage(
                    text = text,
                    attachments = attachments
                )
                if (!serverMsgId.isNullOrBlank()) {
                    userMsg.id = serverMsgId
                }
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
        if (customView != null) {
            hideCustomView()
            return
        }
        if (::circleSearchOverlay.isInitialized && circleSearchOverlay.visibility == View.VISIBLE) {
            dismissCircleToSearch()
            return
        }
        if (::tabSwitcherOverlay.isInitialized && tabSwitcherOverlay.visibility == View.VISIBLE) {
            closeTabSwitcher()
            return
        }
        if (searchOverlay.visibility == View.VISIBLE) {
            closeSearchOverlay()
            return
        }
        if (readerOverlay.visibility == View.VISIBLE) {
            readerOverlay.visibility = View.GONE
            return
        }
        if (::historyDashboard.isInitialized && historyDashboard.visibility == View.VISIBLE) {
            showHome()
            return
        }
        if (::downloadsDashboard.isInitialized && downloadsDashboard.visibility == View.VISIBLE) {
            showHome()
            return
        }
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
