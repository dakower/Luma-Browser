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
    private void RenderPanes()
    {
        if (!IsLoaded) return;
        var live = _spaces.SelectMany(space => space.Tabs).ToHashSet();
        foreach (var stale in _tabPaneHosts.Keys.Where(tab => !live.Contains(tab)).ToList())
        {
            PaneHost.Children.Remove(_tabPaneHosts[stale]); _tabPaneHosts.Remove(stale); _tabPaneHostSplit.Remove(stale); _tabPaneIndicators.Remove(stale);
        }
        var tab = CurrentTab;
        if (tab is null || tab.IsHome) { foreach (FrameworkElement child in PaneHost.Children) child.Visibility = Visibility.Collapsed; return; }
        var split = tab.IsSplit && tab.SecondaryView is not null;
        if (!_tabPaneHosts.TryGetValue(tab, out var host) || !_tabPaneHostSplit.TryGetValue(tab, out var wasSplit) || wasSplit != split)
        {
            if (host is not null) PaneHost.Children.Remove(host);
            host = BuildPaneHost(tab, split); _tabPaneHosts[tab] = host; _tabPaneHostSplit[tab] = split; PaneHost.Children.Add(host);
        }
        foreach (FrameworkElement child in PaneHost.Children) child.Visibility = ReferenceEquals(child, host) ? Visibility.Visible : Visibility.Collapsed;
        if (_tabPaneIndicators.TryGetValue(tab, out var indicators)) { _leftIndicator = indicators.Left; _rightIndicator = indicators.Right; } else _leftIndicator = _rightIndicator = null;
        UpdateIndicators();
    }

    private Grid BuildPaneHost(BrowserTab tab, bool split)
    {
        var host = new Grid { ClipToBounds = true, Visibility = Visibility.Collapsed };
        host.SetResourceReference(Panel.BackgroundProperty, "SurfaceInsetBrush");
        if (!split || tab.SecondaryView is null) { Detach(tab.View); Stretch(tab.View); host.Children.Add(tab.View); return host; }
        var grid = new Grid { ClipToBounds = true };
        grid.SetResourceReference(Panel.BackgroundProperty, "SurfaceInsetBrush");
        var leftColumn = new ColumnDefinition { Width = new GridLength(tab.SplitRatio, GridUnitType.Star), MinWidth = 120 };
        var rightColumn = new ColumnDefinition { Width = new GridLength(1 - tab.SplitRatio, GridUnitType.Star), MinWidth = 120 };
        grid.ColumnDefinitions.Add(leftColumn); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) }); grid.ColumnDefinitions.Add(rightColumn);
        var left = Pane(tab, tab.View, "primary"); var leftMarker = _leftIndicator!;
        var right = Pane(tab, tab.SecondaryView, "secondary"); var rightMarker = _rightIndicator!;
        Grid.SetColumn(right, 2); grid.Children.Add(left); grid.Children.Add(right); _tabPaneIndicators[tab] = (leftMarker, rightMarker);
        var divider = new Border { Background = Brushes.Transparent, Cursor = Cursors.SizeWE, Child = new Border { Width = 1, Background = Brush("#35FFFFFF"), HorizontalAlignment = WpfHorizontalAlignment.Center } };
        divider.MouseLeftButtonDown += (_, e) => { _dividerDragging = true; divider.CaptureMouse(); e.Handled = true; };
        divider.MouseMove += (_, e) => { if (!_dividerDragging || e.LeftButton != MouseButtonState.Pressed) return; tab.SplitRatio = Math.Clamp(e.GetPosition(grid).X / Math.Max(1, grid.ActualWidth - 9), .25, .75); leftColumn.Width = new GridLength(tab.SplitRatio, GridUnitType.Star); rightColumn.Width = new GridLength(1 - tab.SplitRatio, GridUnitType.Star); };
        divider.MouseLeftButtonUp += (_, e) => { _dividerDragging = false; divider.ReleaseMouseCapture(); Save(); e.Handled = true; };
        Grid.SetColumn(divider, 1); grid.Children.Add(divider); host.Children.Add(grid); return host;
    }
    private Grid Pane(BrowserTab tab, WebView2 view, string side)
    {
        Detach(view); Stretch(view); var grid = new Grid { ClipToBounds = true };
        grid.SetResourceReference(Panel.BackgroundProperty, "SurfaceInsetBrush");
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(23) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid(); header.SetResourceReference(Panel.BackgroundProperty, "PanelBrush"); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var domain = new TextBlock { Text = BrowserTab.DomainOf(side == "secondary" ? tab.SecondaryUrl : tab.FullUrl), FontSize = 10, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = WpfVerticalAlignment.Center };
        domain.SetResourceReference(TextBlock.ForegroundProperty, "ChromeMuted"); header.Children.Add(domain);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(PaneButton("IconExternalLink", tab, side, PaneDetach_Click)); actions.Children.Add(PaneButton("IconX", tab, side, PaneClose_Click)); Grid.SetColumn(actions, 1); header.Children.Add(actions);
        var marker = new Border { Height = 1, VerticalAlignment = WpfVerticalAlignment.Bottom }; header.Children.Add(marker); if (side == "secondary") _rightIndicator = marker; else _leftIndicator = marker;
        header.MouseLeftButtonDown += (_, _) => { tab.ActivePane = side; UpdateChrome(); UpdateIndicators(); };
        grid.Children.Add(header); Grid.SetRow(view, 1); grid.Children.Add(view); return grid;
    }
    private Button PaneButton(string iconKey, BrowserTab tab, string side, RoutedEventHandler action) { var icon = new IconPath { Width = 11, Height = 11, Stretch = System.Windows.Media.Stretch.Uniform, Data = (Geometry)FindResource(iconKey), StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round }; icon.SetResourceReference(IconPath.StrokeProperty, "ChromeSecondary"); var b = new Button { Content = icon, Width = 22, Height = 22, Style = (Style)FindResource("BareIconButton"), Tag = Tuple.Create(tab, side) }; b.Click += action; return b; }
    private static void Detach(WebView2 view) { if (view.Parent is Panel p) p.Children.Remove(view); else if (view.Parent is ContentControl c) c.Content = null; }
    private static void Stretch(FrameworkElement e) { e.HorizontalAlignment = WpfHorizontalAlignment.Stretch; e.VerticalAlignment = WpfVerticalAlignment.Stretch; e.MinWidth = e.MinHeight = 0; }
    private void UpdateIndicators() { var accent = (Brush)Resources["AccentBrush"]; if (_leftIndicator is not null) _leftIndicator.Background = CurrentTab?.ActivePane == "primary" ? accent : Brushes.Transparent; if (_rightIndicator is not null) _rightIndicator.Background = CurrentTab?.ActivePane == "secondary" ? accent : Brushes.Transparent; }

    private async Task EnableSplitAsync(BrowserTab tab, string url, bool focusSecond)
    {
        if (tab.IsInternal) { ShowToast("Split View недоступен", "Для внутренней страницы"); return; }
        var target = Normalize(url);
        if (tab.SecondaryView is null)
        {
            tab.SecondaryView = new WebView2(); tab.SecondaryUrl = target; tab.SecondaryTitle = Domain(target); tab.SplitRatio = .5; tab.NotifySplitChanged();
            if (tab == CurrentTab) RenderPanes();
            await ConfigureViewAsync(tab, tab.SecondaryView, target);
        }
        else if (tab.SecondaryView.CoreWebView2 is null)
        {
            tab.SecondaryUrl = target; if (tab == CurrentTab) RenderPanes(); await ConfigureViewAsync(tab, tab.SecondaryView, target);
        }
        else tab.SecondaryView.CoreWebView2.Navigate(target);
        if (focusSecond) { tab.ActivePane = "secondary"; tab.SecondaryView.Focus(); }
        RenderPanes(); UpdateChrome(); Save();
    }
    private void ClosePane(BrowserTab tab, string side) { if (!tab.IsSplit) return; if (side == "primary") tab.PromoteSecondary(); else tab.CloseSecondary(); RenderPanes(); UpdateChrome(); Save(); }
    private async void PaneDetach_Click(object sender, RoutedEventArgs e) { if (sender is not Button { Tag: Tuple<BrowserTab, string> pair }) return; var url = pair.Item2 == "secondary" ? pair.Item1.SecondaryUrl : pair.Item1.FullUrl; var title = pair.Item2 == "secondary" ? pair.Item1.SecondaryTitle : pair.Item1.Title; ClosePane(pair.Item1, pair.Item2); var tab = await AddTabAsync(url); tab.Title = title; }
    private void PaneClose_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: Tuple<BrowserTab, string> pair }) ClosePane(pair.Item1, pair.Item2); }

    /// <summary>
    /// Keeps live tabs and sidebar PINNED rows in sync: every pinned row owns the tab it was
    /// pinned from (matched by runtime id, or by URL after a restart), and that tab stays out
    /// of the TABS section until the row is unpinned.
    /// </summary>
}
