using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Luma.Services;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace Luma;

public partial class MainWindow
{
    // =========================================================================
    // 1. SERVICES INITIALIZATION & FIELDS
    // =========================================================================
    private SoundscapeEngine? _soundscapeEngine;
    private TimeMachineService? _timeMachineService;
    private PageActionStudioService? _pageStudioService;
    private ScrapbookService? _scrapbookService;
    private GlancePreviewService? _glanceService;

    private ExtractedTable? _selectedExtractedTable;
    private TableExtractionResult? _lastExtractionResult;
    private bool _isReaderModeActive;
    private string? _preReaderUrl;
    private bool _isSyncScrollEnabled;
    private bool _isSyncingScroll;

    private void InitializeSuperFeatures()
    {
        try
        {
            _soundscapeEngine = new SoundscapeEngine();
            _timeMachineService = new TimeMachineService();
            _pageStudioService = new PageActionStudioService();
            _scrapbookService = new ScrapbookService();
            _glanceService = new GlancePreviewService();

            _soundscapeEngine.PlaybackStateChanged += OnSoundscapePlaybackChanged;
            _soundscapeEngine.TimerTicked += OnSoundscapeTimerTicked;
            _soundscapeEngine.TimerFinished += OnSoundscapeTimerFinished;
            _scrapbookService.ClipsChanged += OnScrapbookClipsChanged;
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    // =========================================================================
    // 2. DRAWER NAVIGATION & VIEW SWITCHING (<zen-library> — ZenLibrary.mjs)
    // =========================================================================
    private string _activeDrawerTag = "History";

    public void SwitchDrawerView(string tag)
    {
        _activeDrawerTag = string.IsNullOrWhiteSpace(tag) ? "History" : tag;

        if (!_isToolsDrawerOpen)
        {
            _isToolsDrawerOpen = true;
            AnimateToolsDrawer(true);
        }

        // Reset all navigation button backgrounds & labels (zen-library.css: active pill = #55555A)
        var inactiveBrush = Brushes.Transparent;
        var activeBrush = Brush("#55555A");

        DrawerNavMediaBtn.Background = inactiveBrush;
        DrawerNavDownloadsBtn.Background = inactiveBrush;
        DrawerNavBoostsBtn.Background = inactiveBrush;
        if (DrawerNavSpacesBtn is not null) DrawerNavSpacesBtn.Background = inactiveBrush;
        if (DrawerNavTimeMachineBtn is not null) DrawerNavTimeMachineBtn.Background = inactiveBrush;
        if (DrawerNavScrapbookBtn is not null) DrawerNavScrapbookBtn.Background = inactiveBrush;
        if (DrawerNavWhiteboardBtn is not null)
        {
            DrawerNavWhiteboardBtn.Background = inactiveBrush;
            DrawerNavWhiteboardBtn.IsChecked = (_activeDrawerTag == "Whiteboard");
        }
        DrawerNavHistoryBtn.Background = inactiveBrush;

        // Reset sprite icon fills & labels to inactive state (stroke #D0D0D5, transparent interior)
        SetLibraryTabVisualState(
            isMedia: _activeDrawerTag == "Media",
            isDownloads: _activeDrawerTag == "Downloads",
            isBoosts: _activeDrawerTag == "Boosts",
            isSpaces: _activeDrawerTag == "Spaces",
            isHistory: _activeDrawerTag is "History" or ""
        );

        // Hide all views first
        DrawerHistoryView.Visibility = Visibility.Collapsed;
        if (DrawerDownloadsView is not null) DrawerDownloadsView.Visibility = Visibility.Collapsed;
        DrawerSoundscapeView.Visibility = Visibility.Collapsed;
        DrawerBoostsView.Visibility = Visibility.Collapsed;
        if (DrawerSpacesView is not null) DrawerSpacesView.Visibility = Visibility.Collapsed;
        DrawerTimeMachineView.Visibility = Visibility.Collapsed;
        DrawerScrapbookView.Visibility = Visibility.Collapsed;
        if (DrawerWhiteboardView is not null) DrawerWhiteboardView.Visibility = Visibility.Collapsed;

        // Adjust <zen-library> width (ZenLibrarySpacesSection.mjs #updateLibraryWidth expands for Spaces)
        double targetWidth = _activeDrawerTag switch
        {
            "Spaces" => Math.Clamp(106 + Math.Max(1, _spaces.Count) * 258 + 56, 418, Math.Max(418, ActualWidth * 0.78)),
            "Whiteboard" => Math.Max(720, ActualWidth * 0.75),
            _ => 418
        };
        UpdateZenLibraryWidth(targetWidth);

        switch (_activeDrawerTag)
        {
            case "Media":
                DrawerNavMediaBtn.Background = activeBrush;
                DrawerSoundscapeView.Visibility = Visibility.Visible;
                RefreshZenMediaSection();
                UpdateSoundscapeUi();
                break;

            case "Downloads":
                DrawerNavDownloadsBtn.Background = activeBrush;
                if (DrawerDownloadsView is not null)
                {
                    DrawerDownloadsView.Visibility = Visibility.Visible;
                    PopulateDrawerDownloads();
                }
                break;

            case "Boosts":
                DrawerNavBoostsBtn.Background = activeBrush;
                DrawerBoostsView.Visibility = Visibility.Visible;
                RefreshPageStudioView();
                break;

            case "Spaces":
                if (DrawerNavSpacesBtn is not null) DrawerNavSpacesBtn.Background = activeBrush;
                if (DrawerSpacesView is not null)
                {
                    DrawerSpacesView.Visibility = Visibility.Visible;
                    PopulateDrawerSpaces();
                }
                break;

            case "TimeMachine":
                if (DrawerNavTimeMachineBtn is not null) DrawerNavTimeMachineBtn.Background = activeBrush;
                DrawerTimeMachineView.Visibility = Visibility.Visible;
                RefreshTimeMachineView();
                break;

            case "Scrapbook":
                if (DrawerNavScrapbookBtn is not null) DrawerNavScrapbookBtn.Background = activeBrush;
                DrawerScrapbookView.Visibility = Visibility.Visible;
                RefreshScrapbookView();
                break;

            case "Whiteboard":
                OpenWhiteboardTab();
                break;

            case "History":
            default:
                DrawerNavHistoryBtn.Background = activeBrush;
                DrawerHistoryView.Visibility = Visibility.Visible;
                PopulateDrawerHistory();
                break;
        }

        AnimateZenLibrarySectionTransition();
    }

    private void AnimateZenLibrarySectionTransition()
    {
        if (ZenLibraryContentGrid is null || ZenLibraryContentTranslate is null || !_state.AnimationsEnabled) return;
        ZenLibraryContentGrid.BeginAnimation(OpacityProperty, null);
        ZenLibraryContentTranslate.BeginAnimation(TranslateTransform.YProperty, null);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(135);
        ZenLibraryContentGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0.32, 1.0, dur) { EasingFunction = ease });
        ZenLibraryContentTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6.0, 0.0, dur) { EasingFunction = ease });
    }

    private void SetLibraryTabVisualState(bool isMedia, bool isDownloads, bool isBoosts, bool isSpaces, bool isHistory)
    {
        var activeStroke = Brush("#F2F2F5");
        var inactiveStroke = Brush("#D0D0D5");
        var activeFill = Brush("#75777E");
        var activeSubFill = Brush("#4E5058");
        var inactiveCardFill = Brush("#444449");

        // Media sprite
        if (LibMediaBackCard is not null)
        {
            LibMediaBackCard.Fill = isMedia ? activeSubFill : inactiveCardFill;
            LibMediaBackCard.Stroke = isMedia ? activeStroke : inactiveStroke;
        }
        if (LibMediaFrontCard is not null)
        {
            LibMediaFrontCard.Fill = isMedia ? activeFill : inactiveCardFill;
            LibMediaFrontCard.Stroke = isMedia ? activeStroke : inactiveStroke;
        }
        if (LibMediaSun is not null) LibMediaSun.Fill = isMedia ? activeStroke : inactiveStroke;
        if (LibMediaMountain is not null) LibMediaMountain.Fill = isMedia ? activeStroke : inactiveStroke;
        if (LibMediaLabel is not null)
        {
            LibMediaLabel.Foreground = isMedia ? activeStroke : Brush("#D4D4D8");
            LibMediaLabel.FontWeight = isMedia ? FontWeights.SemiBold : FontWeights.Medium;
        }

        // Downloads sprite
        if (LibDownloadsCircle is not null)
        {
            LibDownloadsCircle.Fill = isDownloads ? activeFill : Brushes.Transparent;
            LibDownloadsCircle.Stroke = isDownloads ? activeStroke : inactiveStroke;
        }
        if (LibDownloadsArrow is not null) LibDownloadsArrow.Stroke = isDownloads ? activeStroke : inactiveStroke;
        if (LibDownloadsLabel is not null)
        {
            LibDownloadsLabel.Foreground = isDownloads ? activeStroke : Brush("#D4D4D8");
            LibDownloadsLabel.FontWeight = isDownloads ? FontWeights.SemiBold : FontWeights.Medium;
        }

        // Boosts sprite
        if (LibBoostsCard is not null)
        {
            LibBoostsCard.Fill = isBoosts ? activeFill : Brushes.Transparent;
            LibBoostsCard.Stroke = isBoosts ? activeStroke : inactiveStroke;
        }
        if (LibBoostsBrush is not null) LibBoostsBrush.Fill = isBoosts ? activeStroke : inactiveStroke;
        if (LibBoostsLabel is not null)
        {
            LibBoostsLabel.Foreground = isBoosts ? activeStroke : Brush("#D4D4D8");
            LibBoostsLabel.FontWeight = isBoosts ? FontWeights.SemiBold : FontWeights.Medium;
        }

        // Spaces sprite
        if (LibSpacesBackCard is not null)
        {
            LibSpacesBackCard.Fill = isSpaces ? activeSubFill : inactiveCardFill;
            LibSpacesBackCard.Stroke = isSpaces ? activeStroke : inactiveStroke;
        }
        if (LibSpacesFrontCard is not null)
        {
            LibSpacesFrontCard.Fill = isSpaces ? activeFill : inactiveCardFill;
            LibSpacesFrontCard.Stroke = isSpaces ? activeStroke : inactiveStroke;
        }
        if (LibSpacesLabel is not null)
        {
            LibSpacesLabel.Foreground = isSpaces ? activeStroke : Brush("#D4D4D8");
            LibSpacesLabel.FontWeight = isSpaces ? FontWeights.SemiBold : FontWeights.Medium;
        }

        // History sprite
        if (LibHistoryLid is not null)
        {
            LibHistoryLid.Fill = isHistory ? activeFill : Brushes.Transparent;
            LibHistoryLid.Stroke = isHistory ? activeStroke : inactiveStroke;
        }
        if (LibHistoryBody is not null)
        {
            LibHistoryBody.Fill = isHistory ? activeFill : Brushes.Transparent;
            LibHistoryBody.Stroke = isHistory ? activeStroke : inactiveStroke;
        }
        if (LibHistoryDash is not null) LibHistoryDash.Stroke = isHistory ? activeStroke : inactiveStroke;
        if (LibHistoryLabel is not null)
        {
            LibHistoryLabel.Foreground = isHistory ? activeStroke : Brush("#D4D4D8");
            LibHistoryLabel.FontWeight = isHistory ? FontWeights.SemiBold : FontWeights.Medium;
        }
    }

    // =========================================================================
    // 3. SOUNDSCAPE ENGINE (PROCEDURAL AUDIO SYNTHESIZER)
    // =========================================================================
    private void SoundscapePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_soundscapeEngine is null) return;
        if (sender is not Button btn || btn.Tag is not string tag) return;

        if (Enum.TryParse<SoundscapePreset>(tag, out var preset))
        {
            _soundscapeEngine.SetPreset(preset);
            UpdateSoundscapePresetButtons(preset);
        }
    }

    private void UpdateSoundscapePresetButtons(SoundscapePreset current)
    {
        var activeBg = (Brush)FindResource("SidebarItemActiveBrush");
        var defaultBg = (Brush)FindResource("InputBackgroundBrush");

        SoundscapePresetAlphaBtn.Background = current == SoundscapePreset.AlphaFocus432Hz ? activeBg : defaultBg;
        SoundscapePresetRainBtn.Background = current == SoundscapePreset.CyberpunkRain ? activeBg : defaultBg;
        SoundscapePresetBrownBtn.Background = current == SoundscapePreset.DeepBrownNoise ? activeBg : defaultBg;
        SoundscapePresetCoffeeBtn.Background = current == SoundscapePreset.CoffeeShopVinyl ? activeBg : defaultBg;
    }

    private void SoundscapePlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_soundscapeEngine is null) return;
        _soundscapeEngine.TogglePlayPause();
    }

    private void OnSoundscapePlaybackChanged(bool isPlaying)
    {
        Dispatcher.Invoke(() =>
        {
            SoundscapePlayText.Text = isPlaying ? "Pause Soundscape" : "Start Soundscape";
            SoundscapePlayIcon.Data = (Geometry)FindResource(isPlaying ? "IconPause" : "IconPlay");
            SoundscapePlayPauseBtn.Background = isPlaying ? (Brush)new BrushConverter().ConvertFromString("#10B981")! : (Brush)FindResource("AccentBrush");
        });
    }

    private void SoundscapeVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SoundscapeVolumeText is not null)
        {
            SoundscapeVolumeText.Text = $"{(int)e.NewValue}%";
        }
        if (_soundscapeEngine is not null)
        {
            _soundscapeEngine.Volume = e.NewValue / 100.0;
        }
    }

    private void SoundscapeTimer25_Click(object sender, RoutedEventArgs e)
    {
        _soundscapeEngine?.StartFocusTimer(25);
        ShowToast("Focus Session Started", "25 minutes alpha concentration", false);
    }

    private void SoundscapeTimer50_Click(object sender, RoutedEventArgs e)
    {
        _soundscapeEngine?.StartFocusTimer(50);
        ShowToast("Deep Focus Session Started", "50 minutes hyperfocus", false);
    }

    private void SoundscapeTimerStop_Click(object sender, RoutedEventArgs e)
    {
        _soundscapeEngine?.StopFocusTimer();
        SoundscapeTimerDisplay.Text = "Timer Idle";
    }

    private void OnSoundscapeTimerTicked(TimeSpan remaining)
    {
        Dispatcher.Invoke(() =>
        {
            SoundscapeTimerDisplay.Text = $"{remaining:mm\\:ss}";
        });
    }

    private void OnSoundscapeTimerFinished()
    {
        Dispatcher.Invoke(() =>
        {
            SoundscapeTimerDisplay.Text = "Completed!";
            ShowToast("Focus Session Complete", "Time for a rejuvenating break", false);
        });
    }

    private void UpdateSoundscapeUi()
    {
        if (_soundscapeEngine is null) return;
        UpdateSoundscapePresetButtons(_soundscapeEngine.CurrentPreset);
        SoundscapeVolumeSlider.Value = _soundscapeEngine.Volume * 100.0;
        OnSoundscapePlaybackChanged(_soundscapeEngine.IsPlaying);
    }

    // =========================================================================
    // 4. PAGE ACTION STUDIO (DOM EXTRACTOR & CUSTOM BOOSTS)
    // =========================================================================
    private void RefreshPageStudioView()
    {
        var tab = CurrentTab;
        if (tab is null) return;

        var host = tab.Domain;
        if (_pageStudioService is not null)
        {
            var savedCss = _pageStudioService.GetBoostForDomain(host);
            PageStudioCssEditor.Text = savedCss;
        }
    }

    private async void PageStudioExtractTables_Click(object sender, RoutedEventArgs e)
    {
        var tab = CurrentTab;
        if (tab?.ActiveView.CoreWebView2 is null)
        {
            ShowToast("Extractor Unavailable", "No active page to analyze", true);
            return;
        }

        try
        {
            PageStudioExtractStatusText.Text = "Scanning DOM for tables & grids...";
            var rawJson = await tab.ActiveView.CoreWebView2.ExecuteScriptAsync(PageActionStudioService.TableScraperScript);

            // ExecuteScriptAsync returns JSON-encoded string
            var unescaped = JsonSerializer.Deserialize<string>(rawJson);
            if (string.IsNullOrEmpty(unescaped))
            {
                PageStudioExtractStatusText.Text = "No structured tabular data found.";
                return;
            }

            var result = JsonSerializer.Deserialize<TableExtractionResult>(unescaped);
            _lastExtractionResult = result;

            if (result is null || result.Tables.Count == 0)
            {
                PageStudioExtractStatusText.Text = "No tables or grids found on this page.";
                PageStudioExportButtonsPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                _selectedExtractedTable = result.Tables[0];
                int totalRows = result.Tables.Sum(t => t.RowCount);
                PageStudioExtractStatusText.Text = $"Found {result.Tables.Count} table(s) with {totalRows} total data rows!";
                PageStudioExportButtonsPanel.Visibility = Visibility.Visible;
                ShowToast("Data Extracted", $"{result.Tables.Count} table(s) ready to export", false);
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
            PageStudioExtractStatusText.Text = $"Extraction failed: {ex.Message}";
        }
    }

    private void PageStudioExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedExtractedTable is null) return;
        var csv = PageActionStudioService.TableToCsv(_selectedExtractedTable);
        Copy(csv, "Table CSV copied to clipboard");
    }

    private void PageStudioExportMarkdown_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedExtractedTable is null) return;
        var md = PageActionStudioService.TableToMarkdown(_selectedExtractedTable);
        Copy(md, "Markdown Table copied to clipboard");
    }

    private void PageStudioExportJson_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedExtractedTable is null) return;
        var json = PageActionStudioService.TableToJson(_selectedExtractedTable);
        Copy(json, "JSON data copied to clipboard");
    }

    private async void PageStudioToggleReader_Click(object sender, RoutedEventArgs e)
    {
        var tab = CurrentTab;
        if (tab?.ActiveView.CoreWebView2 is null) return;

        if (_isReaderModeActive && !string.IsNullOrEmpty(_preReaderUrl))
        {
            // Restore normal view
            _isReaderModeActive = false;
            tab.ActiveView.CoreWebView2.Navigate(_preReaderUrl);
            _preReaderUrl = null;
            ShowToast("Reader Mode", "Standard layout restored", false);
            return;
        }

        try
        {
            var rawJson = await tab.ActiveView.CoreWebView2.ExecuteScriptAsync(PageActionStudioService.ReaderModeExtractScript);
            var unescaped = JsonSerializer.Deserialize<string>(rawJson);
            if (string.IsNullOrEmpty(unescaped)) return;

            var data = JsonSerializer.Deserialize<ReaderModeData>(unescaped);
            if (data is null || string.IsNullOrWhiteSpace(data.ContentHtml))
            {
                ShowToast("Pure Reader", "Could not identify an article on this page", true);
                return;
            }

            _preReaderUrl = tab.FullUrl;
            _isReaderModeActive = true;

            var readerHtml = PageActionStudioService.GenerateReaderDocumentHtml(data);
            tab.ActiveView.CoreWebView2.NavigateToString(readerHtml);
            ShowToast("Pure Reader Activated", $"{data.WordCount} words (~{data.EstimatedReadingMinutes} min)", false);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("Reader Mode Error", ex.Message, true);
        }
    }

    private async void PageStudioBoostPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string presetName) return;
        if (PageActionStudioService.BoostPresets.TryGetValue(presetName, out var css))
        {
            PageStudioCssEditor.Text = css;
            await ApplyCurrentStudioCssAsync(css);
        }
    }

    private async void PageStudioApplyCss_Click(object sender, RoutedEventArgs e)
    {
        var css = PageStudioCssEditor.Text;
        await ApplyCurrentStudioCssAsync(css);
    }

    private async Task ApplyCurrentStudioCssAsync(string css)
    {
        var tab = CurrentTab;
        if (tab?.ActiveView.CoreWebView2 is null) return;

        var host = tab.Domain;
        _pageStudioService?.SaveBoostForDomain(host, css);

        var script = PageActionStudioService.CreateInjectCssScript(css);
        await tab.ActiveView.CoreWebView2.ExecuteScriptAsync(script);
        ShowToast("Boost Applied", $"Live CSS injected into {host}", false);
    }

    private async void PageStudioClearCss_Click(object sender, RoutedEventArgs e)
    {
        var tab = CurrentTab;
        if (tab?.ActiveView.CoreWebView2 is null) return;

        var host = tab.Domain;
        _pageStudioService?.SaveBoostForDomain(host, "");
        PageStudioCssEditor.Text = "";

        var script = PageActionStudioService.CreateRemoveCssScript();
        await tab.ActiveView.CoreWebView2.ExecuteScriptAsync(script);
        ShowToast("Boost Cleared", $"Styles reset on {host}", false);
    }

    // =========================================================================
    // 5. OFFLINE TIME MACHINE (DOM SNAPSHOTS & DELTA HISTORY)
    // =========================================================================
    private void RefreshTimeMachineView()
    {
        if (_timeMachineService is null) return;
        var tab = CurrentTab;
        var host = tab?.Domain ?? "";

        var snapshots = string.IsNullOrEmpty(host)
            ? _timeMachineService.GetAllSnapshots()
            : _timeMachineService.GetSnapshotsForHost(host);

        TimeMachineSnapshotsList.ItemsSource = snapshots;
        TimeMachineStatusText.Text = snapshots.Count > 0
            ? $"{snapshots.Count} snapshot(s) stored locally for {host}"
            : $"No snapshots yet for {host}";
    }

    private async void TimeMachineSnapshotNow_Click(object sender, RoutedEventArgs e)
    {
        var tab = CurrentTab;
        if (tab?.ActiveView.CoreWebView2 is null)
        {
            ShowToast("Snapshot Failed", "No active page", true);
            return;
        }

        try
        {
            TimeMachineStatusText.Text = "Capturing DOM snapshot...";
            var rawJson = await tab.ActiveView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
            var html = JsonSerializer.Deserialize<string>(rawJson);

            if (string.IsNullOrEmpty(html))
            {
                TimeMachineStatusText.Text = "Failed to capture page DOM.";
                return;
            }

            var meta = await _timeMachineService!.CaptureSnapshotAsync(tab.FullUrl, tab.Title, html);
            RefreshTimeMachineView();
            ShowToast("Snapshot Saved", $"Compressed into {meta?.DisplaySize}", false);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("Snapshot Error", ex.Message, true);
        }
    }

    private async void TimeMachineCompareLive_Click(object sender, RoutedEventArgs e)
    {
        var tab = CurrentTab;
        if (tab?.ActiveView.CoreWebView2 is null || _timeMachineService is null) return;

        var snapshots = _timeMachineService.GetSnapshotsForHost(tab.Domain);
        if (snapshots.Count == 0)
        {
            ShowToast("No Baseline Snapshot", "Take a snapshot first to compute diffs", true);
            return;
        }

        try
        {
            TimeMachineStatusText.Text = "Computing live LCS delta...";
            var rawJson = await tab.ActiveView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
            var liveHtml = JsonSerializer.Deserialize<string>(rawJson) ?? "";

            var latest = snapshots[0];
            var diff = await _timeMachineService.CompareWithLiveAsync(latest, liveHtml);

            TimeMachineDiffSummary.Text = diff.Summary;
            TimeMachineDiffLinesControl.ItemsSource = diff.Lines;
            TimeMachineDiffContainer.Visibility = Visibility.Visible;
            ShowToast("Delta Computed", diff.Summary, false);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("Diff Error", ex.Message, true);
        }
    }

    private async void TimeMachineItemDiff_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not TimeSnapshotMetadata selected) return;
        if (_timeMachineService is null) return;

        var snapshots = _timeMachineService.GetSnapshotsForHost(selected.Host);
        var target = snapshots.FirstOrDefault(s => s.Id != selected.Id) ?? selected;

        try
        {
            var diff = await _timeMachineService.CompareSnapshotsAsync(selected, target);
            TimeMachineDiffSummary.Text = diff.Summary;
            TimeMachineDiffLinesControl.ItemsSource = diff.Lines;
            TimeMachineDiffContainer.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private async void TimeMachineItemReplay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not TimeSnapshotMetadata selected) return;
        if (_timeMachineService is null) return;

        var tab = CurrentTab;
        if (tab?.ActiveView.CoreWebView2 is null) return;

        try
        {
            var html = await _timeMachineService.ReadSnapshotHtmlAsync(selected);
            tab.ActiveView.CoreWebView2.NavigateToString(html);
            ShowToast("Offline Snapshot Loaded", $"Archived on {selected.DisplayTime}", false);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("Replay Failed", ex.Message, true);
        }
    }

    // =========================================================================
    // 6. OMNI-SCRAPBOOK KNOWLEDGE STUDIO
    // =========================================================================
    private void RefreshScrapbookView()
    {
        if (_scrapbookService is null) return;
        var query = ScrapbookSearchBox?.Text?.Trim() ?? "";
        var clips = _scrapbookService.SearchClips(query);
        ScrapbookClipsList.ItemsSource = clips;
    }

    private void OnScrapbookClipsChanged()
    {
        Dispatcher.Invoke(RefreshScrapbookView);
    }

    private async void ScrapbookClipSelection_Click(object sender, RoutedEventArgs e)
    {
        var tab = CurrentTab;
        if (tab?.ActiveView.CoreWebView2 is null || _scrapbookService is null)
        {
            ShowToast("Clip Unavailable", "No active page open", true);
            return;
        }

        try
        {
            var rawJson = await tab.ActiveView.CoreWebView2.ExecuteScriptAsync("window.getSelection().toString()");
            var text = JsonSerializer.Deserialize<string>(rawJson)?.Trim();

            if (string.IsNullOrEmpty(text))
            {
                // Fallback to title
                text = tab.Title;
            }

            var clip = _scrapbookService.AddClip(tab.Title, tab.FullUrl, text);
            RefreshScrapbookView();
            ShowToast("Clipped to Scrapbook", clip.PreviewSnippet, false);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("Clip Error", ex.Message, true);
        }
    }

    private void ScrapbookExportDossier_Click(object sender, RoutedEventArgs e)
    {
        if (_scrapbookService is null) return;
        var dossier = _scrapbookService.GenerateDossierMarkdown();
        Copy(dossier, "Complete Research Dossier copied to clipboard!");
    }

    private void ScrapbookSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshScrapbookView();
    }

    private void ScrapbookCopyClip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string text)
        {
            Copy(text, "Clip text copied");
        }
    }

    private void ScrapbookDeleteClip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            _scrapbookService?.DeleteClip(id);
        }
    }

    // =========================================================================
    // 7. DUAL-VIEW SYNCHRONIZED SCROLLING (SPLIT VIEW)
    // =========================================================================
    public void ToggleSyncScroll()
    {
        var tab = CurrentTab;
        if (tab is null || !tab.IsSplit || tab.SecondaryView?.CoreWebView2 is null)
        {
            ShowToast("Dual Scroll", "Open Split View first to sync scrolling", true);
            return;
        }

        _isSyncScrollEnabled = !_isSyncScrollEnabled;
        if (_isSyncScrollEnabled)
        {
            SetupSyncScroll(tab);
            ShowToast("Sync Scroll Enabled", "Dual view scrolling locked in sync", false);
        }
        else
        {
            ShowToast("Sync Scroll Disabled", "Independent scrolling restored", false);
        }
    }

    private void SetupSyncScroll(BrowserTab tab)
    {
        if (tab.View.CoreWebView2 is null || tab.SecondaryView?.CoreWebView2 is null) return;

        const string script = """
        (() => {
            if (window._lumaScrollWatcher) return;
            window._lumaScrollWatcher = true;
            window.addEventListener('scroll', () => {
                if (window._lumaIgnoreScroll) return;
                const max = document.documentElement.scrollHeight - window.innerHeight;
                const ratio = max > 0 ? (window.scrollY / max) : 0;
                window.chrome.webview.postMessage(JSON.stringify({ type: 'LUMA_SCROLL_SYNC', ratio: ratio }));
            }, { passive: true });
        })();
        """;

        _ = tab.View.CoreWebView2.ExecuteScriptAsync(script);
        _ = tab.SecondaryView.CoreWebView2.ExecuteScriptAsync(script);

        tab.View.CoreWebView2.WebMessageReceived -= OnPrimaryWebMessageReceived;
        tab.View.CoreWebView2.WebMessageReceived += OnPrimaryWebMessageReceived;

        tab.SecondaryView.CoreWebView2.WebMessageReceived -= OnSecondaryWebMessageReceived;
        tab.SecondaryView.CoreWebView2.WebMessageReceived += OnSecondaryWebMessageReceived;
    }

    private void OnPrimaryWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!_isSyncScrollEnabled || _isSyncingScroll) return;
        var tab = CurrentTab;
        if (tab?.SecondaryView?.CoreWebView2 is null) return;

        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            if (doc.RootElement.TryGetProperty("type", out var type) && type.GetString() == "LUMA_SCROLL_SYNC")
            {
                double ratio = doc.RootElement.GetProperty("ratio").GetDouble();
                _isSyncingScroll = true;
                _ = tab.SecondaryView.CoreWebView2.ExecuteScriptAsync($$"""
                (() => {
                    window._lumaIgnoreScroll = true;
                    const max = document.documentElement.scrollHeight - window.innerHeight;
                    window.scrollTo({ top: {{ratio}} * max, behavior: 'instant' });
                    setTimeout(() => { window._lumaIgnoreScroll = false; }, 50);
                })();
                """);
                _isSyncingScroll = false;
            }
        }
        catch { _isSyncingScroll = false; }
    }

    private void OnSecondaryWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!_isSyncScrollEnabled || _isSyncingScroll) return;
        var tab = CurrentTab;
        if (tab?.View?.CoreWebView2 is null) return;

        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            if (doc.RootElement.TryGetProperty("type", out var type) && type.GetString() == "LUMA_SCROLL_SYNC")
            {
                double ratio = doc.RootElement.GetProperty("ratio").GetDouble();
                _isSyncingScroll = true;
                _ = tab.View.CoreWebView2.ExecuteScriptAsync($$"""
                (() => {
                    window._lumaIgnoreScroll = true;
                    const max = document.documentElement.scrollHeight - window.innerHeight;
                    window.scrollTo({ top: {{ratio}} * max, behavior: 'instant' });
                    setTimeout(() => { window._lumaIgnoreScroll = false; }, 50);
                })();
                """);
                _isSyncingScroll = false;
            }
        }
        catch { _isSyncingScroll = false; }
    }
}
