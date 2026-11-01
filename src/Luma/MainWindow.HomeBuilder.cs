﻿using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Ellipse = System.Windows.Shapes.Ellipse;
using HAlign = System.Windows.HorizontalAlignment;
using IconPath = System.Windows.Shapes.Path;
using Image = System.Windows.Controls.Image;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;
using TextBox = System.Windows.Controls.TextBox;
using VAlign = System.Windows.VerticalAlignment;
using Point = System.Windows.Point;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DataObject = System.Windows.DataObject;
using DragEventArgs = System.Windows.DragEventArgs;

namespace Luma;

/// <summary>One widget placed on the start page.</summary>
internal sealed class HomeWidgetState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Type { get; set; } = "search";
    /// <summary>Width in quarters of the page column (1..4).</summary>
    public int Span { get; set; } = 4;
    public bool Visible { get; set; } = true;
    public string Data { get; set; } = "";
    public Dictionary<string, string> Opts { get; set; } = new();
    public double? X { get; set; }
    public double? Y { get; set; }
}

/// <summary>The whole start page: widgets in order plus the global look.</summary>
internal sealed class HomeLayoutState
{
    public List<HomeWidgetState> Widgets { get; set; } = new();
    public string VAlign { get; set; } = "center";
    public int Width { get; set; } = 680;
    public int Gap { get; set; } = 10;
    public string CardStyle { get; set; } = "filled";
    public int Radius { get; set; } = 14;
    public string Bg { get; set; } = "theme";
    public string BgColor { get; set; } = "#14151A";
    public string BgColor2 { get; set; } = "#3B1F5E";
    public int GradientAngle { get; set; } = 135;
    public int WallpaperBlur { get; set; } = 0;
}

internal sealed class HomeShortcut { public string T { get; set; } = ""; public string U { get; set; } = ""; }
internal sealed class HomePinnedSite
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Bg { get; set; } = "#FFFFFF";
    public string Fg { get; set; } = "#1E2024";
    public string LogoText { get; set; } = "";
    public string? BadgeBg { get; set; }
    public string? BadgeText { get; set; }
    public string? FaviconUrl { get; set; }
}
internal sealed class HomeTodoItem { public string T { get; set; } = ""; public bool D { get; set; } }

public partial class MainWindow
{
    private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private HomeLayoutState _homeLayout = new();
    private bool _homeEditMode;
    private bool _homeBuilderReady;
    private ScrollViewer? _homeScroll;
    private Grid? _homeScrollRoot;
    private Panel? _homeWrap;
    private StackPanel? _homeDock;
    private int _highestZIndex = 10;
    private string _homeHeadlineDefault = "";
    private readonly DispatcherTimer _homeSaveDebounce = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly List<(Panel Host, int Count)> _homeRecentPanels = new();
    private readonly List<Action> _homeStatRepaints = new();

    // Pomodoro keeps running across rebuilds, so its state lives on the window.
    private int _pomoRemaining = 25 * 60;
    private bool _pomoRunning;
    private bool _pomoBreak;
    private DispatcherTimer? _pomoTimer;
    private readonly List<Action> _pomoRepaints = new();

    private static readonly (string Type, string Ru, string En, string RuDesc, string EnDesc, string Icon, int Span, bool Single)[] HomeCatalog =
    [
        ("search", "Поиск", "Search", "Строка поиска и адреса", "Search and address bar", "Search", 4, true),
        ("clock", "Часы", "Clock", "Время и дата", "Time and date", "Clock", 4, true),
        ("greeting", "Приветствие", "Greeting", "Приветствие по времени суток", "Time-aware greeting", "Spark", 4, false),
        ("brand", "Логотип", "Logo", "Знак и название Luma", "Luma mark and name", "Star", 4, true),
        ("headline", "Заголовок", "Headline", "Крупная надпись над поиском", "Large line of text", "Pencil", 4, true),
        ("pinned", "Закрепленные сайты", "Pinned sites", "Экспресс-панель сайтов как в Opera", "Speed dial site cards like Opera", "Layers", 4, false),
        ("actions", "Быстрые действия", "Quick actions", "История, загрузки, настройки и другое", "History, downloads, settings and more", "Layers", 4, false),
        ("shortcuts", "Ярлыки", "Shortcuts", "Ваши сайты в один клик", "Your own sites in one click", "Link", 4, false),
        ("essentials", "Essentials", "Essentials", "Закреплённые сайты левой панели", "Sites pinned in the left panel", "Star", 4, true),
        ("recent", "Недавние сайты", "Recent sites", "Последние посещённые сайты", "Recently visited sites", "Clock", 4, false),
        ("media", "Медиа", "Media", "Плеер текущего трека", "Now playing card", "Music", 4, true),
        ("notes", "Заметки", "Notes", "Блокнот прямо на стартовой", "Scratchpad on the start page", "Note", 2, false),
        ("todo", "Задачи", "Tasks", "Список дел с галочками", "A to-do list", "Check", 2, false),
        ("pomodoro", "Таймер фокуса", "Focus timer", "Pomodoro: работа и перерыв", "Pomodoro: work and break", "Timer", 2, false),
        ("calendar", "Календарь", "Calendar", "Месяц с отметкой сегодняшнего дня", "Month view with today marked", "Grid", 2, false),
        ("stats", "Статистика", "Stats", "Вкладки, история, загрузки", "Tabs, history, downloads", "Chart", 2, false),
        ("text", "Текст", "Text", "Свой текст или цитата", "Your own text or quote", "Pencil", 4, false),
        ("spacer", "Отступ", "Spacer", "Пустое место между блоками", "Empty space between blocks", "Minus", 4, false),
        ("divider", "Разделитель", "Divider", "Тонкая линия", "A thin line", "Divider", 4, false),
    ];

    private static readonly (string Id, string Ru, string En, string Icon)[] HomeActionCatalog =
    [
        ("history", "История", "History", "Clock"),
        ("downloads", "Загрузки", "Downloads", "Download"),
        ("settings", "Настройки", "Settings", "Sliders"),
        ("account", "Профиль", "Profile", "User"),
        ("newwindow", "Новое окно", "New window", "Layers"),
        ("incognito", "Инкогнито", "Private", "Shield"),
    ];

    private bool Ru => EffectiveLanguage is "ru" or "uk";
    private string T(string ru, string en) => Ru ? ru : en;

    private string HomeWidgetName(string type)
    {
        var info = HomeCatalog.FirstOrDefault(c => c.Type == type);
        return info.Type is null ? type : T(info.Ru, info.En);
    }

    // ---------------------------------------------------------------- option helpers
    private static string O(HomeWidgetState w, string key, string def) => w.Opts.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : def;
    private static int OInt(HomeWidgetState w, string key, int def) => int.TryParse(O(w, key, ""), out var v) ? v : def;
    private static void SetO(HomeWidgetState w, string key, string value) => w.Opts[key] = value;

    // ---------------------------------------------------------------- lifecycle
    private void InitializeHomeBuilder()
    {
        if (HomeSurfaceContent is null || EmptyBackground is null) return;
        _homeHeadlineDefault = HomeHeadlineText?.Text ?? "";
        LoadHomeLayout();

        _homeSaveDebounce.Tick += (_, _) => { _homeSaveDebounce.Stop(); SaveHomeLayout(); };

        // The old fixed column of blocks is retired: its named children are re-parented into widgets on demand.
        var legacy = HomeSurfaceContent.Children.OfType<StackPanel>().FirstOrDefault();
        foreach (var el in HomeReusedElements()) DetachHomeElement(el);
        if (legacy is not null) legacy.Visibility = Visibility.Collapsed;

        HomeSurfaceContent.MaxWidth = double.PositiveInfinity;
        HomeSurfaceContent.HorizontalAlignment = HAlign.Stretch;
        HomeSurfaceContent.VerticalAlignment = VAlign.Stretch;
        HomeSurfaceContent.Margin = new Thickness(0);

        _homeWrap = new Canvas { HorizontalAlignment = HAlign.Left, VerticalAlignment = VAlign.Top, ClipToBounds = true };
        _homeScrollRoot = new Grid { HorizontalAlignment = HAlign.Left, VerticalAlignment = VAlign.Top, ClipToBounds = true };
        _homeScrollRoot.Children.Add(_homeWrap);
        _homeScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly,
            Content = _homeScrollRoot,
            Focusable = false,
            ClipToBounds = true
        };
        _homeScroll.SizeChanged += (_, _) =>
        {
            UpdateHomeCanvasBounds();
            if (!_homeEditMode)
            {
                RebuildHomeBuilder();
            }
        };
        HomeSurfaceContent.Children.Add(_homeScroll);

