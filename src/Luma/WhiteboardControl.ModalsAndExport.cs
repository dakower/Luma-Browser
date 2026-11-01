using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
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
using PrintDialog = System.Windows.Controls.PrintDialog;
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
    // БЛОК 1.2: МОДАЛЬНЕ ВІКНО «ЗНІМОК ЕКРАНА»
    // =========================================================================

    private BitmapSource? _pendingScreenshotBitmap = null;
    private int _screenshotRotationAngle = 0; // 0, 90, 180, 270

    public void OpenScreenshotModal()
    {
        if (ScreenshotModal != null)
        {
            ScreenshotModal.Visibility = Visibility.Visible;
            if (ScreenshotPreviewPanel != null) ScreenshotPreviewPanel.Visibility = Visibility.Collapsed;
            if (ScreenshotSourcesPanel != null) ScreenshotSourcesPanel.Visibility = Visibility.Visible;
        }
    }

    public void CloseScreenshotModal()
    {
        if (ScreenshotModal != null)
        {
            ScreenshotModal.Visibility = Visibility.Collapsed;
        }
        _pendingScreenshotBitmap = null;
        _screenshotRotationAngle = 0;
    }

    private void ScreenshotCloseBtn_Click(object sender, RoutedEventArgs e) => CloseScreenshotModal();

    // 1. Джерело: Знімок вкладки / вікна
    private void ScreenshotSourceTab_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Capture primary screen using system GDI
            int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            int screenHeight = (int)SystemParameters.PrimaryScreenHeight;

            using var bmp = new System.Drawing.Bitmap(screenWidth, screenHeight);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;

            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();

            ShowScreenshotPreview(bi);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowAiStatus("Не удалось выполнить захват окна");
        }
    }

    // 2. Джерело: Знімок усієї сторінки з прокручуванням
    private void ScreenshotSourceScroll_Click(object sender, RoutedEventArgs e)
    {
        // For full scroll capture, fallback to capturing current viewport
        ScreenshotSourceTab_Click(sender, e);
    }

    // 3. Джерело: Знімок із буфера обміну
    private void ScreenshotSourceClipboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsImage())
            {
                var bmp = Clipboard.GetImage();
                if (bmp != null)
                {
                    ShowScreenshotPreview(bmp);
                    return;
                }
            }
            ShowAiStatus("У буфері обміну немає зображення");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowAiStatus("Помилка читання з буфера обміну");
        }
    }

    // 4. Джерело: Завантаження файлу
    private void ScreenshotSourceFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "Виберіть зображення для підкладки дошки",
                Filter = "Зображення (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|Усі файли (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(dlg.FileName);
                bi.EndInit();
                bi.Freeze();

                ShowScreenshotPreview(bi);
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowAiStatus("Помилка завантаження файлу зображення");
        }
    }

    private void ShowScreenshotPreview(BitmapSource source)
    {
        _pendingScreenshotBitmap = source;
        _screenshotRotationAngle = 0;

        if (ScreenshotSourcesPanel != null) ScreenshotSourcesPanel.Visibility = Visibility.Collapsed;
        if (ScreenshotPreviewPanel != null) ScreenshotPreviewPanel.Visibility = Visibility.Visible;
        UpdateScreenshotPreviewImage();
    }

    private void ScreenshotRotateLeft_Click(object sender, RoutedEventArgs e)
    {
        _screenshotRotationAngle = (_screenshotRotationAngle + 270) % 360;
        UpdateScreenshotPreviewImage();
    }

    private void ScreenshotRotateRight_Click(object sender, RoutedEventArgs e)
    {
        _screenshotRotationAngle = (_screenshotRotationAngle + 90) % 360;
        UpdateScreenshotPreviewImage();
    }

    private void UpdateScreenshotPreviewImage()
    {
        if (ScreenshotPreviewImage == null || _pendingScreenshotBitmap == null) return;

        if (_screenshotRotationAngle == 0)
        {
            ScreenshotPreviewImage.Source = _pendingScreenshotBitmap;
        }
        else
        {
            var transform = new RotateTransform(_screenshotRotationAngle);
            var transformedBmp = new TransformedBitmap(_pendingScreenshotBitmap, transform);
            ScreenshotPreviewImage.Source = transformedBmp;
        }
    }

    private void ScreenshotCreateBoardBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingScreenshotBitmap == null) return;

        BitmapSource finalImage = _pendingScreenshotBitmap;
        if (_screenshotRotationAngle != 0)
        {
            finalImage = new TransformedBitmap(_pendingScreenshotBitmap, new RotateTransform(_screenshotRotationAngle));
        }

        CloseScreenshotModal();

        // Create a new board with Blank template and place image on the canvas
        CreateNewBoardWithTemplate("Blank", CurrentDefaultFontKind);

        // Insert image as background element or locked image
        InsertImageToCanvas(finalImage, lockPosition: true);
        ShowAiStatus("Дошку створено зі знімка екрана");
    }

    private void InsertImageToCanvas(BitmapSource bitmap, bool lockPosition = false)
    {
        try
        {
            var imgControl = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                MaxWidth = Math.Min(bitmap.PixelWidth, 1200),
                MaxHeight = Math.Min(bitmap.PixelHeight, 900)
            };

            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderBrush = lockPosition ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(120, 59, 130, 246)),
                BorderThickness = new Thickness(lockPosition ? 0 : 1.5),
                Child = imgControl,
                Tag = lockPosition ? "LockedBackground" : "UserImage"
            };

            Canvas.SetLeft(border, 60);
            Canvas.SetTop(border, 40);
            MainCanvas.Children.Add(border);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    // =========================================================================
    // БЛОК 2: ВЕРХНЯ НАВІГАЦІЙНА ПАНЕЛЬ, СИСТЕМНІ МЕНЮ ТА ДІЇ
    // =========================================================================

    // 1. Меню «⋮»
    private void MoreActionsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsPopup != null)
        {
            MoreActionsPopup.IsOpen = !MoreActionsPopup.IsOpen;
        }
    }

    private void MenuHelpItem_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsPopup != null) MoreActionsPopup.IsOpen = false;
        OpenInfoModal(0);
    }

    private void MenuNewBoardItem_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsPopup != null) MoreActionsPopup.IsOpen = false;
        OpenCreateBoardModal();
    }

    private void MenuDuplicateBoardItem_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsPopup != null) MoreActionsPopup.IsOpen = false;
        DuplicateCurrentBoard();
    }

    private void MenuSettingsItem_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsPopup != null) MoreActionsPopup.IsOpen = false;
        OpenMetadataSettingsModal();
    }

    private void MenuEmbedItem_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsPopup != null) MoreActionsPopup.IsOpen = false;
        OpenEmbedModal();
    }

    private void MenuPrintItem_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsPopup != null) MoreActionsPopup.IsOpen = false;
        ExecuteSmartPrint();
    }

    private void MenuThemeAuto_Click(object sender, RoutedEventArgs e) => ApplyWhiteboardTheme("Auto");
    private void MenuThemeLight_Click(object sender, RoutedEventArgs e) => ApplyWhiteboardTheme("Light");
    private void MenuThemeDark_Click(object sender, RoutedEventArgs e) => ApplyWhiteboardTheme("Dark");

    private void MenuDiscussionItem_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsPopup != null) MoreActionsPopup.IsOpen = false;
        ToggleDiscussionDrawer();
    }

    // 2. ДОВІДКА: Інфо-модальне вікно з 3 вкладками
    public void OpenInfoModal(int tabIndex = 0)
    {
        if (InfoModal != null)
        {
            InfoModal.Visibility = Visibility.Visible;
            SelectInfoModalTab(tabIndex);
        }
    }

    public void CloseInfoModal()
    {
        if (InfoModal != null) InfoModal.Visibility = Visibility.Collapsed;
    }

    private void InfoModalClose_Click(object sender, RoutedEventArgs e) => CloseInfoModal();

    private void InfoTabToolsBtn_Click(object sender, RoutedEventArgs e) => SelectInfoModalTab(0);
    private void InfoTabHotkeysBtn_Click(object sender, RoutedEventArgs e) => SelectInfoModalTab(1);
    private void InfoTabGesturesBtn_Click(object sender, RoutedEventArgs e) => SelectInfoModalTab(2);

    private void SelectInfoModalTab(int tabIndex)
    {
        if (InfoTabToolsContent == null || InfoTabHotkeysContent == null || InfoTabGesturesContent == null) return;

        InfoTabToolsContent.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        InfoTabHotkeysContent.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        InfoTabGesturesContent.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

        var activeBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        var inactiveBrush = Brushes.Transparent;
        var activeFg = Brushes.White;
        var inactiveFg = new SolidColorBrush(Color.FromRgb(100, 116, 139));

        if (InfoTabToolsBtn != null)
        {
            InfoTabToolsBtn.Background = tabIndex == 0 ? activeBrush : inactiveBrush;
            InfoTabToolsBtn.Foreground = tabIndex == 0 ? activeFg : inactiveFg;
        }
        if (InfoTabHotkeysBtn != null)
        {
            InfoTabHotkeysBtn.Background = tabIndex == 1 ? activeBrush : inactiveBrush;
            InfoTabHotkeysBtn.Foreground = tabIndex == 1 ? activeFg : inactiveFg;
        }
        if (InfoTabGesturesBtn != null)
        {
            InfoTabGesturesBtn.Background = tabIndex == 2 ? activeBrush : inactiveBrush;
            InfoTabGesturesBtn.Foreground = tabIndex == 2 ? activeFg : inactiveFg;
        }
    }

    // 3. ВБУДУВАТИ СОБІ НА САЙТ (EMBED MODAL)
    public void OpenEmbedModal()
    {
        if (EmbedModal != null)
        {
            UpdateEmbedCodePreview();
            EmbedModal.Visibility = Visibility.Visible;
        }
    }

    public void CloseEmbedModal()
    {
        if (EmbedModal != null) EmbedModal.Visibility = Visibility.Collapsed;
    }

    private void EmbedCloseBtn_Click(object sender, RoutedEventArgs e) => CloseEmbedModal();

    private void EmbedDimension_TextChanged(object sender, TextChangedEventArgs e) => UpdateEmbedCodePreview();

    private void UpdateEmbedCodePreview()
    {
        if (EmbedCodePreviewBox == null) return;
        string w = EmbedWidthBox?.Text?.Trim() ?? "800";
        string h = EmbedHeightBox?.Text?.Trim() ?? "600";
        if (string.IsNullOrEmpty(w)) w = "800";
        if (string.IsNullOrEmpty(h)) h = "600";

        string boardId = _currentBoard?.Id ?? "demo";
        string code = $"<iframe src=\"https://whiteboard.vseosvita.ua/embed/{boardId}\" width=\"{w}\" height=\"{h}\" frameborder=\"0\" allow=\"fullscreen; clipboard-read; clipboard-write\" allowfullscreen></iframe>";
        EmbedCodePreviewBox.Text = code;
    }

    private void CopyEmbedCodeBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (EmbedCodePreviewBox != null)
            {
                Clipboard.SetText(EmbedCodePreviewBox.Text);
                ShowAiStatus("Код для вбудовування скопійовано! 📋");
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    // 4. МЕТАДАНІ ТА НАЛАШТУВАННЯ ДОШКИ
    public void OpenMetadataSettingsModal()
    {
        if (MetadataSettingsModal != null)
        {
            if (MetadataBoardTitleBox != null) MetadataBoardTitleBox.Text = _currentBoard?.Title ?? "Аркуш";
            if (MetadataBoardDescBox != null) MetadataBoardDescBox.Text = _currentBoard?.Description ?? "";
            MetadataSettingsModal.Visibility = Visibility.Visible;
        }
    }

    public void CloseMetadataSettingsModal()
    {
        if (MetadataSettingsModal != null) MetadataSettingsModal.Visibility = Visibility.Collapsed;
    }

    private void MetadataSettingsSave_Click(object sender, RoutedEventArgs e)
    {
        if (_currentBoard != null)
        {
            if (MetadataBoardTitleBox != null) _currentBoard.Title = MetadataBoardTitleBox.Text.Trim();
            if (MetadataBoardDescBox != null) _currentBoard.Description = MetadataBoardDescBox.Text.Trim();
            ScheduleAutoSave();
            RefreshBoardsTabsUi();
        }
        CloseMetadataSettingsModal();
        ShowAiStatus("Налаштування аркуша збережено");
    }

    private void MetadataSettingsClose_Click(object sender, RoutedEventArgs e) => CloseMetadataSettingsModal();

    // 5. КОПІЮВАТИ ЯК ЗОБРАЖЕННЯ (BOUNDING BOX PNG TO CLIPBOARD)
    public void CopyAsImageToClipboard()
    {
        try
        {
            // 1. Calculate bounding box of strokes and visual canvas children
            Rect bounds = Rect.Empty;
            if (MainCanvas.Strokes.Count > 0)
            {
                bounds = MainCanvas.Strokes.GetBounds();
            }

            foreach (UIElement child in MainCanvas.Children)
            {
                if (child.Visibility != Visibility.Visible) continue;
                if (child == NotebookMarginLine) continue;

                double left = Canvas.GetLeft(child);
                double top = Canvas.GetTop(child);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;

                double w = child.RenderSize.Width > 0 ? child.RenderSize.Width : 100;
                double h = child.RenderSize.Height > 0 ? child.RenderSize.Height : 50;
                var childRect = new Rect(left, top, w, h);

                bounds = bounds.IsEmpty ? childRect : Rect.Union(bounds, childRect);
            }

            // If empty, use visible canvas area
            if (bounds.IsEmpty || bounds.Width < 10 || bounds.Height < 10)
            {
                bounds = new Rect(0, 0, Math.Max(800, MainCanvas.ActualWidth), Math.Max(600, MainCanvas.ActualHeight));
            }

            // Add margin padding
            const double pad = 24.0;
            bounds.Inflate(pad, pad);
            bounds.X = Math.Max(0, bounds.X);
            bounds.Y = Math.Max(0, bounds.Y);

            int renderW = Math.Max(60, (int)Math.Ceiling(bounds.Width));
            int renderH = Math.Max(60, (int)Math.Ceiling(bounds.Height));

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // Background fill
                Brush bgBrush = MainCanvas.Background ?? Brushes.White;
                dc.DrawRectangle(bgBrush, null, new Rect(0, 0, renderW, renderH));

                // Translate visual context
                dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
                var brush = new VisualBrush(MainCanvas) { Stretch = Stretch.None };
                dc.DrawRectangle(brush, null, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
                dc.Pop();
            }

            var rtb = new RenderTargetBitmap(renderW, renderH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();

            Clipboard.SetImage(rtb);
            ShowAiStatus("Зображення виділеної області скопійовано в буфер! 📋");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowAiStatus("Помилка копіювання зображення");
        }
    }

    // 6. РОЗУМНИЙ ДРУК (SMART PRINT WITHOUT CUTTING STROKES/SHAPES)
    public void ExecuteSmartPrint()
    {
        try
        {
            var printDlg = new PrintDialog();
            if (printDlg.ShowDialog() != true) return;

            // A4 printable area standard at 96 DPI: 793 x 1122
            double pageWidth = printDlg.PrintableAreaWidth > 100 ? printDlg.PrintableAreaWidth : 793.0;
            double pageHeight = printDlg.PrintableAreaHeight > 100 ? printDlg.PrintableAreaHeight : 1122.0;

            double totalContentHeight = Math.Max(MainCanvas.ActualHeight, 1000);
            if (MainCanvas.Strokes.Count > 0)
            {
                totalContentHeight = Math.Max(totalContentHeight, MainCanvas.Strokes.GetBounds().Bottom + 100);
            }

            var doc = new FixedDocument();
            double currentY = 0;

            while (currentY < totalContentHeight)
            {
                double sliceHeight = pageHeight;

                // Smart check: avoid cutting strokes in half by checking if stroke spans across (currentY + sliceHeight)
                double boundary = currentY + sliceHeight;
                if (boundary < totalContentHeight)
                {
                    double safeCutShift = 0;
                    foreach (var s in MainCanvas.Strokes)
                    {
                        var b = s.GetBounds();
                        if (b.Top < boundary && b.Bottom > boundary)
                        {
                            safeCutShift = Math.Max(safeCutShift, boundary - b.Top + 10);
                        }
                    }
                    if (safeCutShift > 0 && safeCutShift < (pageHeight * 0.35))
                    {
                        sliceHeight -= safeCutShift;
                    }
                }

                var fixedPage = new FixedPage
                {
                    Width = pageWidth,
                    Height = pageHeight
                };

                var container = new Grid
                {
                    Width = pageWidth,
                    Height = sliceHeight,
                    ClipToBounds = true
                };

                var visualBrush = new VisualBrush(MainCanvas)
                {
                    Stretch = Stretch.UniformToFill,
                    Viewbox = new Rect(0, currentY, MainCanvas.ActualWidth, sliceHeight),
                    ViewboxUnits = BrushMappingMode.Absolute
                };

                container.Background = visualBrush;
                fixedPage.Children.Add(container);

                var pageContent = new PageContent();
                ((System.Windows.Markup.IAddChild)pageContent).AddChild(fixedPage);
                doc.Pages.Add(pageContent);

                currentY += sliceHeight;
            }

            printDlg.PrintDocument(doc.DocumentPaginator, $"Електронна дошка — {_currentBoard?.Title ?? "Урок"}");
            ShowAiStatus("Дошку відправлено на друк! 🖨️");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowAiStatus("Помилка під час друку");
        }
    }

    // 7. МОДАЛЬНЕ ВІКНО ПІДТВЕРДЖЕННЯ ЗАКРИТТЯ (CLOSE CONFIRMATION MODAL)
    public void RequestCloseBoard(Action onConfirmedClose)
    {
        _onConfirmedCloseAction = onConfirmedClose;
        if (CloseConfirmationModal != null)
        {
            CloseConfirmationModal.Visibility = Visibility.Visible;
        }
        else
        {
            SaveCurrentBoardState();
            SaveSessionToDisk();
            onConfirmedClose();
        }
    }

    private Action? _onConfirmedCloseAction;

    private void CloseConfirmCancel_Click(object sender, RoutedEventArgs e)
    {
        if (CloseConfirmationModal != null) CloseConfirmationModal.Visibility = Visibility.Collapsed;
    }

    private void CloseConfirmWithoutSave_Click(object sender, RoutedEventArgs e)
    {
        if (CloseConfirmationModal != null) CloseConfirmationModal.Visibility = Visibility.Collapsed;
        _onConfirmedCloseAction?.Invoke();
    }

    private void CloseConfirmSaveAndClose_Click(object sender, RoutedEventArgs e)
    {
        if (CloseConfirmationModal != null) CloseConfirmationModal.Visibility = Visibility.Collapsed;
        SaveCurrentBoardState();
        SaveSessionToDisk();
        _onConfirmedCloseAction?.Invoke();
    }

    // 8. ДИСКУСІЯ / ОБГОВОРЕННЯ ТА НОТАТКИ
    private void ToggleDiscussionDrawer()
    {
        if (DiscussionDrawer != null)
        {
            DiscussionDrawer.Visibility = (DiscussionDrawer.Visibility == Visibility.Visible)
                ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void CloseDiscussionDrawer_Click(object sender, RoutedEventArgs e)
    {
        if (DiscussionDrawer != null) DiscussionDrawer.Visibility = Visibility.Collapsed;
    }

    // 9. ЗАВАНТАЖЕННЯ (DOWNLOAD PNG / PDF)
    private void DownloadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadMenuPopup != null)
        {
            DownloadMenuPopup.IsOpen = !DownloadMenuPopup.IsOpen;
        }
    }

    private void DownloadPngItem_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadMenuPopup != null) DownloadMenuPopup.IsOpen = false;
        ExportBoardBtn_Click(sender, e);
    }

    private void DownloadPdfItem_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadMenuPopup != null) DownloadMenuPopup.IsOpen = false;
        ExecuteSmartPrint();
    }

    private void CopyImageTopBtn_Click(object sender, RoutedEventArgs e) => CopyAsImageToClipboard();

    // 10. ПОВНОЕКРАННИЙ РЕЖИМ (F11)
    private bool _isWhiteboardFullscreen = false;

    public void ToggleFullscreenMode()
    {
        _isWhiteboardFullscreen = !_isWhiteboardFullscreen;
        var win = Window.GetWindow(this);
        if (win != null)
        {
            if (_isWhiteboardFullscreen)
            {
                win.WindowState = WindowState.Maximized;
            }
        }
        ShowAiStatus(_isWhiteboardFullscreen ? "Повноекранний режим (F11 або Esc)" : "Звичайний режим");
    }

    private void ToggleFullscreenBtn_Click(object sender, RoutedEventArgs e) => ToggleFullscreenMode();

    private void CloseBoardBtn_Click(object sender, RoutedEventArgs e)
    {
        RequestCloseBoard(() =>
        {
            var win = Window.GetWindow(this) as MainWindow;
            if (win != null && win.CurrentTab is { IsInternal: true } tab)
            {
                win.CloseTab(tab);
            }
        });
    }

    // 11. ЗАКРІПИТИ / ПРИХОВАТИ ПАНЕЛЬ
    private bool _isToolbarCollapsed = false;

    public void ToggleToolbarCollapse()
    {
        _isToolbarCollapsed = !_isToolbarCollapsed;
        if (FloatingToolbar != null)
        {
            FloatingToolbar.Visibility = _isToolbarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
