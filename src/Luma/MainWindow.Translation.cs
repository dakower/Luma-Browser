using Luma.Core;
using Luma.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
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
    private sealed class PageTranslationSession { public string Url = ""; }
    private readonly ConditionalWeakTable<WebView2, PageTranslationSession> _pageTranslationSessions = new();

    private void MarkPageTranslationActive(WebView2 view)
    {
        var session = _pageTranslationSessions.GetOrCreateValue(view);
        session.Url = view.Source?.ToString() ?? "";
    }

    private bool IsPageTranslationActive(WebView2 view)
        => _pageTranslationSessions.TryGetValue(view, out var session)
           && string.Equals(session.Url, view.Source?.ToString() ?? "", StringComparison.Ordinal);

    private void ResetPageTranslation(WebView2 view) => _pageTranslationSessions.Remove(view);

    private async Task ApplyTranslationAsync(WebView2 view, bool notify)
    {
        var domain = TranslateDomain(view.Source?.ToString() ?? ""); if (domain is null) return;
        // Rewriting live app UI breaks it; Classroom was the visible victim.
        if (IsConferencingHost(domain)) { if (notify) ShowToast("Перевод отключён", "На этом сайте он ломает интерфейс", true); return; }
        if (!notify && !_state.AlwaysTranslateDomains.Contains(domain)) return;
        MarkPageTranslationActive(view);
        try
        {
            // Pages very often have nothing to translate yet on the first pass (framework
            // hydration, lazy sections). Retry a few times with a short backoff instead of
            // giving up, which is why translation used to work only every other time.
            var count = 0;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                count = await TranslateRoundAsync(view);
                if (count > 0) break;
                await Task.Delay(90 + attempt * 110);
            }
            if (notify) ShowToast(count > 0 ? "Страница переведена" : "Не удалось перевести страницу", domain, count == 0);
            // Keep translating content that appears later (SPA navigation, lazy-loaded sections,
            // infinite scroll) instead of stopping after two fixed rounds.
            await EnsureTranslationWatcherAsync(view);
        }
        catch (Exception ex) { App.Log(ex); if (notify) ShowToast("Перевод недоступен", "Проверьте подключение к интернету", true); }
    }
    private static async Task EnsureTranslationWatcherAsync(WebView2 view)
    {
        const string watcher = """
        (()=>{if(window.__lumaTranslateWatcher||!document.body)return;window.__lumaTranslateWatcher=true;let timer=null;const schedule=()=>{clearTimeout(timer);timer=setTimeout(()=>{try{chrome.webview.postMessage({kind:'luma-translate-more'})}catch(e){}},550)};new MutationObserver(muts=>{for(const m of muts)if(m.addedNodes&&m.addedNodes.length){schedule();return}}).observe(document.body,{childList:true,subtree:true})})()
        """;
        await Script(view, watcher);
    }
    private async Task<int> TranslateRoundAsync(WebView2 view)
    {
        const string collect = """
        (()=>{if(!document.body)return[];window.__lumaTranslatedNodes??=new WeakSet();const nodes=[],walker=document.createTreeWalker(document.body,NodeFilter.SHOW_TEXT,{acceptNode:n=>{const p=n.parentElement,t=(n.nodeValue||'').trim();if(!p||!t||t.length<2||!/\p{L}/u.test(t)||/^[\p{Script=Cyrillic}0-9\s\p{P}]+$/u.test(t)||['SCRIPT','STYLE','NOSCRIPT','TEXTAREA','CODE','PRE','OPTION'].includes(p.tagName)||p.closest('[contenteditable="true"],.notranslate,[translate="no"]')||window.__lumaTranslatedNodes.has(n))return NodeFilter.FILTER_REJECT;return NodeFilter.FILTER_ACCEPT}});let n;while((n=walker.nextNode())&&nodes.length<2500)nodes.push(n);window.__lumaTranslationNodes=nodes;return nodes.map(n=>(n.nodeValue||'').trim())})()
        """;
        var raw = await Script(view, collect); var texts = JsonSerializer.Deserialize<List<string>>(raw) ?? []; if (texts.Count == 0) return 0;
        return await TranslateAndApplyAsync(view, texts);
    }

    /// <summary>
    /// Translates and paints the page incrementally: every batch is written into the DOM as soon
    /// as its reply lands, and all batches are in flight at once. Waiting for the slowest request
    /// before touching the page was what made translation feel like a five second freeze.
    /// </summary>
    private async Task<int> TranslateAndApplyAsync(WebView2 view, IReadOnlyList<string> texts)
    {
        const int MaxTextChars = 1400, MaxBatchItems = 28, MaxBatchChars = 2600;
        var sources = new string[texts.Count];
        var cachedIndices = new List<int>();
        var cachedValues = new List<string>();
        var pending = new List<int>();
        for (var i = 0; i < texts.Count; i++)
        {
            sources[i] = texts[i].Length > MaxTextChars ? texts[i][..MaxTextChars] : texts[i];
            if (TranslationCache.TryGetValue(sources[i], out var hit)) { cachedIndices.Add(i); cachedValues.Add(hit); }
            else pending.Add(i);
        }

        var applied = 0;
        // Anything already translated once shows up instantly, with no network at all.
        if (cachedIndices.Count > 0) applied += await ApplyTranslatedAsync(view, cachedIndices, cachedValues);

        var batches = new List<List<int>>();
        var current = new List<int>(); var chars = 0;
        foreach (var index in pending)
        {
            if (current.Count > 0 && (current.Count >= MaxBatchItems || chars + sources[index].Length > MaxBatchChars))
            { batches.Add(current); current = new List<int>(); chars = 0; }
            current.Add(index); chars += sources[index].Length;
        }
        if (current.Count > 0) batches.Add(current);
        if (batches.Count == 0) return applied;

        // Fire every batch immediately; TranslationGate is the only throttle.
        var running = batches.ToDictionary(
            batch => TranslateGroupAsync(batch.Select(i => sources[i]).ToList()),
            batch => batch);

        var inflight = new List<Task<List<string>?>>(running.Keys);
        while (inflight.Count > 0)
        {
            var finished = await Task.WhenAny(inflight);
            inflight.Remove(finished);
            var batch = running[finished];
            var values = await finished;
            if (values is null) continue;

            var indices = new List<int>(batch.Count);
            var translations = new List<string>(batch.Count);
            for (var i = 0; i < batch.Count && i < values.Count; i++)
            {
                var value = values[i];
                var source = sources[batch[i]];
                if (string.IsNullOrWhiteSpace(value) || value == source) continue;
                TranslationCache[source] = value;
                indices.Add(batch[i]);
                translations.Add(value);
            }
            if (indices.Count > 0) applied += await ApplyTranslatedAsync(view, indices, translations);
        }
        return applied;
    }

    /// <summary>Writes one slice of translations into the collected text nodes.</summary>
    private static async Task<int> ApplyTranslatedAsync(WebView2 view, IReadOnlyList<int> indices, IReadOnlyList<string> values)
    {
        var payload = JsonSerializer.Serialize(values);
        var slots = JsonSerializer.Serialize(indices);
        var appliedRaw = await Script(view, $"(()=>{{const v={payload},ix={slots},n=window.__lumaTranslationNodes||[],seen=window.__lumaTranslatedNodes||(window.__lumaTranslatedNodes=new WeakSet());let c=0;for(let k=0;k<ix.length;k++){{const i=ix[k];if(!n[i])continue;{{const o=n[i].nodeValue||'',lead=(o.match(/^\\s*/)||[''])[0],tail=(o.match(/\\s*$/)||[''])[0],val=v[k]?lead+String(v[k]).trim()+tail:o;if(val&&val!==o){{n[i].nodeValue=val;c++}}seen.add(n[i])}}}}return c}})()");
        return int.TryParse(appliedRaw, out var count) ? count : 0;
    }
    // Translation endpoints behave differently from network to network: sometimes the batched
    // "t" endpoint answers, sometimes only the classic per-text "single" endpoint does, and
    // hammering either one in parallel gets the client rate-limited (which is what silently
    // killed translation entirely). So: try the cheap batched endpoints first, remember which
    // one worked, fall back to per-text requests, keep concurrency low and cache every result.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> TranslationCache = new();
    private static int _translateStrategy; // 0 = unknown, 1/2 = multi, 3 = marked batch, 4 = per text

    /// <summary>Translates one group of segments, learning which endpoint this network allows.</summary>
    private static async Task<List<string>?> TranslateGroupAsync(IReadOnlyList<string> items)
    {
        if (items.Count == 0) return new List<string>();
        int[] order = _translateStrategy switch
        {
            1 => [1, 2, 3, 4],
            2 => [2, 1, 3, 4],
            3 => [3, 1, 2, 4],
            4 => [4, 1, 2, 3],
            _ => [1, 2, 3, 4],
        };
        foreach (var strategy in order)
        {
            List<string>? values = strategy switch
            {
                1 => await TranslateMultiAsync(items, "gtx"),
                2 => await TranslateMultiAsync(items, "dict-chrome-ex"),
                3 => await TranslateMarkedBatchAsync(items),
                _ => await TranslateOneByOneAsync(items),
            };
            if (values is not null && values.Count == items.Count
                && values.Select((value, index) => !string.IsNullOrWhiteSpace(value) && !string.Equals(value, items[index], StringComparison.Ordinal)).Any(changed => changed))
            {
                _translateStrategy = strategy;
                return values;
            }
        }
        return null;
    }

    /// <summary>Batched request: every segment is its own q parameter, one reply entry each.</summary>
    private static async Task<List<string>?> TranslateMultiAsync(IReadOnlyList<string> items, string client)
    {
        await TranslationGate.WaitAsync();
        try
        {
            foreach (var host in new[] { "https://translate.googleapis.com", "https://translate.google.com" })
            {
                try
                {
                    var url = host + "/translate_a/t?client=" + Uri.EscapeDataString(client) + "&sl=auto&tl=ru&format=text";
                    using var body = new FormUrlEncodedContent(items.Select(item => new KeyValuePair<string, string>("q", item)));
                    using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = body };
                    using var response = await TranslationHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode) continue;
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    var values = ParseMultiTranslation(doc.RootElement, items.Count);
                    if (values is not null) return values;
                }
                catch { }
            }
            return null;
        }
        catch { return null; }
        finally { TranslationGate.Release(); }
    }

    private static List<string>? ParseMultiTranslation(JsonElement root, int expected)
    {
        if (root.ValueKind == JsonValueKind.String)
            return expected == 1 ? [root.GetString() ?? ""] : null;
        if (root.ValueKind != JsonValueKind.Array) return null;
        var values = new List<string>();
        foreach (var item in root.EnumerateArray())
        {
            if (values.Count >= expected) break;
            if (item.ValueKind == JsonValueKind.String) { values.Add(item.GetString() ?? ""); continue; }
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() == 0) continue;
            if (item[0].ValueKind == JsonValueKind.String) { values.Add(item[0].GetString() ?? ""); continue; }
            var combined = new StringBuilder();
            foreach (var segment in item.EnumerateArray())
                if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0 && segment[0].ValueKind == JsonValueKind.String)
                    combined.Append(segment[0].GetString());
            if (combined.Length > 0) values.Add(combined.ToString());
        }
        return values.Count == expected ? values : null;
    }

    private static async Task<List<string>?> TranslateMarkedBatchAsync(IReadOnlyList<string> items)
    {
        var combined = string.Join("\n", items.Select((item, index) => $"[[[LUMA_SEG_{index:000}]]]\n{item}"));
        var translated = await TranslateChunkAsync(combined);
        if (string.IsNullOrWhiteSpace(translated) || string.Equals(translated, combined, StringComparison.Ordinal)) return null;
        var markers = System.Text.RegularExpressions.Regex.Matches(translated, @"\[\[\[\s*LUMA_SEG_(\d{3})\s*\]\]\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (markers.Count != items.Count) return null;
        var values = Enumerable.Repeat(string.Empty, items.Count).ToList();
        for (var i = 0; i < markers.Count; i++)
        {
            if (!int.TryParse(markers[i].Groups[1].Value, out var index) || index < 0 || index >= values.Count) return null;
            var start = markers[i].Index + markers[i].Length;
            var end = i + 1 < markers.Count ? markers[i + 1].Index : translated.Length;
            values[index] = translated[start..end].Trim();
        }
        return values.All(value => !string.IsNullOrWhiteSpace(value)) ? values : null;
    }

    /// <summary>Last resort: per-text requests with the shared four-request throttle.</summary>
    private static async Task<List<string>?> TranslateOneByOneAsync(IReadOnlyList<string> items)
    {
        var values = (await Task.WhenAll(items.Select(TranslateChunkAsync))).ToList();
        return values.Select((value, index) => !string.IsNullOrWhiteSpace(value) && !string.Equals(value, items[index], StringComparison.Ordinal)).Any(changed => changed) ? values : null;
    }

    private static async Task<string> TranslateChunkAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        await TranslationGate.WaitAsync();
        try
        {
            foreach (var host in new[] { "https://translate.googleapis.com", "https://translate.google.com" })
            {
                try
                {
                    var endpoint = host + "/translate_a/single?client=gtx&sl=auto&tl=ru&dt=t";
                    using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["q"] = text });
                    using var response = await TranslationHttp.PostAsync(endpoint, body);
                    if (!response.IsSuccessStatusCode) continue;
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0 || doc.RootElement[0].ValueKind != JsonValueKind.Array) continue;
                    var builder = new StringBuilder();
                    foreach (var segment in doc.RootElement[0].EnumerateArray())
                        if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0 && segment[0].ValueKind == JsonValueKind.String)
                            builder.Append(segment[0].GetString());
                    if (builder.Length > 0) return builder.ToString();
                }
                catch { }
            }
            return "";
        }
        catch { return ""; }
        finally { TranslationGate.Release(); }
    }

}
