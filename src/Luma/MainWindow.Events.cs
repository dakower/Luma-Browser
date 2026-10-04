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
        MenuVersionText.Text = L("Версия ", "Version ", "Версія ") + AppVersion;
        UpdateAccountMenu();
        MainMenuPopup.IsOpen = !MainMenuPopup.IsOpen;
    }
    private void NewSpaceMenu_Click(object sender, RoutedEventArgs e) { CloseMenusExcept(NewSpacePopup); PaneHost.IsEnabled = false; NewSpaceNameBox.Text = "Пространство " + (_spaces.Count + 1); NewSpacePopup.IsOpen = true; }
    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (AddFolderIcon is not null)
        {
            FolderAnimationHelper.PlayOnce(AddFolderIcon, "add_folder", 23, 20);
        }
        AskFolderName(null);
    }

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
        var isNew = _folderBeingNamed is null;
        var name = string.IsNullOrWhiteSpace(NewFolderNameBox.Text) ? "Новая папка" : NewFolderNameBox.Text.Trim();
        if (isNew) _spaces[_activeSpace].State.Folders.Add(new FolderState { Name = name });
        else _folderBeingNamed!.Name = name;
        _folderBeingNamed = null;
        NewFolderPopup.IsOpen = false;
        Save(); RefreshFolders();
        if (isNew && FoldersHeaderIcon is not null)
        {
            FolderAnimationHelper.PlayOnce(FoldersHeaderIcon, "refresh_folder", 22, 22, () =>
            {
                FoldersHeaderIcon.Source = FolderAnimationHelper.GetFrame("folder", 0, 22);
            });
        }
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
    private async void AmbientLightToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressAmbient || CurrentTab is null) return;
        var on = AmbientLightToggle.IsChecked == true;
        _state.AmbientLight[CurrentTab.Domain] = on;
        _stateStore.Save();
        if (CurrentTab.ActiveView?.CoreWebView2 is not null)
            await ApplyAmbientLightAsync(CurrentTab.ActiveView, on);
    }
    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (CurrentTab?.ActiveView.CoreWebView2 is null) return;
            await CurrentTab.ActiveView.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
            ShowToast("Кеш очищен");
        }
        catch (Exception ex) { App.Log(ex); ShowToast("Ошибка", ex.Message, true); }
    }
    private async void ClearCookies_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (CurrentTab?.ActiveView.CoreWebView2 is null) return;
            var m = CurrentTab.ActiveView.CoreWebView2.CookieManager;
            if (Uri.TryCreate(CurrentTab.ActiveUrl, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
            {
                var cookies = await m.GetCookiesAsync(CurrentTab.ActiveUrl);
                foreach (var c in cookies) m.DeleteCookie(c);
            }
            else
            {
                await CurrentTab.ActiveView.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.Cookies);
            }
            ShowToast("Cookie удалены");
        }
        catch (Exception ex) { App.Log(ex); ShowToast("Ошибка", ex.Message, true); }
    }
    private void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        CloseMenusExcept(TranslatePopup);
        var domain = CurrentTab is null ? null : TranslateDomain(CurrentTab.ActiveUrl);
        var langName = _state.Language switch { "uk" => L("на украинский", "to Ukrainian", "на українську"), "en" => L("на английский", "to English", "на англійську"), _ => L("на русский", "to Russian", "на російську") };
        var alwaysText = L(" - переводится всегда", " - always translated", " - перекладається завжди");
        TranslateDomainText.Text = domain is null
            ? langName
            : (_state.AlwaysTranslateDomains.Contains(domain) ? domain + alwaysText : domain + " - " + langName);
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
    public static string AppVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "2.1.5";
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
            _restoreBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
            // Fix: use the monitor the window is currently on, not always the primary monitor.
            var screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
            var topLeft = transform.Transform(new System.Windows.Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
            var size = transform.Transform(new System.Windows.Point(screen.WorkingArea.Width, screen.WorkingArea.Height));
            WindowState = WindowState.Normal;
            Left = topLeft.X; Top = topLeft.Y; Width = size.X; Height = size.Y;
            _manualMaximized = true;
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

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1)
        {
            if (CurrentTab is { } tab && (tab.ActiveView.CanGoBack || !string.IsNullOrWhiteSpace(tab.BackFallbackInternalUrl)))
            {
                if (!string.IsNullOrWhiteSpace(tab.BackFallbackInternalUrl)) _ = RestoreInternalBackTargetAsync(tab);
                else tab.ActiveView.GoBack();
                e.Handled = true;
            }
        }
        else if (e.ChangedButton == MouseButton.XButton2)
        {
            if (CurrentTab is { } tab && tab.ActiveView.CanGoForward)
            {
                tab.ActiveView.GoForward();
                e.Handled = true;
            }
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        if (e.Key == Key.Escape && HistoryOverlay.Visibility == Visibility.Visible) { CloseHistory(); e.Handled = true; return; }
        if (e.Key == Key.F11) { SetFullscreen(!_fullscreen); e.Handled = true; }
        else if (e.Key == Key.Escape && _fullscreen) { ExitPageFullscreen(); SetFullscreen(false); e.Handled = true; }
        else if (e.Key == Key.Escape) CloseTransientUi();
        else if (e.Key == Key.F5) { if (CurrentTab is { IsHome: false } tab5) tab5.ActiveView.Reload(); e.Handled = true; }
        else if (ctrl && e.Key == Key.W) { if (CurrentTab is { } tabW) { CloseTab(tabW); FilterTabs(); } e.Handled = true; }
        else if (ctrl && e.Key == Key.F4) { if (CurrentTab is { } tabF4) { CloseTab(tabF4); FilterTabs(); } e.Handled = true; }
        else if (ctrl && e.Key == Key.Tab) { CycleTab(+1); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.Tab) { CycleTab(-1); e.Handled = true; }
        else if (ctrl && e.Key == Key.D1) { SelectTabByIndex(0); e.Handled = true; }
        else if (ctrl && e.Key == Key.D2) { SelectTabByIndex(1); e.Handled = true; }
        else if (ctrl && e.Key == Key.D3) { SelectTabByIndex(2); e.Handled = true; }
        else if (ctrl && e.Key == Key.D4) { SelectTabByIndex(3); e.Handled = true; }
        else if (ctrl && e.Key == Key.D5) { SelectTabByIndex(4); e.Handled = true; }
        else if (ctrl && e.Key == Key.D6) { SelectTabByIndex(5); e.Handled = true; }
        else if (ctrl && e.Key == Key.D7) { SelectTabByIndex(6); e.Handled = true; }
        else if (ctrl && e.Key == Key.D8) { SelectTabByIndex(7); e.Handled = true; }
        else if (ctrl && e.Key == Key.D9) { SelectTabByIndex(Tabs.Count - 1); e.Handled = true; }
        else if ((ctrl && e.Key == Key.L) || (alt && e.Key == Key.D) || e.Key == Key.F6) { FocusAddressBar(); e.Handled = true; }
        else if (ctrl && e.Key == Key.H) { History_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.F12) { if (CurrentTab is { IsHome: false } tab12) tab12.ActiveView.CoreWebView2?.OpenDevToolsWindow(); e.Handled = true; }
        else if (alt && e.Key == Key.Home) { OpenNewHomeTab(); e.Handled = true; }
        else if (alt && e.Key == Key.Left) { if (CurrentTab is { IsHome: false } tabBack && tabBack.ActiveView.CanGoBack) { tabBack.ActiveView.GoBack(); e.Handled = true; } }
        else if (alt && e.Key == Key.Right) { if (CurrentTab is { IsHome: false } tabFwd && tabFwd.ActiveView.CanGoForward) { tabFwd.ActiveView.GoForward(); e.Handled = true; } }
        else if (ctrl && e.Key == Key.F) { OpenSearch(SearchPurpose.TabSearch); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.S) { _ = AskLumaAsync("Кратко о странице: главные тезисы списком."); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.R) { _ = ToggleReaderAsync(CurrentTab?.ActiveView); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.T) { _ = ReopenLastClosedAsync(); e.Handled = true; }
        else if (ctrl && e.Key == Key.T) { OpenNewHomeTab(); e.Handled = true; }
        else if (ctrl && e.Key == Key.K) { OpenSearch(SearchPurpose.TabSearch); e.Handled = true; }
        else if (ctrl && alt && e.Key == Key.Right) { CycleSpace(+1); e.Handled = true; }
        else if (ctrl && alt && e.Key == Key.Left) { CycleSpace(-1); e.Handled = true; }
        else if (ctrl && alt && e.Key == Key.D1) { SelectSpaceByIndex(0); e.Handled = true; }
        else if (ctrl && alt && e.Key == Key.D2) { SelectSpaceByIndex(1); e.Handled = true; }
        else if (ctrl && alt && e.Key == Key.D3) { SelectSpaceByIndex(2); e.Handled = true; }
        else if (ctrl && alt && e.Key == Key.D4) { SelectSpaceByIndex(3); e.Handled = true; }
        else if (ctrl && alt && e.Key == Key.D5) { SelectSpaceByIndex(4); e.Handled = true; }
        else if (ctrl && e.Key == Key.R) { if (CurrentTab is { IsHome: false } tabR) tabR.ActiveView.Reload(); }
        else if (ctrl && e.Key == Key.J) { _ = ToggleAssistantAsync(); e.Handled = true; }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        _closing = true;
        _auth.SessionChanged -= AccountSessionChanged;
        CloseFloatingVideoForShutdown();
        _floatingMusic?.Close();
        _floatingMusic = null;
        _downloadWatchdog?.Stop();
        _tabInputWatch.Stop();
        _edgeWatch.Stop();
        _sleepWatch.Stop();
        _feedbackRefreshTimer.Stop();
        _supportChatRealtimeTimer.Stop();
        _supportChatBackgroundTimer.Stop();
        _homeClockTimer.Stop();
        _toastTimer.Stop();
        _hoverHideTimer.Stop();
        _interfaceLanguageTimer?.Stop();
        StopUpdateLoop();
        StopAccountUsageTracking();
        Save();
        foreach (var s in _spaces) foreach (var t in s.Tabs) t.Dispose();
        if (!((App)Application.Current).IsExiting && !((App)Application.Current).KeepInBackground) Application.Current.Shutdown();
    }
    /// <summary>Cycles through spaces by the given delta (+1 right, -1 left).</summary>
    private void CycleSpace(int delta)
    {
        if (_spaces.Count < 2) return;
        var next = (_activeSpace + delta + _spaces.Count) % _spaces.Count;
        SwitchSpace(next);
    }

    /// <summary>Selects a space by zero-based index.</summary>
    private void SelectSpaceByIndex(int index)
    {
        if (index >= 0 && index < _spaces.Count) SwitchSpace(index);
    }

    /// <summary>Cycles through tabs in the current space by the given delta (+1 right, -1 left).</summary>
    private void CycleTab(int delta)
    {
        if (Tabs.Count < 2) return;
        var idx = Tabs.IndexOf(CurrentTab!);
        if (idx < 0) return;
        CurrentTab = Tabs[(idx + delta + Tabs.Count) % Tabs.Count];
    }

    /// <summary>Selects a tab by zero-based index in the current space.</summary>
    private void SelectTabByIndex(int index)
    {
        if (index >= 0 && index < Tabs.Count) CurrentTab = Tabs[index];
    }

    /// <summary>Moves keyboard focus to the address bar / URL input.</summary>
    private void FocusAddressBar()
    {
        // Open the navigation search popup pre-filled with the current URL, identical to
        // clicking the domain pill in the toolbar.
        OpenSearch(SearchPurpose.Navigate, CurrentTab?.ActiveUrl);
    }

    private void Extensions_Click(object sender, RoutedEventArgs e)
    {
        var open = !ExtensionsPopup.IsOpen;
        CloseMenusExcept(ExtensionsPopup);
        if (open)
        {
            AnimateExtensionsButton();
            RenderExtensionsMenu();
        }
        ExtensionsPopup.IsOpen = open;
    }

    private void AnimateExtensionsButton()
    {
        if (ExtensionsButtonRotate is null) return;
        var anim = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromMilliseconds(420)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ExtensionsButtonRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    private void ExtensionsPopup_Opened(object sender, EventArgs e)
    {
        if (ExtensionsCard is null) return;
        ExtensionsCard.Opacity = 0;
        ExtensionsCardScale.ScaleX = 0.92;
        ExtensionsCardScale.ScaleY = 0.92;
        ExtensionsCardShift.Y = -10;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220))) { EasingFunction = ease };
        var scaleX = new DoubleAnimation(0.92, 1.0, new Duration(TimeSpan.FromMilliseconds(260))) { EasingFunction = ease };
        var scaleY = new DoubleAnimation(0.92, 1.0, new Duration(TimeSpan.FromMilliseconds(260))) { EasingFunction = ease };
        var shift = new DoubleAnimation(-10, 0, new Duration(TimeSpan.FromMilliseconds(260))) { EasingFunction = ease };

        ExtensionsCard.BeginAnimation(UIElement.OpacityProperty, fade);
        ExtensionsCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        ExtensionsCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        ExtensionsCardShift.BeginAnimation(TranslateTransform.YProperty, shift);
    }

    private void RenderExtensionsMenu()
    {
        if (ExtensionsList is null || ExtensionsEmpty is null || ExtensionsSubtitle is null) return;
        ExtensionsList.Children.Clear();
        var extensions = ExtensionManager.Extensions.ToList();
        var activeCount = extensions.Count(x => x.IsEnabled);

        ExtensionsSubtitle.Text = extensions.Count == 0
            ? "Нет установленных"
            : $"{extensions.Count} {GetNounEnding(extensions.Count, "расширение", "расширения", "расширений")} ({activeCount} активных)";

        if (extensions.Count == 0)
        {
            ExtensionsEmpty.Visibility = Visibility.Visible;
            ExtensionsScroll.Visibility = Visibility.Collapsed;
            return;
        }

        ExtensionsEmpty.Visibility = Visibility.Collapsed;
        ExtensionsScroll.Visibility = Visibility.Visible;

        var index = 0;
        foreach (var ext in extensions)
        {
            var card = BuildExtensionRow(ext, index++);
            ExtensionsList.Children.Add(card);
        }
    }

    private static string GetNounEnding(int number, string one, string two, string five)
    {
        var n = Math.Abs(number) % 100;
        var n1 = n % 10;
        if (n > 10 && n < 20) return five;
        if (n1 > 1 && n1 < 5) return two;
        if (n1 == 1) return one;
        return five;
    }

    private UIElement BuildExtensionRow(ExtensionMetadata ext, int index)
    {
        var info = ExtensionManager.ReadManifestInfo(ext);

        var border = new Border
        {
            Margin = new Thickness(0, 0, 0, 7),
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(14),
            Background = TryFindResource("SurfaceBrush") as Brush ?? Brushes.Transparent,
            BorderBrush = TryFindResource("BorderBrush") as Brush ?? Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Opacity = 0,
            RenderTransform = new TranslateTransform(0, 8)
        };

        // Hover effect
        border.MouseEnter += (s, e) =>
        {
            border.Background = TryFindResource("SurfaceRaisedBrush") as Brush ?? border.Background;
            border.BorderBrush = TryFindResource("AccentMutedBrush") as Brush ?? border.BorderBrush;
        };
        border.MouseLeave += (s, e) =>
        {
            border.Background = TryFindResource("SurfaceBrush") as Brush ?? border.Background;
            border.BorderBrush = TryFindResource("BorderBrush") as Brush ?? border.BorderBrush;
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icon
        var iconHost = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(10),
            Background = TryFindResource("SurfaceInsetBrush") as Brush ?? Brushes.Transparent,
            BorderBrush = TryFindResource("BorderBrush") as Brush ?? Brushes.Transparent,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = WpfHorizontalAlignment.Left,
            VerticalAlignment = WpfVerticalAlignment.Center
        };

        if (!string.IsNullOrWhiteSpace(info.IconPath) && File.Exists(info.IconPath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.DecodePixelWidth = 64;
                bmp.UriSource = new Uri(info.IconPath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                iconHost.Child = new Image
                {
                    Source = bmp,
                    Width = 20,
                    Height = 20,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    HorizontalAlignment = WpfHorizontalAlignment.Center,
                    VerticalAlignment = WpfVerticalAlignment.Center
                };
            }
            catch
            {
                iconHost.Child = CreateDefaultExtensionIcon();
            }
        }
        else
        {
            iconHost.Child = CreateDefaultExtensionIcon();
        }
        Grid.SetColumn(iconHost, 0);
        grid.Children.Add(iconHost);

        // Titles & Action clickable area
        var textStack = new StackPanel
        {
            VerticalAlignment = WpfVerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            Cursor = Cursors.Hand
        };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var titleText = new TextBlock
        {
            Text = ext.Name,
            Foreground = TryFindResource("ChromeText") as Brush ?? Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            MaxWidth = 180,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var versionText = new TextBlock
        {
            Text = " v" + ext.Version,
            Foreground = TryFindResource("ChromeMuted") as Brush ?? Brushes.Gray,
            FontSize = 10,
            Margin = new Thickness(4, 2, 0, 0),
            VerticalAlignment = WpfVerticalAlignment.Center
        };
        titleRow.Children.Add(titleText);
        titleRow.Children.Add(versionText);
        textStack.Children.Add(titleRow);

        var descText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(ext.Description) ? (ext.IsEnabled ? "Активно" : "Отключено") : ext.Description,
            Foreground = TryFindResource("ChromeMuted") as Brush ?? Brushes.Gray,
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 0),
            MaxWidth = 200,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        textStack.Children.Add(descText);

        textStack.MouseLeftButtonUp += (s, e) =>
        {
            var targetUrl = info.TargetUrl;
            if (!string.IsNullOrWhiteSpace(targetUrl))
            {
                _ = AddTabAsync(targetUrl);
                ExtensionsPopup.IsOpen = false;
            }
            else
            {
                ShowToast(ext.Name, "Расширение активно и работает в фоновом режиме на веб-страницах");
                ExtensionsPopup.IsOpen = false;
            }
        };

        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        // Actions: Toggle switch + remove button
        var actionsStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = WpfVerticalAlignment.Center
        };

        var toggle = new System.Windows.Controls.Primitives.ToggleButton
        {
            IsChecked = ext.IsEnabled,
            Style = (TryFindResource("LumaSwitch") as Style) ?? (TryFindResource("LumaToggleSwitch") as Style),
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = ext.IsEnabled ? "Отключить расширение" : "Включить расширение",
            Cursor = Cursors.Hand
        };
        toggle.Click += async (s, e) =>
        {
            e.Handled = true;
            var enable = toggle.IsChecked == true;
            var profile = CurrentTab?.ActiveView?.CoreWebView2?.Profile ?? ExtensionManager.Profile;
            await ExtensionManager.ToggleExtensionAsync(profile, ext.Id, enable);
            ext.IsEnabled = enable;
            descText.Text = string.IsNullOrWhiteSpace(ext.Description) ? (enable ? "Активно" : "Отключено") : ext.Description;
            toggle.ToolTip = enable ? "Отключить расширение" : "Включить расширение";
        };
        actionsStack.Children.Add(toggle);

        var removeBtn = new Button
        {
            Style = TryFindResource("BareIconButton") as Style,
            Width = 26,
            Height = 26,
            ToolTip = "Удалить расширение",
            Cursor = Cursors.Hand
        };
        var removeIcon = new System.Windows.Shapes.Path
        {
            Width = 11,
            Height = 11,
            Stretch = System.Windows.Media.Stretch.Uniform,
            Data = TryFindResource("IconTrash2") as Geometry,
            Stroke = TryFindResource("ChromeMuted") as Brush ?? Brushes.Gray,
            StrokeThickness = 1.8,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        removeBtn.Content = removeIcon;
        removeBtn.Click += async (s, e) =>
        {
            e.Handled = true;
            var profile = CurrentTab?.ActiveView?.CoreWebView2?.Profile ?? ExtensionManager.Profile;
            await ExtensionManager.RemoveExtensionAsync(profile, ext.Id);
            RenderExtensionsMenu();
            ShowToast("Расширение удалено", ext.Name);
        };
        actionsStack.Children.Add(removeBtn);

        Grid.SetColumn(actionsStack, 2);
        grid.Children.Add(actionsStack);

        border.Child = grid;

        // Cascade entrance animation
        var delay = TimeSpan.FromMilliseconds(40 + index * 35);
        var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220)))
        {
            BeginTime = delay,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slideIn = new DoubleAnimation(8, 0, new Duration(TimeSpan.FromMilliseconds(240)))
        {
            BeginTime = delay,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        border.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        ((TranslateTransform)border.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slideIn);

        return border;
    }

    private UIElement CreateDefaultExtensionIcon()
    {
        return new System.Windows.Shapes.Path
        {
            Width = 15,
            Height = 15,
            Stretch = System.Windows.Media.Stretch.Uniform,
            Data = TryFindResource("IconPuzzle") as Geometry,
            Stroke = TryFindResource("AccentMutedBrush") as Brush ?? Brushes.MediumPurple,
            StrokeThickness = 1.8,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center
        };
    }

    private void ExtensionsStore_Click(object sender, RoutedEventArgs e)
    {
        ExtensionsPopup.IsOpen = false;
        _ = AddTabAsync("https://chromewebstore.google.com");
    }

    private void ExtensionsManage_Click(object sender, RoutedEventArgs e)
    {
        ExtensionsPopup.IsOpen = false;
        _ = OpenExtensionsAsync();
    }

    private sealed class PendingExtensionInstall
    {
        public string? Id { get; init; }
        public string? Title { get; init; }
        public string? LocalCrxPath { get; init; }
        public string? Source { get; init; }
        public CoreWebView2Profile? Profile { get; init; }
    }

    private PendingExtensionInstall? _pendingExtensionInstall;

    public void PromptInstallFromStore(string extId, string? title, string? iconUrl, string source, CoreWebView2Profile? profile, IReadOnlyList<string>? permissions = null)
    {
        profile ??= CurrentTab?.ActiveView?.CoreWebView2?.Profile ?? ExtensionManager.Profile;
        var displayTitle = string.IsNullOrWhiteSpace(title) ? "Расширение" : title.Trim();

        _pendingExtensionInstall = new PendingExtensionInstall
        {
            Id = extId,
            Title = displayTitle,
            Source = source,
            Profile = profile
        };

        ExtensionPromptTitle.Text = $"Добавить «{displayTitle}» в Luma Browser?";
        ExtensionPromptOrigin.Text = string.Equals(source, "edge", StringComparison.OrdinalIgnoreCase)
            ? "Магазин Microsoft Edge Add-ons"
            : "Интернет-магазин Chrome";

        ExtensionPromptPermissionsList.ItemsSource = permissions != null && permissions.Count > 0
            ? permissions
            : new[]
            {
                "Чтение и изменение всех данных на всех веб-сайтах",
                "Отображение всплывающих уведомлений"
            };

        ExtensionPromptIcon.Source = null;
        if (!string.IsNullOrWhiteSpace(iconUrl) && Uri.TryCreate(iconUrl, UriKind.Absolute, out var iconUri))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = iconUri;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                ExtensionPromptIcon.Source = bmp;
                ExtensionPromptIcon.Visibility = Visibility.Visible;
                ExtensionPromptFallbackIcon.Visibility = Visibility.Collapsed;
            }
            catch
            {
                ExtensionPromptIcon.Visibility = Visibility.Collapsed;
                ExtensionPromptFallbackIcon.Visibility = Visibility.Visible;
            }
        }
        else
        {
            ExtensionPromptIcon.Visibility = Visibility.Collapsed;
            ExtensionPromptFallbackIcon.Visibility = Visibility.Visible;
        }

        ExtensionPromptProgress.Visibility = Visibility.Collapsed;
        ExtensionPromptInstallBtn.IsEnabled = true;
        ExtensionPromptCancelBtn.IsEnabled = true;
        ExtensionPromptInstallBtnText.Text = "Добавить расширение";

        AnimateExtensionsButton();
        CloseMenusExcept(ExtensionPromptPopup);
        ExtensionPromptPopup.IsOpen = true;
    }

    public void ShowExtensionPromptForLocalCrx(string crxPath, string name, IReadOnlyList<string> permissions, byte[]? iconBytes)
    {
        var displayTitle = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(crxPath) : name.Trim();
        var profile = CurrentTab?.ActiveView?.CoreWebView2?.Profile ?? ExtensionManager.Profile;

        _pendingExtensionInstall = new PendingExtensionInstall
        {
            LocalCrxPath = crxPath,
            Title = displayTitle,
            Profile = profile
        };

        ExtensionPromptTitle.Text = $"Добавить «{displayTitle}» в Luma Browser?";
        ExtensionPromptOrigin.Text = "Локальный пакет расширения (.crx)";

        ExtensionPromptPermissionsList.ItemsSource = permissions != null && permissions.Count > 0
            ? permissions
            : new[] { "Чтение и изменение данных на посещаемых веб-сайтах" };

        ExtensionPromptIcon.Source = null;
        if (iconBytes != null && iconBytes.Length > 0)
        {
            try
            {
                using var ms = new MemoryStream(iconBytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                ExtensionPromptIcon.Source = bmp;
                ExtensionPromptIcon.Visibility = Visibility.Visible;
                ExtensionPromptFallbackIcon.Visibility = Visibility.Collapsed;
            }
            catch
            {
                ExtensionPromptIcon.Visibility = Visibility.Collapsed;
                ExtensionPromptFallbackIcon.Visibility = Visibility.Visible;
            }
        }
        else
        {
            ExtensionPromptIcon.Visibility = Visibility.Collapsed;
            ExtensionPromptFallbackIcon.Visibility = Visibility.Visible;
        }

        ExtensionPromptProgress.Visibility = Visibility.Collapsed;
        ExtensionPromptInstallBtn.IsEnabled = true;
        ExtensionPromptCancelBtn.IsEnabled = true;
        ExtensionPromptInstallBtnText.Text = "Добавить расширение";

        AnimateExtensionsButton();
        CloseMenusExcept(ExtensionPromptPopup);
        ExtensionPromptPopup.IsOpen = true;
    }

    private void ExtensionPromptPopup_Opened(object sender, EventArgs e)
    {
        if (ExtensionPromptCard is null) return;
        ExtensionPromptCard.Opacity = 0;
        ExtensionPromptCardScale.ScaleX = 0.88;
        ExtensionPromptCardScale.ScaleY = 0.88;
        ExtensionPromptCardShift.Y = -10;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220))) { EasingFunction = ease };
        var scaleX = new DoubleAnimation(0.88, 1.0, new Duration(TimeSpan.FromMilliseconds(260))) { EasingFunction = ease };
        var scaleY = new DoubleAnimation(0.88, 1.0, new Duration(TimeSpan.FromMilliseconds(260))) { EasingFunction = ease };
        var shift = new DoubleAnimation(-10, 0, new Duration(TimeSpan.FromMilliseconds(260))) { EasingFunction = ease };

        ExtensionPromptCard.BeginAnimation(UIElement.OpacityProperty, fade);
        ExtensionPromptCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        ExtensionPromptCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        ExtensionPromptCardShift.BeginAnimation(TranslateTransform.YProperty, shift);
    }

    private void ExtensionPromptCancel_Click(object sender, RoutedEventArgs e)
    {
        CloseExtensionPrompt();
        _pendingExtensionInstall = null;
        if (CurrentTab?.ActiveView?.CoreWebView2 != null)
        {
            try
            {
                _ = CurrentTab.ActiveView.CoreWebView2.ExecuteScriptAsync(@"
                    (() => {
                        if (typeof window.__lumaOnCancelled === 'function') {
                            try { window.__lumaOnCancelled(); } catch(e) {}
                        } else if (window.__lumaEdgeCallback) {
                            try { window.__lumaEdgeCallback('user_cancelled'); } catch(e) {}
                            window.__lumaEdgeCallback = null;
                        }
                    })()
                ");
            }
            catch { }
        }
    }

    private async void ExtensionPromptInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingExtensionInstall is not { } pending) return;

        var profile = pending.Profile ?? CurrentTab?.ActiveView?.CoreWebView2?.Profile ?? ExtensionManager.Profile;
        if (profile == null)
        {
            ShowToast("Ошибка установки", "Профиль браузера недоступен", true);
            CloseExtensionPrompt();
            return;
        }

        ExtensionPromptInstallBtn.IsEnabled = false;
        ExtensionPromptCancelBtn.IsEnabled = false;
        ExtensionPromptInstallBtnText.Text = "Установка...";
        ExtensionPromptProgress.Visibility = Visibility.Visible;

        ExtensionMetadata? meta = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(pending.LocalCrxPath) && File.Exists(pending.LocalCrxPath))
            {
                meta = await ExtensionManager.InstallFromLocalFileAsync(profile, pending.LocalCrxPath);
                try { File.Delete(pending.LocalCrxPath); } catch { }
            }
            else if (!string.IsNullOrWhiteSpace(pending.Id))
            {
                meta = await ExtensionManager.InstallFromStoreAsync(profile, pending.Id, pending.Source);
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }

        if (meta != null)
        {
            CloseExtensionPrompt();
            _pendingExtensionInstall = null;
            ShowToast("Расширение установлено", meta.Name);
            RenderExtensionsMenu();

            if (CurrentTab?.ActiveView?.CoreWebView2 != null)
            {
                try
                {
                    await CurrentTab.ActiveView.CoreWebView2.ExecuteScriptAsync(@"
                        (() => {
                            if (typeof window.__lumaOnInstalled === 'function') {
                                try { window.__lumaOnInstalled(); } catch(e) {}
                            } else if (window.__lumaEdgeCallback) {
                                try { window.__lumaEdgeCallback('success'); } catch(e) {}
                                window.__lumaEdgeCallback = null;
                            }
                            const btns = document.querySelectorAll('[data-luma-patched]');
                            btns.forEach(b => {
                                const textSpan = b.querySelector('.fui-Button__text, span, div') || b;
                                textSpan.innerText = '✓ Добавлено в Luma';
                                b.style.setProperty('background', 'linear-gradient(135deg, #10B981 0%, #059669 100%)', 'important');
                                b.disabled = true;
                            });
                        })()
                    ");
                }
                catch { }
            }
        }
        else
        {
            ExtensionPromptProgress.Visibility = Visibility.Collapsed;
            ExtensionPromptInstallBtn.IsEnabled = true;
            ExtensionPromptCancelBtn.IsEnabled = true;
            ExtensionPromptInstallBtnText.Text = "Повторить";
            ShowToast("Ошибка установки", "Не удалось скачать или установить расширение", true);
        }
    }

    private void CloseExtensionPrompt()
    {
        if (ExtensionPromptCard is null || !ExtensionPromptPopup.IsOpen)
        {
            ExtensionPromptPopup.IsOpen = false;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(ExtensionPromptCard.Opacity, 0, new Duration(TimeSpan.FromMilliseconds(160))) { EasingFunction = ease };
        var scaleX = new DoubleAnimation(ExtensionPromptCardScale.ScaleX, 0.90, new Duration(TimeSpan.FromMilliseconds(160))) { EasingFunction = ease };
        var scaleY = new DoubleAnimation(ExtensionPromptCardScale.ScaleY, 0.90, new Duration(TimeSpan.FromMilliseconds(160))) { EasingFunction = ease };
        var shift = new DoubleAnimation(ExtensionPromptCardShift.Y, -8, new Duration(TimeSpan.FromMilliseconds(160))) { EasingFunction = ease };

        fade.Completed += (_, _) =>
        {
            ExtensionPromptPopup.IsOpen = false;
            ExtensionPromptCard.Opacity = 1;
            ExtensionPromptCardScale.ScaleX = 1;
            ExtensionPromptCardScale.ScaleY = 1;
            ExtensionPromptCardShift.Y = 0;
        };

        ExtensionPromptCard.BeginAnimation(UIElement.OpacityProperty, fade);
        ExtensionPromptCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        ExtensionPromptCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        ExtensionPromptCardShift.BeginAnimation(TranslateTransform.YProperty, shift);
    }

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

