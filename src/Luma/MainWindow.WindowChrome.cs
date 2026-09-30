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
    private void SetSidebar(bool visible, bool animate, bool persist = true, bool peek = false)
    {
        _hoverHideTimer.Stop();
        _sidebarVisible = visible;
        if (persist) { _state.SidebarVisible = visible; _stateStore.Save(); _sidebarAutoShown = false; }
        else _sidebarAutoShown = visible;

        if (peek)
        {
            if (visible) ShowPeek(animate); else HidePeek(animate);
            EdgeReveal.Visibility = Visibility.Collapsed;
            return;
        }
        // A pinned open/close owns the layout again, so drop any floating peek first.
        if (_peekActive) { _peekActive = false; SidebarPeekSlide.BeginAnimation(TranslateTransform.XProperty, null); FinishPeek(); }

        var target = visible ? SidebarWidth : 0d;
        var current = SidebarColumn.Width.IsAbsolute ? SidebarColumn.Width.Value : target;
        BeginAnimation(SidebarSlideProperty, null);
        if (animate && _state.AnimationsEnabled && Math.Abs(current - target) > 0.5)
        {
            Sidebar.Opacity = 1;
            Sidebar.Visibility = Visibility.Visible;
            Sidebar.IsHitTestVisible = true;
            var slide = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            slide.Completed += (_, _) =>
            {
                BeginAnimation(SidebarSlideProperty, null);
                var settled = _sidebarVisible || _sidebarAutoShown ? SidebarWidth : 0d;
                SidebarColumn.Width = new GridLength(settled);
                Sidebar.Opacity = settled > 0 ? 1 : 0;
                Sidebar.Visibility = settled > 0 ? Visibility.Visible : Visibility.Collapsed;
                Sidebar.IsHitTestVisible = settled > 0;
            };
            BeginAnimation(SidebarSlideProperty, slide);
        }
        else
        {
            SidebarColumn.Width = new GridLength(target);
            Sidebar.Opacity = visible ? 1 : 0;
            Sidebar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            Sidebar.IsHitTestVisible = visible;
        }
        EdgeReveal.Visibility = Visibility.Collapsed;
    }

    /// <summary>Real fullscreen for any site: the titlebar and sidebar fold away and the window
    /// covers the whole monitor, including the taskbar.</summary>
    private void SetFullscreen(bool on)
    {
        if (_fullscreen == on) return;
        _fullscreen = on;
        if (on)
        {
            _fsSidebarWasVisible = _sidebarVisible;
            _fsBounds = new Rect(Left, Top, Width, Height);
            _fsWasMaximized = _manualMaximized;
            SetSidebar(false, false, false);
            EdgeReveal.Visibility = Visibility.Collapsed;
            TitleBar.Visibility = Visibility.Collapsed;
            RootShell.RowDefinitions[0].Height = new GridLength(0);
            var screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            var bounds = screen.Bounds;
            var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
            var topLeft = transform.Transform(new Point(bounds.Left, bounds.Top));
            var size = transform.Transform(new Point(bounds.Width, bounds.Height));
            WindowState = WindowState.Normal;
            Left = topLeft.X; Top = topLeft.Y; Width = size.X; Height = size.Y;
        }
        else
        {
            TitleBar.Visibility = Visibility.Visible;
            RootShell.RowDefinitions[0].Height = new GridLength(52);
            Left = _fsBounds.Left; Top = _fsBounds.Top; Width = _fsBounds.Width; Height = _fsBounds.Height;
            _manualMaximized = _fsWasMaximized;
            SetSidebar(_fsSidebarWasVisible, false, false);
        }
    }
    private const double SidebarWidth = 270;
    private bool _peekActive;
    private double _peekHeight, _peekOriginX, _peekOriginY;

    /// <summary>
    /// The hover reveal used to animate the layout column, which resized the native page window
    /// on every animation frame - that is the tearing and the stutter. Now the panel is lifted
    /// into a Popup (its own top-level window, so it paints above the page) and slides in with a
    /// transform. Nothing under it is resized, relaid out or repainted.
    /// </summary>
    private void ShowPeek(bool animate)
    {
        if (_peekActive) return;
        _peekActive = true;
        SidebarPeekSlide.BeginAnimation(TranslateTransform.XProperty, null);

        Sidebar.Visibility = Visibility.Visible;
        Sidebar.IsHitTestVisible = true;

        if (!ReferenceEquals(Sidebar.Parent, SidebarPeekHost))
        {
            SidebarColumn.Width = new GridLength(0);
            if (Sidebar.Parent is Grid host) host.Children.Remove(Sidebar);
            Sidebar.Width = SidebarWidth;
            Sidebar.Opacity = 1;
            SidebarPeekHost.Child = Sidebar;
        }

        SyncPeekBounds();
        SidebarPeekPopup.IsOpen = true;
        var duration = TimeSpan.FromMilliseconds(animate && _state.AnimationsEnabled ? 240 : 0);
        SidebarPeekSlide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(SidebarPeekSlide.X, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void HidePeek(bool animate)
    {
        if (!_peekActive) { FinishPeek(); return; }
        _peekActive = false;
        Sidebar.IsHitTestVisible = false;
        var duration = TimeSpan.FromMilliseconds(animate && _state.AnimationsEnabled ? 210 : 0);
        var slide = new DoubleAnimation(SidebarPeekSlide.X, -SidebarWidth, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        slide.Completed += (_, _) => FinishPeek();
        SidebarPeekSlide.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    /// <summary>Puts the panel back into the layout once the slide-out has finished.</summary>
    private void FinishPeek()
    {
        if (_peekActive) return;
        SidebarPeekPopup.IsOpen = false;
        if (ReferenceEquals(Sidebar.Parent, SidebarPeekHost))
        {
            SidebarPeekHost.Child = null;
            Sidebar.Width = double.NaN;
            Grid.SetColumn(Sidebar, 0);
            if (!BodyGrid.Children.Contains(Sidebar)) BodyGrid.Children.Insert(0, Sidebar);
        }
        var pinned = _sidebarVisible && !_sidebarAutoShown;
        Sidebar.Opacity = pinned ? 1 : 0;
        Sidebar.Visibility = pinned ? Visibility.Visible : Visibility.Collapsed;
        Sidebar.IsHitTestVisible = pinned;
        SidebarColumn.Width = new GridLength(pinned ? SidebarWidth : 0);
    }

    /// <summary>Keeps the floating panel glued to the page area when the window moves or resizes.</summary>
    private void SyncPeekBounds()
    {
        var height = Math.Max(1, SiteShell.ActualHeight);
        Point origin;
        try { origin = SiteShell.PointToScreen(new Point(0, 0)); }
        catch { return; }
        if (Math.Abs(height - _peekHeight) > 0.5) { SidebarPeekClip.Height = height; _peekHeight = height; }
        if (Math.Abs(origin.X - _peekOriginX) > 0.5 || Math.Abs(origin.Y - _peekOriginY) > 0.5)
        {
            _peekOriginX = origin.X; _peekOriginY = origin.Y;
            if (SidebarPeekPopup.IsOpen) { SidebarPeekPopup.HorizontalOffset += 0.1; SidebarPeekPopup.HorizontalOffset -= 0.1; }
        }
    }
    private void CloseTransientUi() { foreach (var p in Popups()) p.IsOpen = false; _openMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, false); }
    private void CloseMenusExcept(Popup keep) { foreach (var p in Popups()) if (p != keep) p.IsOpen = false; _openMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, false); }
    private Popup[] Popups() => [SearchPopup, SiteMenuPopup, TranslatePopup, SplitPopup, DeleteFolderPopup, ConfirmPopup, MainMenuPopup, NewSpacePopup, NewFolderPopup, HistoryPopup, DownloadPopup];
    // Every toast uses the same fixed card width, so they always line up in the same
    // spot at the top-right corner of the page area instead of drifting with their text.
    private const double ToastWidth = 300, ToastMargin = 16;
    private void PositionToast() { ToastPopup.HorizontalOffset = Math.Max(ToastMargin, SiteShell.ActualWidth - ToastWidth - ToastMargin); ToastPopup.VerticalOffset = ToastMargin; }
    private void ShowToast(string title, string subtitle = "", bool error = false)
    {
        if (error) RecordTesterAction("Возникла ошибка Luma: " + title);
        ToastTitle.Text = title; ToastSubtitle.Text = subtitle;
        // Failures show a red cross: a green check on an error message looked wrong.
        ToastIcon.Data = (Geometry)FindResource(error ? "IconX" : "IconCheck");
        ToastIcon.Stroke = Brush(error ? "#F87171" : "#22C55E");
        ToastIconBg.Background = Brush(error ? "#3A2026" : "#1F3D2B");
        PositionToast(); ToastPopup.IsOpen = true;
        // Re-anchor once the popup has measured itself and whenever the window resizes.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PositionToast));
        _toastTimer.Stop(); _toastTimer.Start();
    }
    private void Copy(string value, string title) { try { Clipboard.SetText(value); ShowToast(title, value); } catch (Exception ex) { App.Log(ex); ShowToast("Не удалось скопировать", ex.Message, true); } }

}
