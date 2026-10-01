using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using FontFamily = System.Windows.Media.FontFamily;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Luma;

public partial class MainWindow
{
    private void InitializeDashboardStudio()
    {
        ApplyHomeDashboardSettings();
    }

    public void ApplyHomeDashboardSettings()
    {
        if (HomeSurfaceContent is null) return;

        // 1. Font Family
        var familyName = string.IsNullOrWhiteSpace(_state.HomeFontFamily)
            ? "Segoe UI Variable Display, Segoe UI"
            : _state.HomeFontFamily;
        var font = new FontFamily(familyName);

        if (HomeClockText is not null) HomeClockText.FontFamily = font;
        if (HomeDateText is not null) HomeDateText.FontFamily = font;
        if (HomeHeadlineText is not null) HomeHeadlineText.FontFamily = font;
        if (HomeSearchBox is not null) HomeSearchBox.FontFamily = font;
        if (HomeSearchPlaceholder is not null) HomeSearchPlaceholder.FontFamily = font;

        // 2. Search Shape
        var shape = _state.HomeSearchShape ?? "pill";
        var corner = shape switch
        {
            "square" => new CornerRadius(4),
            "rounded" => new CornerRadius(14),
            _ => new CornerRadius(27)
        };
        if (HomeSearchShell is not null) HomeSearchShell.CornerRadius = corner;

        // 3. Search Style
        var style = _state.HomeSearchStyle ?? "solid";
        if (HomeSearchShell is not null && HomeSearchShadow is not null)
        {
            switch (style)
            {
                case "glass":
                    HomeSearchShell.Background = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF));
                    HomeSearchShell.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
                    HomeSearchShell.BorderThickness = new Thickness(1.2);
                    HomeSearchShadow.Color = Colors.Black;
                    HomeSearchShadow.BlurRadius = 30;
                    HomeSearchShadow.Opacity = 0.45;
                    break;
                case "outline":
                    HomeSearchShell.Background = Brushes.Transparent;
                    HomeSearchShell.BorderBrush = (Brush)FindResource("AccentBrush");
                    HomeSearchShell.BorderThickness = new Thickness(1.8);
                    HomeSearchShadow.Color = Colors.Transparent;
                    HomeSearchShadow.Opacity = 0;
                    break;
                case "glow":
                    HomeSearchShell.Background = (Brush)FindResource("SurfaceBrush");
                    HomeSearchShell.BorderBrush = (Brush)FindResource("AccentBrush");
                    HomeSearchShell.BorderThickness = new Thickness(1.5);
                    var accentBrush = FindResource("AccentBrush") as SolidColorBrush;
                    HomeSearchShadow.Color = accentBrush?.Color ?? Color.FromRgb(0x75, 0x6B, 0xB7);
                    HomeSearchShadow.BlurRadius = 26;
                    HomeSearchShadow.Opacity = 0.55;
                    break;
                default: // "solid"
                    HomeSearchShell.Background = (Brush)FindResource("SurfaceBrush");
                    HomeSearchShell.BorderBrush = (Brush)FindResource("BorderBrush");
                    HomeSearchShell.BorderThickness = new Thickness(1);
                    HomeSearchShadow.Color = Colors.Black;
                    HomeSearchShadow.BlurRadius = 24;
                    HomeSearchShadow.Opacity = 0.3;
                    break;
            }
        }

        // 4. Element Visibility
        if (HomeBrandPanel is not null)
            HomeBrandPanel.Visibility = _state.HomeShowLogo ? Visibility.Visible : Visibility.Collapsed;
        if (HomeClockPanel is not null)
            HomeClockPanel.Visibility = _state.HomeShowClock ? Visibility.Visible : Visibility.Collapsed;
        if (HomeHeadlineText is not null)
            HomeHeadlineText.Visibility = _state.HomeShowHeadline ? Visibility.Visible : Visibility.Collapsed;
        if (HomeSearchShell is not null)
            HomeSearchShell.Visibility = _state.HomeShowSearch ? Visibility.Visible : Visibility.Collapsed;
        if (HomeQuickPillsGrid is not null)
            HomeQuickPillsGrid.Visibility = _state.HomeShowQuickPills ? Visibility.Visible : Visibility.Collapsed;
        if (HomeMediaCard is not null)
        {
            if (!_state.HomeShowMedia) HomeMediaCard.Visibility = Visibility.Collapsed;
            else if (_nowPlaying is not null) HomeMediaCard.Visibility = Visibility.Visible;
        }
        if (HomeRecentSitesGrid is not null)
        {
            if (!_state.HomeShowRecent) HomeRecentSitesGrid.Visibility = Visibility.Collapsed;
            else UpdateHomeRecentSites();
        }

        // 5. Wallpaper & Dim
        if (HomeCustomWallpaper is not null && HomeWallpaperDimOverlay is not null)
        {
            if (!string.IsNullOrWhiteSpace(_state.HomeWallpaperPath) && File.Exists(_state.HomeWallpaperPath))
            {
                try
                {
                    if (HomeCustomWallpaper.Source is null || (HomeCustomWallpaper.Source as BitmapImage)?.UriSource?.LocalPath != _state.HomeWallpaperPath)
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(_state.HomeWallpaperPath);
                        bmp.EndInit();
                        HomeCustomWallpaper.Source = bmp;
                    }
                    HomeCustomWallpaper.Visibility = Visibility.Visible;
                    HomeWallpaperDimOverlay.Opacity = Math.Clamp(_state.HomeWallpaperDim, 0, 0.9);
                    HomeWallpaperDimOverlay.Visibility = Visibility.Visible;
                }
                catch
                {
                    HomeCustomWallpaper.Source = null;
                    HomeCustomWallpaper.Visibility = Visibility.Collapsed;
                    HomeWallpaperDimOverlay.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                HomeCustomWallpaper.Source = null;
                HomeCustomWallpaper.Visibility = Visibility.Collapsed;
                HomeWallpaperDimOverlay.Visibility = Visibility.Collapsed;
            }
        }

        SyncStudioControls();
    }

    private void SyncStudioControls()
    {
        if (HomeToggleLogo is not null) HomeToggleLogo.IsChecked = _state.HomeShowLogo;
        if (HomeToggleClock is not null) HomeToggleClock.IsChecked = _state.HomeShowClock;
        if (HomeToggleHeadline is not null) HomeToggleHeadline.IsChecked = _state.HomeShowHeadline;
        if (HomeToggleSearch is not null) HomeToggleSearch.IsChecked = _state.HomeShowSearch;
        if (HomeTogglePills is not null) HomeTogglePills.IsChecked = _state.HomeShowQuickPills;
        if (HomeToggleMedia is not null) HomeToggleMedia.IsChecked = _state.HomeShowMedia;
        if (HomeToggleRecent is not null) HomeToggleRecent.IsChecked = _state.HomeShowRecent;

        if (HomeDimSlider is not null)
        {
            HomeDimSlider.Value = _state.HomeWallpaperDim;
            if (HomeDimPercentText is not null)
                HomeDimPercentText.Text = $"{(int)(_state.HomeWallpaperDim * 100)}%";
        }

        HighlightStudioSelections();
    }

    private void HighlightStudioSelections()
    {
        // Highlight active font card
        var fontButtons = new[] { HomeFontSegoe, HomeFontInter, HomeFontCascadia, HomeFontGeorgia, HomeFontBahnschrift, HomeFontTrebuchet };
        foreach (var btn in fontButtons)
        {
            if (btn is null) continue;
            var isSelected = string.Equals(btn.Tag as string, _state.HomeFontFamily, StringComparison.OrdinalIgnoreCase);
            btn.BorderBrush = isSelected ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrush");
            btn.BorderThickness = isSelected ? new Thickness(1.8) : new Thickness(1);
        }

        // Highlight active shape button
        var shapeButtons = new[] { HomeShapePill, HomeShapeRounded, HomeShapeSquare };
        foreach (var btn in shapeButtons)
        {
            if (btn is null) continue;
            var isSelected = string.Equals(btn.Tag as string, _state.HomeSearchShape, StringComparison.OrdinalIgnoreCase);
            btn.BorderBrush = isSelected ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrush");
            btn.Background = isSelected ? (Brush)FindResource("AccentSoftBrush") : (Brush)FindResource("SurfaceRaisedBrush");
        }

        // Highlight active style button
        var styleButtons = new[] { HomeStyleSolid, HomeStyleGlass, HomeStyleOutline, HomeStyleGlow };
        foreach (var btn in styleButtons)
        {
            if (btn is null) continue;
            var isSelected = string.Equals(btn.Tag as string, _state.HomeSearchStyle, StringComparison.OrdinalIgnoreCase);
            btn.BorderBrush = isSelected ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrush");
            btn.Background = isSelected ? (Brush)FindResource("AccentSoftBrush") : (Brush)FindResource("SurfaceRaisedBrush");
        }
    }

    private void HomeCustomize_Click(object sender, RoutedEventArgs e)
    {
        CloseSupportChat();
        SyncStudioControls();
        HomeCustomizeDrawerOverlay.Visibility = Visibility.Visible;
    }

    private void HomeCustomizeClose_Click(object sender, RoutedEventArgs e)
    {
        HomeCustomizeDrawerOverlay.Visibility = Visibility.Collapsed;
    }

    private void HomeCustomizeDrawer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // Prevent click from closing overlay when clicking inside drawer
    }

    private void HomeSearchShape_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string shape)
        {
            _state.HomeSearchShape = shape;
            ApplyHomeDashboardSettings();
            Save();
        }
    }

    private void HomeSearchStyle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string style)
        {
            _state.HomeSearchStyle = style;
            ApplyHomeDashboardSettings();
            Save();
        }
    }

    private void HomeFontOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string font)
        {
            _state.HomeFontFamily = font;
            ApplyHomeDashboardSettings();
            Save();
        }
    }

    private void HomeChooseWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите фоновое изображение",
            Filter = "Изображения (*.jpg;*.jpeg;*.png;*.webp;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp|Все файлы (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            _state.HomeWallpaperPath = dlg.FileName;
            ApplyHomeDashboardSettings();
            Save();
        }
    }

    private void HomeResetWallpaper_Click(object sender, RoutedEventArgs e)
    {
        _state.HomeWallpaperPath = null;
        ApplyHomeDashboardSettings();
        Save();
    }

    private void HomeDimSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_state is null) return;
        _state.HomeWallpaperDim = Math.Round(e.NewValue, 2);
        if (HomeWallpaperDimOverlay is not null && HomeWallpaperDimOverlay.Visibility == Visibility.Visible)
            HomeWallpaperDimOverlay.Opacity = _state.HomeWallpaperDim;
        if (HomeDimPercentText is not null)
            HomeDimPercentText.Text = $"{(int)(_state.HomeWallpaperDim * 100)}%";
        Save();
    }

    private void HomeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is string tag)
        {
            var val = cb.IsChecked == true;
            switch (tag)
            {
                case "logo": _state.HomeShowLogo = val; break;
                case "clock": _state.HomeShowClock = val; break;
                case "headline": _state.HomeShowHeadline = val; break;
                case "search": _state.HomeShowSearch = val; break;
                case "pills": _state.HomeShowQuickPills = val; break;
                case "media": _state.HomeShowMedia = val; break;
                case "recent": _state.HomeShowRecent = val; break;
            }
            ApplyHomeDashboardSettings();
            Save();
        }
    }
}
