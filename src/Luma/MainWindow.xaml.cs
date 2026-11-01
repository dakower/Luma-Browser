﻿﻿﻿using Luma.Core;
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
    public sealed class SpaceDotView { public int Index { get; init; } public string Name { get; init; } = "РџСЂРѕСЃС‚СЂР°РЅСЃС‚РІРѕ"; public Brush Brush { get; init; } = Brushes.White; public Brush TextBrush { get; init; } = Brushes.White; public Brush OutlineBrush { get; init; } = Brushes.Transparent; public bool IsActive { get; init; } public double Size { get; init; } public string Letter => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim().Substring(0, 1).ToUpperInvariant(); }
    private sealed class FolderVisual { public TextBlock Label = null!; public TextBox Editor = null!; public Border Content = null!; public StackPanel Items = null!; public RotateTransform Chevron = null!; public Image FolderIcon = null!; public Image DeleteIcon = null!; public TranslateTransform ItemsTranslate = null!; }
    private sealed class SavedDrag { public SavedSite Site = null!; public FolderState? Source; public bool Loose; }
    private enum SearchPurpose { NewTab, Navigate, Split, Folder, TabSearch }

    private readonly ILumaStateStore _stateStore;
    private readonly IClock _clock;
    private readonly LumaState _state;
    internal LumaState State => _state;
    internal void SaveState() => _stateStore.Save();

    internal void BringToFrontAndActivate()
    {
        Dispatcher.Invoke(() =>
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Show();
            Activate();
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                SetForegroundWindow(handle);
                SetFocus(handle);
            }
            Topmost = true;
            Topmost = false;
            Focus();
        });
    }
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
    private Voice.VoiceAssistantService? _voiceService;
    internal Voice.VoiceAssistantService? VoiceService => _voiceService;
    private string _loadedArtworkSource = "";
    private bool _fullscreen, _fsSidebarWasVisible = true, _fsWasMaximized;
    private FolderState? _folderBeingNamed;
    private bool _foldersSectionExpanded = true;
    private readonly DispatcherTimer _hoverHideTimer = new() { Interval = TimeSpan.FromMilliseconds(260) };
    // The page area is a native WebView2 window, and native windows swallow WPF mouse events.
    // That is why MouseEnter on the edge strip only fired when the cursor happened to sit over
    // WPF chrome, so the reveal worked "every other time". Polling the real cursor position
    // works no matter what is underneath it.
    private readonly DispatcherTimer _edgeWatch = new() { Interval = TimeSpan.FromMilliseconds(40) };
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

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    public bool IsForegroundWindowOrChild()
    {
        if (IsActive) return true;
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        var myHwnd = helper.Handle;
        return myHwnd != IntPtr.Zero && (fg == myHwnd || IsChild(myHwnd, fg));
    }

    private void TabInputWatchTick()
    {
        var down = (GetAsyncKeyState(0x01) & 0x8000) != 0;
        var pressedNow = down && !_leftButtonWasDown;
        _leftButtonWasDown = down;
        if (!pressedNow || !IsLoaded || !IsForegroundWindowOrChild() || TabsList.Visibility != Visibility.Visible || !GetCursorPos(out var native)) return;
        // Skip native click override if Alt key (VK_MENU = 0x12) is held so Alt+Click triggers Zen Glance
        if ((GetAsyncKeyState(0x12) & 0x8000) != 0) return;
        for (var i = 0; i < TabsList.Items.Count; i++)
        {
            if (TabsList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem row || row.DataContext is not BrowserTab tab || !row.IsVisible) continue;
            if (tab.IsSplit) continue; // Split View dual-subtab row handles its own sub-tab clicks
            Point topLeft;
            try { topLeft = row.PointToScreen(new Point(0, 0)); }
            catch { continue; }
            var right = topLeft.X + row.ActualWidth;
            var bottom = topLeft.Y + row.ActualHeight;
            if (native.X < topLeft.X || native.X > right || native.Y < topLeft.Y || native.Y > bottom) continue;

            // In collapsed (48px) icon-only mode, there is NEVER a close button!
            // Any click anywhere on the tab must ALWAYS activate the tab!
            if (!IsSidebarVisualExpanded)
            {
                ReleaseCapture();
                if (!IsActive) Activate();
                ActivateTabFromUser(tab);
                return;
            }

            // If the tab is showing its audio mute button, let WPF handle the audio button zone (right - 54 to right - 28)
            if (tab.ShowAudioButton && native.X >= right - 54 && native.X < right - 28) return;
            ReleaseCapture();
            if (!IsActive) Activate();
            // The final 28 px are the close target ONLY when the sidebar is visually expanded!
            if (native.X >= right - 28) _ = CloseTabAfterPointerReleaseAsync(tab, 0x01);
            else ActivateTabFromUser(tab);
            return;
        }
    }

    private async Task CloseTabAfterPointerReleaseAsync(BrowserTab tab, int virtualKey)
    {
        if (!IsSidebarVisualExpanded) return;
        // Native fallback sees the press before WPF receives a Click. Wait for the physical
        // button release so removing the row cannot poison capture/selection state.
        for (var attempt = 0; attempt < 100 && (GetAsyncKeyState(virtualKey) & 0x8000) != 0; attempt++)
            await Task.Delay(5);
        await Dispatcher.InvokeAsync(() =>
        {
            if (!IsSidebarVisualExpanded) return;
            if (_spaces.Any(space => space.Tabs.Contains(tab))) CloseTab(tab);
        }, DispatcherPriority.Input);
    }

    private void EdgeWatchTick()
    {
        // Sidebar is permanently stitched into BodyGrid (Grid.Column="0") like Zen Browser normal mode,
        // never lifted into a floating island Popup.
        if (_peekActive) HidePeek();
        CheckSidebarHoverState();
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
                    if (previous.View?.CoreWebView2 is { } prevCore && !prevCore.IsDocumentPlayingAudio && !IsNeverSleepUrl(previous.FullUrl))
                    {
                        prevCore.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                    }
                    if (previous.SecondaryView?.CoreWebView2 is { } prevSec && !prevSec.IsDocumentPlayingAudio && !IsNeverSleepUrl(previous.SecondaryUrl))
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
    internal static MainWindow? CurrentWindow { get; private set; }

    public MainWindow() : this(BrowserServices.CreateDefault()) { }

    internal MainWindow(BrowserServices services)
    {
        CurrentWindow = this;
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
        InitializeSuperFeatures();
        SiteShell.SizeChanged += (_, _) =>
        {
            if (ToastPopup.IsOpen) PositionToast();
            if (UpdateNotificationPopup != null && UpdateNotificationPopup.IsOpen) PositionUpdateNotification();
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
        try
        {
            _voiceService = new Voice.VoiceAssistantService(this, VoiceSpeechWebView);
            _voiceService.Initialize();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        _hoverHideTimer.Tick += (_, _) => { _hoverHideTimer.Stop(); if (_sidebarAutoShown && !Sidebar.IsMouseOver) SetSidebar(false, true, false, true); };
        _edgeWatch.Tick += (_, _) => EdgeWatchTick(); _edgeWatch.Start();
        _tabInputWatch.Tick += (_, _) => TabInputWatchTick(); _tabInputWatch.Start();
        _sleepWatch.Tick += (_, _) => SleepWatchTick(); _sleepWatch.Start();
        StartEmptyMotion();
        _state.SidebarCompactMode = false;
        _state.SidebarVisible = true;
        _isSidebarExpanded = true; // Default pinned open so sidebar stays permanently visible while browsing sites
        _state.SidebarCollapsedIconsOnly = false;
        _isSidebarHoverExpanded = false;
        SetSidebar(true, false);
        SidebarColumn.Width = new GridLength(206.0);
        if (Sidebar != null) Sidebar.Width = 206.0;
        if (Sidebar != null) Sidebar.Width = _isSidebarExpanded ? 206.0 : 48.0;
        UpdateSidebarExpandedUi();
        RefreshSpaces(); RefreshFolders();
        await LoadSpaceAsync(_activeSpace);
        if (Tabs.Count == 0) _spaces[_activeSpace].Current = EnsureHomeTab(_spaces[_activeSpace]);
        CurrentTab = _spaces[_activeSpace].Current ?? Tabs.FirstOrDefault() ?? EnsureHomeTab(_spaces[_activeSpace]);
        FilterTabs(); UpdateChrome(); RefreshZenSidebar(); UpdateResponsiveLayout(); Save();
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
            var remembered = space.Tabs.FirstOrDefault(tab => tab.Id == space.State.ActiveTabId)
                ?? space.Tabs.FirstOrDefault(tab => !tab.IsHome && !tab.IsEssential)
                ?? space.Tabs.FirstOrDefault();
            space.Current = remembered ?? EnsureHomeTab(space);
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
            "dark" => (Accent: "#7C6EE6", Shell: "#0E0F12", Panel: "#141519", Top: "#00000000", Site: "#111215", Surface: "#1A1B20", Raised: "#22242B", Inset: "#111216", Border: "#23252E", Strong: "#353846", Soft: "#2A2740", MutedAccent: "#A59BF0", Muted: "#7A7D87", Secondary: "#C5C8D4"),
            "graphite" => (Accent: "#8B95A5", Shell: "#101114", Panel: "#16171B", Top: "#00000000", Site: "#121316", Surface: "#1C1E22", Raised: "#24262C", Inset: "#131417", Border: "#252830", Strong: "#383C47", Soft: "#252833", MutedAccent: "#B8C2D1", Muted: "#757985", Secondary: "#C4C8D2"),
            "purple" => (Accent: "#8A7BF0", Shell: "#100E17", Panel: "#171420", Top: "#00000000", Site: "#13111A", Surface: "#1F1B2A", Raised: "#282336", Inset: "#14121B", Border: "#2D273B", Strong: "#443B59", Soft: "#2D2545", MutedAccent: "#BAAFF7", Muted: "#867D94", Secondary: "#CCC4DC"),
            "blue" => (Accent: "#5E8FE6", Shell: "#0C1017", Panel: "#121822", Top: "#00000000", Site: "#0F141D", Surface: "#18202D", Raised: "#1F293A", Inset: "#0F141C", Border: "#253144", Strong: "#384964", Soft: "#202D45", MutedAccent: "#9DC0FA", Muted: "#778499", Secondary: "#C2CFE0"),
            "mint" => (Accent: "#4EAB98", Shell: "#0C1312", Panel: "#12201E", Top: "#00000000", Site: "#0F1715", Surface: "#172422", Raised: "#1F2E2B", Inset: "#0F1816", Border: "#243734", Strong: "#37534E", Soft: "#1D3631", MutedAccent: "#89D1C3", Muted: "#738A85", Secondary: "#BFD3CF"),
            "sand" => (Accent: "#B88B6E", Shell: "#14110E", Panel: "#1B1713", Top: "#00000000", Site: "#16120F", Surface: "#231D19", Raised: "#2C2520", Inset: "#16120F", Border: "#362D27", Strong: "#50433A", Soft: "#382A22", MutedAccent: "#DEC0AB", Muted: "#948479", Secondary: "#D9CBC1"),
            _ => (Accent: "#7C6EE6", Shell: "#0E0F12", Panel: "#141519", Top: "#00000000", Site: "#111215", Surface: "#1A1B20", Raised: "#22242B", Inset: "#111216", Border: "#23252E", Strong: "#353846", Soft: "#2A2740", MutedAccent: "#A59BF0", Muted: "#7A7D87", Secondary: "#C5C8D4")
        };
        var color = (Color)ColorConverter.ConvertFromString(palette.Accent);
        SetZenShift(ZenChromeFor(theme)); ApplyZenChromeResources(); RootShell.Background = Brush("#3D3D42");
        if (BodyGrid is not null) BodyGrid.Background = Brush("#3D3D42");
        SiteShell.Background = RawBrush(palette.Site); EmptyBackground.Background = RawBrush(palette.Shell);
        Resources["ShellBrush"] = RawBrush(palette.Shell);
        Resources["AccentBrush"] = RawBrush(palette.Accent); Resources["AccentSoftBrush"] = RawBrush(palette.Soft); Resources["AccentMutedBrush"] = RawBrush(palette.MutedAccent);
        var homeGlow = theme switch { "blue" => "#557AAE", "purple" => "#6C61A8", "sand" => "#8B6958", "mint" => "#43837B", "graphite" => "#52636C", "dark" => "#454850", _ => "#6C61A8" };
        var homeGlowSoft = theme switch { "blue" => "#26384E", "purple" => "#39334F", "sand" => "#43342D", "mint" => "#24423D", "graphite" => "#293238", "dark" => "#26282D", _ => "#39334F" };
        Resources["HomeGlowBrush"] = RawBrush(homeGlow); Resources["HomeGlowSoftBrush"] = RawBrush(homeGlowSoft);
        Resources["SurfaceBrush"] = RawBrush(palette.Surface); Resources["SurfaceRaisedBrush"] = RawBrush(palette.Raised); Resources["SurfaceInsetBrush"] = RawBrush(palette.Inset);
        Resources["BorderBrush"] = RawBrush(palette.Border); Resources["BorderStrongBrush"] = RawBrush(palette.Strong);
        Resources["PanelBrush"] = RawBrush(palette.Panel); Resources["TopBrush"] = RawBrush(palette.Top);
        Resources["ChromeMuted"] = RawBrush(palette.Muted); Resources["ChromeIconMuted"] = RawBrush(palette.Muted); Resources["ChromeSecondary"] = RawBrush(palette.Secondary);
        Resources["ActiveTabBrush"] = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)); Resources["ActiveTabText"] = Brushes.White;
        SidebarNeon.Fill = RawBrush(palette.Accent);
        HistoryCard.Background = RawBrush(palette.Surface); HistoryCard.BorderBrush = RawBrush(palette.Border);
        foreach (var tab in _spaces.SelectMany(space => space.Tabs))
        {
            if (tab.View.CoreWebView2 is not null) _ = Script(tab.View, BrowserScripts.SetScrollbarAccent(palette.Accent));
            if (tab.SecondaryView?.CoreWebView2 is not null) _ = Script(tab.SecondaryView, BrowserScripts.SetScrollbarAccent(palette.Accent));
        }
        if (_assistantView?.CoreWebView2 is not null)
        {
            _ = Script(_assistantView, BrowserScripts.SetScrollbarAccent(palette.Accent));
            var zenBg = ZenColor("#38383D");
            var zenPanel = ZenColor("#3D3D42");
            var zenRaised = ZenColor("#4B4B50");
            var zenInset = ZenColor("#2C2C32");
            var zenBorder = ZenColor("#505056");
            var zenStrong = ZenColor("#5E5E64");
            static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            PostAssistant(new
            {
                kind = "theme",
                palette = new
                {
                    bg = Hex(zenBg),
                    panel = Hex(zenPanel),
                    surface = Hex(zenPanel),
                    raised = Hex(zenRaised),
                    inset = Hex(zenInset),
                    border = Hex(zenBorder),
                    strong = Hex(zenStrong),
                    accent = palette.Accent,
                    soft = palette.Soft,
                    mutedAccent = palette.MutedAccent,
                    muted = "#A8A8B0",
                    secondary = "#D4D4DC"
                }
            });
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
    private static SolidColorBrush Brush(string color)
    {
        var parsed = (Color)ColorConverter.ConvertFromString(color);
        return new SolidColorBrush(parsed.A == 255 ? ZenShift(parsed) : parsed);
    }
    private static SolidColorBrush RawBrush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private void UpdateHomeClock()
    {
        if (HomeClockText is null || HomeDateText is null) return;
        var now = DateTime.Now;
        HomeClockText.Text = FormatHomeClock(now);
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

    internal async Task<string?> RecognizeHandwritingViaLumaAiAsync(string base64Png)
    {
        try
        {
            var token = await _auth.GetAccessTokenAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(token)) return null;

            const string prompt = """
РўРё вЂ” РІРёСЃРѕРєРѕС‚РѕС‡РЅРёР№ РјРѕРґСѓР»СЊ СЂРѕР·РїС–Р·РЅР°РІР°РЅРЅСЏ СЂСѓРєРѕРїРёСЃРЅРѕРіРѕ С‚РµРєСЃС‚Сѓ (OCR) РґР»СЏ С–РЅС‚РµСЂР°РєС‚РёРІРЅРѕС— С€РєС–Р»СЊРЅРѕС— РґРѕС€РєРё.
РќР° Р·РѕР±СЂажеРЅРЅС– РІС–Рґ СЂСѓРєРё РЅР°РїРёСЃР°РЅРѕ С‚РµРєСЃС‚ (СѓРєСЂР°С—РЅСЃСЊРєРѕСЋ, Р°РЅРіР»С–Р№СЃСЊРєРѕСЋ, С†РёС„СЂРё, Р·РЅР°РєРё абРѕ С„РѕСЂРјСѓР»Рё).
РўРІРѕС” заРІРґР°РЅРЅСЏ вЂ” РўРћР§РќРћ РїСЂРѕС‡РёС‚Р°С‚Рё РЅР°РїРёСЃР°РЅРµ РІС–Рґ СЂСѓРєРё.
РџРѕРІРµСЂРЅРё Р’РРљР›ЮЧРќРћ СЂРѕР·РїС–Р·РЅР°РЅРёР№ С‚РµРєСЃС‚ без лаРїРѕРє, РїСЂРёРІС–С‚Р°РЅСЊ С‚Р° заР№РІРёС… РїРѕСЏСЃРЅРµРЅСЊ.
""";
            var request = new List<AssistantMessage>
            {
                new() { Role = "system", Text = prompt },
                new()
                {
                    Role = "user",
                    Text = "Р РѕР·РїС–Р·РЅР°Р№ СЂСѓРєРѕРїРёСЃРЅРёР№ С‚РµРєСЃС‚ РЅР° Р·РѕР±СЂажеРЅРЅС– С‚Р° РїРѕРІРµСЂРЅРё С‚С–Р»СЊРєРё СЂРѕР·РїС–Р·РЅР°РЅС– СЃРёРјРІРѕР»Рё абРѕ СЃР»РѕРІР°.",
                    Images = [new AssistantImage { Base64 = base64Png, MimeType = "image/png", Name = "ink.png" }]
                }
            };

            var sb = new StringBuilder();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await AssistantClient.StreamAsync(token, "fast", request, delta =>
            {
                sb.Append(delta);
                return Task.CompletedTask;
            }, cts.Token);

            var raw = sb.ToString().Trim();
            if (raw.StartsWith("```"))
            {
                var lines = raw.Split('\n');
                raw = string.Join('\n', lines.Where(l => !l.Trim().StartsWith("```"))).Trim();
            }
            raw = raw.Trim('\"', '\'', '\u00AB', '\u00BB', '`', ' ');
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return null;
        }
    }
}
