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
    private static BrowserTab? TabFromInputSource(DependencyObject? source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is Button) return null; // close/action buttons keep their own behavior
            if (node is FrameworkElement { DataContext: BrowserTab tab }) return tab;
        }
        return null;
    }

    private void ReliableTabPreviewTouchDown(object? sender, TouchEventArgs e)
    {
        var tab = TabFromInputSource(e.OriginalSource as DependencyObject);
        if (tab is null) return;
        ActivateTabFromUser(tab);
        e.Handled = true;
    }

    private void Tab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var tab = TabFromInputSource(e.OriginalSource as DependencyObject);
        if (tab is null) return; // leave close/action buttons to their own Click handlers
        _dragStart = e.GetPosition(this);
        ActivateTabFromUser(tab);
        e.Handled = true;
    }
    private void Tab_MouseMove(object sender, MouseEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed && Moved(e.GetPosition(this)) && sender is FrameworkElement { DataContext: BrowserTab tab } element) DragDrop.DoDragDrop(element, new DataObject("LumaTab", tab), DragDropEffects.Move); }
    private void Tab_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || sender is not FrameworkElement { DataContext: BrowserTab tab }) return;
        e.Handled = true;
        ReleaseTabInputCapture();
        // Do not remove the ListBoxItem while WPF is still completing its mouse event. Doing so
        // can leave selection/capture attached to a detached row and make every later click inert.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (_spaces.Any(space => space.Tabs.Contains(tab))) CloseTab(tab);
        }));
    }
    private void TabsList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_syncingTabSelection) return; if (TabsList.SelectedItem is BrowserTab tab) { ActivateTabFromUser(tab); e.Handled = true; } }
    private void ActivateTabFromUser(BrowserTab tab, int request = 0)
    {
        var ownerIndex = _spaces.FindIndex(space => space.Tabs.Contains(tab));
        if (ownerIndex < 0) return;
        ReleaseTabInputCapture();
        if (request == 0) request = ++_tabActivationSerial;
        // Make a same-space click visible synchronously. The asynchronous passes below only
        // stabilize layout/focus; they are never allowed to decide which tab the user meant.
        if (ownerIndex == _activeSpace && !ReferenceEquals(CurrentTab, tab)) CurrentTab = tab;
        _ = ActivateTabReliablyAsync(tab, request);
    }

    private void ReleaseTabInputCapture()
    {
        Mouse.Capture(null);
        ReleaseCapture();
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            SetForegroundWindow(handle);
            SetFocus(handle);
        }
    }

    private async Task ActivateTabReliablyAsync(BrowserTab tab, int request)
    {
        var ownerIndex = _spaces.FindIndex(space => space.Tabs.Contains(tab));
        if (ownerIndex < 0 || request != _tabActivationSerial) return;
        if (!IsActive) Activate();
        ReleaseTabInputCapture();
        CloseTransientUi();
        CloseHistory();
        await EnsureTabReadyAsync(tab);
        if (request != _tabActivationSerial) return;
        if (ownerIndex != _activeSpace)
        {
            Save();
            _spaces[_activeSpace].Current = CurrentTab;
            _activeSpace = ownerIndex;
            _state.ActiveSpace = ownerIndex;
            await LoadSpaceAsync(ownerIndex);
            if (request != _tabActivationSerial) return;
            OnChanged(nameof(Tabs)); RefreshSpaces(); RefreshFolders(); FilterTabs();
        }
        for (var pass = 0; pass < 3; pass++)
        {
            if (request != _tabActivationSerial || !_spaces[ownerIndex].Tabs.Contains(tab)) return;
            if (!ReferenceEquals(CurrentTab, tab)) CurrentTab = tab;
            WakeTab(tab); UpdateChrome(); RenderPanes();
            _syncingTabSelection = true;
            try { TabsList.SelectedItem = string.IsNullOrEmpty(tab.FolderId) && !tab.IsPinned ? tab : null; }
            finally { _syncingTabSelection = false; }
            if (pass < 2) await Dispatcher.InvokeAsync(() => { }, pass == 0 ? DispatcherPriority.Input : DispatcherPriority.Render);
        }
        Save();
        if (!tab.IsHome) _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (request == _tabActivationSerial && ReferenceEquals(CurrentTab, tab)) { tab.ActiveView.Focus(); Keyboard.Focus(tab.ActiveView); } }));
    }
    private void CloseTab_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: BrowserTab tab }) { CloseTab(tab); FilterTabs(); e.Handled = true; } }
    private void Tab_MouseRightButtonUp(object sender, MouseButtonEventArgs e) { if (sender is FrameworkElement { DataContext: BrowserTab tab }) { ShowTabMenu(tab); e.Handled = true; } }
    private void NewTab_Click(object sender, RoutedEventArgs e) { CloseTransientUi(); OpenNewHomeTab(); }
    private void TabSearch_Click(object sender, RoutedEventArgs e) { CloseTransientUi(); OpenSearch(SearchPurpose.TabSearch); }
    private void ClearTabs_Click(object sender, RoutedEventArgs e) { CloseTransientUi(); ClearTabs(); }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_searchSuppress) return;
        UpdateSearchPlaceholder();
        var typed = SearchBox.Text;
        RefreshSearch(typed);
        TryInlineComplete(typed);
        _searchPrevText = SearchBox.Text;
    }
    private async void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Back || e.Key == Key.Delete) _searchPrevText = SearchBox.Text + " "; if (e.Key == Key.Right && SearchBox.SelectionLength > 0) { SearchBox.CaretIndex = SearchBox.Text.Length; e.Handled = true; return; } if (e.Key == Key.Down) SearchResultsList.SelectedIndex = Math.Min(SearchResultsList.Items.Count - 1, SearchResultsList.SelectedIndex + 1); else if (e.Key == Key.Up) SearchResultsList.SelectedIndex = Math.Max(0, SearchResultsList.SelectedIndex - 1); else if (e.Key == Key.Enter) await ConfirmSearchAsync(); else if (e.Key == Key.Escape) SearchPopup.IsOpen = false; }
    private void SearchResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
    private async void SearchResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => await ConfirmSearchAsync();
    private void SearchPopup_Closed(object sender, EventArgs e)
    {
        SearchBox.Clear(); UpdateSearchPlaceholder(); _searchPrevText = ""; _searchFolder = null;
        PaneHost.IsEnabled = true;
        // Give the keyboard back to the page.
        if (CurrentTab?.ActiveView is { } view) view.Focus();
    }

    /// <summary>Single click on a suggestion opens it, like every other launcher.</summary>
    private async void SearchResultsList_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(SearchResultsList, source) is not ListBoxItem row) return;
        SearchResultsList.SelectedItem = row.DataContext;
        e.Handled = true;
        await ConfirmSearchAsync();
    }
    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTab is not { } tab) return;
        if (!string.IsNullOrWhiteSpace(tab.BackFallbackInternalUrl)) { _ = RestoreInternalBackTargetAsync(tab); return; }
        if (tab.ActiveView.CanGoBack) tab.ActiveView.GoBack();
    }
    private void Forward_Click(object sender, RoutedEventArgs e) { if (CurrentTab?.ActiveView.CanGoForward == true) CurrentTab.ActiveView.GoForward(); }
    private void Reload_Click(object sender, RoutedEventArgs e) { if (CurrentTab is { IsHome: false } tab) tab.ActiveView.Reload(); }
    private void DomainPill_Click(object sender, MouseButtonEventArgs e) => OpenSearch(SearchPurpose.Navigate, CurrentTab?.ActiveUrl);
    private void CopyUrl_Click(object sender, RoutedEventArgs e) { if (CurrentTab is not null) Copy(CurrentTab.ActiveUrl, "Ссылка скопирована"); }
    private void ToggleSidebar_Click(object sender, RoutedEventArgs e) => SetSidebar(!_sidebarVisible, true);
    private void ExitPageFullscreen() { var view = CurrentTab?.ActiveView; if (view?.CoreWebView2 is null) return; _ = view.ExecuteScriptAsync("document.exitFullscreen&&document.fullscreenElement&&document.exitFullscreen()"); }
    private void MainMenu_Click(object sender, RoutedEventArgs e)
    {
        CloseMenusExcept(MainMenuPopup);
        MenuVersionText.Text = "Версия " + AppVersion;
        UpdateAccountMenu();
        MainMenuPopup.IsOpen = !MainMenuPopup.IsOpen;
    }
    private void NewSpaceMenu_Click(object sender, RoutedEventArgs e) { CloseMenusExcept(NewSpacePopup); PaneHost.IsEnabled = false; NewSpaceNameBox.Text = "Пространство " + (_spaces.Count + 1); NewSpacePopup.IsOpen = true; }
    private void NewFolder_Click(object sender, RoutedEventArgs e) => AskFolderName(null);

    /// <summary>Asks for a folder name in a proper Luma dialog. Passing a folder renames it.</summary>
    private void AskFolderName(FolderState? folder)
    {
        CloseMenusExcept(NewFolderPopup);
        _folderBeingNamed = folder;
        NewFolderTitle.Text = folder is null ? "Новая папка" : "Переименовать папку";
        NewFolderHint.Text = folder is null ? "Дайте ей короткое понятное имя" : "Измените название папки";
        NewFolderConfirmText.Text = folder is null ? "Создать" : "Сохранить";
        NewFolderNameBox.Text = folder?.Name ?? "";
        UpdateFolderPlaceholder();
        PaneHost.IsEnabled = false;
        NewFolderPopup.IsOpen = true;
    }

    private async void NewFolderPopup_Opened(object sender, EventArgs e) => await FocusPopupTextBoxAsync(NewFolderPopup, NewFolderNameBox, true);

    private void NewFolderPopup_Closed(object sender, EventArgs e)
    {
        _folderBeingNamed = null;
        RestorePageInputAfterModal();
    }

    private async Task FocusPopupTextBoxAsync(Popup popup, TextBox textBox, bool selectAll)
    {
        foreach (var delay in new[] { 0, 35, 90, 170 })
        {
            if (delay > 0) await Task.Delay(delay);
            await Dispatcher.InvokeAsync(() =>
            {
                if (!popup.IsOpen) return;
                if (PresentationSource.FromVisual(textBox) is System.Windows.Interop.HwndSource source && source.Handle != IntPtr.Zero)
                {
                    SetForegroundWindow(source.Handle);
                    SetFocus(source.Handle);
                }
                textBox.Focus();
                FocusManager.SetFocusedElement(popup, textBox);
                Keyboard.Focus(textBox);
                if (selectAll) textBox.SelectAll(); else textBox.CaretIndex = textBox.Text.Length;
            }, DispatcherPriority.Input);
        }
    }

    private void RestorePageInputAfterModal()
    {
        if (SearchPopup.IsOpen || NewFolderPopup.IsOpen || NewSpacePopup.IsOpen) return;
        PaneHost.IsEnabled = true;
        if (CurrentTab?.ActiveView is { } view) view.Focus();
    }

    private void NewFolderNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CreateFolder_Click(sender, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Escape) { NewFolderPopup.IsOpen = false; e.Handled = true; }
    }

    private void UpdateFolderPlaceholder()
    {
        if (NewFolderPlaceholder is null) return;
        NewFolderPlaceholder.Visibility = string.IsNullOrEmpty(NewFolderNameBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NewFolderNameBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateFolderPlaceholder();

    /// <summary>Accent focus ring around the name field, like the rest of the Luma UI.</summary>
    private void NewFolderNameBox_FocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (NewFolderField is null) return;
        NewFolderField.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, NewFolderNameBox.IsKeyboardFocusWithin ? "AccentBrush" : "BorderBrush");
    }

    private void CancelFolder_Click(object sender, RoutedEventArgs e) { _folderBeingNamed = null; NewFolderPopup.IsOpen = false; }

    private void CreateFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(NewFolderNameBox.Text) ? "Новая папка" : NewFolderNameBox.Text.Trim();
        if (_folderBeingNamed is null) _spaces[_activeSpace].State.Folders.Add(new FolderState { Name = name });
        else _folderBeingNamed.Name = name;
        _folderBeingNamed = null;
        NewFolderPopup.IsOpen = false;
        Save(); RefreshFolders();
    }
    private async void SpaceDot_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SpaceDotView dot } || dot.Index == _activeSpace) return;
        // Load the target space first: otherwise Save() would treat an unopened space as loaded
        // and overwrite its stored session with the single dropped tab.
        await LoadSpaceAsync(dot.Index);
        if (e.Data.GetData("LumaTab") is BrowserTab tab)
        {
            _spaces[_activeSpace].Tabs.Remove(tab); tab.FolderId = null;
            _spaces[dot.Index].Tabs.Add(tab);
            if (tab == CurrentTab) CurrentTab = Tabs.FirstOrDefault();
        }
        else if (e.Data.GetData("LumaSaved") is SavedDrag saved)
        {
            saved.Source?.Items.Remove(saved.Site);
            if (saved.Loose) _spaces[_activeSpace].State.LooseItems.Remove(saved.Site);
            _spaces[dot.Index].State.LooseItems.Add(saved.Site);
        }
        Save(); RefreshFolders(); FilterTabs(); ShowToast("Перенесено", dot.Name);
    }
    private void SiteSettings_Click(object sender, RoutedEventArgs e)
    {
        CloseMenusExcept(SiteMenuPopup);
        SiteMenuDomain.Text = CurrentTab is null || string.IsNullOrWhiteSpace(CurrentTab.Domain) ? "Страница Luma" : CurrentTab.Domain;
        SiteMenuPopup.IsOpen = !SiteMenuPopup.IsOpen;
    }
    private void Share_Click(object sender, RoutedEventArgs e) { if (CurrentTab is not null) Copy(CurrentTab.ActiveUrl, "Ссылка скопирована"); }
    private void PipToggle_Changed(object sender, RoutedEventArgs e) { if (_suppressPip || CurrentTab is null) return; _state.AutoPictureInPicture[CurrentTab.Domain] = PipToggle.IsChecked == true; _stateStore.Save(); }
    private async void ClearCache_Click(object sender, RoutedEventArgs e) { if (CurrentTab?.ActiveView.CoreWebView2 is null) return; await CurrentTab.ActiveView.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache); ShowToast("Кеш очищен"); }
    private async void ClearCookies_Click(object sender, RoutedEventArgs e) { if (CurrentTab?.ActiveView.CoreWebView2 is null) return; var m = CurrentTab.ActiveView.CoreWebView2.CookieManager; foreach (var c in await m.GetCookiesAsync(CurrentTab.ActiveUrl)) m.DeleteCookie(c); ShowToast("Cookie удалены"); }
    private void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        CloseMenusExcept(TranslatePopup);
        var domain = CurrentTab is null ? null : TranslateDomain(CurrentTab.ActiveUrl);
        TranslateDomainText.Text = domain is null
            ? "на русский"
            : (_state.AlwaysTranslateDomains.Contains(domain) ? domain + " - переводится всегда" : domain + " - на русский");
        TranslatePopup.IsOpen = !TranslatePopup.IsOpen;
    }

    /// <summary>Translate the current page only, without remembering the domain.</summary>
    private async void TranslateOnce_Click(object sender, RoutedEventArgs e)
    {
        TranslatePopup.IsOpen = false;
        if (CurrentTab is not null) await ApplyTranslationAsync(CurrentTab.ActiveView, true);
    }

    /// <summary>Translate now and keep translating every page of this domain.</summary>
    private async void TranslateAlways_Click(object sender, RoutedEventArgs e)
    {
        TranslatePopup.IsOpen = false;
        if (CurrentTab is null) return;
        var domain = TranslateDomain(CurrentTab.ActiveUrl);
        if (domain is not null && !_state.AlwaysTranslateDomains.Contains(domain)) { _state.AlwaysTranslateDomains.Add(domain); _stateStore.Save(); }
        await ApplyTranslationAsync(CurrentTab.ActiveView, true);
    }

    /// <summary>Forget the domain and reload so the original text comes back.</summary>
    private async void TranslateCancel_Click(object sender, RoutedEventArgs e)
    {
        TranslatePopup.IsOpen = false;
        if (CurrentTab is null) return;
        var domain = TranslateDomain(CurrentTab.ActiveUrl);
        if (domain is not null) _state.AlwaysTranslateDomains.Remove(domain);
        _stateStore.Save();
        await Script(CurrentTab.ActiveView, "location.reload()");
    }
    private void SplitButton_Click(object sender, RoutedEventArgs e) { CloseMenusExcept(SplitPopup); SplitPopup.IsOpen = !SplitPopup.IsOpen; }
    private void SplitDirection_Click(object sender, RoutedEventArgs e) { SplitPopup.IsOpen = false; OpenSearch(SearchPurpose.Split); }
    private void ClearSplit_Click(object sender, RoutedEventArgs e) { SplitPopup.IsOpen = false; if (CurrentTab?.IsSplit == true) ClosePane(CurrentTab, "secondary"); }
    private void DeleteFolderOnly_Click(object sender, RoutedEventArgs e) { var f = _deleteFolder; _deleteFolder = null; DeleteFolderPopup.IsOpen = false; if (f is null) return; foreach (var i in f.Items) _spaces[_activeSpace].State.LooseItems.Add(i); foreach (var t in Tabs.Where(t => t.FolderId == f.Id)) t.FolderId = null; _spaces[_activeSpace].State.Folders.Remove(f); Save(); RefreshFolders(); FilterTabs(); }
    private void DeleteFolderAndTabs_Click(object sender, RoutedEventArgs e) { var f = _deleteFolder; _deleteFolder = null; DeleteFolderPopup.IsOpen = false; if (f is null) return; CloseFolder(f); _spaces[_activeSpace].State.Folders.Remove(f); Save(); RefreshFolders(); }
    private void DeleteFolderCancel_Click(object sender, RoutedEventArgs e) { _deleteFolder = null; DeleteFolderPopup.IsOpen = false; }
    private void DeleteFolderPopup_Closed(object sender, EventArgs e) => _deleteFolder = null;
    private async void Settings_Click(object sender, RoutedEventArgs e) { CloseTransientUi(); await OpenSettingsAsync(); }
    private void DefaultBrowser_Click(object sender, RoutedEventArgs e) { CloseTransientUi(); BrowserRegistry.OpenDefaultAppsSettings(); }
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) { CloseTransientUi(); await CheckForUpdatesManuallyAsync(); }
    private async void AboutMenu_Click(object sender, RoutedEventArgs e) { CloseTransientUi(); await OpenAboutAsync(); }
    public static string AppVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "2.1.1";
    private async void Import_Click(object sender, RoutedEventArgs e) { CloseTransientUi(); await OpenImportAsync(); }
    private void Exit_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).ExitCompletely();
    // Kept for the WPF chrome, but the cursor poll above is what actually drives the reveal,
    // because the page area cannot deliver these events at all.
    private void EdgeReveal_MouseEnter(object sender, MouseEventArgs e) => EdgeWatchTick();
    private void Sidebar_MouseLeave(object sender, MouseEventArgs e) => EdgeWatchTick();
    private void Sidebar_MouseEnterOverlay(object sender, MouseEventArgs e) => _hoverHideTimer.Stop();
    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void ToggleMaximize()
    {
        if (!_manualMaximized)
        {
            _restoreBounds = new Rect(Left, Top, ActualWidth, ActualHeight); var area = SystemParameters.WorkArea;
            WindowState = WindowState.Normal; Left = area.Left; Top = area.Top; Width = area.Width; Height = area.Height; _manualMaximized = true;
        }
        else { Left = _restoreBounds.Left; Top = _restoreBounds.Top; Width = _restoreBounds.Width; Height = _restoreBounds.Height; _manualMaximized = false; }
    }
    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        for (var node = e.OriginalSource as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, NowPlayingWidget)) { e.Handled = true; return; }
            if (ReferenceEquals(node, TitleBar)) break;
        }
        if (e.ClickCount == 2) { ToggleMaximize(); e.Handled = true; }
        else if (e.ButtonState == MouseButtonState.Pressed && !_manualMaximized) DragMove();
    }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape && HistoryOverlay.Visibility == Visibility.Visible) { CloseHistory(); e.Handled = true; return; } if (e.Key == Key.F11) { SetFullscreen(!_fullscreen); e.Handled = true; } else if (e.Key == Key.Escape && _fullscreen) { ExitPageFullscreen(); SetFullscreen(false); e.Handled = true; } else if (e.Key == Key.Escape) CloseTransientUi(); else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && e.Key == Key.S) { _ = AskLumaAsync("Кратко о странице: главные тезисы списком."); e.Handled = true; }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && e.Key == Key.R) { _ = ToggleReaderAsync(CurrentTab?.ActiveView); e.Handled = true; }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && e.Key == Key.T) { _ = ReopenLastClosedAsync(); e.Handled = true; }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && e.Key == Key.V) { _ = PasteCleanAsync(); e.Handled = true; }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.T) { OpenNewHomeTab(); e.Handled = true; } else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.K) { OpenSearch(SearchPurpose.TabSearch); e.Handled = true; } else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.R) { if (CurrentTab is { IsHome: false } tab) tab.ActiveView.Reload(); } else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.J) { _ = ToggleAssistantAsync(); e.Handled = true; } }
    private void OnClosing(object? sender, CancelEventArgs e) { if (_closing) return; _closing = true; _auth.SessionChanged -= AccountSessionChanged; CloseFloatingVideoForShutdown(); _floatingMusic?.Close(); _floatingMusic = null; _downloadWatchdog?.Stop(); _tabInputWatch.Stop(); _edgeWatch.Stop(); _sleepWatch.Stop(); _feedbackRefreshTimer.Stop(); StopUpdateLoop(); StopAccountUsageTracking(); Save(); foreach (var s in _spaces) foreach (var t in s.Tabs) t.Dispose(); if (!((App)Application.Current).IsExiting && !((App)Application.Current).KeepInBackground) Application.Current.Shutdown(); }
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
