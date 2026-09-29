using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace Luma;

public partial class MainWindow
{
    private sealed class OriginalUiText { public string? Text; public string? Content; public string? Header; public string? ToolTip; public string? Title; public string? InlineText; }
    private readonly ConditionalWeakTable<DependencyObject, OriginalUiText> _originalUi = new();
    private readonly ConditionalWeakTable<WebView2, object> _localizedViews = new();
    private CancellationTokenSource? _interfaceLanguageCts;
    private DispatcherTimer? _interfaceLanguageTimer;
    private bool _interfaceLanguageApplying;

    private string EffectiveLanguage
    {
        get
        {
            return NormalizeInterfaceLanguage(_state.Language);
        }
    }

    private static string NormalizeInterfaceLanguage(string? value)
    {
        value = value?.Trim().ToLowerInvariant();
        var dash = value?.IndexOf('-') ?? -1;
        if (dash > 0) value = value![..dash];
        return value is "en" or "uk" or "ru" ? value : "en";
    }

    private string L(string ru, string en, string uk) => EffectiveLanguage == "ru" ? ru : EffectiveLanguage == "uk" ? uk : en;

    private async void ApplyInterfaceLanguage()
    {
        if (!IsInitialized) return;
        var previous = _interfaceLanguageCts;
        _interfaceLanguageCts = null;
        try { previous?.Cancel(); }
        catch (ObjectDisposedException) { }
        var run = new CancellationTokenSource();
        _interfaceLanguageCts = run;
        _interfaceLanguageApplying = true;
        var token = run.Token;
        var language = EffectiveLanguage;
        try
        {
            Language = System.Windows.Markup.XmlLanguage.GetLanguage(language);
            var targets = new List<(DependencyObject Node, DependencyProperty Property, string Source)>();
            var visited = new HashSet<DependencyObject>();
            foreach (Window window in System.Windows.Application.Current.Windows) CollectUiText(window, targets, visited);
            var webLocalization = ApplyOpenWebInterfaceLanguageAsync();
            if (language == "ru")
            {
                foreach (var target in targets) target.Node.SetCurrentValue(target.Property, target.Source);
            }
            else
            {
                var known = InterfaceTranslationService.GetKnownTranslations(targets.Select(x => x.Source), language);
                foreach (var target in targets)
                    if (known.TryGetValue(target.Source.Trim(), out var ready)) target.Node.SetCurrentValue(target.Property, ready);
                var translations = await InterfaceTranslationService.TranslateManyAsync(targets.Select(x => x.Source), language, token);
                if (token.IsCancellationRequested || language != EffectiveLanguage) return;
                foreach (var target in targets)
                    if (translations.TryGetValue(target.Source.Trim(), out var translated)) target.Node.SetCurrentValue(target.Property, translated);
            }
            await webLocalization;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.Log(ex); }
        finally
        {
            if (ReferenceEquals(_interfaceLanguageCts, run))
            {
                _interfaceLanguageCts = null;
                _interfaceLanguageApplying = false;
                EnsureLocalizationWatcher();
            }
            run.Dispose();
        }
    }

    private void EnsureLocalizationWatcher()
    {
        if (_interfaceLanguageTimer is not null) return;
        _interfaceLanguageTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _interfaceLanguageTimer.Tick += (_, _) => { _interfaceLanguageTimer.Stop(); if (EffectiveLanguage != "ru" && !_interfaceLanguageApplying) ApplyInterfaceLanguage(); };
        LayoutUpdated += (_, _) => { if (EffectiveLanguage != "ru" && !_interfaceLanguageApplying && _interfaceLanguageTimer.IsEnabled == false) _interfaceLanguageTimer.Start(); };
    }

    private async Task ApplyOpenWebInterfaceLanguageAsync()
    {
        var tasks = new List<Task>();
        foreach (var tab in _spaces.SelectMany(space => space.Tabs).Where(tab => tab.IsInternal))
        {
            if (tab.View.CoreWebView2 is not null) tasks.Add(ApplyWebInterfaceLanguageAsync(tab.View));
            if (tab.SecondaryView?.CoreWebView2 is not null) tasks.Add(ApplyWebInterfaceLanguageAsync(tab.SecondaryView));
        }
        if (_assistantView?.CoreWebView2 is not null) tasks.Add(ApplyWebInterfaceLanguageAsync(_assistantView));
        if (WelcomeView?.CoreWebView2 is not null) tasks.Add(ApplyWebInterfaceLanguageAsync(WelcomeView));
        if (tasks.Count > 0) await Task.WhenAll(tasks);
    }

