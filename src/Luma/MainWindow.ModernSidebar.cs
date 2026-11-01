﻿﻿﻿﻿﻿﻿﻿using System.Windows.Media.Effects;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using Image = System.Windows.Controls.Image;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace Luma;

public partial class MainWindow
{
    private bool _isToolsDrawerOpen = false;
    private bool _isSidebarExpanded = true;
    private bool _isSidebarHoverExpanded = false;
    private bool _suppressHoverExpandUntilLeave = false;
    private bool _isEnteringFromOutside = false;
    public static readonly DependencyProperty IsSidebarVisualExpandedProperty =
        DependencyProperty.Register(nameof(IsSidebarVisualExpanded), typeof(bool), typeof(MainWindow), new PropertyMetadata(true));

    public bool IsSidebarVisualExpanded
    {
        get => (bool)GetValue(IsSidebarVisualExpandedProperty);
        set => SetValue(IsSidebarVisualExpandedProperty, value);
    }

    // Workspace Modal editing state
    private int? _editingWorkspaceIndex = null;
    private string _selectedWorkspaceIcon = "рџЏ ";
    private string _selectedWorkspaceAccent = "#7C5CE4";
    private string _selectedWorkspaceGradientEnd = "#121214";
    private string _selectedWorkspaceContainerId = "default";

    // Long-press (>350ms) state for Bottom-Left Workspace Switcher
    private DispatcherTimer? _workspaceLongPressTimer;
    private bool _workspaceLongPressFired = false;

    // Glance target tab
    private BrowserTab? _glanceTargetTab;

    public static readonly string[] ZenWorkspaceEmojis =
    [
        "рџЏ ", "рџ’ј", "вљЎ", "рџЋ®", "рџ§Є", "рџЋЁ", "рџ“љ", "рџ”’", "рџЊђ", "рџљЂ", "рџЋµ", "рџ’Ћ"
    ];

    public static readonly (string Name, string Accent, string GradientEnd)[] ZenWorkspaceGradients =
    [
        ("Luma Violet", "#7C5CE4", "#2A1F4D"),
        ("Ocean Blue", "#3B82F6", "#172B4D"),
        ("Emerald", "#10B981", "#11382B"),
        ("Amber Gold", "#F59E0B", "#3D280A"),
        ("Cyber Pink", "#EC4899", "#3D1428"),
        ("Crimson", "#EF4444", "#3B1515"),
        ("Cyan Frost", "#06B6D4", "#0E343D"),
        ("Midnight", "#6366F1", "#1E1B4B")
    ];

    public sealed class DrawerHistoryGroup
    {
        public string GroupName { get; set; } = string.Empty;
        public List<HistoryEntry> Items { get; set; } = new();
    }

    // =========================================================================
    // TOOLS DRAWER / ZEN LIBRARY (<zen-library> вЂ” Screenshot 2 & ZenLibrary.mjs)
    // =========================================================================
    private bool _isZenLibraryCustomizing = false;
    private string _historyFilterWhen = "All";      // All, Today, Week, Month
    private string _historyFilterSort = "Date";     // Date, Site
    private string _historyFilterType = "History";  // History, Closed

    private void ToggleToolsDrawer_Click(object sender, RoutedEventArgs e)
    {
        _isToolsDrawerOpen = !_isToolsDrawerOpen;
        AnimateToolsDrawer(_isToolsDrawerOpen);
    }

    private void AnimateToolsDrawer(bool open)
    {
        if (ToolsDrawerBorder is null) return;

        double to = open
            ? (_activeDrawerTag switch
            {
                "Spaces" => Math.Clamp(106 + Math.Max(1, _spaces.Count) * 258 + 56, 418, Math.Max(418, ActualWidth * 0.78)),
                "Whiteboard" => Math.Max(720, ActualWidth * 0.75),
                _ => 418
            })
            : 0;

        ToolsDrawerBorder.BeginAnimation(WidthProperty, null);

        if (open)
        {
            // Populate ONLY the currently active section so opening is instantaneous
            SwitchDrawerView(_activeDrawerTag);

            if (Sidebar is not null) Sidebar.Visibility = Visibility.Collapsed;
            BeginAnimation(SidebarSlideProperty, null);
            if (SidebarColumn is not null) SidebarColumn.Width = new GridLength(0);

            // Set final layout width once (avoids 60fps Win32 SetWindowPos thrashing on WebView2 HWND)
            if (ToolsDrawerInnerGrid is not null) ToolsDrawerInnerGrid.Width = to;
            ToolsDrawerBorder.Width = to;
            ToolsDrawerBorder.Visibility = Visibility.Visible;

            if (ToolsDrawerInnerGrid is not null)
            {
                ToolsDrawerInnerGrid.BeginAnimation(OpacityProperty, null);
                ToolsDrawerInnerGrid.Opacity = 0.0;
                var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(145))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                ToolsDrawerInnerGrid.BeginAnimation(OpacityProperty, fadeAnim);
            }
            if (ToolsDrawerTranslate is not null)
            {
                ToolsDrawerTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                ToolsDrawerTranslate.X = -26.0;
                var slideAnim = new DoubleAnimation(-26.0, 0.0, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                ToolsDrawerTranslate.BeginAnimation(TranslateTransform.XProperty, slideAnim);
            }
        }
        else
        {
            if (_isZenLibraryCustomizing)
            {
                _isZenLibraryCustomizing = false;
                UpdateZenLibraryCustomizeOutlines();
            }

            ToolsDrawerBorder.Visibility = Visibility.Collapsed;
            ToolsDrawerBorder.Width = 0;

            if (Sidebar is not null)
            {
                Sidebar.Visibility = Visibility.Visible;
                Sidebar.Width = IsSidebarVisualExpanded ? 206 : 48;
            }
            BeginAnimation(SidebarSlideProperty, null);
            if (SidebarColumn is not null) SidebarColumn.Width = new GridLength(IsSidebarVisualExpanded ? 206 : 48);

            if (SidebarTranslate is not null)
            {
                SidebarTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                SidebarTranslate.X = -14.0;
                var sbSlide = new DoubleAnimation(-14.0, 0.0, TimeSpan.FromMilliseconds(135))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                SidebarTranslate.BeginAnimation(TranslateTransform.XProperty, sbSlide);
            }
        }
    }

    private void UpdateZenLibraryWidth(double targetWidth)
    {
        if (!_isToolsDrawerOpen || ToolsDrawerBorder is null) return;
        ToolsDrawerBorder.BeginAnimation(WidthProperty, null);
        if (ToolsDrawerInnerGrid is not null) ToolsDrawerInnerGrid.Width = targetWidth;
        ToolsDrawerBorder.Width = targetWidth;
    }

    private void ZenLibraryCustomize_Click(object sender, RoutedEventArgs e)
    {
        _isZenLibraryCustomizing = !_isZenLibraryCustomizing;
        UpdateZenLibraryCustomizeOutlines();
    }

    private void UpdateZenLibraryCustomizeOutlines()
    {
        var vis = _isZenLibraryCustomizing ? Visibility.Visible : Visibility.Collapsed;
        if (CustomizeOutlineMedia is not null) CustomizeOutlineMedia.Visibility = vis;
        if (CustomizeOutlineDownloads is not null) CustomizeOutlineDownloads.Visibility = vis;
        if (CustomizeOutlineBoosts is not null) CustomizeOutlineBoosts.Visibility = vis;
        if (CustomizeOutlineSpaces is not null) CustomizeOutlineSpaces.Visibility = vis;
        if (CustomizeOutlineHistory is not null) CustomizeOutlineHistory.Visibility = vis;
        if (ZenLibraryCustomizeBtn is not null)
            ZenLibraryCustomizeBtn.Background = _isZenLibraryCustomizing ? Brush("#55555A") : Brushes.Transparent;
    }

