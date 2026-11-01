using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

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
    // БЛОК 1.1: 11 ШАБЛОНІВ ТА ГЕОМЕТРІЯ АРКУШІВ
    // =========================================================================

    public string CurrentSheetGeometry { get; set; } = "LeftMargin";
    public string CurrentBoardLanguage { get; set; } = "uk";
    public string CurrentDefaultFontKind { get; set; } = "Propysy"; // "Propysy" or "Printed"

    public static string GetUkrainianDateWords(DateTime dt)
    {
        string[] days = {
            "", "Перше", "Друге", "Третє", "Четверте", "П'яте",
            "Шосте", "Сьоме", "Восьме", "Дев'яте", "Десяте",
            "Одинадцяте", "Дванадцяте", "Тринадцяте", "Чотирнадцяте", "П'ятнадцяте",
            "Шістнадцяте", "Сімнадцяте", "Вісімнадцяте", "Дев'ятнадцяте", "Двадцяте",
            "Двадцять перше", "Двадцять друге", "Двадцять третє", "Двадцять четверте", "Двадцять п'яте",
            "Двадцять шосте", "Двадцять сьоме", "Двадцять восьме", "Двадцять дев'яте", "Тридцяте",
            "Тридцять перше"
        };

        string[] months = {
            "", "січня", "лютого", "березня", "квітня", "травня", "червня",
            "липня", "серпня", "вересня", "жовтня", "листопада", "грудня"
        };

        int d = Math.Clamp(dt.Day, 1, 31);
        int m = Math.Clamp(dt.Month, 1, 12);

        return $"{days[d]} {months[m]}";
    }

    private static DrawingBrush CreateDotGridBrush(Color bgColor, bool isDark)
    {
        const double spacing = 25.0;
        Color dotColor = isDark ? Color.FromArgb(70, 255, 255, 255) : Color.FromRgb(180, 192, 208);

        var drawingGroup = new DrawingGroup();
        drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(bgColor), null, new RectangleGeometry(new Rect(0, 0, spacing, spacing))));
        
        var dotBrush = new SolidColorBrush(dotColor);
        dotBrush.Freeze();
        drawingGroup.Children.Add(new GeometryDrawing(dotBrush, null, new EllipseGeometry(new Point(spacing / 2.0, spacing / 2.0), 1.25, 1.25)));

        var brush = new DrawingBrush(drawingGroup)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, spacing, spacing),
            ViewportUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    private static DrawingBrush CreateMillimeterBrush(Color bgColor, bool isDark)
    {
        const double smallCell = 5.0;
        const double bigCell = 25.0;

        Color fineColor = isDark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(50, 59, 130, 246);
        Color mainColor = isDark ? Color.FromArgb(65, 255, 255, 255) : Color.FromArgb(110, 37, 99, 235);

        var fineGroup = new GeometryGroup();
        for (int i = 1; i < 5; i++)
        {
            double p = i * smallCell;
            fineGroup.Children.Add(new LineGeometry(new Point(0, p), new Point(bigCell, p)));
            fineGroup.Children.Add(new LineGeometry(new Point(p, 0), new Point(p, bigCell)));
        }

        var mainGroup = new GeometryGroup();
        mainGroup.Children.Add(new LineGeometry(new Point(0, bigCell), new Point(bigCell, bigCell)));
        mainGroup.Children.Add(new LineGeometry(new Point(bigCell, 0), new Point(bigCell, bigCell)));

        var drawingGroup = new DrawingGroup();
        drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(bgColor), null, new RectangleGeometry(new Rect(0, 0, bigCell, bigCell))));

        var finePen = new Pen(new SolidColorBrush(fineColor), 0.5); finePen.Freeze();
        var mainPen = new Pen(new SolidColorBrush(mainColor), 0.9); mainPen.Freeze();

        drawingGroup.Children.Add(new GeometryDrawing(null, finePen, fineGroup));
        drawingGroup.Children.Add(new GeometryDrawing(null, mainPen, mainGroup));

        var brush = new DrawingBrush(drawingGroup)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, bigCell, bigCell),
            ViewportUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    private static DrawingBrush CreateMusicStavePairBrush(Color bgColor, bool isDark)
    {
        const double staveTileHeight = 170.0;
        Color lineCol = isDark ? Color.FromArgb(80, 255, 255, 255) : Color.FromRgb(100, 116, 139);

        var staveLines = new GeometryGroup();
        // Upper system (treble)
        for (int i = 0; i < 5; i++)
        {
            double y = 16.0 + (i * 8.0);
            staveLines.Children.Add(new LineGeometry(new Point(0, y), new Point(100, y)));
        }
        // Lower system (bass)
        for (int i = 0; i < 5; i++)
        {
            double y = 84.0 + (i * 8.0);
            staveLines.Children.Add(new LineGeometry(new Point(0, y), new Point(100, y)));
        }
        // Vertical bracket line connecting both staves
        staveLines.Children.Add(new LineGeometry(new Point(0, 16.0), new Point(0, 116.0)));

        var drawingGroup = new DrawingGroup();
        drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(bgColor), null, new RectangleGeometry(new Rect(0, 0, 100, staveTileHeight))));
        var pen = new Pen(new SolidColorBrush(lineCol), 1.0);
        pen.Freeze();
        drawingGroup.Children.Add(new GeometryDrawing(null, pen, staveLines));

        var brush = new DrawingBrush(drawingGroup)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 100, staveTileHeight),
            ViewportUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    public void ApplySheetGeometry(string geometry)
    {
        CurrentSheetGeometry = geometry;
        if (_currentBoard != null)
        {
            _currentBoard.SheetGeometry = geometry;
            ScheduleAutoSave();
        }

        if (NotebookMarginLine == null || MainCanvas == null) return;

        double canvasWidth = MainCanvas.ActualWidth > 300 ? MainCanvas.ActualWidth : 1200.0;

        switch (geometry)
        {
            case "RightMargin":
                InkCanvas.SetLeft(NotebookMarginLine, Math.Max(100.0, canvasWidth - 95.0));
                NotebookMarginLine.Visibility = Visibility.Visible;
                break;

            case "NoMargin":
            case "Infinity":
            case "LargeBoard":
                NotebookMarginLine.Visibility = Visibility.Collapsed;
                break;

            case "LeftMargin":
            default:
                InkCanvas.SetLeft(NotebookMarginLine, 95.0);
                NotebookMarginLine.Visibility = Visibility.Visible;
                break;
        }
    }

    public void OpenCreateBoardModal()
    {
        if (CreateBoardModal != null)
        {
            CreateBoardModal.Visibility = Visibility.Visible;
            HighlightCreateBoardSelectedTemplate(_currentBoard?.BackgroundKind ?? "Slanted");
            UpdateCreateBoardFontSelectionUi(CurrentDefaultFontKind);
        }
    }

    public void CloseCreateBoardModal()
    {
        if (CreateBoardModal != null)
        {
            CreateBoardModal.Visibility = Visibility.Collapsed;
        }
    }

    private void CloseCreateBoard_Click(object sender, RoutedEventArgs e) => CloseCreateBoardModal();

    private string _pendingCreateBoardTemplate = "Slanted";

    private void CreateBoardTemplateCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement card && card.Tag is string kind)
        {
            _pendingCreateBoardTemplate = kind;
            HighlightCreateBoardSelectedTemplate(kind);
        }
    }

    private void HighlightCreateBoardSelectedTemplate(string kind)
    {
        var templateCards = new FrameworkElement[]
        {
            NewTplRuled, NewTplGrid, NewTplSlanted, NewTplNotebook, NewTplMusic1, NewTplMusic2,
            NewTplBlank, NewTplGreenBoard, NewTplDarkBoard, NewTplDotGrid, NewTplMillimeter
        };

        foreach (var c in templateCards)
        {
            if (c == null) continue;
            bool match = ((string)c.Tag == kind);
            if (c is Border b)
            {
                b.BorderBrush = match ? new SolidColorBrush(Color.FromRgb(15, 23, 42)) : new SolidColorBrush(Color.FromRgb(226, 232, 240));
                b.BorderThickness = match ? new Thickness(2.0) : new Thickness(1.0);
                b.Background = match ? new SolidColorBrush(Color.FromRgb(241, 245, 249)) : Brushes.White;
            }
        }
    }

    private void CreateBoardFontBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string fontKind)
        {
            CurrentDefaultFontKind = fontKind;
            UpdateCreateBoardFontSelectionUi(fontKind);
        }
    }

    private void UpdateCreateBoardFontSelectionUi(string fontKind)
    {
        if (CreateFontPropysyBtn != null && CreateFontPrintedBtn != null)
        {
            bool isProp = fontKind == "Propysy";
            CreateFontPropysyBtn.Background = isProp ? new SolidColorBrush(Color.FromRgb(15, 23, 42)) : new SolidColorBrush(Color.FromRgb(241, 245, 249));
            CreateFontPropysyBtn.Foreground = isProp ? Brushes.White : new SolidColorBrush(Color.FromRgb(30, 41, 59));

            CreateFontPrintedBtn.Background = !isProp ? new SolidColorBrush(Color.FromRgb(15, 23, 42)) : new SolidColorBrush(Color.FromRgb(241, 245, 249));
            CreateFontPrintedBtn.Foreground = !isProp ? Brushes.White : new SolidColorBrush(Color.FromRgb(30, 41, 59));
        }
    }

    private void ConfirmCreateBoard_Click(object sender, RoutedEventArgs e)
    {
        CloseCreateBoardModal();
        CreateNewBoardWithTemplate(_pendingCreateBoardTemplate, CurrentDefaultFontKind);
    }

    public void CreateNewBoardWithTemplate(string templateKind, string fontKind)
    {
        _autoSaveTimer.Stop();
        SaveCurrentBoardState();

        int nextIndex = _session.Boards.Count + 1;
        bool isCanvasGroup = templateKind is "Blank" or "GreenBoard" or "DarkBoard" or "DotGrid" or "Millimeter";

        string bgColor = templateKind switch
        {
            "GreenBoard" => "#163A2B",
            "DarkBoard" => "#18181B",
            _ => "#FFFFFF"
        };

        var newBoard = new WhiteboardBoardData
        {
            Id = Guid.NewGuid().ToString(),
            Title = $"Аркуш {nextIndex}",
            BackgroundKind = templateKind,
            BackgroundColor = bgColor,
            ShowHeader = !isCanvasGroup, // Only notebook & music staves get notebook header by default
            HeaderDate = GetUkrainianDateWords(DateTime.Now),
            HeaderWorkType = "Класна робота",
            DefaultFont = fontKind,
            SheetGeometry = isCanvasGroup ? "Infinity" : "LeftMargin"
        };

        _session.Boards.Add(newBoard);
        _session.ActiveBoardId = newBoard.Id;

        LoadBoard(newBoard);
        SaveSessionToDisk();

        // If canvas group, switch to drawing tool (Pencil); if notebook, switch to Text mode
        if (isCanvasGroup)
        {
            SwitchToGraphicsMode();
            SetActiveTool(WhiteboardTool.Pencil);
        }
        else
        {
            SwitchToTextMode();
        }

        ShowAiStatus($"Створено аркуш: {newBoard.Title}");
    }

    public void DuplicateCurrentBoard()
    {
        if (_currentBoard == null) return;
        SaveCurrentBoardState();
        int nextIndex = _session.Boards.Count + 1;
        var copy = new WhiteboardBoardData
        {
            Id = Guid.NewGuid().ToString(),
            Title = $"{_currentBoard.Title} (Копія)",
            Description = _currentBoard.Description,
            BackgroundKind = _currentBoard.BackgroundKind,
            BackgroundColor = _currentBoard.BackgroundColor,
            ShowHeader = _currentBoard.ShowHeader,
            HeaderDate = _currentBoard.HeaderDate,
            HeaderWorkType = _currentBoard.HeaderWorkType,
            DefaultFont = _currentBoard.DefaultFont,
            SheetGeometry = _currentBoard.SheetGeometry,
            StrokesIsfBase64 = _currentBoard.StrokesIsfBase64,
            Texts = new List<WhiteboardTextData>(_currentBoard.Texts)
        };
        _session.Boards.Add(copy);
        _session.ActiveBoardId = copy.Id;
        LoadBoard(copy);
        SaveSessionToDisk();
        ShowAiStatus($"Створено копію аркуша: {copy.Title}");
    }

    public void ApplyWhiteboardTheme(string theme)
    {
        switch (theme)
        {
            case "Light":
                ApplySheetStyle("Blank", "#FFFFFF");
                break;
            case "Dark":
                ApplySheetStyle("DarkBoard", "#18181B");
                break;
            case "Auto":
            default:
                ApplySheetStyle("Slanted", "#FFFFFF");
                break;
        }
        ShowAiStatus($"Тема аркуша змінена: {theme}");
    }

    private void OpenColumnModal_Click(object sender, RoutedEventArgs e)
    {
        OpenColumnArithmeticModal("Add");
    }

    private void SheetMarginLeftBtn_Click(object sender, RoutedEventArgs e) => ApplySheetGeometry("LeftMargin");
    private void SheetMarginRightBtn_Click(object sender, RoutedEventArgs e) => ApplySheetGeometry("RightMargin");
    private void SheetMarginNoneBtn_Click(object sender, RoutedEventArgs e) => ApplySheetGeometry("NoMargin");
    private void SheetMarginLargeBtn_Click(object sender, RoutedEventArgs e) => ApplySheetGeometry("LargeBoard");

    private void BoardLangUkBtn_Click(object sender, RoutedEventArgs e)
    {
        CurrentBoardLanguage = "uk";
        ShowAiStatus("Мова дошки: Українська (НУШ)");
    }

    private void BoardLangEnBtn_Click(object sender, RoutedEventArgs e)
    {
        CurrentBoardLanguage = "en";
        if (IpaKeyboardPopup != null) IpaKeyboardPopup.IsOpen = true;
        ShowAiStatus("Language: English (IPA keyboard enabled)");
    }
}