    private void CollectUiText(DependencyObject node, List<(DependencyObject, DependencyProperty, string)> targets, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(node)) return;
        var original = _originalUi.GetOrCreateValue(node);
        if (node is TextBlock text)
        {
            var current = text.Text;
            original.Text = CaptureOriginal(current, original.Text);
            if (!string.IsNullOrWhiteSpace(original.Text)) targets.Add((text, TextBlock.TextProperty, original.Text));
        }
        if (node is ContentControl content && content.Content is string value)
        {
            original.Content = CaptureOriginal(value, original.Content);
            if (!string.IsNullOrWhiteSpace(original.Content)) targets.Add((content, ContentControl.ContentProperty, original.Content));
        }
        if (node is HeaderedContentControl header && header.Header is string headerValue)
        {
            original.Header = CaptureOriginal(headerValue, original.Header);
            if (!string.IsNullOrWhiteSpace(original.Header)) targets.Add((header, HeaderedContentControl.HeaderProperty, original.Header));
        }
        if (node is HeaderedItemsControl itemsHeader && itemsHeader.Header is string itemsHeaderValue)
        {
            original.Header = CaptureOriginal(itemsHeaderValue, original.Header);
            if (!string.IsNullOrWhiteSpace(original.Header)) targets.Add((itemsHeader, HeaderedItemsControl.HeaderProperty, original.Header));
        }
        if (node is Window window && !string.IsNullOrWhiteSpace(window.Title))
        {
            original.Title = CaptureOriginal(window.Title, original.Title);
            if (!string.IsNullOrWhiteSpace(original.Title)) targets.Add((window, Window.TitleProperty, original.Title));
        }
        if (node is System.Windows.Documents.Run run && !string.IsNullOrWhiteSpace(run.Text))
        {
            original.InlineText = CaptureOriginal(run.Text, original.InlineText);
            if (!string.IsNullOrWhiteSpace(original.InlineText)) targets.Add((run, System.Windows.Documents.Run.TextProperty, original.InlineText));
        }
        if (node is System.Windows.Controls.DataGrid grid)
        {
            foreach (var column in grid.Columns)
            {
                if (column.Header is not string columnHeader) continue;
                var columnOriginal = _originalUi.GetOrCreateValue(column);
                columnOriginal.Header = CaptureOriginal(columnHeader, columnOriginal.Header);
                if (!string.IsNullOrWhiteSpace(columnOriginal.Header)) targets.Add((column, System.Windows.Controls.DataGridColumn.HeaderProperty, columnOriginal.Header));
            }
        }
        if (node is FrameworkElement element && element.ToolTip is string tip)
        {
            original.ToolTip = CaptureOriginal(tip, original.ToolTip);
            if (!string.IsNullOrWhiteSpace(original.ToolTip)) targets.Add((element, FrameworkElement.ToolTipProperty, original.ToolTip));
        }
        try { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) CollectUiText(VisualTreeHelper.GetChild(node, i), targets, visited); }
        catch (InvalidOperationException) { }
        try { foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) CollectUiText(child, targets, visited); }
        catch (InvalidOperationException) { }
        if (node is System.Windows.Controls.Primitives.Popup popup && popup.Child is not null) CollectUiText(popup.Child, targets, visited);
    }

    private string? CaptureOriginal(string? current, string? existing)
    {
        if (string.IsNullOrWhiteSpace(current)) return existing;
        if (EffectiveLanguage == "ru") return InterfaceTranslationService.NeedsTranslation(current) ? current : existing;
        if (!string.IsNullOrWhiteSpace(existing)) return existing;
        var source = InterfaceTranslationService.ResolveSourceText(current, EffectiveLanguage);
        return !string.Equals(source, current, StringComparison.Ordinal) || InterfaceTranslationService.NeedsTranslation(current) ? source : null;
    }

    private void AttachWebInterfaceLocalization(WebView2 view)
    {
        if (view.CoreWebView2 is null || _localizedViews.TryGetValue(view, out _)) return;
        _localizedViews.Add(view, new object());
        view.CoreWebView2.WebMessageReceived += async (_, e) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;
                if (!root.TryGetProperty("kind", out var kind) || kind.GetString() != "luma-interface-translate") return;
                var requestId = root.TryGetProperty("requestId", out var id) ? id.GetInt32() : 0;
                var language = root.TryGetProperty("language", out var lang) ? lang.GetString() ?? EffectiveLanguage : EffectiveLanguage;
                if (!root.TryGetProperty("texts", out var list) || list.ValueKind != JsonValueKind.Array) return;
                var texts = list.EnumerateArray().Take(120).Select(x => x.GetString() ?? "").Where(InterfaceTranslationService.NeedsTranslation).ToArray();
                var known = InterfaceTranslationService.GetKnownTranslations(texts, language);
                if (known.Count > 0 && view.CoreWebView2 is not null)
                {
                    var readyPayload = JsonSerializer.Serialize(known);
                    await view.CoreWebView2.ExecuteScriptAsync($"window.__lumaInterfaceTranslationResult?.({requestId},{readyPayload},false)");
                }
                var translated = await InterfaceTranslationService.TranslateManyAsync(texts, language);
                if (view.CoreWebView2 is null) return;
                var payload = JsonSerializer.Serialize(translated);
                await view.CoreWebView2.ExecuteScriptAsync($"window.__lumaInterfaceTranslationResult?.({requestId},{payload},true)");
            }
            catch (Exception ex) { App.Log(ex); }
        };
        view.CoreWebView2.NavigationCompleted += async (_, _) => await ApplyWebInterfaceLanguageAsync(view);
    }

    private async Task ApplyWebInterfaceLanguageAsync(WebView2 view)
    {
        if (view.CoreWebView2 is null) return;
        AttachWebInterfaceLocalization(view);
        var language = EffectiveLanguage;
        var script = """
        (()=>{
          const language=__LUMA_LANGUAGE__;
          const isRu=language==='ru';
          let sequence=window.__lumaTranslationSequence||0;
          const pending=new Map();
          function source(node){if(node.__lumaOriginalRu)return node.__lumaOriginalRu;const value=(node.nodeValue||'').trim();if(/[А-Яа-яЁёІіЇїЄє]/.test(value))node.__lumaOriginalRu=value;return node.__lumaOriginalRu||''}
          function scan(){
            const entries=[];const walker=document.createTreeWalker(document.body||document.documentElement,NodeFilter.SHOW_TEXT,{acceptNode(n){const p=n.parentElement;if(!p||/^(SCRIPT|STYLE|CODE|PRE|TEXTAREA|NOSCRIPT)$/i.test(p.tagName))return NodeFilter.FILTER_REJECT;return source(n)?NodeFilter.FILTER_ACCEPT:NodeFilter.FILTER_REJECT}});let n;while(n=walker.nextNode())entries.push({node:n,original:source(n)});
            document.querySelectorAll('[placeholder],[title],[aria-label],[alt]').forEach(el=>{for(const attr of ['placeholder','title','aria-label','alt']){if(!el.hasAttribute(attr))continue;el.__lumaOriginalAttrs=el.__lumaOriginalAttrs||{};const value=(el.getAttribute(attr)||'').trim();if(!el.__lumaOriginalAttrs[attr]&&/[А-Яа-яЁёІіЇїЄє]/.test(value))el.__lumaOriginalAttrs[attr]=value;if(el.__lumaOriginalAttrs[attr])entries.push({node:el,attr,original:el.__lumaOriginalAttrs[attr]})}});
            if(isRu){entries.forEach(e=>{if(e.attr)e.node.setAttribute(e.attr,e.original);else e.node.nodeValue=e.node.nodeValue.replace(e.node.nodeValue.trim(),e.original)});return}
            const unique=[...new Set(entries.map(e=>e.original))];for(let i=0;i<unique.length;i+=100){const texts=unique.slice(i,i+100),requestId=++sequence;pending.set(requestId,entries.filter(e=>texts.includes(e.original)));chrome.webview.postMessage({kind:'luma-interface-translate',requestId,language,texts})}window.__lumaTranslationSequence=sequence;
          }
          window.__lumaInterfaceTranslationResult=(requestId,map,complete=true)=>{const entries=pending.get(requestId)||[];entries.forEach(e=>{const translated=map[e.original];if(!translated)return;if(e.attr)e.node.setAttribute(e.attr,translated);else e.node.nodeValue=e.node.nodeValue.replace(e.node.nodeValue.trim(),translated)});if(complete)pending.delete(requestId)};
          clearTimeout(window.__lumaTranslateTimer);window.__lumaTranslateTimer=setTimeout(scan,30);
          window.__lumaTranslateObserver?.disconnect();window.__lumaTranslateObserver=new MutationObserver(()=>{clearTimeout(window.__lumaTranslateMutation);window.__lumaTranslateMutation=setTimeout(scan,250)});window.__lumaTranslateObserver.observe(document.documentElement,{subtree:true,childList:true,characterData:true,attributes:true,attributeFilter:['placeholder','title','aria-label','alt']});
        })();
        """.Replace("__LUMA_LANGUAGE__", JsonSerializer.Serialize(language), StringComparison.Ordinal);
        await view.CoreWebView2.ExecuteScriptAsync(script);
    }

    private static async Task WarmLocalizationPacksAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            await InterfaceTranslationService.WarmSupportedPacksAsync();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    private void ApplyPageScaleToOpenTabs()
    {
        var zoom = Math.Clamp(_state.PageScale / 100d, .8, 2d);
        foreach (var tab in _spaces.SelectMany(space => space.Tabs))
        {
            try { if (tab.View.CoreWebView2 is not null && !tab.IsInternal) tab.View.ZoomFactor = zoom; } catch (Exception ex) { App.Log(ex); }
            try { if (tab.SecondaryView?.CoreWebView2 is not null) tab.SecondaryView.ZoomFactor = zoom; } catch (Exception ex) { App.Log(ex); }
        }
    }

    private void ApplyPrivacySettings()
    {
        foreach (var tab in _spaces.SelectMany(space => space.Tabs))
            foreach (var view in new[] { tab.View, tab.SecondaryView }.Where(view => view?.CoreWebView2 is not null))
                try { view!.CoreWebView2.Profile.PreferredTrackingPreventionLevel = _state.BlockThirdPartyCookies ? CoreWebView2TrackingPreventionLevel.Strict : CoreWebView2TrackingPreventionLevel.Balanced; } catch (Exception ex) { App.Log(ex); }
    }
}