    private void DrawerNavTab_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement btn || btn.Tag is not string tag) return;
        e.Handled = true;
        var menu = NewMenu();
        switch (tag)
        {
            case "History":
                Sep(menu);
                Add(menu, "Очистить историю", () =>
                {
                    _state.History.Clear();
                    _state.RecentlyClosed.Clear();
                    Save();
                    PopulateDrawerHistory();
                    ShowToast("История очищена", "Вся история просмотров удалена");
                });
                break;

            case "Downloads":
                Add(menu, "Очистить загрузки", () =>
                {
                    _state.Downloads.Clear();
                    Save();
                    PopulateDrawerDownloads();
                    ShowToast("Загрузки очищены", "История загрузок очищена");
                });
                Add(menu, "Открыть папку загрузок", () =>
                {
                    try { Process.Start(new ProcessStartInfo(_state.DownloadPath) { UseShellExecute = true }); } catch { }
                });
                break;

            case "Media":
                if (_state.ZenLibraryMediaEnabled)
                {
                    Add(menu, "Скрыть раздел медиа", () =>
                    {
                        _state.ZenLibraryMediaEnabled = false;
                        Save();
                        RefreshZenMediaSection();
                    });
                }
                else
                {
                    Add(menu, "Показать раздел медиа", () =>
                    {
                        _state.ZenLibraryMediaEnabled = true;
                        Save();
                        RefreshZenMediaSection();
                    });
                }
                break;

            case "Spaces":
                Add(menu, "Создать воркспейс", () => OpenWorkspaceModal(null));
                break;

            default:
                Add(menu, _isZenLibraryCustomizing ? "Exit Customize Library" : "Customize Library", () =>
                {
                    _isZenLibraryCustomizing = !_isZenLibraryCustomizing;
                    UpdateZenLibraryCustomizeOutlines();
                });
                break;
        }
        OpenMenu(menu);
    }

    private void ClearHistoryRange(TimeSpan span)
    {
        var cutoff = DateTime.UtcNow - span;
        _state.History.RemoveAll(h => h.VisitedAt >= cutoff);
        Save();
        PopulateDrawerHistory();
        ShowToast("History cleared", "Selected time range cleared");
    }

    // ---- ZenLibraryHistorySection.mjs Filter Panel & History List ----
    private void HistoryFilterToggle_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryFilterPanel is null) return;
        bool open = HistoryFilterPanel.Visibility != Visibility.Visible;
        HistoryFilterPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (HistoryFilterToggleBtn is not null)
            HistoryFilterToggleBtn.Background = open ? Brush("#5E5E64") : Brush("#49494E");
    }

    private void HistoryFilterChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        var parts = tag.Split(':');
        if (parts.Length != 2) return;

        string group = parts[0].ToLowerInvariant();
        string val = parts[1].ToLowerInvariant();

        if (group == "when")
        {
            string normalized = val switch
            {
                "today" => "Today",
                "week" => "Week",
                "month" => "Month",
                _ => "All"
            };
            _historyFilterWhen = _historyFilterWhen == normalized ? "All" : normalized;
        }
        else if (group == "sort")
        {
            _historyFilterSort = val == "site" ? "Site" : "Date";
        }
        else if (group is "type" or "source")
        {
            _historyFilterType = val == "closed" ? "Closed" : "History";
            if (LibHistoryLabel is not null)
                LibHistoryLabel.Text = _historyFilterType == "Closed" ? "Archive" : "History";
            if (DrawerSearchPlaceholder is not null)
                DrawerSearchPlaceholder.Text = _historyFilterType == "Closed" ? "Search Closed TabsвЂ¦" : "Search HistoryвЂ¦";
        }

        UpdateHistoryFilterChipsVisuals();
        PopulateDrawerHistory();
    }

    private void UpdateHistoryFilterChipsVisuals()
    {
        static void StyleChip(Button? chip, bool active)
        {
            if (chip is null) return;
            chip.Background = active ? Brush("#63636A") : Brush("#4B4B50");
            chip.Foreground = active ? Brush("#FFFFFF") : Brush("#F2F2F5");
            chip.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }

        StyleChip(HistFilterWhenTodayBtn, _historyFilterWhen == "Today");
        StyleChip(HistFilterWhenWeekBtn, _historyFilterWhen == "Week");
        StyleChip(HistFilterWhenMonthBtn, _historyFilterWhen == "Month");

        StyleChip(HistFilterSortDateBtn, _historyFilterSort == "Date");
        StyleChip(HistFilterSortSiteBtn, _historyFilterSort == "Site");

        StyleChip(HistFilterSourceHistoryBtn, _historyFilterType == "History");
        StyleChip(HistFilterSourceClosedBtn, _historyFilterType == "Closed");
    }

    private void PopulateDrawerHistory()
    {
        if (DrawerHistoryGroupsControl is null) return;

        var query = DrawerHistorySearchBox?.Text?.Trim() ?? string.Empty;
        var sourceList = _historyFilterType == "Closed" ? _state.RecentlyClosed : _state.History;

        var todayLocal = DateTime.Today;
        var filtered = sourceList
            .Where(h =>
            {
                if (!string.IsNullOrEmpty(query) &&
                    !h.Title.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !h.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
                    return false;

                var localDate = h.VisitedAt.ToLocalTime().Date;
                return _historyFilterWhen switch
                {
                    "Today" => localDate == todayLocal,
                    "Week" => (todayLocal - localDate).TotalDays <= 7,
                    "Month" => (todayLocal - localDate).TotalDays <= 31,
                    _ => true
                };
            })
            .OrderByDescending(h => h.VisitedAt)
            .Take(string.IsNullOrEmpty(query) ? 65 : 120)
            .ToList();

        var groups = new List<DrawerHistoryGroup>();

        if (_historyFilterSort == "Site")
        {
            foreach (var siteGroup in filtered.GroupBy(h => string.IsNullOrWhiteSpace(h.Domain) ? "Other" : h.Domain).OrderBy(g => g.Key))
            {
                groups.Add(new DrawerHistoryGroup
                {
                    GroupName = siteGroup.Key,
                    Items = siteGroup.ToList()
                });
            }
        }
        else
        {
            var yesterdayLocal = todayLocal.AddDays(-1);
            foreach (var dateGroup in filtered.GroupBy(h => h.VisitedAt.ToLocalTime().Date).OrderByDescending(g => g.Key))
            {
                string label;
                if (dateGroup.Key == todayLocal)
                    label = "Today";
                else if (dateGroup.Key == yesterdayLocal)
                    label = "Yesterday";
                else
                {
                    int daysAgo = (int)(todayLocal - dateGroup.Key).TotalDays;
                    label = daysAgo switch
                    {
                        >= 2 and <= 30 => $"{daysAgo} days ago",
                        _ => dateGroup.Key.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture)
                    };
                }
                groups.Add(new DrawerHistoryGroup { GroupName = label, Items = dateGroup.ToList() });
            }
        }

        DrawerHistoryGroupsControl.ItemsSource = groups;
    }

    private void DrawerHistorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DrawerSearchPlaceholder is not null)
        {
            DrawerSearchPlaceholder.Visibility = string.IsNullOrEmpty(DrawerHistorySearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        PopulateDrawerHistory();
    }

    private async void DrawerHistoryItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject src && IsInsideChildButton(src, (DependencyObject)sender)) return;
        if (sender is FrameworkElement { DataContext: HistoryEntry entry } && !string.IsNullOrWhiteSpace(entry.Url))
        {
            if (CurrentTab is { IsHome: true })
            {
                NavigateInput(entry.Url);
            }
            else
            {
                await AddTabAsync(entry.Url);
            }
        }
    }

    private void DrawerHistoryItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HistoryEntry entry }) return;
        e.Handled = true;
        var menu = NewMenu();
        Add(menu, "Открыть ссылку", async () => await AddTabAsync(entry.Url));
        Add(menu, "Открыть в новой вкладке", async () => await AddTabAsync(entry.Url));
        Add(menu, "Скопировать ссылку", () => { try { System.Windows.Clipboard.SetText(entry.Url); ShowToast("Ссылка скопирована", entry.Url); } catch { } });
        Sep(menu);
        Add(menu, "Удалить из истории", () =>
        {
            _state.History.Remove(entry);
            _state.RecentlyClosed.Remove(entry);
            Save();
            PopulateDrawerHistory();
        });
        OpenMenu(menu);
    }

    private void DrawerHistoryForgetItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: HistoryEntry entry })
        {
            _state.History.Remove(entry);
            _state.RecentlyClosed.Remove(entry);
            Save();
            PopulateDrawerHistory();
        }
    }

    // ---- ZenLibraryDownloadsSection.mjs Downloads List ----
    private void DrawerDownloadsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DrawerDownloadsSearchPlaceholder is not null && DrawerDownloadsSearchBox is not null)
        {
            DrawerDownloadsSearchPlaceholder.Visibility = string.IsNullOrEmpty(DrawerDownloadsSearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        PopulateDrawerDownloads();
    }

    private void DrawerClearDownloads_Click(object sender, RoutedEventArgs e)
    {
        _state.Downloads.Clear();
        Save();
        PopulateDrawerDownloads();
        ShowToast("Downloads cleared", "All finished downloads removed from list");
    }

    private void PopulateDrawerDownloads()
    {
        if (DrawerDownloadsListHost is null) return;
        DrawerDownloadsListHost.Children.Clear();

        var query = DrawerDownloadsSearchBox?.Text?.Trim() ?? string.Empty;
        var downloads = _state.Downloads
            .Where(d => string.IsNullOrEmpty(query) ||
                        System.IO.Path.GetFileName(d.FilePath).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        d.SourceUrl.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.StartedAt)
            .Take(100)
            .ToList();

        if (DrawerDownloadsEmptyText is not null)
            DrawerDownloadsEmptyText.Visibility = downloads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var item in downloads)
        {
            var fileName = System.IO.Path.GetFileName(item.FilePath);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "Downloaded file";
            var domain = BrowserTab.DomainOf(item.SourceUrl);
            var sizeStr = item.TotalBytes > 0
                ? $"{item.TotalBytes / 1024.0 / 1024.0:0.1} MB"
                : (item.BytesReceived > 0 ? $"{item.BytesReceived / 1024.0:0} KB" : "");
            var statusStr = item.Status == "completed"
                ? (string.IsNullOrEmpty(sizeStr) ? domain : $"{sizeStr} вЂ” {domain}")
                : $"{item.Status} вЂ” {domain}";

            var rowBd = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 1, 0, 1),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            rowBd.MouseEnter += (_, _) => rowBd.Background = Brush("#14FFFFFF");
            rowBd.MouseLeave += (_, _) => rowBd.Background = Brushes.Transparent;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconBd = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(6),
                Background = Brush("#4B4B50"),
                HorizontalAlignment = WpfHorizontalAlignment.Left,
                VerticalAlignment = WpfVerticalAlignment.Center
            };
            if (TryFindResource("IconDownloads") is Geometry dlGeom)
            {
                iconBd.Child = new System.Windows.Shapes.Path
                {
                    Data = dlGeom,
                    Width = 14,
                    Height = 14,
                    Stroke = Brush("#F2F2F5"),
                    StrokeThickness = 1.6,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    HorizontalAlignment = WpfHorizontalAlignment.Center,
                    VerticalAlignment = WpfVerticalAlignment.Center
                };
            }
            Grid.SetColumn(iconBd, 0);
            grid.Children.Add(iconBd);

            var textStack = new StackPanel { VerticalAlignment = WpfVerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0) };
            textStack.Children.Add(new TextBlock
            {
                Text = fileName,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = Brush("#F2F2F5"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            textStack.Children.Add(new TextBlock
            {
                Text = statusStr,
                FontSize = 11,
                Foreground = Brush("#B8B8C0"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 0)
            });
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            var folderBtn = new Button
            {
                Width = 26,
                Height = 26,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Show in Folder"
            };
            if (TryFindResource("ZenIconFolder") is Geometry fGeom)
            {
                folderBtn.Content = new System.Windows.Shapes.Path
                {
                    Data = fGeom,
                    Width = 13,
                    Height = 13,
                    Fill = Brush("#D0D0D5"),
                    Stretch = System.Windows.Media.Stretch.Uniform
                };
            }
            folderBtn.Click += (_, ev) =>
            {
                ev.Handled = true;
                try
                {
                    if (System.IO.File.Exists(item.FilePath))
                        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FilePath}\"") { UseShellExecute = true });
                    else if (System.IO.Directory.Exists(_state.DownloadPath))
                        Process.Start(new ProcessStartInfo(_state.DownloadPath) { UseShellExecute = true });
                }
                catch { }
            };
            Grid.SetColumn(folderBtn, 2);
            grid.Children.Add(folderBtn);

            rowBd.Child = grid;
            rowBd.MouseLeftButtonUp += (_, ev) =>
            {
                if (ev.OriginalSource is DependencyObject src && IsInsideChildButton(src, rowBd)) return;
                try
                {
                    if (System.IO.File.Exists(item.FilePath))
                        Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
                }
                catch { }
            };

            DrawerDownloadsListHost.Children.Add(rowBd);
        }
    }

    // ---- ZenLibrarySpacesSection.mjs Horizontal Space Cards (.zen-library-space) ----
    private void PopulateDrawerSpaces()
    {
        if (DrawerSpacesListHost is null) return;
        DrawerSpacesListHost.Children.Clear();

        for (int i = 0; i < _spaces.Count; i++)
        {
            int idx = i;
            var space = _spaces[i];
            bool isActiveSpace = (i == _activeSpace);

            // .zen-library-space card: min-width 235px, max-width 245px, border-radius 16px, padding 8px 6px
            var card = new Border
            {
                Width = 240,
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(8, 10, 8, 10),
                Margin = new Thickness(0, 0, 14, 0),
                Background = isActiveSpace ? Brush("#4B4B50") : Brush("#444449"),
                BorderBrush = isActiveSpace ? Brush("#6E6E75") : Brush("#54545A"),
                BorderThickness = new Thickness(1)
            };

            var cardGrid = new Grid();
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // .zen-library-space-header
            var headerGrid = new Grid { Margin = new Thickness(4, 0, 4, 8) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconBtn = new Button
            {
                Width = 26,
                Height = 26,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Content = CreateWorkspaceIconElement(space.State.Icon, "#F2F2F5", 14),
                ToolTip = "Switch to this Space"
            };
            iconBtn.Click += (_, _) =>
            {
                SelectSpaceByIndex(idx);
                PopulateDrawerSpaces();
            };
            Grid.SetColumn(iconBtn, 0);
            headerGrid.Children.Add(iconBtn);

            var titleBox = new System.Windows.Controls.TextBox
            {
                Text = space.State.Name,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#F2F2F5"),
                CaretBrush = Brush("#F2F2F5"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalContentAlignment = WpfVerticalAlignment.Center,
                Margin = new Thickness(6, 0, 6, 0)
            };
            titleBox.LostFocus += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(titleBox.Text))
                {
                    space.State.Name = titleBox.Text.Trim();
                    Save();
                    RefreshZenSidebar();
                }
            };
            titleBox.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter)
                {
                    Keyboard.ClearFocus();
                    ke.Handled = true;
                }
            };
            Grid.SetColumn(titleBox, 1);
            headerGrid.Children.Add(titleBox);

            var themeBtn = new Button
            {
                Width = 26,
                Height = 26,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Edit Space Theme & Icon"
            };
            if (TryFindResource("ZenIconPaintbrushFill") is Geometry pbGeom)
            {
                themeBtn.Content = new System.Windows.Shapes.Path
                {
                    Data = pbGeom,
                    Width = 13,
                    Height = 13,
                    Fill = Brush("#C8C8CE"),
                    Stretch = System.Windows.Media.Stretch.Uniform
                };
            }
            themeBtn.Click += (_, _) => OpenWorkspaceModal(idx);
            Grid.SetColumn(themeBtn, 2);
            headerGrid.Children.Add(themeBtn);

            Grid.SetRow(headerGrid, 0);
            cardGrid.Children.Add(headerGrid);

            // .zen-library-space-tabs
            var tabsScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var tabsStack = new StackPanel();

            var spaceTabs = space.Tabs.Where(t => !t.IsHome && !t.IsEssential).ToList();
            if (spaceTabs.Count == 0 && space.State.SessionTabs.Count > 0)
            {
                foreach (var savedTab in space.State.SessionTabs.Where(st => !string.IsNullOrWhiteSpace(st.Url)))
                {
                    var row = CreateLibrarySpaceTabRow(
                        savedTab.Title,
                        savedTab.Url,
                        false,
                        async () =>
                        {
                            SelectSpaceByIndex(idx);
                            await LoadSpaceAsync(idx);
                            PopulateDrawerSpaces();
                        });
                    tabsStack.Children.Add(row);
                }
            }
            else
            {
                foreach (var tab in spaceTabs)
                {
                    bool isTabSelected = isActiveSpace && ReferenceEquals(CurrentTab, tab);
                    var row = CreateLibrarySpaceTabRow(
                        tab.DisplayTitle,
                        tab.ActiveUrl,
                        isTabSelected,
                        () =>
                        {
                            if (_activeSpace != idx) SelectSpaceByIndex(idx);
                            ActivateTabFromUser(tab);
                            PopulateDrawerSpaces();
                        });
                    tabsStack.Children.Add(row);
                }
            }

            // "+ New Tab" row inside Space card
            var addTabBtn = new Button
            {
                Height = 32,
                Margin = new Thickness(0, 4, 0, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = WpfHorizontalAlignment.Left
            };
            var addStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 0) };
            if (TryFindResource("IconPlus") is Geometry plusGeom)
            {
                addStack.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = plusGeom,
                    Width = 11,
                    Height = 11,
                    Stroke = Brush("#B8B8C0"),
                    StrokeThickness = 1.6,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    VerticalAlignment = WpfVerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                });
            }
            addStack.Children.Add(new TextBlock
            {
                Text = "New Tab",
                FontSize = 12,
                Foreground = Brush("#B8B8C0"),
                VerticalAlignment = WpfVerticalAlignment.Center
            });
            addTabBtn.Content = addStack;
            addTabBtn.Click += (_, _) =>
            {
                if (_activeSpace != idx) SelectSpaceByIndex(idx);
                NewTab_Click(this, new RoutedEventArgs());
            };
            tabsStack.Children.Add(addTabBtn);

            tabsScroll.Content = tabsStack;
            Grid.SetRow(tabsScroll, 1);
            cardGrid.Children.Add(tabsScroll);

            card.Child = cardGrid;
            DrawerSpacesListHost.Children.Add(card);
        }
    }

    private Border CreateLibrarySpaceTabRow(string title, string url, bool isSelected, Action onClick)
    {
        var bd = new Border
        {
            Height = 34,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 1, 0, 1),
            Background = isSelected ? Brush("#5E5E64") : Brushes.Transparent,
            Cursor = Cursors.Hand
        };
        bd.MouseEnter += (_, _) => { if (!isSelected) bd.Background = Brush("#14FFFFFF"); };
        bd.MouseLeave += (_, _) => { if (!isSelected) bd.Background = Brushes.Transparent; };

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var img = new Image { Width = 15, Height = 15, HorizontalAlignment = WpfHorizontalAlignment.Left, VerticalAlignment = WpfVerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        try { img.Source = new BitmapImage(new Uri(BrowserTab.FaviconOf(url))); } catch { }
        Grid.SetColumn(img, 0);
        g.Children.Add(img);

        var tb = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(title) ? BrowserTab.DomainOf(url) : title,
            FontSize = 12.5,
            Foreground = Brush("#F2F2F5"),
            VerticalAlignment = WpfVerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(tb, 1);
        g.Children.Add(tb);

        bd.Child = g;
        bd.MouseLeftButtonUp += (_, _) => onClick();
        return bd;
    }

    // ---- ZenLibraryMediaSection.mjs Opt-In & Media Grid ----
    private void ZenMediaEnable_Click(object sender, RoutedEventArgs e)
    {
        _state.ZenLibraryMediaEnabled = true;
        Save();
        RefreshZenMediaSection();
    }

    private void RefreshZenMediaSection()
    {
        bool enabled = _state.ZenLibraryMediaEnabled;
        if (ZenMediaOptInCard is not null)
            ZenMediaOptInCard.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        if (ZenMediaEnabledContent is not null)
            ZenMediaEnabledContent.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled)
            PopulateZenMediaGrid();
    }

    private void DrawerMediaSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DrawerMediaSearchPlaceholder is not null && DrawerMediaSearchBox is not null)
        {
            DrawerMediaSearchPlaceholder.Visibility = string.IsNullOrEmpty(DrawerMediaSearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        PopulateZenMediaGrid();
    }

    private void PopulateZenMediaGrid()
    {
        if (ZenMediaGridHost is null) return;
        ZenMediaGridHost.Children.Clear();

        var query = DrawerMediaSearchBox?.Text?.Trim() ?? string.Empty;
        var candidateDirs = new[]
        {
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            _state.DownloadPath
        };

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".gif" };
        var files = new List<System.IO.FileInfo>();

        foreach (var dir in candidateDirs.Where(d => !string.IsNullOrWhiteSpace(d) && System.IO.Directory.Exists(d)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var dirInfo = new System.IO.DirectoryInfo(dir);
                foreach (var f in dirInfo.EnumerateFiles())
                {
                    if (!extensions.Contains(f.Extension)) continue;
                    if (!string.IsNullOrEmpty(query) && !f.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                    files.Add(f);
                }
            }
            catch { }
        }

        foreach (var file in files.OrderByDescending(f => f.LastWriteTimeUtc).Take(18))
        {
            var card = new Border
            {
                Width = 86,
                Height = 86,
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 8, 8),
                Background = Brush("#49494E"),
                ClipToBounds = true,
                Cursor = Cursors.Hand,
                ToolTip = $"{file.Name}\nClick to open in new tab"
            };

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(file.FullName);
                bmp.DecodePixelWidth = 160;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();

                card.Child = new Image
                {
                    Source = bmp,
                    Stretch = System.Windows.Media.Stretch.UniformToFill,
                    HorizontalAlignment = WpfHorizontalAlignment.Center,
                    VerticalAlignment = WpfVerticalAlignment.Center
                };
            }
            catch { }

            string fullPath = file.FullName;
            card.MouseLeftButtonUp += async (_, _) =>
            {
                await AddTabAsync(new Uri(fullPath).AbsoluteUri);
            };
            ZenMediaGridHost.Children.Add(card);
        }
    }

    private void DrawerNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement btn && btn.Tag is string tag)
        {
            if (tag == "Whiteboard")
            {
                OpenWhiteboardTab();
                return;
            }
            SwitchDrawerView(tag);
        }
    }

    public void OpenWhiteboardTab(bool activate = true)
    {
        if (_spaces.Count == 0) return;

        // Р•СЃР»Рё РїР°РЅелСЊ-РїРѕР»СѓРѕРєРЅРѕ Р±С‹ла РѕС‚РєСЂС‹С‚Р°, заРєСЂС‹РІаеРј ее, С‡С‚РѕР±С‹ РѕСЃРІРѕР±РѕРґРёС‚СЊ РІРµСЃСЊ СЌРєСЂР°РЅ РїРѕРґ РІРєлаРґРєСѓ
        if (_isToolsDrawerOpen)
        {
            _isToolsDrawerOpen = false;
            AnimateToolsDrawer(false);
        }

        var existing = _spaces[_activeSpace].Tabs.FirstOrDefault(t => t.IsInternal && t.InternalPageKind == "luma://whiteboard");
        if (existing is not null)
        {
            if (activate) CurrentTab = existing;
            UpdateChrome();
            RenderPanes();
            FilterTabs();
            return;
        }

        var tab = new BrowserTab
        {
            FullUrl = "luma://whiteboard",
            Title = "Р”РѕСЃРєР°",
            IsInternal = true,
            InternalPageKind = "luma://whiteboard"
        };
        _spaces[_activeSpace].Tabs.Add(tab);
        RecordTesterAction("Открыта вкладка Интерактивная доска");

        if (activate) CurrentTab = tab;
        UpdateChrome();
        RenderPanes();
        FilterTabs();
    }

    private void TopWhiteboardBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenWhiteboardTab();
        WhiteboardPanel?.CreateNewBoardWithTemplate("Slanted", "Propysy");
    }

    private void FloatingWhiteboardBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenWhiteboardTab();
        WhiteboardPanel?.OpenCreateBoardModal();
    }

    private void FloatingScreenshotBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenWhiteboardTab();
        WhiteboardPanel?.OpenScreenshotModal();
    }

    // =========================================================================
    // MAIN MENU ACTIONS
    // =========================================================================
    private void WorkspacePill1_Click(object sender, RoutedEventArgs e) => SelectSpaceByIndex(0);

    private void WorkspacePill2_Click(object sender, RoutedEventArgs e)
    {
        if (_spaces.Count > 1) SelectSpaceByIndex(1);
        else OpenWorkspaceModal(null);
    }

    private void ClearFolderTabs_Click(object sender, RoutedEventArgs e) => ClearTabs_Click(sender, e);

    private void NewWindow_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientUi();
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch { }
    }

    private void NewPrivateWindow_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientUi();
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe)) Process.Start(new ProcessStartInfo(exe, "--incognito") { UseShellExecute = true });
        }
        catch { }
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientUi();
        try { CurrentTab?.ActiveView?.CoreWebView2?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); }
        catch { }
    }

    private void SavePage_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientUi();
        try { CurrentTab?.ActiveView?.CoreWebView2?.ExecuteScriptAsync("window.print()"); }
        catch { }
    }

    private void Find_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientUi();
        OpenSearch(SearchPurpose.TabSearch);
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTab?.ActiveView is not null)
        {
            CurrentTab.ActiveView.ZoomFactor = Math.Min(3.0, CurrentTab.ActiveView.ZoomFactor + 0.1);
            UpdateZoomText();
        }
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTab?.ActiveView is not null)
        {
            CurrentTab.ActiveView.ZoomFactor = Math.Max(0.3, CurrentTab.ActiveView.ZoomFactor - 0.1);
            UpdateZoomText();
        }
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTab?.ActiveView is not null)
        {
            CurrentTab.ActiveView.ZoomFactor = 1.0;
            UpdateZoomText();
        }
    }

    private void UpdateZoomText()
    {
        if (MenuZoomText is not null && CurrentTab?.ActiveView is not null)
        {
            MenuZoomText.Text = $"{(int)Math.Round(CurrentTab.ActiveView.ZoomFactor * 100)}%";
        }
    }

    private void MenuHistory_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientUi();
        AnimateToolsDrawer(true);
    }

    private void MenuDownloads_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientUi();
        AnimateToolsDrawer(true);
    }

    private void MenuBookmarks_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientUi();
        OpenSearch(SearchPurpose.TabSearch);
    }

    private void MenuPasswords_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientUi();
        Settings_Click(sender, e);
    }

    private void MenuExtensions_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientUi();
        Settings_Click(sender, e);
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientUi();
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    // =========================================================================
    // 1. ZEN SIDEBAR MASTER REFRESH (Essentials, Pinned Tabs, Workspaces, Theme)
    // =========================================================================
    public void RefreshZenSidebar()
    {
        if (!IsLoaded) return;
        RenderZenEssentials();
        RenderZenLocalPinnedTabs();
        UpdateWorkspaceIndicatorUi();
        RenderBottomWorkspaceStrip();
        UpdateSidebarContainerBadge();
    }

    private void UpdateSidebarContainerBadge()
    {
        if (SidebarContainerBadge is null || SidebarContainerDot is null || SidebarContainerText is null) return;
        var containerId = CurrentTab?.ContainerId;
        if (string.IsNullOrWhiteSpace(containerId) || containerId == "default")
        {
            if (_spaces.Count > _activeSpace && _activeSpace >= 0)
                containerId = _spaces[_activeSpace].State.ContainerId;
        }
        var info = ZenContainers.Get(containerId);
        if (info.Id == "default" || !IsSidebarVisualExpanded)
        {
            SidebarContainerBadge.Visibility = Visibility.Collapsed;
            return;
        }
        SidebarContainerBadge.Visibility = Visibility.Visible;
        SidebarContainerText.Text = info.Name;
        var color = ParseColorSafe(info.ColorHex, Color.FromRgb(59, 130, 246));
        SidebarContainerDot.Fill = new SolidColorBrush(color);
        SidebarContainerBadge.Background = new SolidColorBrush(Color.FromArgb(0x28, color.R, color.G, color.B));
    }

    // =========================================================================
    // 2. GLOBAL ESSENTIALS (#zen-essentials-container вЂ” Screenshot 1 & vertical-tabs.css)
    // =========================================================================
    private void RenderZenEssentials()
    {
        if (ZenEssentialsHost is null) return;
        ZenEssentialsHost.Children.Clear();

        _state.Essentials ??= new();
        if (_state.Essentials.Count == 0)
        {
            ZenEssentialsHost.Visibility = Visibility.Collapsed;
            return;
        }
        ZenEssentialsHost.Visibility = Visibility.Visible;

        bool visualExpanded = IsSidebarVisualExpanded;
        if (ZenEssentialsWrapper is not null)
        {
            ZenEssentialsWrapper.Width = visualExpanded ? 194 : 36;
            ZenEssentialsWrapper.HorizontalAlignment = WpfHorizontalAlignment.Left;
        }
        ZenEssentialsHost.Width = visualExpanded ? 194 : 36;
        ZenEssentialsHost.HorizontalAlignment = WpfHorizontalAlignment.Left;
        ZenEssentialsHost.Columns = visualExpanded ? 4 : 1;

        foreach (var essential in _state.Essentials)
        {
            var liveTab = FindLiveTabForEssential(essential);
            var isCurrent = (liveTab is not null && ReferenceEquals(CurrentTab, liveTab))
                || (CurrentTab is not null && !CurrentTab.IsHome && SameSite(CurrentTab.ActiveUrl, essential.Url));

            var isDiscord = string.Equals(essential.CustomIconKey, "IconDiscord", StringComparison.OrdinalIgnoreCase)
                || string.Equals(essential.Id, "ess_discord", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(essential.Url.TrimEnd('/'), "https://discord.com/app", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(essential.FaviconUrl));
            var isGoogle = string.Equals(essential.CustomIconKey, "IconGoogle", StringComparison.OrdinalIgnoreCase)
                || string.Equals(essential.Id, "ess_google", StringComparison.OrdinalIgnoreCase)
                || ((string.Equals(essential.Url.TrimEnd('/'), "https://www.google.com", StringComparison.OrdinalIgnoreCase) || string.Equals(essential.Url.TrimEnd('/'), "https://google.com", StringComparison.OrdinalIgnoreCase)) && string.IsNullOrEmpty(essential.FaviconUrl));

            var btn = new Button
            {
                Width = visualExpanded ? (_state.Essentials.Count == 1 ? 44 : double.NaN) : 36,
                Height = visualExpanded ? 44 : 36,
                HorizontalAlignment = visualExpanded ? (_state.Essentials.Count == 1 ? WpfHorizontalAlignment.Left : WpfHorizontalAlignment.Stretch) : WpfHorizontalAlignment.Center,
                Margin = visualExpanded ? new Thickness(2, 2, 2, 2) : new Thickness(0, 2, 0, 2),
                Cursor = Cursors.Hand,
                Tag = essential,
                ToolTip = $"{essential.Title}"
            };

            // Prominent squircle tile with rich 24px icon
            var bd = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(isCurrent ? 1.5 : 0),
                Background = isCurrent ? Brush("#56565A") : Brush("#4A4A4F"),
                BorderBrush = isCurrent ? Brush("#8A8D93") : Brushes.Transparent,
                Width = visualExpanded ? (_state.Essentials.Count == 1 ? 44 : double.NaN) : 36,
                Height = visualExpanded ? 44 : 36
            };

            btn.MouseEnter += (_, _) =>
            {
                if (!isCurrent) bd.Background = Brush("#525257");
            };
            btn.MouseLeave += (_, _) =>
            {
                if (!isCurrent) bd.Background = Brush("#4A4A4F");
            };

            var cellGrid = new Grid();

            if (isDiscord)
            {
                var circle = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(13),
                    Background = Brush("#5865F2"),
                    HorizontalAlignment = WpfHorizontalAlignment.Center,
                    VerticalAlignment = WpfVerticalAlignment.Center
                };
                if (TryFindResource("ZenIconDiscordInner") is Geometry discGeom)
                {
                    circle.Child = new System.Windows.Shapes.Path
                    {
                        Data = discGeom,
                        Width = 16,
                        Height = 12,
                        Fill = Brushes.White,
                        Stretch = System.Windows.Media.Stretch.Uniform,
                        HorizontalAlignment = WpfHorizontalAlignment.Center,
                        VerticalAlignment = WpfVerticalAlignment.Center
                    };
                }
                cellGrid.Children.Add(circle);
            }
            else if (isGoogle)
            {
                var circle = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(13),
                    Background = Brushes.White,
                    HorizontalAlignment = WpfHorizontalAlignment.Center,
                    VerticalAlignment = WpfVerticalAlignment.Center
                };
                circle.Child = CreateGoogleGLogoCanvas();
                cellGrid.Children.Add(circle);
            }
            else
            {
                var img = new Image
                {
                    Width = 24,
                    Height = 24,
                    HorizontalAlignment = WpfHorizontalAlignment.Center,
                    VerticalAlignment = WpfVerticalAlignment.Center
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                try
                {
                    var fav = !string.IsNullOrWhiteSpace(liveTab?.FaviconUrl) ? liveTab.FaviconUrl : (!string.IsNullOrWhiteSpace(essential.FaviconUrl) ? essential.FaviconUrl : BrowserTab.FaviconOf(essential.Url));
                    if (!string.IsNullOrWhiteSpace(fav))
                    {
                        if (fav.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                        {
                            var comma = fav.IndexOf(',');
                            if (comma > 0)
                            {
                                var bytes = Convert.FromBase64String(fav[(comma + 1)..]);
                                var bmp = new BitmapImage();
                                bmp.BeginInit();
                                bmp.StreamSource = new MemoryStream(bytes);
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.EndInit();
                                bmp.Freeze();
                                img.Source = bmp;
                            }
                        }
                        else
                        {
                            img.Source = new BitmapImage(new Uri(fav));
                        }
                    }
                }
                catch { }
                cellGrid.Children.Add(img);
            }

            // Audio indicator badge in top-right corner of Essential tile
            if (liveTab is { ShowAudioButton: true })
            {
                var audioBtn = new Button
                {
                    Width = 16,
                    Height = 16,
                    HorizontalAlignment = WpfHorizontalAlignment.Right,
                    VerticalAlignment = WpfVerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 2, 0),
                    Background = Brush("#CC202020"),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Tag = liveTab,
                ToolTip = liveTab.IsMuted ? "Включить звук" : "Выключить звук",

                };
                audioBtn.Click += TabAudioMute_Click;
                var audioIcon = new System.Windows.Shapes.Path
                {
                    Data = (Geometry)(TryFindResource(liveTab.IsMuted ? "ZenIconAudioMuted" : "ZenIconAudio") ?? TryFindResource("IconAudioPlaying") ?? Geometry.Empty),
                    Width = 9,
                    Height = 9,
                    Fill = liveTab.IsMuted ? Brush("#9E9E9E") : Brush("#F2F2F2"),
                    Stretch = System.Windows.Media.Stretch.Uniform
                };
                audioBtn.Content = audioIcon;
                cellGrid.Children.Add(audioBtn);
            }

            bd.Child = cellGrid;
            btn.Template = CreateSimpleBorderButtonTemplate();
            btn.Content = bd;

            btn.PreviewMouseLeftButtonDown += async (_, e) =>
            {
                if (e.OriginalSource is DependencyObject src && IsInsideChildButton(src, btn)) return;
                e.Handled = true;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && liveTab is not null)
                {
                    await OpenZenGlanceAsync(liveTab);
                    return;
                }
                await ActivateOrOpenEssentialAsync(essential);
            };

            btn.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                ShowEssentialContextMenu(essential, liveTab);
            };

            ZenEssentialsHost.Children.Add(btn);
        }
    }

    private static Viewbox CreateGoogleGLogoCanvas()
    {
        var canvas = new Canvas { Width = 48, Height = 48 };
        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(Color.FromRgb(0xEA, 0x43, 0x35)),
            Data = Geometry.Parse("M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z")
        });
        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(Color.FromRgb(0x42, 0x85, 0xF4)),
            Data = Geometry.Parse("M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z")
        });
        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(Color.FromRgb(0xFB, 0xBC, 0x05)),
            Data = Geometry.Parse("M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z")
        });
        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(Color.FromRgb(0x34, 0xA8, 0x53)),
            Data = Geometry.Parse("M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z")
        });
        return new Viewbox
        {
            Width = 13.5,
            Height = 13.5,
            Stretch = System.Windows.Media.Stretch.Uniform,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center,
            Child = canvas
        };
    }

    private BrowserTab? FindLiveTabForEssential(EssentialItemState essential)
    {
        var allTabs = _spaces.SelectMany(s => s.Tabs).ToList();
        if (!string.IsNullOrEmpty(essential.TabId))
        {
            var byId = allTabs.FirstOrDefault(t => t.Id == essential.TabId);
            if (byId is not null) return byId;
        }
        return allTabs.FirstOrDefault(t => t.IsEssential && SameSite(t.FullUrl, essential.Url))
            ?? allTabs.FirstOrDefault(t => SameSite(t.FullUrl, essential.Url));
    }

    private async Task ActivateOrOpenEssentialAsync(EssentialItemState essential)
    {
        var existing = FindLiveTabForEssential(essential);
        if (existing is not null)
        {
            essential.TabId = existing.Id;
            existing.IsEssential = true;
            ActivateTabFromUser(existing);
            RefreshZenSidebar();
            return;
        }

        var tab = await AddTabAsync(essential.Url, activate: true, pinned: true, essential: true);
        essential.TabId = tab.Id;
        FilterTabs();
        RefreshZenSidebar();
        Save();
    }

    private void ShowEssentialContextMenu(EssentialItemState essential, BrowserTab? liveTab)
    {
        var menu = NewMenu();
        Add(menu, $"Открыть {essential.Title}", async () => await ActivateOrOpenEssentialAsync(essential));
        if (liveTab is not null)
        {
            Add(menu, "Перезагрузить страницу", () => liveTab.ActiveView?.Reload());
            Add(menu, liveTab.IsMuted ? "Включить звук вкладки" : "Выключить звук вкладки", () => TabAudioMute_Click(liveTab, new RoutedEventArgs()));
        }
        Sep(menu);
        Add(menu, "Скопировать ссылку", () => { try { System.Windows.Clipboard.SetText(essential.Url); ShowToast("Ссылка скопирована", essential.Url); } catch { } });
        Add(menu, "Убрать из избранных", () => RemoveFromEssentials(essential, liveTab));
        if (liveTab is not null)
        {
            Add(menu, "Закрыть вкладку", () =>
            {
                essential.TabId = null;
                CloseTab(liveTab);
                RefreshZenSidebar();
            });
        }
        OpenMenu(menu);
    }

    public void PinToEssentials(BrowserTab tab)
    {
        if (tab.IsHome) return;
        _state.Essentials ??= new();
        var existing = _state.Essentials.FirstOrDefault(e => SameSite(e.Url, tab.ActiveUrl));
        if (existing is null)
        {
            existing = new EssentialItemState
            {
                Title = tab.DisplayTitle,
                Url = tab.ActiveUrl,
                AccentColor = "#7C5CE4",
                TabId = tab.Id,
                FaviconUrl = tab.FaviconUrl
            };
            _state.Essentials.Add(existing);
        }
        else
        {
            existing.TabId = tab.Id;
        }
        tab.IsEssential = true;
        tab.IsPinned = true;
        tab.FolderId = null;
        Save();
        FilterTabs();
        RefreshZenSidebar();
        ShowToast("Global Essentials", $"В«{tab.DisplayTitle}В» заРєСЂРµРїлеРЅР° РґР»СЏ РІСЃРµС… РІРѕСЂРєСЃРїРµР№СЃРѕРІ");
    }

    private void RemoveFromEssentials(EssentialItemState essential, BrowserTab? liveTab)
    {
        _state.Essentials?.Remove(essential);
        if (liveTab is not null)
        {
            liveTab.IsEssential = false;
            liveTab.IsPinned = false;
        }
        Save();
        FilterTabs();
        RefreshZenSidebar();
        ShowToast("Global Essentials", $"В«{essential.Title}В» РѕС‚РєСЂРµРїлеРЅР°");
    }

    // =========================================================================
    // 3. LOCAL WORKSPACE PINNED TABS (#vertical-pinned-tabs-container)
    // =========================================================================
    private void RenderZenLocalPinnedTabs()
    {
        if (ZenPinnedTabsHost is null || ZenPinnedWrapper is null || _spaces.Count == 0) return;
        ZenPinnedTabsHost.Children.Clear();

        var space = _spaces[_activeSpace];
        var localSites = space.State.LooseItems
            .Where(site => !(_state.Essentials?.Any(e => SameSite(e.Url, site.Url)) ?? false))
            .ToList();

        if (localSites.Count == 0)
        {
            ZenPinnedWrapper.Visibility = Visibility.Collapsed;
            return;
        }

        ZenPinnedWrapper.Visibility = Visibility.Visible;

        foreach (var site in localSites)
        {
            var liveTab = ResolveSiteTab(site, null);
            var isSelected = liveTab is not null && ReferenceEquals(CurrentTab, liveTab);
            bool visualExpanded = IsSidebarVisualExpanded;

            var rowBd = new Border
            {
                Width = visualExpanded ? double.NaN : 36,
                Height = visualExpanded ? 32 : 36,
                Margin = visualExpanded ? new Thickness(0, 1, 0, 1) : new Thickness(0, 2, 0, 2),
                HorizontalAlignment = visualExpanded ? WpfHorizontalAlignment.Stretch : WpfHorizontalAlignment.Center,
                CornerRadius = new CornerRadius(visualExpanded ? 7 : 10),
                BorderThickness = new Thickness(!visualExpanded && isSelected ? 1.5 : 0),
                BorderBrush = !visualExpanded && isSelected ? Brush("#8A8D93") : Brushes.Transparent,
                Background = isSelected ? (visualExpanded ? (Brush)FindResource("SidebarItemActiveBrush") : Brush("#56565A")) : Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = site,
                ToolTip = $"ЗаРєСЂРµРїлеРЅРѕ РІ РІРѕСЂРєСЃРїРµР№СЃРµ В«{space.State.Name}В»: {site.Title}"
            };

            rowBd.MouseEnter += (_, _) =>
            {
                if (!isSelected) rowBd.Background = visualExpanded ? (Brush)FindResource("SidebarItemHoverBrush") : Brush("#525257");
            };
            rowBd.MouseLeave += (_, _) =>
            {
                if (!isSelected) rowBd.Background = Brushes.Transparent;
            };

            // Favicon
            var favBorder = new Border
            {
                Width = 17,
                Height = 17,
                CornerRadius = new CornerRadius(4),
                Background = Brush("#262629"),
                ClipToBounds = true,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = WpfVerticalAlignment.Center
            };
            var favImg = new Image { Width = 13, Height = 13 };
            RenderOptions.SetBitmapScalingMode(favImg, BitmapScalingMode.HighQuality);
            try
            {
                var favUrl = liveTab?.FaviconUrl ?? BrowserTab.FaviconOf(site.Url);
                favImg.Source = new BitmapImage(new Uri(favUrl));
            }
            catch { }
            favBorder.Child = favImg;

            if (!visualExpanded)
            {
                // In collapsed (48px) mode, pinned tab is a clean centered 36x32 tile with centered favicon
                rowBd.Child = favBorder;
            }
            else
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 6, 0) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Left active bar
                if (isSelected)
                {
                    var activeBar = new Border
                    {
                        Width = 3,
                        Height = 14,
                        CornerRadius = new CornerRadius(1.5),
                        HorizontalAlignment = WpfHorizontalAlignment.Left,
                        Background = (Brush)FindResource("AccentIndicatorBrush")
                    };
                    Grid.SetColumn(activeBar, 0);
                    grid.Children.Add(activeBar);
                }

                Grid.SetColumn(favBorder, 1);
                grid.Children.Add(favBorder);

                // Title
                var titleBlock = new TextBlock
                {
                    Text = liveTab?.DisplayTitle ?? site.Title,
                    FontSize = 12,
                    FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = isSelected ? Brushes.White : (Brush)FindResource("TextSecondaryBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = WpfVerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 4, 0)
                };
                Grid.SetColumn(titleBlock, 2);
                grid.Children.Add(titleBlock);

                // Audio Mute button if playing/muted
                if (liveTab is { ShowAudioButton: true })
                {
                    var audioBtn = new Button
                    {
                        Width = 20,
                        Height = 20,
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Cursor = Cursors.Hand,
                        Tag = liveTab,
                        Margin = new Thickness(0, 0, 2, 0),
                ToolTip = liveTab.IsMuted ? "Включить звук" : "Выключить звук",

                    };
                    audioBtn.Click += TabAudioMute_Click;
                    audioBtn.Content = new System.Windows.Shapes.Path
                    {
                        Data = (Geometry)(TryFindResource(liveTab.IsMuted ? "ZenIconAudioMuted" : "ZenIconAudio") ?? TryFindResource("IconAudioPlaying") ?? Geometry.Empty),
                        Width = 11,
                        Height = 11,
                        Fill = liveTab.IsMuted ? Brush("#EF4444") : Brush("#A59BF0"),
                        Stretch = System.Windows.Media.Stretch.Uniform
                    };
                    Grid.SetColumn(audioBtn, 3);
                    grid.Children.Add(audioBtn);
                }

                // Pin icon badge
                var pinIcon = new System.Windows.Shapes.Path
                {
                    Data = (Geometry)FindResource("ZenIconPin"),
                    Width = 10,
                    Height = 10,
                    Fill = Brush("#7A7A85"),
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    VerticalAlignment = WpfVerticalAlignment.Center,
                    Opacity = 0.75
                };
                Grid.SetColumn(pinIcon, 4);
                grid.Children.Add(pinIcon);

                rowBd.Child = grid;
            }

            rowBd.PreviewMouseLeftButtonDown += async (_, e) =>
            {
                if (e.OriginalSource is DependencyObject src && IsInsideChildButton(src, rowBd)) return;
                e.Handled = true;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && liveTab is not null)
                {
                    await OpenZenGlanceAsync(liveTab);
                    return;
                }
                var req = ++_tabActivationSerial;
                if (liveTab is not null) ActivateTabFromUser(liveTab, req);
                else await OpenSavedSiteAsync(site, null, req);
                RefreshZenSidebar();
            };

            rowBd.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                var menu = NewMenu();
                if (liveTab is not null)
                {
                    Add(menu, "Перезагрузить", () => liveTab.ActiveView?.Reload());
                    Add(menu, liveTab.IsMuted ? "Включить звук вкладки" : "Выключить звук вкладки", () => TabAudioMute_Click(liveTab, new RoutedEventArgs()));
                    Add(menu, "Закрыть вкладку", () => { CloseTab(liveTab); FilterTabs(); });
                    Sep(menu);
                }
                Add(menu, "Скопировать ссылку", () => { try { System.Windows.Clipboard.SetText(site.Url); ShowToast("Ссылка скопирована", site.Url); } catch { } });
                Sep(menu);
                Add(menu, "Открепить вкладку", () =>
                {
                    UnpinSavedSite(site);
                    RefreshZenSidebar();
                });
                Add(menu, "Удалить сайт", () =>
                {
                    DeleteSavedSite(site, null, true);
                    RefreshZenSidebar();
                });
                OpenMenu(menu);
            };

            ZenPinnedTabsHost.Children.Add(rowBd);
        }
    }

    private static bool IsInsideChildButton(DependencyObject source, DependencyObject container)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, container); node = VisualTreeHelper.GetParent(node))
        {
            if (node is Button) return true;
        }
        return false;
    }

    // =========================================================================
    // 4. WORKSPACES: INDICATOR, THEME GRADIENT, SWITCHER & LONG-PRESS D&D GRID
    // =========================================================================
    private UIElement CreateWorkspaceIconElement(string? icon, string colorHex, double size = 13)
    {
        if (string.IsNullOrWhiteSpace(icon) || icon.Equals("dot", StringComparison.OrdinalIgnoreCase) || icon.Equals("no-icon", StringComparison.OrdinalIgnoreCase))
        {
            return new Ellipse
            {
                Width = 6.5,
                Height = 6.5,
                Fill = Brush(colorHex),
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = WpfVerticalAlignment.Center
            };
        }

        if (icon.Equals("briefcase", StringComparison.OrdinalIgnoreCase) && TryFindResource("ZenIconBriefcase") is Geometry bcGeom)
        {
            return new System.Windows.Shapes.Path
            {
                Data = bcGeom,
                Width = size,
                Height = size,
                Fill = Brush(colorHex),
                Stretch = System.Windows.Media.Stretch.Uniform,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = WpfVerticalAlignment.Center
            };
        }

        if (icon.Equals("folder", StringComparison.OrdinalIgnoreCase) && TryFindResource("ZenIconFolder") is Geometry fldGeom)
        {
            return new System.Windows.Shapes.Path
            {
                Data = fldGeom,
                Width = size,
                Height = size,
                Fill = Brush(colorHex),
                Stretch = System.Windows.Media.Stretch.Uniform,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = WpfVerticalAlignment.Center
            };
        }

        return new TextBlock
        {
            Text = icon,
            FontSize = size - 1,
            Foreground = Brush(colorHex),
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center
        };
    }

    public void UpdateWorkspaceIndicatorUi()
    {
        if (_spaces.Count == 0 || _activeSpace < 0 || _activeSpace >= _spaces.Count) return;
        var activeState = _spaces[_activeSpace].State;

        if (ZenWorkspaceNameBlock is not null)
            ZenWorkspaceNameBlock.Text = activeState.Name;

        bool isDot = string.IsNullOrWhiteSpace(activeState.Icon)
            || activeState.Icon.Equals("dot", StringComparison.OrdinalIgnoreCase)
            || activeState.Icon.Equals("no-icon", StringComparison.OrdinalIgnoreCase);
        bool isBriefcase = activeState.Icon.Equals("briefcase", StringComparison.OrdinalIgnoreCase);
        bool isFolder = activeState.Icon.Equals("folder", StringComparison.OrdinalIgnoreCase);
        bool isSvgIcon = isBriefcase || isFolder;

        if (ZenWorkspaceDotIcon is not null)
        {
            ZenWorkspaceDotIcon.Visibility = isDot ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ZenWorkspaceIconPath is not null)
        {
            ZenWorkspaceIconPath.Visibility = isSvgIcon ? Visibility.Visible : Visibility.Collapsed;
            if (isSvgIcon && TryFindResource(isFolder ? "ZenIconFolder" : "ZenIconBriefcase") is Geometry svgGeom)
            {
                ZenWorkspaceIconPath.Data = svgGeom;
            }
        }
        if (ZenWorkspaceEmojiBlock is not null)
        {
            ZenWorkspaceEmojiBlock.Visibility = (!isDot && !isSvgIcon) ? Visibility.Visible : Visibility.Collapsed;
            if (!isDot && !isSvgIcon) ZenWorkspaceEmojiBlock.Text = activeState.Icon;
        }

        // Only tint sidebar if user explicitly selected a custom gradient color (not default flat #3D3D42 / #202020)
        bool hasCustomTint = !string.IsNullOrWhiteSpace(activeState.Accent)
            && !activeState.Accent.Equals("#3D3D42", StringComparison.OrdinalIgnoreCase)
            && !activeState.Accent.Equals("#202020", StringComparison.OrdinalIgnoreCase)
            && !activeState.Accent.Equals("#121214", StringComparison.OrdinalIgnoreCase);

        if (ZenSidebarGradientOverlay is not null)
        {
            ZenSidebarGradientOverlay.Opacity = hasCustomTint ? 0.14 : 0.0;
        }

        if (hasCustomTint && ZenGradientStop0 is not null && ZenGradientStop1 is not null)
        {
            ZenGradientStop0.Color = ParseColorSafe(activeState.Accent, Color.FromRgb(0x3D, 0x3D, 0x42));
            ZenGradientStop1.Color = ParseColorSafe(activeState.GradientEnd, Color.FromRgb(0x3D, 0x3D, 0x42));
        }
    }

    private void RenderBottomWorkspaceStrip()
    {
        if (ZenWorkspaceStripHost is null) return;
        ZenWorkspaceStripHost.Children.Clear();

        if (!IsSidebarVisualExpanded)
        {
            if (ZenWorkspaceStripScroll is not null) ZenWorkspaceStripScroll.Visibility = Visibility.Collapsed;
            return;
        }
        if (ZenWorkspaceStripScroll is not null) ZenWorkspaceStripScroll.Visibility = Visibility.Visible;

        for (int i = 0; i < _spaces.Count; i++)
        {
            int spaceIdx = i;
            var space = _spaces[i].State;
            bool isActive = (i == _activeSpace);

            var pillBtn = new Button
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(1, 0, 1, 0),
                Cursor = Cursors.Hand,
                AllowDrop = true,
                Tag = spaceIdx,
                ToolTip = $"{space.Name} (Ctrl+{i + 1})\nClick: switch В· Right-click: options В· Long-press: Drag & Drop Grid"
            };

            var bd = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(6),
                Background = isActive ? Brush("#55555A") : Brush("#01FFFFFF"),
                BorderThickness = new Thickness(0)
            };

            string iconColor = isActive ? "#F2F2F5" : "#8A8D93";
            bd.Child = CreateWorkspaceIconElement(space.Icon, iconColor, 13);

            pillBtn.MouseEnter += (_, _) => bd.Background = isActive ? Brush("#5E5E64") : Brush("#4C4C52");
            pillBtn.MouseLeave += (_, _) =>
            {
                bd.Background = isActive ? Brush("#55555A") : Brush("#01FFFFFF");
                _workspaceLongPressTimer?.Stop();
            };

            pillBtn.Template = CreateSimpleBorderButtonTemplate();
            pillBtn.Content = bd;

            // Long-press (>350ms) opens Drag & Drop Workspace Grid, short click switches workspace
            pillBtn.PreviewMouseLeftButtonDown += (_, _) =>
            {
                _workspaceLongPressFired = false;
                _workspaceLongPressTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(360) };
                _workspaceLongPressTimer.Stop();
                _workspaceLongPressTimer.Tick -= OnWorkspaceLongPressTimerTick;
                _workspaceLongPressTimer.Tick += OnWorkspaceLongPressTimerTick;
                _workspaceLongPressTimer.Start();
            };

            pillBtn.PreviewMouseLeftButtonUp += (_, e) =>
            {
                _workspaceLongPressTimer?.Stop();
                if (_workspaceLongPressFired)
                {
                    _workspaceLongPressFired = false;
                    e.Handled = true;
                    return;
                }
                e.Handled = true;
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => SelectSpaceByIndex(spaceIdx)));
            };

            pillBtn.MouseRightButtonUp += (_, e) =>
            {
                _workspaceLongPressTimer?.Stop();
                e.Handled = true;
                ShowWorkspaceContextMenu(spaceIdx);
            };

            // Allow dropping a tab directly onto a workspace icon in the footer strip
            pillBtn.DragOver += (_, e) =>
            {
                if (e.Data.GetDataPresent("LumaTab"))
                {
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                }
            };
            pillBtn.Drop += async (_, e) =>
            {
                if (e.Data.GetData("LumaTab") is BrowserTab droppedTab && spaceIdx != _activeSpace)
                {
                    await MoveTabToWorkspaceAsync(droppedTab, spaceIdx);
                    e.Handled = true;
                }
            };

            ZenWorkspaceStripHost.Children.Add(pillBtn);
        }
    }

    private async Task MoveTabToWorkspaceAsync(BrowserTab tab, int targetSpaceIndex)
    {
        if (targetSpaceIndex < 0 || targetSpaceIndex >= _spaces.Count || targetSpaceIndex == _activeSpace) return;
        await LoadSpaceAsync(targetSpaceIndex);
        _spaces[_activeSpace].Tabs.Remove(tab);
        tab.FolderId = null;
        tab.IsPinned = false;
        _spaces[targetSpaceIndex].Tabs.Add(tab);
        if (ReferenceEquals(CurrentTab, tab))
        {
            CurrentTab = Tabs.FirstOrDefault() ?? EnsureHomeTab(_spaces[_activeSpace]);
        }
        Save();
        FilterTabs();
        RefreshZenSidebar();
        ShowToast("Р’РєлаРґРєР° РїРµСЂРµРЅРµСЃРµРЅР°", $"РџРµСЂРµРјРµС‰РµРЅРѕ РІ РІРѕСЂРєСЃРїРµР№СЃ В«{_spaces[targetSpaceIndex].State.Name}В»");
    }

    // ---- Bottom-Left Workspace Switcher: Click vs Long-Press (>350ms) ----
    private void ZenBottomWorkspaceBtn_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _workspaceLongPressFired = false;
        _workspaceLongPressTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(360) };
        _workspaceLongPressTimer.Stop();
        _workspaceLongPressTimer.Tick -= OnWorkspaceLongPressTimerTick;
        _workspaceLongPressTimer.Tick += OnWorkspaceLongPressTimerTick;
        _workspaceLongPressTimer.Start();
    }

    private void OnWorkspaceLongPressTimerTick(object? sender, EventArgs e)
    {
        _workspaceLongPressTimer?.Stop();
        _workspaceLongPressFired = true;
        OpenWorkspaceGridView();
    }

    private void ZenBottomWorkspaceBtn_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _workspaceLongPressTimer?.Stop();
        if (_workspaceLongPressFired)
        {
            _workspaceLongPressFired = false;
            e.Handled = true;
            return;
        }
        // Normal short click -> Open Workspace Switcher Menu
        ToggleWorkspaceMenuPopup();
        e.Handled = true;
    }

    private void ZenBottomWorkspaceBtn_MouseLeave(object sender, MouseEventArgs e)
    {
        _workspaceLongPressTimer?.Stop();
    }

    private void ZenBottomWorkspaceBtn_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _workspaceLongPressTimer?.Stop();
        e.Handled = true;
        OpenWorkspaceGridView();
    }

    private void ZenWorkspaceHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject src && IsInsideChildButton(src, ZenWorkspaceIndicatorRow)) return;
        ToggleWorkspaceMenuPopup();
    }

    private void ToggleWorkspaceMenuPopup()
    {
        if (ZenWorkspaceMenuPopup is null) return;
        if (ZenWorkspaceMenuPopup.IsOpen)
        {
            ZenWorkspaceMenuPopup.IsOpen = false;
            return;
        }
        PopulateWorkspaceMenuPopup();
        ZenWorkspaceMenuPopup.IsOpen = true;
    }

    private void PopulateWorkspaceMenuPopup()
    {
        if (ZenWorkspaceMenuListHost is null) return;
        ZenWorkspaceMenuListHost.Children.Clear();

        for (int i = 0; i < _spaces.Count; i++)
        {
            int idx = i;
            var space = _spaces[i];
            bool isActive = (i == _activeSpace);
            var container = ZenContainers.Get(space.State.ContainerId);
            var accent = ParseColorSafe(space.State.Accent, Color.FromRgb(0x7C, 0x5C, 0xE4));

            var rowGrid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var selectBtn = new Button
            {
                Style = (Style)FindResource("ModernMenuRowButtonStyle"),
                Background = isActive ? new SolidColorBrush(Color.FromArgb(0x2E, accent.R, accent.G, accent.B)) : Brushes.Transparent
            };

            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconEl = CreateWorkspaceIconElement(space.State.Icon, "#F2F2F5", 13);
            Grid.SetColumn(iconEl, 0);
            inner.Children.Add(iconEl);

            var nameStack = new StackPanel { VerticalAlignment = WpfVerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
            nameStack.Children.Add(new TextBlock
            {
                Text = space.State.Name,
                FontSize = 12,
                FontWeight = isActive ? FontWeights.Bold : FontWeights.Medium,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (container.Id != "default")
            {
                nameStack.Children.Add(new TextBlock
                {
                    Text = $"Cookie Jar: {container.Name}",
                    FontSize = 9.5,
                    Foreground = Brush(container.ColorHex)
                });
            }
            Grid.SetColumn(nameStack, 1);
            inner.Children.Add(nameStack);

            var shortcutBlock = new TextBlock
            {
                Text = $"Ctrl+{i + 1}",
                FontSize = 10,
                Foreground = Brush("#8E8E93"),
                VerticalAlignment = WpfVerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            Grid.SetColumn(shortcutBlock, 2);
            inner.Children.Add(shortcutBlock);

            selectBtn.Content = inner;
            selectBtn.Click += (_, _) =>
            {
                ZenWorkspaceMenuPopup.IsOpen = false;
                SelectSpaceByIndex(idx);
            };
            Grid.SetColumn(selectBtn, 0);
            rowGrid.Children.Add(selectBtn);

            var editBtn = new Button
            {
                Style = (Style)FindResource("UtilityIconButtonStyle"),
                Width = 26,
                Height = 26,
                Margin = new Thickness(2, 0, 0, 0),
                ToolTip = "РќР°СЃС‚СЂРѕРёС‚СЊ РІРѕСЂРєСЃРїРµР№СЃ (РёРјСЏ, РёРєРѕРЅРєР°, РіСЂР°РґРёРµРЅС‚, РєРѕРЅС‚РµР№РЅРµСЂ)"
            };
            editBtn.Content = new TextBlock { Text = "вњЋ", FontSize = 12, Foreground = Brush("#8E8E93") };
            editBtn.Click += (_, _) =>
            {
                ZenWorkspaceMenuPopup.IsOpen = false;
                OpenWorkspaceModal(idx);
            };
            Grid.SetColumn(editBtn, 1);
            rowGrid.Children.Add(editBtn);

            ZenWorkspaceMenuListHost.Children.Add(rowGrid);
        }
    }

    // ---- Long-Press Workspace Grid View with Drag-and-Drop Reordering ----
    private void OpenWorkspaceGridView_Click(object sender, RoutedEventArgs e)
    {
        if (ZenWorkspaceMenuPopup is not null) ZenWorkspaceMenuPopup.IsOpen = false;
        OpenWorkspaceGridView();
    }

    private void CloseWorkspaceGridPopup_Click(object sender, RoutedEventArgs e)
    {
        if (ZenWorkspaceGridPopup is not null) ZenWorkspaceGridPopup.IsOpen = false;
    }

    public void OpenWorkspaceGridView()
    {
        if (ZenWorkspaceGridPopup is null) return;
        PopulateWorkspaceGridPopup();
        ZenWorkspaceGridPopup.IsOpen = true;
    }

    private void PopulateWorkspaceGridPopup()
    {
        if (ZenWorkspaceGridWrapHost is null) return;
        ZenWorkspaceGridWrapHost.Children.Clear();

        for (int i = 0; i < _spaces.Count; i++)
        {
            int idx = i;
            var space = _spaces[i];
            bool isActive = (i == _activeSpace);
            var accent = ParseColorSafe(space.State.Accent, Color.FromRgb(0x7C, 0x5C, 0xE4));
            var gradEnd = ParseColorSafe(space.State.GradientEnd, Color.FromRgb(0x1E, 0x1E, 0x24));

            var card = new Border
            {
                Width = 128,
                Height = 76,
                Margin = new Thickness(4),
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(10),
                Cursor = Cursors.SizeAll,
                AllowDrop = true,
                Tag = idx,
                BorderThickness = new Thickness(isActive ? 2 : 1),
                BorderBrush = isActive ? new SolidColorBrush(accent) : Brush("#34343E"),
                Background = new LinearGradientBrush(
                    Color.FromArgb(0x55, accent.R, accent.G, accent.B),
                    Color.FromArgb(0x99, gradEnd.R, gradEnd.G, gradEnd.B),
                    new Point(0, 0), new Point(1, 1)),
                ToolTip = "РџРµСЂРµС‚Р°С‰РёС‚Рµ РєР°СЂС‚РѕС‡РєСѓ РґР»СЏ РёР·РјРµРЅРµРЅРёСЏ РїРѕСЂСЏРґРєР° РІРѕСЂРєСЃРїРµР№СЃРѕРІ РёР»Рё РєР»РёРєРЅРёС‚Рµ РґР»СЏ РїРµСЂРµРєР»СЋС‡РµРЅРёСЏ"
            };

            var stack = new StackPanel();
            var topRow = new Grid();
            var gridIconEl = CreateWorkspaceIconElement(space.State.Icon, "#F2F2F5", 16);
            if (gridIconEl is FrameworkElement gridIconFe)
            {
                gridIconFe.HorizontalAlignment = WpfHorizontalAlignment.Left;
            }
            topRow.Children.Add(gridIconEl);
            topRow.Children.Add(new TextBlock
            {
                Text = $"#{i + 1}",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("#A0A0AB"),
                HorizontalAlignment = WpfHorizontalAlignment.Right
            });
            stack.Children.Add(topRow);

            stack.Children.Add(new TextBlock
            {
                Text = space.State.Name,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 6, 0, 2)
            });

            var tabCount = space.Loaded ? space.Tabs.Count : space.State.SessionTabs.Count;
            stack.Children.Add(new TextBlock
            {
                Text = $"{tabCount} РІРєР». В· {ZenContainers.Get(space.State.ContainerId).Name}",
                FontSize = 9.5,
                Foreground = Brush("#B0B0BC"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            card.Child = stack;

            Point cardDragStart = default;
            card.PreviewMouseLeftButtonDown += (_, e) =>
            {
                cardDragStart = e.GetPosition(card);
            };
            card.MouseMove += (_, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed &&
                    (Math.Abs(e.GetPosition(card).X - cardDragStart.X) > 5 ||
                     Math.Abs(e.GetPosition(card).Y - cardDragStart.Y) > 5))
                {
                    DragDrop.DoDragDrop(card, new DataObject("ZenWorkspaceIndex", idx), DragDropEffects.Move);
                }
            };
            card.MouseLeftButtonUp += (_, _) =>
            {
                ZenWorkspaceGridPopup.IsOpen = false;
                SelectSpaceByIndex(idx);
            };
            card.DragOver += (_, e) =>
            {
                if (e.Data.GetDataPresent("ZenWorkspaceIndex"))
                {
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                }
            };
            card.Drop += (_, e) =>
            {
                if (e.Data.GetData("ZenWorkspaceIndex") is int fromIdx && fromIdx != idx)
                {
                    ReorderWorkspaces(fromIdx, idx);
                    PopulateWorkspaceGridPopup();
                    e.Handled = true;
                }
            };

            ZenWorkspaceGridWrapHost.Children.Add(card);
        }
    }

    private void ReorderWorkspaces(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _spaces.Count || toIndex < 0 || toIndex >= _spaces.Count || fromIndex == toIndex)
            return;

        var activeRuntime = _spaces[_activeSpace];
        var movedRuntime = _spaces[fromIndex];
        var movedState = _state.Spaces[fromIndex];

        _spaces.RemoveAt(fromIndex);
        _state.Spaces.RemoveAt(fromIndex);

        _spaces.Insert(toIndex, movedRuntime);
        _state.Spaces.Insert(toIndex, movedState);

        _activeSpace = _spaces.IndexOf(activeRuntime);
        _state.ActiveSpace = _activeSpace;

        Save();
        RefreshSpaces();
        RefreshZenSidebar();
        ShowToast("РџРѕСЂСЏРґРѕРє РІРѕСЂРєСЃРїРµР№СЃРѕРІ РѕР±РЅРѕРІР»С‘РЅ", $"В«{movedState.Name}В» РїРµСЂРµРјРµС‰С‘РЅ РЅР° РїРѕР·РёС†РёСЋ #{toIndex + 1}");
    }

    private void ShowWorkspaceContextMenu(int spaceIndex)
    {
        if (spaceIndex < 0 || spaceIndex >= _spaces.Count) return;
        var space = _spaces[spaceIndex].State;
        var menu = NewMenu();
        Add(menu, $"Переключиться на «{space.Name}»", () => SelectSpaceByIndex(spaceIndex), spaceIndex != _activeSpace, $"Ctrl+{spaceIndex + 1}");
        Add(menu, "Настроить воркспейс", () => OpenWorkspaceModal(spaceIndex));
        Add(menu, "Создать новый воркспейс", () => OpenWorkspaceModal(null));
        Sep(menu);
        Add(menu, "Удалить пространство", () => DeleteSpace(spaceIndex), _spaces.Count > 1);
        OpenMenu(menu);
    }

    // =========================================================================
    // 5. ZEN WORKSPACE CREATION / EDIT MODAL (Name, Icon, Gradient, Container)
    // =========================================================================
    public void OpenWorkspaceModal(int? editSpaceIndex = null)
    {
        CloseMenusExcept(NewSpacePopup);
        _editingWorkspaceIndex = editSpaceIndex;

        if (editSpaceIndex is int idx && idx >= 0 && idx < _spaces.Count)
        {
            var st = _spaces[idx].State;
            _selectedWorkspaceIcon = string.IsNullOrWhiteSpace(st.Icon) ? "рџЏ " : st.Icon;
            _selectedWorkspaceAccent = string.IsNullOrWhiteSpace(st.Accent) ? "#7C5CE4" : st.Accent;
            _selectedWorkspaceGradientEnd = string.IsNullOrWhiteSpace(st.GradientEnd) ? "#2A1F4D" : st.GradientEnd;
            _selectedWorkspaceContainerId = string.IsNullOrWhiteSpace(st.ContainerId) ? "default" : st.ContainerId;

            if (NewSpaceTitle is not null) NewSpaceTitle.Text = "РќР°СЃС‚СЂРѕР№РєР° РІРѕСЂРєСЃРїРµР№СЃР° Zen";
            if (NewSpaceConfirmBtnText is not null) NewSpaceConfirmBtnText.Text = "РЎРѕС…СЂР°РЅРёС‚СЊ";
            NewSpaceNameBox.Text = st.Name;
        }
        else
        {
            var preset = ZenWorkspaceGradients[_spaces.Count % ZenWorkspaceGradients.Length];
            _selectedWorkspaceIcon = ZenWorkspaceEmojis[_spaces.Count % ZenWorkspaceEmojis.Length];
            _selectedWorkspaceAccent = preset.Accent;
            _selectedWorkspaceGradientEnd = preset.GradientEnd;
            _selectedWorkspaceContainerId = "default";

            if (NewSpaceTitle is not null) NewSpaceTitle.Text = "РќРѕРІС‹Р№ РІРѕСЂРєСЃРїРµР№СЃ Zen";
            if (NewSpaceConfirmBtnText is not null) NewSpaceConfirmBtnText.Text = "РЎРѕР·РґР°С‚СЊ";
            NewSpaceNameBox.Text = $"Р’РѕСЂРєСЃРїРµР№СЃ {_spaces.Count + 1}";
        }

        RenderWorkspaceModalPickers();
        PaneHost.IsEnabled = false;
        NewSpacePopup.IsOpen = true;
    }

    private void RenderWorkspaceModalPickers()
    {
        if (NewSpacePreviewIcon is not null) NewSpacePreviewIcon.Text = _selectedWorkspaceIcon;
        if (NewSpacePreviewBadge is not null)
        {
            var c0 = ParseColorSafe(_selectedWorkspaceAccent, Color.FromRgb(0x7C, 0x5C, 0xE4));
            var c1 = ParseColorSafe(_selectedWorkspaceGradientEnd, Color.FromRgb(0x2A, 0x1F, 0x4D));
            NewSpacePreviewBadge.Background = new LinearGradientBrush(c0, c1, new Point(0, 0), new Point(1, 1));
        }

        // 1. Emoji / Icon Picker
        if (ZenWorkspaceIconPickerHost is not null)
        {
            ZenWorkspaceIconPickerHost.Children.Clear();
            foreach (var emoji in ZenWorkspaceEmojis)
            {
                bool selected = (emoji == _selectedWorkspaceIcon);
                var btn = new Button
                {
                    Width = 30,
                    Height = 30,
                    Margin = new Thickness(2),
                    Cursor = Cursors.Hand
                };
                var bd = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Background = selected ? Brush(_selectedWorkspaceAccent) : Brush("#25252B"),
                    BorderBrush = selected ? Brushes.White : Brush("#35353D"),
                    BorderThickness = new Thickness(selected ? 1.5 : 1),
                    Child = new TextBlock
                    {
                        Text = emoji,
                        FontSize = 14,
                        HorizontalAlignment = WpfHorizontalAlignment.Center,
                        VerticalAlignment = WpfVerticalAlignment.Center
                    }
                };
                btn.Template = CreateSimpleBorderButtonTemplate();
                btn.Content = bd;
                btn.Click += (_, _) =>
                {
                    _selectedWorkspaceIcon = emoji;
                    RenderWorkspaceModalPickers();
                };
                ZenWorkspaceIconPickerHost.Children.Add(btn);
            }
        }

        // 2. Gradient Color Picker
        if (ZenWorkspaceColorPickerHost is not null)
        {
            ZenWorkspaceColorPickerHost.Children.Clear();
            foreach (var grad in ZenWorkspaceGradients)
            {
                bool selected = string.Equals(grad.Accent, _selectedWorkspaceAccent, StringComparison.OrdinalIgnoreCase);
                var c0 = ParseColorSafe(grad.Accent, Color.FromRgb(0x7C, 0x5C, 0xE4));
                var c1 = ParseColorSafe(grad.GradientEnd, Color.FromRgb(0x1E, 0x1E, 0x24));

                var btn = new Button
                {
                    Width = 38,
                    Height = 26,
                    Margin = new Thickness(3, 2, 3, 2),
                    Cursor = Cursors.Hand,
                    ToolTip = grad.Name
                };
                var bd = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Background = new LinearGradientBrush(c0, c1, new Point(0, 0), new Point(1, 1)),
                    BorderBrush = selected ? Brushes.White : Brush("#40FFFFFF"),
                    BorderThickness = new Thickness(selected ? 2 : 1)
                };
                btn.Template = CreateSimpleBorderButtonTemplate();
                btn.Content = bd;
                btn.Click += (_, _) =>
                {
                    _selectedWorkspaceAccent = grad.Accent;
                    _selectedWorkspaceGradientEnd = grad.GradientEnd;
                    RenderWorkspaceModalPickers();
                };
                ZenWorkspaceColorPickerHost.Children.Add(btn);
            }
        }

        // 3. Cookie Jar Container Picker
        if (ZenWorkspaceContainerPickerHost is not null)
        {
            ZenWorkspaceContainerPickerHost.Children.Clear();
            foreach (var jar in ZenContainers.All)
            {
                bool selected = string.Equals(jar.Id, _selectedWorkspaceContainerId, StringComparison.OrdinalIgnoreCase);
                var jarColor = ParseColorSafe(jar.ColorHex, Color.FromRgb(0x8E, 0x8E, 0x93));

                var btn = new Button
                {
                    Height = 28,
                    Margin = new Thickness(2),
                    Padding = new Thickness(8, 0, 8, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = $"РџСЂРѕС„РёР»СЊ WebView2: {ZenContainers.NormalizeProfileName(jar.Id)}"
                };
                var bd = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(8, 0, 8, 0),
                    Background = selected
                        ? new SolidColorBrush(Color.FromArgb(0x38, jarColor.R, jarColor.G, jarColor.B))
                        : Brush("#222228"),
                    BorderBrush = selected ? new SolidColorBrush(jarColor) : Brush("#32323A"),
                    BorderThickness = new Thickness(selected ? 1.5 : 1)
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = WpfVerticalAlignment.Center };
                row.Children.Add(new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = new SolidColorBrush(jarColor),
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = WpfVerticalAlignment.Center
                });
                row.Children.Add(new TextBlock
                {
                    Text = jar.Name,
                    FontSize = 11,
                    FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = selected ? Brushes.White : Brush("#C8C8D2"),
                    VerticalAlignment = WpfVerticalAlignment.Center
                });
                bd.Child = row;
                btn.Template = CreateSimpleBorderButtonTemplate();
                btn.Content = bd;
                btn.Click += (_, _) =>
                {
                    _selectedWorkspaceContainerId = jar.Id;
                    RenderWorkspaceModalPickers();
                };
                ZenWorkspaceContainerPickerHost.Children.Add(btn);
            }
        }
    }

    internal void SaveWorkspaceFromModal()
    {
        var name = string.IsNullOrWhiteSpace(NewSpaceNameBox.Text)
            ? $"Р’РѕСЂРєСЃРїРµР№СЃ {_spaces.Count + 1}"
            : NewSpaceNameBox.Text.Trim();

        if (_editingWorkspaceIndex is int editIdx && editIdx >= 0 && editIdx < _spaces.Count)
        {
            var st = _spaces[editIdx].State;
            st.Name = name;
            st.Icon = _selectedWorkspaceIcon;
            st.Accent = _selectedWorkspaceAccent;
            st.GradientEnd = _selectedWorkspaceGradientEnd;
            st.ContainerId = _selectedWorkspaceContainerId;
            NewSpacePopup.IsOpen = false;
            Save();
            RefreshSpaces();
            RefreshZenSidebar();
            ShowToast("Р’РѕСЂРєСЃРїРµР№СЃ РѕР±РЅРѕРІР»С‘РЅ", $"{st.Icon} {st.Name} ({ZenContainers.Get(st.ContainerId).Name})");
        }
        else
        {
            var state = new SpaceState
            {
                Name = name,
                Icon = _selectedWorkspaceIcon,
                Accent = _selectedWorkspaceAccent,
                GradientEnd = _selectedWorkspaceGradientEnd,
                ContainerId = _selectedWorkspaceContainerId,
                Folders = new List<FolderState>()
            };
            _state.Spaces.Add(state);
            _spaces.Add(new RuntimeSpace(state) { Loaded = true });
            NewSpacePopup.IsOpen = false;
            SwitchSpace(_spaces.Count - 1);
            RefreshZenSidebar();
            ShowToast("РЎРѕР·РґР°РЅ РІРѕСЂРєСЃРїРµР№СЃ", $"{state.Icon} {state.Name} В· {ZenContainers.Get(state.ContainerId).Name}");
        }
    }

    // =========================================================================
    // 6. AUDIO MUTE / UNMUTE & TAB DRAG-AND-DROP REORDERING
    // =========================================================================
    private void TabAudioMute_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: BrowserTab tab })
        {
            ToggleTabMute(tab);
        }
    }

    public void ToggleTabMute(BrowserTab tab)
    {
        tab.IsMuted = !tab.IsMuted;
        try
        {
            if (tab.View?.CoreWebView2 is { } core1) core1.IsMuted = tab.IsMuted;
            if (tab.SecondaryView?.CoreWebView2 is { } core2) core2.IsMuted = tab.IsMuted;
        }
        catch { }
        RefreshZenSidebar();
        ShowToast(tab.IsMuted ? "Р—РІСѓРє РІРєлаРґРєРё РІС‹РєР»СЋС‡РµРЅ" : "Р—РІСѓРє РІРєлаРґРєРё РІРєР»СЋС‡С‘РЅ", tab.DisplayTitle);
    }

    // ---- Drag & Drop Reordering in TabsList, Pinned & Essentials ----
    private void TabItem_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("LumaTab"))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void TabItem_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("LumaTab")) return;
        if (e.Data.GetData("LumaTab") is not BrowserTab draggedTab) return;
        if (sender is not FrameworkElement { DataContext: BrowserTab targetTab } || ReferenceEquals(draggedTab, targetTab)) return;

        var list = _spaces[_activeSpace].Tabs;
        int fromIdx = list.IndexOf(draggedTab);
        int toIdx = list.IndexOf(targetTab);
        if (fromIdx < 0 || toIdx < 0) return;

        draggedTab.IsPinned = false;
        draggedTab.IsEssential = false;
        draggedTab.FolderId = null;

        list.Move(fromIdx, toIdx);
        FilterTabs();
        RefreshZenSidebar();
        Save();
        e.Handled = true;
    }

    private void TabsList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("LumaTab") || e.Data.GetDataPresent("LumaSaved"))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void TabsList_Drop(object sender, DragEventArgs e)
    {
        if (e.Handled) return;
        if (e.Data.GetData("LumaTab") is BrowserTab tab && (tab.IsPinned || !string.IsNullOrEmpty(tab.FolderId)))
        {
            var site = _spaces[_activeSpace].State.LooseItems.FirstOrDefault(s => s.TabId == tab.Id || SameSite(s.Url, tab.FullUrl));
            if (site is not null) _spaces[_activeSpace].State.LooseItems.Remove(site);
            tab.IsPinned = false;
            tab.IsEssential = false;
            tab.FolderId = null;
            FilterTabs();
            RefreshZenSidebar();
            Save();
            e.Handled = true;
        }
    }

    private void ZenPinned_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("LumaTab"))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void ZenPinned_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("LumaTab") is BrowserTab tab)
        {
            PinTab(tab);
            RefreshZenSidebar();
            e.Handled = true;
        }
    }

    private void ZenEssentials_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("LumaTab"))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void ZenEssentials_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("LumaTab") is BrowserTab tab)
        {
            PinToEssentials(tab);
            e.Handled = true;
        }
    }

    // =========================================================================
    // 7. ZEN GLANCE (Alt + Left Click Floating Preview without losing focus)
    // =========================================================================
    public async Task OpenZenGlanceAsync(BrowserTab tab)
    {
        if (ZenGlancePopup is null) return;
        _glanceTargetTab = tab;

        if (ZenGlanceTitleText is not null) ZenGlanceTitleText.Text = tab.DisplayTitle;
        if (ZenGlanceUrlText is not null) ZenGlanceUrlText.Text = tab.ActiveUrl;
        if (ZenGlanceDomainBadge is not null) ZenGlanceDomainBadge.Text = tab.Domain;
        if (ZenGlanceContainerBadge is not null) ZenGlanceContainerBadge.Text = $"РљРѕРЅС‚РµР№РЅРµСЂ: {tab.ContainerName}";
        if (ZenGlanceFaviconImage is not null)
        {
            try { ZenGlanceFaviconImage.Source = new BitmapImage(new Uri(tab.FaviconUrl)); }
            catch { ZenGlanceFaviconImage.Source = null; }
        }
        if (ZenGlanceBodyText is not null)
        {
            ZenGlanceBodyText.Text = "ЗаРіСЂСѓР·РєР° РїСЂРµРґРїСЂРѕСЃРјРѕС‚СЂР° Luma Glance...";
        }

        ZenGlancePopup.IsOpen = true;

        try
        {
            if (tab.ActiveView?.CoreWebView2 is { } core && !tab.IsHome)
            {
                var raw = await core.ExecuteScriptAsync("(() => { const p = [...document.querySelectorAll('p, h1, h2, h3, article')].map(e => e.innerText?.trim()).filter(Boolean).slice(0, 18); return p.join('\\n\\n') || document.body?.innerText?.slice(0, 2200) || ''; })()");
                var text = System.Text.Json.JsonSerializer.Deserialize<string>(raw);
                if (!string.IsNullOrWhiteSpace(text) && ZenGlanceBodyText is not null)
                {
                    ZenGlanceBodyText.Text = text.Trim();
                    return;
                }
            }

            if (!tab.IsHome && !string.IsNullOrWhiteSpace(tab.ActiveUrl) && tab.ActiveUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                _glanceService ??= new Luma.Services.GlancePreviewService();
                var card = _glanceService.AnalyzeUrl(tab.ActiveUrl);
                if (ZenGlanceBodyText is not null)
                {
                    ZenGlanceBodyText.Text = $"Р”РѕРјРµРЅ: {card.Host} ({card.Protocol})\nРљР°С‚РµРіРѕСЂРёСЏ: {card.Category}\nР§РёСЃС‚С‹Р№ URL: {card.CleanUrl}\nРЈРґалеРЅРѕ С‚СЂРµРєРµСЂРѕРІ: {card.RemovedTrackingParamsCount}\n\nРќажРјРёС‚Рµ В«РћС‚РєСЂС‹С‚СЊ РІРєлаРґРєСѓВ», С‡С‚РѕР±С‹ заРіСЂСѓР·РёС‚СЊ Рё Р°РєС‚РёРІРёСЂРѕРІР°С‚СЊ СЃС‚СЂР°РЅРёС†Сѓ РїРѕР»РЅРѕСЃС‚СЊСЋ.";
                }
            }
            else if (ZenGlanceBodyText is not null)
            {
                ZenGlanceBodyText.Text = "Р’РЅСѓС‚СЂРµРЅРЅСЏСЏ РёР»Рё СЃС‚Р°СЂС‚РѕРІР°СЏ СЃС‚СЂР°РЅРёС†Р° Luma Browser.";
            }
        }
        catch (Exception ex)
        {
            if (ZenGlanceBodyText is not null)
                ZenGlanceBodyText.Text = $"РџСЂРµРґРїСЂРѕСЃРјРѕС‚СЂ СЃС‚СЂР°РЅРёС†С‹: {tab.DisplayTitle}\nURL: {tab.ActiveUrl}\n({ex.Message})";
        }
    }

    private void ZenGlanceExpand_Click(object sender, RoutedEventArgs e)
    {
        if (ZenGlancePopup is not null) ZenGlancePopup.IsOpen = false;
        if (_glanceTargetTab is not null)
        {
            ActivateTabFromUser(_glanceTargetTab);
        }
    }

    private async void ZenGlanceSplit_Click(object sender, RoutedEventArgs e)
    {
        if (ZenGlancePopup is not null) ZenGlancePopup.IsOpen = false;
        if (_glanceTargetTab is not null && CurrentTab is not null && !ReferenceEquals(CurrentTab, _glanceTargetTab))
        {
            var url = _glanceTargetTab.ActiveUrl;
            var title = _glanceTargetTab.DisplayTitle;
            CloseTab(_glanceTargetTab);
            await EnableSplitAsync(CurrentTab, url, true);
            CurrentTab.SecondaryTitle = title;
            FilterTabs();
            RefreshZenSidebar();
        }
    }

    private void ZenGlanceClose_Click(object sender, RoutedEventArgs e)
    {
        if (ZenGlancePopup is not null) ZenGlancePopup.IsOpen = false;
    }

    // =========================================================================
    // 8. SPLIT VIEW GROUPING IN SIDEBAR (Ctrl + Alt + V)
    // =========================================================================
    public async void SplitView_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTab is null || CurrentTab.IsHome)
        {
            ShowToast("Split View (Ctrl+Alt+V)", "Откройте сайт, чтобы разделить экран");

            return;
        }

        if (CurrentTab.IsSplit)
        {
            UnsplitCurrentTab(CurrentTab);
            return;
        }

        // Find another non-home tab in the active workspace to pair into Split View, or open Search(Split)
        var partner = Tabs.FirstOrDefault(t => !ReferenceEquals(t, CurrentTab) && !t.IsHome && !t.IsPinned && string.IsNullOrEmpty(t.FolderId));
        if (partner is not null)
        {
            var partnerUrl = partner.ActiveUrl;
            var partnerTitle = partner.DisplayTitle;
            var partnerFav = partner.Favicon;
            CloseTab(partner);
            await EnableSplitAsync(CurrentTab, partnerUrl, true);
            CurrentTab.SecondaryTitle = partnerTitle;
            if (!string.IsNullOrEmpty(partnerFav)) CurrentTab.SecondaryFavicon = partnerFav;
            FilterTabs();
            RefreshZenSidebar();
            ShowToast("Split View Р°РєС‚РёРІРµРЅ (Ctrl+Alt+V)", $"РћР±СЉРµРґРёРЅРµРЅС‹ В«{CurrentTab.Title}В» Рё В«{partnerTitle}В»");
        }
        else
        {
            OpenSearch(SearchPurpose.Split);
        }
    }

    private async void UnsplitTab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: BrowserTab tab })
        {
            await UnsplitTabToSeparateTabAsync(tab);
        }
    }

    private async void UnsplitCurrentTab(BrowserTab tab)
    {
        await UnsplitTabToSeparateTabAsync(tab);
    }

    private async Task UnsplitTabToSeparateTabAsync(BrowserTab tab)
    {
        if (!tab.IsSplit) return;
        var secUrl = tab.SecondaryUrl;
        var secTitle = tab.SecondaryTitle;
        ClosePane(tab, "secondary");
        if (!string.IsNullOrWhiteSpace(secUrl))
        {
            var newTab = await AddTabAsync(secUrl, activate: false, containerId: tab.ContainerId);
            newTab.Title = secTitle;
        }
        FilterTabs();
        RefreshZenSidebar();
        ShowToast("Split View СЂазСЉРµРґРёРЅС‘РЅ", "Р’РєлаРґРєРё СЂазРґелеРЅС‹ РѕР±СЂР°С‚РЅРѕ РІ СЃРїРёСЃРѕРє");
    }

    private void SplitSubTabPrimary_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BrowserTab tab })
        {
            if (e.OriginalSource is DependencyObject src && IsInsideChildButton(src, (DependencyObject)sender)) return;
            tab.ActivePane = "primary";
            if (!ReferenceEquals(CurrentTab, tab)) ActivateTabFromUser(tab);
            else { UpdateChrome(); UpdateIndicators(); tab.View?.Focus(); }
            e.Handled = true;
        }
    }

    private void SplitSubTabSecondary_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BrowserTab tab })
        {
            if (e.OriginalSource is DependencyObject src && IsInsideChildButton(src, (DependencyObject)sender)) return;
            tab.ActivePane = "secondary";
            if (!ReferenceEquals(CurrentTab, tab)) ActivateTabFromUser(tab);
            else { UpdateChrome(); UpdateIndicators(); tab.SecondaryView?.Focus(); }
            e.Handled = true;
        }
    }

    private void SplitSubTabClosePrimary_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: BrowserTab tab })
        {
            ClosePane(tab, "primary");
            RefreshZenSidebar();
        }
    }

    private void SplitSubTabCloseSecondary_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: BrowserTab tab })
        {
            ClosePane(tab, "secondary");
            RefreshZenSidebar();
        }
    }

    // =========================================================================
    // 9. COMPACT MODE (Ctrl+Alt+S Floating Sidebar & Ctrl+Alt+C 48px Icon-Only)
    // =========================================================================
    private int _sidebarAnimToken = 0;

    public void ToggleSidebarExpand_Click(object sender, RoutedEventArgs e)
    {
        _isSidebarExpanded = !_isSidebarExpanded;
        _isSidebarHoverExpanded = false;
        _state.SidebarCollapsedIconsOnly = !_isSidebarExpanded;
        _suppressHoverExpandUntilLeave = !_isSidebarExpanded;

        double targetWidth = _isSidebarExpanded ? 206.0 : 48.0;

        _sidebarAnimToken++;
        Sidebar?.BeginAnimation(FrameworkElement.WidthProperty, null);
        if (SidebarShadow is not null)
        {
            SidebarShadow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            SidebarShadow.Opacity = 0.0;
        }

        if (SidebarColumn is not null)
        {
            SidebarColumn.Width = new GridLength(targetWidth);
        }
        if (Sidebar is not null)
        {
            Sidebar.Width = targetWidth;
            Sidebar.MaxWidth = targetWidth;
            Sidebar.Clip = new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(0, 0, targetWidth, 10000));
        }

        IsSidebarVisualExpanded = _isSidebarExpanded;
        UpdateSidebarExpandedUi();
        RefreshZenSidebar();
        UpdateResponsiveLayout();
        Save();
    }

    public void ToggleCompactMode_Click(object sender, RoutedEventArgs e)
    {
        ToggleSidebarExpand_Click(sender, e);
    }

    private void AnimateHoverSidebar(double targetWidth)
    {
        if (Sidebar is null || _isToolsDrawerOpen || _fullscreen) return;

        double currentWidth = Sidebar.ActualWidth > 0 ? Sidebar.ActualWidth : Sidebar.Width;
        if (double.IsNaN(currentWidth) || currentWidth <= 0) currentWidth = targetWidth;

        if (Math.Abs(currentWidth - targetWidth) < 1.0)
        {
            Sidebar.BeginAnimation(FrameworkElement.WidthProperty, null);
            Sidebar.Width = targetWidth;
            BeginAnimation(SidebarSlideProperty, null);
            if (SidebarColumn is not null) SidebarColumn.Width = new GridLength(targetWidth);
            if (SidebarShadow is not null)
            {
                SidebarShadow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                SidebarShadow.Opacity = targetWidth > 50.0 ? 0.28 : 0.0;
            }
            IsSidebarVisualExpanded = targetWidth > 50.0;
            UpdateSidebarExpandedUi();
            return;
        }

        int currentToken = ++_sidebarAnimToken;

        double distance = Math.Abs(targetWidth - currentWidth);
        int durationMs = Math.Clamp((int)(150.0 * (distance / 158.0)), 40, 150);

        var widthAnim = new DoubleAnimation
        {
            From = currentWidth,
            To = targetWidth,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var shadowAnim = new DoubleAnimation
        {
            From = SidebarShadow?.Opacity ?? (targetWidth > 50.0 ? 0.0 : 0.28),
            To = targetWidth > 50.0 ? 0.28 : 0.0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        // Smooth opacity fade on text/expanded details so collapse is silky smooth and never choppy
        double textTargetOpacity = targetWidth > 50.0 ? 1.0 : 0.0;
        var textFadeAnim = new DoubleAnimation
        {
            To = textTargetOpacity,
            Duration = TimeSpan.FromMilliseconds(targetWidth > 50.0 ? 130 : 80),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        if (SidebarAddressBar is not null)
        {
            if (targetWidth > 50.0) SidebarAddressBar.Visibility = Visibility.Visible;
            SidebarAddressBar.BeginAnimation(UIElement.OpacityProperty, textFadeAnim);
        }
        if (ZenWorkspaceNameBlock is not null)
        {
            if (targetWidth > 50.0) ZenWorkspaceNameBlock.Visibility = Visibility.Visible;
            ZenWorkspaceNameBlock.BeginAnimation(UIElement.OpacityProperty, textFadeAnim);
        }
        if (ZenNewTabLabel is not null)
        {
            if (targetWidth > 50.0) ZenNewTabLabel.Visibility = Visibility.Visible;
            ZenNewTabLabel.BeginAnimation(UIElement.OpacityProperty, textFadeAnim);
        }

        widthAnim.Completed += (_, _) =>
        {
            if (currentToken != _sidebarAnimToken) return;

            Sidebar.BeginAnimation(FrameworkElement.WidthProperty, null);
            Sidebar.Width = targetWidth;
            BeginAnimation(SidebarSlideProperty, null);
            if (SidebarColumn is not null) SidebarColumn.Width = new GridLength(targetWidth);

            if (SidebarShadow is not null)
            {
                SidebarShadow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                SidebarShadow.Opacity = targetWidth > 50.0 ? 0.28 : 0.0;
            }

            IsSidebarVisualExpanded = targetWidth > 50.0;
            UpdateSidebarExpandedUi();
        };

        if (targetWidth > 50.0)
        {
            IsSidebarVisualExpanded = true;
            UpdateSidebarExpandedUi();
        }

        Sidebar.BeginAnimation(FrameworkElement.WidthProperty, widthAnim, HandoffBehavior.SnapshotAndReplace);
        BeginAnimation(SidebarSlideProperty, widthAnim, HandoffBehavior.SnapshotAndReplace);
        if (SidebarShadow is not null)
        {
            SidebarShadow.BeginAnimation(DropShadowEffect.OpacityProperty, shadowAnim, HandoffBehavior.SnapshotAndReplace);
        }
    }

    private void UpdateSidebarExpandedUi()
    {
        bool expanded = _isSidebarExpanded || _isSidebarHoverExpanded;
        IsSidebarVisualExpanded = expanded;
        StartSidebarEdgePolling();

        double contentWidth = expanded ? 194.0 : 36.0;
        double sidebarWidth = expanded ? 206.0 : 48.0;

        if (Sidebar is not null)
        {
            Sidebar.Width = sidebarWidth;
            Sidebar.MaxWidth = sidebarWidth;
            Sidebar.IsHitTestVisible = true;
            Sidebar.Clip = new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(0, 0, sidebarWidth, 10000));
        }

        if (ZenSidebarInnerGrid is not null)
        {
            ZenSidebarInnerGrid.Margin = new Thickness(6, 6, 6, 6);
            ZenSidebarInnerGrid.HorizontalAlignment = expanded ? WpfHorizontalAlignment.Left : WpfHorizontalAlignment.Center;
            ZenSidebarInnerGrid.Width = contentWidth;
            ZenSidebarInnerGrid.MaxWidth = contentWidth;
        }

        if (TabsListContainer is not null)
        {
            TabsListContainer.Width = contentWidth;
            TabsListContainer.MaxWidth = contentWidth;
            TabsListContainer.HorizontalAlignment = expanded ? WpfHorizontalAlignment.Stretch : WpfHorizontalAlignment.Center;
        }
        if (TabsList is not null)
        {
            TabsList.Width = contentWidth;
            TabsList.MaxWidth = contentWidth;
            TabsList.HorizontalAlignment = expanded ? WpfHorizontalAlignment.Stretch : WpfHorizontalAlignment.Center;
        }

        if (ZenNewTabBtn is not null)
        {
            ZenNewTabBtn.Width = contentWidth;
            ZenNewTabBtn.HorizontalAlignment = expanded ? WpfHorizontalAlignment.Left : WpfHorizontalAlignment.Center;
        }

        if (SidebarEdgeTrigger is not null)
        {
            SidebarEdgeTrigger.Visibility = Visibility.Collapsed;
            SidebarEdgeTrigger.IsHitTestVisible = false;
        }
        if (ExpandSidebarIcon is not null && TryFindResource("ZenIconSidebar") is Geometry sbGeom)
        {
            ExpandSidebarIcon.Data = sbGeom;
        }

        if (ZenTopButtonsGrid is not null)
        {
            if (expanded)
            {
                if (ZenTopCol0 is not null) ZenTopCol0.Width = new GridLength(34);
                if (ZenTopCol1 is not null) ZenTopCol1.Width = new GridLength(2);
                if (ZenTopCol2 is not null) ZenTopCol2.Width = new GridLength(34);
                if (ZenTopCol3 is not null) ZenTopCol3.Width = new GridLength(1, GridUnitType.Star);
                if (ZenTopCol4 is not null) ZenTopCol4.Width = new GridLength(34);
                if (ZenTopCol5 is not null) ZenTopCol5.Width = new GridLength(2);
                if (ZenTopCol6 is not null) ZenTopCol6.Width = new GridLength(34);
                if (ZenTopCol7 is not null) ZenTopCol7.Width = new GridLength(2);
                if (ZenTopCol8 is not null) ZenTopCol8.Width = new GridLength(34);

                if (ZenTopMenuButton is not null) ZenTopMenuButton.Visibility = Visibility.Visible;
                if (ZenBackButton is not null) ZenBackButton.Visibility = Visibility.Visible;
                if (ZenForwardButton is not null) ZenForwardButton.Visibility = Visibility.Visible;
                if (ZenReloadButton is not null) ZenReloadButton.Visibility = Visibility.Visible;

                if (ZenExpandSidebarBtn is not null)
                {
                    Grid.SetColumn(ZenExpandSidebarBtn, 2);
                    Grid.SetColumnSpan(ZenExpandSidebarBtn, 1);
                    ZenExpandSidebarBtn.Width = 34;
                    ZenExpandSidebarBtn.Height = 34;
                    ZenExpandSidebarBtn.HorizontalAlignment = WpfHorizontalAlignment.Center;
                    ZenExpandSidebarBtn.Visibility = Visibility.Visible;
                    ZenExpandSidebarBtn.ToolTip = _isSidebarExpanded ? "РЎРІРµСЂРЅСѓС‚СЊ Р±РѕРєРѕРІСѓСЋ РїР°РЅелСЊ (Ctrl+Alt+C)" : "ЗаРєСЂРµРїРёС‚СЊ Р±РѕРєРѕРІСѓСЋ РїР°РЅелСЊ (Ctrl+Alt+C)";
                }
            }
            else
            {
                if (ZenTopCol0 is not null) ZenTopCol0.Width = new GridLength(36);
                if (ZenTopCol1 is not null) ZenTopCol1.Width = new GridLength(0);
                if (ZenTopCol2 is not null) ZenTopCol2.Width = new GridLength(0);
                if (ZenTopCol3 is not null) ZenTopCol3.Width = new GridLength(0);
                if (ZenTopCol4 is not null) ZenTopCol4.Width = new GridLength(0);
                if (ZenTopCol5 is not null) ZenTopCol5.Width = new GridLength(0);
                if (ZenTopCol6 is not null) ZenTopCol6.Width = new GridLength(0);
                if (ZenTopCol7 is not null) ZenTopCol7.Width = new GridLength(0);
                if (ZenTopCol8 is not null) ZenTopCol8.Width = new GridLength(0);

                if (ZenTopMenuButton is not null) ZenTopMenuButton.Visibility = Visibility.Collapsed;
                if (ZenBackButton is not null) ZenBackButton.Visibility = Visibility.Collapsed;
                if (ZenForwardButton is not null) ZenForwardButton.Visibility = Visibility.Collapsed;
                if (ZenReloadButton is not null) ZenReloadButton.Visibility = Visibility.Collapsed;

                if (ZenExpandSidebarBtn is not null)
                {
                    Grid.SetColumn(ZenExpandSidebarBtn, 0);
                    Grid.SetColumnSpan(ZenExpandSidebarBtn, 1);
                    ZenExpandSidebarBtn.Width = 36;
                    ZenExpandSidebarBtn.Height = 34;
                    ZenExpandSidebarBtn.HorizontalAlignment = WpfHorizontalAlignment.Center;
                    ZenExpandSidebarBtn.Visibility = Visibility.Visible;
                    ZenExpandSidebarBtn.ToolTip = "ЗаРєСЂРµРїРёС‚СЊ Р±РѕРєРѕРІСѓСЋ РїР°РЅелСЊ (Ctrl+Alt+C)";
                }
            }
        }
        else if (ZenExpandSidebarBtn is not null)
        {
            ZenExpandSidebarBtn.ToolTip = _isSidebarExpanded ? "РЎРІРµСЂРЅСѓС‚СЊ Р±РѕРєРѕРІСѓСЋ РїР°РЅелСЊ (Ctrl+Alt+C)" : "ЗаРєСЂРµРїРёС‚СЊ Р±РѕРєРѕРІСѓСЋ РїР°РЅелСЊ (Ctrl+Alt+C)";
            if (expanded)
            {
                Grid.SetColumn(ZenExpandSidebarBtn, 2);
                Grid.SetColumnSpan(ZenExpandSidebarBtn, 1);
                ZenExpandSidebarBtn.Width = 34;
                ZenExpandSidebarBtn.Height = 34;
                ZenExpandSidebarBtn.HorizontalAlignment = WpfHorizontalAlignment.Left;
            }
            else
            {
                Grid.SetColumn(ZenExpandSidebarBtn, 0);
                Grid.SetColumnSpan(ZenExpandSidebarBtn, 1);
                ZenExpandSidebarBtn.Width = 36;
                ZenExpandSidebarBtn.Height = 34;
                ZenExpandSidebarBtn.HorizontalAlignment = WpfHorizontalAlignment.Center;
            }
        }

        if (ZenEssentialsWrapper is not null)
        {
            ZenEssentialsWrapper.Width = expanded ? 194 : 36;
            ZenEssentialsWrapper.HorizontalAlignment = expanded ? WpfHorizontalAlignment.Left : WpfHorizontalAlignment.Center;
        }
        if (ZenEssentialsHost is not null)
        {
            ZenEssentialsHost.Width = expanded ? 194 : 36;
            ZenEssentialsHost.HorizontalAlignment = expanded ? WpfHorizontalAlignment.Left : WpfHorizontalAlignment.Center;
            ZenEssentialsHost.Columns = expanded ? 4 : 1;
        }

        if (ZenWorkspaceIndicatorRow is not null)
        {
            ZenWorkspaceIndicatorRow.Margin = expanded ? new Thickness(0, 2, 0, 2) : new Thickness(0, 2, 0, 2);
            ZenWorkspaceIndicatorRow.Opacity = 1.0;
        }
        if (ZenWorkspaceNameBlock is not null)
        {
            ZenWorkspaceNameBlock.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            ZenWorkspaceNameBlock.Opacity = expanded ? 1.0 : 0.0;
        }
        if (ZenClearTabsBtn is not null)
        {
            ZenClearTabsBtn.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ZenPinnedSeparator is not null)
        {
            ZenPinnedSeparator.Margin = expanded ? new Thickness(0, 0, 8, 0) : new Thickness(4, 0, 4, 0);
        }
        if (ZenNewTabBtn is not null)
        {
            ZenNewTabBtn.Width = expanded ? 194 : 36;
            ZenNewTabBtn.Height = 32;
            ZenNewTabBtn.HorizontalAlignment = WpfHorizontalAlignment.Left;
            ZenNewTabBtn.Padding = expanded ? new Thickness(0) : new Thickness(0);
        }
        if (ZenNewTabLabel is not null)
        {
            ZenNewTabLabel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            ZenNewTabLabel.Opacity = expanded ? 1.0 : 0.0;
        }
        if (TabsList is not null)
        {
            ScrollViewer.SetVerticalScrollBarVisibility(TabsList, expanded ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
        }
        if (SidebarAddressBar is not null)
        {
            SidebarAddressBar.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            SidebarAddressBar.Opacity = expanded ? 1.0 : 0.0;
        }
        if (ZenCompactModeBtn is not null)
        {
            ZenCompactModeBtn.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ZenSplitViewFooterBtn is not null)
        {
            ZenSplitViewFooterBtn.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ZenToolsDrawerFooterBtn is not null)
        {
            ZenToolsDrawerFooterBtn.Visibility = Visibility.Visible;
            if (expanded)
            {
                Grid.SetColumn(ZenToolsDrawerFooterBtn, 0);
                Grid.SetColumnSpan(ZenToolsDrawerFooterBtn, 1);
                ZenToolsDrawerFooterBtn.HorizontalAlignment = WpfHorizontalAlignment.Left;
            }
            else
            {
                Grid.SetColumn(ZenToolsDrawerFooterBtn, 0);
                Grid.SetColumnSpan(ZenToolsDrawerFooterBtn, 3);
                ZenToolsDrawerFooterBtn.HorizontalAlignment = WpfHorizontalAlignment.Center;
            }
        }
        if (ZenWorkspaceStripScroll is not null)
        {
            ZenWorkspaceStripScroll.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ZenCreateNewBtn is not null)
        {
            ZenCreateNewBtn.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ZenSettingsFooterBtn is not null)
        {
            ZenSettingsFooterBtn.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        }

        // Keep essentials tiles size synchronized
        if (ZenEssentialsHost is not null)
        {
            RenderZenEssentials();
        }
    }

    private DispatcherTimer? _sidebarEdgePollTimer;
    private DateTime _sidebarEdgeSince = DateTime.MinValue;

    private void StartSidebarEdgePolling()
    {
        if (_sidebarEdgePollTimer is not null) return;
        _sidebarEdgePollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _sidebarEdgePollTimer.Tick += (_, _) => SidebarEdgePollTick();
        _sidebarEdgePollTimer.Start();
    }

    private void SidebarEdgePollTick()
    {
        if (!IsLoaded || Sidebar is null) return;
        if (_isSidebarExpanded || _isToolsDrawerOpen || _fullscreen) { _sidebarEdgeSince = DateTime.MinValue; return; }

        // Already hover-expanded: let the regular logic collapse it when the cursor leaves
        if (_isSidebarHoverExpanded) { _sidebarEdgeSince = DateTime.MinValue; CheckSidebarHoverState(); return; }

        if (_suppressHoverExpandUntilLeave || !IsForegroundWindowOrChild() || !GetCursorPos(out var native))
        {
            _sidebarEdgeSince = DateTime.MinValue;
            return;
        }

        Point topLeft;
        try { topLeft = Sidebar.PointToScreen(new Point(0, 0)); }
        catch { _sidebarEdgeSince = DateTime.MinValue; return; }

        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var cur = transform.Transform(new Point(native.X - topLeft.X, native.Y - topLeft.Y));
        bool insideHeight = cur.Y >= 0 && cur.Y <= Sidebar.ActualHeight;

        // Expand ONLY when hovering at the far left edge of the screen/rail (-12px to 5px).
        // Tab tiles start at 6px, so hovering or moving across tabs (6px..48px) never expands the panel!
        bool atLeftEdge = insideHeight && cur.X >= -12.0 && cur.X <= 5.0;
        if (!atLeftEdge) { _sidebarEdgeSince = DateTime.MinValue; return; }

        if (_sidebarEdgeSince == DateTime.MinValue) _sidebarEdgeSince = DateTime.UtcNow;
        else if ((DateTime.UtcNow - _sidebarEdgeSince).TotalMilliseconds >= 120)
        {
            _sidebarEdgeSince = DateTime.MinValue;
            ExpandSidebarOnHover();
        }
    }

    private void OnSidebarMouseEnter()
    {
        CheckSidebarHoverState();
    }

    private void ZenSidebar_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isSidebarExpanded || _isToolsDrawerOpen || _fullscreen || Sidebar is null) return;
        var pt = e.GetPosition(Sidebar);
        // Entering from webpage side: flag it so moving across towards tabs does not expand panel
        _isEnteringFromOutside = pt.X >= 42.0;
    }

    private void SidebarEdgeTrigger_MouseEnter(object sender, MouseEventArgs e)
    {
    }

    private void ZenSidebar_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isSidebarExpanded || _isToolsDrawerOpen || _fullscreen || Sidebar is null) return;
        var pt = e.GetPosition(Sidebar);
        bool insideHeight = pt.Y >= 0 && pt.Y <= Sidebar.ActualHeight;

        if (!_isSidebarHoverExpanded)
        {
            // Expansion at the right edge is handled by StartSidebarEdgePolling (screen-space, works over WebView2 too)
            _isEnteringFromOutside = false;
        }
        else
        {
            // In expanded mode, collapse if cursor leaves the expanded 206px panel
            if (pt.X > 214.0 || pt.X < -12.0 || !insideHeight)
            {
                CollapseSidebarOnHover();
            }
        }
    }

    private void ExpandSidebarOnHover()
    {
        _isEnteringFromOutside = false;
        if (_isSidebarExpanded || _isToolsDrawerOpen || _fullscreen || _isSidebarHoverExpanded) return;
        if (_suppressHoverExpandUntilLeave) return;
        _isSidebarHoverExpanded = true;
        AnimateHoverSidebar(206.0);
    }

    private void CollapseSidebarOnHover()
    {
        _isEnteringFromOutside = false;
        if (!_isSidebarHoverExpanded) return;
        _isSidebarHoverExpanded = false;
        AnimateHoverSidebar(48.0);
    }

    private void CheckSidebarHoverState()
    {
        if (_isSidebarExpanded || _isToolsDrawerOpen || _fullscreen || !IsLoaded || Sidebar is null) return;
        if (!IsForegroundWindowOrChild()) return;

        // Keep sidebar open if any sidebar popup or context menu is open
        if ((MainMenuPopup?.IsOpen ?? false) ||
            (ZenWorkspaceMenuPopup?.IsOpen ?? false) ||
            (ZenWorkspaceGridPopup?.IsOpen ?? false) ||
            (ZenCreateNewPopup?.IsOpen ?? false) ||
            (NewSpacePopup?.IsOpen ?? false) ||
            (TranslatePopup?.IsOpen ?? false) ||
            (_openMenu?.IsOpen ?? false))
        {
            return;
        }

        if (!GetCursorPos(out var native)) return;
        Point topLeft;
        try { topLeft = Sidebar.PointToScreen(new Point(0, 0)); }
        catch { return; }

        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var cursorDip = transform.Transform(new Point(native.X - topLeft.X, native.Y - topLeft.Y));

        bool insideSidebarHeight = cursorDip.Y >= 0 && cursorDip.Y <= Sidebar.ActualHeight;

        if (_suppressHoverExpandUntilLeave)
        {
            if (cursorDip.X > 54 || !insideSidebarHeight)
            {
                _suppressHoverExpandUntilLeave = false;
            }
            return;
        }

        // Only collapse if currently hover expanded and mouse is outside
        if (_isSidebarHoverExpanded)
        {
            bool insideExpandedSidebar = cursorDip.X >= -12.0 && cursorDip.X <= 214.0 && insideSidebarHeight;
            if (!insideExpandedSidebar)
            {
                CollapseSidebarOnHover();
            }
        }
    }

    private void ZenSidebar_MouseLeave(object sender, MouseEventArgs e)
    {
        _isEnteringFromOutside = false;
        CheckSidebarHoverState();
    }

    // =========================================================================
    // 10. CAPSULE ADDRESS BAR    // =========================================================================
    // 10. CAPSULE ADDRESS BAR & QUICK ACTIONS
    // =========================================================================
    private void SidebarAddress_Click(object sender, MouseButtonEventArgs e)
    {
        if (SidebarAddressInput is not null && SidebarDomainText is not null)
        {
            SidebarDomainText.Visibility = Visibility.Collapsed;
            SidebarAddressInput.Visibility = Visibility.Visible;
            SidebarAddressInput.Text = CurrentTab?.ActiveUrl ?? string.Empty;
            SidebarAddressInput.Focus();
            SidebarAddressInput.SelectAll();
        }
        else
        {
            OpenSearch(SearchPurpose.Navigate, CurrentTab?.ActiveUrl);
        }
    }

    private void SidebarAddressInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var text = SidebarAddressInput.Text.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                NavigateInput(text);
            }
            EndAddressEdit();
            CurrentTab?.ActiveView?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndAddressEdit();
            CurrentTab?.ActiveView?.Focus();
            e.Handled = true;
        }
    }

    private void SidebarAddressInput_LostFocus(object sender, RoutedEventArgs e) => EndAddressEdit();

    private void EndAddressEdit()
    {
        if (SidebarAddressInput is not null && SidebarDomainText is not null)
        {
            SidebarAddressInput.Visibility = Visibility.Collapsed;
            SidebarDomainText.Visibility = Visibility.Visible;
        }
    }

    private void WorkspaceMenu_Click(object sender, RoutedEventArgs e) => ToggleWorkspaceMenuPopup();

    private void ClearUnpinnedTabs_Click(object sender, RoutedEventArgs e)
    {
        ClearTabs_Click(sender, e);
        ShowToast("Р’РєлаРґРєРё РѕС‡РёС‰РµРЅС‹", "ЗаРєСЂРµРїР»С‘РЅРЅС‹Рµ РІРєлаРґРєРё Рё Essentials СЃРѕС…СЂР°РЅРµРЅС‹");
    }

    private async void EssentialSite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrEmpty(url))
        {
            await AddTabAsync(url);
        }
    }

    private void CreateNewButton_Click(object sender, RoutedEventArgs e)
    {
        OpenWorkspaceModal(null);
    }

    private void CreateNewSpace_Click(object sender, RoutedEventArgs e)
    {
        if (ZenCreateNewPopup is not null) ZenCreateNewPopup.IsOpen = false;
        OpenWorkspaceModal(null);
    }

    private void CreateNewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ZenCreateNewPopup is not null) ZenCreateNewPopup.IsOpen = false;
        NewFolder_Click(sender, e);
    }

    private void CreateSplitView_Click(object sender, RoutedEventArgs e)
    {
        if (ZenCreateNewPopup is not null) ZenCreateNewPopup.IsOpen = false;
        SplitView_Click(sender, e);
    }

    private void WorkspaceSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tagStr } && int.TryParse(tagStr, out int idx))
        {
            SelectSpaceByIndex(idx);
            RefreshZenSidebar();
        }
    }

    private static Color ParseColorSafe(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }

    private static ControlTemplate CreateSimpleBorderButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        template.VisualTree = presenter;
        return template;
    }

    // =========================================================================
    // LUMA SHIELD (AdBlock & Safe Protection)
    // =========================================================================
        private void SidebarShieldBtn_Click(object sender, RoutedEventArgs e)
    {
        var tab = CurrentTab;
        if (tab == null) return;
        var url = tab.ActiveUrl ?? "";
        var domain = Luma.Services.LumaShield.ExtractDomain(url);
        ShieldDomainText.Text = string.IsNullOrWhiteSpace(domain) ? "Текущий сайт" : domain;

        var isEnabled = Luma.Services.LumaShield.IsShieldEnabledForPage(url);
        var isProtected = Luma.Services.LumaShield.IsProtectedSite(url);

        if (isProtected)
        {
            ShieldStatusText.Text = "Доверенный сервис";
            ShieldBlockedCountText.Text = "Иммунитет активен (звонки и WebRTC)";
            ShieldToggleCheckBox.IsEnabled = false;
            ShieldToggleCheckBox.IsChecked = false;
        }
        else
        {
            ShieldToggleCheckBox.IsEnabled = true;
            ShieldToggleCheckBox.IsChecked = isEnabled;
            ShieldStatusText.Text = isEnabled ? "Защита активна" : "Защита отключена";
            ShieldBlockedCountText.Text = $"Заблокировано: {tab.ShieldBlockedCount}";
        }

        ShieldPopup.IsOpen = true;
    }

    private void ShieldToggle_Click(object sender, RoutedEventArgs e)
    {
        var tab = CurrentTab;
        if (tab == null) return;
        var url = tab.ActiveUrl ?? "";
        var enabled = ShieldToggleCheckBox.IsChecked == true;
        Luma.Services.LumaShield.SetShieldEnabledForPage(url, enabled);
        UpdateShieldState(tab);
        Reload_Click(sender, e);
    }

    public void UpdateShieldState(BrowserTab? tab = null)
    {
        tab ??= CurrentTab;
        if (tab == null || SidebarShieldBtn == null || SidebarShieldIcon == null) return;
        var url = tab.ActiveUrl ?? "";
        var isProtected = Luma.Services.LumaShield.IsProtectedSite(url);
        var isEnabled = Luma.Services.LumaShield.IsShieldEnabledForPage(url);

        if (isProtected)
        {
            SidebarShieldIcon.Fill = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA));
            SidebarShieldBtn.ToolTip = "Luma Shield: Доверенный сервис (звонки и WebRTC защищены)";
        }
        else if (isEnabled)
        {
            SidebarShieldIcon.Fill = new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA));
            SidebarShieldBtn.ToolTip = $"Luma Shield: Защита активна (заблокировано: {tab.ShieldBlockedCount})";
        }
        else
        {
            SidebarShieldIcon.Fill = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));
            SidebarShieldBtn.ToolTip = "Luma Shield: Защита отключена для этого сайта";
        }

        if (ShieldPopup != null && ShieldPopup.IsOpen && ShieldBlockedCountText != null)
        {
            ShieldBlockedCountText.Text = $"Заблокировано: {tab.ShieldBlockedCount}";
        }
    }

}
