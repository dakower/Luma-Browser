package com.luma.browser.browser

import android.webkit.WebView
import kotlinx.coroutines.suspendCancellableCoroutine
import org.json.JSONObject
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
 * 3. Page Translation
 */
object PageTools {

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
                    val unescaped = JSONObject(if (result.startsWith("\"") && result.endsWith("\"")) {
                        // evaluated JSON was stringified twice
                        org.json.JSONTokener(result).nextValue().toString()
                    } else result)
                    cont.resume(PageContext(
                        title = unescaped.optString("title"),
                        url = unescaped.optString("url"),
                        selection = unescaped.optString("selection"),
                        text = unescaped.optString("text")
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
                var title = (document.querySelector('h1') ? document.querySelector('h1').innerText : '') || document.title;
                var paragraphs = article ? Array.from(article.querySelectorAll('p')).map(p => p.outerHTML).join('') : '';
                return JSON.stringify({
                    title: title,
                    byline: window.location.hostname,
                    contentHtml: paragraphs || (article ? article.innerHTML : ''),
                    text: article ? article.innerText : ''
                });
            })();
        """.trimIndent()

        webView.evaluateJavascript(js) { result ->
            try {
                if (result != null && result != "null") {
                    val raw = if (result.startsWith("\"") && result.endsWith("\"")) {
                        org.json.JSONTokener(result).nextValue().toString()
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

    fun injectTranslation(webView: WebView, targetLang: String = "ru") {
        val js = """
            (function() {
                if (window._lumaTranslating) return;
                window._lumaTranslating = true;
                var script = document.createElement('script');
                script.type = 'text/javascript';
                script.src = '//translate.google.com/translate_a/element.js?cb=googleTranslateElementInit';
                document.head.appendChild(script);
                window.googleTranslateElementInit = function() {
                    new google.translate.TranslateElement({
                        pageLanguage: 'auto',
                        includedLanguages: '$targetLang',
                        layout: google.translate.TranslateElement.InlineLayout.SIMPLE,
                        autoDisplay: false
                    }, 'google_translate_element');
                };
            })();
        """.trimIndent()
        webView.evaluateJavascript(js, null)
    }
}
