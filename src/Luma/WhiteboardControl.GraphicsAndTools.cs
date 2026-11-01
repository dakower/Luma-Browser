using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Orientation = System.Windows.Controls.Orientation;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfPath = System.Windows.Shapes.Path;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace Luma;

public partial class WhiteboardControl
{
    // =========================================================================
    // БЛОК 4: НИЖНЯ ПАНЕЛЬ — РЕЖИМ 2 (ГРАФІЧНІ ІНСТРУМЕНТИ, КРЕСЛЕННЯ ТА ОБ'ЄКТИ)
    // =========================================================================

    // -------------------------------------------------------------------------
    // 16. РОЗМИТТЯ ОБЛАСТІ (BLUR AREA — ПРИХОВУВАННЯ ВІДПОВІДІ З КЛІКОМ ДЛЯ РОЗКРИТТЯ)
    // -------------------------------------------------------------------------
    public void InsertBlurAnswerArea(double x = 160.0, double y = 200.0, double width = 220.0, double height = 80.0)
    {
        var blurContainer = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(180, 241, 245, 249)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            BorderThickness = new Thickness(1.5),
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true
        };

        var blurEffect = new BlurEffect { Radius = 18.0, RenderingBias = RenderingBias.Quality };
        blurContainer.Effect = blurEffect;

        var contentGrid = new Grid();
        var revealLabel = new TextBlock
        {
            Text = "👁️ Відповідь прихована\n(натисніть, щоб розкрити)",
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center
        };
        contentGrid.Children.Add(revealLabel);
        blurContainer.Child = contentGrid;

        bool isRevealed = false;
        blurContainer.MouseLeftButtonDown += (s, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
            {
                isRevealed = !isRevealed;
                if (isRevealed)
                {
                    blurContainer.Effect = null;
                    blurContainer.Background = new SolidColorBrush(Color.FromArgb(30, 34, 197, 94));
                    blurContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                    revealLabel.Text = "Розкрито ✅";
                    revealLabel.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
                }
                else
                {
                    blurContainer.Effect = blurEffect;
                    blurContainer.Background = new SolidColorBrush(Color.FromArgb(180, 241, 245, 249));
                    blurContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                    revealLabel.Text = "👁️ Відповідь прихована\n(натисніть, щоб розкрити)";
                    revealLabel.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                }
                e.Handled = true;
            }
        };

        Canvas.SetLeft(blurContainer, x);
        Canvas.SetTop(blurContainer, y);
        MakeElementDraggable(blurContainer);
        MainCanvas.Children.Add(blurContainer);

