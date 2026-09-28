using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Luma;

public partial class FloatingVideoWindow : Window
{
    private readonly Action<string, double> _command;
    private readonly Action _closedByUser;
    private bool _ownerClosing;
    private bool _seeking;
    private bool _chromeVisible;
    public bool IsClosed { get; private set; }
    private double _duration;
    private string _previewSource = "";

    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    public FloatingVideoWindow(Action<string, double> command, Action closedByUser)
    {
        _command = command;
        _closedByUser = closedByUser;
        InitializeComponent();
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowProc);
        Loaded += (_, _) => PlaceNearWorkArea();
        Closed += (_, _) => { IsClosed = true; if (!_ownerClosing) _closedByUser(); };
    }

    public void SyncTheme(Window owner)
    {
        Copy(owner, "SurfaceBrush", "VideoSurface");
        Copy(owner, "SurfaceRaisedBrush", "VideoRaised");
        Copy(owner, "SurfaceInsetBrush", "VideoInset");
        Copy(owner, "BorderStrongBrush", "VideoBorder");
        Copy(owner, "AccentBrush", "VideoAccent");
        Copy(owner, "ChromeMuted", "VideoMuted");
    }

    private void Copy(Window owner, string source, string target)
    {
        if (owner.TryFindResource(source) is Brush brush) Resources[target] = brush.CloneCurrentValue();
    }

    public void Update(BrowserTab tab)
    {
        TitleText.Text = string.IsNullOrWhiteSpace(tab.MediaTitle) ? tab.DisplayTitle : tab.MediaTitle;
        SourceText.Text = string.IsNullOrWhiteSpace(tab.MediaArtist) ? tab.Domain : tab.MediaArtist;
        PositionText.Text = tab.MediaPositionText;
        _duration = tab.MediaDuration;
        RemainingText.Text = "−" + Format(Math.Max(0, _duration - tab.MediaPosition));
        if (!_seeking) SeekSlider.Value = tab.MediaProgress;
        ToggleIcon.Data = (Geometry)FindResource(tab.MediaPlaying ? "IconPause" : "IconPlay");
        SetPreview(string.IsNullOrWhiteSpace(tab.MediaArtwork) ? tab.FaviconUrl : tab.MediaArtwork);
    }

    public void SetFrame(byte[] jpeg)
    {
        try
        {
            using var stream = new MemoryStream(jpeg, false);
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            VideoFrame.Source = image;
            EmptyState.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    public void CloseFromOwner()
    {
        _ownerClosing = true;
        Close();
    }

    private void SetPreview(string source)
    {
        if (string.Equals(_previewSource, source, StringComparison.Ordinal)) return;
        _previewSource = source;
        try
        {
            if (string.IsNullOrWhiteSpace(source)) throw new InvalidOperationException();
            var image = new BitmapImage();
            image.BeginInit(); image.DecodePixelWidth = 160; image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(source, UriKind.RelativeOrAbsolute); image.EndInit();
            PreviewImage.Source = image; PreviewImage.Visibility = Visibility.Visible; PreviewFallback.Visibility = Visibility.Collapsed;
        }
        catch { PreviewImage.Source = null; PreviewImage.Visibility = Visibility.Collapsed; PreviewFallback.Visibility = Visibility.Visible; }
    }

    private void PlaceNearWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left + 16, area.Right - Width - 24);
        Top = Math.Max(area.Top + 16, area.Bottom - Height - 24);
    }

    private void VideoSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindInteractive(e.OriginalSource as DependencyObject)) return;
        if (e.ClickCount == 2)
        {
            _chromeVisible = !_chromeVisible;
            InfoPanel.Visibility = _chromeVisible ? Visibility.Visible : Visibility.Collapsed;
            ControlsPanel.Visibility = _chromeVisible ? Visibility.Visible : Visibility.Collapsed;
            e.Handled = true;
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
            e.Handled = true;
        }
    }

    private static bool FindInteractive(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Slider) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void ReturnToLuma_Click(object sender, RoutedEventArgs e) => _command("return-fullscreen", 0);
    private void Toggle_Click(object sender, RoutedEventArgs e) => _command("toggle", 0);
    private void Back_Click(object sender, RoutedEventArgs e) => _command("seek-relative", -15);
    private void Forward_Click(object sender, RoutedEventArgs e) => _command("seek-relative", 15);
    private void SeekSlider_DragStart(object sender, MouseButtonEventArgs e) => _seeking = true;
    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_seeking && _duration > 0)
        {
            var position = e.NewValue * _duration;
            PositionText.Text = Format(position);
            RemainingText.Text = "−" + Format(Math.Max(0, _duration - position));
        }
    }
    private void SeekSlider_DragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_seeking) return;
        _seeking = false;
        if (_duration > 0) _command("seek", SeekSlider.Value * _duration);
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmNcHitTest) return IntPtr.Zero;
        var packed = lParam.ToInt64();
        var point = PointFromScreen(new Point(unchecked((short)(packed & 0xffff)), unchecked((short)((packed >> 16) & 0xffff))));
        const double grip = 18;
        var hit = point.Y <= grip && point.X <= grip ? HtTopLeft
            : point.Y <= grip && point.X >= ActualWidth - grip ? HtTopRight
            : point.Y >= ActualHeight - grip && point.X <= grip ? HtBottomLeft
            : point.Y >= ActualHeight - grip && point.X >= ActualWidth - grip ? HtBottomRight
            : HtClient;
        handled = true;
        return new IntPtr(hit);
    }

    private static string Format(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.Hours > 0 ? $"{time.Hours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";
    }
}
