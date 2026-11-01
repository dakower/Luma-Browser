using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;

namespace Luma;

public class PresentationSlideData
{
    public int SlideNumber { get; set; }
    public BitmapSource SlideImage { get; set; } = null!;
    public double Width { get; set; }
    public double Height { get; set; }
    public StrokeCollection SlideStrokes { get; set; } = new();
    public FrameworkElement? CanvasContainer { get; set; }
}

public class PresentationSession
{
    public string Title { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public List<PresentationSlideData> Slides { get; set; } = new();
    public int CurrentSlideIndex { get; set; } = 0;
}

public partial class WhiteboardControl
{
    // =========================================================================
    // ПРЕЗЕНТАЦІЇ ТА PDF-ДОКУМЕНТИ (ПРИВ'ЯЗКА ДО АРКУША + РОЗШИРЕНЕ СЛАЙД-ШОУ)
    // =========================================================================

    // Презентації прив'язані строго до конкретного аркуша (board.Id -> PresentationSession)
    private readonly Dictionary<string, PresentationSession> _boardPresentations = new();

    private PresentationSession? _activePresentation = null;
    private int _slideshowCurrentIndex = 0;
    private Color _slideshowPenColor = Color.FromRgb(30, 41, 59);
    private double _slideshowPenSize = 3.5;
    private bool _slideshowIsHighlighter = false;
    private bool _slideshowIsLaser = false;

    // Додаткові інструменти слайд-шоу
    private double _slideZoomLevel = 1.0;
    private DispatcherTimer? _slideMiniTimer = null;
    private int _slideTimerRemainingSeconds = 180;
    private bool _slideTimerIsRunning = false;

    /// <summary>
    /// Імпорт PDF презентації або сторінок підручника на окремий аркуш
    /// </summary>
    public async Task ImportPresentationPdfAsync()
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "Виберіть презентацію або документ PDF",
                Filter = "Документи та презентації PDF (*.pdf)|*.pdf|Усі файли (*.*)|*.*",
                Multiselect = false
            };

            if (dlg.ShowDialog() != true) return;

            string filePath = dlg.FileName;
            string fileName = System.IO.Path.GetFileName(filePath);

            ShowAiStatus($"Завантаження презентації «{fileName}»...");