        ShowAiStatus("Створено інтерактивну область приховування відповіді");
    }

    private void BlurToolBtn_Click(object sender, RoutedEventArgs e)
    {
        InsertBlurAnswerArea();
    }

    // -------------------------------------------------------------------------
    // 15. ПРИБРАТИ ФОН ЗОБРАЖЕННЯ (MAGIC WAND BACKGROUND REMOVAL)
    // -------------------------------------------------------------------------
    public static BitmapSource RemoveImageBackground(BitmapSource source, Color? keyColor = null, byte tolerance = 35)
    {
        var formattedBmp = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = formattedBmp.PixelWidth;
        int height = formattedBmp.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        formattedBmp.CopyPixels(pixels, stride, 0);

        Color target = keyColor ?? Color.FromArgb(255, pixels[2], pixels[1], pixels[0]);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];

            int diffR = Math.Abs(r - target.R);
            int diffG = Math.Abs(g - target.G);
            int diffB = Math.Abs(b - target.B);

            if (diffR <= tolerance && diffG <= tolerance && diffB <= tolerance)
            {
                pixels[i + 3] = 0; // Alpha = 0
            }
        }

        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    private void MagicWandRemoveBackground_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Image? targetImage = null;
            foreach (UIElement child in MainCanvas.Children)
            {
                if (child is Border { Child: Image img })
                {
                    targetImage = img;
                }
            }

            if (targetImage != null && targetImage.Source is BitmapSource bs)
            {
                var cleanBmp = RemoveImageBackground(bs);
                targetImage.Source = cleanBmp;
                ShowAiStatus("Фон зображення успішно видалено чарівною паличкою ✨");
            }
            else
            {
                ShowAiStatus("Виберіть або додайте зображення для видалення фону");
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowAiStatus("Помилка обробки фону зображення");
        }
    }

    // -------------------------------------------------------------------------
    // 9. ЗАЛИВКА ФАРБОЮ (PAINT BUCKET ДЛЯ ЗАМКНЕНИХ ФІГУР)
    // -------------------------------------------------------------------------
    private bool _isPaintBucketActive = false;

    private void PaintBucketToolBtn_Click(object sender, RoutedEventArgs e)
    {
        _isPaintBucketActive = !_isPaintBucketActive;
        if (_isPaintBucketActive)
        {
            MainCanvas.Cursor = Cursors.Cross;
            ShowAiStatus("Заливка: натисніть на замкнену фігуру для фарбування");
        }
        else
        {
            MainCanvas.Cursor = Cursors.Pen;
        }
    }

    private void HandlePaintBucketClick(Point clickPos)
    {
        if (!_isPaintBucketActive) return;

        var fillBrush = new SolidColorBrush(_currentPenColor);
        fillBrush.Freeze();

        VisualTreeHelper.HitTest(MainCanvas, null, result =>
        {
            if (result.VisualHit is Shape shape)
            {
                shape.Fill = fillBrush;
                ShowAiStatus("Фігуру залито вибраним кольором 🎨");
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(clickPos));
    }

    // -------------------------------------------------------------------------
    // 8. МАГНІТНЕ ПРИЛИПАННЯ ВЕРШИН (SNAP-TO-VERTEX)
    // -------------------------------------------------------------------------
    private readonly List<Point> _magneticSnapVertices = new();

    public Point SnapPointToNearestVertex(Point rawPoint, double snapRadius = 14.0)
    {
        Point bestPoint = rawPoint;
        double minDistance = snapRadius;

        foreach (var v in _magneticSnapVertices)
        {
            double dist = Math.Sqrt(Math.Pow(v.X - rawPoint.X, 2) + Math.Pow(v.Y - rawPoint.Y, 2));
            if (dist < minDistance)
            {
                minDistance = dist;
                bestPoint = v;
            }
        }

        return bestPoint;
    }

    public void RegisterMagneticVertex(Point vertex)
    {
        if (!_magneticSnapVertices.Contains(vertex))
        {
            _magneticSnapVertices.Add(vertex);
            if (_magneticSnapVertices.Count > 120)
            {
                _magneticSnapVertices.RemoveAt(0);
            }
        }
    }

    // -------------------------------------------------------------------------
    // БЛОК 6: ПОВНА КАРТА ГАРЯЧИХ КЛАВІШ ТА ЖЕСТІВ СКРОЛУ/ЗУМУ
    // -------------------------------------------------------------------------
    private void HandleGlobalWhiteboardKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (ctrl && e.Key == Key.Z)
        {
            PerformUndo();
            e.Handled = true;
            return;
        }
        if ((ctrl && e.Key == Key.Y) || (ctrl && shift && e.Key == Key.Z))
        {
            PerformRedo();
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.C)
        {
            CopyAsImageToClipboard();
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.D0)
        {
            ResetZoom();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F11)
        {
            ToggleFullscreenMode();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            if (_isWhiteboardFullscreen) ToggleFullscreenMode();
            else SetActiveTool(WhiteboardTool.Pointer);
            e.Handled = true;
            return;
        }

        if (!IsAnyTextBoxFocused())
        {
            switch (e.Key)
            {
                case Key.I:
                    SwitchToTextMode();
                    e.Handled = true;
                    break;
                case Key.V:
                    SetActiveTool(WhiteboardTool.Pointer);
                    e.Handled = true;
                    break;
                case Key.P:
                    SwitchToGraphicsMode();
                    SetActiveTool(WhiteboardTool.Pencil);
                    e.Handled = true;
                    break;
                case Key.H:
                    SwitchToGraphicsMode();
                    SetActiveTool(WhiteboardTool.Highlighter);
                    e.Handled = true;
                    break;
                case Key.E:
                    SwitchToGraphicsMode();
                    SetActiveTool(WhiteboardTool.Eraser);
                    e.Handled = true;
                    break;
                case Key.L:
                    SwitchToGraphicsMode();
                    SetActiveTool(WhiteboardTool.Laser);
                    e.Handled = true;
                    break;
                case Key.R:
                    ToggleRuler();
                    e.Handled = true;
                    break;
            }
        }
    }

    private bool IsAnyTextBoxFocused()
    {
        var focused = Keyboard.FocusedElement;
        return focused is TextBox || focused is PasswordBox;
    }

    private void HandleCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (ctrl)
        {
            double zoomFactor = e.Delta > 0 ? 1.10 : 0.90;
            ApplyZoomAtPoint(zoomFactor, e.GetPosition(MainCanvas));
            e.Handled = true;
        }
        else if (shift)
        {
            if (MainCanvas.RenderTransform is not TranslateTransform tt)
            {
                tt = new TranslateTransform();
                MainCanvas.RenderTransform = tt;
            }
            tt.X += (e.Delta > 0 ? 30 : -30);
            e.Handled = true;
        }
    }

    private void ApplyZoomAtPoint(double factor, Point center)
    {
        double currentZoom = 1.0;
        if (MainCanvas.LayoutTransform is ScaleTransform st)
        {
            currentZoom = st.ScaleX;
        }
        double newZoom = Math.Clamp(currentZoom * factor, 0.25, 4.0);
        MainCanvas.LayoutTransform = new ScaleTransform(newZoom, newZoom);
        UpdateZoomPercentageDisplay(newZoom);
    }

    public void ResetZoom()
    {
        MainCanvas.LayoutTransform = new ScaleTransform(1.0, 1.0);
        UpdateZoomPercentageDisplay(1.0);
        ShowAiStatus("Масштаб 100%");
    }

    private void UpdateZoomPercentageDisplay(double zoom)
    {
        if (ZoomLevelText != null)
        {
            ZoomLevelText.Text = $"{(int)Math.Round(zoom * 100)}%";
        }
    }

    private void ZoomInBtn_Click(object sender, RoutedEventArgs e)
    {
        ApplyZoomAtPoint(1.15, new Point(MainCanvas.ActualWidth / 2, MainCanvas.ActualHeight / 2));
    }

    private void ZoomOutBtn_Click(object sender, RoutedEventArgs e)
    {
        ApplyZoomAtPoint(0.85, new Point(MainCanvas.ActualWidth / 2, MainCanvas.ActualHeight / 2));
    }

    private void ResetZoomBtn_Click(object sender, RoutedEventArgs e)
    {
        ResetZoom();
    }
}
