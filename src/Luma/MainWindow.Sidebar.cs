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
    private void RelinkPinnedTabs()
    {
        if (_spaces.Count == 0) return;
        var space = _spaces[_activeSpace];
        var owned = new HashSet<string>();
        foreach (var site in space.State.LooseItems)
        {
            var tab = (!string.IsNullOrEmpty(site.TabId) ? space.Tabs.FirstOrDefault(t => t.Id == site.TabId) : null)
                ?? space.Tabs.FirstOrDefault(t => !owned.Contains(t.Id) && (string.Equals(t.FullUrl, site.Url, StringComparison.OrdinalIgnoreCase) || string.Equals(t.SecondaryUrl, site.Url, StringComparison.OrdinalIgnoreCase)))
                ?? space.Tabs.FirstOrDefault(t => !owned.Contains(t.Id) && t.IsPinned && SameSite(t.FullUrl, site.Url));
            if (tab is null) { site.TabId = null; continue; }
            site.TabId = tab.Id; tab.IsPinned = true; tab.FolderId = null; owned.Add(tab.Id);
        }
        foreach (var t in space.Tabs) if (t.IsPinned && !owned.Contains(t.Id)) t.IsPinned = false;
    }

    private void FilterTabs()
    {
        RelinkPinnedTabs();
        var view = CollectionViewSource.GetDefaultView(Tabs);
        view.Filter = o => o is BrowserTab t && string.IsNullOrEmpty(t.FolderId) && !t.IsPinned;
        view.Refresh();
        if (TabsList is null || TabsListContainer is null) return;
        TabsList.ItemsSource = Tabs;
        TabsList.Items.Refresh();
        // The tabs section no longer collapses, so its height is never constrained.
        TabsListContainer.BeginAnimation(MaxHeightProperty, null);
        TabsListContainer.ClearValue(MaxHeightProperty);
        TabsListContainer.InvalidateMeasure();
    }
    private void RefreshFolders()
    {
        if (FoldersHost is null) return; FoldersHost.Children.Clear(); LooseItemsHost.Children.Clear(); _folderVisuals.Clear();
        foreach (var folder in _spaces[_activeSpace].State.Folders) FoldersHost.Children.Add(Folder(folder));
        foreach (var item in _spaces[_activeSpace].State.LooseItems) LooseItemsHost.Children.Add(SiteRow(item, null, true));
        if (FoldersContainer is not null)
        {
            FoldersContainer.BeginAnimation(MaxHeightProperty, null);
            if (_foldersSectionExpanded) FoldersContainer.ClearValue(MaxHeightProperty); else FoldersContainer.MaxHeight = 0;
        }
        if (PinnedContainer is not null)
        {
            PinnedContainer.BeginAnimation(MaxHeightProperty, null);
            PinnedContainer.ClearValue(MaxHeightProperty);
        }
    }
    private FrameworkElement Folder(FolderState folder)
    {
        var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
        var header = new Border { Height = 38, CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, AllowDrop = true, Background = Brushes.Transparent };
        var row = new Grid { Margin = new Thickness(7, 0, 7, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition());

        var rotation = new RotateTransform(folder.Expanded ? 0 : -90);
        var arrow = new IconPath
        {
            Width = 10, Height = 10, Stretch = System.Windows.Media.Stretch.Uniform, Data = (Geometry)FindResource("IconChevronDown"),
            Stroke = (Brush)Resources["ChromeMuted"], StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            RenderTransform = rotation, RenderTransformOrigin = new Point(.5, .5), VerticalAlignment = WpfVerticalAlignment.Center
        };

        // Layered Lucide folder/folder-open paths retain the existing open animation.
        var folderScale = new ScaleTransform(1, 1);
        var iconHost = new Grid
        {
            Width = 20, Height = 20, RenderTransform = folderScale,
            RenderTransformOrigin = new Point(.5, .5), VerticalAlignment = WpfVerticalAlignment.Center
        };
        var back = new IconPath
        {
            Width = 19, Height = 19, Stretch = System.Windows.Media.Stretch.Uniform, Data = (Geometry)FindResource("IconFolderBack"),
            Fill = Brushes.Transparent, Stroke = folder.Expanded ? (Brush)Resources["AccentBrush"] : (Brush)Resources["BorderStrongBrush"], StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round, HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center
        };
        var frontTranslate = new TranslateTransform(0, folder.Expanded ? 1.8 : 0);
        var front = new IconPath
        {
            Width = 19, Height = 19, Stretch = System.Windows.Media.Stretch.Uniform, Data = (Geometry)FindResource("IconFolderFront"),
            Fill = Brushes.Transparent, Stroke = folder.Expanded ? (Brush)Resources["AccentMutedBrush"] : (Brush)Resources["ChromeMuted"], StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round, RenderTransform = frontTranslate,
            HorizontalAlignment = WpfHorizontalAlignment.Center, VerticalAlignment = WpfVerticalAlignment.Center
        };
        var shine = new IconPath
        {
            Width = 8, Height = 8, Stretch = System.Windows.Media.Stretch.Uniform, Data = (Geometry)FindResource("IconFolderShine"),
            Fill = Brushes.Transparent, Stroke = (Brush)Resources["ChromeSecondary"], StrokeThickness = 1.7, StrokeLineJoin = PenLineJoin.Round, Opacity = folder.Expanded ? .82 : 0,
            HorizontalAlignment = WpfHorizontalAlignment.Left, VerticalAlignment = WpfVerticalAlignment.Bottom,
            Margin = new Thickness(1, 0, 0, 1), IsHitTestVisible = false
        };
        iconHost.Children.Add(back); iconHost.Children.Add(front); iconHost.Children.Add(shine);
        Grid.SetColumn(iconHost, 1);

        var label = new TextBlock { Text = folder.Name, FontSize = 12.5, FontWeight = FontWeights.Medium, Foreground = (Brush)Resources["ChromeSecondary"], VerticalAlignment = WpfVerticalAlignment.Center };
        Grid.SetColumn(label, 2);
        var editor = new TextBox { Text = folder.Name, FontSize = 12.5, Foreground = Brushes.White, Background = (Brush)Resources["SurfaceRaisedBrush"], BorderBrush = (Brush)Resources["BorderStrongBrush"], BorderThickness = new Thickness(1), Padding = new Thickness(5, 2, 5, 2), Visibility = Visibility.Collapsed };
        Grid.SetColumn(editor, 2);
        row.Children.Add(arrow); row.Children.Add(iconHost); row.Children.Add(label); row.Children.Add(editor); header.Child = row;

        var itemsTranslate = new TranslateTransform(0, folder.Expanded ? 0 : -7);
        var items = new StackPanel { Opacity = folder.Expanded ? 1 : 0, RenderTransform = itemsTranslate };
        foreach (var site in folder.Items) items.Children.Add(SiteRow(site, folder, false));
        var content = new Border { ClipToBounds = true, MaxHeight = folder.Expanded ? 1000 : 0, Padding = new Thickness(16, 1, 0, 1), Child = items };
        outer.Children.Add(header); outer.Children.Add(content);

        var visual = new FolderVisual
        {
            Label = label, Editor = editor, Content = content, Items = items, Chevron = rotation,
            FolderBack = back, FolderFront = front, FolderShine = shine, FolderFrontTranslate = frontTranslate,
            FolderScale = folderScale, ItemsTranslate = itemsTranslate
        };
        _folderVisuals[folder.Id] = visual;
        var hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        hoverTimer.Tick += (_, _) => { hoverTimer.Stop(); if (!folder.Expanded) ToggleFolder(folder, visual); };
        header.MouseEnter += (_, _) => header.Background = (Brush)Resources["SurfaceRaisedBrush"];
        header.MouseLeave += (_, _) => header.Background = Brushes.Transparent;
        header.MouseLeftButtonUp += (_, e) => { ToggleFolder(folder, visual); e.Handled = true; };
        header.MouseRightButtonUp += (_, e) => { ShowFolderMenu(folder); e.Handled = true; };
        header.DragEnter += (_, e) => { if (Supported(e.Data)) { header.Background = (Brush)Resources["AccentSoftBrush"]; if (!folder.Expanded) hoverTimer.Start(); e.Effects = DragDropEffects.Move; e.Handled = true; } };
        header.DragLeave += (_, _) => { hoverTimer.Stop(); header.Background = Brushes.Transparent; };
        header.Drop += (_, e) => { hoverTimer.Stop(); DropInFolder(folder, e.Data); e.Handled = true; };
        editor.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Rename(folder, visual, true); e.Handled = true; } else if (e.Key == Key.Escape) { Rename(folder, visual, false); e.Handled = true; } };
        editor.LostKeyboardFocus += (_, _) => { if (editor.Visibility == Visibility.Visible) Rename(folder, visual, true); };
        return outer;
    }
    private FrameworkElement SiteRow(SavedSite site, FolderState? source, bool loose)
    {
        var button = new Button { Height = 34, Style = (Style)FindResource("SidebarButton"), AllowDrop = true, Tag = new SavedDrag { Site = site, Source = source, Loose = loose } };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var image = new Image { Width = 15, Height = 15, Margin = new Thickness(0, 0, 9, 0) };
        void UseLumaIcon() { try { image.Source = new BitmapImage(new Uri(BrowserTab.LumaIconUri)); } catch { } }
        image.ImageFailed += (_, _) => UseLumaIcon();
        try { image.Source = new BitmapImage(new Uri(BrowserTab.FaviconOf(site.Url))); } catch { UseLumaIcon(); }
        panel.Children.Add(image);
        panel.Children.Add(new TextBlock { Text = site.Title, Foreground = (Brush)Resources["ChromeSecondary"], FontSize = 12, FontWeight = FontWeights.Medium, VerticalAlignment = WpfVerticalAlignment.Center });
        button.Content = panel;
        button.MouseRightButtonUp += (_, e) => { ShowSavedSiteMenu(site, source, loose); e.Handled = true; };
        // Activate on the first tunneling mouse-down. Using both down and up created two
        // competing activations while CurrentTab rebuilt the sidebar, so pinned rows sometimes
        // appeared to require a second click.
        button.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            _dragStart = e.GetPosition(this);
            if (!IsActive) Activate();
            var request = ++_tabActivationSerial;
            var live = ResolveSiteTab(site, source);
            if (live is not null) ActivateTabFromUser(live, request);
            else Dispatcher.BeginInvoke(async () => await OpenSavedSiteAsync(site, source, request));
            e.Handled = true;
        };
        button.MouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed && Moved(e.GetPosition(this))) { button.Opacity = .72; DragDrop.DoDragDrop(button, new DataObject("LumaSaved", button.Tag), DragDropEffects.Move); button.Opacity = 1; } };
        button.DragEnter += (_, e) => { if (Supported(e.Data)) { e.Effects = DragDropEffects.Move; e.Handled = true; } };
        button.Drop += (_, e) => { DropBefore(site, source, loose, e.Data); e.Handled = true; };
        return button;
    }
    private async Task OpenSavedSiteAsync(SavedSite site, FolderState? folder, int activationRequest)
    {
        // A pinned row owns exactly one live tab. Resolve it by id, then by URL, then by site.
        // Only spawn a tab when the row truly has none, and mark the pin before the tab is added,
        // so clicking a pinned row can never flash a duplicate plain tab in the TABS list.
        var open = ResolveSiteTab(site, folder);
        if (open is null)
        {
            open = await AddTabAsync(site.Url, false, folder?.Id, pinned: folder is null);
            if (folder is null) { site.TabId = open.Id; FilterTabs(); Save(); }
        }
        else if (folder is null && (!open.IsPinned || site.TabId != open.Id))
        {
            site.TabId = open.Id; open.IsPinned = true; open.FolderId = null; FilterTabs(); Save();
        }
        if (string.Equals(open.SecondaryUrl, site.Url, StringComparison.OrdinalIgnoreCase)) open.ActivePane = "secondary"; else open.ActivePane = "primary";
        ActivateTabFromUser(open, activationRequest);
    }

    private void ShowSavedSiteMenu(SavedSite site, FolderState? folder, bool loose)
    {
        var menu = NewMenu();
        if (loose) Add(menu, "Открепить", () => UnpinSavedSite(site));
        else Add(menu, "Закрепить", () => PinSavedSite(site, folder, loose));
        Add(menu, "Удалить", () => DeleteSavedSite(site, folder, loose));
        OpenMenu(menu);
    }

    private void PinSavedSite(SavedSite site, FolderState? folder, bool loose)
    {
        if (loose) return;
        folder?.Items.Remove(site);
        if (!_spaces[_activeSpace].State.LooseItems.Any(x => string.Equals(x.Url, site.Url, StringComparison.OrdinalIgnoreCase))) _spaces[_activeSpace].State.LooseItems.Add(site);
        foreach (var tab in Tabs.Where(t => t.FolderId == folder?.Id && (t.FullUrl == site.Url || t.SecondaryUrl == site.Url))) tab.FolderId = null;
        Save(); RefreshFolders(); FilterTabs();
    }

    private void DeleteSavedSite(SavedSite site, FolderState? folder, bool loose)
    {
        if (loose) _spaces[_activeSpace].State.LooseItems.Remove(site); else folder?.Items.Remove(site);
        foreach (var tab in Tabs.Where(t => (folder is null || t.FolderId == folder.Id) && (t.FullUrl == site.Url || t.SecondaryUrl == site.Url)).ToList()) CloseTab(tab);
        Save(); RefreshFolders(); FilterTabs();
    }

    private void ToggleFolder(FolderState folder, FolderVisual visual)
    {
        folder.Expanded = !folder.Expanded;
        var ms = _state.AnimationsEnabled ? 285 : 0;
        var duration = TimeSpan.FromMilliseconds(ms);
        if (_foldersSectionExpanded)
        {
            FoldersContainer.BeginAnimation(MaxHeightProperty, null);
            FoldersContainer.ClearValue(MaxHeightProperty);
        }
        var height = folder.Expanded ? Math.Max(1, visual.Items.Children.Count * 37 + 2) : 0;
        var ease = new QuinticEase { EasingMode = folder.Expanded ? EasingMode.EaseOut : EasingMode.EaseInOut };

        visual.Content.BeginAnimation(MaxHeightProperty, new DoubleAnimation(visual.Content.ActualHeight, height, duration) { EasingFunction = ease });
        visual.Items.BeginAnimation(OpacityProperty, new DoubleAnimation(folder.Expanded ? 0 : 1, folder.Expanded ? 1 : 0, TimeSpan.FromMilliseconds(ms == 0 ? 0 : 190)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        visual.ItemsTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(folder.Expanded ? -7 : 0, folder.Expanded ? 0 : -7, duration) { EasingFunction = ease });
        visual.Chevron.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(folder.Expanded ? -90 : 0, folder.Expanded ? 0 : -90, duration) { EasingFunction = ease });
        visual.FolderFrontTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(folder.Expanded ? 0 : 1.8, folder.Expanded ? 1.8 : 0, duration) { EasingFunction = ease });
        visual.FolderShine.BeginAnimation(OpacityProperty, new DoubleAnimation(folder.Expanded ? 0 : .82, folder.Expanded ? .82 : 0, duration));
        visual.FolderBack.Fill = folder.Expanded ? (Brush)Resources["AccentBrush"] : (Brush)Resources["BorderStrongBrush"];
        visual.FolderFront.Fill = folder.Expanded ? (Brush)Resources["AccentMutedBrush"] : (Brush)Resources["ChromeMuted"];
        var pulse = new DoubleAnimation(1, folder.Expanded ? 1.12 : .94, TimeSpan.FromMilliseconds(ms == 0 ? 0 : 125)) { AutoReverse = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        visual.FolderScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        visual.FolderScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        Save();
    }

    // ---- Profile row: real spaces (same data as the bottom space-dots), rendered as letter avatars. ----
    // The avatar template root is a Grid: take the space either from Tag or from the row's DataContext.
    private static SpaceDotView? DotOf(object sender) => sender is FrameworkElement element ? element.Tag as SpaceDotView ?? element.DataContext as SpaceDotView : null;
    private void ProfileAvatar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (!IsActive) Activate();
        if (DotOf(sender) is { } dot) SwitchSpace(dot.Index);
        e.Handled = true;
    }
    private void ProfileAvatar_Click(object sender, MouseButtonEventArgs e) { if (DotOf(sender) is { } dot) SwitchSpace(dot.Index); e.Handled = true; }
    private void ProfileAvatar_MouseRightButtonUp(object sender, MouseButtonEventArgs e) { if (DotOf(sender) is { } dot) { ShowSpaceMenu(dot); e.Handled = true; } }
    private void AvatarAdd_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (!IsActive) Activate();
        AvatarAdd_Click(sender, e);
        e.Handled = true;
    }
    private void AvatarAdd_Click(object sender, MouseButtonEventArgs e)
    {
        CloseMenusExcept(NewSpacePopup);
        PaneHost.IsEnabled = false;
        NewSpaceNameBox.Text = "Пространство " + (_spaces.Count + 1);
        NewSpacePopup.IsOpen = true;
        e.Handled = true;
    }
    private async void NewSpacePopup_Opened(object sender, EventArgs e)
    {
        UpdateSpacePlaceholder();
        await FocusPopupTextBoxAsync(NewSpacePopup, NewSpaceNameBox, true);
    }
    private void NewSpacePopup_Closed(object sender, EventArgs e) => RestorePageInputAfterModal();
    private void NewSpaceNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CreateSpace_Click(sender, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Escape) { NewSpacePopup.IsOpen = false; e.Handled = true; }
    }
    private void UpdateSpacePlaceholder()
    {
        if (NewSpacePlaceholder is null) return;
        NewSpacePlaceholder.Visibility = string.IsNullOrEmpty(NewSpaceNameBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }
    private void NewSpaceNameBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateSpacePlaceholder();
    private void NewSpaceNameBox_FocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (NewSpaceField is null) return;
        NewSpaceField.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, NewSpaceNameBox.IsKeyboardFocusWithin ? "AccentBrush" : "BorderBrush");
    }
    private void CancelSpace_Click(object sender, RoutedEventArgs e) => NewSpacePopup.IsOpen = false;
    private void CreateSpace_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(NewSpaceNameBox.Text) ? "Пространство " + (_spaces.Count + 1) : NewSpaceNameBox.Text.Trim();
        var state = new SpaceState { Name = name, Folders = new List<FolderState>() };
        _state.Spaces.Add(state);
        _spaces.Add(new RuntimeSpace(state) { Loaded = true });
        NewSpacePopup.IsOpen = false;
        SwitchSpace(_spaces.Count - 1);
    }
    private void ShowFolderMenu(FolderState folder)
    {
        var menu = NewMenu(); Add(menu, "Переименовать", () => AskFolderName(folder)); Add(menu, "Новая вкладка в папке", () => OpenSearch(SearchPurpose.Folder, null, folder)); Add(menu, "Открыть все", async () => await OpenFolderAsync(folder), folder.Items.Count > 0);
        if (Tabs.Any(t => Belongs(t, folder))) Add(menu, "Закрыть все", () => CloseFolder(folder)); Sep(menu); Add(menu, "Удалить папку", () => AskDelete(folder)); OpenMenu(menu);
    }
    private void StartRename(FolderState folder) { if (!_folderVisuals.TryGetValue(folder.Id, out var v)) return; v.Label.Visibility = Visibility.Collapsed; v.Editor.Visibility = Visibility.Visible; v.Editor.Text = folder.Name; v.Editor.Focus(); v.Editor.SelectAll(); }
    private void Rename(FolderState folder, FolderVisual v, bool save) { if (save && !string.IsNullOrWhiteSpace(v.Editor.Text)) folder.Name = v.Editor.Text.Trim(); v.Label.Text = folder.Name; v.Editor.Visibility = Visibility.Collapsed; v.Label.Visibility = Visibility.Visible; Save(); }
    private async Task OpenFolderAsync(FolderState folder) { BrowserTab? last = null; foreach (var item in folder.Items) last = Tabs.FirstOrDefault(t => t.FullUrl == item.Url || t.SecondaryUrl == item.Url) ?? await AddTabAsync(item.Url, false, folder.Id); if (last is not null) CurrentTab = last; FilterTabs(); }
    private bool Belongs(BrowserTab tab, FolderState f) => tab.FolderId == f.Id || f.Items.Any(i => i.Url == tab.FullUrl || i.Url == tab.SecondaryUrl);
    private void CloseFolder(FolderState folder) { foreach (var tab in Tabs.Where(t => Belongs(t, folder)).ToList()) CloseTab(tab); FilterTabs(); }
    private void AskDelete(FolderState folder) { CloseTransientUi(); _deleteFolder = folder; DeleteFolderTitle.Text = $"Удалить папку {folder.Name}?"; DeleteFolderPopup.IsOpen = true; }
    private void SaveSite(FolderState folder, string title, string url) { if (!folder.Items.Any(i => i.Url == url)) folder.Items.Add(new SavedSite { Title = string.IsNullOrWhiteSpace(title) ? Domain(url) : title, Url = url }); }
    private static bool Supported(IDataObject data) => data.GetDataPresent("LumaTab") || data.GetDataPresent("LumaSaved");
    private bool Moved(Point p) => Math.Abs(p.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(p.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance;
    private void DropInFolder(FolderState folder, IDataObject data)
    {
        if (data.GetData("LumaTab") is BrowserTab tab) { SaveSite(folder, tab.DisplayTitle, tab.ActiveUrl); tab.FolderId = folder.Id; }
        else if (data.GetData("LumaSaved") is SavedDrag saved) { saved.Source?.Items.Remove(saved.Site); if (saved.Loose) _spaces[_activeSpace].State.LooseItems.Remove(saved.Site); if (!folder.Items.Contains(saved.Site)) folder.Items.Add(saved.Site); }
        folder.Expanded = true; Save(); RefreshFolders(); FilterTabs();
    }

    private void DropBefore(SavedSite target, FolderState? targetFolder, bool targetLoose, IDataObject data)
    {
        SavedSite? moved = null;
        if (data.GetData("LumaSaved") is SavedDrag saved)
        {
            if (ReferenceEquals(saved.Site, target)) return;
            saved.Source?.Items.Remove(saved.Site);
            if (saved.Loose) _spaces[_activeSpace].State.LooseItems.Remove(saved.Site);
            moved = saved.Site;
        }
        else if (data.GetData("LumaTab") is BrowserTab tab)
        {
            moved = new SavedSite { Title = tab.DisplayTitle, Url = tab.ActiveUrl };
            tab.FolderId = targetFolder?.Id;
        }
        if (moved is null) return;
        var list = targetLoose ? _spaces[_activeSpace].State.LooseItems : targetFolder!.Items;
        var index = Math.Max(0, list.IndexOf(target));
        if (!list.Any(x => string.Equals(x.Url, moved.Url, StringComparison.OrdinalIgnoreCase))) list.Insert(index, moved);
        Save(); RefreshFolders(); FilterTabs();
    }

    private void FoldersHeader_Click(object sender, MouseButtonEventArgs e)
    {
        for (var source = e.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source)) if (source is Button) return;
        _foldersSectionExpanded = !_foldersSectionExpanded;
        var ms = _state.AnimationsEnabled ? 230 : 0;
        var target = _foldersSectionExpanded ? Math.Max(1, FoldersHost.ActualHeight + 4) : 0;
        var heightAnimation = new DoubleAnimation(FoldersContainer.ActualHeight, target, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (_foldersSectionExpanded) heightAnimation.Completed += (_, _) =>
        {
            if (!_foldersSectionExpanded) return;
            FoldersContainer.BeginAnimation(MaxHeightProperty, null);
            FoldersContainer.ClearValue(MaxHeightProperty);
            FoldersContainer.InvalidateMeasure();
        };
        FoldersContainer.BeginAnimation(MaxHeightProperty, heightAnimation);
        FoldersSectionFrontTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_foldersSectionExpanded ? 0 : 1.8, _foldersSectionExpanded ? 1.8 : 0, TimeSpan.FromMilliseconds(ms)));
        FoldersSectionBack.Stroke = _foldersSectionExpanded ? (Brush)Resources["AccentBrush"] : (Brush)Resources["ChromeMuted"];
        FoldersSectionFront.Stroke = _foldersSectionExpanded ? (Brush)Resources["AccentMutedBrush"] : (Brush)Resources["ChromeMuted"];
        e.Handled = true;
    }

    private Task AnimateSpaceContentAsync(double from, double to, double opacityFrom, double opacityTo, int milliseconds)
    {
        SpaceContentTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        SpaceContentRoot.BeginAnimation(OpacityProperty, null);
        if (!_state.AnimationsEnabled || milliseconds <= 0) { SpaceContentTranslate.X = to; SpaceContentRoot.Opacity = opacityTo; return Task.CompletedTask; }
        var completion = new TaskCompletionSource<bool>();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var move = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease };
        move.Completed += (_, _) =>
        {
            SpaceContentTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            SpaceContentRoot.BeginAnimation(OpacityProperty, null);
            SpaceContentTranslate.X = to;
            SpaceContentRoot.Opacity = opacityTo;
            completion.TrySetResult(true);
        };
        SpaceContentTranslate.BeginAnimation(TranslateTransform.XProperty, move, HandoffBehavior.SnapshotAndReplace);
        SpaceContentRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(opacityFrom, opacityTo, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
        return completion.Task;
    }

    private void RefreshSpaces()
    {
        if (ProfileAvatars is null) return;
        var accent = (Brush)Resources["AccentBrush"];
        var accentSoft = (Brush)Resources["AccentSoftBrush"];
        var list = _spaces.Select((_, i) => new SpaceDotView
        {
            Index = i,
            Name = _spaces[i].State.Name,
            IsActive = i == _activeSpace,
            Size = i == _activeSpace ? 8 : 6,
            Brush = i == _activeSpace ? accentSoft : (Brush)Resources["SurfaceRaisedBrush"],
            TextBrush = i == _activeSpace ? Brushes.White : (Brush)Resources["ChromeSecondary"],
            OutlineBrush = i == _activeSpace ? accent : (Brush)Resources["BorderBrush"],
        }).ToList();
        ProfileAvatars.ItemsSource = list;
    }
    private void ShowSpaceMenu(SpaceDotView dot)
    {
        var menu = NewMenu();
        Add(menu, "Переключиться", () => SwitchSpace(dot.Index), !dot.IsActive);
        Sep(menu);
        Add(menu, "Удалить пространство", () => AskDeleteSpace(dot), _spaces.Count > 1);
        OpenMenu(menu);
    }
    private void AskDeleteSpace(SpaceDotView dot)
    {
        var open = dot.Index >= 0 && dot.Index < _spaces.Count ? _spaces[dot.Index].Tabs.Count : 0;
        Confirm($"Удалить пространство {dot.Name}?",
            open > 0 ? $"Будут закрыты вкладки: {open}. Папки и закреплённые сайты этого пространства также будут удалены."
                     : "Папки и закреплённые сайты этого пространства будут удалены.",
            () => DeleteSpace(dot.Index));
    }
    private void Confirm(string title, string text, Action action)
    {
        CloseTransientUi();
        _confirmAction = action; ConfirmTitle.Text = title; ConfirmText.Text = text; ConfirmPopup.IsOpen = true;
    }
    private void ConfirmYes_Click(object sender, RoutedEventArgs e) { var action = _confirmAction; _confirmAction = null; ConfirmPopup.IsOpen = false; action?.Invoke(); }
    private void ConfirmNo_Click(object sender, RoutedEventArgs e) { _confirmAction = null; ConfirmPopup.IsOpen = false; }
    private void ConfirmPopup_Closed(object sender, EventArgs e) => _confirmAction = null;

    private void DeleteSpace(int index)
    {
        if (_spaces.Count <= 1 || index < 0 || index >= _spaces.Count) { ShowToast("Нельзя удалить", "Должно остаться хотя бы одно пространство"); return; }
        var removed = _spaces[index]; foreach (var tab in removed.Tabs.ToList()) { ForgetViewState(tab.View); ForgetViewState(tab.SecondaryView); tab.Dispose(); } removed.Tabs.Clear();
        _spaces.RemoveAt(index); _state.Spaces.RemoveAt(index);
        if (_activeSpace > index) _activeSpace--; else if (_activeSpace == index) _activeSpace = Math.Min(index, _spaces.Count - 1);
        _state.ActiveSpace = _activeSpace; OnChanged(nameof(Tabs)); CurrentTab = _spaces[_activeSpace].Current ?? Tabs.FirstOrDefault();
        RefreshSpaces(); RefreshFolders(); FilterTabs(); Save();
    }

    private async void SwitchSpace(int index)
    {
        if (index < 0 || index >= _spaces.Count || index == _activeSpace) return;
        var request = ++_spaceSwitchSerial;
        var previousIndex = _activeSpace;
        var direction = index > previousIndex ? 1d : -1d;
        Save();
        _spaces[previousIndex].Current = CurrentTab;

        // Switch active space index immediately so the UI responds without any perceived lag
        _activeSpace = index;
        _state.ActiveSpace = index;
        RecordTesterAction($"Переключено пространство: {index + 1}");

        // Ensure space has at least one tab if it's currently empty
        if (_spaces[index].Tabs.Count == 0) _spaces[index].Current = EnsureHomeTab(_spaces[index]);
        OnChanged(nameof(Tabs));
        CurrentTab = _spaces[index].Current ?? Tabs.FirstOrDefault() ?? EnsureHomeTab(_spaces[index]);

        // Refresh UI immediately so the user sees the active space dot and new tabs list
        RefreshSpaces();
        RefreshFolders();
        FilterTabs();

        try
        {
            await LoadSpaceAsync(index);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }

        if (request != _spaceSwitchSerial) return;

        if (_spaces[index].Tabs.Count == 0) _spaces[index].Current = EnsureHomeTab(_spaces[index]);
        CurrentTab = _spaces[index].Current ?? Tabs.FirstOrDefault() ?? EnsureHomeTab(_spaces[index]);
        RefreshSpaces();
        RefreshFolders();
        FilterTabs();

        SpaceContentTranslate.X = 10 * direction;
        SpaceContentRoot.Opacity = .92;
        await AnimateSpaceContentAsync(10 * direction, 0, .92, 1, 145);
        if (request == _spaceSwitchSerial) Save();
    }
    private void Save()
    {
        if (((App)Application.Current).IsPrivateSession) return;
        foreach (var space in _spaces.Where(s => s.Loaded))
        {
            space.State.SessionTabs = space.Tabs.Where(t => !t.IsInternal || t.IsHome).Select(t => _pendingTabRestores.TryGetValue(t.Id, out var pending) ? pending : t.ToSession()).ToList();
            space.State.ActiveTabId = space.Current?.Id ?? "";
        }
        _state.ActiveSpace = _activeSpace;
        _stateStore.Save();
    }

    /// <summary>Suspends tabs nobody has touched for a while; Chromium hands the memory back.</summary>
}
