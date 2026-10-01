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

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private sealed class RuntimeSpace
    {
        public SpaceState State { get; }
        public ObservableCollection<BrowserTab> Tabs { get; } = new();
        public BrowserTab? Current { get; set; }
        public bool Loaded { get; set; }
        public Task? Loading { get; set; }
        public RuntimeSpace(SpaceState state) => State = state;
    }
    public sealed class SpaceDotView { public int Index { get; init; } public string Name { get; init; } = "Пространство"; public Brush Brush { get; init; } = Brushes.White; public Brush TextBrush { get; init; } = Brushes.White; public Brush OutlineBrush { get; init; } = Brushes.Transparent; public bool IsActive { get; init; } public double Size { get; init; } public string Letter => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim().Substring(0, 1).ToUpperInvariant(); }
    private sealed class FolderVisual { public TextBlock Label = null!; public TextBox Editor = null!; public Border Content = null!; public StackPanel Items = null!; public RotateTransform Chevron = null!; public Image FolderIcon = null!; public Image DeleteIcon = null!; public TranslateTransform ItemsTranslate = null!; }
    private sealed class SavedDrag { public SavedSite Site = null!; public FolderState? Source; public bool Loose; }
    private enum SearchPurpose { NewTab, Navigate, Split, Folder, TabSearch }

    private readonly ILumaStateStore _stateStore;
    private readonly IClock _clock;
    private readonly LumaState _state;
    private readonly List<RuntimeSpace> _spaces = new();
    private readonly ObservableCollection<SearchSuggestion> _searchResults = new();
    private readonly Dictionary<string, FolderVisual> _folderVisuals = new();
    private readonly Dictionary<string, TabSessionState> _pendingTabRestores = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task> _tabInitializationTasks = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _homeClockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private static readonly HttpClient TranslationHttp = CreateTranslationHttp();
    // Public translation endpoints start returning 429/empty payloads when a page fans out
    // dozens of requests. Keep one shared, deliberately small limit for every tab.
    private static readonly SemaphoreSlim TranslationGate = new(4, 4);

    /// <summary>
    /// Translation is dozens of small requests to one host, so the defaults were the bottleneck:
    /// two connections per server, no compression and a fresh TLS handshake now and then. This
    /// handler keeps a warm pool, multiplexes over HTTP/2 and lets the server gzip its replies.
    /// </summary>
    private static HttpClient CreateTranslationHttp()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            MaxConnectionsPerServer = 8,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(4),
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(12),
            DefaultRequestVersion = System.Net.HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140.0 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru-RU,ru;q=0.9,en;q=0.8");
        return client;
    }
    private int _activeSpace;
    private BrowserTab? _currentTab;
    private SearchPurpose _searchPurpose;
    private string _searchPrevText = "";
    private bool _searchSuppress;
    private bool _syncingTabSelection;
    private int _tabActivationSerial;
    private int _spaceSwitchSerial;
    private FolderState? _searchFolder;
    private FolderState? _deleteFolder;
    private Action? _confirmAction;
    private Point _dragStart;
    private bool _sidebarVisible = true, _sidebarAutoShown, _closing, _suppressPip, _suppressAmbient, _dividerDragging, _manualMaximized, _seekDragging, _volumeDragging;
    private BrowserTab? _nowPlaying;
    private FloatingMusicWindow? _floatingMusic;
    private bool _floatingMusicDismissed;
    private bool _floatingVideoRequested;
    private string _loadedArtworkSource = "";
    private bool _fullscreen, _fsSidebarWasVisible = true, _fsWasMaximized;
    private FolderState? _folderBeingNamed;
    private bool _foldersSectionExpanded = true;
    private readonly DispatcherTimer _hoverHideTimer = new() { Interval = TimeSpan.FromMilliseconds(260) };
    // The page area is a native WebView2 window, and native windows swallow WPF mouse events.
    // That is why MouseEnter on the edge strip only fired when the cursor happened to sit over
    // WPF chrome, so the reveal worked "every other time". Polling the real cursor position
    // works no matter what is underneath it.
    private readonly DispatcherTimer _edgeWatch = new() { Interval = TimeSpan.FromMilliseconds(60) };
    // WebView2 can retain native mouse capture after selecting text. This independent poller
    // observes the physical left-button edge and activates the tab under the cursor by its
    // screen rectangle, so no routed WPF event or WebView focus state can block switching.
    private readonly DispatcherTimer _tabInputWatch = new() { Interval = TimeSpan.FromMilliseconds(10) };
    private bool _leftButtonWasDown;
    // Background tabs are suspended after a while: Chromium gives the memory back and the row
    // only dims a shade, so nothing about the sidebar changes visually.
    private readonly DispatcherTimer _sleepWatch = new() { Interval = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource? _navigationSaveDebounce;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private void TabInputWatchTick()
    {
        var down = (GetAsyncKeyState(0x01) & 0x8000) != 0;
        var pressedNow = down && !_leftButtonWasDown;
        _leftButtonWasDown = down;
        if (!pressedNow || !IsLoaded || !IsActive || TabsList.Visibility != Visibility.Visible || !GetCursorPos(out var native)) return;
        for (var i = 0; i < TabsList.Items.Count; i++)
        {
            if (TabsList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem row || row.DataContext is not BrowserTab tab || !row.IsVisible) continue;
            Point topLeft;
            try { topLeft = row.PointToScreen(new Point(0, 0)); }
            catch { continue; }
            var right = topLeft.X + row.ActualWidth;
            var bottom = topLeft.Y + row.ActualHeight;
            if (native.X < topLeft.X || native.X > right || native.Y < topLeft.Y || native.Y > bottom) continue;
            ReleaseCapture();
            if (!IsActive) Activate();
            // The final 24 px are the close target. Handle it here too because a captured
            // WebView may suppress the close button's routed Click exactly like a tab click.
            if (native.X >= right - 24) _ = CloseTabAfterPointerReleaseAsync(tab, 0x01);
            else ActivateTabFromUser(tab);
            return;
        }
    }

    private async Task CloseTabAfterPointerReleaseAsync(BrowserTab tab, int virtualKey)
    {
        // Native fallback sees the press before WPF receives a Click. Wait for the physical
        // button release so removing the row cannot poison capture/selection state.
        for (var attempt = 0; attempt < 100 && (GetAsyncKeyState(virtualKey) & 0x8000) != 0; attempt++)
            await Task.Delay(5);
        await Dispatcher.InvokeAsync(() =>
        {
            if (_spaces.Any(space => space.Tabs.Contains(tab))) CloseTab(tab);
        }, DispatcherPriority.Input);
    }

    private void EdgeWatchTick()
    {
        if (!IsLoaded || WindowState == WindowState.Minimized) return;
        if (!GetCursorPos(out var native)) return;
        Point local;
        try { local = PointFromScreen(new Point(native.X, native.Y)); }
        catch { return; }
        var insideWindow = local.X >= -2 && local.Y >= -2 && local.X <= ActualWidth + 2 && local.Y <= ActualHeight + 2;
        if (!_sidebarVisible && !_sidebarAutoShown && !_fullscreen && insideWindow && IsActive && local.X <= 8)
        {
            SetSidebar(true, true, false, true);
        }
        else if (_sidebarAutoShown && (!insideWindow || !IsActive || _fullscreen || local.X > SidebarWidth + 24))
        {
            SetSidebar(false, true, false, true);
        }
        if (_peekActive) SyncPeekBounds();
    }
    private Rect _fsBounds = new(0, 0, 1280, 800);
    private Rect _restoreBounds;
    private ContextMenu? _openMenu;
    private Border? _leftIndicator, _rightIndicator;
    private readonly Dictionary<BrowserTab, Grid> _tabPaneHosts = new();
    private readonly Dictionary<BrowserTab, bool> _tabPaneHostSplit = new();
    private readonly Dictionary<BrowserTab, (Border Left, Border Right)> _tabPaneIndicators = new();
    private string? _previewTheme;
    private BrowserTab? _previewThemeTab;
    private bool _nowPlayingExpanded;

    public ObservableCollection<BrowserTab> Tabs => _spaces.Count == 0 ? new() : _spaces[_activeSpace].Tabs;
    public BrowserTab? CurrentTab
    {
        get => _currentTab;
        set
        {
            if (_currentTab == value) return;
            var previous = _currentTab;
            if (previous is not null && previous != value)
            {
                if (ReferenceEquals(previous, _previewThemeTab)) CancelThemePreview();
                try
                {
                    if (previous.View?.CoreWebView2 is { } prevCore && !prevCore.IsDocumentPlayingAudio)
                    {
                        prevCore.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                    }
                    if (previous.SecondaryView?.CoreWebView2 is { } prevSec && !prevSec.IsDocumentPlayingAudio)
                    {
                        prevSec.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                    }
                }
                catch { }
            }
            _currentTab = value;
            if (value is not null) { value.LastActiveAt = _clock.UtcNow; WakeTab(value); }
            if (_spaces.Count > 0) _spaces[_activeSpace].Current = value;
            OnChanged(); CloseTransientUi();
            if (IsLoaded && TabsList is not null) { _syncingTabSelection = true; try { TabsList.SelectedItem = value is not null && string.IsNullOrEmpty(value.FolderId) && !value.IsPinned ? value : null; } finally { _syncingTabSelection = false; } }
            UpdateChrome(); RenderPanes();
            if (_assistantVisible) UpdateAssistantContext();
            if (IsLoaded && FoldersHost is not null) RefreshFolders();
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow() : this(BrowserServices.CreateDefault()) { }

    internal MainWindow(BrowserServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _stateStore = services.StateStore;
        _clock = services.Clock;
        _auth = services.Auth;
        _auth.SessionChanged += AccountSessionChanged;
        _updates = services.Updates;
        _state = _stateStore.State;
        _state.Language = NormalizeInterfaceLanguage(_state.Language);
        ConfigureBrowserLanguage(_state.Language);
        InitializeComponent();
        ApplyInterfaceLanguage();
        _ = WarmLocalizationPacksAsync();
        if (_state.Spaces.Count == 0) _state.Spaces.Add(new SpaceState());
        foreach (var space in _state.Spaces) _spaces.Add(new RuntimeSpace(space));
        _activeSpace = Math.Clamp(_state.ActiveSpace, 0, _spaces.Count - 1);
        DataContext = this; SearchResultsList.ItemsSource = _searchResults; InitializeDownloads(); InitializeTestCenter();
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastPopup.IsOpen = false; };
        _homeClockTimer.Tick += (_, _) => UpdateHomeClock();
        _homeClockTimer.Start();
        UpdateHomeClock();
        InitializeDashboardStudio();
        InitializeSupportChat();
        SiteShell.SizeChanged += (_, _) =>
        {
            if (ToastPopup.IsOpen) PositionToast();
            if (HistoryPopup.IsOpen) { HistoryOverlay.Width = Math.Max(1, SiteShell.ActualWidth); HistoryOverlay.Height = Math.Max(1, SiteShell.ActualHeight); }
            UpdateResponsiveLayout();
        };
        SizeChanged += (_, _) =>
        {
            if (_assistantVisible) AssistantColumn.Width = new GridLength(ResponsiveAssistantWidth());
            UpdateResponsiveLayout();
        };
        // WebView2 hosts its own child window; after it holds focus, the very first click on a
        // native WPF control can be consumed just re-activating this window instead of reaching
        // the control. Forcing activation on the initial PreviewMouseDown fixes the "must click
        // twice" symptom across the whole shell (folders, sidebar, menus, etc.).
        PreviewMouseDown += (_, _) => { if (!IsActive) Activate(); };
        // Touch has no row-level XAML handler. Mouse activation is intentionally handled only
        // by the row plus the native fallback poller; a second global mouse handler used to
        // create competing activation requests for the same click.
        AddHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(ReliableTabPreviewTouchDown), true);
        ApplyTheme(); Loaded += OnLoaded; Closing += OnClosing;
        Activated += (_, _) => { _floatingMusicDismissed = false; _ = StopFloatingVideoAsync(); HideFloatingMusic(); };
        Deactivated += (_, _) => { _floatingMusicDismissed = false; HandleBrowserBackgrounded(); TrimProcessMemory(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) { _floatingMusicDismissed = false; HandleBrowserBackgrounded(); TrimProcessMemory(); } else if (IsActive) HideFloatingMusic(); };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyInterfaceLanguage();
        await _auth.RestoreSessionAsync(!((App)Application.Current).IsPrivateSession);
        UpdateAccountMenu();
        StartAccountUsageTracking();
        _hoverHideTimer.Tick += (_, _) => { _hoverHideTimer.Stop(); if (_sidebarAutoShown && !Sidebar.IsMouseOver) SetSidebar(false, true, false, true); };
        _edgeWatch.Tick += (_, _) => EdgeWatchTick(); _edgeWatch.Start();
        _tabInputWatch.Tick += (_, _) => TabInputWatchTick(); _tabInputWatch.Start();
        _sleepWatch.Tick += (_, _) => SleepWatchTick(); _sleepWatch.Start();
        StartEmptyMotion(); SetSidebar(_state.SidebarVisible, false); RefreshSpaces(); RefreshFolders();
        await LoadSpaceAsync(_activeSpace);
        if (Tabs.Count == 0) _spaces[_activeSpace].Current = EnsureHomeTab(_spaces[_activeSpace]);
        CurrentTab = _spaces[_activeSpace].Current ?? Tabs.FirstOrDefault() ?? EnsureHomeTab(_spaces[_activeSpace]);
        FilterTabs(); UpdateChrome(); Save();
        await ClearForgetDomainsAsync();
        await OfferPendingCrashReportAsync();
        _ = StartUpdateLoopAsync();
    }

    private async Task LoadSpaceAsync(int index)
    {
        var space = _spaces[index];
        if (space.Loaded)
        {
            if (space.Loading is not null) await space.Loading;
            return;
        }
        space.Loaded = true;
        var loading = LoadCoreAsync();
        space.Loading = loading;
        try { await loading; }
        finally { space.Loading = null; }

        async Task LoadCoreAsync()
        {
            foreach (var saved in space.State.SessionTabs.Where(item => !string.IsNullOrWhiteSpace(item.Url)))
            {
                var tab = AddTabShellToSpace(space, saved);
                if (!tab.IsHome) _pendingTabRestores[tab.Id] = saved;
            }

            // Publish lightweight rows immediately. Only the selected tab creates a WebView now;
            // the rest are restored on demand, so changing spaces never constructs many Chromium
            // windows on the UI thread at once.
            var remembered = space.Tabs.FirstOrDefault(tab => tab.Id == space.State.ActiveTabId);
            var home = EnsureHomeTab(space);
            space.Current = remembered ?? home;
            if (index == _activeSpace) { OnChanged(nameof(Tabs)); FilterTabs(); }
            if (space.Current is not null) await EnsureTabReadyAsync(space.Current);
        }
    }

    private void StartEmptyMotion() { if (!_state.AnimationsEnabled) return; void M(TranslateTransform t,double a,double b,double c,double d,double seconds){var e=new SineEase{EasingMode=EasingMode.EaseInOut};t.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(a,b,TimeSpan.FromSeconds(seconds)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=e},HandoffBehavior.SnapshotAndReplace);t.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(c,d,TimeSpan.FromSeconds(seconds*1.17)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=e},HandoffBehavior.SnapshotAndReplace);} M(EmptyOrb1Transform,-70,130,-55,80,12);M(EmptyOrb2Transform,85,-110,60,-75,14);M(EmptyOrb3Transform,-60,95,70,-60,16);}

    private void ApplyTheme(string? previewTheme = null)
    {
        var theme = previewTheme ?? Registry.CurrentUser.OpenSubKey(@"Software\Luma")?.GetValue("Theme") as string ?? "dark";
        var palette = theme switch
        {
            "dark" => (Accent: "#8B8F99", Shell: "#0C0D0F", Panel: "#141518", Top: "#090A0C", Site: "#101114", Surface: "#1A1B1F", Raised: "#222329", Inset: "#111216", Border: "#2D2F36", Strong: "#41444D", Soft: "#2B2D33", MutedAccent: "#C1C4CC", Muted: "#858891", Secondary: "#D6D8DE"),
            "blue" => (Accent: "#6687C8", Shell: "#0B1119", Panel: "#111B28", Top: "#090F16", Site: "#0E1621", Surface: "#172334", Raised: "#1D2C40", Inset: "#0B1420", Border: "#2A3C53", Strong: "#405873", Soft: "#243550", MutedAccent: "#AFC3EA", Muted: "#8090A5", Secondary: "#CFD8E6"),
            "purple" => (Accent: "#756BB7", Shell: "#111016", Panel: "#1A1821", Top: "#0E0D12", Site: "#141219", Surface: "#211E29", Raised: "#292531", Inset: "#17141C", Border: "#37323F", Strong: "#4A4355", Soft: "#302A42", MutedAccent: "#B9B0DE", Muted: "#8D8797", Secondary: "#D7D2DC"),
            "sand" => (Accent: "#A77C64", Shell: "#151210", Panel: "#201A17", Top: "#110E0C", Site: "#191411", Surface: "#28211D", Raised: "#302722", Inset: "#191411", Border: "#40352F", Strong: "#594940", Soft: "#3A2D27", MutedAccent: "#D8B9A7", Muted: "#A08D82", Secondary: "#E2D7D0"),
            "mint" => (Accent: "#4F9E94", Shell: "#0C1413", Panel: "#12201E", Top: "#09110F", Site: "#0E1816", Surface: "#172724", Raised: "#1D302C", Inset: "#0C1715", Border: "#29413C", Strong: "#3B5B54", Soft: "#203B36", MutedAccent: "#A9D8D1", Muted: "#7F9E99", Secondary: "#CEE0DD"),
            "graphite" => (Accent: "#607D8B", Shell: "#121416", Panel: "#1B1E21", Top: "#0F1113", Site: "#16191B", Surface: "#22262A", Raised: "#2A2F34", Inset: "#171A1D", Border: "#363C42", Strong: "#4B545C", Soft: "#29343A", MutedAccent: "#ABC0CA", Muted: "#8D969D", Secondary: "#D2D8DC"),
            _ => (Accent: "#756BB7", Shell: "#111016", Panel: "#1A1821", Top: "#0E0D12", Site: "#141219", Surface: "#211E29", Raised: "#292531", Inset: "#17141C", Border: "#37323F", Strong: "#4A4355", Soft: "#302A42", MutedAccent: "#B9B0DE", Muted: "#8D8797", Secondary: "#D7D2DC")
        };
        var color = (Color)ColorConverter.ConvertFromString(palette.Accent);
        RootShell.Background = Brush(palette.Shell); SiteShell.Background = Brush(palette.Site); EmptyBackground.Background = Brush(palette.Shell);
        Resources["AccentBrush"] = Brush(palette.Accent); Resources["AccentSoftBrush"] = Brush(palette.Soft); Resources["AccentMutedBrush"] = Brush(palette.MutedAccent);
        var homeGlow = theme switch { "blue" => "#557AAE", "purple" => "#6C61A8", "sand" => "#8B6958", "mint" => "#43837B", "graphite" => "#52636C", "dark" => "#454850", _ => "#6C61A8" };
        var homeGlowSoft = theme switch { "blue" => "#26384E", "purple" => "#39334F", "sand" => "#43342D", "mint" => "#24423D", "graphite" => "#293238", "dark" => "#26282D", _ => "#39334F" };
        Resources["HomeGlowBrush"] = Brush(homeGlow); Resources["HomeGlowSoftBrush"] = Brush(homeGlowSoft);
        Resources["SurfaceBrush"] = Brush(palette.Surface); Resources["SurfaceRaisedBrush"] = Brush(palette.Raised); Resources["SurfaceInsetBrush"] = Brush(palette.Inset);
        Resources["BorderBrush"] = Brush(palette.Border); Resources["BorderStrongBrush"] = Brush(palette.Strong);
        Resources["PanelBrush"] = Brush(palette.Panel); Resources["TopBrush"] = Brush(palette.Top);
        Resources["ChromeMuted"] = Brush(palette.Muted); Resources["ChromeIconMuted"] = Brush(palette.Muted); Resources["ChromeSecondary"] = Brush(palette.Secondary);
        Resources["ActiveTabBrush"] = Brush(palette.Soft); Resources["ActiveTabText"] = Brushes.White;
        SidebarNeon.Fill = Brush(palette.Accent);
        HistoryCard.Background = Brush(palette.Surface); HistoryCard.BorderBrush = Brush(palette.Border);
        foreach (var tab in _spaces.SelectMany(space => space.Tabs))
        {
            if (tab.View.CoreWebView2 is not null) _ = Script(tab.View, BrowserScripts.SetScrollbarAccent(palette.Accent));
            if (tab.SecondaryView?.CoreWebView2 is not null) _ = Script(tab.SecondaryView, BrowserScripts.SetScrollbarAccent(palette.Accent));
        }
        if (_assistantView?.CoreWebView2 is not null)
        {
            _ = Script(_assistantView, BrowserScripts.SetScrollbarAccent(palette.Accent));
            PostAssistant(new { kind = "theme", palette = new { bg = palette.Shell, panel = palette.Panel, surface = palette.Surface, raised = palette.Raised, inset = palette.Inset, border = palette.Border, strong = palette.Strong, accent = palette.Accent, soft = palette.Soft, mutedAccent = palette.MutedAccent, muted = palette.Muted, secondary = palette.Secondary } });
        }
        _floatingMusic?.SyncTheme(this);
        _floatingVideo?.SyncTheme(this);
        RefreshSpaces(); UpdateIndicators();
    }

    private void PreviewTheme(BrowserTab tab, string theme)
    {
        if (theme is not ("dark" or "blue" or "purple" or "sand" or "mint" or "graphite")) return;
        _previewTheme = theme; _previewThemeTab = tab; ApplyTheme(theme);
    }

    private void CommitThemePreview() { _previewTheme = null; _previewThemeTab = null; }
    private void CancelThemePreview()
    {
        if (_previewTheme is null) return;
        _previewTheme = null; _previewThemeTab = null; ApplyTheme();
    }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private void UpdateHomeClock()
    {
        if (HomeClockText is null || HomeDateText is null) return;
        var now = DateTime.Now;
        HomeClockText.Text = now.ToString("HH:mm");
        var culture = _state.Language switch
        {
            "ru" => new System.Globalization.CultureInfo("ru-RU"),
            "uk" => new System.Globalization.CultureInfo("uk-UA"),
            _ => new System.Globalization.CultureInfo("en-US")
        };
        var dateFormatted = now.ToString("dddd, d MMMM", culture);
        if (dateFormatted.Length > 0)
            dateFormatted = char.ToUpper(dateFormatted[0], culture) + dateFormatted[1..];
        HomeDateText.Text = dateFormatted;
    }

}