            StorageFile storageFile;
            try
            {
                storageFile = await StorageFile.GetFileFromPathAsync(filePath);
            }
            catch
            {
                string tempCopy = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"luma_slide_{Guid.NewGuid():N}.pdf");
                File.Copy(filePath, tempCopy, true);
                storageFile = await StorageFile.GetFileFromPathAsync(tempCopy);
            }

            var pdfDoc = await PdfDocument.LoadFromFileAsync(storageFile);
            if (pdfDoc == null || pdfDoc.PageCount == 0)
            {
                ShowAiStatus("Не вдалося відкрити сторінки у файлі PDF");
                return;
            }

            var session = new PresentationSession
            {
                Title = fileName,
                FilePath = filePath
            };

            uint count = pdfDoc.PageCount;
            const double targetWidth = 1600.0;

            for (uint i = 0; i < count; i++)
            {
                using var page = pdfDoc.GetPage(i);
                using var stream = new InMemoryRandomAccessStream();

                var renderOptions = new PdfPageRenderOptions
                {
                    DestinationWidth = 1920
                };

                await page.RenderToStreamAsync(stream, renderOptions);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream.AsStreamForRead();
                bitmap.EndInit();
                bitmap.Freeze();

                double aspect = (bitmap.PixelWidth > 0) ? (bitmap.PixelHeight / (double)bitmap.PixelWidth) : 0.5625;
                double slideHeight = targetWidth * aspect;

                var slide = new PresentationSlideData
                {
                    SlideNumber = (int)(i + 1),
                    SlideImage = bitmap,
                    Width = targetWidth,
                    Height = slideHeight
                };

                session.Slides.Add(slide);
            }

            // Створюємо новий аркуш спеціально під цю презентацію, щоб не захаращувати інші
            var presBoard = new WhiteboardBoardData
            {
                Title = $"📊 {fileName}",
                BackgroundKind = "Blank",
                BackgroundColor = "#FFFFFF",
                ShowHeader = false
            };

            _session.Boards.Add(presBoard);
            _boardPresentations[presBoard.Id] = session;

            // Перемикаємося на новий аркуш презентації
            SwitchToBoard(presBoard.Id);

            ShowAiStatus($"Створено аркуш презентації «{fileName}» ({count} слайдів) 📄✨");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowAiStatus("Помилка під час імпорту PDF");
        }
    }

    /// <summary>
    /// Синхронізація перегляду презентації при завантаженні/перемиканні аркуша
    /// </summary>
    public void SyncPresentationForCurrentBoard()
    {
        if (_currentBoard != null && _boardPresentations.TryGetValue(_currentBoard.Id, out var session) && session != null && session.Slides.Count > 0)
        {
            _activePresentation = session;
            if (BoardPresentationOverlay != null)
            {
                BoardPresentationOverlay.Visibility = Visibility.Visible;
            }
            DisplaySlide(session.CurrentSlideIndex);
        }
        else
        {
            _activePresentation = null;
            if (BoardPresentationOverlay != null)
            {
                BoardPresentationOverlay.Visibility = Visibility.Collapsed;
            }
            if (PresentationOverlayGrid != null)
            {
                PresentationOverlayGrid.Visibility = Visibility.Collapsed;
            }
            if (SlideBlackoutOverlay != null)
            {
                SlideBlackoutOverlay.Visibility = Visibility.Collapsed;
            }
            if (SlidesOverviewModal != null)
            {
                SlidesOverviewModal.Visibility = Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// Відображення слайда з точним збереженням Aspect Ratio (1:1 без спотворень)
    /// </summary>
    public void DisplaySlide(int index)
    {
        if (_activePresentation == null || _activePresentation.Slides.Count == 0) return;

        SaveCurrentSlideStrokes();

        index = Math.Clamp(index, 0, _activePresentation.Slides.Count - 1);
        _slideshowCurrentIndex = index;
        _activePresentation.CurrentSlideIndex = index;
        var slide = _activePresentation.Slides[index];

        // 1. Оновлення режиму дошки (BoardPresentationOverlay)
        if (BoardSlideContainer != null && BoardSlideImage != null && BoardSlideInkCanvas != null)
        {
            BoardSlideContainer.Width = slide.Width;
            BoardSlideContainer.Height = slide.Height;
            BoardSlideImage.Width = slide.Width;
            BoardSlideImage.Height = slide.Height;
            BoardSlideInkCanvas.Width = slide.Width;
            BoardSlideInkCanvas.Height = slide.Height;

            BoardSlideImage.Source = slide.SlideImage;
            BoardSlideInkCanvas.Strokes.Clear();
            if (slide.SlideStrokes.Count > 0)
            {
                BoardSlideInkCanvas.Strokes.Add(slide.SlideStrokes.Clone());
            }
        }

        if (BoardSlideCounterText != null)
        {
            BoardSlideCounterText.Text = $"Слайд {slide.SlideNumber} з {_activePresentation.Slides.Count}";
        }

        if (BoardPresentationTitleText != null)
        {
            BoardPresentationTitleText.Text = _activePresentation.Title;
        }

        // 2. Оновлення повноекранного режиму (PresentationOverlayGrid)
        if (SlideCanvasContainer != null && SlideDisplayImage != null && SlideInkCanvas != null)
        {
            SlideCanvasContainer.Width = slide.Width;
            SlideCanvasContainer.Height = slide.Height;
            SlideDisplayImage.Width = slide.Width;
            SlideDisplayImage.Height = slide.Height;
            SlideInkCanvas.Width = slide.Width;
            SlideInkCanvas.Height = slide.Height;

            SlideDisplayImage.Source = slide.SlideImage;
            SlideInkCanvas.Strokes.Clear();
            if (slide.SlideStrokes.Count > 0)
            {
                SlideInkCanvas.Strokes.Add(slide.SlideStrokes.Clone());
            }
        }

        if (SlideCounterText != null)
        {
            SlideCounterText.Text = $"Слайд {slide.SlideNumber} з {_activePresentation.Slides.Count}";
        }

        if (PresentationTitleText != null)
        {
            PresentationTitleText.Text = _activePresentation.Title;
        }

        SyncActiveToolToBoardSlide();
    }

    /// <summary>
    /// Перехід до наступного слайда
    /// </summary>
    public void NextSlide()
    {
        if (_activePresentation == null || _activePresentation.Slides.Count == 0) return;

        if (_slideshowCurrentIndex < _activePresentation.Slides.Count - 1)
        {
            DisplaySlide(_slideshowCurrentIndex + 1);
        }
    }

    /// <summary>
    /// Перехід до попереднього слайда
    /// </summary>
    public void PreviousSlide()
    {
        if (_activePresentation == null || _activePresentation.Slides.Count == 0) return;

        if (_slideshowCurrentIndex > 0)
        {
            DisplaySlide(_slideshowCurrentIndex - 1);
        }
    }

    /// <summary>
    /// Збереження рукописних штрихів поточного слайда
    /// </summary>
    private void SaveCurrentSlideStrokes()
    {
        if (_activePresentation == null || _slideshowCurrentIndex >= _activePresentation.Slides.Count) return;

        var slide = _activePresentation.Slides[_slideshowCurrentIndex];

        if (PresentationOverlayGrid != null && PresentationOverlayGrid.Visibility == Visibility.Visible && SlideInkCanvas != null)
        {
            slide.SlideStrokes = SlideInkCanvas.Strokes.Clone();
        }
        else if (BoardPresentationOverlay != null && BoardPresentationOverlay.Visibility == Visibility.Visible && BoardSlideInkCanvas != null)
        {
            slide.SlideStrokes = BoardSlideInkCanvas.Strokes.Clone();
        }
    }

    /// <summary>
    /// Запуск повноекранного слайд-шоу (F5)
    /// </summary>
    public void StartSlideshow(int slideIndex = -1)
    {
        if (_activePresentation == null || _activePresentation.Slides.Count == 0)
        {
            ShowAiStatus("Спочатку відкрийте презентацію або документ PDF 📄");
            return;
        }

        SaveCurrentSlideStrokes();

        if (slideIndex >= 0)
        {
            _slideshowCurrentIndex = Math.Clamp(slideIndex, 0, _activePresentation.Slides.Count - 1);
        }

        if (PresentationOverlayGrid != null)
        {
            PresentationOverlayGrid.Visibility = Visibility.Visible;
            PresentationOverlayGrid.Focus();
        }

        UpdateSlideshowPenTool();
        DisplaySlide(_slideshowCurrentIndex);
    }

    /// <summary>
    /// Вихід із повноекранного режиму (Esc)
    /// </summary>
    public void ExitSlideshow()
    {
        SaveCurrentSlideStrokes();

        if (PresentationOverlayGrid != null)
        {
            PresentationOverlayGrid.Visibility = Visibility.Collapsed;
        }

        if (SlideBlackoutOverlay != null)
        {
            SlideBlackoutOverlay.Visibility = Visibility.Collapsed;
        }

        if (BoardPresentationOverlay != null && BoardPresentationOverlay.Visibility == Visibility.Visible)
        {
            DisplaySlide(_slideshowCurrentIndex);
            BoardPresentationOverlay.Focus();
        }
    }

    /// <summary>
    /// Закриття перегляду презентації на поточному аркуші
    /// </summary>
    public void CloseBoardPresentation()
    {
        SaveCurrentSlideStrokes();

        if (_currentBoard != null)
        {
            _boardPresentations.Remove(_currentBoard.Id);
            _currentBoard.Title = "Аркуш";
            RefreshBoardsTabsUi();
        }

        _activePresentation = null;

        if (BoardPresentationOverlay != null)
        {
            BoardPresentationOverlay.Visibility = Visibility.Collapsed;
        }

        if (PresentationOverlayGrid != null)
        {
            PresentationOverlayGrid.Visibility = Visibility.Collapsed;
        }

        ShowAiStatus("Презентацію закрито. Аркуш відновлено 📝");
    }

    /// <summary>
    /// Очищення позначок на поточному слайді
    /// </summary>
    public void ClearCurrentSlideAnnotations()
    {
        if (BoardSlideInkCanvas != null)
        {
            BoardSlideInkCanvas.Strokes.Clear();
        }

        if (SlideInkCanvas != null)
        {
            SlideInkCanvas.Strokes.Clear();
        }

        if (_activePresentation != null && _slideshowCurrentIndex < _activePresentation.Slides.Count)
        {
            _activePresentation.Slides[_slideshowCurrentIndex].SlideStrokes.Clear();
        }

        ShowAiStatus("Помітки слайда очищено 🧹");
    }

    /// <summary>
    /// Синхронізація активного інструменту нижньої панелі дошки на полотно слайда
    /// </summary>
    public void SyncActiveToolToBoardSlide()
    {
        if (BoardSlideInkCanvas == null || MainCanvas == null) return;

        BoardSlideInkCanvas.EditingMode = MainCanvas.EditingMode;
        BoardSlideInkCanvas.Cursor = MainCanvas.Cursor;
        if (MainCanvas.DefaultDrawingAttributes != null)
        {
            BoardSlideInkCanvas.DefaultDrawingAttributes = MainCanvas.DefaultDrawingAttributes.Clone();
        }
    }

    // =========================================================================
    // ДОДАТКОВІ ФУНКЦІЇ СЛАЙД-ШОУ: СІТКА, ПАУЗА, ТАЙМЕР, ЗУМ
    // =========================================================================

    /// <summary>
    /// Відкриття модального вікна сітки всіх слайдів презентації
    /// </summary>
    public void OpenSlidesOverview()
    {
        if (_activePresentation == null || _activePresentation.Slides.Count == 0 || SlidesOverviewModal == null || SlidesOverviewGridContainer == null) return;

        SlidesOverviewGridContainer.Children.Clear();
        if (SlidesOverviewCountText != null)
        {
            SlidesOverviewCountText.Text = $" ({_activePresentation.Slides.Count} слайдів)";
        }

        for (int i = 0; i < _activePresentation.Slides.Count; i++)
        {
            int slideIdx = i;
            var slide = _activePresentation.Slides[i];
            bool isCurrent = (slideIdx == _slideshowCurrentIndex);

            var card = new Border
            {
                Width = 190,
                Height = 135,
                Margin = new Thickness(8),
                CornerRadius = new CornerRadius(10),
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = isCurrent ? new SolidColorBrush(Color.FromRgb(79, 70, 229)) : new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(isCurrent ? 2.5 : 1),
                Cursor = Cursors.Hand,
                SnapsToDevicePixels = true
            };

            var grid = new Grid();

            var img = new Image
            {
                Source = slide.SlideImage,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(4)
            };
            grid.Children.Add(img);

            var badge = new Border
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(200, 15, 23, 42)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(6)
            };
            badge.Child = new TextBlock
            {
                Text = $"{slide.SlideNumber}",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White
            };
            grid.Children.Add(badge);

            card.Child = grid;

            card.MouseLeftButtonDown += (_, _) =>
            {
                SlidesOverviewModal.Visibility = Visibility.Collapsed;
                DisplaySlide(slideIdx);
            };

            SlidesOverviewGridContainer.Children.Add(card);
        }

        SlidesOverviewModal.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Пауза уваги / Затемнення екрана (Blackout)
    /// </summary>
    public void ToggleSlideBlackout()
    {
        if (SlideBlackoutOverlay == null) return;

        SlideBlackoutOverlay.Visibility = (SlideBlackoutOverlay.Visibility == Visibility.Visible)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>
    /// Перемикання видимості міні-таймера вправи на слайді
    /// </summary>
    public void ToggleSlideTimer()
    {
        if (SlideMiniTimerCard == null) return;

        SlideMiniTimerCard.Visibility = (SlideMiniTimerCard.Visibility == Visibility.Visible)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (SlideMiniTimerCard.Visibility == Visibility.Visible && _slideMiniTimer == null)
        {
            InitSlideMiniTimer();
        }
    }

    private void InitSlideMiniTimer()
    {
        _slideMiniTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _slideMiniTimer.Tick += (_, _) =>
        {
            if (_slideTimerRemainingSeconds > 0)
            {
                _slideTimerRemainingSeconds--;
                UpdateSlideMiniTimerDisplay();
            }
            else
            {
                _slideTimerIsRunning = false;
                _slideMiniTimer.Stop();
                if (SlideMiniTimerText != null) SlideMiniTimerText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                ShowAiStatus("Час на виконання вправи вичерпано! ⏱️🔔");
            }
        };
    }

    private void UpdateSlideMiniTimerDisplay()
    {
        if (SlideMiniTimerText == null) return;
        int m = _slideTimerRemainingSeconds / 60;
        int s = _slideTimerRemainingSeconds % 60;
        SlideMiniTimerText.Text = $"{m:00}:{s:00}";
    }

    private void SlideMiniTimerStart_Click(object sender, RoutedEventArgs e)
    {
        if (_slideMiniTimer == null) InitSlideMiniTimer();

        if (_slideTimerIsRunning)
        {
            _slideTimerIsRunning = false;
            _slideMiniTimer?.Stop();
        }
        else
        {
            _slideTimerIsRunning = true;
            _slideMiniTimer?.Start();
        }
    }

    private void SlideMiniTimerAddMinute_Click(object sender, RoutedEventArgs e)
    {
        _slideTimerRemainingSeconds += 60;
        UpdateSlideMiniTimerDisplay();
    }

    private void SlideMiniTimerReset_Click(object sender, RoutedEventArgs e)
    {
        _slideTimerIsRunning = false;
        _slideMiniTimer?.Stop();
        _slideTimerRemainingSeconds = 180;
        if (SlideMiniTimerText != null) SlideMiniTimerText.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        UpdateSlideMiniTimerDisplay();
    }

    private void SlideMiniTimerClose_Click(object sender, RoutedEventArgs e)
    {
        if (SlideMiniTimerCard != null) SlideMiniTimerCard.Visibility = Visibility.Collapsed;
    }

    // Масштабування слайда (Zoom)
    public void SlideZoomIn()
    {
        _slideZoomLevel = Math.Min(3.0, _slideZoomLevel + 0.15);
        ApplySlideZoom();
    }

    public void SlideZoomOut()
    {
        _slideZoomLevel = Math.Max(0.5, _slideZoomLevel - 0.15);
        ApplySlideZoom();
    }

    public void SlideZoomReset()
    {
        _slideZoomLevel = 1.0;
        ApplySlideZoom();
    }

    private void ApplySlideZoom()
    {
        if (SlideViewbox != null)
        {
            SlideViewbox.LayoutTransform = new ScaleTransform(_slideZoomLevel, _slideZoomLevel);
        }
        if (SlideZoomText != null)
        {
            SlideZoomText.Text = $"{(int)Math.Round(_slideZoomLevel * 100)}%";
        }
    }

    private void SlideZoomIn_Click(object sender, RoutedEventArgs e) => SlideZoomIn();
    private void SlideZoomOut_Click(object sender, RoutedEventArgs e) => SlideZoomOut();
    private void SlideZoomReset_Click(object sender, MouseButtonEventArgs e) => SlideZoomReset();

    // =========================================================================
    // НАЛАШТУВАННЯ ІНСТРУМЕНТІВ ПОВНОЕКРАННОГО СЛАЙД-ШОУ
    // =========================================================================

    private void UpdateSlideshowPenTool()
    {
        if (SlideInkCanvas == null) return;

        if (_slideshowIsLaser)
        {
            SlideInkCanvas.EditingMode = InkCanvasEditingMode.None;
            SlideInkCanvas.Cursor = Cursors.Cross;
            return;
        }

        SlideInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
        SlideInkCanvas.Cursor = Cursors.Pen;

        var da = new DrawingAttributes
        {
            Color = _slideshowPenColor,
            Width = _slideshowPenSize,
            Height = _slideshowPenSize,
            FitToCurve = true,
            IsHighlighter = _slideshowIsHighlighter
        };

        if (_slideshowIsHighlighter)
        {
            da.Color = Color.FromArgb(140, _slideshowPenColor.R, _slideshowPenColor.G, _slideshowPenColor.B);
            da.Width = 18.0;
            da.Height = 18.0;
        }

        SlideInkCanvas.DefaultDrawingAttributes = da;
    }

    // =========================================================================
    // ОБРОБНИКИ ПОДІЙ ІНТЕРФЕЙСУ
    // =========================================================================

    private async void ImportPdfPresentationBtn_Click(object sender, RoutedEventArgs e)
    {
        await ImportPresentationPdfAsync();
    }

    private void BoardSlidePrevBtn_Click(object sender, RoutedEventArgs e) => PreviousSlide();
    private void BoardSlideNextBtn_Click(object sender, RoutedEventArgs e) => NextSlide();
    private void BoardSlideFullscreenBtn_Click(object sender, RoutedEventArgs e) => StartSlideshow(_slideshowCurrentIndex);
    private void BoardSlideClearNotesBtn_Click(object sender, RoutedEventArgs e) => ClearCurrentSlideAnnotations();
    private void BoardSlideCloseBtn_Click(object sender, RoutedEventArgs e) => CloseBoardPresentation();

    private void SlidesGridBtn_Click(object sender, RoutedEventArgs e) => OpenSlidesOverview();
    private void SlideBlackoutBtn_Click(object sender, RoutedEventArgs e) => ToggleSlideBlackout();
    private void SlideTimerToggleBtn_Click(object sender, RoutedEventArgs e) => ToggleSlideTimer();

    private void SlidesOverviewClose_Click(object sender, RoutedEventArgs e)
    {
        if (SlidesOverviewModal != null) SlidesOverviewModal.Visibility = Visibility.Collapsed;
    }

    private void SlidesOverviewModal_BackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (SlidesOverviewModal != null) SlidesOverviewModal.Visibility = Visibility.Collapsed;
    }

    private void SlidesOverviewCard_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // предотвратить закрытие при клике внутри окна
    }

    private void SlideBlackoutOverlay_Click(object sender, MouseButtonEventArgs e)
    {
        if (SlideBlackoutOverlay != null) SlideBlackoutOverlay.Visibility = Visibility.Collapsed;
    }

    private void BoardPresentationOverlay_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta < 0) NextSlide();
        else if (e.Delta > 0) PreviousSlide();
        e.Handled = true;
    }

    private void SlideshowPrevBtn_Click(object sender, RoutedEventArgs e) => PreviousSlide();
    private void SlideshowNextBtn_Click(object sender, RoutedEventArgs e) => NextSlide();
    private void SlideshowExitBtn_Click(object sender, RoutedEventArgs e) => ExitSlideshow();
    private void SlideClearAnnotationsBtn_Click(object sender, RoutedEventArgs e) => ClearCurrentSlideAnnotations();

    private void PresentationOverlayGrid_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl)
        {
            if (e.Delta > 0) SlideZoomIn();
            else SlideZoomOut();
            e.Handled = true;
            return;
        }

        if (e.Delta < 0) NextSlide();
        else if (e.Delta > 0) PreviousSlide();
        e.Handled = true;
    }

    private void PresentationOverlayGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (SlidesOverviewModal != null && SlidesOverviewModal.Visibility == Visibility.Visible)
            {
                SlidesOverviewModal.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
            if (SlideBlackoutOverlay != null && SlideBlackoutOverlay.Visibility == Visibility.Visible)
            {
                SlideBlackoutOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
            ExitSlideshow();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            ExitSlideshow();
            e.Handled = true;
        }
        else if (e.Key == Key.B)
        {
            ToggleSlideBlackout();
            e.Handled = true;
        }
        else if (e.Key == Key.G)
        {
            OpenSlidesOverview();
            e.Handled = true;
        }
        else if (e.Key == Key.Left || e.Key == Key.PageUp || e.Key == Key.Back)
        {
            PreviousSlide();
            e.Handled = true;
        }
        else if (e.Key == Key.Right || e.Key == Key.PageDown || e.Key == Key.Space || e.Key == Key.Enter)
        {
            NextSlide();
            e.Handled = true;
        }
    }

    private void SlideToolPen_Checked(object sender, RoutedEventArgs e)
    {
        _slideshowIsHighlighter = false;
        _slideshowIsLaser = false;
        UpdateSlideshowPenTool();
    }

    private void SlideToolHighlighter_Checked(object sender, RoutedEventArgs e)
    {
        _slideshowIsHighlighter = true;
        _slideshowIsLaser = false;
        UpdateSlideshowPenTool();
    }

    private void SlideToolLaser_Checked(object sender, RoutedEventArgs e)
    {
        _slideshowIsHighlighter = false;
        _slideshowIsLaser = true;
        UpdateSlideshowPenTool();
    }

    private void SlideToolEraser_Checked(object sender, RoutedEventArgs e)
    {
        if (SlideInkCanvas != null)
        {
            SlideInkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
            SlideInkCanvas.Cursor = Cursors.Hand;
        }
    }

    private void SlideColorDark_Click(object sender, RoutedEventArgs e)
    {
        _slideshowPenColor = Color.FromRgb(30, 41, 59);
        UpdateSlideshowPenTool();
    }

    private void SlideColorIndigo_Click(object sender, RoutedEventArgs e)
    {
        _slideshowPenColor = Color.FromRgb(79, 70, 229);
        UpdateSlideshowPenTool();
    }

    private void SlideColorWhite_Click(object sender, RoutedEventArgs e)
    {
        _slideshowPenColor = Colors.White;
        UpdateSlideshowPenTool();
    }

    private void SlideColorRed_Click(object sender, RoutedEventArgs e)
    {
        _slideshowPenColor = Color.FromRgb(239, 68, 68);
        UpdateSlideshowPenTool();
    }

    private void SlideColorGreen_Click(object sender, RoutedEventArgs e)
    {
        _slideshowPenColor = Color.FromRgb(22, 163, 74);
        UpdateSlideshowPenTool();
    }

    private void SlideColorYellow_Click(object sender, RoutedEventArgs e)
    {
        _slideshowPenColor = Color.FromRgb(234, 179, 8);
        UpdateSlideshowPenTool();
    }
}
