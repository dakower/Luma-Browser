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
    private void ActivatePane(BrowserTab tab, WebView2 view)
    {
        // A focus message from an old/background WebView is not a user request to switch tabs.
        // WebView2 can emit it late after text selection or native mouse capture; accepting it
        // here was the root cause of a freshly selected tab jumping back to the previous page.
        if (!ReferenceEquals(tab, CurrentTab)) return;
        tab.ActivePane = ReferenceEquals(tab.SecondaryView, view) ? "secondary" : "primary";
        UpdateChrome(); UpdateIndicators();
    }
    public async void NavigateInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        var target = Normalize(input);
        if (TryParseLumaSearchUrl(target, out var query, out var mode)) { await OpenLumaSearchTabAsync(query, mode); return; }
        if (CurrentTab is null) await AddTabAsync(target);
        else CurrentTab.ActiveView.CoreWebView2?.Navigate(target);
    }

    private string Normalize(string input) => UrlTools.NormalizeInput(input, _state.SearchEngine);
    private string HomeUrl() => UrlTools.HomeUrl(_state.SearchEngine);
    private static string Domain(string url) { var value = BrowserTab.DomainOf(url); return value == "Страница Luma" ? "Новая вкладка" : value; }

    private void UpdateChrome()
    {
        var tab = CurrentTab; var present = tab is not null; var home = tab?.IsHome == true;
        EmptyBackground.Visibility = !present || home ? Visibility.Visible : Visibility.Collapsed;
        if (!home) _showAccountSurface = false;
        if (home) ShowHomeSurface(_showAccountSurface);
        else AccountSurfaceCloseButton.Visibility = Visibility.Collapsed;
        DomainText.Text = home ? "Luma" : present ? tab!.Domain : "Новая вкладка";
        CopyUrlButton.IsEnabled = present; SiteSettingsButton.IsEnabled = present;
        TranslateButton.IsEnabled = present && !tab!.IsInternal; SplitButton.IsEnabled = present && !tab!.IsInternal;
        BackButton.IsEnabled = present && (tab!.ActiveView.CanGoBack || !string.IsNullOrWhiteSpace(tab.BackFallbackInternalUrl)); ForwardButton.IsEnabled = present && tab!.ActiveView.CanGoForward;
        if (present)
        {
            _suppressPip = true; PipToggle.IsChecked = _state.AutoPictureInPicture.TryGetValue(tab!.Domain, out var pip) && pip; _suppressPip = false;
            if (Uri.TryCreate(tab.ActiveUrl, UriKind.Absolute, out var uri)) { var secure = uri.Scheme == "https"; SecurityText.Text = secure ? "Защищено" : "Не защищено"; SecurityIcon.Data = (Geometry)FindResource(secure ? "IconShieldCheck" : "IconInfo"); SecurityIcon.Stroke = secure ? new SolidColorBrush(Color.FromRgb(97, 196, 84)) : new SolidColorBrush(Color.FromRgb(224, 138, 147)); }
        }
    }

    private void OpenSearch(SearchPurpose purpose, string? initial = null, FolderState? folder = null)
    {
        CloseMenusExcept(SearchPopup); _searchPurpose = purpose; _searchFolder = folder;
        SearchPopup.HorizontalOffset = Math.Max(20, (SiteShell.ActualWidth - 660) / 2); SearchPopup.VerticalOffset = Math.Max(50, (SiteShell.ActualHeight - 450) / 2);
        SearchScopeText.Text = purpose switch
        {
            SearchPurpose.Navigate => "Перейти в этой вкладке",
            SearchPurpose.Split => "Открыть в Split View",
            SearchPurpose.Folder => "Добавить в папку",
            SearchPurpose.TabSearch => "Только открытые вкладки",
            _ => "Новая вкладка",
        };
        // The page is a native child window that owns the keyboard. While the palette is open it
        // must not: otherwise typing goes into the page and the field looks frozen.
        PaneHost.IsEnabled = false;
        var tabSearch = purpose == SearchPurpose.TabSearch;
        SearchHeaderTitle.Text = tabSearch ? "Поиск открытых вкладок" : "Навигация Luma";
        SearchHeaderHint.Text = tabSearch ? "Название, домен или адрес — без поиска в интернете" : "Адрес, история и сохранённые сайты";
        SearchPlaceholder.Text = tabSearch ? "Начните вводить название вкладки…" : "Поиск или адрес сайта";
        _searchPrevText = initial ?? ""; SearchBox.Text = initial ?? ""; UpdateSearchPlaceholder(); RefreshSearch(SearchBox.Text); SearchPopup.IsOpen = true;
        _ = FocusSearchBoxAsync(initial is not null);
    }
    private void UpdateSearchPlaceholder() => SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Claims the keyboard for the palette. WPF focus alone is not enough here: the popup is its
    /// own top-level window and the page is a native WebView2 window, so Windows keeps sending
    /// keystrokes to whichever native window holds focus. That is why clicking the domain pill
    /// sometimes opened a field that could not be typed in or cleared. Handing native focus to
    /// the popup window as well makes it deterministic.
    /// </summary>
    private async Task FocusSearchBoxAsync(bool selectAll)
    {
        foreach (var delay in new[] { 0, 30, 80, 160 })
        {
            if (delay > 0) await Task.Delay(delay);
            await Dispatcher.InvokeAsync(() =>
            {
                if (!SearchPopup.IsOpen) return;
                if (PresentationSource.FromVisual(SearchBox) is System.Windows.Interop.HwndSource source && source.Handle != IntPtr.Zero)
                {
                    SetForegroundWindow(source.Handle);
                    SetFocus(source.Handle);
                }
                SearchBox.Focus();
                FocusManager.SetFocusedElement(SearchPopup, SearchBox);
                Keyboard.Focus(SearchBox);
                if (selectAll) SearchBox.SelectAll();
                else SearchBox.CaretIndex = SearchBox.Text.Length;
            }, DispatcherPriority.Input);
        }
    }
    private async void SearchPopup_Opened(object? sender, EventArgs e) => await FocusSearchBoxAsync(!string.IsNullOrEmpty(SearchBox.Text));
    /// <summary>Host without "www.", lowercased. Empty when the url is not parseable.</summary>
    private static string HostKey(string url) => UrlTools.HostKey(url);

    /// <summary>
    /// How well a known place matches what has been typed so far. A prefix of the domain wins by
    /// a wide margin, because that is what people actually type: "yo" means youtube.com, not some
    /// page whose title happens to contain "yo". Returns -1 when there is no match at all.
    /// </summary>
    private static double MatchScore(string query, string title, string url) => UrlTools.MatchScore(query, title, url);

    /// <summary>
    /// Builds the suggestion list: open tabs, saved sites and history, ranked by how well they
    /// match plus how often and how recently they were visited. Raw "search the web" is only the
    /// top row when nothing known matches, so Enter usually opens the site you meant.
    /// </summary>
    private void RefreshSearch(string query)
    {
        _searchResults.Clear();
        var text = query.Trim();
        var lower = text.ToLowerInvariant();
        if (_searchPurpose == SearchPurpose.TabSearch)
        {
            foreach (var openTab in Tabs.Where(t => !t.IsHome && (lower.Length == 0 || t.DisplayTitle.Contains(lower, StringComparison.OrdinalIgnoreCase) || t.Domain.Contains(lower, StringComparison.OrdinalIgnoreCase) || t.ActiveUrl.Contains(lower, StringComparison.OrdinalIgnoreCase))))
                _searchResults.Add(new SearchSuggestion { Title = openTab.DisplayTitle, Subtitle = openTab.Domain, Url = openTab.ActiveUrl, ExistingTab = openTab, Kind = "tab" });
            if (_searchResults.Count > 0) SearchResultsList.SelectedIndex = 0;
            return;
        }
        var now = _clock.UtcNow;
        var ranked = new List<(double Score, SearchSuggestion Item)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Offer(double score, SearchSuggestion item)
        {
            if (score < 0 || string.IsNullOrWhiteSpace(item.Url)) return;
            if (!seen.Add(item.Url.TrimEnd('/'))) return;
            ranked.Add((score, item));
        }

        var visits = new Dictionary<string, (int Count, DateTime Last, string Title)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _state.History)
        {
            if (string.IsNullOrWhiteSpace(entry.Url)) continue;
            if (visits.TryGetValue(entry.Url, out var prev))
                visits[entry.Url] = (prev.Count + 1, entry.VisitedAt > prev.Last ? entry.VisitedAt : prev.Last, string.IsNullOrWhiteSpace(prev.Title) ? entry.Title : prev.Title);
            else visits[entry.Url] = (1, entry.VisitedAt, entry.Title);
        }

        // Frequency and recency, both capped so one heavily reloaded page cannot bury everything.
        double Popularity(string url)
        {
            var host = HostKey(url);
            double best = 0;
            foreach (var pair in visits)
            {
                var sameUrl = string.Equals(pair.Key, url, StringComparison.OrdinalIgnoreCase);
                if (!sameUrl && (host.Length == 0 || HostKey(pair.Key) != host)) continue;
                var days = Math.Max(0, (now - pair.Value.Last).TotalDays);
                var value = Math.Min(34, Math.Log(1 + pair.Value.Count) * 11) + Math.Min(26, 26 / (1 + days)) + (sameUrl ? 6 : 0);
                if (value > best) best = value;
            }
            return best;
        }

        foreach (var openTab in Tabs)
            Offer(MatchScore(lower, openTab.DisplayTitle, openTab.ActiveUrl) is var s && s < 0 ? -1 : s + 20 + Popularity(openTab.ActiveUrl),
                new SearchSuggestion { Title = openTab.DisplayTitle, Subtitle = Domain(openTab.ActiveUrl), Url = openTab.ActiveUrl, ExistingTab = openTab, Kind = "tab" });

        if (_spaces.Count > 0 && _activeSpace >= 0 && _activeSpace < _spaces.Count)
        {
            var space = _spaces[_activeSpace].State;
            foreach (var site in space.LooseItems.Concat(space.Folders.SelectMany(f => f.Items)))
                Offer(MatchScore(lower, site.Title, site.Url) is var s && s < 0 ? -1 : s + 14 + Popularity(site.Url),
                    new SearchSuggestion { Title = string.IsNullOrWhiteSpace(site.Title) ? Domain(site.Url) : site.Title, Subtitle = Domain(site.Url), Url = site.Url, Kind = "site" });
        }

        foreach (var closed in _state.RecentlyClosed.Take(12))
            Offer(MatchScore(lower, closed.Title, closed.Url) is var c && c < 0 ? -1 : c + 10 + Popularity(closed.Url),
                new SearchSuggestion { Title = string.IsNullOrWhiteSpace(closed.Title) ? Domain(closed.Url) : closed.Title, Subtitle = "Недавно закрытая", Url = closed.Url, Kind = "closed" });

        foreach (var pair in visits.OrderByDescending(p => p.Value.Last).Take(600))
        {
            var title = string.IsNullOrWhiteSpace(pair.Value.Title) ? Domain(pair.Key) : pair.Value.Title;
            Offer(MatchScore(lower, title, pair.Key) is var s && s < 0 ? -1 : s + Popularity(pair.Key),
                new SearchSuggestion { Title = title, Subtitle = pair.Key, Url = pair.Key, Kind = "history" });
        }

        var best = 0d;
        foreach (var row in ranked.OrderByDescending(r => r.Score).Take(text.Length == 0 ? 7 : 6))
        {
            if (row.Score > best) best = row.Score;
            _searchResults.Add(row.Item);
        }

        if (text.Length > 0)
        {
            var looksLikeAddress = (text.Contains('.') || text.Contains("://", StringComparison.Ordinal)) && !text.Contains(' ');
            var fallback = new SearchSuggestion
            {
                Title = looksLikeAddress ? text : $"Искать «{text}»",
                Subtitle = Normalize(text),
                Url = Normalize(text),
                Kind = looksLikeAddress ? "address" : "search",
            };
            // Only lead with the raw query when nothing familiar looks like a real hit.
            if (looksLikeAddress || best < 95) _searchResults.Insert(0, fallback);
            else _searchResults.Add(fallback);
        }

        if (_searchResults.Count > 0) SearchResultsList.SelectedIndex = 0;
    }

    /// <summary>
    /// Finishes the domain inline while typing ("yo" -> "youtube.com" with the tail selected), so
    /// Enter goes straight to the site. Skipped while deleting, so backspace still works normally.
    /// </summary>
    private void TryInlineComplete(string typed)
    {
        if (_searchPurpose == SearchPurpose.TabSearch) return;
        if (typed.Length == 0 || typed.Length < _searchPrevText.Length) return;
        if (SearchBox.CaretIndex != typed.Length || SearchBox.SelectionLength > 0) return;
        if (typed.Contains(' ') || typed.Contains('/') || typed.Contains("://", StringComparison.Ordinal)) return;
        var lower = typed.ToLowerInvariant();
        var hit = _searchResults.FirstOrDefault(s => s.Kind is "tab" or "site" or "history"
            && HostKey(s.Url).StartsWith(lower, StringComparison.Ordinal)
            && HostKey(s.Url).Length > lower.Length);
        if (hit is null) return;
        var host = HostKey(hit.Url);
        _searchSuppress = true;
        SearchBox.Text = typed + host[lower.Length..];
        SearchBox.Select(typed.Length, host.Length - lower.Length);
        _searchSuppress = false;
        SearchResultsList.SelectedItem = hit;
    }
    private async Task ConfirmSearchAsync()
    {
        var item = SearchResultsList.SelectedItem as SearchSuggestion ?? (string.IsNullOrWhiteSpace(SearchBox.Text) ? null : new SearchSuggestion { Url = Normalize(SearchBox.Text), Title = SearchBox.Text });
        if (item is null) return; var purpose = _searchPurpose; var folder = _searchFolder; _searchFolder = null; SearchPopup.IsOpen = false;
        if (purpose == SearchPurpose.TabSearch) { if (item.ExistingTab is not null) ActivateTabFromUser(item.ExistingTab); return; }
        if (item.ExistingTab is not null && purpose == SearchPurpose.Navigate) { ActivateTabFromUser(item.ExistingTab); return; }
        if (TryParseLumaSearchUrl(item.Url, out var searchQuery, out var searchMode)) { await OpenLumaSearchTabAsync(searchQuery, searchMode); return; }
        if (purpose == SearchPurpose.Navigate && CurrentTab is not null) CurrentTab.ActiveView.CoreWebView2?.Navigate(item.Url);
        else if (purpose == SearchPurpose.Split && CurrentTab is not null) await EnableSplitAsync(CurrentTab, item.Url, true);
        else if (purpose == SearchPurpose.Folder && folder is not null) { SaveSite(folder, item.Title, item.Url); folder.Expanded = true; await AddTabAsync(item.Url, true, folder.Id); Save(); RefreshFolders(); FilterTabs(); }
        else await AddTabAsync(item.Url);
    }

}
