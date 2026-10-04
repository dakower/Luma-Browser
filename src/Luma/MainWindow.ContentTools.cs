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
    private async Task AskLumaAsync(string prompt)
    {
        await ToggleAssistantAsync(true);
        for (var attempt = 0; attempt < 40 && !_assistantReady; attempt++) await Task.Delay(50);
        UpdateAssistantContext();
        PostAssistant(new { kind = "ask", text = prompt });
    }

    /// <summary>Distraction-free reading overlay painted in the browser palette.</summary>
    private async Task ToggleReaderAsync(WebView2? view)
    {
        if (view?.CoreWebView2 is null) return;
        var accentColor = ((SolidColorBrush)Resources["AccentBrush"]).Color;
        var accentHex = $"#{accentColor.R:X2}{accentColor.G:X2}{accentColor.B:X2}";
        var raw = await Script(view, BrowserScripts.Reader.Replace("__LUMA_READER_ACCENT__", accentHex, StringComparison.Ordinal));
        var result = JsonSerializer.Deserialize<string>(raw) ?? "";
        if (result == "none") ShowToast("\u0420\u0435\u0436\u0438\u043c \u0447\u0442\u0435\u043d\u0438\u044f", "\u041d\u0430 \u044d\u0442\u043e\u0439 \u0441\u0442\u0440\u0430\u043d\u0438\u0446\u0435 \u043d\u0435\u0447\u0435\u0433\u043e \u0447\u0438\u0442\u0430\u0442\u044c", true);
    }

    /// <summary>Wipes cookies, storage and history for one site only — the rest is untouched.</summary>
    private async Task ForgetSiteAsync(WebView2? view)
    {
        var core = view?.CoreWebView2;
        if (core is null || !Uri.TryCreate(view!.Source?.ToString() ?? "", UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
        var origin = uri.GetLeftPart(UriPartial.Authority);
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        try
        {
            await core.CallDevToolsProtocolMethodAsync("Storage.clearDataForOrigin", JsonSerializer.Serialize(new { origin, storageTypes = "all" }));
            await core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
            var cookies = await core.CallDevToolsProtocolMethodAsync("Network.getCookies", JsonSerializer.Serialize(new { urls = new[] { origin + "/" } }));
            using (var doc = JsonDocument.Parse(cookies))
            {
                if (doc.RootElement.TryGetProperty("cookies", out var list))
                    foreach (var cookie in list.EnumerateArray())
                    {
                        var payload = JsonSerializer.Serialize(new
                        {
                            name = cookie.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            domain = cookie.TryGetProperty("domain", out var d) ? d.GetString() ?? "" : "",
                            path = cookie.TryGetProperty("path", out var p) ? p.GetString() ?? "/" : "/",
                        });
                        await core.CallDevToolsProtocolMethodAsync("Network.deleteCookies", payload);
                    }
            }
            var removed = _state.History.RemoveAll(entry => UrlTools.SameSite(entry.Url, "https://" + host) || (Uri.TryCreate(entry.Url, UriKind.Absolute, out var u) && string.Equals(u.Host.Replace("www.", ""), host.Replace("www.", ""), StringComparison.OrdinalIgnoreCase)));
            if (removed > 0) _stateStore.Save();
            core.Reload();
            ShowToast("\u0421\u0430\u0439\u0442 \u0437\u0430\u0431\u044b\u0442", host);
        }
        catch (Exception ex) { App.Log(ex); ShowToast("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u043e\u0447\u0438\u0441\u0442\u0438\u0442\u044c \u0441\u0430\u0439\u0442", ex.Message, true); }
    }

    /// <summary>Called for every incoming luma-media-state message. Updates the source tab's media snapshot
    /// and lets <see cref="RefreshNowPlaying"/> decide whether the titlebar widget should track this tab.</summary>
    private void UpdateMediaState(BrowserTab tab, bool playing, string title, string artist, string artwork, double position, double duration, double volume, bool isVideo)
    {
        // YouTube Music and Spotify may render audio through WebAudio/DRM while exposing a
        // paused helper <audio> element. WebView2 still knows that the document is audible, so
        // never let that helper's false report hide the Luma music widget.
        if (!playing && IsSupportedMusicService(tab))
        {
            try { playing = tab.ActiveView.CoreWebView2?.IsDocumentPlayingAudio == true; }
            catch { }
        }
        tab.MediaPlaying = playing;
        tab.MediaIsVideo = isVideo;
        if (playing)
        {
            tab.MediaTitle = string.IsNullOrWhiteSpace(title) ? tab.DisplayTitle : title;
            tab.MediaArtist = artist;
            tab.MediaArtwork = artwork;
            tab.MediaPosition = position;
            tab.MediaDuration = duration;
            tab.MediaVolume = volume;
        }
        RefreshNowPlaying();
        RefreshFloatingMusic();
        if (ReferenceEquals(tab, _floatingVideoTab)) _floatingVideo?.Update(tab);
    }

    /// <summary>Picks which tab's playback the compact titlebar widget follows. A tab that is
    /// actively playing right now always takes over (so starting a new track elsewhere switches
    /// the widget to it), but pausing the tracked tab does NOT drop it — the widget keeps
    /// showing it (with a "play" icon) until either another tab starts playing or the tracked
    /// tab is closed, exactly like Chrome's media widget behaves.</summary>
    private void RefreshNowPlaying()
    {
        var playingNow = _spaces.SelectMany(s => s.Tabs).FirstOrDefault(t => t.MediaPlaying);
        if (playingNow is not null) _nowPlaying = playingNow;
        if (_nowPlaying is null) { NowPlayingWidget.Visibility = Visibility.Collapsed; if (HomeMediaCard is not null) HomeMediaCard.Visibility = Visibility.Collapsed; SetNowPlayingTray(false, false); return; }
        NowPlayingWidget.Visibility = Visibility.Visible;
        if (HomeMediaCard is not null) HomeMediaCard.Visibility = _state.HomeShowMedia ? Visibility.Visible : Visibility.Collapsed;
        SyncNowPlayingUi();
    }

    /// <summary>Pushes <see cref="_nowPlaying"/>'s current values into the widget/popup. No XAML bindings are
    /// used here on purpose — everything is set procedurally on named elements to avoid Popup DataContext quirks.</summary>
    private void SyncNowPlayingUi()
    {
        var tab = _nowPlaying;
        if (tab is null) return;
        var displayTitle = string.IsNullOrWhiteSpace(tab.MediaTitle) ? "Воспроизведение" : tab.MediaTitle;
        NowPlayingPillTitle.Text = displayTitle;
        NowPlayingPillArtist.Text = string.IsNullOrWhiteSpace(tab.MediaArtist) ? tab.Domain : tab.MediaArtist;
        NowPlayingPopupTitle.Text = displayTitle;
        NowPlayingPopupArtist.Text = tab.MediaArtist;
        NowPlayingPopupArtist.Visibility = string.IsNullOrWhiteSpace(tab.MediaArtist) ? Visibility.Collapsed : Visibility.Visible;
        // The real cover art stands in for the icon whenever the site provides one; the
        // note glyph is only a placeholder for tracks without artwork.
        var artSource = string.IsNullOrWhiteSpace(tab.MediaArtwork) ? tab.FaviconUrl : tab.MediaArtwork;
        var hasArt = !string.IsNullOrWhiteSpace(artSource);
        if (hasArt && (!string.Equals(_loadedArtworkSource, artSource, StringComparison.Ordinal) || NowPlayingArt.Source is null))
        {
            try
            {
                var bmp = new BitmapImage(); bmp.BeginInit(); bmp.DecodePixelWidth = 240; bmp.UriSource = new Uri(artSource, UriKind.RelativeOrAbsolute); bmp.EndInit();
                _loadedArtworkSource = artSource;
                NowPlayingArt.Source = bmp; NowPlayingPillArt.Source = bmp;
            }
            catch { hasArt = false; _loadedArtworkSource = ""; }
        }
        if (hasArt)
        {
            NowPlayingArt.Visibility = Visibility.Visible; NowPlayingPopupFallbackIcon.Visibility = Visibility.Collapsed;
            NowPlayingPillArt.Visibility = Visibility.Visible; NowPlayingPillFallbackIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            _loadedArtworkSource = ""; NowPlayingArt.Source = NowPlayingPillArt.Source = null;
            NowPlayingArt.Visibility = Visibility.Collapsed; NowPlayingPopupFallbackIcon.Visibility = Visibility.Visible;
            NowPlayingPillArt.Visibility = Visibility.Collapsed; NowPlayingPillFallbackIcon.Visibility = Visibility.Visible;
        }
        NowPlayingPos.Text = tab.MediaPositionText;
        NowPlayingDur.Text = tab.MediaDurationText;
        if (!_seekDragging) { SeekFill.Width = Math.Clamp(tab.MediaProgress, 0, 1) * Math.Max(1, SeekTrack.ActualWidth); PositionThumb(SeekThumb, SeekFill.Width); }
        if (!_volumeDragging) { VolumeFill.Width = Math.Clamp(tab.MediaVolume, 0, 1) * Math.Max(1, VolumeTrack.ActualWidth); PositionThumb(VolumeThumb, VolumeFill.Width); }
        var playIcon = tab.MediaPlaying ? "IconPause" : "IconPlay";
        var geometry = (Geometry)FindResource(playIcon);
        NowPlayingToggleIcon.Data = geometry;
        NowPlayingToggleIconPopup.Data = geometry;
        try
        {
            if (NowPlayingPillProgress is not null && NowPlayingWidget is not null)
            {
                var targetWidth = Math.Clamp(tab.MediaProgress, 0, 1) * Math.Max(0, NowPlayingWidget.ActualWidth - 24);
                NowPlayingPillProgress.Width = targetWidth;
            }
            if (tab.MediaPlaying)
            {
                AnimateEqualizer(true);
            }
            else
            {
                AnimateEqualizer(false);
            }
        }
        catch { }
        if (HomeMediaCard is not null)
        {
            HomeMediaCard.Visibility = _state.HomeShowMedia ? Visibility.Visible : Visibility.Collapsed;
            HomeMediaTitle.Text = displayTitle;
            HomeMediaArtist.Text = string.IsNullOrWhiteSpace(tab.MediaArtist) ? tab.Domain : tab.MediaArtist;
            HomeMediaToggleIcon.Data = geometry;
            if (hasArt && NowPlayingArt.Source is not null)
            {
                HomeMediaArt.Source = NowPlayingArt.Source;
                HomeMediaArt.Visibility = Visibility.Visible;
                HomeMediaFallbackIcon.Visibility = Visibility.Collapsed;
            }
            else
            {
                HomeMediaArt.Source = null;
                HomeMediaArt.Visibility = Visibility.Collapsed;
                HomeMediaFallbackIcon.Visibility = Visibility.Visible;
            }
        }
        _floatingMusic?.Update(tab);
    }

    private bool _equalizerRunning;
    private void AnimateEqualizer(bool play)
    {
        if (EqBar1 is null || EqBar2 is null || EqBar3 is null) return;
        if (!play)
        {
            _equalizerRunning = false;
            EqBar1.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            EqBar2.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            EqBar3.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            EqBar1.ScaleY = 0.3;
            EqBar2.ScaleY = 0.3;
            EqBar3.ScaleY = 0.3;
            return;
        }
        if (_equalizerRunning) return;
        _equalizerRunning = true;
        void StartBar(ScaleTransform st, double from, double to, int ms)
        {
            var anim = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(ms)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }
        StartBar(EqBar1, 0.25, 0.95, 380);
        StartBar(EqBar2, 0.4, 1.0, 270);
        StartBar(EqBar3, 0.2, 0.85, 430);
    }

    /// <summary>Sends a control command to whichever tab's page script is currently driving <see cref="_nowPlaying"/>.</summary>
    private void SendMediaCommand(string action, double value = 0)
    {
        var core = _nowPlaying?.ActiveView.CoreWebView2;
        if (core is null) return;
        try { core.PostWebMessageAsJson(JsonSerializer.Serialize(new { kind = "luma-media-control", action, value })); }
        catch (Exception ex) { App.Log(ex); }
    }

    private void HomeMediaCard_Click(object sender, MouseButtonEventArgs e)
    {
        for (var source = e.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
            if (source is Button) return;
        if (_nowPlaying is not null)
        {
            var spaceIdx = _spaces.FindIndex(s => s.Tabs.Contains(_nowPlaying));
            if (spaceIdx >= 0 && spaceIdx != _activeSpace) SwitchSpace(spaceIdx);
            CurrentTab = _nowPlaying;
        }
    }

    private void HomeMediaToggle_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SendMediaCommand("toggle");
    }

    private void NowPlayingWidget_Click(object sender, MouseButtonEventArgs e)
    {
        for (var source = e.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source)) if (source is Button) return;
        SyncNowPlayingUi(); SetNowPlayingTray(!_nowPlayingExpanded); e.Handled = true;
    }
    private void NowPlayingTrayClose_Click(object sender, RoutedEventArgs e) => SetNowPlayingTray(false);
    private void SetNowPlayingTray(bool open, bool animate = true)
    {
        _nowPlayingExpanded = open && _nowPlaying is not null;
        var duration = TimeSpan.FromMilliseconds(animate && _state.AnimationsEnabled ? 260 : 0);
        if (_nowPlayingExpanded) NowPlayingTray.Visibility = Visibility.Visible;
        var height = new DoubleAnimation(NowPlayingTray.ActualHeight > 0 ? NowPlayingTray.ActualHeight : 0, _nowPlayingExpanded ? 184 : 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var opacity = new DoubleAnimation(NowPlayingTray.Opacity, _nowPlayingExpanded ? 1 : 0, duration);
        var slide = new DoubleAnimation(NowPlayingTrayTranslate.Y, _nowPlayingExpanded ? 0 : -16, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (!_nowPlayingExpanded) height.Completed += (_, _) => { if (!_nowPlayingExpanded) NowPlayingTray.Visibility = Visibility.Collapsed; };
        NowPlayingTray.BeginAnimation(FrameworkElement.HeightProperty, height); NowPlayingTray.BeginAnimation(UIElement.OpacityProperty, opacity); NowPlayingTrayTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
    }
    private void NowPlayingToggle_Click(object sender, RoutedEventArgs e) { e.Handled = true; SendMediaCommand("toggle"); }
    private void NowPlayingPrev_Click(object sender, RoutedEventArgs e) { SendMediaCommand("skip", -1); }
    private void NowPlayingNext_Click(object sender, RoutedEventArgs e) { SendMediaCommand("skip", 1); }

    private void SeekTrack_MouseDown(object sender, MouseButtonEventArgs e) { _seekDragging = true; SeekTrack.CaptureMouse(); ApplySeekDrag(e.GetPosition(SeekTrack).X); e.Handled = true; }
    private void SeekTrack_MouseMove(object sender, MouseEventArgs e) { if (_seekDragging && e.LeftButton == MouseButtonState.Pressed) ApplySeekDrag(e.GetPosition(SeekTrack).X); }
    private void SeekTrack_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_seekDragging) return;
        _seekDragging = false; SeekTrack.ReleaseMouseCapture();
        var ratio = Math.Clamp(e.GetPosition(SeekTrack).X / Math.Max(1, SeekTrack.ActualWidth), 0, 1);
        if (_nowPlaying is { MediaDuration: > 0 } tab) { tab.MediaPosition = ratio * tab.MediaDuration; SendMediaCommand("seek", tab.MediaPosition); }
    }
    private void ApplySeekDrag(double x) { SeekFill.Width = Math.Clamp(x / Math.Max(1, SeekTrack.ActualWidth), 0, 1) * Math.Max(1, SeekTrack.ActualWidth); PositionThumb(SeekThumb, SeekFill.Width); }

    private void VolumeTrack_MouseDown(object sender, MouseButtonEventArgs e) { _volumeDragging = true; VolumeTrack.CaptureMouse(); ApplyVolumeDrag(e.GetPosition(VolumeTrack).X); e.Handled = true; }
    private void VolumeTrack_MouseMove(object sender, MouseEventArgs e) { if (_volumeDragging && e.LeftButton == MouseButtonState.Pressed) ApplyVolumeDrag(e.GetPosition(VolumeTrack).X); }
    private void VolumeTrack_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_volumeDragging) return;
        _volumeDragging = false; VolumeTrack.ReleaseMouseCapture();
        var ratio = Math.Clamp(e.GetPosition(VolumeTrack).X / Math.Max(1, VolumeTrack.ActualWidth), 0, 1);
        if (_nowPlaying is { } tab) { tab.MediaVolume = ratio; SendMediaCommand("volume", ratio); }
    }
    private void ApplyVolumeDrag(double x) { VolumeFill.Width = Math.Clamp(x / Math.Max(1, VolumeTrack.ActualWidth), 0, 1) * Math.Max(1, VolumeTrack.ActualWidth); PositionThumb(VolumeThumb, VolumeFill.Width); }
    private static void PositionThumb(FrameworkElement thumb, double fillWidth) => thumb.Margin = new Thickness(Math.Max(0, fillWidth - thumb.Width / 2), 0, 0, 0);

    private ContextMenu NewMenu()
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        var dpi = VisualTreeHelper.GetDpi(this);
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("LumaContextMenu"),
            Placement = PlacementMode.AbsolutePoint,
            HorizontalOffset = cursor.X / dpi.DpiScaleX + 5,
            VerticalOffset = cursor.Y / dpi.DpiScaleY + 5,
        };
        return menu;
    }
    private void Add(ContextMenu menu, string title, Action action, bool enabled = true, string hotkey = "", string iconKey = "")
    {
        var item = new MenuItem { Header = title, IsEnabled = enabled, InputGestureText = hotkey, Style = (Style)FindResource("LumaMenuItem") };
        if (!string.IsNullOrWhiteSpace(iconKey))
        {
            var icon = new IconPath { Width = 16, Height = 16, Stretch = System.Windows.Media.Stretch.Uniform, Data = (Geometry)FindResource(iconKey), StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            icon.SetBinding(IconPath.StrokeProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(MenuItem), 1) });
            item.Icon = icon;
        }
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }
    private void Sep(ContextMenu menu) => menu.Items.Add(new Separator { Style = (Style)FindResource("LumaSeparator") });
    private void OpenMenu(ContextMenu menu) { _openMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, false); _openMenu = menu; menu.Opacity = 0; menu.Opened += (_, _) => menu.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(_state.AnimationsEnabled ? 170 : 0))); menu.Closed += (_, _) => { if (_openMenu == menu) _openMenu = null; }; menu.IsOpen = true; }
    private void ShowPageMenu(BrowserTab tab, WebView2 view, string mode, string link, string image)
    {
        var menu = NewMenu();
        if (mode == "editable")
        {
            Add(menu, "Вырезать", async () => await Script(view, "document.execCommand('cut')"), true, "", "IconScissors");
            Add(menu, "Копировать", async () => await Script(view, "document.execCommand('copy')"), true, "", "IconCopy");
            Add(menu, "Вставить", async () => await Script(view, "document.execCommand('paste')"), true, "", "IconClipboardPaste");
            Add(menu, "Выделить всё", async () => await Script(view, "document.execCommand('selectAll')"), true, "", "IconListChecks");
        }
        else if (mode == "selection")
        {
            Add(menu, "Копировать выделенное", async () => { var json = await Script(view, "String(getSelection())"); Copy(JsonSerializer.Deserialize<string>(json) ?? "", "Текст скопирован"); }, true, "", "IconCopy");
            Sep(menu);
            Add(menu, "Спросить Luma о фрагменте", async () => await AskLumaAsync("Что значит выделенный фрагмент?"), true, "", "IconSpark");
            Add(menu, "Объяснить проще", async () => await AskLumaAsync("Объясни выделенный фрагмент простыми словами."), true, "", "IconCircleHelp");
            var targetLangName = _state.Language switch { "uk" => "українську", "en" => "English", _ => "русский" };
            Add(menu, "Перевести фрагмент", async () => await AskLumaAsync($"Переведи выделенный фрагмент на {targetLangName} язык."), true, "", "IconGlobe");
        }
        else if (mode == "video")
        {
            Add(menu, "Воспроизвести / пауза", async () => await Script(view, "(()=>{const v=window.__lumaContextTarget;v.paused?v.play():v.pause()})()"), true, "", "IconPlay");
            Add(menu, "Картинка в картинке", async () => await Script(view, "window.__lumaContextTarget?.requestPictureInPicture?.()"), true, "", "IconPictureInPicture");
        }
        else if (!string.IsNullOrEmpty(image))
        {
            Add(menu, "Открыть изображение", async () => await AddTabAsync(image), true, "", "IconImage");
            Add(menu, "Сохранить изображение как…", async () => await SaveUrlAsync(image), true, "", "IconDownload");
            Add(menu, "Копировать адрес изображения", () => Copy(image, "Адрес скопирован"), true, "", "IconCopy");
        }
        else if (!string.IsNullOrEmpty(link))
        {
            Add(menu, "Открыть ссылку в новой вкладке", async () => await AddTabAsync(link, false), true, "", "IconExternalLink");
            Add(menu, "Открыть ссылку в Split View", async () => await EnableSplitAsync(tab, link, true), true, "", "IconSplit");
            Add(menu, "Открыть ссылку в новом окне", () => OpenProcess(link), true, "", "IconSquare");
            Sep(menu);
            Add(menu, "Сохранить ссылку как…", async () => await SaveUrlAsync(link), true, "", "IconDownload");
            Add(menu, "Копировать адрес ссылки", () => Copy(link, "Адрес скопирован"), true, "", "IconLink");
        }
        else
        {
            var canReturnToSearch = !string.IsNullOrWhiteSpace(tab.BackFallbackInternalUrl);
            Add(menu, "Назад", () => { if (canReturnToSearch) _ = RestoreInternalBackTargetAsync(tab); else if (view.CanGoBack) view.GoBack(); }, view.CanGoBack || canReturnToSearch, "", "IconChevronLeft");
            Add(menu, "Вперёд", () => { if (view.CanGoForward) view.GoForward(); }, view.CanGoForward, "", "IconChevronRight");
            Add(menu, "Перезагрузить", view.Reload, true, "Ctrl+R", "IconReload");
            Sep(menu);
            Add(menu, "Сохранить как…", async () => await SavePageAsync(view), true, "Ctrl+S", "IconDownload");
            Add(menu, "Печать…", () => view.CoreWebView2?.ShowPrintUI(), true, "Ctrl+P", "IconPrinter");
            Add(menu, "Трансляция…", () => ShowToast("Трансляция", "Зависит от поддержки устройства"), true, "", "IconCast");
            var transLabel = _state.Language switch { "uk" => "Перекласти сторінку", "en" => "Translate page", _ => "Перевести страницу" };
            Add(menu, transLabel, async () => await ApplyTranslationAsync(view, true), true, "", "IconGlobe");
            Add(menu, "Режим чтения", async () => await ToggleReaderAsync(view), true, "Ctrl+Shift+R", "IconBookOpen");
            Add(menu, _state.ForceDarkDomains.Contains(DomainKey(view.Source?.ToString() ?? "")) ? "Выключить тёмный режим сайта" : "Включить тёмный режим сайта", async () => await ToggleSiteDarkAsync(view), !tab.IsInternal, "", "IconMoon");
            Sep(menu);
            Add(menu, "Кратко о странице", async () => await AskLumaAsync("Кратко о странице: главные тезисы списком."), true, "Ctrl+Shift+S", "IconFileText");
            Add(menu, "Забыть этот сайт", async () => await ForgetSiteAsync(view), !tab.IsInternal, "", "IconTrash2");
            Add(menu, "Забывать при выходе", () => ToggleForgetOnExit(view), !tab.IsInternal, "", "IconLogOut");
            Sep(menu);
            Add(menu, "Просмотр кода страницы", async () => await AddTabAsync("view-source:" + (view.Source?.ToString() ?? tab.ActiveUrl)), true, "Ctrl+U", "IconCodeXml");
            Add(menu, "Просмотреть код", () => view.CoreWebView2?.OpenDevToolsWindow(), true, "", "IconGear");
        }
        OpenMenu(menu);
    }
    private static async Task<string> Script(WebView2 view, string script) => view.CoreWebView2 is null ? "null" : await view.CoreWebView2.ExecuteScriptAsync(script);
    private async Task SaveUrlAsync(string url)
    {
        // Handle blob: and data: URIs that cannot be fetched by HttpClient.
        if (url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var view = CurrentTab?.ActiveView;
            if (view?.CoreWebView2 is null) { ShowToast("Ошибка сохранения", "Не удалось получить доступ к файлу", true); return; }
            // Use the page's own JS to convert blob/data URL to base64 bytes.
            var rawJson = await view.CoreWebView2.ExecuteScriptAsync($@"
                (async () => {{
                    try {{
                        const r = await fetch({System.Text.Json.JsonSerializer.Serialize(url)});
                        const ab = await r.arrayBuffer();
                        const bytes = new Uint8Array(ab);
                        let b = '';
                        bytes.forEach(byte => b += String.fromCharCode(byte));
                        return btoa(b);
                    }} catch(e) {{ return null; }}
                }})()");
            var b64 = System.Text.Json.JsonSerializer.Deserialize<string?>(rawJson);
            if (string.IsNullOrWhiteSpace(b64)) { ShowToast("Ошибка сохранения", "Не удалось прочитать файл", true); return; }
            var d2 = new SaveFileDialog { FileName = "image.png" };
            if (d2.ShowDialog() != true) return;
            await File.WriteAllBytesAsync(d2.FileName, Convert.FromBase64String(b64));
            ShowToast("Файл сохранён", Path.GetFileName(d2.FileName));
            return;
        }
        string fileName;
        try { fileName = Path.GetFileName(new Uri(url).LocalPath); } catch { fileName = "file"; }
        var dlg = new SaveFileDialog { FileName = string.IsNullOrEmpty(fileName) ? "image" : fileName };
        if (dlg.ShowDialog() != true) return;
        try
        {
            // Use WebView2's own cookie store by fetching via the page's JS context,
            // which preserves auth cookies and session headers.
            var view = CurrentTab?.ActiveView;
            byte[] bytes;
            if (view?.CoreWebView2 is not null)
            {
                var rawJson = await view.CoreWebView2.ExecuteScriptAsync($@"
                    (async () => {{
                        try {{
                            const r = await fetch({System.Text.Json.JsonSerializer.Serialize(url)}, {{credentials: 'include'}});
                            if (!r.ok) return null;
                            const ab = await r.arrayBuffer();
                            const bytes = new Uint8Array(ab);
                            let b = '';
                            bytes.forEach(byte => b += String.fromCharCode(byte));
                            return btoa(b);
                        }} catch(e) {{ return null; }}
                    }})()");
                var b64 = System.Text.Json.JsonSerializer.Deserialize<string?>(rawJson);
                if (!string.IsNullOrWhiteSpace(b64))
                {
                    bytes = Convert.FromBase64String(b64);
                    await File.WriteAllBytesAsync(dlg.FileName, bytes);
                    ShowToast("Файл сохранён", Path.GetFileName(dlg.FileName));
                    return;
                }
            }
            // Fallback: plain HttpClient (works for public URLs).
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
            bytes = await http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(dlg.FileName, bytes);
            ShowToast("Файл сохранён", Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex) { ShowToast("Ошибка сохранения", ex.Message, true); }
    }
    private async Task SavePageAsync(WebView2 view) { if (view.CoreWebView2 is null) return; var d = new SaveFileDialog { FileName = "page.html", Filter = "HTML|*.html" }; if (d.ShowDialog() != true) return; var json = await view.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML"); await File.WriteAllTextAsync(d.FileName, JsonSerializer.Deserialize<string>(json) ?? ""); }
    private static void OpenProcess(string url) => Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "\"" + url.Replace("\"", "") + "\"") { UseShellExecute = true });

    private async Task OpenAuthPopupAsync(CoreWebView2NewWindowRequestedEventArgs e, WebView2 opener)
    {
        var features = e.WindowFeatures;
        var width = features.HasSize ? Math.Clamp(features.Width, 400d, 760d) : 480d;
        var height = features.HasSize ? Math.Clamp(features.Height, 440d, 900d) : 640d;
        var popup = new PopupWindow(this, width, height, (Brush)Resources["AccentBrush"]);
        popup.Show();
        await popup.View.EnsureCoreWebView2Async(opener.CoreWebView2?.Environment ?? await BrowserEnvironmentAsync());
        await popup.View.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScripts.Scrollbars(CurrentAccentCss()));
        popup.Attach();
        e.NewWindow = popup.View.CoreWebView2;
    }

    private async Task ApplyPipAsync(WebView2 view) { var domain = BrowserTab.DomainOf(view.Source?.ToString() ?? ""); if (!_state.AutoPictureInPicture.TryGetValue(domain, out var on) || !on) return; await Script(view, "document.querySelectorAll('video').forEach(v=>v.addEventListener('play',()=>v.requestPictureInPicture?.().catch(()=>{}),{once:true}))"); }
    private async Task ApplyAmbientLightAsync(WebView2 view, bool? forceState = null)
    {
        var domain = BrowserTab.DomainOf(view.Source?.ToString() ?? "");
        var isYt = domain.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || domain.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
        if (!isYt) return;
        var enabled = forceState ?? (_state.AmbientLight.TryGetValue(domain, out var on) ? on : true);
        await Script(view, BrowserScripts.AmbientLight(enabled));
    }
    private static string? TranslateDomain(string url) { if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null; return uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host; }
}
