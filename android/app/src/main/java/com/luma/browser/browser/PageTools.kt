package com.luma.browser.browser

import android.webkit.WebView
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONArray
import org.json.JSONObject
import org.json.JSONTokener
import java.net.URLEncoder
import java.util.concurrent.TimeUnit
import kotlin.coroutines.resume

data class PageContext(
    val title: String,
    val url: String,
    val selection: String,
    val text: String
)

data class ReaderArticle(
    val title: String,
    val byline: String,
    val contentHtml: String,
    val text: String
)

/**
 * PageTools — scripts and tools injected into the active webview:
 * 1. Read context (title, selection, body text) for LumaAI
 * 2. Reader Mode extraction
 * 3. Page Translation (Direct DOM TreeWalker + Google GTX batch translation, CSP/CORS immune)
 */
object PageTools {

    private val translateHttpClient = OkHttpClient.Builder()
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(15, TimeUnit.SECONDS)
        .build()

    suspend fun extractPageContext(webView: WebView): PageContext = suspendCancellableCoroutine { cont ->
        val js = """
            (function() {
                var sel = window.getSelection() ? window.getSelection().toString() : '';
                var bodyText = document.body ? document.body.innerText.substring(0, 8000) : '';
                return JSON.stringify({
                    title: document.title || '',
                    url: window.location.href || '',
                    selection: sel.substring(0, 2000),
                    text: bodyText
                });
            })();
        """.trimIndent()

        webView.evaluateJavascript(js) { result ->
            try {
                if (result != null && result != "null") {
                    val raw = if (result.startsWith("\"") && result.endsWith("\"")) {
                        JSONTokener(result).nextValue().toString()
                    } else result
                    val json = JSONObject(raw)
                    cont.resume(PageContext(
                        title = json.optString("title", ""),
                        url = json.optString("url", ""),
                        selection = json.optString("selection", ""),
                        text = json.optString("text", "")
                    ))
                } else {
                    cont.resume(PageContext(webView.title ?: "", webView.url ?: "", "", ""))
                }
            } catch (_: Exception) {
                cont.resume(PageContext(webView.title ?: "", webView.url ?: "", "", ""))
            }
        }
    }

    suspend fun extractReaderContent(webView: WebView): ReaderArticle = suspendCancellableCoroutine { cont ->
        val js = """
            (function() {
                var article = document.querySelector('article') || document.querySelector('main') || document.body;
                if (!article) return JSON.stringify({ title: document.title, byline: '', contentHtml: '', text: '' });

                var clone = article.cloneNode(true);
                clone.querySelectorAll('nav, footer, aside, header, script, style, iframe, [role="complementary"]').forEach(function(el) {
                    el.remove();
                });

                var author = '';
                var authorMeta = document.querySelector('meta[name="author"]') || document.querySelector('[rel="author"]');
                if (authorMeta) author = authorMeta.content || authorMeta.innerText || '';

                return JSON.stringify({
                    title: document.title || 'Статья',
                    byline: author,
                    contentHtml: clone.innerHTML.substring(0, 50000),
                    text: clone.innerText.substring(0, 15000)
                });
            })();
        """.trimIndent()

        webView.evaluateJavascript(js) { result ->
            try {
                if (result != null && result != "null") {
                    val raw = if (result.startsWith("\"") && result.endsWith("\"")) {
                        JSONTokener(result).nextValue().toString()
                    } else result
                    val json = JSONObject(raw)
                    cont.resume(ReaderArticle(
                        title = json.optString("title", "Статья"),
                        byline = json.optString("byline", ""),
                        contentHtml = json.optString("contentHtml", ""),
                        text = json.optString("text", "")
                    ))
                } else {
                    cont.resume(ReaderArticle(webView.title ?: "Статья", webView.url ?: "", "", ""))
                }
            } catch (_: Exception) {
                cont.resume(ReaderArticle(webView.title ?: "Статья", webView.url ?: "", "", ""))
            }
        }
    }