        BuildHomeDock();
        if (HomeCustomizeButton is not null) HomeCustomizeButton.Visibility = Visibility.Collapsed;
        _homeBuilderReady = true;
        RebuildHomeBuilder();
    }

    private IEnumerable<FrameworkElement> HomeReusedElements()
    {
        if (HomeSearchShell is not null) yield return HomeSearchShell;
        if (HomeClockPanel is not null) yield return HomeClockPanel;
        if (HomeBrandPanel is not null) yield return HomeBrandPanel;
        if (HomeHeadlineText is not null) yield return HomeHeadlineText;
        if (HomeMediaCard is not null) yield return HomeMediaCard;
    }

    private static void DetachHomeElement(FrameworkElement el)
    {
        switch (el.Parent)
        {
            case Panel p: p.Children.Remove(el); break;
            case Decorator d: d.Child = null; break;
            case ContentControl c: c.Content = null; break;
        }
    }

    private void LoadHomeLayout()
    {
        HomeLayoutState? parsed = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(_state.HomeLayoutJson))
                parsed = JsonSerializer.Deserialize<HomeLayoutState>(_state.HomeLayoutJson);
        }
        catch { parsed = null; }

        if (parsed is null)
        {
            parsed = HomePreset("minimal");
            if (!string.IsNullOrWhiteSpace(_state.HomeWallpaperPath)) parsed.Bg = "wallpaper";
        }
        _homeLayout = parsed;
        NormalizeHomeLayout();
    }

    private void NormalizeHomeLayout()
    {
        var l = _homeLayout;
        l.Widgets ??= new();
        l.Width = Math.Clamp(l.Width, 440, 1000);
        l.Gap = Math.Clamp(l.Gap, 2, 28);
        l.Radius = Math.Clamp(l.Radius, 0, 30);
        l.VAlign = l.VAlign == "top" ? "top" : "center";
        l.CardStyle = l.CardStyle is "outline" or "glass" or "flat" ? l.CardStyle : "filled";
        l.Bg = l.Bg is "solid" or "gradient" or "wallpaper" ? l.Bg : "theme";
        l.WallpaperBlur = Math.Clamp(l.WallpaperBlur, 0, 40);
        var seen = new HashSet<string>();
        l.Widgets.RemoveAll(w =>
        {
            var info = HomeCatalog.FirstOrDefault(c => c.Type == w.Type);
            if (info.Type is null) return true;
            return info.Single && !seen.Add(w.Type);
        });
        foreach (var w in l.Widgets)
        {
            int minSpan = (w.Type is "stats" or "pomodoro" or "calendar") ? 2 : 1;
            w.Span = Math.Clamp(w.Span, minSpan, 4);
            w.Opts ??= new();
            if (string.IsNullOrEmpty(w.Id)) w.Id = Guid.NewGuid().ToString("N")[..8];
        }
// User has full control over pinned widget presence
    }

    private void SaveHomeLayout()
    {
        try
        {
            _state.HomeLayoutJson = JsonSerializer.Serialize(_homeLayout);
            SaveState();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    private void QueueHomeSave() { _homeSaveDebounce.Stop(); _homeSaveDebounce.Start(); }

    private HomeLayoutState HomePreset(string id)
    {
        HomeWidgetState W(string type, int span = 4) => new() { Type = type, Span = span };
        var l = new HomeLayoutState();
        switch (id)
        {
            case "classic":
                l.Widgets = new() { W("brand"), W("clock"), W("headline"), W("search"), W("pinned", 4), W("actions") };
                break;
            case "work":
                l.Widgets = new() { W("greeting"), W("search"), W("pinned", 4), W("actions"), W("notes", 2), W("todo", 2), W("pomodoro", 2), W("calendar", 2), W("recent") };
                l.VAlign = "top"; l.Width = 860;
                break;
            case "focus":
                l.Widgets = new() { W("clock"), W("search"), W("pinned", 4), W("pomodoro", 4) };
                break;
            case "dash":
                l.Widgets = new() { W("greeting"), W("search"), W("pinned", 4), W("stats", 2), W("calendar", 2), W("recent") };
                l.VAlign = "top"; l.Width = 860;
                break;
            default:
                l.Widgets = new() { W("brand"), W("search"), W("pinned", 4) };
                break;
        }
        return l;
    }

    // ---------------------------------------------------------------- build
    internal void RebuildHomeBuilder()
    {
        if (!_homeBuilderReady || _homeWrap is null) return;
        if (_homeWrap is Canvas canvasRoot)
        {
            canvasRoot.Background = _homeEditMode ? CreateGridBrush(24.0) : Brushes.Transparent;
        }
        foreach (var el in HomeReusedElements()) DetachHomeElement(el);
        _homeWrap.Children.Clear();
        _homeRecentPanels.Clear();
        _pomoRepaints.Clear();
        _homeStatRepaints.Clear();

        var l = _homeLayout;
        double gap = l.Gap;

        double viewW = _homeScroll?.ActualWidth > 150 ? _homeScroll.ActualWidth : (_homeScroll?.ViewportWidth > 150 ? _homeScroll.ViewportWidth : 1200);
        double viewH = _homeScroll?.ActualHeight > 150 ? _homeScroll.ActualHeight : (_homeScroll?.ViewportHeight > 150 ? _homeScroll.ViewportHeight : 800);

        double effectiveW = Math.Min(l.Width, Math.Max(320.0, viewW - 32.0));
        double cw = Math.Max(40.0, (effectiveW - 3 * gap) / 4.0);
        double defaultStartX = Math.Max(16.0, Math.Round((viewW - effectiveW) / 2.0));
        double curY = l.VAlign == "top" ? 36.0 : Math.Max(28.0, Math.Round((viewH - 420.0) / 2.0));

        // Auto-assign default initial coordinates for any widgets missing X/Y
        const double GridSnap = 24.0;
        foreach (var w in l.Widgets)
        {
            if (!w.Visible && !_homeEditMode) continue;
            if (!w.X.HasValue || !w.Y.HasValue)
            {
                double wWidth = SpanWidth(w.Span, cw, gap);
                w.X = Math.Max(GridSnap, Math.Round((viewW - wWidth) / (GridSnap * 2.0)) * GridSnap);
                w.Y = Math.Round(curY / GridSnap) * GridSnap;
                curY += 80;
            }
        }

                double maxBottom = 0;
        foreach (var w in l.Widgets)
        {
            if (!w.Visible && !_homeEditMode) continue;
            var cell = BuildHomeCell(w, cw, gap);
            if (cell is not null)
            {
                // Respect the exact coordinates chosen by the user without artificial shifts
                double cellX = w.X ?? defaultStartX;
                double cellY = w.Y ?? 60.0;

                Canvas.SetLeft(cell, cellX);
                Canvas.SetTop(cell, cellY);
                _homeWrap.Children.Add(cell);
                maxBottom = Math.Max(maxBottom, cellY + 120.0);
            }
        }
if (_homeEditMode)
        {
            var addTile = BuildAddWidgetTile(effectiveW, gap);
            Canvas.SetLeft(addTile, defaultStartX);
            Canvas.SetTop(addTile, maxBottom + 24);
            _homeWrap.Children.Add(addTile);
        }
        else if (_homeWrap.Children.Count == 0)
        {
            var emptyTile = new Border
            {
                Width = effectiveW,
                Padding = new Thickness(0, 40, 0, 40),
                Child = Muted(T("Здесь пусто. Нажмите ✎ в правом нижнем углу, чтобы собрать свою стартовую.", "It is empty here. Press ✎ in the bottom right corner to build your own start page."), 13, true)
            };
            Canvas.SetLeft(emptyTile, defaultStartX);
            Canvas.SetTop(emptyTile, 120);
            _homeWrap.Children.Add(emptyTile);
        }

        UpdateHomeCanvasBounds();
        ApplyHomeLook();
        RefreshHomeDynamicWidgets();
        if (_homeStudioOpen) RenderHomeStudio();
    }

    private void UpdateHomeCanvasBounds()
    {
        if (_homeWrap is null) return;
        double viewW = _homeScroll?.ActualWidth > 100 ? _homeScroll.ActualWidth : (_homeScroll?.ViewportWidth > 100 ? _homeScroll.ViewportWidth : 1200);
        double viewH = _homeScroll?.ActualHeight > 100 ? _homeScroll.ActualHeight : (_homeScroll?.ViewportHeight > 100 ? _homeScroll.ViewportHeight : 800);
        double maxContentH = viewH;
        foreach (UIElement child in _homeWrap.Children)
        {
            if (child is FrameworkElement fe)
            {
                double top = Canvas.GetTop(fe);
                if (!double.IsNaN(top))
                {
                    double h = fe.ActualHeight > 0 ? fe.ActualHeight : (fe.Height > 0 ? fe.Height : 70.0);
                    maxContentH = Math.Max(maxContentH, top + h + 48.0);
                }
            }
        }
        _homeWrap.Width = viewW;
        _homeWrap.Height = maxContentH;
        if (_homeScrollRoot is not null)
        {
            _homeScrollRoot.Width = viewW;
            _homeScrollRoot.Height = maxContentH;
        }
    }

    private double SpanWidth(int span, double cw, double gap) => cw * span + (span - 1) * gap;

    private FrameworkElement? BuildHomeCell(HomeWidgetState w, double cw, double gap)
    {
        var content = BuildWidgetContent(w);
        if (content is null) return null;

        int span = w.Span;
        var cell = new Grid { Width = SpanWidth(span, cw, gap), VerticalAlignment = VAlign.Top };
        var host = new Grid { IsHitTestVisible = !_homeEditMode, Opacity = w.Visible ? 1 : 0.38 };
        host.Children.Add(content);
        cell.Children.Add(host);

        if (_homeEditMode)
        {
            cell.Background = Brushes.Transparent;
            cell.Cursor = Cursors.SizeAll;
            cell.Tag = w;

            var outline = new Border { CornerRadius = new CornerRadius(Math.Max(4, _homeLayout.Radius)), BorderThickness = new Thickness(1.5), IsHitTestVisible = false, Margin = new Thickness(-3) };
            outline.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            outline.Opacity = 0.55;
            cell.Children.Add(outline);
            cell.Children.Add(BuildWidgetToolbar(w));

            Point cellDragStart = default;
            double startX = 0;
            double startY = 0;
            bool cellMouseDown = false;
            bool isDragging = false;

            cell.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject dep)
                {
                    var b = FindVisualAncestor<Button>(dep);
                    if (b is not null && b.Tag as string != "Grip") return;
                }
                cellMouseDown = true;
                isDragging = false;
                cellDragStart = e.GetPosition(_homeWrap);
                startX = Canvas.GetLeft(cell);
                if (double.IsNaN(startX)) startX = w.X ?? 0;
                startY = Canvas.GetTop(cell);
                if (double.IsNaN(startY)) startY = w.Y ?? 0;
                cell.CaptureMouse();
                Panel.SetZIndex(cell, ++_highestZIndex);
            };

            cell.PreviewMouseMove += (_, e) =>
            {
                if (cellMouseDown && e.LeftButton == MouseButtonState.Pressed && _homeWrap is not null)
                {
                    var cur = e.GetPosition(_homeWrap);
                    var diff = cur - cellDragStart;
                    if (!isDragging && (Math.Abs(diff.X) > 3 || Math.Abs(diff.Y) > 3))
                    {
                        isDragging = true;
                        cell.Opacity = 0.90;
                        outline.BorderThickness = new Thickness(2);
                        outline.Opacity = 1;
                    }

                    if (isDragging)
                    {
                        // Ultra-smooth 1:1 real-time drag following the cursor perfectly without jitter
                        cell.RenderTransform = new TranslateTransform(diff.X, diff.Y);
                    }
                }
                else if (e.LeftButton != MouseButtonState.Pressed && cellMouseDown)
                {
                    cellMouseDown = false;
                    if (cell.IsMouseCaptured) cell.ReleaseMouseCapture();
                }
            };

            cell.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (cell.IsMouseCaptured) cell.ReleaseMouseCapture();

                if (cellMouseDown)
                {
                    cellMouseDown = false;
                    cell.Opacity = w.Visible ? 1 : 0.38;
                    outline.BorderThickness = new Thickness(1.5);
                    outline.Opacity = 0.55;

                    if (isDragging && _homeWrap is not null)
                    {
                        isDragging = false;
                        cell.RenderTransform = null;
                        var cur = e.GetPosition(_homeWrap);
                        var diff = cur - cellDragStart;

                        // Clean, precise snapping to 16px grid upon release
                        const double GridSnap = 16.0;
                        double viewW = _homeScroll?.ActualWidth > 100 ? _homeScroll.ActualWidth : 1200;
                        double widgetW = cell.ActualWidth > 0 ? cell.ActualWidth : (!double.IsNaN(cell.Width) ? cell.Width : 200);
                        double maxX = Math.Max(GridSnap, viewW - widgetW - GridSnap);

                        double finalX = Math.Clamp(Math.Round((startX + diff.X) / GridSnap) * GridSnap, GridSnap, maxX);
                        double finalY = Math.Max(GridSnap, Math.Round((startY + diff.Y) / GridSnap) * GridSnap);

                        w.X = finalX;
                        w.Y = finalY;
                        Canvas.SetLeft(cell, finalX);
                        Canvas.SetTop(cell, finalY);

                        UpdateHomeCanvasBounds();
                        SaveHomeLayout();
                    }
                    else
                    {
                        // Simple click without movement: expand in studio
                        if (_homeStudioOpen)
                        {
                            _studioExpanded = w.Id;
                            _studioTab = "widgets";
                            RenderHomeStudio();
                        }
                    }

                    // Restore panels and docks
                    if (_homeEditMode)
                    {
                        UpdateHomeDockVisibility();
                        if (_homeStudioOpen && HomeCustomizeDrawerOverlay is not null)
                        {
                            HomeCustomizeDrawerOverlay.Visibility = Visibility.Visible;
                        }
                    }
                }
            };
        }
        return cell;
    }

    private FrameworkElement BuildWidgetToolbar(HomeWidgetState w)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        Button Mini(string icon, string tip, Action act) =>
            HBtn(HIcon(icon, 11), act, tip, "Zen_3D3D42", "Zen_56565A", 6, new Thickness(0), 22, 22);

        // Dedicated drag grip button
        var grip = HBtn(HIcon("Grid", 11), () => { }, T("Перетащить виджет", "Drag to move"), "Zen_3D3D42", "Zen_56565A", 6, new Thickness(0), 22, 22);
        grip.Tag = "Grip";
        grip.Cursor = Cursors.SizeAll;
        bar.Children.Add(grip);

        bar.Children.Add(Mini("Left", T("Сдвинуть раньше", "Move earlier"), () => MoveHomeWidget(w, -1)));
        bar.Children.Add(Mini("Right", T("Сдвинуть позже", "Move later"), () => MoveHomeWidget(w, +1)));
        bar.Children.Add(Mini("Minus", T("Уже", "Narrower"), () => { int minSpan = (w.Type is "stats" or "pomodoro" or "calendar") ? 2 : 1; w.Span = Math.Max(minSpan, w.Span - 1); HomeLayoutChanged(); }));
        bar.Children.Add(Mini("Plus", T("Шире", "Wider"), () => { w.Span = Math.Min(4, w.Span + 1); HomeLayoutChanged(); }));
        bar.Children.Add(Mini("Eye", w.Visible ? T("Скрыть", "Hide") : T("Показать", "Show"), () => { w.Visible = !w.Visible; HomeLayoutChanged(); }));
        if (w.Type is not ("brand" or "media" or "divider" or "stats" or "essentials"))
        {
            Button? gear = null;
            gear = Mini("Sliders", T("Параметры виджета", "Widget options"), () => ShowWidgetSettings(w, gear!));
            bar.Children.Add(gear);
        }
        bar.Children.Add(Mini("Trash", T("Удалить", "Remove"), () => { _homeLayout.Widgets.Remove(w); HomeLayoutChanged(); }));

        var shell = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(3),
            HorizontalAlignment = HAlign.Right,
            VerticalAlignment = VAlign.Top,
            Margin = new Thickness(0, -15, 6, 0),
            BorderThickness = new Thickness(1),
            Child = bar,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black }
        };
        shell.SetResourceReference(Border.BackgroundProperty, "Zen_3D3D42");
        shell.SetResourceReference(Border.BorderBrushProperty, "Zen_505056");
        Panel.SetZIndex(shell, 20);
        return shell;
    }

    private void ReorderHomeWidgets(string srcId, string dstId)
    {
        var list = _homeLayout.Widgets;
        var src = list.FirstOrDefault(x => x.Id == srcId);
        var dst = list.FirstOrDefault(x => x.Id == dstId);
        if (src is null || dst is null || ReferenceEquals(src, dst)) return;
        var srcIdx = list.IndexOf(src);
        var dstIdx = list.IndexOf(dst);
        if (srcIdx < 0 || dstIdx < 0) return;
        list.RemoveAt(srcIdx);
        list.Insert(dstIdx, src);
        HomeLayoutChanged();
    }

    private void MoveHomeWidget(HomeWidgetState w, int delta)
    {
        var list = _homeLayout.Widgets;
        var i = list.IndexOf(w);
        var j = Math.Clamp(i + delta, 0, list.Count - 1);
        if (i < 0 || i == j) return;
        list.RemoveAt(i);
        list.Insert(j, w);
        HomeLayoutChanged();
    }

    private void HomeLayoutChanged()
    {
        NormalizeHomeLayout();
        SaveHomeLayout();
        RebuildHomeBuilder();
    }

    private FrameworkElement BuildAddWidgetTile(double width, double gap)
    {
        var inner = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HAlign.Center, VerticalAlignment = VAlign.Center };
        inner.Children.Add(HIcon("Plus", 14, "ZenMutedBrush"));
        var label = Muted(T("Добавить виджет", "Add widget"), 12.5);
        label.Margin = new Thickness(8, 0, 0, 0);
        inner.Children.Add(label);
        Button? btn = null;
        btn = HBtn(inner, () => ShowWidgetGallery(btn!), null, null, "ZenCardBrush", Math.Max(6, _homeLayout.Radius - 2), new Thickness(0), 44);
        btn.Width = width;
        btn.Margin = new Thickness(0, 4, 0, 0);
        return btn;
    }

    // ---------------------------------------------------------------- card chrome
    /// <summary>Wraps content in a card that follows the chosen card style and theme.</summary>
    private Border HCard(UIElement child, Thickness? padding = null)
    {
        var b = new Border { CornerRadius = new CornerRadius(_homeLayout.Radius), Padding = padding ?? new Thickness(14), Child = child };
        switch (_homeLayout.CardStyle)
        {
            case "outline":
                b.Background = Brushes.Transparent;
                b.BorderThickness = new Thickness(1.2);
                b.SetResourceReference(Border.BorderBrushProperty, "ZenCardBorderBrush");
                break;
            case "glass":
                b.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
                b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF));
                b.BorderThickness = new Thickness(1);
                break;
            case "flat":
                b.SetResourceReference(Border.BackgroundProperty, "ZenInputBrush");
                break;
            default:
                b.SetResourceReference(Border.BackgroundProperty, "ZenCardBrush");
                break;
        }
        return b;
    }

    private Button HomeCardButton(UIElement content, Action onClick, string? tip = null, double? height = null)
    {
        string normal = _homeLayout.CardStyle switch { "outline" => "", "glass" => "", "flat" => "ZenInputBrush", _ => "ZenCardBrush" };
        var btn = HBtn(content, onClick, tip, normal.Length == 0 ? null : normal, "ZenCardHoverBrush", _homeLayout.Radius, new Thickness(12, 0, 12, 0), height);
        if (btn.Content is Border bd)
        {
            if (_homeLayout.CardStyle == "outline")
            {
                bd.BorderThickness = new Thickness(1.2);
                bd.SetResourceReference(Border.BorderBrushProperty, "ZenCardBorderBrush");
            }
            else if (_homeLayout.CardStyle == "glass")
            {
                bd.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
                bd.BorderBrush = new SolidColorBrush(Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF));
                bd.BorderThickness = new Thickness(1);
            }
        }
        return btn;
    }

    // ---------------------------------------------------------------- widgets
    private FrameworkElement? BuildWidgetContent(HomeWidgetState w)
    {
        switch (w.Type)
        {
            case "search":
            {
                var s = HomeSearchShell;
                s.Width = double.NaN;
                s.Margin = new Thickness(0);
                s.HorizontalAlignment = HAlign.Stretch;
                s.Height = Math.Clamp(OInt(w, "h", 50), 40, 76);
                var ph = O(w, "ph", "");
                HomeSearchPlaceholder.Text = ph.Length > 0 ? ph : T("Поиск в интернете или адрес сайта...", "Search the web or enter an address...");
                return s;
            }
            case "clock":
            {
                HomeClockPanel.Margin = new Thickness(0);
                HomeClockPanel.HorizontalAlignment = HAlign.Center;
                HomeClockText.FontSize = Math.Clamp(OInt(w, "size", 64), 28, 140);
                HomeDateText.Visibility = O(w, "date", "1") == "1" ? Visibility.Visible : Visibility.Collapsed;
                UpdateHomeClock();
                return HomeClockPanel;
            }
            case "brand":
                HomeBrandPanel.Margin = new Thickness(0);
                HomeBrandPanel.HorizontalAlignment = HAlign.Center;
                return HomeBrandPanel;
            case "headline":
            {
                HomeHeadlineText.Margin = new Thickness(0);
                HomeHeadlineText.HorizontalAlignment = HAlign.Center;
                HomeHeadlineText.TextAlignment = TextAlignment.Center;
                HomeHeadlineText.TextWrapping = TextWrapping.Wrap;
                HomeHeadlineText.FontSize = Math.Clamp(OInt(w, "size", 26), 14, 64);
                var custom = O(w, "text", "");
                HomeHeadlineText.Text = custom.Length > 0 ? custom : _homeHeadlineDefault;
                HomeHeadlineText.Visibility = Visibility.Visible;
                return HomeHeadlineText;
            }
            case "media":
            {
                var g = new Grid();
                HomeMediaCard.Width = double.NaN;
                HomeMediaCard.Margin = new Thickness(0);
                g.Children.Add(HomeMediaCard);
                if (_homeEditMode && HomeMediaCard.Visibility != Visibility.Visible)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HAlign.Center, VerticalAlignment = VAlign.Center };
                    row.Children.Add(HIcon("Music", 15, "ZenMutedBrush"));
                    var t = Muted(T("Здесь появится плеер, когда что-то заиграет", "The player shows up here when something plays"), 12);
                    t.Margin = new Thickness(10, 0, 0, 0);
                    row.Children.Add(t);
                    var card = HCard(row, new Thickness(14));
                    card.Height = 54;
                    g.Children.Add(card);
                }
                return g;
            }
            case "greeting": return BuildGreeting(w);
            case "pinned": return BuildPinnedSites(w);
            case "actions": return BuildActions(w);
            case "shortcuts": return BuildShortcuts(w);
            case "essentials": return BuildEssentials();
            case "recent": return BuildRecent(w);
            case "notes": return BuildNotes(w);
            case "todo": return BuildTodo(w);
            case "pomodoro": return BuildPomodoro(w);
            case "calendar": return BuildCalendar(w);
            case "stats": return BuildStats();
            case "text":
            {
                var tb = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(w.Data) ? T("Ваш текст", "Your text") : w.Data,
                    FontSize = Math.Clamp(OInt(w, "size", 16), 10, 72),
                    FontWeight = O(w, "bold", "0") == "1" ? FontWeights.SemiBold : FontWeights.Normal,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = O(w, "align", "center") switch { "left" => TextAlignment.Left, "right" => TextAlignment.Right, _ => TextAlignment.Center },
                    HorizontalAlignment = HAlign.Stretch
                };
                tb.SetResourceReference(TextBlock.ForegroundProperty, "ZenTextBrush");
                return tb;
            }
            case "spacer":
            {
                var sp = new Border { Height = Math.Clamp(OInt(w, "h", 32), 4, 240) };
                return sp;
            }
            case "divider":
            {
                var line = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6) };
                line.SetResourceReference(Border.BackgroundProperty, "ZenCardBorderBrush");
                return line;
            }
        }
        return null;
    }

    private TextBlock Muted(string text, double size = 12, bool center = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, TextAlignment = center ? TextAlignment.Center : TextAlignment.Left };
        t.SetResourceReference(TextBlock.ForegroundProperty, "ZenMutedBrush");
        return t;
    }

    private TextBlock Txt(string text, double size = 12.5, bool semibold = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis };
        if (semibold) t.FontWeight = FontWeights.SemiBold;
        t.SetResourceReference(TextBlock.ForegroundProperty, "ZenTextBrush");
        return t;
    }

    private FrameworkElement BuildGreeting(HomeWidgetState w)
    {
        var hour = DateTime.Now.Hour;
        var hello = hour switch
        {
            < 5 => T("Доброй ночи", "Good night"),
            < 12 => T("Доброе утро", "Good morning"),
            < 18 => T("Добрый день", "Good afternoon"),
            _ => T("Добрый вечер", "Good evening")
        };
        var name = _auth.CurrentUser?.DisplayName;
        var text = O(w, "name", "1") == "1" && !string.IsNullOrWhiteSpace(name) ? $"{hello}, {name}" : hello;
        var tb = Txt(text, Math.Clamp(OInt(w, "size", 26), 14, 56));
        tb.TextAlignment = TextAlignment.Center;
        tb.HorizontalAlignment = HAlign.Center;
        tb.TextWrapping = TextWrapping.Wrap;
        tb.TextTrimming = TextTrimming.None;
        return tb;
    }

    private void RunHomeAction(string id)
    {
        var args = new RoutedEventArgs();
        switch (id)
        {
            case "downloads": Downloads_Click(this, args); break;
            case "history": History_Click(this, args); break;
            case "settings": Settings_Click(this, args); break;
            case "account": Account_Click(this, args); break;
            case "newwindow": NewWindow_Click(this, args); break;
            case "incognito": NewPrivateWindow_Click(this, args); break;
        }
    }

    private FrameworkElement BuildActions(HomeWidgetState w)
    {
        var ids = O(w, "items", "history,downloads,settings,account").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => HomeActionCatalog.Any(a => a.Id == id)).ToList();
        if (ids.Count == 0) return Muted(T("Выберите действия в параметрах виджета", "Pick actions in the widget options"), 12, true);
        bool iconsOnly = O(w, "mode", "labels") == "icons";
        var grid = new UniformGrid { Rows = 1, Columns = ids.Count };
        foreach (var id in ids)
        {
            var info = HomeActionCatalog.First(a => a.Id == id);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HAlign.Center, VerticalAlignment = VAlign.Center };
            row.Children.Add(HIcon(info.Icon, 15));
            if (!iconsOnly)
            {
                var t = Txt(T(info.Ru, info.En), 12.5);
                t.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(t);
            }
            var btn = HomeCardButton(row, () => RunHomeAction(id), T(info.Ru, info.En), 40);
            btn.Margin = new Thickness(0, 0, id == ids[^1] ? 0 : 6, 0);
            grid.Children.Add(btn);
        }
        return grid;
    }

    private FrameworkElement SiteTile(string title, string subtitle, string faviconUrl, Action onClick)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 11, 0, 10) };
        var badge = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), HorizontalAlignment = HAlign.Left };
        badge.SetResourceReference(Border.BackgroundProperty, "Zen_4B4B50");
        var img = new Image { Width = 16, Height = 16, HorizontalAlignment = HAlign.Center, VerticalAlignment = VAlign.Center };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        try { img.Source = new BitmapImage(new Uri(faviconUrl)); } catch { }
        badge.Child = img;
        stack.Children.Add(badge);
        var t = Txt(title, 12.5, true);
        t.Margin = new Thickness(0, 9, 0, 0);
        stack.Children.Add(t);
        if (!string.IsNullOrEmpty(subtitle))
        {
            var s = Muted(subtitle, 10.5);
            s.TextTrimming = TextTrimming.CharacterEllipsis;
            s.TextWrapping = TextWrapping.NoWrap;
            s.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(s);
        }
        return HomeCardButton(stack, onClick, title);
    }

    internal static List<HomePinnedSite> GetDefaultPinnedSites() => [];

    internal static List<HomePinnedSite> ReadPinnedSites(HomeWidgetState w)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(w.Data)) return [];
            var list = JsonSerializer.Deserialize<List<HomePinnedSite>>(w.Data);
            return list ?? [];
        }
        catch { return []; }
    }

    private static readonly string FaviconsDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma", "Favicons");

    internal static void LoadSiteIcon(Image imgTarget, string url, string? faviconUrl = null)
    {
        try
        {
            string iconUrl = !string.IsNullOrWhiteSpace(faviconUrl) && (faviconUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) || faviconUrl.StartsWith("pack:", StringComparison.OrdinalIgnoreCase) || faviconUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                ? faviconUrl
                : BrowserTab.FaviconOf(url);

            if (string.IsNullOrWhiteSpace(iconUrl)) return;

            System.IO.Directory.CreateDirectory(FaviconsDir);
            string cacheKey = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(iconUrl))).ToLowerInvariant();
            string cachedPath = System.IO.Path.Combine(FaviconsDir, $"{cacheKey}.png");

            if (System.IO.File.Exists(cachedPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(cachedPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    imgTarget.Source = bmp;
                    return;
                }
                catch { }
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
                    var bytes = await client.GetByteArrayAsync(iconUrl);
                    await System.IO.File.WriteAllBytesAsync(cachedPath, bytes);

                    await imgTarget.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.UriSource = new Uri(cachedPath, UriKind.Absolute);
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.EndInit();
                            bmp.Freeze();
                            imgTarget.Source = bmp;
                        }
                        catch { }
                    });
                }
                catch { }
            });
        }
        catch { }
    }


    private FrameworkElement BuildPinnedTile(HomeWidgetState w, HomePinnedSite site, List<HomePinnedSite> list, Action onClick)
    {
        string title = !string.IsNullOrWhiteSpace(site.Title) ? site.Title : "Сайт";

        var cardBorder = new Border
        {
            Width = 68,
            Height = 68,
            Margin = new Thickness(8),
            CornerRadius = new CornerRadius(18),
            Background = RawBrush("#1B1B1F"),
            BorderBrush = RawBrush("#24FFFFFF"),
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true,
            ClipToBounds = true,
            Cursor = Cursors.Hand,
            ToolTip = title
        };

        var cardGrid = new Grid();

        // Subtle Globe vector fallback (no raw numbers or letters!)
        var fallbackPath = new System.Windows.Shapes.Path
        {
            Width = 24,
            Height = 24,
            Stretch = System.Windows.Media.Stretch.Uniform,
            Stroke = RawBrush("#4B4B52"),
            StrokeThickness = 1.5,
            Data = Geometry.Parse("M12 2a10 10 0 1 0 10 10A10 10 0 0 0 12 2zm0 0a15 15 0 0 1 4 10 15 15 0 0 1-4 10 15 15 0 0 1-4-10 15 15 0 0 1 4-10zm-9 10h18"),
            HorizontalAlignment = HAlign.Center,
            VerticalAlignment = VAlign.Center
        };
        cardGrid.Children.Add(fallbackPath);

        // Favicon Image
        var img = new Image
        {
            Width = 34,
            Height = 34,
            Stretch = System.Windows.Media.Stretch.Uniform,
            HorizontalAlignment = HAlign.Center,
            VerticalAlignment = VAlign.Center
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        LoadSiteIcon(img, site.Url, site.FaviconUrl);
        cardGrid.Children.Add(img);

        // Delete button visible on hover or in edit mode
        var delBtn = new Button
        {
            Template = CreateSimpleBorderButtonTemplate(),
            Width = 20,
            Height = 20,
            HorizontalAlignment = HAlign.Right,
            VerticalAlignment = VAlign.Top,
            Margin = new Thickness(0, 4, 4, 0),
            Cursor = Cursors.Hand,
            ToolTip = T("Удалить с главной", "Delete site"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Visibility = _homeEditMode ? Visibility.Visible : Visibility.Collapsed
        };
        var delBd = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x14, 0x14, 0x18)),
            Child = HIcon("X", 8, "ZenTextBrush")
        };
        delBtn.Content = delBd;
        delBtn.Click += (_, e) =>
        {
            e.Handled = true;
            list.Remove(site);
            w.Data = JsonSerializer.Serialize(list);
            HomeLayoutChanged();
            ShowToast("Удалено с главной", title);
        };
        cardGrid.Children.Add(delBtn);

        cardBorder.Child = cardGrid;

        cardBorder.MouseEnter += (_, _) =>
        {
            cardBorder.Background = RawBrush("#26262D");
            cardBorder.BorderBrush = RawBrush("#48FFFFFF");
            if (!_homeEditMode) delBtn.Visibility = Visibility.Visible;
        };
        cardBorder.MouseLeave += (_, _) =>
        {
            cardBorder.Background = RawBrush("#1B1B1F");
            cardBorder.BorderBrush = RawBrush("#24FFFFFF");
            if (!_homeEditMode) delBtn.Visibility = Visibility.Collapsed;
        };

        cardBorder.MouseLeftButtonUp += (_, e) =>
        {
            if (_homeEditMode) return;
            onClick();
        };

        // Right-click context menu
        cardBorder.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            var menu = NewMenu();
            Add(menu, "Открыть", () => onClick());
            Add(menu, "Открыть в новой вкладке", async () => await AddTabAsync(site.Url));
            Add(menu, "Скопировать ссылку", () => { try { System.Windows.Clipboard.SetText(site.Url); ShowToast("Ссылка скопирована", site.Url); } catch { } });
            Sep(menu);
            Add(menu, "Удалить с главной страницы", () =>
            {
                list.Remove(site);
                w.Data = JsonSerializer.Serialize(list);
                HomeLayoutChanged();
                ShowToast("Удалено с главной", title);
            });
            OpenMenu(menu);
        };

        return cardBorder;
    }

    private FrameworkElement BuildPinnedSites(HomeWidgetState w)
    {
        var list = ReadPinnedSites(w);
        if (list.Count == 0)
        {
            return new Grid { Visibility = Visibility.Collapsed };
        }

        var wrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HAlign.Center,
            MaxWidth = 850
        };

        foreach (var site in list)
        {
            var tile = BuildPinnedTile(w, site, list, () => _ = AddTabAsync(Normalize(site.Url)));
            wrap.Children.Add(tile);
        }

        // We do NOT add BuildAddPinnedTile - user pins sites via tab right-click!
        return wrap;
    }

    public void PinTabToHomePage(BrowserTab tab)
    {
        if (tab.IsHome || string.IsNullOrWhiteSpace(tab.ActiveUrl)) return;

        var pinnedWidget = _homeLayout.Widgets.FirstOrDefault(x => x.Type == "pinned");
        if (pinnedWidget is null)
        {
            pinnedWidget = new HomeWidgetState { Type = "pinned", Span = 4, Visible = true };
            var searchIdx = _homeLayout.Widgets.FindIndex(x => x.Type == "search");
            if (searchIdx >= 0)
                _homeLayout.Widgets.Insert(searchIdx + 1, pinnedWidget);
            else
                _homeLayout.Widgets.Add(pinnedWidget);
        }
        else
        {
            pinnedWidget.Visible = true;
        }

        var list = ReadPinnedSites(pinnedWidget);
        var existing = list.FirstOrDefault(x => SameSite(x.Url, tab.ActiveUrl) || string.Equals(x.Url, tab.ActiveUrl, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ShowToast("Главная страница", $"«{tab.DisplayTitle}» уже закреплён на главной");
            return;
        }

        var domain = BrowserTab.DomainOf(tab.ActiveUrl);
        var title = string.IsNullOrWhiteSpace(tab.DisplayTitle) ? domain : tab.DisplayTitle;

        var favicon = !string.IsNullOrWhiteSpace(tab.Favicon) ? tab.Favicon : tab.FaviconUrl;
        var newSite = new HomePinnedSite
        {
            Title = title,
            Url = tab.ActiveUrl,
            FaviconUrl = favicon,
            Bg = "#1A1A1E",
            Fg = "#FFFFFF",
            LogoText = title.Length > 12 ? title[..10] + ".." : title
        };

        list.Add(newSite);
        pinnedWidget.Data = JsonSerializer.Serialize(list);
        HomeLayoutChanged();

        ShowToast("Закреплено на главной", $"«{title}» добавлена на домашнюю страницу");
    }

    private static List<HomeShortcut> ReadShortcuts(HomeWidgetState w)
    {
        try { return string.IsNullOrWhiteSpace(w.Data) ? new() : JsonSerializer.Deserialize<List<HomeShortcut>>(w.Data) ?? new(); }
        catch { return new(); }
    }

    private FrameworkElement BuildShortcuts(HomeWidgetState w)
    {
        var items = ReadShortcuts(w);
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(-3, -3, -3, -3) };
        foreach (var item in items)
        {
            var tile = SiteTile(string.IsNullOrWhiteSpace(item.T) ? BrowserTab.DomainOf(item.U) : item.T, BrowserTab.DomainOf(item.U), BrowserTab.FaviconOf(item.U), () => _ = AddTabAsync(Normalize(item.U)));
            var holder = new Grid { Margin = new Thickness(3) };
            holder.Children.Add(tile);
            if (_homeEditMode)
            {
                var remove = HBtn(HIcon("X", 10), () => { items.Remove(item); w.Data = JsonSerializer.Serialize(items); HomeLayoutChanged(); }, T("Убрать ярлык", "Remove shortcut"), "Zen_3D3D42", "Zen_56565A", 6, new Thickness(0), 20, 20);
                remove.HorizontalAlignment = HAlign.Right;
                remove.VerticalAlignment = VAlign.Top;
                remove.Margin = new Thickness(0, 4, 4, 0);
                remove.IsHitTestVisible = true;
                holder.Children.Add(remove);
            }
            grid.Children.Add(holder);
        }

        Button? add = null;
        var plus = new StackPanel { Margin = new Thickness(0, 11, 0, 10), VerticalAlignment = VAlign.Center, HorizontalAlignment = HAlign.Center };
        plus.Children.Add(HIcon("Plus", 16, "ZenMutedBrush"));
        var lbl = Muted(T("Ярлык", "Shortcut"), 11);
        lbl.Margin = new Thickness(0, 6, 0, 0);
        plus.Children.Add(lbl);
        add = HomeCardButton(plus, () => ShowShortcutEditor(w, add!), T("Добавить ярлык", "Add shortcut"));
        var addHolder = new Grid { Margin = new Thickness(3) };
        addHolder.Children.Add(add);
        // The add tile is always clickable, even in edit mode where the rest of the widget is inert.
        if (items.Count < 12) grid.Children.Add(addHolder);
        return grid;
    }

    private FrameworkElement BuildEssentials()
    {
        var list = _state.Essentials ?? new();
        if (list.Count == 0) return HCard(Muted(T("Закрепите сайты в левой панели — они появятся здесь.", "Pin sites in the left panel — they will show up here."), 12, true));
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(-3) };
        foreach (var e in list)
        {
            var holder = new Grid { Margin = new Thickness(3) };
            var essential = e;
            holder.Children.Add(SiteTile(essential.Title, BrowserTab.DomainOf(essential.Url), BrowserTab.FaviconOf(essential.Url), () => _ = ActivateOrOpenEssentialAsync(essential)));
            grid.Children.Add(holder);
        }
        return grid;
    }

    private FrameworkElement BuildRecent(HomeWidgetState w)
    {
        var count = Math.Clamp(OInt(w, "count", 4), 4, 12);
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(-3) };
        _homeRecentPanels.Add((grid, count));
        PopulateRecent(grid, count);
        return grid;
    }

    private void PopulateRecent(Panel host, int count)
    {
        host.Children.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<HistoryEntry>();
        foreach (var h in _state.History.OrderByDescending(x => x.VisitedAt))
        {
            if (!Uri.TryCreate(h.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !seen.Add(uri.Host)) continue;
            items.Add(h);
            if (items.Count >= count) break;
        }
        if (items.Count == 0)
        {
            if (host is UniformGrid ug) ug.Columns = 1;
            host.Children.Add(HCard(Muted(T("Пока нет посещённых сайтов.", "No visited sites yet."), 12, true)));
            return;
        }
        if (host is UniformGrid grid) grid.Columns = 4;
        foreach (var h in items)
        {
            var holder = new Grid { Margin = new Thickness(3) };
            var url = h.Url;
            holder.Children.Add(SiteTile(string.IsNullOrWhiteSpace(h.Title) ? h.Domain : h.Title, h.Domain, h.FaviconUrl, () => _ = AddTabAsync(url)));
            host.Children.Add(holder);
        }
    }

    /// <summary>Called when the start page is shown again so history-based widgets are current.</summary>
    internal void RefreshHomeDynamicWidgets()
    {
        foreach (var (host, count) in _homeRecentPanels) PopulateRecent(host, count);
        foreach (var repaint in _homeStatRepaints) repaint();
    }

    private FrameworkElement BuildNotes(HomeWidgetState w)
    {
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(HIcon("Note", 14, "ZenMutedBrush"));
        var title = Muted(T("Заметки", "Notes"), 11.5);
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(8, 0, 0, 0);
        head.Children.Add(title);
        stack.Children.Add(head);

        var box = new TextBox
        {
            Text = w.Data,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            FontSize = 13,
            Height = Math.Clamp(OInt(w, "h", 150), 80, 420)
        };
        box.SetResourceReference(TextBox.ForegroundProperty, "ZenTextBrush");
        box.SetResourceReference(TextBox.CaretBrushProperty, "ZenTextBrush");
        var placeholder = Muted(T("Пишите здесь — всё сохраняется само", "Type here — it saves itself"), 13);
        placeholder.IsHitTestVisible = false;
        placeholder.Visibility = string.IsNullOrEmpty(w.Data) ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) =>
        {
            w.Data = box.Text;
            placeholder.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
            QueueHomeSave();
        };
        var holder = new Grid();
        holder.Children.Add(box);
        holder.Children.Add(placeholder);
        stack.Children.Add(holder);
        return HCard(stack);
    }

    private static List<HomeTodoItem> ReadTodos(HomeWidgetState w)
    {
        try { return string.IsNullOrWhiteSpace(w.Data) ? new() : JsonSerializer.Deserialize<List<HomeTodoItem>>(w.Data) ?? new(); }
        catch { return new(); }
    }

    private FrameworkElement BuildTodo(HomeWidgetState w)
    {
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(HIcon("Check", 14, "ZenMutedBrush"));
        var title = Muted(T("Задачи", "Tasks"), 11.5);
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(8, 0, 0, 0);
        head.Children.Add(title);
        stack.Children.Add(head);

        var list = new StackPanel();
        stack.Children.Add(list);
        var items = ReadTodos(w);

        void Persist() { w.Data = JsonSerializer.Serialize(items); QueueHomeSave(); }
        void Render()
        {
            list.Children.Clear();
            foreach (var item in items.ToList())
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4), Background = Brushes.Transparent };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var check = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5), Cursor = Cursors.Hand, HorizontalAlignment = HAlign.Left, VerticalAlignment = VAlign.Center };
                check.SetResourceReference(Border.BorderBrushProperty, item.D ? "AccentBrush" : "ZenMutedBrush");
                if (item.D)
                {
                    check.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    check.Child = HIcon("Check", 10, "ZenTextBrush", 2);
                }
                check.MouseLeftButtonUp += (_, _) => { item.D = !item.D; Persist(); Render(); };
                row.Children.Add(check);

                var text = Txt(item.T, 12.5);
                text.TextWrapping = TextWrapping.Wrap;
                text.TextTrimming = TextTrimming.None;
                text.VerticalAlignment = VAlign.Center;
                if (item.D) { text.TextDecorations = TextDecorations.Strikethrough; text.Opacity = 0.5; }
                Grid.SetColumn(text, 1);
                row.Children.Add(text);

                var del = HBtn(HIcon("X", 9, "ZenMutedBrush"), () => { items.Remove(item); Persist(); Render(); }, null, null, "Zen_4B4B50", 6, new Thickness(0), 20, 20);
                del.Opacity = 0;
                row.MouseEnter += (_, _) => del.Opacity = 1;
                row.MouseLeave += (_, _) => del.Opacity = 0;
                Grid.SetColumn(del, 2);
                row.Children.Add(del);
                list.Children.Add(row);
            }
            if (items.Count == 0)
            {
                var empty = Muted(T("Список пуст", "Nothing here yet"), 12);
                empty.Margin = new Thickness(0, 0, 0, 6);
                list.Children.Add(empty);
            }
        }
        Render();

        var inputShell = new Border { CornerRadius = new CornerRadius(Math.Max(4, _homeLayout.Radius - 5)), Padding = new Thickness(10, 0, 10, 0), Height = 32, Margin = new Thickness(0, 6, 0, 0) };
        inputShell.SetResourceReference(Border.BackgroundProperty, "Zen_4B4B50");
        var input = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 12.5, VerticalContentAlignment = VAlign.Center, MaxLength = 140 };
        input.SetResourceReference(TextBox.ForegroundProperty, "ZenTextBrush");
        input.SetResourceReference(TextBox.CaretBrushProperty, "ZenTextBrush");
        var hint = Muted(T("Новая задача — Enter", "New task — Enter"), 12);
        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = VAlign.Center;
        input.TextChanged += (_, _) => hint.Visibility = string.IsNullOrEmpty(input.Text) ? Visibility.Visible : Visibility.Collapsed;
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Text) || items.Count >= 30) return;
            items.Add(new HomeTodoItem { T = input.Text.Trim() });
            input.Clear();
            Persist();
            Render();
            e.Handled = true;
        };
        var ig = new Grid();
        ig.Children.Add(input);
        ig.Children.Add(hint);
        inputShell.Child = ig;
        stack.Children.Add(inputShell);
        return HCard(stack);
    }

    private int PomoWorkMinutes() => Math.Clamp(OInt(_homeLayout.Widgets.FirstOrDefault(x => x.Type == "pomodoro") ?? new HomeWidgetState(), "work", 25), 1, 180);
    private int PomoBreakMinutes() => Math.Clamp(OInt(_homeLayout.Widgets.FirstOrDefault(x => x.Type == "pomodoro") ?? new HomeWidgetState(), "break", 5), 1, 60);

    private void EnsurePomoTimer()
    {
        if (_pomoTimer is not null) return;
        _pomoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pomoTimer.Tick += (_, _) =>
        {
            if (!_pomoRunning) return;
            _pomoRemaining--;
            if (_pomoRemaining <= 0)
            {
                _pomoBreak = !_pomoBreak;
                _pomoRemaining = (_pomoBreak ? PomoBreakMinutes() : PomoWorkMinutes()) * 60;
                ShowToast(_pomoBreak ? T("Перерыв", "Break time") : T("Фокус", "Back to focus"),
                    _pomoBreak ? T("Отдохните несколько минут", "Rest for a few minutes") : T("Новый рабочий отрезок начался", "A new focus session started"));
            }
            foreach (var repaint in _pomoRepaints.ToList()) repaint();
        };
        _pomoTimer.Start();
    }

    private FrameworkElement BuildPomodoro(HomeWidgetState w)
    {
        EnsurePomoTimer();
        if (!_pomoRunning && _pomoRemaining == 25 * 60 && PomoWorkMinutes() != 25) _pomoRemaining = PomoWorkMinutes() * 60;

        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 4) };
        head.Children.Add(HIcon("Timer", 13, "ZenMutedBrush"));
        var mode = Muted("", 11.5);
        mode.FontWeight = FontWeights.SemiBold;
        mode.Margin = new Thickness(7, 0, 0, 0);
        head.Children.Add(mode);
        stack.Children.Add(head);

        var time = Txt("25:00", 28);
        time.FontWeight = FontWeights.Normal;
        time.HorizontalAlignment = HAlign.Center;
        time.Margin = new Thickness(0, 2, 0, 8);
        stack.Children.Add(time);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HAlign.Center };
        var playHolder = new ContentControl { Focusable = false };
        void Repaint()
        {
            var m = _pomoRemaining / 60; var s = _pomoRemaining % 60;
            time.Text = $"{m:00}:{s:00}";
            mode.Text = _pomoBreak ? T("Перерыв", "Break") : T("Фокус", "Focus");
            var icon = HIcon(_pomoRunning ? "Pause" : "Play", 12);
            var play = HBtn(icon, () => { _pomoRunning = !_pomoRunning; foreach (var r in _pomoRepaints.ToList()) r(); }, _pomoRunning ? T("Пауза", "Pause") : T("Старт", "Start"), "Zen_4B4B50", "Zen_56565A", 8, new Thickness(0), 30, 42);
            playHolder.Content = play;
        }
        _pomoRepaints.Add(Repaint);
        controls.Children.Add(playHolder);
        var reset = HBtn(HIcon("Reset", 12), () =>
        {
            _pomoRunning = false; _pomoBreak = false; _pomoRemaining = PomoWorkMinutes() * 60;
            foreach (var r in _pomoRepaints.ToList()) r();
        }, T("Сброс", "Reset"), "Zen_3D3D42", "Zen_4B4B50", 8, new Thickness(0), 30, 32);
        reset.Margin = new Thickness(8, 0, 0, 0);
        controls.Children.Add(reset);
        stack.Children.Add(controls);
        Repaint();
        var card = HCard(stack, new Thickness(14, 12, 14, 12));
        card.Height = 116;
        return card;
    }

    private FrameworkElement BuildCalendar(HomeWidgetState w)
    {
        var culture = Ru ? new CultureInfo(EffectiveLanguage == "uk" ? "uk-UA" : "ru-RU") : new CultureInfo("en-US");
        bool mondayFirst = O(w, "week", "mon") == "mon";
        var shown = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var stack = new StackPanel();
        var body = new StackPanel();

        void Render()
        {
            stack.Children.Clear();
            var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = culture.DateTimeFormat.MonthNames[shown.Month - 1];
            var title = Txt($"{char.ToUpper(name[0], culture)}{name[1..]} {shown.Year}", 12.5, true);
            title.VerticalAlignment = VAlign.Center;
            head.Children.Add(title);
            var nav = new StackPanel { Orientation = Orientation.Horizontal };
            nav.Children.Add(HBtn(HIcon("Left", 11), () => { shown = shown.AddMonths(-1); Render(); }, null, null, "Zen_4B4B50", 6, new Thickness(0), 22, 22));
            nav.Children.Add(HBtn(HIcon("Right", 11), () => { shown = shown.AddMonths(1); Render(); }, null, null, "Zen_4B4B50", 6, new Thickness(0), 22, 22));
            Grid.SetColumn(nav, 1);
            head.Children.Add(nav);
            stack.Children.Add(head);

            var grid = new UniformGrid { Columns = 7 };
            var first = mondayFirst ? DayOfWeek.Monday : DayOfWeek.Sunday;
            for (int i = 0; i < 7; i++)
            {
                var d = (DayOfWeek)(((int)first + i) % 7);
                var t = Muted(culture.DateTimeFormat.GetAbbreviatedDayName(d).TrimEnd('.'), 10);
                t.TextAlignment = TextAlignment.Center;
                t.TextTrimming = TextTrimming.None;
                t.Margin = new Thickness(0, 0, 0, 4);
                grid.Children.Add(t);
            }
            int offset = ((int)shown.DayOfWeek - (int)first + 7) % 7;
            for (int i = 0; i < offset; i++) grid.Children.Add(new Border());
            int days = DateTime.DaysInMonth(shown.Year, shown.Month);
            for (int day = 1; day <= days; day++)
            {
                bool today = shown.Year == DateTime.Today.Year && shown.Month == DateTime.Today.Month && day == DateTime.Today.Day;
                var cell = new Border { Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(1) };
                var label = Txt(day.ToString(), 11.5, today);
                label.HorizontalAlignment = HAlign.Center;
                label.VerticalAlignment = VAlign.Center;
                if (today) cell.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                else label.SetResourceReference(TextBlock.ForegroundProperty, "ZenMutedBrush");
                cell.Child = label;
                grid.Children.Add(cell);
            }
            stack.Children.Add(grid);
        }
        Render();
        return HCard(stack);
    }

    private FrameworkElement BuildStats()
    {
        var root = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 10) };
        head.Children.Add(HIcon("Activity", 13, "ZenMutedBrush"));
        var title = Muted(T("Активность", "Activity"), 11.5);
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(7, 0, 0, 0);
        head.Children.Add(title);
        root.Children.Add(head);

        var grid = new Grid();
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        var values = new List<TextBlock>();
        (string Ru, string En, string Icon)[] meta = [("Вкладки", "Tabs", "Layers"), ("История", "History", "Clock"), ("Загрузки", "Downloads", "Download")];
        for (int i = 0; i < 3; i++)
        {
            var col = new StackPanel { HorizontalAlignment = HAlign.Center };
            var v = Txt("0", 21);
            v.FontWeight = FontWeights.SemiBold;
            v.Margin = new Thickness(0, 0, 0, 4);
            v.HorizontalAlignment = HAlign.Center;
            values.Add(v);
            col.Children.Add(v);

            var lblRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HAlign.Center };
            var ic = HIcon(meta[i].Icon, 11, "ZenMutedBrush");
            ic.Margin = new Thickness(0, 0, 4, 0);
            lblRow.Children.Add(ic);
            var lbl = Muted(T(meta[i].Ru, meta[i].En), 11.5);
            lblRow.Children.Add(lbl);
            col.Children.Add(lblRow);

            Grid.SetColumn(col, i);
            grid.Children.Add(col);
        }
        root.Children.Add(grid);

        void Repaint()
        {
            values[0].Text = _spaces.Sum(s => s.Tabs.Count(t => !t.IsHome)).ToString();
            values[1].Text = _state.History.Count.ToString();
            values[2].Text = _state.Downloads.Count.ToString();
        }
        _homeStatRepaints.Add(Repaint);
        Repaint();
        var card = HCard(root, new Thickness(14, 12, 14, 12));
        card.Height = 116;
        return card;
    }

    private static DrawingBrush CreateGridBrush(double gridSize = 20.0)
    {
        var geometry = new GeometryGroup();
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, 1.5, 1.5)));
        var drawing = new GeometryDrawing(
            new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF)),
            null,
            geometry
        );
        drawing.Freeze();
        var brush = new DrawingBrush
        {
            Drawing = drawing,
            Viewport = new Rect(0, 0, gridSize, gridSize),
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.Tile
        };
        brush.Freeze();
        return brush;
    }
}
