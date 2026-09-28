using Microsoft.Web.WebView2.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using IconPath = System.Windows.Shapes.Path;
using Orientation = System.Windows.Controls.Orientation;
using VerticalAlignment = System.Windows.VerticalAlignment;
using WpfControl = System.Windows.Controls.Control;

namespace Luma;

/// <summary>
/// Small sheet window used for provider sign-in flows (Google, Apple, GitHub…).
/// Sites open those with window.open(), so they need a real child window: handing them a
/// plain tab breaks window.opener and the provider reports "please enable popup windows".
/// The chrome is drawn by us so the sheet matches the rest of Luma.
/// </summary>
public sealed class PopupWindow : Window
{
    public WebView2 View { get; } = new();
    private readonly TextBlock _domain = new()
    {
        Foreground = Paint("#D4D4D8"),
        FontSize = 12.5,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly Border _lock = new()
    {
        Width = 18,
        Height = 18,
        CornerRadius = new CornerRadius(9),
        Margin = new Thickness(0, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static SolidColorBrush Paint(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private static Brush ThemeBrush(Window owner, string key, string fallback) => owner.TryFindResource(key) as Brush ?? Paint(fallback);

    public PopupWindow(Window owner, double width, double height, Brush accent)
    {
        Owner = owner;
        Width = width;
        Height = height + 44;
        MinWidth = 360;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        // FontFamily lives on Control, not on FrameworkElement.
        FontFamily = (Application.Current?.MainWindow as WpfControl)?.FontFamily ?? new FontFamily("Segoe UI");

        _lock.Background = accent;

        var header = new Grid { Height = 44, Background = ThemeBrush(owner, "PanelBrush", "#1B1C1F") };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(_lock);
        title.Children.Add(_domain);
        Grid.SetColumn(title, 0);
        header.Children.Add(title);

        var close = new Button
        {
            Width = 32,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Paint("#D4D4D8"),
            Content = new IconPath
            {
                Width = 11,
                Height = 11,
                Stretch = Stretch.Uniform,
                Stroke = Paint("#D4D4D8"),
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = Application.Current?.TryFindResource("IconX") as Geometry ?? Geometry.Empty,
            },
        };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 2);
        header.Children.Add(close);

        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        View.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 16, 16, 21);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0);
        Grid.SetRow(View, 1);
        layout.Children.Add(header);
        layout.Children.Add(View);

        var card = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = ThemeBrush(owner, "SurfaceInsetBrush", "#17181A"),
            BorderBrush = ThemeBrush(owner, "BorderBrush", "#323338"),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Margin = new Thickness(12),
            Child = layout,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 28, ShadowDepth = 10, Opacity = .45 },
        };
        Content = card;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => { try { View.Dispose(); } catch { } };
    }

    /// <summary>Wires the freshly created CoreWebView2 up to the sheet chrome.</summary>
    public void Attach()
    {
        var web = View.CoreWebView2;
        if (web is null) return;
        web.Settings.UserAgent = MainWindow.ChromeUserAgent;
        web.Settings.IsStatusBarEnabled = false;
        web.Settings.AreDefaultContextMenusEnabled = false;
        web.WindowCloseRequested += (_, _) => Dispatcher.Invoke(Close);
        web.SourceChanged += (_, _) => Dispatcher.Invoke(() => _domain.Text = BrowserTab.DomainOf(web.Source ?? ""));
        web.DocumentTitleChanged += (_, _) => Dispatcher.Invoke(() => Title = web.DocumentTitle);
        // Keep nested pop-ups (consent screens) inside this same sheet.
        web.NewWindowRequested += (_, e) => { e.Handled = true; web.Navigate(e.Uri); };
        _domain.Text = BrowserTab.DomainOf(web.Source ?? "");
    }
}