    /**
     * Translates page DOM text nodes incrementally using Google's GTX translation engine.
     * Operates without external script dependencies, bypassing CSP and CORS limitations.
     */
    suspend fun translatePage(
        webView: WebView,
        targetLang: String = "ru",
        onStatus: ((String) -> Unit)? = null
    ) = withContext(Dispatchers.Main) {
        val collectJs = """
            (function() {
                if (!document.body) return JSON.stringify([]);
                window.__lumaTranslatedNodes = window.__lumaTranslatedNodes || new WeakSet();
                const nodes = [];
                const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, {
                    acceptNode: function(n) {
                        const p = n.parentElement;
                        const t = (n.nodeValue || '').trim();
                        if (!p || !t || t.length < 2 || ['SCRIPT','STYLE','NOSCRIPT','TEXTAREA','CODE','PRE','OPTION'].includes(p.tagName) || p.closest('[contenteditable="true"],.notranslate,[translate="no"]') || window.__lumaTranslatedNodes.has(n)) {
                            return NodeFilter.FILTER_REJECT;
                        }
                        return NodeFilter.FILTER_ACCEPT;
                    }
                });
                let n;
                while ((n = walker.nextNode()) && nodes.length < 1500) {
                    nodes.push(n);
                }
                window.__lumaTranslationNodes = nodes;
                return JSON.stringify(nodes.map(n => (n.nodeValue || '').trim()));
            })();
        """.trimIndent()

        val rawTextsJson = suspendCancellableCoroutine<String?> { cont ->
            webView.evaluateJavascript(collectJs) { res ->
                cont.resume(res)
            }
        }

        if (rawTextsJson.isNullOrBlank() || rawTextsJson == "null" || rawTextsJson == "\"[]\"") {
            onStatus?.invoke("Текст для перевода не найден")
            return@withContext
        }

        val jsonStr = if (rawTextsJson.startsWith("\"") && rawTextsJson.endsWith("\"")) {
            try { JSONTokener(rawTextsJson).nextValue().toString() } catch (_: Exception) { rawTextsJson }
        } else rawTextsJson

        val textsArray = try { JSONArray(jsonStr) } catch (_: Exception) { return@withContext }
        val texts = (0 until textsArray.length()).map { textsArray.getString(it) }
        if (texts.isEmpty()) return@withContext

        onStatus?.invoke("Перевод фрагментов (${texts.size})...")

        // Batch texts into batches of ~25 items or ~2000 chars
        val batches = mutableListOf<List<Pair<Int, String>>>()
        var currentBatch = mutableListOf<Pair<Int, String>>()
        var currentLen = 0

        texts.forEachIndexed { index, text ->
            if (currentBatch.isNotEmpty() && (currentBatch.size >= 25 || currentLen + text.length > 2000)) {
                batches.add(currentBatch)
                currentBatch = mutableListOf()
                currentLen = 0
            }
            currentBatch.add(index to text)
            currentLen += text.length
        }
        if (currentBatch.isNotEmpty()) batches.add(currentBatch)

        var totalTranslated = 0

        withContext(Dispatchers.IO) {
            batches.forEach { batch ->
                val combinedText = batch.joinToString("\n\n") { it.second }
                val translatedCombined = try {
                    val formBody = okhttp3.FormBody.Builder()
                        .add("q", combinedText)
                        .build()
                    val url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=$targetLang&dt=t"
                    val req = Request.Builder()
                        .url(url)
                        .post(formBody)
                        .header("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)")
                        .build()
                    val resp = translateHttpClient.newCall(req).execute()
                    if (!resp.isSuccessful) return@forEach
                    val respStr = resp.body?.string() ?: ""
                    val root = JSONArray(respStr)
                    val sentences = root.getJSONArray(0)
                    val sb = StringBuilder()
                    for (i in 0 until sentences.length()) {
                        val sent = sentences.getJSONArray(i)
                        sb.append(sent.getString(0))
                    }
                    sb.toString()
                } catch (_: Exception) { null }

                if (!translatedCombined.isNullOrBlank()) {
                    val translatedParts = translatedCombined.split("\n\n")
                    val applyPayload = JSONArray()
                    batch.forEachIndexed { i, (origIdx, _) ->
                        val trans = if (i < translatedParts.size) translatedParts[i] else null
                        if (!trans.isNullOrBlank()) {
                            totalTranslated++
                            applyPayload.put(JSONObject().apply {
                                put("index", origIdx)
                                put("text", trans.trim())
                            })
                        }
                    }

                    withContext(Dispatchers.Main) {
                        val applyJs = """
                            (function() {
                                const updates = ${applyPayload.toString()};
                                if (!window.__lumaTranslationNodes) return;
                                updates.forEach(u => {
                                    const node = window.__lumaTranslationNodes[u.index];
                                    if (node && node.parentElement) {
                                        node.nodeValue = u.text;
                                        if (window.__lumaTranslatedNodes) window.__lumaTranslatedNodes.add(node);
                                    }
                                });
                            })();
                        """.trimIndent()
                        webView.evaluateJavascript(applyJs, null)
                    }
                }
            }
        }
        if (totalTranslated > 0) {
            onStatus?.invoke("Страница переведена ($totalTranslated фрагментов)")
        } else {
            onStatus?.invoke("Не удалось перевести страницу")
        }
    }
}
