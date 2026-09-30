using Luma.Core;
using Luma.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Cursors = System.Windows.Input.Cursors;
using DataObject = System.Windows.DataObject;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using IDataObject = System.Windows.IDataObject;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;
using IconPath = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Size = System.Windows.Size;
using TextBox = System.Windows.Controls.TextBox;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace Luma;

public partial class MainWindow
{
    private async Task OpenSettingsAsync() { RecordTesterAction("Открыты настройки"); await OpenInternalPageAsync("luma://settings", "Настройки Luma", InternalPages.Settings(_state)); }
    private async Task OpenAboutAsync()
    {
        await OpenSettingsAsync(); await Task.Delay(140);
        var tab = CurrentTab;
        if (tab?.IsInternal == true && tab.InternalPageKind == "luma://settings" && tab.ActiveView.CoreWebView2 is not null)
            await Script(tab.ActiveView, "document.querySelector('[data-page=about]')?.click()");
    }
    private async Task OpenImportAsync() => await OpenInternalPageAsync("luma://import", "Импорт из Chrome", InternalPages.Import());

    private static bool TryParseLumaSearchUrl(string url, out string query, out string mode)
    {
        query = ""; mode = "all";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "luma" || uri.Host != "search") return false;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('='); if (eq < 0) continue;
            string value; try { value = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' ')); } catch { continue; }
            if (pair[..eq].Equals("q", StringComparison.OrdinalIgnoreCase)) query = value;
            else if (pair[..eq].Equals("mode", StringComparison.OrdinalIgnoreCase)) mode = value;
        }
        if (mode is not ("ai" or "all" or "images" or "shopping" or "video" or "shorts" or "news")) mode = "all";
        return !string.IsNullOrWhiteSpace(query);
    }

    private async Task<BrowserTab> OpenLumaSearchTabAsync(string query, string mode = "all", bool activate = true)
    {
        query = (query ?? "").Trim();
        var url = "luma://search?q=" + Uri.EscapeDataString(query) + "&mode=" + Uri.EscapeDataString(mode);
        var tab = new BrowserTab { FullUrl = url, Title = query.Length > 0 ? query + " — Luma Search" : "Luma Search", IsInternal = true, InternalPageKind = url };
        Tabs.Add(tab); if (activate) CurrentTab = tab;
        await ConfigureLumaSearchTabAsync(tab, query, mode);
        RenderPanes(); FilterTabs(); UpdateChrome();
        return tab;
    }

    private async Task ConfigureLumaSearchTabAsync(BrowserTab tab, string query, string mode)
    {
        try
        {
            await tab.View.EnsureCoreWebView2Async(await BrowserEnvironmentAsync());
            var web = tab.View.CoreWebView2;
            web.Settings.AreDefaultContextMenusEnabled = false; web.Settings.IsStatusBarEnabled = false;
            AttachWebInterfaceLocalization(tab.View);
            await web.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScripts.Scrollbars(CurrentAccentCss()));
            await web.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScripts.ContextMenu("primary"));
            web.WebMessageReceived += (_, e) => ReceiveWebMessage(tab, tab.View, e.WebMessageAsJson);
            web.NewWindowRequested += (_, e) => { e.Handled = true; var target = e.Uri; Dispatcher.BeginInvoke(async () => await AddTabAsync(target)); };
            web.NavigateToString(InternalPages.Search(query, mode, HasTestCenterAccess));
        }
        catch (Exception ex) { App.Log(ex); ShowToast("Не удалось открыть Luma Search", ex.Message, true); }
    }

    private async Task NavigateFromLumaSearchAsync(BrowserTab tab, string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
        var searchUrl = tab.InternalPageKind;
        tab.BackFallbackInternalUrl = searchUrl;
        tab.IsInternal = false;
        tab.InternalPageKind = "";
        tab.FullUrl = target;
        tab.Title = Domain(target);
        tab.ActivePane = "primary";

        // A Luma Search page has a deliberately minimal WebView configuration. Reusing that
        // instance for a real website skipped normal navigation/error handlers and allowed the
        // branded Microsoft Edge error document to leak through. Promote the result into a fully
        // configured site view instead, so every failed request is replaced by Luma's page.
        var siteView = tab.ReplacePrimaryView();
        if (ReferenceEquals(CurrentTab, tab)) RenderPanes();
        UpdateChrome(); FilterTabs(); Save();
        await ConfigureViewAsync(tab, siteView, target);
        if (ReferenceEquals(CurrentTab, tab)) { RenderPanes(); UpdateChrome(); }
    }

    private async Task RestoreInternalBackTargetAsync(BrowserTab tab)
    {
        var target = tab.BackFallbackInternalUrl;
        if (!TryParseLumaSearchUrl(target, out var query, out var mode) || tab.ActiveView.CoreWebView2 is null) return;
        tab.BackFallbackInternalUrl = "";
        tab.IsInternal = true;
        tab.InternalPageKind = target;
        tab.FullUrl = target;
        tab.Title = query + " — Luma Search";
        tab.ActiveView.CoreWebView2.NavigateToString(InternalPages.Search(query, mode, HasTestCenterAccess));
        UpdateChrome(); FilterTabs(); Save();
        await Task.CompletedTask;
    }

    private sealed class LumaSearchItem
    {
        public string title { get; init; } = "";
        public string url { get; init; } = "";
        public string snippet { get; init; } = "";
        public string displayUrl { get; init; } = "";
        public string image { get; init; } = "";
        public string thumbnail { get; init; } = "";
    }

    private sealed class LumaAiSearchResult
    {
        public string answer { get; init; } = "";
        public string error { get; init; } = "";
        public List<LumaSearchItem> results { get; init; } = [];
        public LumaSearchItem[] sources { get; init; } = [];
    }

    private sealed record LumaSearchCacheEntry(DateTimeOffset CreatedAt, List<LumaSearchItem> Results);
    private static readonly ConcurrentDictionary<string, LumaSearchCacheEntry> LumaSearchCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient LumaSearchHttp = CreateLumaSearchHttp();

    private static HttpClient CreateLumaSearchHttp()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(7),
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(14) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(ChromeUserAgent);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en;q=0.7");
        return http;
    }

    private static string SearchPlain(string value)
    {
        var noTags = Regex.Replace(value ?? "", "<[^>]+>", " ", RegexOptions.Singleline);
        return Regex.Replace(WebUtility.HtmlDecode(noTags), @"\s+", " ").Trim();
    }

    private static string SearchUrl(string value)
    {
        if (!Uri.TryCreate(WebUtility.HtmlDecode(value ?? ""), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return "";
        var host = uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
        // Only block the search engine home/results pages themselves, not every subdomain.
        // This allows Google Docs, Google Drive, Google Maps, Bing Maps etc. to appear in results.
        var isSearchRedirect =
            (host == "google.com" && (uri.AbsolutePath.TrimEnd('/') == "/url" || uri.AbsolutePath.TrimEnd('/') == "/search")) ||
            (host == "bing.com" && uri.AbsolutePath.StartsWith("/search", StringComparison.OrdinalIgnoreCase)) ||
            (host == "duckduckgo.com" && uri.AbsolutePath.TrimEnd('/') == "/l") ||
            (host == "duckduckgo.com" && uri.AbsolutePath.TrimEnd('/') == "") ||
            uri.Query.Contains("uddg=", StringComparison.OrdinalIgnoreCase);
        if (isSearchRedirect) return "";
        return uri.AbsoluteUri;
    }

    private static string SearchMediaUrl(string value)
    {
        if (!Uri.TryCreate(WebUtility.HtmlDecode(value ?? ""), UriKind.Absolute, out var uri) || uri.Scheme != "https") return "";
        return uri.AbsoluteUri;
    }

    private static LumaSearchItem? SearchItem(string title, string url, string snippet, string image = "", string thumbnail = "")
    {
        url = SearchUrl(url); image = string.IsNullOrWhiteSpace(image) ? "" : SearchMediaUrl(image); thumbnail = string.IsNullOrWhiteSpace(thumbnail) ? "" : SearchMediaUrl(thumbnail);
        title = SearchPlain(title); if (url.Length == 0 || title.Length == 0) return null;
        return new LumaSearchItem { title = title, url = url, snippet = SearchPlain(snippet), displayUrl = new Uri(url).Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase), image = image, thumbnail = thumbnail };
    }

    private static async Task<string> DownloadSearchTextAsync(string url, CancellationToken token)
    {
        try
        {
            using var response = await LumaSearchHttp.GetAsync(url, HttpCompletionOption.ResponseContentRead, token);
            if (!response.IsSuccessStatusCode) return "";
            var text = await response.Content.ReadAsStringAsync(token);
            return text.Length > 2_500_000 ? text[..2_500_000] : text;
        }
        catch { return ""; }
    }

    private static async Task<List<LumaSearchItem>> SearchRssAsync(string query, bool news, CancellationToken token)
    {
        var endpoint = news ? "https://www.bing.com/news/search?format=rss&mkt=ru-RU&q=" : "https://www.bing.com/search?format=rss&mkt=ru-RU&q=";
        var xml = await DownloadSearchTextAsync(endpoint + Uri.EscapeDataString(query), token);
        var output = new List<LumaSearchItem>();
        if (xml.Length == 0) return output;
        try
        {
            var document = XDocument.Parse(xml);
            foreach (var node in document.Descendants("item").Take(18))
            {
                var item = SearchItem(node.Element("title")?.Value ?? "", node.Element("link")?.Value ?? "", node.Element("description")?.Value ?? "");
                if (item is not null) output.Add(item);
            }
        }
        catch { }
        return output;
    }

    private static async Task<List<LumaSearchItem>> SearchLiteAsync(string query, CancellationToken token)
    {
        var html = await DownloadSearchTextAsync("https://lite.duckduckgo.com/lite/?q=" + Uri.EscapeDataString(query), token);
        var output = new List<LumaSearchItem>();
        foreach (Match match in Regex.Matches(html, "<a[^>]+href=[\\\"']([^\\\"']+)[\\\"'][^>]*>([\\s\\S]*?)</a>", RegexOptions.IgnoreCase))
        {
            var raw = WebUtility.HtmlDecode(match.Groups[1].Value);
            try
            {
                var parsed = new Uri(new Uri("https://lite.duckduckgo.com"), raw);
                var redirect = parsed.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Split('=', 2)).FirstOrDefault(part => part.Length == 2 && part[0] == "uddg");
                if (redirect is not null) raw = Uri.UnescapeDataString(redirect[1].Replace('+', ' '));
            }
            catch { }
            var after = html.Substring(match.Index + match.Length, Math.Min(1100, html.Length - match.Index - match.Length));
            var snippetMatch = Regex.Match(after, "<(?:td|div)[^>]*class=[\\\"'][^\\\"']*(?:result-snippet|snippet)[^\\\"']*[\\\"'][^>]*>([\\s\\S]*?)</(?:td|div)>", RegexOptions.IgnoreCase);
            var item = SearchItem(match.Groups[2].Value, raw, snippetMatch.Success ? snippetMatch.Groups[1].Value : "");
            if (item is not null) output.Add(item);
            if (output.Count >= 16) break;
        }
        return output;
    }

    private static async Task<List<LumaSearchItem>> SearchWebHtmlAsync(string query, CancellationToken token)
    {
        var html = await DownloadSearchTextAsync("https://www.bing.com/search?setlang=ru&cc=ru&count=30&q=" + Uri.EscapeDataString(query), token);
        var output = new List<LumaSearchItem>();
        foreach (Match block in Regex.Matches(html, "<li[^>]*class=[\\\"'][^\\\"']*\\bb_algo\\b[^\\\"']*[\\\"'][^>]*>([\\s\\S]*?)</li>", RegexOptions.IgnoreCase))
        {
            var link = Regex.Match(block.Groups[1].Value, "<h2[^>]*>[\\s\\S]*?<a[^>]+href=[\\\"']([^\\\"']+)[\\\"'][^>]*>([\\s\\S]*?)</a>", RegexOptions.IgnoreCase);
            if (!link.Success) continue;
            var snippet = Regex.Match(block.Groups[1].Value, "<p[^>]*>([\\s\\S]*?)</p>", RegexOptions.IgnoreCase);
            var item = SearchItem(link.Groups[2].Value, link.Groups[1].Value, snippet.Success ? snippet.Groups[1].Value : "");
            if (item is not null) output.Add(item);
            if (output.Count >= 24) break;
        }
        return output;
    }

    private static string SearchJsonText(string value)
    {
        try { return JsonSerializer.Deserialize<string>("\"" + value + "\"") ?? ""; }
        catch { return SearchPlain(value.Replace("\\u0026", "&", StringComparison.OrdinalIgnoreCase)); }
    }

    private static async Task<List<LumaSearchItem>> SearchYouTubeAsync(string query, CancellationToken token)
    {
        var html = await DownloadSearchTextAsync("https://www.youtube.com/results?hl=ru&search_query=" + Uri.EscapeDataString(query), token);
        var output = new List<LumaSearchItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(html, "\\\"videoRenderer\\\"\\s*:\\s*\\x7B[\\s\\S]{0,700}?\\\"videoId\\\"\\s*:\\s*\\\"([A-Za-z0-9_-]{6,})\\\"[\\s\\S]{0,2200}?\\\"title\\\"\\s*:\\s*\\x7B[\\s\\S]{0,500}?\\\"text\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"\\\\])+)\\\"", RegexOptions.IgnoreCase))
        {
            var id = match.Groups[1].Value;
            if (!seen.Add(id)) continue;
            var item = SearchItem(SearchJsonText(match.Groups[2].Value), "https://www.youtube.com/watch?v=" + id, "Видео по запросу «" + query + "»", "", "https://i.ytimg.com/vi/" + id + "/hqdefault.jpg");
            if (item is not null) output.Add(item);
            if (output.Count >= 18) break;
        }
        foreach (Match match in Regex.Matches(html, "\\\"channelRenderer\\\"\\s*:\\s*\\x7B[\\s\\S]{0,900}?\\\"channelId\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"[\\s\\S]{0,1700}?\\\"title\\\"\\s*:\\s*\\x7B[\\s\\S]{0,400}?\\\"(?:simpleText|text)\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"\\\\])+)\\\"", RegexOptions.IgnoreCase))
        {
            var id = match.Groups[1].Value;
            if (!seen.Add("channel:" + id)) continue;
            var item = SearchItem(SearchJsonText(match.Groups[2].Value), "https://www.youtube.com/channel/" + id, "YouTube-канал по запросу «" + query + "»");
            if (item is not null) output.Insert(0, item);
            if (output.Count >= 22) break;
        }
        return output;
    }

    private static async Task<List<LumaSearchItem>> SearchImagesAsync(string query, CancellationToken token)
    {
        var html = await DownloadSearchTextAsync("https://www.bing.com/images/search?form=HDRSC2&q=" + Uri.EscapeDataString(query), token);
        var output = new List<LumaSearchItem>();
        foreach (Match match in Regex.Matches(html, "<a[^>]+class=[\\\"'][^\\\"']*iusc[^\\\"']*[\\\"'][^>]+m=[\\\"']([^\\\"']+)[\\\"'][^>]*>", RegexOptions.IgnoreCase))
        {
            try
            {
                using var metadata = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[1].Value));
                var root = metadata.RootElement;
                string S(string name) => root.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
                var item = SearchItem(S("t").Length > 0 ? S("t") : S("desc"), S("purl").Length > 0 ? S("purl") : S("surl"), "", S("murl"), S("turl"));
                if (item is not null && (item.image.Length > 0 || item.thumbnail.Length > 0)) output.Add(item);
            }
            catch { }
            if (output.Count >= 18) break;
        }
        return output;
    }

    private static async Task<List<LumaSearchItem>> SearchVideosAsync(string query, CancellationToken token)
    {
        var html = await DownloadSearchTextAsync("https://www.bing.com/videos/search?FORM=HDRSC4&q=" + Uri.EscapeDataString(query), token);
        var output = new List<LumaSearchItem>();
        foreach (Match match in Regex.Matches(html, "<[^>]*\\bm\\s*=\\s*([\\\"'])([\\s\\S]*?)\\1[^>]*>", RegexOptions.IgnoreCase))
        {
            try
            {
                using var metadata = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[2].Value));
                var root = metadata.RootElement;
                string S(params string[] names)
                {
                    foreach (var name in names)
                        if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                            return value.GetString() ?? "";
                    return "";
                }
                var title = S("t", "title", "tt");
                var pageUrl = S("purl", "url", "surl", "murl");
                var image = S("imgurl", "imageUrl", "poster");
                var thumbnail = S("turl", "thumbnailUrl", "thUrl", "thumb");
                var item = SearchItem(title, pageUrl, S("desc", "description", "caption"), image, thumbnail);
                if (item is not null) output.Add(WithVideoThumbnail(item));
            }
            catch { }
            if (output.Count >= 24) break;
        }
        return output;
    }

    private static async Task<List<LumaSearchItem>> SearchReaderAsync(string query, CancellationToken token)
    {
        var target = "http://www.google.com/search?q=" + Uri.EscapeDataString(query) + "&hl=ru";
        var markdown = await DownloadSearchTextAsync("https://r.jina.ai/" + target, token);
        var output = new List<LumaSearchItem>();
        foreach (Match match in Regex.Matches(markdown, "\\[([^]\\r\\n]{2,220})\\]\\((https?://[^)\\s]+)\\)"))
        {
            var around = markdown.Substring(Math.Max(0, match.Index - 180), Math.Min(markdown.Length - Math.Max(0, match.Index - 180), match.Length + 520));
            var item = SearchItem(match.Groups[1].Value, match.Groups[2].Value, around.Replace(match.Value, ""));
            if (item is not null) output.Add(item);
            if (output.Count >= 16) break;
        }
        return output;
    }

    // Google dorks are sent unchanged through the Google reader endpoint so
    // operators such as site:, filetype:, intitle: and quoted phrases keep their semantics.
    private static Task<List<LumaSearchItem>> SearchGoogleDorkAsync(string query, CancellationToken token)
        => SearchReaderAsync(query, token);

    private static async Task<List<LumaSearchItem>> SearchWikipediaAsync(string query, CancellationToken token)
    {
        var json = await DownloadSearchTextAsync("https://ru.wikipedia.org/w/api.php?action=query&list=search&format=json&utf8=1&srlimit=12&srsearch=" + Uri.EscapeDataString(query), token);
        var output = new List<LumaSearchItem>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("query", out var queryNode) || !queryNode.TryGetProperty("search", out var items)) return output;
            foreach (var node in items.EnumerateArray())
            {
                var title = node.TryGetProperty("title", out var titleNode) ? titleNode.GetString() ?? "" : "";
                var snippet = node.TryGetProperty("snippet", out var snippetNode) ? snippetNode.GetString() ?? "" : "";
                var pageId = node.TryGetProperty("pageid", out var pageNode) && pageNode.TryGetInt64(out var id) ? id : 0;
                var item = SearchItem(title, pageId > 0 ? "https://ru.wikipedia.org/?curid=" + pageId : "", snippet);
                if (item is not null) output.Add(item);
            }
        }
        catch { }
        return output;
    }

    private static async Task<List<LumaSearchItem>> SearchServerAsync(string query, string mode, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Authentication.SupabaseOptions.ProjectUrl + "/functions/v1/luma-search")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { query, mode }), Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("apikey", Authentication.SupabaseOptions.PublishableKey);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Authentication.SupabaseOptions.PublishableKey);
            using var response = await LumaSearchHttp.SendAsync(request, token);
            if (!response.IsSuccessStatusCode) return [];
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (!document.RootElement.TryGetProperty("results", out var resultsNode)) return [];
            var raw = JsonSerializer.Deserialize<List<LumaSearchItem>>(resultsNode.GetRawText()) ?? [];
            return raw.Select(item => SearchItem(item.title ?? "", item.url ?? "", item.snippet ?? "", item.image ?? "", item.thumbnail ?? ""))
                .OfType<LumaSearchItem>().ToList();
        }
        catch { return []; }
    }

    private static readonly HashSet<string> SearchStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "кто", "что", "это", "такой", "такая", "такие", "как", "где", "когда", "зачем", "почему", "для", "про", "the", "what", "who", "how", "where"
    };

    private static string SearchTransliterate(string value)
    {
        var map = new Dictionary<char, string>
        {
            ['а']="a",['б']="b",['в']="v",['г']="g",['д']="d",['е']="e",['ё']="e",['ж']="zh",['з']="z",['и']="i",['й']="y",['к']="k",['л']="l",['м']="m",['н']="n",['о']="o",['п']="p",['р']="r",['с']="s",['т']="t",['у']="u",['ф']="f",['х']="h",['ц']="ts",['ч']="ch",['ш']="sh",['щ']="sch",['ы']="y",['э']="e",['ю']="yu",['я']="ya"
        };
        var result = new StringBuilder();
        foreach (var c in value.ToLowerInvariant())
        {
            if (map.TryGetValue(c, out var replacement)) result.Append(replacement);
            else result.Append(c);
        }
        return result.ToString();
    }

    private static string SearchTransliterateToCyrillic(string value)
    {
        var source = value.ToLowerInvariant();
        var pairs = new (string Latin, string Cyrillic)[]
        {
            ("shch", "щ"), ("sch", "щ"), ("yo", "ё"), ("zh", "ж"), ("kh", "х"), ("ts", "ц"),
            ("ch", "ч"), ("sh", "ш"), ("yu", "ю"), ("ya", "я"), ("ye", "е")
        };
        foreach (var pair in pairs) source = source.Replace(pair.Latin, pair.Cyrillic, StringComparison.OrdinalIgnoreCase);
        var map = new Dictionary<char, string>
        {
            ['a']="а",['b']="б",['c']="к",['d']="д",['e']="е",['f']="ф",['g']="г",['h']="х",['i']="и",['j']="дж",['k']="к",['l']="л",['m']="м",['n']="н",['o']="о",['p']="п",['q']="к",['r']="р",['s']="с",['t']="т",['u']="у",['v']="в",['w']="в",['x']="кс",['y']="й",['z']="з"
        };
        var result = new StringBuilder();
        foreach (var c in source)
        {
            if (map.TryGetValue(c, out var replacement)) result.Append(replacement);
            else result.Append(c);
        }
        return result.ToString();
    }

    private static string SearchSwapKeyboardLayout(string value)
    {
        const string latin = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`";
        const string cyrillic = "йцукенгшщзхъфывапролджэячсмитьбюё";
        var latinToCyrillic = value.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        var result = new StringBuilder();
        foreach (var original in value)
        {
            var lower = char.ToLowerInvariant(original);
            var index = (latinToCyrillic ? latin : cyrillic).IndexOf(lower);
            if (index < 0) { result.Append(original); continue; }
            var replacement = (latinToCyrillic ? cyrillic : latin)[index];
            result.Append(char.IsUpper(original) ? char.ToUpperInvariant(replacement) : replacement);
        }
        return result.ToString();
    }

    private static readonly Dictionary<string, string> SearchAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["вк"]="вконтакте vk", ["vk"]="vkontakte вконтакте", ["тг"]="telegram телеграм", ["tg"]="telegram телеграм",
        ["ют"]="youtube ютуб", ["ютуб"]="youtube", ["yt"]="youtube", ["инста"]="instagram", ["инст"]="instagram",
        ["insta"]="instagram", ["ig"]="instagram", ["дс"]="discord", ["dc"]="discord", ["гх"]="github", ["gh"]="github",
        ["споти"]="spotify", ["ям"]="яндекс музыка", ["sc"]="soundcloud", ["реддит"]="reddit", ["тт"]="tiktok",
        ["гпт"]="chatgpt", ["ии"]="искусственный интеллект ai", ["кс"]="counter strike", ["кс2"]="counter strike 2",
        ["майн"]="minecraft", ["гта"]="gta", ["пк"]="компьютер pc", ["ноут"]="ноутбук laptop"
    };

    private static readonly Regex SearchDorkOperatorRegex = new(
        @"(?<![\p{L}\p{Nd}_])(?<exclude>-?)(?<operator>site|filetype|ext|intitle|allintitle|inurl|allinurl|intext|allintext|before|after|cache|related|link|define|source|weather|stocks|map|movie):(?<value>""[^""]+""|[^\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SearchProtectedSyntaxRegex = new(
        @"(?<![\p{L}\p{Nd}_])-?(?:site|filetype|ext|intitle|allintitle|inurl|allinurl|intext|allintext|before|after|cache|related|link|define|source|weather|stocks|map|movie):(?:""[^""]+""|[^\s]+)|""[^""]+""|\b(?:OR|AND)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool SearchHasGoogleDork(string query) => SearchDorkOperatorRegex.IsMatch(query)
        || Regex.IsMatch(query, @"(?:^|\s)(?:OR|AND)(?:\s|$)|""[^""]+""", RegexOptions.IgnoreCase);

    private static string SearchTransformFreeText(string query, Func<string, string> transform)
    {
        var protectedParts = new List<string>();
        var masked = SearchProtectedSyntaxRegex.Replace(query, match =>
        {
            protectedParts.Add(match.Value);
            return $"\uE000{protectedParts.Count - 1}\uE001";
        });
        var transformed = transform(masked);
        for (var index = 0; index < protectedParts.Count; index++)
            transformed = transformed.Replace($"\uE000{index}\uE001", protectedParts[index], StringComparison.Ordinal);
        return transformed;
    }

    private static bool SearchMatchesGoogleDorks(string query, LumaSearchItem item)
    {
        foreach (Match match in SearchDorkOperatorRegex.Matches(query))
        {
            var operation = match.Groups["operator"].Value.ToLowerInvariant();
            var value = match.Groups["value"].Value.Trim().Trim('"').TrimStart('*', '.').ToLowerInvariant();
            if (value.Length == 0) continue;
            var address = item.url.ToLowerInvariant();
            var title = SearchPlain(item.title).ToLowerInvariant();
            var snippet = SearchPlain(item.snippet).ToLowerInvariant();
            var matches = operation switch
            {
                "site" => Uri.TryCreate(item.url, UriKind.Absolute, out var uri) && (uri.Host.Equals(value, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + value, StringComparison.OrdinalIgnoreCase)),
                "filetype" or "ext" => Regex.IsMatch(address, @"\." + Regex.Escape(value) + @"(?:$|[?#])", RegexOptions.IgnoreCase),
                "intitle" or "allintitle" => title.Contains(value, StringComparison.OrdinalIgnoreCase),
                "inurl" or "allinurl" => address.Contains(value, StringComparison.OrdinalIgnoreCase),
                "intext" or "allintext" => snippet.Contains(value, StringComparison.OrdinalIgnoreCase),
                _ => true
            };
            if (match.Groups["exclude"].Value == "-" ? matches : !matches) return false;
        }
        return true;
    }

    private static List<string> BuildSearchQueryVariants(string query, int limit = 4)
    {
        var output = new List<string>();
        void Add(string candidate)
        {
            candidate = Regex.Replace(candidate.Trim(), @"\s+", " ");
            if (candidate.Length > 1 && output.All(item => !string.Equals(item, candidate, StringComparison.OrdinalIgnoreCase))) output.Add(candidate);
        }
        Add(query);
        Add(SearchTransformFreeText(query, value => Regex.Replace(value, @"[\p{L}\p{Nd}]+", match => SearchAbbreviations.TryGetValue(match.Value, out var expanded) ? expanded : match.Value)));
        if (Regex.IsMatch(query, "[A-Za-z]")) Add(SearchTransformFreeText(query, SearchTransliterateToCyrillic));
        if (Regex.IsMatch(query, "[А-Яа-яЁёІіЇїЄє]")) Add(SearchTransformFreeText(query, SearchTransliterate));
        Add(SearchTransformFreeText(query, SearchSwapKeyboardLayout));
        return output.Take(limit).ToList();
    }

    private static async Task<List<LumaSearchItem>> SearchQueryVariantsAsync(IEnumerable<string> variants, Func<string, Task<List<LumaSearchItem>>> source, int deadlineMs = 2800)
    {
        var pending = variants.Select(async variant => { try { return await source(variant); } catch { return []; } }).ToList();
        var batches = new List<List<LumaSearchItem>>();
        var deadline = Task.Delay(deadlineMs);
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending.Cast<Task>().Append(deadline));
            if (ReferenceEquals(completed, deadline)) break;
            var task = (Task<List<LumaSearchItem>>)completed;
            pending.Remove(task);
            batches.Add(await task);
        }
        return batches.SelectMany(batch => batch).GroupBy(item => item.url.Split('#')[0], StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
    }

    private static List<LumaSearchItem> RankSearchItems(string query, IEnumerable<LumaSearchItem> items, int limit)
    {
        var queryVariants = BuildSearchQueryVariants(query, 5);
        var terms = queryVariants.SelectMany(variant => Regex.Matches(SearchPlain(variant).ToLowerInvariant(), @"[\p{L}\p{Nd}]{2,}").Select(match => match.Value))
            .Where(term => !SearchStopWords.Contains(term)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var expanded = new HashSet<string>(terms, StringComparer.OrdinalIgnoreCase);
        foreach (var term in terms)
        {
            var latin = SearchTransliterate(term);
            if (latin.Length > 2 && !string.Equals(latin, term, StringComparison.OrdinalIgnoreCase)) expanded.Add(latin);
            if (term == "гитлер") expanded.Add("hitler");
            if (term is "ютуб" or "ютюб") expanded.Add("youtube");
            if (term == "гугл") expanded.Add("google");
        }
        if (expanded.Count == 0) return items.Take(limit).ToList();
        return items.Select((item, index) =>
            {
                var title = SearchPlain(item.title).ToLowerInvariant();
                var snippet = SearchPlain(item.snippet).ToLowerInvariant();
                var address = (item.displayUrl + " " + item.url).ToLowerInvariant();
                var score = expanded.Sum(term => (title.Contains(term, StringComparison.OrdinalIgnoreCase) ? 6 : 0)
                    + (address.Contains(term, StringComparison.OrdinalIgnoreCase) ? 3 : 0)
                    + (snippet.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1 : 0));
                foreach (var variant in queryVariants)
                {
                    var phrase = SearchPlain(variant).ToLowerInvariant();
                    var compact = Regex.Replace(phrase, @"[^\p{L}\p{Nd}]", "");
                    if (phrase.Length > 2 && title.Contains(phrase, StringComparison.OrdinalIgnoreCase)) score += 10;
                    if (compact.Length > 2 && Regex.Replace(address, @"[^\p{L}\p{Nd}]", "").Contains(compact, StringComparison.OrdinalIgnoreCase)) score += 12;
                }
                if (address.Contains("wikipedia.org", StringComparison.OrdinalIgnoreCase)) score -= 2;
                if (address.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || address.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)) score -= 2;
                return (item, index, score);
            })
            .OrderByDescending(entry => entry.score).ThenBy(entry => entry.index)
            .Select(entry => entry.item).Take(limit).ToList();
    }

    private static List<LumaSearchItem> DiversifySearchItems(IEnumerable<LumaSearchItem> items, int limit, int perHost)
    {
        var output = new List<LumaSearchItem>();
        var hostCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var host = item.displayUrl.Length > 0 ? item.displayUrl : item.url;
            hostCounts.TryGetValue(host, out var count);
            var hostLimit = Regex.IsMatch(host, "(?:wikipedia\\.org|youtube\\.com|youtu\\.be)$", RegexOptions.IgnoreCase) ? 1 : perHost;
            if (count >= hostLimit) continue;
            hostCounts[host] = count + 1;
            output.Add(item);
            if (output.Count >= limit) break;
        }
        return output;
    }

    private static async Task<List<List<LumaSearchItem>>> CollectSearchBatchesAsync(IEnumerable<Task<List<LumaSearchItem>>> sources, int enough, int deadlineMs)
    {
        var pending = sources.ToList();
        var batches = new List<List<LumaSearchItem>>();
        var requiredBatches = Math.Min(6, pending.Count);
        var deadline = Task.Delay(deadlineMs);
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending.Cast<Task>().Append(deadline));
            if (ReferenceEquals(completed, deadline)) break;
            var source = (Task<List<LumaSearchItem>>)completed;
            pending.Remove(source);
            try { batches.Add(await source); } catch { batches.Add([]); }
            if (batches.Count >= requiredBatches && batches.Sum(batch => batch.Count) >= enough) break;
        }
        return batches;
    }

    private static LumaSearchItem WithVideoThumbnail(LumaSearchItem item)
    {
        if (item.thumbnail.Length > 0 || item.image.Length > 0) return item;
        var match = Regex.Match(item.url, @"(?:youtube\.com/(?:watch\?(?:[^#]*&)?v=|shorts/)|youtu\.be/)([A-Za-z0-9_-]{6,})", RegexOptions.IgnoreCase);
        if (!match.Success) return item;
        return new LumaSearchItem
        {
            title = item.title, url = item.url, snippet = item.snippet, displayUrl = item.displayUrl,
            image = item.image, thumbnail = "https://i.ytimg.com/vi/" + match.Groups[1].Value + "/hqdefault.jpg"
        };
    }

    private static async Task<LumaSearchItem> EnsureVideoThumbnailAsync(LumaSearchItem item, CancellationToken token)
    {
        item = WithVideoThumbnail(item);
        if (item.thumbnail.Length > 0 || item.image.Length > 0) return item;
        var html = await DownloadSearchTextAsync(item.url, token);
        foreach (Match meta in Regex.Matches(html, "<meta\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (!Regex.IsMatch(meta.Value, "(?:property|name)\\s*=\\s*([\\\"'])(?:og:image(?::secure_url)?|twitter:image(?::src)?)\\1", RegexOptions.IgnoreCase)) continue;
            var content = Regex.Match(meta.Value, "content\\s*=\\s*([\\\"'])([\\s\\S]*?)\\1", RegexOptions.IgnoreCase);
            if (!content.Success) continue;
            var raw = WebUtility.HtmlDecode(content.Groups[2].Value).Trim();
            if (raw.StartsWith("//", StringComparison.Ordinal)) raw = "https:" + raw;
            else if (Uri.TryCreate(new Uri(item.url), raw, out var absolute)) raw = absolute.AbsoluteUri;
            var thumbnail = SearchMediaUrl(raw);
            if (thumbnail.Length == 0) continue;
            return new LumaSearchItem
            {
                title = item.title, url = item.url, snippet = item.snippet, displayUrl = item.displayUrl,
                image = item.image, thumbnail = thumbnail
            };
        }
        return item;
    }

    private sealed record SearchSourceDiagnostic(string Source, string Status, int Count, long ElapsedMs, string Error);
    private sealed record SearchRunResult(List<LumaSearchItem> Results, List<SearchSourceDiagnostic> Diagnostics, long ElapsedMs);

    private static async Task<List<LumaSearchItem>> ObserveSearchSourceAsync(string name, Func<Task<List<LumaSearchItem>>> source, ConcurrentBag<SearchSourceDiagnostic> diagnostics)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var items = await source();
            diagnostics.Add(new(name, items.Count == 0 ? "empty" : "ok", items.Count, timer.ElapsedMilliseconds, items.Count == 0 ? "Источник не вернул результатов" : ""));
            return items;
        }
        catch (OperationCanceledException)
        {
            diagnostics.Add(new(name, "timeout", 0, timer.ElapsedMilliseconds, "Истекло время ожидания"));
            throw;
        }
        catch (Exception ex)
        {
            diagnostics.Add(new(name, "error", 0, timer.ElapsedMilliseconds, SanitizeDiagnosticText(ex.Message)));
            throw;
        }
    }

    private static async Task<SearchRunResult> RunLocalLumaSearchAsync(string query, string mode, CancellationToken token)
    {
        var total = Stopwatch.StartNew();
        var hasGoogleDork = SearchHasGoogleDork(query);
        var diagnostics = new ConcurrentBag<SearchSourceDiagnostic>();
        Task<List<LumaSearchItem>> Track(string name, Func<Task<List<LumaSearchItem>>> source) => ObserveSearchSourceAsync(name, source, diagnostics);
        var cacheKey = mode + "\n" + query.Trim().ToLowerInvariant();
        if (LumaSearchCache.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.CreatedAt < TimeSpan.FromMinutes(20))
            return new(cached.Results, [new("Cache", "ok", cached.Results.Count, 0, "")], total.ElapsedMilliseconds);
        List<LumaSearchItem> output;
        using var fast = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            if (mode == "images")
            {
                var imageVariants = BuildSearchQueryVariants(query, 3);
                var imageBatches = await CollectSearchBatchesAsync([
                    Track("Images", () => SearchQueryVariantsAsync(imageVariants, value => SearchImagesAsync(value, fast.Token))),
                    Track("Server", () => SearchServerAsync(query, mode, fast.Token))], 8, 3000);
                var candidates = imageBatches.SelectMany(items => items).Where(item => (item.image.Length > 0 || item.thumbnail.Length > 0) && (!hasGoogleDork || SearchMatchesGoogleDorks(query, item)))
                    .GroupBy(item => item.url, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
                output = RankSearchItems(query, candidates, 18);
            }
            else
            {
                var enriched = hasGoogleDork ? query : mode switch { "shopping" => query + " купить цена магазин", "video" => query + " видео", "shorts" => query + " shorts короткое видео", _ => query };
                var variants = BuildSearchQueryVariants(enriched, 4);
                var youtubeSource = Track("YouTube", () => SearchQueryVariantsAsync(variants.Take(mode is "video" or "shorts" ? 3 : 2), value => SearchYouTubeAsync(value, fast.Token)));
                var sources = new List<Task<List<LumaSearchItem>>>
                {
                    Track("Server", () => SearchServerAsync(query, mode, fast.Token)),
                    Track("Web HTML", () => SearchQueryVariantsAsync(variants.Take(3), value => SearchWebHtmlAsync(value, fast.Token))),
                    Track("RSS", () => SearchQueryVariantsAsync(variants.Take(2), value => SearchRssAsync(value, mode == "news", fast.Token))),
                    Track("Lite", () => SearchQueryVariantsAsync(variants.Take(3), value => SearchLiteAsync(value, fast.Token))),
                    Track("Reader", () => SearchQueryVariantsAsync(variants.Take(2), value => SearchReaderAsync(value, fast.Token))),
                    Track("Wikipedia", () => SearchQueryVariantsAsync(variants.Take(2), value => SearchWikipediaAsync(value, fast.Token))), youtubeSource
                };
                if (hasGoogleDork) sources.Insert(0, Track("Google Dorks", () => SearchGoogleDorkAsync(query, fast.Token)));
                Task<List<LumaSearchItem>>? videoSource = null;
                if (mode is "video" or "shorts")
                {
                    videoSource = Track("Video", () => SearchQueryVariantsAsync(variants.Take(3), value => SearchVideosAsync(value, fast.Token)));
                    sources.Insert(0, videoSource);
                }
                var batches = await CollectSearchBatchesAsync(sources, 18, mode is "video" or "shorts" ? 5000 : 4200);
                Task<List<LumaSearchItem>>[] keySources = videoSource is null ? new[] { youtubeSource } : new[] { youtubeSource, videoSource! };
                await Task.WhenAny(Task.WhenAll(keySources), Task.Delay(1400, fast.Token));
                foreach (var source in keySources)
                    if (source.IsCompletedSuccessfully) batches.Insert(0, source.Result);
                var candidates = new List<LumaSearchItem>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in batches.SelectMany(items => items))
                    if ((!hasGoogleDork || SearchMatchesGoogleDorks(query, item)) && seen.Add(item.url.Split('#')[0])) candidates.Add(item);
                var ranked = RankSearchItems(query, candidates, 36);
                if (mode == "video")
                {
                    var filtered = ranked.Where(item => Regex.IsMatch(item.url + " " + item.title, "youtube\\.com|youtu\\.be|vimeo\\.com|rutube\\.ru|vk\\.com/video|video", RegexOptions.IgnoreCase)).Take(14).ToList();
                    using var previews = CancellationTokenSource.CreateLinkedTokenSource(fast.Token);
                    previews.CancelAfter(TimeSpan.FromMilliseconds(2200));
                    output = (await Task.WhenAll(filtered.Select(item => EnsureVideoThumbnailAsync(item, previews.Token))))
                        .Where(item => item.thumbnail.Length > 0 || item.image.Length > 0).ToList();
                }
                else if (mode == "shorts")
                {
                    var filtered = ranked.Where(item => Regex.IsMatch(item.url + " " + item.title, "/shorts/|tiktok\\.com|/reel/|коротк", RegexOptions.IgnoreCase)).Take(14).ToList();
                    using var previews = CancellationTokenSource.CreateLinkedTokenSource(fast.Token);
                    previews.CancelAfter(TimeSpan.FromMilliseconds(2200));
                    output = (await Task.WhenAll(filtered.Select(item => EnsureVideoThumbnailAsync(item, previews.Token))))
                        .Where(item => item.thumbnail.Length > 0 || item.image.Length > 0).ToList();
                }
                else output = DiversifySearchItems(ranked, 20, 2);
            }
        }
        finally { fast.Cancel(); }
        if (output.Count > 0) LumaSearchCache[cacheKey] = new LumaSearchCacheEntry(DateTimeOffset.UtcNow, output);
        await Task.Delay(30);
        var ordered = diagnostics.OrderBy(item => item.Source, StringComparer.Ordinal).ToList();
        var expectedSources = (mode == "images" ? new[] { "Images", "Server" } : mode is "video" or "shorts"
            ? new[] { "Server", "Web HTML", "RSS", "Lite", "Reader", "Wikipedia", "YouTube", "Video" }
            : new[] { "Server", "Web HTML", "RSS", "Lite", "Reader", "Wikipedia", "YouTube" }).ToList();
        if (hasGoogleDork && mode != "images") expectedSources.Add("Google Dorks");
        foreach (var source in expectedSources.Where(source => ordered.All(item => item.Source != source)))
            ordered.Add(new(source, "timeout", 0, total.ElapsedMilliseconds, "Источник не завершил ответ до формирования выдачи"));
        ordered = ordered.OrderBy(item => item.Source, StringComparer.Ordinal).ToList();
        return new(output, ordered, total.ElapsedMilliseconds);
    }

    private async Task<LumaAiSearchResult> RunLocalLumaAiSearchAsync(string query, List<LumaSearchItem> results, string historyJson, CancellationToken token, bool concise = false)
    {
        var accessToken = await _auth.GetAccessTokenAsync(token);
        if (string.IsNullOrWhiteSpace(accessToken)) return new LumaAiSearchResult { error = "Войдите в аккаунт Luma, чтобы использовать режим ИИ.", results = results, sources = results.Take(6).ToArray() };
        var sources = string.Join("\n\n", results.Take(10).Select((item, index) => $"[{index + 1}] {item.title}\n{item.url}\n{item.snippet}"));
        var langHint = _state.Language switch
        {
            "uk" => "українською мовою (або мовою запиту)",
            "en" => "in English (or in the language of the query)",
            _ => "на языке запроса пользователя (по умолчанию по-русски)"
        };
        var systemText = concise
            ? $"Ты — LumaAI. Дай сверхкраткий ответ {langHint}: 2–4 коротких предложения, максимум 70 слов. Без заголовков, списков, Markdown, решёток, звёздочек и обратных кавычек. Только суть. При необходимости укажи источники как [1], [2]. Не называй поставщиков поиска или моделей."
            : $"Ты — поисковый режим LumaAI. Отвечай {langHint}, ясно и по существу. Не используй Markdown, решётки, звёздочки или обратные кавычки — только обычный текст. Используй переданные источники и ссылайся на них как [1], [2]. Поддерживай диалог и учитывай предыдущие реплики. Не называй сторонние поисковые сервисы или поставщиков моделей. Если веб-источники временно не загрузились, всё равно дай полезный ответ по общим знаниям, кратко отметив отсутствие веб-ссылок.";
        var messages = new List<AssistantMessage>
        {
            new() { Role = "system", Text = systemText }
        };
        try
        {
            using var history = JsonDocument.Parse(string.IsNullOrWhiteSpace(historyJson) ? "[]" : historyJson);
            if (history.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var item in history.RootElement.EnumerateArray().TakeLast(6))
                {
                    var role = item.TryGetProperty("role", out var roleNode) ? roleNode.GetString() ?? "" : "";
                    var text = item.TryGetProperty("text", out var textNode) ? textNode.GetString() ?? "" : "";
                    if (role is "user" or "assistant" && text.Length > 0) messages.Add(new AssistantMessage { Role = role, Text = text[..Math.Min(text.Length, 4000)] });
                }
        }
        catch { }
        messages.Add(new AssistantMessage { Role = "user", Text = $"Запрос: {query}\n\nАктуальные источники:\n{sources}" });
        var answer = new StringBuilder();
        try
        {
            await AssistantClient.StreamAsync(accessToken, _state.AssistantModel, messages, delta => { answer.Append(delta); return Task.CompletedTask; }, token);
            return new LumaAiSearchResult { answer = answer.ToString(), results = results, sources = results.Take(6).ToArray() };
        }
        catch (Exception ex) { App.Log(ex); return new LumaAiSearchResult { error = ex.Message, results = results, sources = results.Take(6).ToArray() }; }
    }

    private async Task RunLumaSearchAsync(WebView2 view, string query, string mode, int requestId, string historyJson = "[]")
    {
        if (view.CoreWebView2 is null || string.IsNullOrWhiteSpace(query)) return;
        mode = mode is "ai" or "all" or "images" or "shopping" or "video" or "shorts" or "news" ? mode : "all";
        query = query.Trim()[..Math.Min(query.Trim().Length, 300)];
        RecordTesterAction($"Выполнен поиск: режим {mode}");
        try
        {
            using var searchTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var searchRun = await RunLocalLumaSearchAsync(query, mode == "ai" ? "all" : mode, searchTimeout.Token);
            var results = searchRun.Results;
            RecordTesterAction($"Поиск завершён: режим {mode}, результатов {results.Count}, {searchRun.ElapsedMs} мс");
            if (mode == "ai")
            {
                using var aiTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
                var answer = await RunLocalLumaAiSearchAsync(query, results, historyJson, aiTimeout.Token);
                var json = JsonSerializer.Serialize(new { answer.answer, answer.error, answer.results, answer.sources, searchDiagnostics = searchRun.Diagnostics, elapsedMs = searchRun.ElapsedMs });
                if (view.CoreWebView2 is not null) await view.CoreWebView2.ExecuteScriptAsync($"window.lumaSearchResult?.(Object.assign({{requestId:{requestId}}},{json}))");
                return;
            }

            var baseJson = JsonSerializer.Serialize(new { results, overviewPending = mode == "all", searchDiagnostics = searchRun.Diagnostics, elapsedMs = searchRun.ElapsedMs });
            if (view.CoreWebView2 is null) return;
            await view.CoreWebView2.ExecuteScriptAsync($"window.lumaSearchResult?.(Object.assign({{requestId:{requestId}}},{baseJson}))");
            if (mode != "all") return;

            using var overviewTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var overview = await RunLocalLumaAiSearchAsync(query, results, "[]", overviewTimeout.Token, true);
            var updateJson = JsonSerializer.Serialize(new { requestId, update = "overview", overview = overview.answer, overviewSources = overview.sources, overviewError = overview.error });
            if (view.CoreWebView2 is not null) await view.CoreWebView2.ExecuteScriptAsync($"window.lumaSearchResult?.({updateJson})");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            var error = JsonSerializer.Serialize(new { requestId, error = "Не удалось получить результаты. Проверьте подключение к интернету." });
            if (view.CoreWebView2 is not null) await view.CoreWebView2.ExecuteScriptAsync($"window.lumaSearchResult?.({error})");
        }
    }
    private async Task OpenInternalPageAsync(string url, string title, string html)
    {
        var existing = Tabs.FirstOrDefault(t => t.IsInternal && t.FullUrl == url); if (existing is not null) { CurrentTab = existing; return; }
        RecordTesterAction("Открыта внутренняя страница Luma");
        var tab = new BrowserTab { FullUrl = url, Title = title, IsInternal = true, InternalPageKind = url }; Tabs.Add(tab); CurrentTab = tab;
        // Internal pages must reuse the very same WebView2 environment as site tabs. Asking for a
        // second, default environment is what made Settings fail to open with "the group or
        // resource is not in the correct state".
        try { await tab.View.EnsureCoreWebView2Async(await BrowserEnvironmentAsync()); tab.View.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false; AttachWebInterfaceLocalization(tab.View); await tab.View.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScripts.Scrollbars(CurrentAccentCss())); tab.View.CoreWebView2.WebMessageReceived += (_, e) => ReceiveWebMessage(tab, tab.View, e.WebMessageAsJson); tab.View.CoreWebView2.NavigateToString(html); } catch (Exception ex) { App.Log(ex); ShowToast("Не удалось открыть страницу", ex.Message, true); }
        RenderPanes(); FilterTabs();
    }
    private async Task ChooseDownloadFolderAsync(WebView2 view)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Выберите папку для загрузок Luma",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(_state.DownloadPath) ? _state.DownloadPath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath)) return;
        _state.DownloadPath = dialog.SelectedPath;
        _stateStore.Save();
        if (view.CoreWebView2 is not null)
            await view.CoreWebView2.ExecuteScriptAsync($"window.lumaSetDownloadPath?.({JsonSerializer.Serialize(dialog.SelectedPath)})");
    }

    private async Task RunChromeImportAsync(WebView2 view, ChromeImportOptions options)
    {
        if (((App)Application.Current).IsPrivateSession) { ShowToast("Недоступно в приватном режиме", "Импорт отключён", true); return; }
        if (view.CoreWebView2 is null) return;
        async Task Progress(int percent, string message) => await Script(view, $"window.lumaImportUpdate?.({JsonSerializer.Serialize(new { type = "progress", percent, message })})");
        try
        {
            var report = await ChromeImporter.ImportAsync(_spaces[_activeSpace].State, _state, view.CoreWebView2.CookieManager, options, Progress);
            foreach (var url in report.Tabs) await AddTabAsync(url, false);
            RefreshFolders(); FilterTabs(); Save();
            var result = new { type = "result", report.Bookmarks, report.History, report.Passwords, report.Autofill, report.Cookies, tabs = report.Tabs.Count, warnings = report.Warnings };
            await Script(view, $"window.lumaImportUpdate?.({JsonSerializer.Serialize(result)})");
        }
        catch (Exception ex)
        {
            App.Log(ex); await Script(view, $"window.lumaImportUpdate?.({JsonSerializer.Serialize(new { type = "result", error = ex.Message, warnings = new[] { ex.Message } })})");
        }
    }
    private void ApplySettings(JsonElement root)
    {
        string S(string n, string f) => root.TryGetProperty(n, out var v) ? v.GetString() ?? f : f; bool B(string n, bool f) => root.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : f; int I(string n, int f) => root.TryGetProperty(n, out var v) && v.TryGetInt32(out var number) ? number : f;
        _state.StartupBehavior = S("startup", _state.StartupBehavior); _state.SidebarVisible = B("sidebar", _state.SidebarVisible); _state.ConfirmManyTabs = B("confirm", _state.ConfirmManyTabs); _state.SearchEngine = S("search", _state.SearchEngine); _state.Language = NormalizeInterfaceLanguage(S("language", _state.Language)); ConfigureBrowserLanguage(_state.Language); _state.AnimationsEnabled = B("animations", _state.AnimationsEnabled); _state.DownloadPath = S("download", _state.DownloadPath);
        _state.BlockThirdPartyCookies = B("blockThirdPartyCookies", _state.BlockThirdPartyCookies); _state.DoNotTrack = B("doNotTrack", _state.DoNotTrack); _state.FloatingMusicEnabled = B("floatingMusic", _state.FloatingMusicEnabled); _state.FloatingVideoEnabled = B("floatingVideo", _state.FloatingVideoEnabled); _state.FloatingMediaAlwaysOnTop = B("floatingAlwaysOnTop", _state.FloatingMediaAlwaysOnTop); if (root.TryGetProperty("floatingVideoQuality", out var quality) && quality.TryGetInt32(out var q)) _state.FloatingVideoQuality = Math.Clamp(q, 45, 95);
        _state.RestoreSession = B("restore", _state.RestoreSession);
        var oldProfile = _state.PerformanceProfile;
        _state.PerformanceProfile = S("performanceProfile", _state.PerformanceProfile);
        _state.PageScale = Math.Clamp(I("pageScale", _state.PageScale), 80, 200);
        if (root.TryGetProperty("sleepMinutes", out _))
            _state.SleepAfterMinutes = Math.Clamp(I("sleepMinutes", _state.SleepAfterMinutes), 0, 240);
        else if (oldProfile != _state.PerformanceProfile)
        {
            if (_state.PerformanceProfile == "memory") _state.SleepAfterMinutes = 2;
            else if (_state.PerformanceProfile == "speed") _state.SleepAfterMinutes = 0;
            else _state.SleepAfterMinutes = 5;
        }
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Luma")) key.SetValue("Theme", S("theme", "dark")); CommitThemePreview(); Save(); ApplyTheme(); ApplyInterfaceLanguage(); ApplyPageScaleToOpenTabs(); ApplyPrivacySettings(); SetSidebar(_state.SidebarVisible, true); TrimProcessMemory(); ShowToast(L("Настройки сохранены", "Settings saved", "Налаштування збережено"));
    }

    /// <summary>Animates the sidebar column width. A grid column width cannot be animated
    /// directly, so the animation runs on this window-level double and writes the column.</summary>
    public static readonly DependencyProperty SidebarSlideProperty = DependencyProperty.Register(
        "SidebarSlide", typeof(double), typeof(MainWindow),
        new PropertyMetadata(0d, (d, e) => { if (d is MainWindow w && w.SidebarColumn is not null) w.SidebarColumn.Width = new GridLength(Math.Max(0, (double)e.NewValue)); }));

    /// <summary>
    /// Shows or hides the sidebar. The manual toggle resizes the layout column (the page is
    /// meant to make room), while the hover reveal floats the panel over the page in a Popup so
    /// the page is never resized mid-animation.
    /// </summary>
}
