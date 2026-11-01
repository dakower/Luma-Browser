using System.Globalization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Orientation = System.Windows.Controls.Orientation;
using Path = System.IO.Path;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfPath = System.Windows.Shapes.Path;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace Luma;

/// <summary>
/// Інтерактивна електронна дошка для Luma-Browser (1-в-1 ):
/// - Повна автентична структура та палітра української освітньої дошки  (#22B24C)
/// - Менеджер аркушів (створення, перемикання, видалення, автозбереження сесії на диск)
/// - 5 шаблонів аркуша: Чистий, Клітинка (5 мм), В лінійку, Коса лінія (65°), Нотний стан
/// - 4 кольори аркуша: Білий, Кремовий зошит, Зелена класна дошка, Темний графіт
/// - Шкільний заголовок («27 жовтня», «Класна робота» каліграфічним шрифтом Propysy)
/// - Інструменти 1-в-1: Вказівник, Указка (ідеально центрована імпульсна хвиля зі слідом), Олівець, Маркер, Гумка (S/P), Текст Аа, Стікери, Фігури, Креслярські інструменти, Штампи, Зображення
/// - Креслярські інструменти: Лінійка з робочими кнопками (поворот 0°/90°, зміна довжини), Транспортир 180°
/// - Геометричні фігури з правильними прямими ребрами: Відрізок, Стрілка, Двостороння стрілка, Прямокутник, Коло, Трикутник, Прямокутний трикутник, Осі координат X-Y, Фігурна дужка
/// - Функції для вчителя: Таймер та секундомір уроку з звуковим сигналом, Експорт аркуша у PNG та копіювання в буфер обміну
/// - Шкільні штампи: Молодець, 12 балів, Чудово, Перевірено тощо
/// - Гарантоване перетворення рукописного введення в прописи («🪄 В пропис» / Enter) через Cun.ai (Gemini 2.5 Flash)
/// </summary>
public partial class WhiteboardControl : UserControl
{
    private static readonly Guid ShapeStrokePropertyGuid = new Guid("98765432-1234-4321-abcd-0123456789ab");

    public static readonly Guid WhiteboardShapeGuid = new Guid("8B8E8D5F-1C71-4A59-86BC-5F5943C95147");

    private readonly Dictionary<Stroke, Guid> _strokeShapeMap = new();

    private void RegisterStrokeShapeId(Stroke st, Guid shapeId)
    {
        _strokeShapeMap[st] = shapeId;
        try
        {
            if (!st.ContainsPropertyData(ShapeStrokePropertyGuid))
                st.AddPropertyData(ShapeStrokePropertyGuid, shapeId.ToString());
        }
        catch { }
        try
        {
            if (!st.ContainsPropertyData(WhiteboardShapeGuid))
                st.AddPropertyData(WhiteboardShapeGuid, shapeId.ToString());
        }
        catch { }
    }

    private bool TryGetStrokeShapeId(Stroke st, out Guid shapeId)
    {
        if (_strokeShapeMap.TryGetValue(st, out shapeId))
        {
            return true;
        }
        try
        {
            if (st.ContainsPropertyData(ShapeStrokePropertyGuid))
            {
                var obj = st.GetPropertyData(ShapeStrokePropertyGuid);
                if (obj is string s && Guid.TryParse(s, out shapeId)) return true;
            }
            if (st.ContainsPropertyData(WhiteboardShapeGuid))
            {
                var obj = st.GetPropertyData(WhiteboardShapeGuid);
                if (obj is string s && Guid.TryParse(s, out shapeId)) return true;
                if (obj is byte[] b && b.Length == 16) { shapeId = new Guid(b); return true; }
            }
        }
        catch { }
        shapeId = Guid.Empty;
        return false;
    }

    public const double LineHeight = 50.0;

    private enum WhiteboardTool
    {
        Pointer,
        Laser,
        Pencil,
        Highlighter,
        Eraser,
        Text,
        Shapes
    }

    private enum ShapeKind
    {
        Point,
        Line,
        Ray,
        InfiniteLine,
        Arc,
        Angle,
        Triangle,
        RightTriangle,
        Rhombus,
        Parallelogram,
        Trapezoid,
        Polygon,
        Star,
        Rectangle,
        Square,
        Ellipse,
        CurlyBracket,
        ComparisonBrace,
        SquareBracket,
        CoordinateAxes,
        NumberLine,
        FunctionGraph,
        Tangent,
        Perpendicular,
        Bisector,
        Altitude,
        Cube,
        SquarePyramid,
        Cylinder,
        Sphere,
        Arrow,
        DoubleArrow
    }

    private WhiteboardTool _activeTool = WhiteboardTool.Pencil;
    private ShapeKind _activeShape = ShapeKind.Rectangle;

    private Color _currentPenColor = Color.FromRgb(30, 41, 59);
    private double _currentPenSize = 3.0;
    private Color _currentHighlighterColor = Color.FromArgb(130, 255, 235, 59);
    private bool _eraseByStroke = true;
    private bool _isAiEnabled = true;
    private bool _isAutoPropysyEnabled = false;
    private bool _isProcessingOcr = false;

    // Таймер неактивности для ИИ-распознавания каллиграфии (2.5 сек)
    private readonly DispatcherTimer _inactivityTimer;
    private readonly StrokeCollection _currentWordStrokes = new();
    private readonly StrokeCollection _recentDrawnStrokes = new();

    // Шрифты Propysy (Украинские школьные прописи)
    private readonly FontFamily _propysyFont = new(new Uri("pack://application:,,,/"), "./Fonts/#Propysy Regular, Propysy Regular, Segoe Script, Segoe Print, Arial");
    private readonly FontFamily _propysyBoldFont = new(new Uri("pack://application:,,,/"), "./Fonts/#Propysy Bold, Propysy Bold, Segoe Script, Segoe Print, Arial");

    // Менеджер сессий и аркушей
    private WhiteboardSessionData _session = new();
    private WhiteboardBoardData? _currentBoard;
    private bool _isLoadingSession;
    private readonly DispatcherTimer _autoSaveTimer;

    // Вибрані елементи
    private WhiteboardImageItem? _selectedImageItem;
    private WhiteboardStickerItem? _selectedStickerItem;

    // Стилі ліній пера (суцільна, пунктир, точкова тощо)
    private WhiteboardStrokePattern _currentStrokePattern = WhiteboardStrokePattern.Solid;

    // Перетягування таймера уроку
    private bool _isTimerDragging;
    private Point _timerMouseStart;
    private Point _timerInitialTranslate;

    // Креслярські інструменти
    private WhiteboardRulerItem? _activeRuler;
    private WhiteboardProtractorItem? _activeProtractor;
    private WhiteboardSetSquareItem? _activeSetSquare;

    // Таймер уроку для вчителя
    private readonly DispatcherTimer _lessonTimer;
    private int _timerSeconds = 300; // 5 хвилин за замовчуванням
    private bool _isTimerRunning;

    // Малювання геометричних фігур
    private bool _isDrawingShape;
    private Point _shapeStartPoint;
    private System.Windows.Shapes.Shape? _previewShapeElement;

    // Лазерная указка
    private readonly List<(Point Pt, double Opacity)> _laserTrail = new();
    private readonly DispatcherTimer _laserFadeTimer;
    private System.Windows.Shapes.Ellipse? _laserHeadCircle;
    private System.Windows.Shapes.Ellipse? _laserGlowCircle;

    // Система Undo / Redo
    private readonly Stack<IWhiteboardAction> _undoStack = new();
    private readonly Stack<IWhiteboardAction> _redoStack = new();
    private bool _isExecutingUndoRedo = false;

    public WhiteboardControl()
    {
        InitializeComponent();

        // 1. Таймер дебаунса для ИИ-распознавания рукописного ввода
        _inactivityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(4500)
        };
        _inactivityTimer.Tick += OnInactivityTimerTick;

        // 2. Таймер дебаунса автосохранения сессии
        _autoSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _autoSaveTimer.Tick += (_, _) =>
        {
            _autoSaveTimer.Stop();
            SaveCurrentBoardState();
            SaveSessionToDisk();
        };

        // 3. Таймер плавного затухания следа лазерной указки (30 кадров/сек)
        _laserFadeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _laserFadeTimer.Tick += LaserFadeTimer_Tick;

        // 4. Таймер урока для учителя
        _lessonTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _lessonTimer.Tick += LessonTimer_Tick;

        // 5. Відстеження зміни штрихів в InkCanvas
        MainCanvas.StrokeCollected += MainCanvas_StrokeCollected;
        MainCanvas.StrokeErasing += MainCanvas_StrokeErasing;
        MainCanvas.StrokeErased += MainCanvas_StrokeErased;
        MainCanvas.Strokes.StrokesChanged += MainCanvas_StrokesChanged;

        // 6. Инициализация пера, математических символов и интерфейса
        ApplyPenAttributes();
        UpdateUndoRedoButtons();
        UpdateTimerDisplay();
        InitMathSymbols();

        // 7. Загрузка сессии
        Loaded += WhiteboardControl_Loaded;
        Unloaded += WhiteboardControl_Unloaded;
    }

    private void WhiteboardControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (_session.Boards.Count == 0)
        {
            LoadSessionFromDisk();
        }
        RootGrid.Focus();
    }

    private void WhiteboardControl_Unloaded(object sender, RoutedEventArgs e)
    {
        _autoSaveTimer.Stop();
        _laserFadeTimer.Stop();
        _lessonTimer.Stop();
        SaveCurrentSlideStrokes();
        SaveCurrentBoardState();
        SaveSessionToDisk();
    }

    // =========================================================================
    // СЕССИЯ И МЕНЕДЖЕР АРКУШЕЙ (SESSION & MULTI-BOARD MANAGER)
    // =========================================================================

    private static string GetSessionFilePath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "whiteboard_session.json");
    }

    private void LoadSessionFromDisk()
    {
        _isLoadingSession = true;
        try
        {
            string path = GetSessionFilePath();
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var loaded = JsonSerializer.Deserialize<WhiteboardSessionData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (loaded != null && loaded.Boards.Count > 0)
                    {
                        _session = loaded;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }

        if (_session.Boards.Count == 0)
        {
            var firstBoard = new WhiteboardBoardData
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Лист 1",
                BackgroundKind = "Grid",
                BackgroundColor = "#FFFFFF",
                ShowHeader = false
            };
            _session.Boards.Add(firstBoard);
            _session.ActiveBoardId = firstBoard.Id;
        }

        var active = _session.Boards.FirstOrDefault(b => b.Id == _session.ActiveBoardId) ?? _session.Boards[0];
        try
        {
            LoadBoard(active);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        finally
        {
            _isLoadingSession = false;
        }
    }

    private void SaveSessionToDisk()
    {
        if (_isLoadingSession) return;
        try
        {
            string path = GetSessionFilePath();
            string json = JsonSerializer.Serialize(_session, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(path, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private void ScheduleAutoSave()
    {
        if (_isLoadingSession) return;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void SaveCurrentBoardState()
    {
        if (_currentBoard == null) return;

        _currentBoard.HeaderDate = HeaderDateBox.Text;
        _currentBoard.HeaderWorkType = HeaderWorkTypeBox.Text;
        _currentBoard.ShowHeader = NotebookHeaderContainer.Visibility == Visibility.Visible;

        _currentBoard.StrokesIsfBase64 = SerializeStrokes(MainCanvas.Strokes);

        _currentBoard.Texts.Clear();
        foreach (UIElement child in MainCanvas.Children)
        {
            if (child is FrameworkElement fe && fe.Tag is WhiteboardTextTag tag)
            {
                _currentBoard.Texts.Add(new WhiteboardTextData
                {
                    Text = tag.Text,
                    ColorHex = tag.ColorHex,
                    FontSize = tag.FontSize,
                    X = InkCanvas.GetLeft(fe),
                    Y = InkCanvas.GetTop(fe)
                });
            }
        }

        _currentBoard.Images.Clear();
        foreach (UIElement child in MainCanvas.Children)
        {
            if (child is WhiteboardImageItem imgItem && !string.IsNullOrEmpty(imgItem.Base64Data))
            {
                _currentBoard.Images.Add(new WhiteboardImageData
                {
                    Id = imgItem.Id,
                    Base64Data = imgItem.Base64Data,
                    X = InkCanvas.GetLeft(imgItem),
                    Y = InkCanvas.GetTop(imgItem),
                    Width = imgItem.ActualWidth > 0 ? imgItem.ActualWidth : imgItem.Width,
                    Height = imgItem.ActualHeight > 0 ? imgItem.ActualHeight : imgItem.Height
                });
            }
        }

        _currentBoard.Stickers.Clear();
        foreach (UIElement child in MainCanvas.Children)
        {
            if (child is WhiteboardStickerItem sticker)
            {
                _currentBoard.Stickers.Add(new WhiteboardStickerData
                {
                    Id = sticker.Id,
                    ColorHex = sticker.ColorHex,
                    Text = sticker.Text,
                    X = InkCanvas.GetLeft(sticker),
                    Y = InkCanvas.GetTop(sticker),
                    Width = sticker.ActualWidth > 0 ? sticker.ActualWidth : sticker.Width,
                    Height = sticker.ActualHeight > 0 ? sticker.ActualHeight : sticker.Height
                });
            }
        }

        if (_activeRuler != null && MainCanvas.Children.Contains(_activeRuler))
        {
            _currentBoard.IsRulerVisible = true;
            _currentBoard.RulerX = InkCanvas.GetLeft(_activeRuler);
            _currentBoard.RulerY = InkCanvas.GetTop(_activeRuler);
            _currentBoard.RulerAngle = _activeRuler.Angle;
        }
        else
        {
            _currentBoard.IsRulerVisible = false;
        }
    }

    private void LoadBoard(WhiteboardBoardData board)
    {
        _isLoadingSession = true;
        _inactivityTimer.Stop();
        _currentWordStrokes.Clear();
        _recentDrawnStrokes.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
        UpdateUndoRedoButtons();

        DeselectAllItems();

        _currentBoard = board;
        _session.ActiveBoardId = board.Id;

        MainCanvas.Strokes.Clear();
        _recentDrawnStrokes.Clear();
        MainCanvas.Children.Clear();
        _activeRuler = null;
        _activeProtractor = null;
        _activeSetSquare = null;

        ApplySheetStyle(board.BackgroundKind, board.BackgroundColor);

        HeaderDateBox.Text = string.IsNullOrWhiteSpace(board.HeaderDate) ? "27 жовтня" : board.HeaderDate;
        HeaderWorkTypeBox.Text = string.IsNullOrWhiteSpace(board.HeaderWorkType) ? "Класна робота" : board.HeaderWorkType;
        NotebookHeaderContainer.Visibility = board.ShowHeader ? Visibility.Visible : Visibility.Collapsed;
        HeaderToggleCheckBox.IsChecked = board.ShowHeader;

        if (!string.IsNullOrWhiteSpace(board.StrokesIsfBase64))
        {
            try
            {
                var strokes = DeserializeStrokes(board.StrokesIsfBase64);
                if (strokes != null && strokes.Count > 0)
                {
                    for (int i = 0; i < strokes.Count; i++)
                    {
                        var st = strokes[i];
                        if (st.ContainsPropertyData(WhiteboardDashedStroke.StrokePatternGuid))
                        {
                            var patVal = (int)st.GetPropertyData(WhiteboardDashedStroke.StrokePatternGuid);
                            var pattern = (WhiteboardStrokePattern)patVal;
                            if (pattern != WhiteboardStrokePattern.Solid)
                            {
                                strokes[i] = new WhiteboardDashedStroke(st.StylusPoints.Clone(), st.DrawingAttributes.Clone(), pattern);
                            }
                        }
                    }
                    foreach (var st in strokes)
                    {
                        if (!MainCanvas.Strokes.Contains(st))
                        {
                            try { MainCanvas.Strokes.Add(st); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log(ex);
            }
        }

        if (board.Texts != null)
        {
            foreach (var t in board.Texts)
            {
                if (string.IsNullOrWhiteSpace(t.Text)) continue;
                Color color = Color.FromRgb(30, 41, 59);
                try { color = (Color)ColorConverter.ConvertFromString(t.ColorHex); } catch { }

                var textElem = CreateDraggablePropysyText(t.Text, color, t.FontSize);
                InkCanvas.SetLeft(textElem, t.X);
                InkCanvas.SetTop(textElem, t.Y);
                MainCanvas.Children.Add(textElem);
            }
        }

        if (board.Images != null)
        {
            foreach (var imgData in board.Images)
            {
                if (string.IsNullOrWhiteSpace(imgData.Base64Data)) continue;
                try
                {
                    var bitmap = Base64ToBitmapImage(imgData.Base64Data);
                    var imgItem = new WhiteboardImageItem(imgData.Id, bitmap, imgData.Base64Data, imgData.Width, imgData.Height);
                    AttachImageItemEvents(imgItem);
                    InkCanvas.SetLeft(imgItem, imgData.X);
                    InkCanvas.SetTop(imgItem, imgData.Y);
                    MainCanvas.Children.Add(imgItem);
                }
                catch (Exception ex)
                {
                    App.Log(ex);
                }
            }
        }

        if (board.Stickers != null)
        {
            foreach (var sData in board.Stickers)
            {
                var sticker = new WhiteboardStickerItem(sData.Id, sData.ColorHex, sData.Text, sData.Width, sData.Height);
                AttachStickerItemEvents(sticker);
                InkCanvas.SetLeft(sticker, sData.X);
                InkCanvas.SetTop(sticker, sData.Y);
                MainCanvas.Children.Add(sticker);
            }
        }

        if (board.IsRulerVisible)
        {
            ShowRuler(board.RulerX, board.RulerY, board.RulerAngle);
        }

        RefreshBoardsTabsUi();
        SyncPresentationForCurrentBoard();

        _isLoadingSession = false;
    }

    private void RefreshBoardsTabsUi()
    {
        BoardsTabContainer.Children.Clear();

        for (int i = 0; i < _session.Boards.Count; i++)
        {
            var b = _session.Boards[i];
            bool isActive = (b.Id == _currentBoard?.Id);

            var tabBorder = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 4, 8, 4),
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                Background = isActive ? new SolidColorBrush(Color.FromRgb(15, 23, 42)) : Brushes.Transparent,
                BorderBrush = isActive ? new SolidColorBrush(Color.FromRgb(30, 41, 59)) : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                SnapsToDevicePixels = true,
                Tag = b.Id
            };

            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = WpfVerticalAlignment.Center
            };

            var iconPath = new WpfPath
            {
                Data = Geometry.Parse("M 14,2 L 6,2 C 4.9,2 4,2.9 4,4 L 4,20 C 4,21.1 4.9,22 6,22 L 18,22 C 19.1,22 20,21.1 20,20 L 20,8 L 14,2 Z M 13,9 L 13,3.5 L 18.5,9 L 13,9 Z"),
                Width = 11,
                Height = 11,
                Stretch = Stretch.Uniform,
                Fill = isActive ? Brushes.White : new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = WpfVerticalAlignment.Center
            };
            stack.Children.Add(iconPath);

            var titleBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(b.Title) ? $"Аркуш {i + 1}" : b.Title,
                FontSize = 11.5,
                FontWeight = isActive ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = isActive ? Brushes.White : new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                VerticalAlignment = WpfVerticalAlignment.Center
            };
            stack.Children.Add(titleBlock);

            Button? currentCloseBtn = null;
            if (_session.Boards.Count > 1)
            {
                var closeBtn = new Button
                {
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(6, 0, 0, 0),
                    Cursor = Cursors.Hand,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    ToolTip = "Видалити цей аркуш",
                    Tag = b.Id
                };

                var closeTemplate = new ControlTemplate(typeof(Button));
                var fef = new FrameworkElementFactory(typeof(Border));
                fef.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
                fef.SetValue(Border.BackgroundProperty, Brushes.Transparent);

                var closeIcon = new FrameworkElementFactory(typeof(WpfPath));
                closeIcon.SetValue(WpfPath.DataProperty, Geometry.Parse("M 4,4 L 12,12 M 12,4 L 4,12"));
                closeIcon.SetValue(WpfPath.StrokeProperty, isActive ? Brushes.White : new SolidColorBrush(Color.FromRgb(148, 163, 184)));
                closeIcon.SetValue(WpfPath.StrokeThicknessProperty, 1.6);
                closeIcon.SetValue(WpfPath.WidthProperty, 8.0);
                closeIcon.SetValue(WpfPath.HeightProperty, 8.0);
                closeIcon.SetValue(WpfPath.StretchProperty, Stretch.Uniform);
                closeIcon.SetValue(WpfPath.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
                closeIcon.SetValue(WpfPath.VerticalAlignmentProperty, WpfVerticalAlignment.Center);

                fef.AppendChild(closeIcon);
                closeTemplate.VisualTree = fef;
                closeBtn.Template = closeTemplate;

                closeBtn.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    if (s is Button btn && btn.Tag is string boardId)
                    {
                        DeleteBoard(boardId);
                    }
                };
                closeBtn.Click += (s, e) =>
                {
                    e.Handled = true;
                    if (s is Button btn && btn.Tag is string boardId)
                    {
                        DeleteBoard(boardId);
                    }
                };

                currentCloseBtn = closeBtn;
                stack.Children.Add(closeBtn);
            }

            tabBorder.Child = stack;

            string targetId = b.Id;
            tabBorder.MouseLeftButtonDown += (s, e) =>
            {
                if (currentCloseBtn != null && e.OriginalSource is DependencyObject dep && IsChildOf(dep, currentCloseBtn)) return;
                if (targetId != _currentBoard?.Id)
                {
                    SwitchToBoard(targetId);
                }
            };

            if (!isActive)
            {
                tabBorder.MouseEnter += (_, _) => tabBorder.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                tabBorder.MouseLeave += (_, _) => tabBorder.Background = Brushes.Transparent;
            }

            BoardsTabContainer.Children.Add(tabBorder);
        }
    }

    private void AddBoardBtn_Click(object sender, RoutedEventArgs e)
    {
        _autoSaveTimer.Stop();
        SaveCurrentBoardState();

        int nextIndex = _session.Boards.Count + 1;
        var newBoard = new WhiteboardBoardData
        {
            Id = Guid.NewGuid().ToString(),
            Title = $"Лист {nextIndex}",
            BackgroundKind = _currentBoard?.BackgroundKind ?? "Slanted",
            BackgroundColor = _currentBoard?.BackgroundColor ?? "#FFFFFF",
            ShowHeader = false
        };
        _session.Boards.Add(newBoard);
        _session.ActiveBoardId = newBoard.Id;

        LoadBoard(newBoard);
        SaveSessionToDisk();
    }

    private void SwitchToBoard(string boardId)
    {
        var target = _session.Boards.FirstOrDefault(b => b.Id == boardId);
        if (target == null || target == _currentBoard) return;

        _autoSaveTimer.Stop();
        SaveCurrentSlideStrokes();
        SaveCurrentBoardState();
        SaveSessionToDisk();

        LoadBoard(target);
    }

    private void DeleteBoard(string boardId)
    {
        if (_session.Boards.Count <= 1) return;

        int index = _session.Boards.FindIndex(b => b.Id == boardId);
        if (index < 0) return;

        bool wasActive = (_session.Boards[index].Id == _currentBoard?.Id);
        _session.Boards.RemoveAt(index);
        _boardPresentations.Remove(boardId);

        if (wasActive)
        {
            int newActiveIndex = Math.Min(index, _session.Boards.Count - 1);
            var nextBoard = _session.Boards[newActiveIndex];
            _session.ActiveBoardId = nextBoard.Id;
            LoadBoard(nextBoard);
        }
        else
        {
            RefreshBoardsTabsUi();
        }

        SaveSessionToDisk();
    }

    // =========================================================================
    // СИСТЕМА СКАСУВАТИ ТА ПОВТОРИТИ (UNDO / REDO)
    // =========================================================================

    private void MainCanvas_StrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (_isLoadingSession) return;
        ScheduleAutoSave();

        if (e.Added.Count > 0)
        {
            foreach (var st in e.Added)
            {
                if (!_recentDrawnStrokes.Contains(st))
                {
                    try { _recentDrawnStrokes.Add(st); } catch { }
                }
            }
        }

        if (_isExecutingUndoRedo) return;

        if (e.Removed.Count > 0)
        {
            var removedShapeIds = new HashSet<Guid>();
            foreach (var s in e.Removed)
            {
                if (TryGetStrokeShapeId(s, out var sid))
                {
                    removedShapeIds.Add(sid);
                }
            }

            foreach (var shapeId in removedShapeIds)
            {
                bool anyLeft = MainCanvas.Strokes.Any(s => TryGetStrokeShapeId(s, out var sid) && sid == shapeId);
                if (!anyLeft)
                {
                    var toRemove = MainCanvas.Children.OfType<FrameworkElement>()
                        .Where(f => (f.Tag is WhiteboardVertexTag vt && vt.ShapeId == shapeId) ||
                                    (f.Tag is string str && str == "VertexLabel" && MainCanvas.Strokes.Count == 0)).ToList();
                    foreach (var fe in toRemove)
                    {
                        MainCanvas.Children.Remove(fe);
                    }
                }
            }

            if (MainCanvas.Strokes.Count == 0)
            {
                var orphaned = MainCanvas.Children.OfType<FrameworkElement>()
                    .Where(f => f.Tag is WhiteboardVertexTag || (f.Tag is string s && s == "VertexLabel")).ToList();
                foreach (var fe in orphaned)
                {
                    MainCanvas.Children.Remove(fe);
                }
            }
        }

        if (e.Added.Count > 0 && e.Removed.Count == 0)
        {
            _undoStack.Push(new StrokesAddedAction(e.Added, MainCanvas));
            _redoStack.Clear();
            UpdateUndoRedoButtons();
        }
        else if (e.Removed.Count > 0 && e.Added.Count == 0)
        {
            _undoStack.Push(new StrokesRemovedAction(e.Removed, MainCanvas));
            _redoStack.Clear();
            UpdateUndoRedoButtons();
        }
    }

    private void UndoBtn_Click(object sender, RoutedEventArgs e) => PerformUndo();
    private void RedoBtn_Click(object sender, RoutedEventArgs e) => PerformRedo();

    private void PerformUndo()
    {
        if (_undoStack.Count == 0) return;
        _isExecutingUndoRedo = true;
        try
        {
            var action = _undoStack.Pop();
            action.Undo();
            _redoStack.Push(action);
            ScheduleAutoSave();
        }
        finally
        {
            _isExecutingUndoRedo = false;
            UpdateUndoRedoButtons();
        }
    }

    private void PerformRedo()
    {
        if (_redoStack.Count == 0) return;
        _isExecutingUndoRedo = true;
        try
        {
            var action = _redoStack.Pop();
            action.Redo();
            _undoStack.Push(action);
            ScheduleAutoSave();
        }
        finally
        {
            _isExecutingUndoRedo = false;
            UpdateUndoRedoButtons();
        }
    }

    private void UpdateUndoRedoButtons()
    {
        UndoBtn.IsEnabled = _undoStack.Count > 0;
        RedoBtn.IsEnabled = _redoStack.Count > 0;
    }

    // =========================================================================
    // НАЛАШТУВАННЯ АРКУША
    // =========================================================================

    private void SheetSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        SheetSettingsModal.Visibility = (SheetSettingsModal.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CloseSettingsModal_Click(object sender, RoutedEventArgs e)
    {
        SheetSettingsModal.Visibility = Visibility.Collapsed;
    }

    private void TemplateCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement card && card.Tag is string kind)
        {
            string currentColor = _currentBoard?.BackgroundColor ?? "#FFFFFF";
            ApplySheetStyle(kind, currentColor);
            if (_currentBoard != null)
            {
                _currentBoard.BackgroundKind = kind;
                ScheduleAutoSave();
            }
            HighlightActiveTemplateCard(kind);
        }
    }

    private void SheetColorChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement chip && chip.Tag is string hex)
        {
            string currentKind = _currentBoard?.BackgroundKind ?? "Slanted";
            ApplySheetStyle(currentKind, hex);
            if (_currentBoard != null)
            {
                _currentBoard.BackgroundColor = hex;
                ScheduleAutoSave();
            }
            HighlightActiveColorChip(hex);
        }
    }

    private void HeaderToggleCheckBox_Click(object sender, RoutedEventArgs e)
    {
        bool show = HeaderToggleCheckBox.IsChecked ?? false;
        NotebookHeaderContainer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (_currentBoard != null)
        {
            _currentBoard.ShowHeader = show;
            ScheduleAutoSave();
        }
    }

    private void HeaderBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_currentBoard != null)
        {
            _currentBoard.HeaderDate = HeaderDateBox.Text;
            _currentBoard.HeaderWorkType = HeaderWorkTypeBox.Text;
            ScheduleAutoSave();
        }
    }

    private void ApplySheetStyle(string kind, string colorHex)
    {
        Color bgColor;
        try { bgColor = (Color)ColorConverter.ConvertFromString(colorHex); } catch { bgColor = Colors.White; }

        bool isDark = (colorHex == "#163A2B" || colorHex == "#18181B");

        HeaderDateBox.Foreground = isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(30, 41, 59));
        HeaderWorkTypeBox.Foreground = isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(30, 41, 59));
        HeaderDateBox.CaretBrush = isDark ? Brushes.White : Brushes.Black;
        HeaderWorkTypeBox.CaretBrush = isDark ? Brushes.White : Brushes.Black;

        if (isDark && _currentPenColor == Color.FromRgb(30, 41, 59))
        {
            _currentPenColor = Colors.White;
            PencilColorDot.Fill = Brushes.White;
            ApplyPenAttributes();
        }
        else if (!isDark && _currentPenColor == Colors.White)
        {
            _currentPenColor = Color.FromRgb(30, 41, 59);
            PencilColorDot.Fill = new SolidColorBrush(_currentPenColor);
            ApplyPenAttributes();
        }

        switch (kind)
        {
            case "Blank":
                MainCanvas.Background = new SolidColorBrush(bgColor);
                break;
            case "Grid":
                MainCanvas.Background = CreateGridBrush(bgColor, isDark);
                break;
            case "Ruled":
                MainCanvas.Background = CreateRuledBrush(bgColor, isDark);
                break;
            case "Music":
                MainCanvas.Background = CreateMusicStaveBrush(bgColor, isDark);
                break;
            case "Slanted":
            default:
                MainCanvas.Background = CreateSlantedBrush(bgColor, isDark);
                break;
        }

        if (NotebookMarginLine != null)
        {
            NotebookMarginLine.Visibility = (kind == "Slanted") ? Visibility.Visible : Visibility.Collapsed;
        }

        HighlightActiveTemplateCard(kind);
        HighlightActiveColorChip(colorHex);
    }

    private void HighlightActiveTemplateCard(string kind)
    {
        var cards = new[] { TemplateBlankCard, TemplateGridCard, TemplateRuledCard, TemplateSlantedCard, TemplateMusicCard };
        foreach (var c in cards)
        {
            if (c == null) continue;
            bool match = ((string)c.Tag == kind);
            c.BorderBrush = match ? new SolidColorBrush(Color.FromRgb(15, 23, 42)) : Brushes.Transparent;
            c.BorderThickness = match ? new Thickness(2) : new Thickness(0);
        }
    }

    private void HighlightActiveColorChip(string hex)
    {
        var chips = new[] { ColorWhiteChip, ColorCreamChip, ColorGreenBoardChip, ColorDarkGraphiteChip };
        foreach (var chip in chips)
        {
            if (chip == null) continue;
            bool match = string.Equals((string)chip.Tag, hex, StringComparison.OrdinalIgnoreCase);
            chip.BorderBrush = match ? new SolidColorBrush(Color.FromRgb(15, 23, 42)) : new SolidColorBrush(Color.FromRgb(100, 116, 139));
            chip.BorderThickness = match ? new Thickness(2.5) : new Thickness(1);
        }
    }

    private static DrawingBrush CreateGridBrush(Color bgColor, bool isDark)
    {
        const double cellSize = 25.0;
        Color lineColor = isDark ? Color.FromArgb(45, 255, 255, 255) : Color.FromRgb(215, 222, 232);

        var lineGroup = new GeometryGroup();
        lineGroup.Children.Add(new LineGeometry(new Point(0, cellSize), new Point(cellSize, cellSize)));
        lineGroup.Children.Add(new LineGeometry(new Point(cellSize, 0), new Point(cellSize, cellSize)));

        var drawingGroup = new DrawingGroup();
        drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(bgColor), null, new RectangleGeometry(new Rect(0, 0, cellSize, cellSize))));
        var pen = new Pen(new SolidColorBrush(lineColor), 0.75);
        pen.Freeze();
        drawingGroup.Children.Add(new GeometryDrawing(null, pen, lineGroup));

        var brush = new DrawingBrush(drawingGroup)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, cellSize, cellSize),
            ViewportUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    private static DrawingBrush CreateRuledBrush(Color bgColor, bool isDark)
    {
        Color lineColor = isDark ? Color.FromArgb(55, 255, 255, 255) : Color.FromRgb(180, 196, 218);

        var lineGroup = new GeometryGroup();
        lineGroup.Children.Add(new LineGeometry(new Point(0, LineHeight), new Point(100, LineHeight)));

        var drawingGroup = new DrawingGroup();
        drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(bgColor), null, new RectangleGeometry(new Rect(0, 0, 100, LineHeight))));
        var pen = new Pen(new SolidColorBrush(lineColor), 1.0);
        pen.Freeze();
        drawingGroup.Children.Add(new GeometryDrawing(null, pen, lineGroup));

        var brush = new DrawingBrush(drawingGroup)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 100, LineHeight),
            ViewportUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    private static DrawingBrush CreateSlantedBrush(Color bgColor, bool isDark)
    {
        double angleRad = 65.0 * Math.PI / 180.0;
        double tileWidth = LineHeight / Math.Tan(angleRad);

        var drawingGroup = new DrawingGroup();
        drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(bgColor), null, new RectangleGeometry(new Rect(0, 0, tileWidth, LineHeight))));

        Color baseCol = isDark ? Color.FromArgb(90, 255, 255, 255) : Color.FromRgb(110, 134, 170);
        Color midCol = isDark ? Color.FromArgb(55, 255, 255, 255) : Color.FromRgb(165, 185, 212);
        Color topCol = isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromRgb(200, 215, 232);
        Color slantCol = isDark ? Color.FromArgb(50, 255, 255, 255) : Color.FromRgb(190, 208, 230);

        var basePen = new Pen(new SolidColorBrush(baseCol), 1.25); basePen.Freeze();
        var midPen = new Pen(new SolidColorBrush(midCol), 0.85); midPen.Freeze();
        var topPen = new Pen(new SolidColorBrush(topCol), 0.7); topPen.Freeze();
        var slantPen = new Pen(new SolidColorBrush(slantCol), 0.75); slantPen.Freeze();

        drawingGroup.Children.Add(new GeometryDrawing(null, basePen, new LineGeometry(new Point(0, LineHeight), new Point(tileWidth, LineHeight))));
        drawingGroup.Children.Add(new GeometryDrawing(null, midPen, new LineGeometry(new Point(0, 20), new Point(tileWidth, 20))));
        drawingGroup.Children.Add(new GeometryDrawing(null, topPen, new LineGeometry(new Point(0, 0), new Point(tileWidth, 0))));
        drawingGroup.Children.Add(new GeometryDrawing(null, slantPen, new LineGeometry(new Point(0, 0), new Point(tileWidth, LineHeight))));

        var brush = new DrawingBrush(drawingGroup)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, tileWidth, LineHeight),
            ViewportUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    private static DrawingBrush CreateMusicStaveBrush(Color bgColor, bool isDark)
    {
        const double staveTileHeight = 90.0;
        Color lineCol = isDark ? Color.FromArgb(80, 255, 255, 255) : Color.FromRgb(100, 116, 139);

        var staveLines = new GeometryGroup();
        for (int i = 0; i < 5; i++)
        {
            double y = 10.0 + (i * 8.0);
            staveLines.Children.Add(new LineGeometry(new Point(0, y), new Point(100, y)));
        }

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

    // =========================================================================
    // ПАНЕЛЬ ІНСТРУМЕНТІВ
    // =========================================================================

    private void PointerBtn_Click(object sender, RoutedEventArgs e) => SetActiveTool(WhiteboardTool.Pointer);
    private void LaserBtn_Click(object sender, RoutedEventArgs e) => SetActiveTool(WhiteboardTool.Laser);

    private void PencilBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTool == WhiteboardTool.Pencil)
        {
            PencilSettingsPopup.IsOpen = !PencilSettingsPopup.IsOpen;
        }
        else
        {
            SetActiveTool(WhiteboardTool.Pencil);
        }
    }

    private void HighlighterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTool == WhiteboardTool.Highlighter)
        {
            HighlighterPopup.IsOpen = !HighlighterPopup.IsOpen;
        }
        else
        {
            SetActiveTool(WhiteboardTool.Highlighter);
        }
    }

    private void EraserBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTool == WhiteboardTool.Eraser)
        {
            _eraseByStroke = !_eraseByStroke;
            UpdateEraserModeDisplay();
        }
        else
        {
            SetActiveTool(WhiteboardTool.Eraser);
        }
    }

    private void TextToolBtn_Click(object sender, RoutedEventArgs e) => SetActiveTool(WhiteboardTool.Text);

    private void StickerToolBtn_Click(object sender, RoutedEventArgs e)
    {
        StickerColorPopup.IsOpen = !StickerColorPopup.IsOpen;
    }

    private void ShapesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTool == WhiteboardTool.Shapes)
        {
            ShapesPopup.IsOpen = !ShapesPopup.IsOpen;
        }
        else
        {
            SetActiveTool(WhiteboardTool.Shapes);
            ShapesPopup.IsOpen = true;
        }
        if (ShapesPopup.IsOpen)
        {
            FilterShapesByCategory("Simplest");
        }
    }

    private void ShapeSelect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tag)
        {
            if (Enum.TryParse<ShapeKind>(tag, out var shapeKind))
            {
                _activeShape = shapeKind;
                ShapesPopup.IsOpen = false;
                SetActiveTool(WhiteboardTool.Shapes);
            }
        }
    }

    private void DraftingToolsBtn_Click(object sender, RoutedEventArgs e)
    {
        DraftingPopup.IsOpen = !DraftingPopup.IsOpen;
    }

    private void SelectDraftingTool_Click(object sender, RoutedEventArgs e)
    {
        DraftingPopup.IsOpen = false;
        if (sender is FrameworkElement fe && fe.Tag is string tool)
        {
            if (tool == "Ruler")
            {
                ToggleRuler();
            }
            else if (tool == "Protractor")
            {
                ToggleProtractor();
            }
            else if (tool == "SetSquare")
            {
                ToggleSetSquare();
            }
        }
    }

    private void StrokePattern_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string patStr)
        {
            if (Enum.TryParse<WhiteboardStrokePattern>(patStr, out var pat))
            {
                _currentStrokePattern = pat;
                UpdateStrokePatternButtonsUi();
            }
        }
    }

    private void UpdateStrokePatternButtonsUi()
    {
        var btns = new[] { StrokeSolidBtn, StrokeDoubleBtn, StrokeWavyBtn, StrokeDashedBtn, StrokeDashDotBtn, StrokeDottedBtn };
        foreach (var b in btns)
        {
            if (b == null) continue;
            bool active = (string)b.Tag == _currentStrokePattern.ToString();
            if (b.Template?.FindName("bd", b) is Border bd)
            {
                bd.Background = active ? new SolidColorBrush(Color.FromRgb(240, 253, 244)) : new SolidColorBrush(Color.FromRgb(248, 250, 252));
                bd.BorderBrush = active ? new SolidColorBrush(Color.FromRgb(34, 178, 76)) : new SolidColorBrush(Color.FromRgb(226, 232, 240));
                bd.BorderThickness = active ? new Thickness(1.5) : new Thickness(1);
            }
        }
    }

    private void ToggleRuler()
    {
        if (_activeRuler != null && MainCanvas.Children.Contains(_activeRuler))
        {
            MainCanvas.Children.Remove(_activeRuler);
            _activeRuler = null;
            ScheduleAutoSave();
        }
        else
        {
            double posX = Math.Max(50, (MainCanvas.ActualWidth - 500) / 2);
            double posY = Math.Max(120, (MainCanvas.ActualHeight - 70) / 2);
            ShowRuler(posX, posY, 0.0);
            ScheduleAutoSave();
        }
    }

    private void ShowRuler(double x, double y, double angle)
    {
        if (_activeRuler != null && MainCanvas.Children.Contains(_activeRuler))
        {
            MainCanvas.Children.Remove(_activeRuler);
        }

        _activeRuler = new WhiteboardRulerItem(500, 70, angle);
        _activeRuler.CloseRequested += () =>
        {
            if (MainCanvas.Children.Contains(_activeRuler))
            {
                MainCanvas.Children.Remove(_activeRuler);
                _activeRuler = null;
                ScheduleAutoSave();
            }
        };
        _activeRuler.Changed += ScheduleAutoSave;

        InkCanvas.SetLeft(_activeRuler, x);
        InkCanvas.SetTop(_activeRuler, y);
        MainCanvas.Children.Add(_activeRuler);
    }

    private void ToggleProtractor()
    {
        if (_activeProtractor != null && MainCanvas.Children.Contains(_activeProtractor))
        {
            MainCanvas.Children.Remove(_activeProtractor);
            _activeProtractor = null;
            ScheduleAutoSave();
        }
        else
        {
            double posX = Math.Max(50, (MainCanvas.ActualWidth - 360) / 2);
            double posY = Math.Max(120, (MainCanvas.ActualHeight - 200) / 2);

            _activeProtractor = new WhiteboardProtractorItem(360, 200);
            _activeProtractor.CloseRequested += () =>
            {
                if (MainCanvas.Children.Contains(_activeProtractor))
                {
                    MainCanvas.Children.Remove(_activeProtractor);
                    _activeProtractor = null;
                    ScheduleAutoSave();
                }
            };

            InkCanvas.SetLeft(_activeProtractor, posX);
            InkCanvas.SetTop(_activeProtractor, posY);
            MainCanvas.Children.Add(_activeProtractor);
            ScheduleAutoSave();
        }
    }

    private void ToggleSetSquare()
    {
        if (_activeSetSquare != null && MainCanvas.Children.Contains(_activeSetSquare))
        {
            MainCanvas.Children.Remove(_activeSetSquare);
            _activeSetSquare = null;
            ScheduleAutoSave();
        }
        else
        {
            double posX = Math.Max(50, (MainCanvas.ActualWidth - 340) / 2);
            double posY = Math.Max(120, (MainCanvas.ActualHeight - 260) / 2);

            _activeSetSquare = new WhiteboardSetSquareItem(320, 0);
            _activeSetSquare.CloseRequested += () =>
            {
                if (MainCanvas.Children.Contains(_activeSetSquare))
                {
                    MainCanvas.Children.Remove(_activeSetSquare);
                    _activeSetSquare = null;
                    ScheduleAutoSave();
                }
            };
            _activeSetSquare.Changed += ScheduleAutoSave;

            InkCanvas.SetLeft(_activeSetSquare, posX);
            InkCanvas.SetTop(_activeSetSquare, posY);
            MainCanvas.Children.Add(_activeSetSquare);
            ScheduleAutoSave();
        }
    }

    // =========================================================================
    // ТАЙМЕР ТА СЕКУНДОМІР УРОКУ (ДЛЯ ВЧИТЕЛЯ)
    // =========================================================================

    private void TimerTopBtn_Click(object sender, RoutedEventArgs e)
    {
        LessonTimerWidget.Visibility = (LessonTimerWidget.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CloseTimerWidget_Click(object sender, RoutedEventArgs e)
    {
        LessonTimerWidget.Visibility = Visibility.Collapsed;
    }

    private void LessonTimerWidget_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Point hit = e.GetPosition(LessonTimerWidget);
        var hitResult = VisualTreeHelper.HitTest(LessonTimerWidget, hit);
        if (hitResult?.VisualHit != null)
        {
            DependencyObject? cur = hitResult.VisualHit;
            while (cur != null && cur != LessonTimerWidget)
            {
                if (cur is Button) return; // Allow button clicks
                cur = VisualTreeHelper.GetParent(cur);
            }
        }

        _isTimerDragging = true;
        _timerMouseStart = e.GetPosition(RootGrid);
        _timerInitialTranslate = new Point(TimerTranslateTransform.X, TimerTranslateTransform.Y);
        LessonTimerWidget.CaptureMouse();
        e.Handled = true;
    }

    private void LessonTimerWidget_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isTimerDragging)
        {
            Point cur = e.GetPosition(RootGrid);
            TimerTranslateTransform.X = _timerInitialTranslate.X + (cur.X - _timerMouseStart.X);
            TimerTranslateTransform.Y = _timerInitialTranslate.Y + (cur.Y - _timerMouseStart.Y);
            e.Handled = true;
        }
    }

    private void LessonTimerWidget_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isTimerDragging)
        {
            _isTimerDragging = false;
            LessonTimerWidget.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void TimerPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string minutesStr && int.TryParse(minutesStr, out int mins))
        {
            _lessonTimer.Stop();
            _isTimerRunning = false;
            TimerStartPauseBtn.Content = "▶ Старт";
            _timerSeconds = mins * 60;
            UpdateTimerDisplay();
        }
    }

    private void TimerStartPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isTimerRunning)
        {
            _lessonTimer.Stop();
            _isTimerRunning = false;
            TimerStartPauseBtn.Content = "▶ Старт";
        }
        else
        {
            if (_timerSeconds <= 0) _timerSeconds = 300;
            _lessonTimer.Start();
            _isTimerRunning = true;
            TimerStartPauseBtn.Content = "⏸ Пауза";
        }
    }

    private void TimerReset_Click(object sender, RoutedEventArgs e)
    {
        _lessonTimer.Stop();
        _isTimerRunning = false;
        TimerStartPauseBtn.Content = "▶ Старт";
        _timerSeconds = 300; // сброс на 5 минут
        UpdateTimerDisplay();
    }

    private void LessonTimer_Tick(object? sender, EventArgs e)
    {
        if (_timerSeconds > 0)
        {
            _timerSeconds--;
            UpdateTimerDisplay();
            if (_timerSeconds == 0)
            {
                _lessonTimer.Stop();
                _isTimerRunning = false;
                TimerStartPauseBtn.Content = "▶ Старт";
                try { SystemSounds.Exclamation.Play(); } catch { }
                ShowAiStatus("⏰ Час завдання вичерпано!");
            }
        }
    }

    private void UpdateTimerDisplay()
    {
        int minutes = _timerSeconds / 60;
        int seconds = _timerSeconds % 60;
        string timeStr = $"{minutes:D2}:{seconds:D2}";
        TimerDisplayBlock.Text = timeStr;
        TimerTopLabel.Text = _isTimerRunning ? timeStr : "Таймер";
    }

    // =========================================================================
    // ШВИДКІ МАТЕМАТИЧНІ СИМВОЛИ (QUICK MATH SYMBOLS)
    // =========================================================================

    private void MathSymbolsTopBtn_Click(object sender, RoutedEventArgs e)
    {
        MathSymbolsPopup.IsOpen = !MathSymbolsPopup.IsOpen;
    }

    private void InitMathSymbols()
    {
        if (MathSymbolsGrid == null) return;
        MathSymbolsGrid.Children.Clear();

        string[] symbols = new[]
        {
            "+", "−", "×", "÷", "±", "=", "≠", "≈", "≤", "≥",
            "√", "∛", "π", "°", "²", "³", "ⁿ", "∞", "∑", "∫",
            "∆", "∠", "⊥", "∥", "∈", "∉", "⊂", "∪", "∩", "%",
            "α", "β", "γ", "λ", "φ", "ω"
        };

        foreach (var sym in symbols)
        {
            var btn = new Button
            {
                Content = sym,
                Width = 26,
                Height = 26,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = $"Вставити символ {sym}"
            };
            btn.Click += MathSymbolBtn_Click;
            MathSymbolsGrid.Children.Add(btn);
        }
    }

    private void MathSymbolBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Content is string sym)
        {
            MathSymbolsPopup.IsOpen = false;
            InsertMathSymbol(sym);
        }
    }

    private void InsertMathSymbol(string symbol)
    {
        double posX = Math.Max(60, (MainCanvas.ActualWidth - 120) / 2);
        double posY = Math.Max(120, (MainCanvas.ActualHeight - 60) / 2);
        CreateEditableTextBox(new Point(posX, posY), symbol);
    }

    // =========================================================================
    // ЕКСПОРТ АРКУША (PNG ТА БУФЕР ОБМІНУ)
    // =========================================================================

    private void ExportBoardBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int w = (int)Math.Max(800, MainCanvas.ActualWidth);
            int h = (int)Math.Max(600, MainCanvas.ActualHeight);

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(MainCanvas);

            Clipboard.SetImage(rtb);
            ShowAiStatus("Аркуш скопійовано в буфер обміну як картинку 📋");

            var dlg = new SaveFileDialog
            {
                Title = "Зберегти аркуш як зображення",
                Filter = "PNG зображення (*.png)|*.png|JPEG зображення (*.jpg)|*.jpg",
                FileName = $"{_currentBoard?.Title ?? "Аркуш"}_{DateTime.Now:yyyyMMdd_HHmm}.png"
            };

            if (dlg.ShowDialog() == true)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.OpenWrite(dlg.FileName);
                encoder.Save(fs);
                ShowAiStatus("Аркуш збережено у файл! 💾");
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowAiStatus("Помилка збереження аркуша");
        }
    }

    private void SetActiveTool(WhiteboardTool tool)
    {
        _activeTool = tool;

        if (tool != WhiteboardTool.Pencil)
        {
            _inactivityTimer.Stop();
            _currentWordStrokes.Clear();
        }

        if (tool != WhiteboardTool.Laser)
        {
            _laserFadeTimer.Stop();
            _laserTrail.Clear();
            LaserCanvas.Children.Clear();
        }

        bool isText = (tool == WhiteboardTool.Text);
        if (CursorToolsPanel != null && TextFormatPanel != null)
        {
            CursorToolsPanel.Visibility = isText ? Visibility.Collapsed : Visibility.Visible;
            TextFormatPanel.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        }

        if (PointerBtn != null) PointerBtn.IsChecked = (tool == WhiteboardTool.Pointer);
        if (LaserBtn != null) LaserBtn.IsChecked = (tool == WhiteboardTool.Laser);
        if (PencilBtn != null) PencilBtn.IsChecked = (tool == WhiteboardTool.Pencil);
        if (HighlighterBtn != null) HighlighterBtn.IsChecked = (tool == WhiteboardTool.Highlighter);
        if (EraserBtn != null) EraserBtn.IsChecked = (tool == WhiteboardTool.Eraser);
        if (TextToolBtn != null) TextToolBtn.IsChecked = isText;
        if (ShapesBtn != null) ShapesBtn.IsChecked = (tool == WhiteboardTool.Shapes);

        if (TextToggleTextMode != null) TextToggleTextMode.IsChecked = isText;
        if (PointerBtnTextMode != null) PointerBtnTextMode.IsChecked = !isText && (tool == WhiteboardTool.Pointer);
        if (LaserBtnTextMode != null) LaserBtnTextMode.IsChecked = !isText && (tool == WhiteboardTool.Laser);

        DeselectAllItems();

        switch (tool)
        {
            case WhiteboardTool.Pointer:
                MainCanvas.EditingMode = InkCanvasEditingMode.Select;
                MainCanvas.Cursor = Cursors.Arrow;
                break;

            case WhiteboardTool.Laser:
                MainCanvas.EditingMode = InkCanvasEditingMode.None;
                MainCanvas.Cursor = Cursors.None;
                InitLaserHead();
                _laserFadeTimer.Start();
                break;

            case WhiteboardTool.Pencil:
                MainCanvas.EditingMode = InkCanvasEditingMode.Ink;
                ApplyPenAttributes();
                MainCanvas.Cursor = Cursors.Pen;
                break;

            case WhiteboardTool.Highlighter:
                MainCanvas.EditingMode = InkCanvasEditingMode.Ink;
                ApplyHighlighterAttributes();
                MainCanvas.Cursor = Cursors.Pen;
                break;

            case WhiteboardTool.Eraser:
                UpdateEraserModeDisplay();
                MainCanvas.Cursor = Cursors.Hand;
                break;

            case WhiteboardTool.Text:
                MainCanvas.EditingMode = InkCanvasEditingMode.None;
                MainCanvas.Cursor = Cursors.IBeam;
                break;

            case WhiteboardTool.Shapes:
                MainCanvas.EditingMode = InkCanvasEditingMode.None;
                MainCanvas.Cursor = Cursors.Cross;
                break;
        }
        SyncActiveToolToBoardSlide();
    }

    private void ApplyPenAttributes()
    {
        MainCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = _currentPenColor,
            Width = _currentPenSize,
            Height = _currentPenSize,
            FitToCurve = true,
            IsHighlighter = false,
            StylusTip = StylusTip.Ellipse,
            IgnorePressure = false
        };
    }

    private void ApplyHighlighterAttributes()
    {
        MainCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = _currentHighlighterColor,
            Width = 12,
            Height = 24,
            FitToCurve = true,
            IsHighlighter = true,
            StylusTip = StylusTip.Rectangle,
            IgnorePressure = true
        };
    }

    private void UpdateEraserModeDisplay()
    {
        MainCanvas.EditingMode = _eraseByStroke ? InkCanvasEditingMode.EraseByStroke : InkCanvasEditingMode.EraseByPoint;
        if (EraserModeText != null)
        {
            EraserModeText.Text = _eraseByStroke ? "S" : "P";
        }
        if (EraserModeBadge != null)
        {
            EraserModeBadge.Background = _eraseByStroke ? new SolidColorBrush(Color.FromRgb(59, 130, 246)) : new SolidColorBrush(Color.FromRgb(249, 115, 22));
        }
        EraserBtn.ToolTip = _eraseByStroke ? "Гумка: стирання штриха цілком (S)" : "Гумка: стирання по пікселях (P)";
    }

    private void PencilColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                _currentPenColor = color;
                PencilColorDot.Fill = new SolidColorBrush(color);
                ApplyPenAttributes();
                PencilSettingsPopup.IsOpen = false;
            }
            catch { }
        }
    }

    private void PencilSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _currentPenSize = Math.Round(e.NewValue, 0);
        if (PencilSizeLabel != null)
        {
            PencilSizeLabel.Text = $"{_currentPenSize} px";
        }
        if (_activeTool == WhiteboardTool.Pencil)
        {
            ApplyPenAttributes();
        }
    }

    private void HighlighterColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            try
            {
                var baseColor = (Color)ColorConverter.ConvertFromString(hex);
                _currentHighlighterColor = Color.FromArgb(130, baseColor.R, baseColor.G, baseColor.B);
                ApplyHighlighterAttributes();
                HighlighterPopup.IsOpen = false;
            }
            catch { }
        }
    }

    private void ClearBoardBtn_Click(object sender, RoutedEventArgs e)
    {
        _inactivityTimer.Stop();
        _currentWordStrokes.Clear();
        _recentDrawnStrokes.Clear();
        DeselectAllItems();

        if (MainCanvas.Strokes.Count > 0 || MainCanvas.Children.Count > 0)
        {
            var oldStrokes = new StrokeCollection(MainCanvas.Strokes);
            var oldElements = MainCanvas.Children.Cast<UIElement>().ToList();

            _undoStack.Push(new ClearCanvasAction(oldStrokes, oldElements, MainCanvas));
            _redoStack.Clear();
            UpdateUndoRedoButtons();

            MainCanvas.Strokes.Clear();
            MainCanvas.Children.Clear();
            _activeRuler = null;
            _activeProtractor = null;
            _activeSetSquare = null;

            ScheduleAutoSave();
        }
    }

    // =========================================================================
    // ЛАЗЕРНА УКАЗКА (ІДЕАЛЬНО ЦЕНТРОВАНИЙ СПАЛАХ І СЛІД)
    // =========================================================================

    private void InitLaserHead()
    {
        LaserCanvas.Children.Clear();
        _laserTrail.Clear();

        _laserGlowCircle = new System.Windows.Shapes.Ellipse
        {
            Width = 28,
            Height = 28,
            Fill = new SolidColorBrush(Color.FromArgb(90, 244, 63, 94)),
            IsHitTestVisible = false
        };

        _laserHeadCircle = new System.Windows.Shapes.Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush(Color.FromRgb(244, 63, 94)),
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            IsHitTestVisible = false
        };

        LaserCanvas.Children.Add(_laserGlowCircle);
        LaserCanvas.Children.Add(_laserHeadCircle);
    }

    private void UpdateLaserPointer(Point pos)
    {
        if (_laserGlowCircle == null || _laserHeadCircle == null)
        {
            InitLaserHead();
        }

        Canvas.SetLeft(_laserGlowCircle!, pos.X - 14);
        Canvas.SetTop(_laserGlowCircle!, pos.Y - 14);

        Canvas.SetLeft(_laserHeadCircle!, pos.X - 5);
        Canvas.SetTop(_laserHeadCircle!, pos.Y - 5);

        _laserTrail.Add((pos, 1.0));
        if (_laserTrail.Count > 35)
        {
            _laserTrail.RemoveAt(0);
        }

        RenderLaserTrail();
    }

    private void RenderLaserTrail()
    {
        for (int i = LaserCanvas.Children.Count - 1; i >= 0; i--)
        {
            if (LaserCanvas.Children[i] is Line)
            {
                LaserCanvas.Children.RemoveAt(i);
            }
        }

        if (_laserTrail.Count < 2) return;

        for (int i = 0; i < _laserTrail.Count - 1; i++)
        {
            var p1 = _laserTrail[i];
            var p2 = _laserTrail[i + 1];

            byte alpha = (byte)Math.Clamp(p1.Opacity * 200, 0, 200);
            var line = new Line
            {
                X1 = p1.Pt.X,
                Y1 = p1.Pt.Y,
                X2 = p2.Pt.X,
                Y2 = p2.Pt.Y,
                Stroke = new SolidColorBrush(Color.FromArgb(alpha, 244, 63, 94)),
                StrokeThickness = 3.5 * p1.Opacity,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            };
            LaserCanvas.Children.Insert(0, line);
        }
    }

    private void LaserFadeTimer_Tick(object? sender, EventArgs e)
    {
        if (_laserTrail.Count == 0) return;

        bool changed = false;
        for (int i = _laserTrail.Count - 1; i >= 0; i--)
        {
            var item = _laserTrail[i];
            double nextOpacity = item.Opacity - 0.08;
            if (nextOpacity <= 0)
            {
                _laserTrail.RemoveAt(i);
                changed = true;
            }
            else
            {
                _laserTrail[i] = (item.Pt, nextOpacity);
                changed = true;
            }
        }

        if (changed)
        {
            RenderLaserTrail();
        }
    }

    private void TriggerLaserAttentionPing(Point center)
    {
        UpdateLaserPointer(center);

        var wave = new System.Windows.Shapes.Ellipse
        {
            Width = 60,
            Height = 60,
            Stroke = new SolidColorBrush(Color.FromRgb(244, 63, 94)),
            StrokeThickness = 2.5,
            Fill = new SolidColorBrush(Color.FromArgb(40, 244, 63, 94)),
            IsHitTestVisible = false
        };

        // Центрируем масштабирование строго относительно центра фигуры (30, 30)
        var scaleTransform = new ScaleTransform(0.15, 0.15, 30, 30);
        wave.RenderTransform = scaleTransform;

        Canvas.SetLeft(wave, center.X - 30);
        Canvas.SetTop(wave, center.Y - 30);
        LaserCanvas.Children.Add(wave);

        var scaleAnim = new DoubleAnimation(0.15, 1.35, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };
        var opacityAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(420));

        scaleAnim.Completed += (_, _) =>
        {
            if (LaserCanvas.Children.Contains(wave))
            {
                LaserCanvas.Children.Remove(wave);
            }
        };

        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        wave.BeginAnimation(OpacityProperty, opacityAnim);
    }

    // =========================================================================
    // ГЕОМЕТРИЧНІ ФІГУРИ (БЕЗ ДЕФОРМАЦІЇ СПЛАЙНАМИ)
    // =========================================================================

    private void StartShapeDrawing(Point start)
    {
        _isDrawingShape = true;
        _shapeStartPoint = start;
        ShapePreviewCanvas.Children.Clear();

        _previewShapeElement = CreatePreviewShapeElement(_activeShape);
        ShapePreviewCanvas.Children.Add(_previewShapeElement);
    }

    private System.Windows.Shapes.Shape CreatePreviewShapeElement(ShapeKind kind)
    {
        var strokeBrush = new SolidColorBrush(_currentPenColor);
        var dashArray = new DoubleCollection { 4, 3 };

        return kind switch
        {
            ShapeKind.Line => new Line
            {
                Stroke = strokeBrush,
                StrokeThickness = _currentPenSize,
                StrokeDashArray = dashArray
            },
            ShapeKind.Rectangle => new System.Windows.Shapes.Rectangle
            {
                Stroke = strokeBrush,
                StrokeThickness = _currentPenSize,
                StrokeDashArray = dashArray
            },
            ShapeKind.Ellipse => new System.Windows.Shapes.Ellipse
            {
                Stroke = strokeBrush,
                StrokeThickness = _currentPenSize,
                StrokeDashArray = dashArray
            },
            ShapeKind.Triangle => new System.Windows.Shapes.Polygon
            {
                Stroke = strokeBrush,
                StrokeThickness = _currentPenSize,
                StrokeDashArray = dashArray
            },
            _ => new System.Windows.Shapes.Path
            {
                Stroke = strokeBrush,
                StrokeThickness = _currentPenSize,
                StrokeDashArray = dashArray,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            }
        };
    }

    private void UpdateShapeDrawing(Point current)
    {
        if (_previewShapeElement == null) return;

        double left = Math.Min(_shapeStartPoint.X, current.X);
        double top = Math.Min(_shapeStartPoint.Y, current.Y);
        double width = Math.Max(2, Math.Abs(current.X - _shapeStartPoint.X));
        double height = Math.Max(2, Math.Abs(current.Y - _shapeStartPoint.Y));

        switch (_previewShapeElement)
        {
            case Line line:
                line.X1 = _shapeStartPoint.X;
                line.Y1 = _shapeStartPoint.Y;
                line.X2 = current.X;
                line.Y2 = current.Y;
                break;

            case System.Windows.Shapes.Rectangle rect:
                Canvas.SetLeft(rect, left);
                Canvas.SetTop(rect, top);
                rect.Width = width;
                rect.Height = height;
                break;

            case System.Windows.Shapes.Ellipse ellipse:
                Canvas.SetLeft(ellipse, left);
                Canvas.SetTop(ellipse, top);
                ellipse.Width = width;
                ellipse.Height = height;
                break;

            case System.Windows.Shapes.Polygon poly:
                poly.Points = new PointCollection
                {
                    new Point(left + width / 2.0, top),
                    new Point(left + width, top + height),
                    new Point(left, top + height)
                };
                break;

            case System.Windows.Shapes.Path path:
                path.Data = BuildShapeGeometry(_activeShape, _shapeStartPoint, current);
                break;
        }
    }

    private static Point[] CreateRhombusPoints(double left, double top, double width, double height) =>
        new[]
        {
            new Point(left + width / 2.0, top),
            new Point(left + width, top + height / 2.0),
            new Point(left + width / 2.0, top + height),
            new Point(left, top + height / 2.0),
            new Point(left + width / 2.0, top)
        };

    private static Point[] CreateParallelogramPoints(double left, double top, double width, double height)
    {
        double skew = width * 0.25;
        return new[]
        {
            new Point(left + skew, top),
            new Point(left + width, top),
            new Point(left + width - skew, top + height),
            new Point(left, top + height),
            new Point(left + skew, top)
        };
    }

    private static Point[] CreateTrapezoidPoints(double left, double top, double width, double height)
    {
        double inset = width * 0.22;
        return new[]
        {
            new Point(left + inset, top),
            new Point(left + width - inset, top),
            new Point(left + width, top + height),
            new Point(left, top + height),
            new Point(left + inset, top)
        };
    }

    private static Point[] CreateStarPoints(double left, double top, double width, double height)
    {
        double midX = left + width / 2.0;
        double midY = top + height / 2.0;
        double R = Math.Min(width, height) / 2.0;
        double r = R * 0.382;
        var pts = new Point[11];
        for (int i = 0; i < 10; i++)
        {
            double angle = -Math.PI / 2.0 + i * Math.PI / 5.0;
            double rad = (i % 2 == 0) ? R : r;
            pts[i] = new Point(midX + rad * Math.Cos(angle), midY + rad * Math.Sin(angle));
        }
        pts[10] = pts[0];
        return pts;
    }

    private static List<Point> GenerateCurlyBracketPoints(Point p1, Point p2)
    {
        double left = Math.Min(p1.X, p2.X);
        double right = Math.Max(p1.X, p2.X);
        double top = Math.Min(p1.Y, p2.Y);
        double bottom = Math.Max(p1.Y, p2.Y);
        double height = Math.Max(20.0, bottom - top);
        double width = Math.Max(16.0, right - left);
        double midY = (top + bottom) / 2.0;

        bool pointLeft = p1.X <= p2.X;
        double spineX = left + width * 0.5;
        double outerX = pointLeft ? right : left;
        double tipX = pointLeft ? left : right;

        var pts = new List<Point>();

        AddCubicBezier(pts,
            new Point(outerX, top),
            new Point(spineX, top),
            new Point(spineX, top + height * 0.12),
            new Point(spineX, midY - height * 0.08), 12);

        AddCubicBezier(pts,
            new Point(spineX, midY - height * 0.08),
            new Point(spineX, midY - height * 0.02),
            new Point(tipX, midY - height * 0.01),
            new Point(tipX, midY), 10);

        AddCubicBezier(pts,
            new Point(tipX, midY),
            new Point(tipX, midY + height * 0.01),
            new Point(spineX, midY + height * 0.02),
            new Point(spineX, midY + height * 0.08), 10);

        AddCubicBezier(pts,
            new Point(spineX, midY + height * 0.08),
            new Point(spineX, bottom - height * 0.12),
            new Point(spineX, bottom),
            new Point(outerX, bottom), 12);

        return pts;
    }

    private static void AddCubicBezier(List<Point> pts, Point p0, Point p1, Point p2, Point p3, int steps)
    {
        for (int i = (pts.Count == 0 ? 0 : 1); i <= steps; i++)
        {
            double t = (double)i / steps;
            double u = 1.0 - t;
            double tt = t * t;
            double uu = u * u;
            double uuu = uu * u;
            double ttt = tt * t;

            double x = uuu * p0.X + 3 * uu * t * p1.X + 3 * u * tt * p2.X + ttt * p3.X;
            double y = uuu * p0.Y + 3 * uu * t * p1.Y + 3 * u * tt * p2.Y + ttt * p3.Y;
            pts.Add(new Point(x, y));
        }
    }

    private static Geometry BuildShapeGeometry(ShapeKind kind, Point p1, Point p2)
    {
        double left = Math.Min(p1.X, p2.X);
        double top = Math.Min(p1.Y, p2.Y);
        double width = Math.Max(2, Math.Abs(p2.X - p1.X));
        double height = Math.Max(2, Math.Abs(p2.Y - p1.Y));
        double right = left + width;
        double bottom = top + height;

        var geomGroup = new GeometryGroup();

        switch (kind)
        {
            case ShapeKind.Arrow:
                geomGroup.Children.Add(new LineGeometry(p1, p2));
                double a = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
                double hLen = Math.Max(12.0, 10.0);
                Point w1 = new Point(p2.X - hLen * Math.Cos(a - Math.PI / 6.0), p2.Y - hLen * Math.Sin(a - Math.PI / 6.0));
                Point w2 = new Point(p2.X - hLen * Math.Cos(a + Math.PI / 6.0), p2.Y - hLen * Math.Sin(a + Math.PI / 6.0));
                geomGroup.Children.Add(new LineGeometry(w1, p2));
                geomGroup.Children.Add(new LineGeometry(w2, p2));
                break;

            case ShapeKind.DoubleArrow:
                geomGroup.Children.Add(new LineGeometry(p1, p2));
                double daAngle = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
                double daLen = Math.Max(12.0, 10.0);
                Point ew1 = new Point(p2.X - daLen * Math.Cos(daAngle - Math.PI / 6.0), p2.Y - daLen * Math.Sin(daAngle - Math.PI / 6.0));
                Point ew2 = new Point(p2.X - daLen * Math.Cos(daAngle + Math.PI / 6.0), p2.Y - daLen * Math.Sin(daAngle + Math.PI / 6.0));
                geomGroup.Children.Add(new LineGeometry(ew1, p2));
                geomGroup.Children.Add(new LineGeometry(ew2, p2));
                Point sw1 = new Point(p1.X + daLen * Math.Cos(daAngle - Math.PI / 6.0), p1.Y + daLen * Math.Sin(daAngle - Math.PI / 6.0));
                Point sw2 = new Point(p1.X + daLen * Math.Cos(daAngle + Math.PI / 6.0), p1.Y + daLen * Math.Sin(daAngle + Math.PI / 6.0));
                geomGroup.Children.Add(new LineGeometry(sw1, p1));
                geomGroup.Children.Add(new LineGeometry(sw2, p1));
                break;

            case ShapeKind.RightTriangle:
                var rtFig = new PathFigure { StartPoint = new Point(left, top), IsClosed = true };
                rtFig.Segments.Add(new LineSegment(new Point(left, bottom), true));
                rtFig.Segments.Add(new LineSegment(new Point(right, bottom), true));
                var rtGeom = new PathGeometry();
                rtGeom.Figures.Add(rtFig);
                geomGroup.Children.Add(rtGeom);
                double sq = Math.Min(14.0, Math.Min(width, height) * 0.25);
                geomGroup.Children.Add(new LineGeometry(new Point(left, bottom - sq), new Point(left + sq, bottom - sq)));
                geomGroup.Children.Add(new LineGeometry(new Point(left + sq, bottom - sq), new Point(left + sq, bottom)));
                break;

            case ShapeKind.Rhombus:
                var rhPoints = CreateRhombusPoints(left, top, width, height);
                var rhFig = new PathFigure { StartPoint = rhPoints[0], IsClosed = true };
                rhFig.Segments.Add(new PolyLineSegment(rhPoints.Skip(1), true));
                var rhGeom = new PathGeometry();
                rhGeom.Figures.Add(rhFig);
                geomGroup.Children.Add(rhGeom);
                break;

            case ShapeKind.Parallelogram:
                var parPoints = CreateParallelogramPoints(left, top, width, height);
                var fnParFig = new PathFigure { StartPoint = parPoints[0], IsClosed = true };
                fnParFig.Segments.Add(new PolyLineSegment(parPoints.Skip(1), true));
                var fnParGeom = new PathGeometry();
                fnParGeom.Figures.Add(fnParFig);
                geomGroup.Children.Add(fnParGeom);
                break;

            case ShapeKind.Trapezoid:
                var trapPoints = CreateTrapezoidPoints(left, top, width, height);
                var trapFig = new PathFigure { StartPoint = trapPoints[0], IsClosed = true };
                trapFig.Segments.Add(new PolyLineSegment(trapPoints.Skip(1), true));
                var trapGeom = new PathGeometry();
                trapGeom.Figures.Add(trapFig);
                geomGroup.Children.Add(trapGeom);
                break;

            case ShapeKind.Star:
                var starPoints = CreateStarPoints(left, top, width, height);
                var starFig = new PathFigure { StartPoint = starPoints[0], IsClosed = true };
                starFig.Segments.Add(new PolyLineSegment(starPoints.Skip(1), true));
                var starGeom = new PathGeometry();
                starGeom.Figures.Add(starFig);
                geomGroup.Children.Add(starGeom);
                break;

            case ShapeKind.Cube:
                double dx = width * 0.28;
                double dy = height * 0.28;
                geomGroup.Children.Add(new RectangleGeometry(new Rect(left, top + dy, width - dx, height - dy)));
                geomGroup.Children.Add(new LineGeometry(new Point(left, top + dy), new Point(left + dx, top)));
                geomGroup.Children.Add(new LineGeometry(new Point(right - dx, top + dy), new Point(right, top)));
                geomGroup.Children.Add(new LineGeometry(new Point(right - dx, bottom), new Point(right, bottom - dy)));
                geomGroup.Children.Add(new LineGeometry(new Point(left + dx, top), new Point(right, top)));
                geomGroup.Children.Add(new LineGeometry(new Point(right, top), new Point(right, bottom - dy)));
                break;

            case ShapeKind.Cylinder:
                double midX = left + width / 2.0;
                double ry = Math.Min(height * 0.16, 26.0);
                double rx = width / 2.0;
                geomGroup.Children.Add(new EllipseGeometry(new Point(midX, top + ry), rx, ry));
                geomGroup.Children.Add(new LineGeometry(new Point(left, top + ry), new Point(left, bottom - ry)));
                geomGroup.Children.Add(new LineGeometry(new Point(right, top + ry), new Point(right, bottom - ry)));
                var cylArcFig = new PathFigure { StartPoint = new Point(left, bottom - ry), IsClosed = false };
                cylArcFig.Segments.Add(new ArcSegment(new Point(right, bottom - ry), new System.Windows.Size(rx, ry), 0, false, SweepDirection.Counterclockwise, true));
                var cylGeom = new PathGeometry();
                cylGeom.Figures.Add(cylArcFig);
                geomGroup.Children.Add(cylGeom);
                break;

            case ShapeKind.CoordinateAxes:
                double axMidX = left + width / 2.0;
                double axMidY = top + height / 2.0;
                geomGroup.Children.Add(new LineGeometry(new Point(left, axMidY), new Point(right, axMidY)));
                geomGroup.Children.Add(new LineGeometry(new Point(right - 10, axMidY - 5), new Point(right, axMidY)));
                geomGroup.Children.Add(new LineGeometry(new Point(right - 10, axMidY + 5), new Point(right, axMidY)));
                geomGroup.Children.Add(new LineGeometry(new Point(axMidX, bottom), new Point(axMidX, top)));
                geomGroup.Children.Add(new LineGeometry(new Point(axMidX - 5, top + 10), new Point(axMidX, top)));
                geomGroup.Children.Add(new LineGeometry(new Point(axMidX + 5, top + 10), new Point(axMidX, top)));
                break;

            
            case ShapeKind.Point:
                geomGroup.Children.Add(new EllipseGeometry(p2, 4, 4));
                break;

            case ShapeKind.Ray:
                geomGroup.Children.Add(new LineGeometry(p1, p2));
                double rayA = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
                double rayLen = 12.0;
                geomGroup.Children.Add(new LineGeometry(new Point(p2.X - rayLen * Math.Cos(rayA - Math.PI / 6.0), p2.Y - rayLen * Math.Sin(rayA - Math.PI / 6.0)), p2));
                geomGroup.Children.Add(new LineGeometry(new Point(p2.X - rayLen * Math.Cos(rayA + Math.PI / 6.0), p2.Y - rayLen * Math.Sin(rayA + Math.PI / 6.0)), p2));
                break;

            case ShapeKind.InfiniteLine:
                Vector infDir = p2 - p1;
                if (infDir.LengthSquared < 1) infDir = new Vector(1, 0);
                infDir.Normalize();
                geomGroup.Children.Add(new LineGeometry(p1 - infDir * 2000, p1 + infDir * 2000));
                break;

            case ShapeKind.Arc:
                var arcFig = new PathFigure { StartPoint = p1, IsClosed = false };
                arcFig.Segments.Add(new ArcSegment(p2, new System.Windows.Size(width, height), 0, false, SweepDirection.Clockwise, true));
                var arcGeom = new PathGeometry();
                arcGeom.Figures.Add(arcFig);
                geomGroup.Children.Add(arcGeom);
                break;

            case ShapeKind.Angle:
                geomGroup.Children.Add(new LineGeometry(p1, new Point(p1.X + width, p1.Y)));
                geomGroup.Children.Add(new LineGeometry(p1, new Point(p1.X + width * 0.7, p1.Y - height)));
                var angleFig = new PathFigure { StartPoint = new Point(p1.X + Math.Min(width * 0.4, 25), p1.Y), IsClosed = false };
                angleFig.Segments.Add(new ArcSegment(new Point(p1.X + Math.Min(width * 0.3, 20), p1.Y - Math.Min(height * 0.3, 16)), new System.Windows.Size(20, 20), 0, false, SweepDirection.Counterclockwise, true));
                var angleGeom = new PathGeometry();
                angleGeom.Figures.Add(angleFig);
                geomGroup.Children.Add(angleGeom);
                break;

            case ShapeKind.ComparisonBrace:
                var compFig = new PathFigure { StartPoint = p1, IsClosed = false };
                Point cp = new Point(Math.Max(p1.X, p2.X) + 20, (p1.Y + p2.Y) / 2.0);
                compFig.Segments.Add(new QuadraticBezierSegment(cp, p2, true));
                var compGeom = new PathGeometry();
                compGeom.Figures.Add(compFig);
                geomGroup.Children.Add(compGeom);
                break;

            case ShapeKind.SquareBracket:
                geomGroup.Children.Add(new LineGeometry(new Point(p1.X, p1.Y), new Point(p2.X, p1.Y)));
                geomGroup.Children.Add(new LineGeometry(new Point(p2.X, p1.Y), new Point(p2.X, p2.Y)));
                geomGroup.Children.Add(new LineGeometry(new Point(p2.X, p2.Y), new Point(p1.X, p2.Y)));
                break;

            case ShapeKind.NumberLine:
                geomGroup.Children.Add(new LineGeometry(new Point(left, top + height / 2.0), new Point(right, top + height / 2.0)));
                geomGroup.Children.Add(new LineGeometry(new Point(right - 10, top + height / 2.0 - 5), new Point(right, top + height / 2.0)));
                geomGroup.Children.Add(new LineGeometry(new Point(right - 10, top + height / 2.0 + 5), new Point(right, top + height / 2.0)));
                double yMid = top + height / 2.0;
                for (double x = left + 20; x < right - 20; x += 30)
                {
                    geomGroup.Children.Add(new LineGeometry(new Point(x, yMid - 6), new Point(x, yMid + 6)));
                }
                break;

            case ShapeKind.FunctionGraph:
                geomGroup.Children.Add(new LineGeometry(new Point(left, top + height * 0.8), new Point(right, top + height * 0.8)));
                geomGroup.Children.Add(new LineGeometry(new Point(left + width / 2.0, bottom), new Point(left + width / 2.0, top)));
                var curveFig = new PathFigure { StartPoint = new Point(left + width * 0.1, top + height * 0.2), IsClosed = false };
                curveFig.Segments.Add(new QuadraticBezierSegment(new Point(left + width / 2.0, top + height * 0.8), new Point(right - width * 0.1, top + height * 0.2), true));
                var curveGeom = new PathGeometry();
                curveGeom.Figures.Add(curveFig);
                geomGroup.Children.Add(curveGeom);
                break;

            case ShapeKind.Perpendicular:
                geomGroup.Children.Add(new LineGeometry(new Point(left, bottom), new Point(right, bottom)));
                double perpMidX = (left + right) / 2.0;
                geomGroup.Children.Add(new LineGeometry(new Point(perpMidX, bottom), new Point(perpMidX, top)));
                geomGroup.Children.Add(new LineGeometry(new Point(perpMidX, bottom - 12), new Point(perpMidX + 12, bottom - 12)));
                geomGroup.Children.Add(new LineGeometry(new Point(perpMidX + 12, bottom - 12), new Point(perpMidX + 12, bottom)));
                break;

            case ShapeKind.Bisector:
                geomGroup.Children.Add(new LineGeometry(new Point(left, bottom), new Point(right, bottom)));
                geomGroup.Children.Add(new LineGeometry(new Point(left, bottom), new Point(left + width * 0.7, top)));
                geomGroup.Children.Add(new LineGeometry(new Point(left, bottom), new Point(right, top + height * 0.35)));
                break;

            case ShapeKind.Altitude:
                geomGroup.Children.Add(new LineGeometry(new Point(left + width * 0.5, top), new Point(left, bottom)));
                geomGroup.Children.Add(new LineGeometry(new Point(left + width * 0.5, top), new Point(right, bottom)));
                geomGroup.Children.Add(new LineGeometry(new Point(left, bottom), new Point(right, bottom)));
                double altMidX = left + width * 0.5;
                geomGroup.Children.Add(new LineGeometry(new Point(altMidX, top), new Point(altMidX, bottom)));
                geomGroup.Children.Add(new LineGeometry(new Point(altMidX, bottom - 10), new Point(altMidX + 10, bottom - 10)));
                geomGroup.Children.Add(new LineGeometry(new Point(altMidX + 10, bottom - 10), new Point(altMidX + 10, bottom)));
                break;

            case ShapeKind.Tangent:
                double tangR = Math.Min(width, height) * 0.35;
                Point tangCenter = new Point(left + width * 0.5, top + height * 0.5);
                geomGroup.Children.Add(new EllipseGeometry(tangCenter, tangR, tangR));
                geomGroup.Children.Add(new LineGeometry(new Point(left, tangCenter.Y - tangR), new Point(right, tangCenter.Y - tangR)));
                geomGroup.Children.Add(new EllipseGeometry(new Point(tangCenter.X, tangCenter.Y - tangR), 3, 3));
                break;

            case ShapeKind.SquarePyramid:
                geomGroup.Children.Add(new LineGeometry(new Point(left + width * 0.2, bottom - height * 0.2), new Point(right - width * 0.1, bottom - height * 0.2)));
                geomGroup.Children.Add(new LineGeometry(new Point(right - width * 0.1, bottom - height * 0.2), new Point(right - width * 0.3, bottom)));
                geomGroup.Children.Add(new LineGeometry(new Point(right - width * 0.3, bottom), new Point(left, bottom)));
                geomGroup.Children.Add(new LineGeometry(new Point(left, bottom), new Point(left + width * 0.2, bottom - height * 0.2)));
                Point pyrApex = new Point(left + width * 0.45, top);
                geomGroup.Children.Add(new LineGeometry(pyrApex, new Point(left, bottom)));
                geomGroup.Children.Add(new LineGeometry(pyrApex, new Point(right - width * 0.3, bottom)));
                geomGroup.Children.Add(new LineGeometry(pyrApex, new Point(right - width * 0.1, bottom - height * 0.2)));
                geomGroup.Children.Add(new LineGeometry(pyrApex, new Point(left + width * 0.2, bottom - height * 0.2)));
                break;

            case ShapeKind.Sphere:
                geomGroup.Children.Add(new EllipseGeometry(new Point(left + width / 2.0, top + height / 2.0), width / 2.0, height / 2.0));
                geomGroup.Children.Add(new EllipseGeometry(new Point(left + width / 2.0, top + height / 2.0), width / 2.0, height * 0.2));
                break;

            case ShapeKind.Polygon:
                var polyPoints = new List<Point>();
                for (int i = 0; i < 6; i++)
                {
                    double ang = i * Math.PI / 3.0;
                    polyPoints.Add(new Point(left + width / 2.0 + (width / 2.0) * Math.Cos(ang), top + height / 2.0 + (height / 2.0) * Math.Sin(ang)));
                }
                var polyFig = new PathFigure { StartPoint = polyPoints[0], IsClosed = true };
                polyFig.Segments.Add(new PolyLineSegment(polyPoints.Skip(1), true));
                var polyGeom = new PathGeometry();
                polyGeom.Figures.Add(polyFig);
                geomGroup.Children.Add(polyGeom);
                break;

            case ShapeKind.CurlyBracket:
                var cbPoints = GenerateCurlyBracketPoints(p1, p2);
                if (cbPoints.Count > 1)
                {
                    var cbFig = new PathFigure { StartPoint = cbPoints[0], IsClosed = false };
                    cbFig.Segments.Add(new PolyLineSegment(cbPoints.Skip(1), true));
                    var cbGeom = new PathGeometry();
                    cbGeom.Figures.Add(cbFig);
                    geomGroup.Children.Add(cbGeom);
                }
                break;
        }

        return geomGroup;
    }

        private List<UIElement> AddShapeVertexLabels(ShapeKind kind, double left, double top, double width, double height, Guid shapeId)
    {
        var labels = new List<(Point pt, string label)>();
        double right = left + width;
        double bottom = top + height;

        switch (kind)
        {
            case ShapeKind.Point:
                labels.Add((new Point(left + 8, top - 18), "A"));
                break;
            case ShapeKind.Line:
            case ShapeKind.Ray:
            case ShapeKind.InfiniteLine:
                labels.Add((new Point(left - 18, top - 14), "A"));
                labels.Add((new Point(right + 6, bottom - 10), "B"));
                break;
            case ShapeKind.Angle:
                labels.Add((new Point(right - 6, top - 16), "A"));
                labels.Add((new Point(left - 18, bottom - 12), "B"));
                labels.Add((new Point(right + 6, bottom - 12), "C"));
                break;
            case ShapeKind.Triangle:
                labels.Add((new Point(left + width * 0.5 - 5, top - 22), "A"));
                labels.Add((new Point(left - 18, bottom + 2), "B"));
                labels.Add((new Point(right + 6, bottom + 2), "C"));
                break;
            case ShapeKind.RightTriangle:
                labels.Add((new Point(left - 18, top - 18), "A"));
                labels.Add((new Point(left - 18, bottom + 2), "B"));
                labels.Add((new Point(right + 6, bottom + 2), "C"));
                break;
            case ShapeKind.Rectangle:
            case ShapeKind.Square:
                labels.Add((new Point(left - 18, top - 18), "A"));
                labels.Add((new Point(right + 6, top - 18), "B"));
                labels.Add((new Point(right + 6, bottom + 2), "C"));
                labels.Add((new Point(left - 18, bottom + 2), "D"));
                break;
            case ShapeKind.Rhombus:
                labels.Add((new Point(left + width * 0.5 - 5, top - 22), "A"));
                labels.Add((new Point(right + 6, top + height * 0.5 - 10), "B"));
                labels.Add((new Point(left + width * 0.5 - 5, bottom + 4), "C"));
                labels.Add((new Point(left - 18, top + height * 0.5 - 10), "D"));
                break;
            case ShapeKind.Parallelogram:
                {
                    double pOffset = width * 0.25;
                    labels.Add((new Point(left - 18, bottom + 2), "A"));
                    labels.Add((new Point(right - pOffset + 6, bottom + 2), "B"));
                    labels.Add((new Point(right + 6, top - 20), "C"));
                    labels.Add((new Point(left + pOffset - 18, top - 20), "D"));
                }
                break;
            case ShapeKind.Trapezoid:
                {
                    double trOffset = width * 0.25;
                    labels.Add((new Point(left - 18, bottom + 2), "A"));
                    labels.Add((new Point(right + 6, bottom + 2), "B"));
                    labels.Add((new Point(right - trOffset + 6, top - 20), "C"));
                    labels.Add((new Point(left + trOffset - 18, top - 20), "D"));
                }
                break;
            case ShapeKind.Perpendicular:
                labels.Add((new Point(left - 18, bottom + 2), "A"));
                labels.Add((new Point(left + width * 0.5 - 5, top - 22), "B"));
                labels.Add((new Point(right + 6, bottom + 2), "C"));
                labels.Add((new Point(left + width * 0.5 + 4, bottom + 4), "H"));
                break;
            case ShapeKind.Altitude:
                labels.Add((new Point(left + width * 0.5 - 5, top - 22), "A"));
                labels.Add((new Point(left - 18, bottom + 2), "B"));
                labels.Add((new Point(right + 6, bottom + 2), "C"));
                labels.Add((new Point(left + width * 0.5 + 4, bottom + 4), "H"));
                break;
            case ShapeKind.Cube:
                {
                    double cdx = width * 0.28;
                    double cdy = height * 0.28;
                    // Нижня основа ABCD прямокутного паралелепіпеда
                    labels.Add((new Point(left - 18, bottom + 2), "A"));
                    labels.Add((new Point(right - cdx + 6, bottom + 2), "B"));
                    labels.Add((new Point(right + 8, bottom - cdy - 4), "C"));
                    labels.Add((new Point(left + cdx - 18, bottom - cdy - 4), "D"));
                    // Верхня основа A₁B₁C₁D₁ (відповідно прямо над кожною нижньою вершиною)
                    labels.Add((new Point(left - 22, top + cdy - 12), "A₁"));
                    labels.Add((new Point(right - cdx + 6, top + cdy - 12), "B₁"));
                    labels.Add((new Point(right + 8, top - 18), "C₁"));
                    labels.Add((new Point(left + cdx - 18, top - 18), "D₁"));
                }
                break;
            case ShapeKind.SquarePyramid:
                {
                    double pyrDx = width * 0.25;
                    double pyrDy = height * 0.25;
                    labels.Add((new Point(left + width * 0.5 - 5, top - 22), "S"));
                    labels.Add((new Point(left - 18, bottom + 2), "A"));
                    labels.Add((new Point(right - pyrDx + 6, bottom + 2), "B"));
                    labels.Add((new Point(right + 6, bottom - pyrDy - 8), "C"));
                    labels.Add((new Point(left + pyrDx - 18, bottom - pyrDy - 8), "D"));
                    labels.Add((new Point(left + width * 0.5 + 4, bottom - pyrDy * 0.5), "O"));
                }
                break;
            case ShapeKind.Cylinder:
                {
                    double cylRy = Math.Min(height * 0.16, 26.0);
                    labels.Add((new Point(left + width * 0.5 - 6, top - 20), "O₁"));
                    labels.Add((new Point(left + width * 0.5 - 6, bottom + 4), "O"));
                    labels.Add((new Point(right + 6, top + cylRy - 10), "A"));
                    labels.Add((new Point(right + 6, bottom - cylRy - 10), "B"));
                }
                break;
            case ShapeKind.Sphere:
                labels.Add((new Point(left + width * 0.5 - 6, top + height * 0.5 - 10), "O"));
                labels.Add((new Point(right + 6, top + height * 0.5 - 10), "A"));
                break;
        }

        var resultElements = new List<UIElement>();
        foreach (var (pt, name) in labels)
        {
            var tb = new TextBlock
            {
                Text = name,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Cambria Math, Segoe UI, Arial"),
                Foreground = new SolidColorBrush(_currentPenColor),
                Tag = new WhiteboardVertexTag(shapeId, name),
                IsHitTestVisible = true
            };
            InkCanvas.SetLeft(tb, Math.Max(0, pt.X));
            InkCanvas.SetTop(tb, Math.Max(0, pt.Y));
            MainCanvas.Children.Add(tb);
            resultElements.Add(tb);
        }
        return resultElements;
    }

    private void CommitShapeDrawing(Point end)
    {
        _isDrawingShape = false;
        ShapePreviewCanvas.Children.Clear();
        _previewShapeElement = null;

        if (ShapeSnappingCheck?.IsChecked == true)
        {
            double snap = LineHeight;
            _shapeStartPoint.X = Math.Round(_shapeStartPoint.X / 25.0) * 25.0;
            _shapeStartPoint.Y = Math.Round(_shapeStartPoint.Y / snap) * snap;
            end.X = Math.Round(end.X / 25.0) * 25.0;
            end.Y = Math.Round(end.Y / snap) * snap;
        }

        // ПРЯМЫЕ ФИГУРЫ: FitToCurve = false, чтобы углы не деформировались!
        var straightDa = new DrawingAttributes
        {
            Color = _currentPenColor,
            Width = _currentPenSize,
            Height = _currentPenSize,
            FitToCurve = false,
            StylusTip = StylusTip.Ellipse
        };

        var committedStrokes = new StrokeCollection();

        double left = Math.Min(_shapeStartPoint.X, end.X);
        double top = Math.Min(_shapeStartPoint.Y, end.Y);
        double right = Math.Max(_shapeStartPoint.X, end.X);
        double bottom = Math.Max(_shapeStartPoint.Y, end.Y);
        double width = Math.Max(2, right - left);
        double height = Math.Max(2, bottom - top);

        switch (_activeShape)
        {
            case ShapeKind.Line:
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(_shapeStartPoint.X, _shapeStartPoint.Y), new StylusPoint(end.X, end.Y) }), straightDa));
                break;

            case ShapeKind.Arrow:
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(_shapeStartPoint.X, _shapeStartPoint.Y), new StylusPoint(end.X, end.Y) }), straightDa));
                double a = Math.Atan2(end.Y - _shapeStartPoint.Y, end.X - _shapeStartPoint.X);
                double hLen = Math.Max(12.0, _currentPenSize * 4.0);
                Point w1 = new Point(end.X - hLen * Math.Cos(a - Math.PI / 6.0), end.Y - hLen * Math.Sin(a - Math.PI / 6.0));
                Point w2 = new Point(end.X - hLen * Math.Cos(a + Math.PI / 6.0), end.Y - hLen * Math.Sin(a + Math.PI / 6.0));
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(w1.X, w1.Y), new StylusPoint(end.X, end.Y), new StylusPoint(w2.X, w2.Y) }), straightDa));
                break;

            case ShapeKind.DoubleArrow:
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(_shapeStartPoint.X, _shapeStartPoint.Y), new StylusPoint(end.X, end.Y) }), straightDa));
                double daAngle = Math.Atan2(end.Y - _shapeStartPoint.Y, end.X - _shapeStartPoint.X);
                double daLen = Math.Max(12.0, _currentPenSize * 4.0);
                Point endW1 = new Point(end.X - daLen * Math.Cos(daAngle - Math.PI / 6.0), end.Y - daLen * Math.Sin(daAngle - Math.PI / 6.0));
                Point endW2 = new Point(end.X - daLen * Math.Cos(daAngle + Math.PI / 6.0), end.Y - daLen * Math.Sin(daAngle + Math.PI / 6.0));
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(endW1.X, endW1.Y), new StylusPoint(end.X, end.Y), new StylusPoint(endW2.X, endW2.Y) }), straightDa));
                Point startW1 = new Point(_shapeStartPoint.X + daLen * Math.Cos(daAngle - Math.PI / 6.0), _shapeStartPoint.Y + daLen * Math.Sin(daAngle - Math.PI / 6.0));
                Point startW2 = new Point(_shapeStartPoint.X + daLen * Math.Cos(daAngle + Math.PI / 6.0), _shapeStartPoint.Y + daLen * Math.Sin(daAngle + Math.PI / 6.0));
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(startW1.X, startW1.Y), new StylusPoint(_shapeStartPoint.X, _shapeStartPoint.Y), new StylusPoint(startW2.X, startW2.Y) }), straightDa));
                break;

            case ShapeKind.Rectangle:
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[]
                {
                    new StylusPoint(left, top),
                    new StylusPoint(right, top),
                    new StylusPoint(right, bottom),
                    new StylusPoint(left, bottom),
                    new StylusPoint(left, top)
                }), straightDa));
                break;

            case ShapeKind.Ellipse:
                double cx = (left + right) / 2.0;
                double cy = (top + bottom) / 2.0;
                double rx = width / 2.0;
                double ry = height / 2.0;
                var ellipsePts = new List<StylusPoint>();
                for (int i = 0; i <= 48; i++)
                {
                    double t = i * 2.0 * Math.PI / 48.0;
                    ellipsePts.Add(new StylusPoint(cx + rx * Math.Cos(t), cy + ry * Math.Sin(t)));
                }
                committedStrokes.Add(new Stroke(new StylusPointCollection(ellipsePts), straightDa));
                break;

            case ShapeKind.Triangle:
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[]
                {
                    new StylusPoint(left + width / 2.0, top),
                    new StylusPoint(right, bottom),
                    new StylusPoint(left, bottom),
                    new StylusPoint(left + width / 2.0, top)
                }), straightDa));
                break;

            case ShapeKind.RightTriangle:
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[]
                {
                    new StylusPoint(left, top),
                    new StylusPoint(left, bottom),
                    new StylusPoint(right, bottom),
                    new StylusPoint(left, top)
                }), straightDa));
                double sqMark = Math.Min(14.0, Math.Min(width, height) * 0.25);
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[]
                {
                    new StylusPoint(left, bottom - sqMark),
                    new StylusPoint(left + sqMark, bottom - sqMark),
                    new StylusPoint(left + sqMark, bottom)
                }), straightDa));
                break;

            case ShapeKind.Rhombus:
                var rhPts = CreateRhombusPoints(left, top, width, height);
                committedStrokes.Add(new Stroke(new StylusPointCollection(rhPts.Select(p => new StylusPoint(p.X, p.Y))), straightDa));
                break;

            case ShapeKind.Parallelogram:
                var parPts = CreateParallelogramPoints(left, top, width, height);
                committedStrokes.Add(new Stroke(new StylusPointCollection(parPts.Select(p => new StylusPoint(p.X, p.Y))), straightDa));
                break;

            case ShapeKind.Trapezoid:
                var trPts = CreateTrapezoidPoints(left, top, width, height);
                committedStrokes.Add(new Stroke(new StylusPointCollection(trPts.Select(p => new StylusPoint(p.X, p.Y))), straightDa));
                break;

            case ShapeKind.Star:
                var stPts = CreateStarPoints(left, top, width, height);
                committedStrokes.Add(new Stroke(new StylusPointCollection(stPts.Select(p => new StylusPoint(p.X, p.Y))), straightDa));
                break;

            case ShapeKind.Cube:
                double cdx = width * 0.28;
                double cdy = height * 0.28;
                // Front rectangle
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[]
                {
                    new StylusPoint(left, top + cdy),
                    new StylusPoint(right - cdx, top + cdy),
                    new StylusPoint(right - cdx, bottom),
                    new StylusPoint(left, bottom),
                    new StylusPoint(left, top + cdy)
                }), straightDa));
                // 3 visible connecting edges
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(left, top + cdy), new StylusPoint(left + cdx, top) }), straightDa));
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(right - cdx, top + cdy), new StylusPoint(right, top) }), straightDa));
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(right - cdx, bottom), new StylusPoint(right, bottom - cdy) }), straightDa));
                // Back edges
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(left + cdx, top), new StylusPoint(right, top) }), straightDa));
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(right, top), new StylusPoint(right, bottom - cdy) }), straightDa));
                // 3 hidden dashed edges for school stereometry: A->D, D->C, D->D1
                committedStrokes.Add(new WhiteboardDashedStroke(new StylusPointCollection(new[] { new StylusPoint(left, bottom), new StylusPoint(left + cdx, bottom - cdy) }), straightDa, WhiteboardStrokePattern.Dashed));
                committedStrokes.Add(new WhiteboardDashedStroke(new StylusPointCollection(new[] { new StylusPoint(left + cdx, bottom - cdy), new StylusPoint(right, bottom - cdy) }), straightDa, WhiteboardStrokePattern.Dashed));
                committedStrokes.Add(new WhiteboardDashedStroke(new StylusPointCollection(new[] { new StylusPoint(left + cdx, bottom - cdy), new StylusPoint(left + cdx, top) }), straightDa, WhiteboardStrokePattern.Dashed));
                break;

            case ShapeKind.Cylinder:
                double cylMidX = (left + right) / 2.0;
                double cylRy = Math.Min(height * 0.16, 26.0);
                double cylRx = width / 2.0;
                // Top ellipse
                var cylTopPts = new List<StylusPoint>();
                for (int i = 0; i <= 36; i++)
                {
                    double t = i * 2.0 * Math.PI / 36.0;
                    cylTopPts.Add(new StylusPoint(cylMidX + cylRx * Math.Cos(t), top + cylRy + cylRy * Math.Sin(t)));
                }
                committedStrokes.Add(new Stroke(new StylusPointCollection(cylTopPts), straightDa));
                // Vertical sides
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(left, top + cylRy), new StylusPoint(left, bottom - cylRy) }), straightDa));
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(right, top + cylRy), new StylusPoint(right, bottom - cylRy) }), straightDa));
                // Bottom front arc (0 to PI)
                var cylBotPts = new List<StylusPoint>();
                for (int i = 0; i <= 24; i++)
                {
                    double t = i * Math.PI / 24.0;
                    cylBotPts.Add(new StylusPoint(cylMidX - cylRx * Math.Cos(t), bottom - cylRy + cylRy * Math.Sin(t)));
                }
                committedStrokes.Add(new Stroke(new StylusPointCollection(cylBotPts), straightDa));
                break;

            case ShapeKind.CoordinateAxes:
                double axMidX = (left + right) / 2.0;
                double axMidY = (top + bottom) / 2.0;
                // Horizontal X axis
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(left, axMidY), new StylusPoint(right, axMidY) }), straightDa));
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(right - 10, axMidY - 5), new StylusPoint(right, axMidY), new StylusPoint(right - 10, axMidY + 5) }), straightDa));
                // Vertical Y axis
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(axMidX, bottom), new StylusPoint(axMidX, top) }), straightDa));
                committedStrokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(axMidX - 5, top + 10), new StylusPoint(axMidX, top), new StylusPoint(axMidX + 5, top + 10) }), straightDa));
                break;

            
            case ShapeKind.CurlyBracket:
                var cbPts = GenerateCurlyBracketPoints(_shapeStartPoint, end);
                if (cbPts.Count > 1)
                {
                    committedStrokes.Add(new Stroke(new StylusPointCollection(cbPts.Select(p => new StylusPoint(p.X, p.Y))), straightDa));
                }
                break;
            default:
                var shapeGeom = BuildShapeGeometry(_activeShape, _shapeStartPoint, end);
                if (shapeGeom != null)
                {
                    var flat = shapeGeom.GetFlattenedPathGeometry();
                    foreach (PathFigure fig in flat.Figures)
                    {
                        var pts = new List<StylusPoint> { new StylusPoint(fig.StartPoint.X, fig.StartPoint.Y) };
                        foreach (PathSegment seg in fig.Segments)
                        {
                            if (seg is PolyLineSegment pls)
                            {
                                pts.AddRange(pls.Points.Select(p => new StylusPoint(p.X, p.Y)));
                            }
                            else if (seg is LineSegment ls)
                            {
                                pts.Add(new StylusPoint(ls.Point.X, ls.Point.Y));
                            }
                        }
                        if (fig.IsClosed && pts.Count > 0)
                        {
                            pts.Add(pts[0]);
                        }
                        if (pts.Count > 1)
                        {
                            committedStrokes.Add(new Stroke(new StylusPointCollection(pts), straightDa));
                        }
                    }
                }
                break;
        }

        if (ShapeDashedCheck?.IsChecked == true)
        {
            for (int sIdx = 0; sIdx < committedStrokes.Count; sIdx++)
            {
                var orig = committedStrokes[sIdx];
                committedStrokes[sIdx] = new WhiteboardDashedStroke(orig.StylusPoints.Clone(), orig.DrawingAttributes.Clone(), WhiteboardStrokePattern.Dashed);
            }
        }

        Guid shapeId = Guid.NewGuid();
        foreach (var st in committedStrokes)
        {
            RegisterStrokeShapeId(st, shapeId);
        }

        List<UIElement>? createdLabels = null;
        if (ShapeLabelsCheck?.IsChecked == true)
        {
            createdLabels = AddShapeVertexLabels(_activeShape, left, top, width, height, shapeId);
        }

        if (committedStrokes.Count > 0 || (createdLabels != null && createdLabels.Count > 0))
        {
            foreach (var st in committedStrokes)
            {
                try
                {
                    if (!MainCanvas.Strokes.Contains(st)) MainCanvas.Strokes.Add(st);
                }
                catch { }
                try
                {
                    if (!_recentDrawnStrokes.Contains(st)) _recentDrawnStrokes.Add(st);
                }
                catch { }
            }

            var actionList = new List<IWhiteboardAction>();
            if (committedStrokes.Count > 0)
            {
                actionList.Add(new StrokesAddedAction(committedStrokes, MainCanvas));
            }
            if (createdLabels != null && createdLabels.Count > 0)
            {
                foreach (var lbl in createdLabels)
                {
                    actionList.Add(new ElementAddedAction(lbl, MainCanvas));
                }
            }

            if (actionList.Count == 1)
            {
                _undoStack.Push(actionList[0]);
            }
            else if (actionList.Count > 1)
            {
                _undoStack.Push(new CompositeWhiteboardAction(actionList));
            }
            _redoStack.Clear();
            UpdateUndoRedoButtons();
            ScheduleAutoSave();
        }

        if (ShapePinToolCheck?.IsChecked == true)
        {
            ShowAiStatus("Фігуру закріплено на полотні");
        }
    }

    // =========================================================================
    // СТІКЕРИ
    // =========================================================================

    private void AddStickerColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            StickerColorPopup.IsOpen = false;

            double posX = Math.Max(40, (MainCanvas.ActualWidth - 125) / 2);
            double posY = Math.Max(40, (MainCanvas.ActualHeight - 100) / 2);

            var sticker = new WhiteboardStickerItem(Guid.NewGuid().ToString(), hex, "", 125, double.NaN);
            AttachStickerItemEvents(sticker);

            InkCanvas.SetLeft(sticker, posX);
            InkCanvas.SetTop(sticker, posY);
            MainCanvas.Children.Add(sticker);

            _undoStack.Push(new ElementAddedAction(sticker, MainCanvas));
            _redoStack.Clear();
            UpdateUndoRedoButtons();

            SelectStickerItem(sticker);
            ScheduleAutoSave();
        }
    }

    private void AttachStickerItemEvents(WhiteboardStickerItem item)
    {
        item.DeleteRequested += (sender) =>
        {
            if (MainCanvas.Children.Contains(sender))
            {
                MainCanvas.Children.Remove(sender);
                _undoStack.Push(new ElementRemovedAction(sender, MainCanvas));
                _redoStack.Clear();
                UpdateUndoRedoButtons();
                if (_selectedStickerItem == sender) _selectedStickerItem = null;
                ScheduleAutoSave();
            }
        };

        item.Selected += (sender) => SelectStickerItem(sender);
        item.Changed += ScheduleAutoSave;
    }

    private void SelectStickerItem(WhiteboardStickerItem item)
    {
        DeselectAllItems();
        _selectedStickerItem = item;
        _selectedStickerItem.SetSelected(true);
    }

    // =========================================================================
    // ЗОБРАЖЕННЯ
    // =========================================================================

    private void InsertImageBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "Виберіть зображення для вставки на аркуш",
                Filter = "Зображення (*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|Всі файли (*.*)|*.*",
                Multiselect = true
            };

            if (dlg.ShowDialog() == true)
            {
                double offsetX = 0;
                double offsetY = 0;
                foreach (var fileName in dlg.FileNames)
                {
                    InsertImageFromFile(fileName, offsetX, offsetY);
                    offsetX += 30;
                    offsetY += 30;
                }
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private void InsertImageFromFile(string filePath, double offsetX = 0, double offsetY = 0)
    {
        try
        {
            byte[] fileBytes = File.ReadAllBytes(filePath);
            string base64 = Convert.ToBase64String(fileBytes);
            var bitmap = Base64ToBitmapImage(base64);

            double w = bitmap.PixelWidth;
            double h = bitmap.PixelHeight;

            const double maxInitialSize = 360.0;
            if (w > maxInitialSize || h > maxInitialSize)
            {
                double ratio = w / h;
                if (w >= h) { w = maxInitialSize; h = w / ratio; }
                else { h = maxInitialSize; w = h * ratio; }
            }

            double posX = Math.Max(40, (MainCanvas.ActualWidth - w) / 2 + offsetX);
            double posY = Math.Max(40, (MainCanvas.ActualHeight - h) / 2 + offsetY);

            var imgItem = new WhiteboardImageItem(Guid.NewGuid().ToString(), bitmap, base64, w, h);
            AttachImageItemEvents(imgItem);

            InkCanvas.SetLeft(imgItem, posX);
            InkCanvas.SetTop(imgItem, posY);
            MainCanvas.Children.Add(imgItem);

            _undoStack.Push(new ElementAddedAction(imgItem, MainCanvas));
            _redoStack.Clear();
            UpdateUndoRedoButtons();

            SelectImageItem(imgItem);
            ScheduleAutoSave();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private void AttachImageItemEvents(WhiteboardImageItem item)
    {
        item.DeleteRequested += (sender) =>
        {
            if (MainCanvas.Children.Contains(sender))
            {
                MainCanvas.Children.Remove(sender);
                _undoStack.Push(new ElementRemovedAction(sender, MainCanvas));
                _redoStack.Clear();
                UpdateUndoRedoButtons();
                if (_selectedImageItem == sender) _selectedImageItem = null;
                ScheduleAutoSave();
            }
        };

        item.Selected += (sender) => SelectImageItem(sender);
        item.Changed += ScheduleAutoSave;
    }

    private void SelectImageItem(WhiteboardImageItem item)
    {
        DeselectAllItems();
        _selectedImageItem = item;
        _selectedImageItem.SetSelected(true);
    }

    private void DeselectAllItems()
    {
        if (_selectedImageItem != null)
        {
            _selectedImageItem.SetSelected(false);
            _selectedImageItem = null;
        }
        if (_selectedStickerItem != null)
        {
            _selectedStickerItem.SetSelected(false);
            _selectedStickerItem = null;
        }
    }

    private static bool IsInsideInteractiveItem(DependencyObject? obj)
    {
        while (obj != null)
        {
            if (obj is Button || obj is TextBox || obj is Slider ||
                obj is WhiteboardRulerItem ||
                obj is WhiteboardProtractorItem ||
                obj is WhiteboardSetSquareItem ||
                obj is WhiteboardStickerItem ||
                obj is WhiteboardImageItem)
            {
                return true;
            }
            if (obj is FrameworkElement fe && fe.Tag is WhiteboardTextTag)
            {
                return true;
            }
            obj = VisualTreeHelper.GetParent(obj);
        }
        return false;
    }

    private static BitmapImage Base64ToBitmapImage(string base64)
    {
        byte[] bytes = Convert.FromBase64String(base64);
        var image = new BitmapImage();
        using var ms = new MemoryStream(bytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string BitmapSourceToBase64(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    // =========================================================================
    // ПЕРЕХВАТ МИШІ
    // =========================================================================

        private void EraseChildrenAtPoint(Point pt, double radius)
    {
        if (MainCanvas == null) return;
        var toRemove = new List<UIElement>();
        foreach (UIElement child in MainCanvas.Children)
        {
            if (child is FrameworkElement fe)
            {
                double left = InkCanvas.GetLeft(fe);
                double top = InkCanvas.GetTop(fe);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;
                double w = fe.ActualWidth > 0 ? fe.ActualWidth : fe.Width;
                double h = fe.ActualHeight > 0 ? fe.ActualHeight : fe.Height;
                if (w <= 0) w = 30;
                if (h <= 0) h = 30;

                double hitRadius = (fe.Tag is WhiteboardVertexTag || (fe.Tag is string sTag && sTag == "VertexLabel")) ? Math.Max(radius, 36.0) : radius;
                var rect = new Rect(left - hitRadius, top - hitRadius, w + hitRadius * 2, h + hitRadius * 2);
                if (rect.Contains(pt))
                {
                    toRemove.Add(fe);
                }
            }
        }

        foreach (var el in toRemove)
        {
            MainCanvas.Children.Remove(el);
            _undoStack.Push(new ElementRemovedAction(el, MainCanvas));
        }
        if (toRemove.Count > 0)
        {
            UpdateUndoRedoButtons();
            ScheduleAutoSave();
        }
    }

    private void MainCanvas_StrokeErasing(object sender, InkCanvasStrokeErasingEventArgs e)
    {
        if (TryGetStrokeShapeId(e.Stroke, out var shapeId))
        {
            var relatedLabels = MainCanvas.Children.OfType<FrameworkElement>()
                .Where(fe => fe.Tag is WhiteboardVertexTag vt && vt.ShapeId == shapeId).ToList();
            foreach (var lbl in relatedLabels)
            {
                MainCanvas.Children.Remove(lbl);
                _undoStack.Push(new ElementRemovedAction(lbl, MainCanvas));
            }
        }
    }

    private void MainCanvas_StrokeErased(object sender, RoutedEventArgs e)
    {
        var allShapeIds = new HashSet<Guid>();
        foreach (var s in MainCanvas.Strokes)
        {
            if (TryGetStrokeShapeId(s, out var sid))
            {
                allShapeIds.Add(sid);
            }
        }

        var orphaned = MainCanvas.Children.OfType<FrameworkElement>()
            .Where(f => f.Tag is WhiteboardVertexTag vt && !allShapeIds.Contains(vt.ShapeId))
            .ToList();

        foreach (var o in orphaned)
        {
            MainCanvas.Children.Remove(o);
        }
    }

private void MainCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideInteractiveItem(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (_activeTool == WhiteboardTool.Laser)
        {
            Point laserPos = e.GetPosition(LaserCanvas);
            TriggerLaserAttentionPing(laserPos);
            e.Handled = true;
            return;
        }

        Point pos = e.GetPosition(MainCanvas);

        if (_activeTool == WhiteboardTool.Eraser)
        {
            EraseChildrenAtPoint(pos, 24.0);
        }

        if (_activeTool == WhiteboardTool.Text)
        {
            CreateEditableTextBox(pos);
            e.Handled = true;
            return;
        }

        if (_activeTool == WhiteboardTool.Shapes)
        {
            StartShapeDrawing(pos);
            MainCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        DeselectAllItems();
    }

    private void MainCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_activeTool == WhiteboardTool.Laser)
        {
            Point laserPos = e.GetPosition(LaserCanvas);
            UpdateLaserPointer(laserPos);
            return;
        }

        if (_activeTool == WhiteboardTool.Eraser && e.LeftButton == MouseButtonState.Pressed)
        {
            Point pos = e.GetPosition(MainCanvas);
            EraseChildrenAtPoint(pos, 24.0);
        }

        if (_activeTool == WhiteboardTool.Shapes && _isDrawingShape)
        {
            Point pos = e.GetPosition(MainCanvas);
            UpdateShapeDrawing(pos);
            return;
        }
    }

    private void MainCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_activeTool == WhiteboardTool.Shapes && _isDrawingShape)
        {
            MainCanvas.ReleaseMouseCapture();
            Point pos = e.GetPosition(MainCanvas);
            CommitShapeDrawing(pos);
            e.Handled = true;
        }
    }

    // =========================================================================
    // DRAG & DROP ТА CTRL+V
    // =========================================================================

    private void MainCanvas_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void MainCanvas_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        try
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var supported = new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" };
            Point dropPoint = e.GetPosition(MainCanvas);

            double offsetX = 0;
            foreach (var file in files)
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (supported.Contains(ext))
                {
                    byte[] bytes = File.ReadAllBytes(file);
                    string base64 = Convert.ToBase64String(bytes);
                    var bitmap = Base64ToBitmapImage(base64);

                    double w = bitmap.PixelWidth;
                    double h = bitmap.PixelHeight;
                    const double maxInitialSize = 360.0;
                    if (w > maxInitialSize || h > maxInitialSize)
                    {
                        double ratio = w / h;
                        if (w >= h) { w = maxInitialSize; h = w / ratio; }
                        else { h = maxInitialSize; w = h * ratio; }
                    }

                    var imgItem = new WhiteboardImageItem(Guid.NewGuid().ToString(), bitmap, base64, w, h);
                    AttachImageItemEvents(imgItem);
                    InkCanvas.SetLeft(imgItem, Math.Max(10, dropPoint.X + offsetX));
                    InkCanvas.SetTop(imgItem, Math.Max(10, dropPoint.Y));
                    MainCanvas.Children.Add(imgItem);

                    _undoStack.Push(new ElementAddedAction(imgItem, MainCanvas));
                    _redoStack.Clear();
                    UpdateUndoRedoButtons();

                    SelectImageItem(imgItem);
                    offsetX += 30;
                }
            }
            ScheduleAutoSave();
            e.Handled = true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private async void RootGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_activePresentation != null && _activePresentation.Slides.Count > 0 &&
            BoardPresentationOverlay?.Visibility == Visibility.Visible &&
            PresentationOverlayGrid?.Visibility != Visibility.Visible)
        {
            if (e.Key == Key.Left || e.Key == Key.PageUp)
            {
                PreviousSlide();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Right || e.Key == Key.PageDown || e.Key == Key.Space)
            {
                NextSlide();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                CloseBoardPresentation();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.F5)
        {
            if (_activePresentation != null && _activePresentation.Slides.Count > 0)
            {
                if (PresentationOverlayGrid?.Visibility == Visibility.Visible)
                {
                    ExitSlideshow();
                }
                else
                {
                    StartSlideshow(_slideshowCurrentIndex);
                }
                e.Handled = true;
                return;
            }
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Key == Key.Z)
            {
                PerformUndo();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Y)
            {
                PerformRedo();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (Keyboard.FocusedElement is TextBox) return;

            try
            {
                if (Clipboard.ContainsImage())
                {
                    var bitmap = Clipboard.GetImage();
                    if (bitmap != null)
                    {
                        string base64 = BitmapSourceToBase64(bitmap);
                        double w = bitmap.PixelWidth;
                        double h = bitmap.PixelHeight;
                        const double maxInitialSize = 360.0;
                        if (w > maxInitialSize || h > maxInitialSize)
                        {
                            double ratio = w / h;
                            if (w >= h) { w = maxInitialSize; h = w / ratio; }
                            else { h = maxInitialSize; w = h * ratio; }
                        }

                        double posX = Math.Max(40, (MainCanvas.ActualWidth - w) / 2);
                        double posY = Math.Max(40, (MainCanvas.ActualHeight - h) / 2);

                        var imgItem = new WhiteboardImageItem(Guid.NewGuid().ToString(), bitmap, base64, w, h);
                        AttachImageItemEvents(imgItem);
                        InkCanvas.SetLeft(imgItem, posX);
                        InkCanvas.SetTop(imgItem, posY);
                        MainCanvas.Children.Add(imgItem);

                        _undoStack.Push(new ElementAddedAction(imgItem, MainCanvas));
                        _redoStack.Clear();
                        UpdateUndoRedoButtons();

                        SelectImageItem(imgItem);
                        ScheduleAutoSave();
                        e.Handled = true;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log(ex);
            }
        }

        if ((e.Key == Key.Delete || e.Key == Key.Back) && Keyboard.FocusedElement is not TextBox)
        {
            if (_selectedImageItem != null && MainCanvas.Children.Contains(_selectedImageItem))
            {
                MainCanvas.Children.Remove(_selectedImageItem);
                _undoStack.Push(new ElementRemovedAction(_selectedImageItem, MainCanvas));
                _redoStack.Clear();
                UpdateUndoRedoButtons();
                _selectedImageItem = null;
                ScheduleAutoSave();
                e.Handled = true;
                return;
            }
            if (_selectedStickerItem != null && MainCanvas.Children.Contains(_selectedStickerItem))
            {
                MainCanvas.Children.Remove(_selectedStickerItem);
                _undoStack.Push(new ElementRemovedAction(_selectedStickerItem, MainCanvas));
                _redoStack.Clear();
                UpdateUndoRedoButtons();
                _selectedStickerItem = null;
                ScheduleAutoSave();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Enter && Keyboard.FocusedElement is not TextBox)
        {
            _inactivityTimer.Stop();
            e.Handled = true;

            if (MainCanvas.Strokes.Count > 0 && !_isProcessingOcr)
            {
                var selectedStrokes = MainCanvas.GetSelectedStrokes();
                var toConvert = selectedStrokes.Count > 0
                    ? new StrokeCollection(selectedStrokes)
                    : new StrokeCollection(MainCanvas.Strokes);

                _currentWordStrokes.Clear();
                _recentDrawnStrokes.Clear();

                _isProcessingOcr = true;
                try
                {
                    await ProcessAndSnapInkAsync(toConvert);
                }
                finally
                {
                    _isProcessingOcr = false;
                }
            }
        }
    }

    // =========================================================================
    // ТЕКСТОВИЙ РЕДАКТОР (PROPYSY)
    // =========================================================================

    private void CreateEditableTextBox(Point position, string initialText = "")
    {
        int row = (int)Math.Floor(position.Y / LineHeight);
        if (row < 0) row = 0;
        double snappedBaseY = (row + 1) * LineHeight;
        double fontSize = LineHeight * 0.84;
        double baselineOffset = fontSize * 1.875;

        var textBox = new TextBox
        {
            FontFamily = _propysyFont,
            FontSize = fontSize,
            Foreground = new SolidColorBrush(_currentPenColor),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 59, 130, 246)),
            BorderThickness = new Thickness(0, 0, 0, 1.5),
            Padding = new Thickness(2, 0, 2, 0),
            MinWidth = 80,
            CaretBrush = new SolidColorBrush(_currentPenColor),
            AcceptsReturn = false,
            Text = initialText
        };

        if (!string.IsNullOrEmpty(initialText))
        {
            textBox.CaretIndex = initialText.Length;
        }

        Typography.SetContextualAlternates(textBox, true);
        Typography.SetStandardLigatures(textBox, true);
        Typography.SetContextualLigatures(textBox, true);

        double targetLeft = Math.Max(10, position.X);
        double targetTop = Math.Max(0, snappedBaseY - baselineOffset);

        InkCanvas.SetLeft(textBox, targetLeft);
        InkCanvas.SetTop(textBox, targetTop);
        MainCanvas.Children.Add(textBox);

        textBox.Loaded += (_, _) =>
        {
            textBox.Focus();
            Keyboard.Focus(textBox);
            if (!string.IsNullOrEmpty(initialText))
            {
                textBox.SelectAll();
            }
        };

        void CommitTextBox()
        {
            if (!MainCanvas.Children.Contains(textBox)) return;
            string enteredText = textBox.Text.Trim();
            double left = InkCanvas.GetLeft(textBox);
            double top = InkCanvas.GetTop(textBox);
            MainCanvas.Children.Remove(textBox);

            if (!string.IsNullOrWhiteSpace(enteredText))
            {
                double canvasWidth = MainCanvas.ActualWidth > 300 ? MainCanvas.ActualWidth : 1200.0;
                double rightMargin = Math.Max(500.0, canvasWidth - 65.0);
                double defaultLeftMargin = 32.0;

                double firstLineWidth = Math.Max(150.0, rightMargin - left);
                double subsequentLineWidth = Math.Max(200.0, rightMargin - defaultLeftMargin);

                var wrappedLines = WrapCursiveText(enteredText, firstLineWidth, subsequentLineWidth, fontSize);
                int baseRow = (int)Math.Round(top / LineHeight);

                for (int i = 0; i < wrappedLines.Count; i++)
                {
                    double curLeft = (i == 0) ? left : defaultLeftMargin;
                    int curRow = baseRow + i;
                    double curTop = Math.Max(0, (curRow + 1) * LineHeight - baselineOffset);

                    var textElement = CreateDraggablePropysyText(wrappedLines[i], _currentPenColor, fontSize);
                    InkCanvas.SetLeft(textElement, curLeft);
                    InkCanvas.SetTop(textElement, curTop);
                    MainCanvas.Children.Add(textElement);

                    _undoStack.Push(new ElementAddedAction(textElement, MainCanvas));
                }

                _redoStack.Clear();
                UpdateUndoRedoButtons();
                ScheduleAutoSave();
            }
        }

        textBox.LostFocus += (_, _) => CommitTextBox();
        textBox.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter)
            {
                CommitTextBox();
                ke.Handled = true;
            }
            else if (ke.Key == Key.Escape)
            {
                MainCanvas.Children.Remove(textBox);
                ke.Handled = true;
            }
        };
    }

    private record WhiteboardTextTag(string Text, string ColorHex, double FontSize);

    private FrameworkElement CreateDraggablePropysyText(string text, Color color, double fontSize, FontFamily? font = null)
    {
        var textBlock = font != null ? new TextBlock
        {
            Text = text,
            FontFamily = font,
            FontSize = fontSize,
            Foreground = new SolidColorBrush(color),
            LineHeight = fontSize * 1.2
        } : CreatePropysyTextBlock(text, color, fontSize);

        string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        var container = new Grid
        {
            Cursor = Cursors.SizeAll,
            Tag = new WhiteboardTextTag(text, hex, fontSize),
            Focusable = true
        };

        var selectionBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 59, 130, 246)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Background = Brushes.Transparent,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };

        container.Children.Add(textBlock);
        container.Children.Add(selectionBorder);

        bool isDragging = false;
        Point dragStart = default;
        Point itemStart = default;

        container.GotFocus += (_, _) => selectionBorder.Visibility = Visibility.Visible;
        container.LostFocus += (_, _) => selectionBorder.Visibility = Visibility.Collapsed;

        container.KeyDown += (s, ke) =>
        {
            if (ke.Key == Key.Delete || ke.Key == Key.Back)
            {
                if (MainCanvas.Children.Contains(container))
                {
                    MainCanvas.Children.Remove(container);
                    _undoStack.Push(new ElementRemovedAction(container, MainCanvas));
                    _redoStack.Clear();
                    UpdateUndoRedoButtons();
                    ScheduleAutoSave();
                    ke.Handled = true;
                }
            }
        };

        container.PreviewMouseLeftButtonDown += (s, e) =>
        {
            container.Focus();
            isDragging = true;
            dragStart = e.GetPosition(MainCanvas);
            itemStart = new Point(InkCanvas.GetLeft(container), InkCanvas.GetTop(container));
            container.CaptureMouse();
            e.Handled = true;
        };

        container.MouseMove += (s, e) =>
        {
            if (!isDragging) return;
            Point cur = e.GetPosition(MainCanvas);
            double newX = Math.Max(0, itemStart.X + (cur.X - dragStart.X));
            double newY = Math.Max(0, itemStart.Y + (cur.Y - dragStart.Y));
            InkCanvas.SetLeft(container, newX);
            InkCanvas.SetTop(container, newY);
            e.Handled = true;
        };

        container.MouseLeftButtonUp += (s, e) =>
        {
            if (isDragging)
            {
                isDragging = false;
                container.ReleaseMouseCapture();
                ScheduleAutoSave();
                e.Handled = true;
            }
        };

        return container;
    }

    public static bool IsChildOf(DependencyObject? child, DependencyObject? parent)
    {
        DependencyObject? current = child;
        while (current != null)
        {
            if (current == parent) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private TextBlock CreatePropysyTextBlock(string text, Color color, double fontSize)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontFamily = _propysyFont,
            FontSize = fontSize,
            Foreground = new SolidColorBrush(color),
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };

        Typography.SetContextualAlternates(tb, true);
        Typography.SetStandardLigatures(tb, true);
        Typography.SetContextualLigatures(tb, true);
        Typography.SetDiscretionaryLigatures(tb, true);

        return tb;
    }

    private List<string> WrapCursiveText(string rawText, double firstLineWidth, double subsequentLineWidth, double fontSize)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(rawText)) return result;

        string[] words = rawText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return result;

        string currentLine = "";
        double currentLimit = firstLineWidth;

        double Measure(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0.0;
            var ft = new FormattedText(
                s,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface(_propysyFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                fontSize,
                Brushes.Black,
                1.0);
            return ft.WidthIncludingTrailingWhitespace;
        }

        foreach (var word in words)
        {
            string candidate = string.IsNullOrEmpty(currentLine) ? word : (currentLine + " " + word);
            if (Measure(candidate) <= currentLimit)
            {
                currentLine = candidate;
                continue;
            }

            // Слово не поміщається цілком. Пробуємо перенос за складами української мови!
            var hyphenPositions = UkrHyphenator.GetHyphenationPositions(word);
            bool hyphenApplied = false;

            for (int h = hyphenPositions.Count - 1; h >= 0; h--)
            {
                int cut = hyphenPositions[h];
                string prefix = word.Substring(0, cut) + "-";
                string candidateWithHyphen = string.IsNullOrEmpty(currentLine) ? prefix : (currentLine + " " + prefix);

                if (Measure(candidateWithHyphen) <= currentLimit)
                {
                    result.Add(candidateWithHyphen);
                    currentLine = word.Substring(cut);
                    currentLimit = subsequentLineWidth;
                    hyphenApplied = true;
                    break;
                }
            }

            if (!hyphenApplied)
            {
                if (!string.IsNullOrEmpty(currentLine))
                {
                    result.Add(currentLine);
                    currentLimit = subsequentLineWidth;
                    currentLine = "";

                    if (Measure(word) <= currentLimit)
                    {
                        currentLine = word;
                    }
                    else
                    {
                        bool newHyphenApplied = false;
                        for (int h = hyphenPositions.Count - 1; h >= 0; h--)
                        {
                            int cut = hyphenPositions[h];
                            string prefix = word.Substring(0, cut) + "-";
                            if (Measure(prefix) <= currentLimit)
                            {
                                result.Add(prefix);
                                currentLine = word.Substring(cut);
                                newHyphenApplied = true;
                                break;
                            }
                        }
                        if (!newHyphenApplied)
                        {
                            currentLine = word;
                        }
                    }
                }
                else
                {
                    currentLine = word;
                }
            }
        }

        if (!string.IsNullOrEmpty(currentLine))
        {
            result.Add(currentLine);
        }

        return result;
    }

    // =========================================================================
    // ШІ-РОЗПІЗНАВАННЯ КАЛІГРАФІЇ («🪄 В ПРОПИС» ПРАЦЮЄ ЗАВЖДИ)
    // =========================================================================

    private void AiRecognitionToggle_Click(object sender, RoutedEventArgs e)
    {
        _isAiEnabled = AiRecognitionToggle.IsChecked ?? false;
        if (!_isAiEnabled)
        {
            _inactivityTimer.Stop();
            _currentWordStrokes.Clear();
        }
    }

    private async void RecognizeNowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessingOcr) return;

        StrokeCollection strokesToProcess;

        // 1. Если есть штрихи в текущем буфере ввода
        if (_currentWordStrokes.Count > 0)
        {
            _inactivityTimer.Stop();
            strokesToProcess = new StrokeCollection(_currentWordStrokes);
            _currentWordStrokes.Clear();
        }
        else
        {
            // 2. Если пользователь выделил штрихи
            var selectedStrokes = MainCanvas.GetSelectedStrokes();
            if (selectedStrokes.Count > 0)
            {
                strokesToProcess = new StrokeCollection(selectedStrokes);
            }
            // 3. Если есть недавние нарисованные штрихи
            else if (_recentDrawnStrokes.Count > 0)
            {
                strokesToProcess = new StrokeCollection();
                foreach (var s in _recentDrawnStrokes)
                {
                    if (MainCanvas.Strokes.Contains(s)) strokesToProcess.Add(s);
                }
                _recentDrawnStrokes.Clear();
            }
            // 4. Если на холсте есть любые штрихи
            else if (MainCanvas.Strokes.Count > 0)
            {
                strokesToProcess = new StrokeCollection(MainCanvas.Strokes);
            }
            else
            {
                ShowAiStatus("Спочатку напишіть текст на дошці ✍");
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                timer.Tick += (_, _) => { timer.Stop(); HideAiStatus(); };
                timer.Start();
                return;
            }
        }

        if (strokesToProcess.Count == 0) return;

        _isProcessingOcr = true;
        try
        {
            await ProcessAndSnapInkAsync(strokesToProcess);
        }
        finally
        {
            _isProcessingOcr = false;
        }
    }

    private void MainCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        Stroke finalStroke = e.Stroke;
        if (_currentStrokePattern != WhiteboardStrokePattern.Solid && _activeTool == WhiteboardTool.Pencil)
        {
            var customStroke = new WhiteboardDashedStroke(e.Stroke.StylusPoints.Clone(), e.Stroke.DrawingAttributes.Clone(), _currentStrokePattern);
            int idx = MainCanvas.Strokes.IndexOf(e.Stroke);
            if (idx >= 0)
            {
                try { MainCanvas.Strokes[idx] = customStroke; } catch { }
            }
            finalStroke = customStroke;
        }

        if (!_recentDrawnStrokes.Contains(finalStroke))
        {
            try { _recentDrawnStrokes.Add(finalStroke); } catch { }
        }

        if (!_isAutoPropysyEnabled || _activeTool != WhiteboardTool.Pencil)
        {
            _inactivityTimer.Stop();
            return;
        }

        if (!_currentWordStrokes.Contains(finalStroke))
        {
            try { _currentWordStrokes.Add(finalStroke); } catch { }
        }

        // Авто-режим: перезапускаем таймер неактивности (1.6 с) после каждого завершенного штриха
        _inactivityTimer.Stop();
        _inactivityTimer.Interval = TimeSpan.FromMilliseconds(1600);
        _inactivityTimer.Start();
    }

    private async void OnInactivityTimerTick(object? sender, EventArgs e)
    {
        _inactivityTimer.Stop();

        if (sender != null && !_isAutoPropysyEnabled)
        {
            return;
        }

        if (_isProcessingOcr)
        {
            return;
        }

        // Если пользователь в этот момент удерживает мышь/стилус, откладываем на 1 сек
        if (Mouse.LeftButton == MouseButtonState.Pressed)
        {
            _inactivityTimer.Interval = TimeSpan.FromMilliseconds(1000);
            _inactivityTimer.Start();
            return;
        }

        StrokeCollection strokesToProcess;
        if (_currentWordStrokes.Count > 0)
        {
            strokesToProcess = new StrokeCollection(_currentWordStrokes);
            _currentWordStrokes.Clear();
        }
        else if (MainCanvas.Strokes.Count > 0)
        {
            strokesToProcess = new StrokeCollection(MainCanvas.Strokes);
        }
        else
        {
            return;
        }

        if (strokesToProcess.Count == 0) return;

        _isProcessingOcr = true;
        try
        {
            await ProcessAndSnapInkAsync(strokesToProcess);
        }
        finally
        {
            _isProcessingOcr = false;
        }
    }

    private async Task ProcessAndSnapInkAsync(StrokeCollection strokes)
    {
        if (strokes.Count == 0) return;

        var validStrokes = FilterOutlierStrokes(strokes);
        if (validStrokes.Count == 0) return;

        Rect bounds = validStrokes.GetBounds();
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;

        foreach (var stroke in strokes)
        {
            stroke.DrawingAttributes.Color = Color.FromRgb(79, 70, 229);
        }

        ShowAiStatus("Оцифрування в пропис...");

        try
        {
            string? recognizedText = await RecognizeInkAsync(validStrokes);

            if (string.IsNullOrWhiteSpace(recognizedText))
            {
                foreach (var stroke in strokes)
                {
                    stroke.DrawingAttributes.Color = _currentPenColor;
                }
                ShowAiStatus("Не вдалося розпізнати. Спробуйте написати чіткіше");
                var failTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                failTimer.Tick += (_, _) => { failTimer.Stop(); HideAiStatus(); };
                failTimer.Start();
                return;
            }

            var textLines = recognizedText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (textLines.Length == 0)
            {
                HideAiStatus();
                return;
            }

            foreach (var stroke in strokes)
            {
                if (MainCanvas.Strokes.Contains(stroke))
                {
                    MainCanvas.Strokes.Remove(stroke);
                }
            }

            int baseRow = (int)Math.Floor(bounds.Top / LineHeight);
            if (baseRow < 0) baseRow = 0;
            if (textLines.Length == 1)
            {
                double centerY = (bounds.Top + bounds.Bottom) / 2.0;
                baseRow = (int)Math.Floor(centerY / LineHeight);
                if (baseRow < 0) baseRow = 0;
            }

            double fontSize = LineHeight * 0.84;
            double baselineOffset = fontSize * 1.875;
            const double WordSpacing = 14.0;

            for (int lineIndex = 0; lineIndex < textLines.Length; lineIndex++)
            {
                string lineText = textLines[lineIndex].Trim();
                if (string.IsNullOrWhiteSpace(lineText)) continue;

                int currentRow = baseRow + lineIndex;
                double snappedBaseY = (currentRow + 1) * LineHeight;
                double targetTop = Math.Max(0, snappedBaseY - baselineOffset);

                var ft = new FormattedText(
                    lineText,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    new Typeface(_propysyFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    fontSize,
                    Brushes.Black,
                    1.0);
                double wordWidth = ft.WidthIncludingTrailingWhitespace;

                var occupiedIntervals = new List<(double Left, double Right, FrameworkElement Element)>();
                foreach (UIElement child in MainCanvas.Children)
                {
                    if (child is FrameworkElement fe)
                    {
                        double feTop = InkCanvas.GetTop(fe);
                        if (double.IsNaN(feTop)) continue;

                        if (Math.Abs(feTop - targetTop) < LineHeight * 0.6)
                        {
                            double feLeft = InkCanvas.GetLeft(fe);
                            if (double.IsNaN(feLeft)) feLeft = 0;

                            double feWidth = fe.ActualWidth > 0 ? fe.ActualWidth : fe.Width;
                            if (feWidth <= 0 && fe is Grid g && g.Children.Count > 0 && g.Children[0] is TextBlock tb)
                            {
                                var existingFt = new FormattedText(
                                    tb.Text,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    System.Windows.FlowDirection.LeftToRight,
                                    new Typeface(_propysyFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                                    fontSize,
                                    Brushes.Black,
                                    1.0);
                                feWidth = existingFt.WidthIncludingTrailingWhitespace;
                            }

                            if (feWidth > 0)
                            {
                                occupiedIntervals.Add((feLeft, feLeft + feWidth, fe));
                            }
                        }
                    }
                }

                for (int i = occupiedIntervals.Count - 1; i >= 0; i--)
                {
                    var (exLeft, exRight, exElem) = occupiedIntervals[i];
                    double overlapLeft = Math.Max(bounds.Left, exLeft);
                    double overlapRight = Math.Min(bounds.Right, exRight);
                    double overlapWidth = Math.Max(0, overlapRight - overlapLeft);

                    double exWidth = exRight - exLeft;
                    if (exWidth > 0 && (overlapWidth / exWidth > 0.45 || (bounds.Width > 0 && overlapWidth / bounds.Width > 0.45)))
                    {
                        MainCanvas.Children.Remove(exElem);
                        occupiedIntervals.RemoveAt(i);
                    }
                }

                occupiedIntervals.Sort((a, b) => a.Left.CompareTo(b.Left));

                double targetLeft = Math.Max(16.0, bounds.Left);
                bool hasOverlap = true;
                int maxIterations = 50;
                while (hasOverlap && maxIterations-- > 0)
                {
                    hasOverlap = false;
                    foreach (var interval in occupiedIntervals)
                    {
                        if (targetLeft < interval.Right + WordSpacing && (targetLeft + wordWidth) > interval.Left - WordSpacing)
                        {
                            targetLeft = interval.Right + WordSpacing;
                            hasOverlap = true;
                            break;
                        }
                    }
                }

                double canvasWidth = MainCanvas.ActualWidth > 300 ? MainCanvas.ActualWidth : 1200.0;
                double rightMargin = Math.Max(500.0, canvasWidth - 65.0);
                double defaultLeftMargin = 32.0;

                double firstLineWidth = Math.Max(150.0, rightMargin - targetLeft);
                double subsequentLineWidth = Math.Max(200.0, rightMargin - defaultLeftMargin);

                var wrappedSubLines = WrapCursiveText(lineText, firstLineWidth, subsequentLineWidth, fontSize);
                for (int subIdx = 0; subIdx < wrappedSubLines.Count; subIdx++)
                {
                    string subLineText = wrappedSubLines[subIdx];
                    int subRow = currentRow + subIdx;
                    double subSnappedBaseY = (subRow + 1) * LineHeight;
                    double subTargetTop = Math.Max(0, subSnappedBaseY - baselineOffset);
                    double subTargetLeft = (subIdx == 0) ? targetLeft : defaultLeftMargin;

                    bool isMath = WhiteboardAiClient.IsMathFormula(subLineText);
                    var textElement = CreateDraggablePropysyText(subLineText, _currentPenColor, fontSize, isMath ? new FontFamily("Cambria Math, Segoe UI") : _propysyFont);
                    InkCanvas.SetLeft(textElement, subTargetLeft);
                    InkCanvas.SetTop(textElement, subTargetTop);
                    MainCanvas.Children.Add(textElement);

                    _undoStack.Push(new ElementAddedAction(textElement, MainCanvas));
                }

                if (wrappedSubLines.Count > 1)
                {
                    baseRow += wrappedSubLines.Count - 1;
                }
                _redoStack.Clear();
                UpdateUndoRedoButtons();
            }

            ScheduleAutoSave();
            HideAiStatus();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            foreach (var stroke in strokes)
            {
                stroke.DrawingAttributes.Color = _currentPenColor;
            }
            ShowAiStatus("Помилка розпізнавання. Перевірте мережеве з'єднання");
            var errTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.0) };
            errTimer.Tick += (_, _) => { errTimer.Stop(); HideAiStatus(); };
            errTimer.Start();
        }
    }

    private static StrokeCollection FilterOutlierStrokes(StrokeCollection strokes)
    {
        if (strokes == null || strokes.Count <= 1) return strokes ?? new StrokeCollection();

        double totalX = 0, totalY = 0;
        int count = 0;
        foreach (var s in strokes)
        {
            var b = s.GetBounds();
            totalX += (b.Left + b.Right) / 2.0;
            totalY += (b.Top + b.Bottom) / 2.0;
            count++;
        }
        if (count == 0) return strokes;

        double avgX = totalX / count;
        double avgY = totalY / count;

        var result = new StrokeCollection();
        foreach (var s in strokes)
        {
            var b = s.GetBounds();
            double cx = (b.Left + b.Right) / 2.0;
            double cy = (b.Top + b.Bottom) / 2.0;
            double dist = Math.Sqrt((cx - avgX) * (cx - avgX) + (cy - avgY) * (cy - avgY));

            if (b.Width < 8 && b.Height < 8 && dist > 250)
            {
                continue;
            }
            result.Add(s);
        }

        return result.Count > 0 ? result : strokes;
    }

    private static readonly System.Net.Http.HttpClient GoogleHandwritingHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    public async Task<string?> RecognizeInkAsync(StrokeCollection strokes)
    {
        if (strokes.Count == 0) return null;

        // TIER 1: Онлайн ИИ-распознавание каллиграфии Google Handwriting с контекстным выбором кандидата
        try
        {
            string? googleResult = await RecognizeViaGoogleHandwritingAsync(strokes);
            if (!string.IsNullOrWhiteSpace(googleResult))
            {
                return PostProcessContextualText(googleResult.Trim());
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }

        byte[] pngBytes = ExportStrokesToPngBytes(strokes);
        string base64Png = Convert.ToBase64String(pngBytes);

        // TIER 2: Облачный Vision AI (Gemini / Multi-language Vision)
        try
        {
            string? cloudResult = await WhiteboardAiClient.RecognizeHandwritingAsync(base64Png);
            if (!string.IsNullOrWhiteSpace(cloudResult))
            {
                return PostProcessContextualText(cloudResult.Trim());
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }

        // TIER 3: Локальный Windows OCR (ru-RU, en-US, uk-UA)
        try
        {
            string? localResult = await RecognizeViaWindowsOcrAsync(pngBytes);
            if (!string.IsNullOrWhiteSpace(localResult))
            {
                return PostProcessContextualText(WhiteboardAiClient.CleanOcrText(localResult));
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }

        return null;
    }

    private static async Task<string?> RecognizeViaGoogleHandwritingAsync(StrokeCollection strokes)
    {
        if (strokes == null || strokes.Count == 0) return null;

        Rect bounds = strokes.GetBounds();
        int width = Math.Max(800, (int)Math.Ceiling(bounds.Width + 100));
        int height = Math.Max(600, (int)Math.Ceiling(bounds.Height + 100));

        var inkList = new List<object>();
        int time = 0;

        foreach (var stroke in strokes)
        {
            if (stroke.StylusPoints.Count == 0) continue;
            var xList = new List<int>();
            var yList = new List<int>();
            var tList = new List<int>();

            foreach (var pt in stroke.StylusPoints)
            {
                xList.Add((int)Math.Round(pt.X));
                yList.Add((int)Math.Round(pt.Y));
                tList.Add(time);
                time += 12;
            }

            inkList.Add(new object[] { xList, yList, tList });
        }

        if (inkList.Count == 0) return null;

        string[] languages = { "uk", "en" };
        foreach (var lang in languages)
        {
            try
            {
                var payload = new
                {
                    app = "mobilesearch",
                    api_level = "537.36",
                    device = "Chrome",
                    input_type = "0",
                    options = "enable_pre_space",
                    requests = new[]
                    {
                        new
                        {
                            writing_guide = new { writing_area_width = width, writing_area_height = height },
                            pre_context = "",
                            max_num_results = 15, // Запрашиваем 15 гипотез для полноценного контекстного ранжирования
                            max_completions = 0,
                            language = lang,
                            ink = inkList
                        }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await GoogleHandwritingHttp.PostAsync("https://inputtools.google.com/request?ime=handwriting&app=mobilesearch", content);

                if (!response.IsSuccessStatusCode) continue;

                var respString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(respString);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() >= 2)
                {
                    var status = root[0].GetString();
                    if (status == "SUCCESS")
                    {
                        var dataArray = root[1];
                        if (dataArray.GetArrayLength() > 0)
                        {
                            var candidatesGroup = dataArray[0];
                            if (candidatesGroup.GetArrayLength() >= 2)
                            {
                                var candidatesList = candidatesGroup[1];
                                var candidates = new List<string>();
                                foreach (var cand in candidatesList.EnumerateArray())
                                {
                                    var strVal = cand.GetString();
                                    if (!string.IsNullOrWhiteSpace(strVal))
                                    {
                                        candidates.Add(strVal.Trim());
                                    }
                                }

                                if (candidates.Count > 0)
                                {
                                    string best = SelectBestContextualCandidate(candidates);
                                    if (!string.IsNullOrWhiteSpace(best))
                                    {
                                        return best;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Попробовать следующий язык или fallback
            }
        }

        return null;
    }

    private static async Task<string?> RecognizeViaWindowsOcrAsync(byte[] pngBytes)
    {
        try
        {
            using var randomAccessStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new Windows.Storage.Streams.DataWriter(randomAccessStream))
            {
                writer.WriteBytes(pngBytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(randomAccessStream);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            Windows.Media.Ocr.OcrEngine? engine = null;
            try
            {
                if (Windows.Media.Ocr.OcrEngine.IsLanguageSupported(new Windows.Globalization.Language("uk-UA")))
                    engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("uk-UA"));
            }
            catch { }

            engine ??= Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();

            try
            {
                if (engine == null && Windows.Media.Ocr.OcrEngine.IsLanguageSupported(new Windows.Globalization.Language("en-US")))
                    engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
                if (engine == null && Windows.Media.Ocr.OcrEngine.IsLanguageSupported(new Windows.Globalization.Language("uk-UA")))
                    engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("uk-UA"));
            }
            catch { }

            if (engine != null)
            {
                var ocrResult = await engine.RecognizeAsync(softwareBitmap);
                var text = ocrResult?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        return null;
    }

        private static byte[] ExportStrokesToPngBytes(StrokeCollection strokes)
    {
        Rect bounds = strokes.GetBounds();
        const double pad = 30.0;

        int renderWidth = Math.Max(120, (int)Math.Ceiling(bounds.Width + pad * 2));
        int renderHeight = Math.Max(90, (int)Math.Ceiling(bounds.Height + pad * 2));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, renderWidth, renderHeight));

            var ocrPen = new DrawingAttributes
            {
                Color = Colors.Black,
                Width = 3.6,
                Height = 3.6,
                FitToCurve = true,
                StylusTip = StylusTip.Ellipse
            };

            var transform = new TranslateTransform(-bounds.Left + pad, -bounds.Top + pad);
            dc.PushTransform(transform);

            foreach (var stroke in strokes)
            {
                var copy = stroke.Clone();
                copy.DrawingAttributes = ocrPen;
                copy.Draw(dc);
            }

            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(renderWidth, renderHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var memoryStream = new MemoryStream();
        encoder.Save(memoryStream);
        return memoryStream.ToArray();
    }

    private DispatcherTimer? _statusAutoDismissTimer;

    private void ShowAiStatus(string message)
    {
        if (AiStatusToast == null || AiStatusText == null) return;
        AiStatusText.Text = message;
        AiStatusToast.Visibility = Visibility.Visible;

        _statusAutoDismissTimer?.Stop();
        _statusAutoDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.0) };
        _statusAutoDismissTimer.Tick += (s, e) =>
        {
            _statusAutoDismissTimer?.Stop();
            AiStatusToast.Visibility = Visibility.Collapsed;
        };
        _statusAutoDismissTimer.Start();
    }

    private void HideAiStatus()
    {
        if (AiStatusToast == null) return;
        _statusAutoDismissTimer?.Stop();
        AiStatusToast.Visibility = Visibility.Collapsed;
    }

    private static string SerializeStrokes(StrokeCollection strokes)
    {
        if (strokes == null || strokes.Count == 0) return string.Empty;
        try
        {
            using var ms = new MemoryStream();
            strokes.Save(ms);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return string.Empty;
        }
    }

    private static StrokeCollection DeserializeStrokes(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return new StrokeCollection();
        try
        {
            byte[] bytes = Convert.FromBase64String(base64);
            using var ms = new MemoryStream(bytes);
            return new StrokeCollection(ms);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return new StrokeCollection();
        }
    }


    // =========================================================================
    // НОВІ ОБРОБНИКИ ПАНЕЛІ ІНСТРУМЕНТІВ ТА ДОКА 1:1
    // =========================================================================

    private bool _rightDockCollapsed = false;
    private void RightDockCollapseBtn_Click(object sender, RoutedEventArgs e)
    {
        _rightDockCollapsed = !_rightDockCollapsed;
        if (RightDockCollapseIcon != null)
        {
            RightDockCollapseIcon.Data = Geometry.Parse(_rightDockCollapsed ? "M 11,7 L 16,12 L 11,17 M 5,7 L 10,12 L 5,17" : "M 17,7 L 12,12 L 17,17 M 11,7 L 6,12 L 11,17");
        }
        if (RightDockSheetsBtn != null) RightDockSheetsBtn.Visibility = _rightDockCollapsed ? Visibility.Collapsed : Visibility.Visible;
        if (RightDockAiConvertBtn != null) RightDockAiConvertBtn.Visibility = _rightDockCollapsed ? Visibility.Collapsed : Visibility.Visible;
        if (RightDockHelpBtn != null) RightDockHelpBtn.Visibility = _rightDockCollapsed ? Visibility.Collapsed : Visibility.Visible;
        if (RightDockPinBtn != null) RightDockPinBtn.Visibility = _rightDockCollapsed ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RightDockSheetsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetsManagerDrawer == null) return;
        SheetsManagerDrawer.Visibility = (SheetsManagerDrawer.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        if (SheetsManagerDrawer.Visibility == Visibility.Visible)
        {
            PopulateSheetsDrawer();
        }
    }

    private void PopulateSheetsDrawer()
    {
        if (SheetsListDrawerContainer == null) return;
        SheetsListDrawerContainer.Children.Clear();
        for (int i = 0; i < _session.Boards.Count; i++)
        {
            var b = _session.Boards[i];
            bool isActive = (b.Id == _currentBoard?.Id);

            var itemBorder = new Border
            {
                Background = isActive ? new SolidColorBrush(Color.FromRgb(240, 253, 244)) : new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = isActive ? new SolidColorBrush(Color.FromRgb(34, 178, 76)) : new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(isActive ? 1.5 : 1.0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand,
                Tag = b.Id
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var text = new TextBlock
            {
                Text = b.Title,
                FontSize = 12,
                FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
                Foreground = isActive ? new SolidColorBrush(Color.FromRgb(22, 101, 52)) : new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                VerticalAlignment = VerticalAlignment.Center
            };
            sp.Children.Add(text);
            itemBorder.Child = sp;

            itemBorder.MouseLeftButtonDown += (s, ev) =>
            {
                if (b.Id != _currentBoard?.Id)
                {
                    SwitchToBoard(b.Id);
                    PopulateSheetsDrawer();
                }
            };

            SheetsListDrawerContainer.Children.Add(itemBorder);
        }
    }

    private void CloseSheetsDrawer_Click(object sender, RoutedEventArgs e)
    {
        if (SheetsManagerDrawer != null) SheetsManagerDrawer.Visibility = Visibility.Collapsed;
    }

    private void RightDockParticipantsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ParticipantsFlyout != null)
        {
            ParticipantsFlyout.Visibility = (ParticipantsFlyout.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void CloseParticipantsFlyout_Click(object sender, RoutedEventArgs e)
    {
        if (ParticipantsFlyout != null) ParticipantsFlyout.Visibility = Visibility.Collapsed;
    }

    private void CopyInviteLink_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText($"https://whiteboard.edu/session/{_session.ActiveBoardId ?? Guid.NewGuid().ToString()}"); } catch { }
        ShowAiStatus("Посилання на дошку скопійовано!");
        if (ParticipantsFlyout != null) ParticipantsFlyout.Visibility = Visibility.Collapsed;
    }

    private void RightDockProfileBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileFlyout != null)
        {
            ProfileFlyout.Visibility = (ProfileFlyout.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void CloseProfileFlyout_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileFlyout != null) ProfileFlyout.Visibility = Visibility.Collapsed;
    }

    private void RightDockHelpBtn_Click(object sender, RoutedEventArgs e)
    {
        if (HelpModal != null)
        {
            HelpModal.Visibility = (HelpModal.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void CloseHelpModal_Click(object sender, RoutedEventArgs e)
    {
        if (HelpModal != null) HelpModal.Visibility = Visibility.Collapsed;
    }

    private void RightDockPinBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowAiStatus("Панель інструментів закріплено");
    }

    private void PencilSizeChoice_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tag && double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out double sz))
        {
            _currentPenSize = sz;
            ApplyPenAttributes();
            if (PencilSizeSlider != null) PencilSizeSlider.Value = sz;
            if (PencilSizeLabel != null) PencilSizeLabel.Text = $"{sz:0} px";

            if (PencilSizeS != null)
            {
                PencilSizeS.BorderThickness = (sz == 2) ? new Thickness(1.5) : new Thickness(0);
                PencilSizeS.BorderBrush = (sz == 2) ? new SolidColorBrush(Color.FromRgb(34, 178, 76)) : Brushes.Transparent;
            }
            if (PencilSizeM != null)
            {
                PencilSizeM.BorderThickness = (sz == 4) ? new Thickness(1.5) : new Thickness(0);
                PencilSizeM.BorderBrush = (sz == 4) ? new SolidColorBrush(Color.FromRgb(34, 178, 76)) : Brushes.Transparent;
            }
            if (PencilSizeL != null)
            {
                PencilSizeL.BorderThickness = (sz == 8) ? new Thickness(1.5) : new Thickness(0);
                PencilSizeL.BorderBrush = (sz == 8) ? new SolidColorBrush(Color.FromRgb(34, 178, 76)) : Brushes.Transparent;
            }
            if (PencilSizeXL != null)
            {
                PencilSizeXL.BorderThickness = (sz == 14) ? new Thickness(1.5) : new Thickness(0);
                PencilSizeXL.BorderBrush = (sz == 14) ? new SolidColorBrush(Color.FromRgb(34, 178, 76)) : Brushes.Transparent;
            }
        }
    }

    private void PencilCustomColor_Click(object sender, RoutedEventArgs e)
    {
        var colors = new[] { Color.FromRgb(14, 165, 233), Color.FromRgb(99, 102, 241), Color.FromRgb(236, 72, 153), Color.FromRgb(249, 115, 22), Color.FromRgb(16, 185, 129) };
        var nextCol = colors[Math.Abs(_currentPenColor.GetHashCode()) % colors.Length];
        _currentPenColor = nextCol;
        PencilColorDot.Fill = new SolidColorBrush(nextCol);
        if (CurrentColorCircle != null) CurrentColorCircle.Fill = new SolidColorBrush(nextCol);
        ApplyPenAttributes();
    }

    private void LinesArrowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (LinesArrowsPopup != null)
        {
            LinesArrowsPopup.IsOpen = !LinesArrowsPopup.IsOpen;
        }
    }

    private static readonly Dictionary<string, string> ShapeToCategory = new()
    {
        ["Point"] = "Simplest",
        ["Line"] = "Simplest",
        ["Ray"] = "Simplest",
        ["InfiniteLine"] = "Simplest",
        ["Arc"] = "Simplest",
        ["Angle"] = "Simplest",

        ["Triangle"] = "Planimetry",
        ["RightTriangle"] = "Planimetry",
        ["Rhombus"] = "Planimetry",
        ["Parallelogram"] = "Planimetry",
        ["Trapezoid"] = "Planimetry",
        ["Polygon"] = "Planimetry",
        ["Star"] = "Planimetry",
        ["Rectangle"] = "Planimetry",
        ["Square"] = "Planimetry",
        ["Ellipse"] = "Planimetry",

        ["CurlyBracket"] = "Brackets",
        ["ComparisonBrace"] = "Brackets",
        ["SquareBracket"] = "Brackets",

        ["CoordinateAxes"] = "Graphs",
        ["NumberLine"] = "Graphs",
        ["FunctionGraph"] = "Graphs",

        ["Perpendicular"] = "Constructions",
        ["Bisector"] = "Constructions",
        ["Altitude"] = "Constructions",
        ["Tangent"] = "Constructions",

        ["Cube"] = "Stereometry",
        ["SquarePyramid"] = "Stereometry",
        ["Cylinder"] = "Stereometry",
        ["Sphere"] = "Stereometry"
    };

    private void FilterShapesByCategory(string category)
    {
        if (ShapesGridContainer == null) return;
        foreach (UIElement child in ShapesGridContainer.Children)
        {
            if (child is Button btn && btn.Tag is string shapeName)
            {
                if (ShapeToCategory.TryGetValue(shapeName, out var shapeCat))
                {
                    btn.Visibility = (shapeCat == category) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
    }

    private void ShapesCategoryTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string cat)
        {
            if (btn.Parent is StackPanel sp)
            {
                foreach (var child in sp.Children)
                {
                    if (child is Button b)
                    {
                        bool active = (b == btn);
                        b.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
                        b.Foreground = active ? new SolidColorBrush(Color.FromRgb(15, 23, 42)) : new SolidColorBrush(Color.FromRgb(100, 116, 139));
                    }
                }
            }
            FilterShapesByCategory(cat);
        }
    }

    private void FillBucketBtn_Click(object sender, RoutedEventArgs e)
    {
        MainCanvas.Background = new SolidColorBrush(_currentPenColor);
        ShowAiStatus("Фон аркуша залито");
    }

    private void ColorDotBtn_Click(object sender, RoutedEventArgs e)
    {
        if (QuickColorPopup != null)
        {
            QuickColorPopup.IsOpen = !QuickColorPopup.IsOpen;
        }
    }

    private void FormulaBtn_Click(object sender, RoutedEventArgs e)
    {
        if (FormulaEditorModal != null)
        {
            FormulaEditorModal.Visibility = Visibility.Visible;
            PopulateFormulaPalette("Preset");
        }
    }

    private void CloseFormulaModal_Click(object sender, RoutedEventArgs e)
    {
        if (FormulaEditorModal != null) FormulaEditorModal.Visibility = Visibility.Collapsed;
    }

    private void FormulaTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            if (btn.Parent is StackPanel sp)
            {
                foreach (var child in sp.Children)
                {
                    if (child is Button b)
                    {
                        bool active = (b == btn);
                        b.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
                        b.Foreground = active ? new SolidColorBrush(Color.FromRgb(15, 23, 42)) : new SolidColorBrush(Color.FromRgb(100, 116, 139));
                    }
                }
            }
            PopulateFormulaPalette(tag);
        }
    }

    private void PopulateFormulaPalette(string category)
    {
        if (FormulaSymbolsPalette == null) return;
        FormulaSymbolsPalette.Children.Clear();

        string[] symbols = category switch
        {
            "Roots" => new[] { "√x", "∛x", "x²", "x³", "xⁿ", "x₁", "a/b", "|x|", "(x)", "[x]" },
            "Numbers" => new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "+", "-", "×", "÷", "=", "≠", "<", ">", "≤", "≥", "±", "≈" },
            "Variables" => new[] { "x", "y", "z", "a", "b", "c", "f(x)", "f'(x)", "dy/dx", "S", "V", "t" },
            "Geometry" => new[] { "∠", "Δ", "⊥", "∥", "≅", "∼", "°", "R", "r", "AB", "P", "S" },
            "Constants" => new[] { "π", "e", "∞", "θ", "φ", "log", "ln", "sin", "cos", "tg", "ctg" },
            "Calculus" => new[] { "∫", "∬", "∑", "∏", "lim", "dx", "dt", "→", "∂" },
            "Greek" => new[] { "α", "β", "γ", "δ", "λ", "μ", "σ", "ω", "Ω", "Δ", "ρ", "φ" },
            "Physics" => new[] { "F=ma", "E=mc²", "v=s/t", "ρ=m/V", "P=UI", "I=U/R", "A=Fs", "N=A/t" },
            "Chemistry" => new[] { "H₂O", "CO₂", "O₂", "H⁺", "OH⁻", "NaCl", "→", "⇄", "↑", "↓" },
            _ => new[] { "y = ax² + bx + c", "a² + b² = c²", "S = πr²", "x = (-b ± √D) / 2a", "E = mc²", "v = s/t" }
        };

        foreach (var sym in symbols)
        {
            var btn = new Button
            {
                Content = sym,
                Style = (Style)FindResource("VseosvitaTopActionBtn"),
                Margin = new Thickness(2),
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 13,
                FontFamily = new FontFamily("Cambria Math, Segoe UI"),
                Tag = sym
            };
            btn.Click += (s, ev) =>
            {
                if (FormulaInputBox != null)
                {
                    FormulaInputBox.SelectedText = sym;
                    FormulaInputBox.CaretIndex += sym.Length;
                    FormulaInputBox.Focus();
                }
            };
            FormulaSymbolsPalette.Children.Add(btn);
        }
    }

    private void FormulaInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (FormulaPreviewBlock != null && FormulaInputBox != null)
        {
            FormulaPreviewBlock.Text = string.IsNullOrEmpty(FormulaInputBox.Text) ? "Формула" : FormulaInputBox.Text;
        }
    }

    private void ClearFormulaInput_Click(object sender, RoutedEventArgs e)
    {
        if (FormulaInputBox != null) FormulaInputBox.Text = "";
    }

    private void InsertFormulaToCanvas_Click(object sender, RoutedEventArgs e)
    {
        string formula = FormulaInputBox?.Text ?? "";
        if (string.IsNullOrWhiteSpace(formula)) formula = "y = ax² + bx + c";

        int targetRow = GetLowestOccupiedNotebookRow() + 1;
        double fontSize = LineHeight * 0.84;
        double baselineOffset = fontSize * 1.875;
        double targetTop = Math.Max(0, (targetRow + 1) * LineHeight - baselineOffset);
        double targetLeft = Math.Max(95.0, (MainCanvas.ActualWidth - 300) / 2);

        var mathFont = new FontFamily("Cambria Math, Segoe UI");
        var element = CreateDraggablePropysyText(formula, _currentPenColor, fontSize, mathFont);
        InkCanvas.SetLeft(element, targetLeft);
        InkCanvas.SetTop(element, targetTop);
        MainCanvas.Children.Add(element);

        _undoStack.Push(new ElementAddedAction(element, MainCanvas));
        _redoStack.Clear();
        UpdateUndoRedoButtons();
        ScheduleAutoSave();

        if (FormulaEditorModal != null) FormulaEditorModal.Visibility = Visibility.Collapsed;
        ShowAiStatus("Формулу вставлено як текст");
    }

    private void MathTrainerBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenColumnArithmeticModal("Add");
    }

    private void MathTrainerItem_Click(object sender, RoutedEventArgs e)
    {
        if (MathTrainerPopup != null) MathTrainerPopup.IsOpen = false;
        if (sender is FrameworkElement fe && fe.Tag is string tag)
        {
            if (tag is "Add" or "Sub" or "Mul" or "Div")
            {
                OpenColumnArithmeticModal(tag);
            }
            else if (tag == "FractionCircle")
            {
                double posX = Math.Max(50, (MainCanvas.ActualWidth - 280) / 2);
                double posY = Math.Max(100, (MainCanvas.ActualHeight - 260) / 2);
                CreateFractionCircleWidget(posX, posY);
            }
            else
            {
                double posX = Math.Max(50, (MainCanvas.ActualWidth - 280) / 2);
                double posY = Math.Max(100, (MainCanvas.ActualHeight - 260) / 2);
                CreateFractionEquationWidget(posX, posY);
            }
        }
    }

    private string _selectedColumnOp = "Add";

    private void OpenColumnArithmeticModal(string op = "Add")
    {
        _selectedColumnOp = op;
        if (ColumnArithmeticModal == null) return;

        UpdateColumnOpButtonsUi();

        string initialNum1 = "";
        string initialNum2 = "";
        if (_activeNotebookTextBox != null && !string.IsNullOrWhiteSpace(_activeNotebookTextBox.Text))
        {
            string src = _activeNotebookTextBox.SelectionLength > 0 ? _activeNotebookTextBox.SelectedText : _activeNotebookTextBox.Text;
            var match = System.Text.RegularExpressions.Regex.Match(src, @"(\d+)\s*([\+\-\*\/−×÷])\s*(\d+)");
            if (match.Success)
            {
                initialNum1 = match.Groups[1].Value;
                initialNum2 = match.Groups[3].Value;
                string opChar = match.Groups[2].Value;
                _selectedColumnOp = opChar switch
                {
                    "+" => "Add",
                    "-" or "−" => "Sub",
                    "*" or "×" => "Mul",
                    "/" or "÷" or ":" => "Div",
                    _ => "Add"
                };
                UpdateColumnOpButtonsUi();
            }
        }

        if (ColumnNum1Box != null) ColumnNum1Box.Text = initialNum1;
        if (ColumnNum2Box != null) ColumnNum2Box.Text = initialNum2;
        UpdateColumnPreviewUi();

        ColumnArithmeticModal.Visibility = Visibility.Visible;
        if (string.IsNullOrEmpty(initialNum1)) ColumnNum1Box?.Focus();
        else ColumnNum2Box?.Focus();
    }

    private void CloseColumnModal_Click(object sender, RoutedEventArgs e)
    {
        if (ColumnArithmeticModal != null)
        {
            ColumnArithmeticModal.Visibility = Visibility.Collapsed;
        }
    }

    private void ColOpSelect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tag)
        {
            _selectedColumnOp = tag;
            UpdateColumnOpButtonsUi();
            UpdateColumnPreviewUi();
        }
    }

    private void UpdateColumnOpButtonsUi()
    {
        var activeBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        var inactiveBrush = new SolidColorBrush(Color.FromRgb(241, 245, 249));

        if (ColOpAddBtn != null)
        {
            bool sel = _selectedColumnOp == "Add";
            ColOpAddBtn.Background = sel ? activeBrush : inactiveBrush;
            ColOpAddBtn.Foreground = sel ? Brushes.White : new SolidColorBrush(Color.FromRgb(22, 163, 74));
        }
        if (ColOpSubBtn != null)
        {
            bool sel = _selectedColumnOp == "Sub";
            ColOpSubBtn.Background = sel ? activeBrush : inactiveBrush;
            ColOpSubBtn.Foreground = sel ? Brushes.White : new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }
        if (ColOpMulBtn != null)
        {
            bool sel = _selectedColumnOp == "Mul";
            ColOpMulBtn.Background = sel ? activeBrush : inactiveBrush;
            ColOpMulBtn.Foreground = sel ? Brushes.White : new SolidColorBrush(Color.FromRgb(37, 99, 235));
        }
        if (ColOpDivBtn != null)
        {
            bool sel = _selectedColumnOp == "Div";
            ColOpDivBtn.Background = sel ? activeBrush : inactiveBrush;
            ColOpDivBtn.Foreground = sel ? Brushes.White : new SolidColorBrush(Color.FromRgb(147, 51, 234));
        }
    }

    private void ColumnInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateColumnPreviewUi();
    }

    private void ColumnAnswerCheck_Changed(object sender, RoutedEventArgs e)
    {
        UpdateColumnPreviewUi();
    }

    private void ClearColumnInputs_Click(object sender, RoutedEventArgs e)
    {
        if (ColumnNum1Box != null) ColumnNum1Box.Text = "";
        if (ColumnNum2Box != null) ColumnNum2Box.Text = "";
        UpdateColumnPreviewUi();
        ColumnNum1Box?.Focus();
    }

    private string FormatColumnCalculation(string s1, string s2, string op, bool showAns)
    {
        string opSym = op switch { "Add" => "+", "Sub" => "−", "Mul" => "×", "Div" => "÷", _ => "+" };
        if (string.IsNullOrWhiteSpace(s1)) s1 = "345";
        if (string.IsNullOrWhiteSpace(s2)) s2 = "128";

        s1 = s1.Trim();
        s2 = s2.Trim();

        if (op == "Div")
        {
            if (long.TryParse(s1, out long a) && long.TryParse(s2, out long b) && b > 0)
            {
                long q = a / b;
                long r = a % b;
                if (showAns)
                {
                    return r > 0 ? $"{s1} : {s2} = {q} (ост. {r})" : $"{s1} : {s2} = {q}";
                }
                else
                {
                    return $"{s1} : {s2} = ?";
                }
            }
            return $"{s1} : {s2} = ?";
        }

        bool parsed1 = long.TryParse(s1, out long n1);
        bool parsed2 = long.TryParse(s2, out long n2);
        string resultStr = "";
        if (parsed1 && parsed2)
        {
            long res = op == "Add" ? (n1 + n2) : op == "Sub" ? (n1 - n2) : (n1 * n2);
            resultStr = res.ToString();
        }

        int maxLen = Math.Max(s1.Length, s2.Length);
        if (showAns && !string.IsNullOrEmpty(resultStr))
        {
            maxLen = Math.Max(maxLen, resultStr.Length);
        }

        int totalWidth = maxLen + 2;
        string line1 = s1.PadLeft(totalWidth);
        string line2 = opSym + " " + s2.PadLeft(totalWidth - 2);
        string divLine = new string('—', totalWidth);
        string line3 = showAns && !string.IsNullOrEmpty(resultStr) ? resultStr.PadLeft(totalWidth) : "";

        if (showAns && !string.IsNullOrEmpty(resultStr))
        {
            return $"{line1}\n{line2}\n{divLine}\n{line3}";
        }
        else
        {
            return $"{line1}\n{line2}\n{divLine}";
        }
    }

    private void UpdateColumnPreviewUi()
    {
        if (ColumnPreviewBlock == null) return;
        string s1 = ColumnNum1Box?.Text ?? "";
        string s2 = ColumnNum2Box?.Text ?? "";
        bool showAns = ColumnIncludeAnswerCheck?.IsChecked == true;
        ColumnPreviewBlock.Text = FormatColumnCalculation(s1, s2, _selectedColumnOp, showAns);
    }

    private void InsertColumnModalBtn_Click(object sender, RoutedEventArgs e)
    {
        string s1 = ColumnNum1Box?.Text?.Trim() ?? "";
        string s2 = ColumnNum2Box?.Text?.Trim() ?? "";
        bool showAns = ColumnIncludeAnswerCheck?.IsChecked == true;

        if (string.IsNullOrWhiteSpace(s1) && string.IsNullOrWhiteSpace(s2))
        {
            ShowAiStatus("Введіть числа для стовпчика");
            ColumnNum1Box?.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(s1)) s1 = "0";
        if (string.IsNullOrWhiteSpace(s2)) s2 = "0";

        string formattedText = FormatColumnCalculation(s1, s2, _selectedColumnOp, showAns);

        int targetRow = GetLowestOccupiedNotebookRow() + 1;
        double fontSize = LineHeight * 0.76;
        double baselineOffset = fontSize * 1.875;
        double targetTop = Math.Max(0, (targetRow + 1) * LineHeight - baselineOffset);
        double targetLeft = Math.Max(95.0, (MainCanvas.ActualWidth - 200) / 2);

        var element = CreateDraggablePropysyText(formattedText, _currentPenColor, fontSize, new FontFamily("Consolas, Segoe UI"));
        InkCanvas.SetLeft(element, targetLeft);
        InkCanvas.SetTop(element, targetTop);
        MainCanvas.Children.Add(element);

        _undoStack.Push(new ElementAddedAction(element, MainCanvas));
        _redoStack.Clear();
        UpdateUndoRedoButtons();
        ScheduleAutoSave();

        if (ColumnArithmeticModal != null)
        {
            ColumnArithmeticModal.Visibility = Visibility.Collapsed;
        }

        ShowAiStatus("Стовпчик вставлено у зошит");
    }


    private void CreateColumnArithmeticWidget(string op, double posX, double posY)
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(12),
            MinWidth = 240,
            Cursor = Cursors.Arrow,
            SnapsToDevicePixels = true,
            Tag = "InteractiveWidget"
        };
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Color.FromRgb(15, 23, 42),
            BlurRadius = 18,
            ShadowDepth = 3,
            Opacity = 0.12
        };

        var rootSp = new StackPanel();

        // 1. Шапка з перетягуванням та кнопкою закриття
        var header = new DockPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            Height = 34,
            Cursor = Cursors.SizeAll
        };

        string opTitle = op switch
        {
            "Add" => "➕ Додавання в стовпчик",
            "Sub" => "➖ Віднімання в стовпчик",
            "Mul" => "✖️ Множення в стовпчик",
            "Div" => "➗ Ділення куточком",
            _ => "Дія в стовпчик"
        };

        var titleBlock = new TextBlock
        {
            Text = opTitle,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        DockPanel.SetDock(titleBlock, Dock.Left);
        header.Children.Add(titleBlock);

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 28,
            Height = 24,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 4, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        closeBtn.Click += (_, _) =>
        {
            MainCanvas.Children.Remove(card);
            _undoStack.Push(new ElementRemovedAction(card, MainCanvas));
            UpdateUndoRedoButtons();
            ScheduleAutoSave();
        };
        DockPanel.SetDock(closeBtn, Dock.Right);
        header.Children.Add(closeBtn);
        rootSp.Children.Add(header);

        // Перетягування віджети через заголовок
        Point? dragStart = null;
        header.MouseLeftButtonDown += (s, ev) =>
        {
            header.CaptureMouse();
            dragStart = ev.GetPosition(MainCanvas);
            ev.Handled = true;
        };
        header.MouseMove += (s, ev) =>
        {
            if (dragStart.HasValue)
            {
                var cur = ev.GetPosition(MainCanvas);
                double l = InkCanvas.GetLeft(card);
                double t = InkCanvas.GetTop(card);
                if (double.IsNaN(l)) l = 0;
                if (double.IsNaN(t)) t = 0;
                InkCanvas.SetLeft(card, l + (cur.X - dragStart.Value.X));
                InkCanvas.SetTop(card, t + (cur.Y - dragStart.Value.Y));
                dragStart = cur;
                ev.Handled = true;
            }
        };
        header.MouseLeftButtonUp += (s, ev) =>
        {
            header.ReleaseMouseCapture();
            dragStart = null;
            ev.Handled = true;
        };

        // 2. Тіло стовпчика з живим редагуванням цифр користувачем
        var body = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };

        string defNum1 = op == "Add" ? "345" : op == "Sub" ? "524" : op == "Mul" ? "46" : "864";
        string defNum2 = op == "Add" ? "128" : op == "Sub" ? "179" : op == "Mul" ? "23" : "4";
        string opSym = op == "Add" ? "+" : op == "Sub" ? "−" : op == "Mul" ? "×" : "÷";

        if (op is "Add" or "Sub" or "Mul")
        {
            var carryText = new TextBlock
            {
                Text = "",
                FontSize = 12,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                TextAlignment = TextAlignment.Right,
                Height = 16,
                Margin = new Thickness(0, 0, 4, 0)
            };
            body.Children.Add(carryText);

            var num1Box = new TextBox
            {
                Text = defNum1,
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                TextAlignment = TextAlignment.Right,
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 203, 213, 225)),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Padding = new Thickness(4, 1, 4, 1)
            };
            body.Children.Add(num1Box);

            var row2Grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Color opColor = op == "Add" ? Color.FromRgb(22, 163, 74) : op == "Sub" ? Color.FromRgb(220, 38, 38) : Color.FromRgb(37, 99, 235);
            var opLabel = new TextBlock
            {
                Text = opSym,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(opColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 8, 0)
            };
            Grid.SetColumn(opLabel, 0);
            row2Grid.Children.Add(opLabel);

            var num2Box = new TextBox
            {
                Text = defNum2,
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                TextAlignment = TextAlignment.Right,
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 203, 213, 225)),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Padding = new Thickness(4, 1, 4, 1)
            };
            Grid.SetColumn(num2Box, 1);
            row2Grid.Children.Add(num2Box);
            body.Children.Add(row2Grid);

            var line = new System.Windows.Shapes.Rectangle
            {
                Height = 2.5,
                Fill = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Margin = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
            };
            body.Children.Add(line);

            var mulStepsPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 2) };
            body.Children.Add(mulStepsPanel);

            var resultBox = new TextBox
            {
                Text = "",
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                TextAlignment = TextAlignment.Right,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                IsReadOnly = false,
                Padding = new Thickness(4, 1, 4, 1)
            };
            body.Children.Add(resultBox);

            bool showAnswer = true;

            Action updateCalculation = () =>
            {
                string s1 = num1Box.Text.Trim();
                string s2 = num2Box.Text.Trim();
                if (long.TryParse(s1, out long n1) && long.TryParse(s2, out long n2))
                {
                    long res = op == "Add" ? (n1 + n2) : op == "Sub" ? (n1 - n2) : (n1 * n2);
                    if (showAnswer)
                    {
                        resultBox.Text = res.ToString();
                        resultBox.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
                    }
                    else
                    {
                        resultBox.Text = "???";
                        resultBox.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                    }

                    if (op == "Add")
                    {
                        // Обчислення перенесень розрядів
                        var carries = new StringBuilder();
                        long rem = 0;
                        string rev1 = new string(s1.Reverse().ToArray());
                        string rev2 = new string(s2.Reverse().ToArray());
                        int maxLen = Math.Max(rev1.Length, rev2.Length);
                        for (int i = 0; i < maxLen; i++)
                        {
                            int d1 = i < rev1.Length ? (rev1[i] - '0') : 0;
                            int d2 = i < rev2.Length ? (rev2[i] - '0') : 0;
                            long sum = d1 + d2 + rem;
                            rem = sum / 10;
                            carries.Insert(0, rem > 0 ? $"{rem}" : " ");
                        }
                        carryText.Text = carries.ToString().TrimEnd().Length > 0 ? carries.ToString() : "";
                    }
                    else
                    {
                        carryText.Text = "";
                    }

                    if (op == "Mul" && s2.Length > 1 && n2 > 0)
                    {
                        mulStepsPanel.Children.Clear();
                        mulStepsPanel.Visibility = Visibility.Visible;
                        string rev2 = new string(s2.Reverse().ToArray());
                        for (int i = 0; i < rev2.Length; i++)
                        {
                            int d = rev2[i] - '0';
                            long partial = n1 * d;
                            string shift = new string(' ', i);
                            mulStepsPanel.Children.Add(new TextBlock
                            {
                                Text = $"{partial}{shift}",
                                FontSize = 20,
                                FontFamily = new FontFamily("Consolas"),
                                TextAlignment = TextAlignment.Right,
                                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105))
                            });
                        }
                    }
                    else
                    {
                        mulStepsPanel.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    resultBox.Text = "";
                    carryText.Text = "";
                    mulStepsPanel.Visibility = Visibility.Collapsed;
                }
            };

            num1Box.TextChanged += (_, _) => updateCalculation();
            num2Box.TextChanged += (_, _) => updateCalculation();
            updateCalculation();

            // Панель інструментів стовпчика: перемикач відповіді та вставка
            var toolsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };

            var toggleAnsBtn = new Button
            {
                Content = "👁️ Відповідь",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                Cursor = Cursors.Hand
            };
            toggleAnsBtn.Click += (_, _) =>
            {
                showAnswer = !showAnswer;
                toggleAnsBtn.Content = showAnswer ? "👁️ Відповідь" : "🙈 Приховати";
                updateCalculation();
            };
            toolsPanel.Children.Add(toggleAnsBtn);

            var stampBtn = new Button
            {
                Content = "📝 Вставити на дошку",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(10, 3, 10, 3),
                Background = new SolidColorBrush(Color.FromRgb(34, 178, 76)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            stampBtn.Click += (_, _) =>
            {
                InsertColumnCalculationToBoard(num1Box.Text, opSym, num2Box.Text, resultBox.Text, posX, posY);
                MainCanvas.Children.Remove(card);
            };
            toolsPanel.Children.Add(stampBtn);

            body.Children.Add(toolsPanel);
        }
        else // Div (Ділення куточком)
        {
            var divInputGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            divInputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            divInputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            divInputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            var div1Box = new TextBox
            {
                Text = defNum1,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"),
                TextAlignment = TextAlignment.Right,
                Padding = new Thickness(4)
            };
            Grid.SetColumn(div1Box, 0);
            divInputGrid.Children.Add(div1Box);

            var colon = new TextBlock
            {
                Text = " : ",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 6, 0)
            };
            Grid.SetColumn(colon, 1);
            divInputGrid.Children.Add(colon);

            var div2Box = new TextBox
            {
                Text = defNum2,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"),
                Padding = new Thickness(4)
            };
            Grid.SetColumn(div2Box, 2);
            divInputGrid.Children.Add(div2Box);
            body.Children.Add(divInputGrid);

            var divResultText = new TextBlock
            {
                FontSize = 16,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Margin = new Thickness(4, 4, 4, 8)
            };
            body.Children.Add(divResultText);

            Action updateDivision = () =>
            {
                if (long.TryParse(div1Box.Text.Trim(), out long a) && long.TryParse(div2Box.Text.Trim(), out long b) && b > 0)
                {
                    long q = a / b;
                    long r = a % b;
                    divResultText.Text = $"Частка: {q}" + (r > 0 ? $" (остача {r})" : " (без остачі)");
                }
                else
                {
                    divResultText.Text = "Введіть ділене і дільник";
                }
            };

            div1Box.TextChanged += (_, _) => updateDivision();
            div2Box.TextChanged += (_, _) => updateDivision();
            updateDivision();
        }

        rootSp.Children.Add(body);
        card.Child = rootSp;

        InkCanvas.SetLeft(card, posX);
        InkCanvas.SetTop(card, posY);
        MainCanvas.Children.Add(card);

        _undoStack.Push(new ElementAddedAction(card, MainCanvas));
        _redoStack.Clear();
        UpdateUndoRedoButtons();
        ScheduleAutoSave();
    }

    private void InsertColumnCalculationToBoard(string n1, string op, string n2, string res, double posX, double posY)
    {
        double snappedTop = Math.Round(posY / LineHeight) * LineHeight;
        var tb = new TextBlock
        {
            Text = $"  {n1}\n{op} {n2}\n—————\n  {res}",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Consolas, Segoe UI"),
            Foreground = new SolidColorBrush(_currentPenColor),
            Tag = "MathColumnItem"
        };
        InkCanvas.SetLeft(tb, Math.Max(95.0, posX));
        InkCanvas.SetTop(tb, snappedTop);
        MainCanvas.Children.Add(tb);
        _undoStack.Push(new ElementAddedAction(tb, MainCanvas));
        UpdateUndoRedoButtons();
        ScheduleAutoSave();
        ShowAiStatus("Стовпчик вставлено на дошку");
    }

    private void CreateFractionEquationWidget(double posX, double posY)
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Cursor = Cursors.SizeAll,
            SnapsToDevicePixels = true,
            Tag = "InteractiveWidget"
        };
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(15, 23, 42), BlurRadius = 14, ShadowDepth = 2, Opacity = 0.08 };

        var sp = new StackPanel();
        var tb = new TextBlock { Text = "½ + ¼ = ¾", FontSize = 22, FontWeight = FontWeights.Bold, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        sp.Children.Add(tb);
        card.Child = sp;

        AttachSimpleWidgetDrag(card);
        InkCanvas.SetLeft(card, posX);
        InkCanvas.SetTop(card, posY);
        MainCanvas.Children.Add(card);
        _undoStack.Push(new ElementAddedAction(card, MainCanvas));
        UpdateUndoRedoButtons();
    }

    private void CreateFractionCircleWidget(double posX, double posY)
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Cursor = Cursors.SizeAll,
            SnapsToDevicePixels = true,
            Tag = "InteractiveWidget"
        };
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(15, 23, 42), BlurRadius = 14, ShadowDepth = 2, Opacity = 0.08 };

        var sp = new StackPanel();
        var p = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 50,50 L 50,10 A 40,40 0 1 1 10,50 Z"),
            Fill = new SolidColorBrush(Color.FromArgb(160, 34, 178, 76)),
            Stroke = new SolidColorBrush(Color.FromRgb(34, 178, 76)),
            StrokeThickness = 2,
            Width = 80,
            Height = 80
        };
        sp.Children.Add(p);
        sp.Children.Add(new TextBlock { Text = "¾ кола", HorizontalAlignment = System.Windows.HorizontalAlignment.Center, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0) });
        card.Child = sp;

        AttachSimpleWidgetDrag(card);
        InkCanvas.SetLeft(card, posX);
        InkCanvas.SetTop(card, posY);
        MainCanvas.Children.Add(card);
        _undoStack.Push(new ElementAddedAction(card, MainCanvas));
        UpdateUndoRedoButtons();
    }

    private void AttachSimpleWidgetDrag(FrameworkElement el)
    {
        Point? dragStart = null;
        el.MouseLeftButtonDown += (s, ev) =>
        {
            el.CaptureMouse();
            dragStart = ev.GetPosition(MainCanvas);
            ev.Handled = true;
        };
        el.MouseMove += (s, ev) =>
        {
            if (dragStart.HasValue)
            {
                var cur = ev.GetPosition(MainCanvas);
                double l = InkCanvas.GetLeft(el);
                double t = InkCanvas.GetTop(el);
                if (double.IsNaN(l)) l = 0;
                if (double.IsNaN(t)) t = 0;
                InkCanvas.SetLeft(el, l + (cur.X - dragStart.Value.X));
                InkCanvas.SetTop(el, t + (cur.Y - dragStart.Value.Y));
                dragStart = cur;
                ev.Handled = true;
            }
        };
        el.MouseLeftButtonUp += (s, ev) =>
        {
            el.ReleaseMouseCapture();
            dragStart = null;
            ev.Handled = true;
        };
    }



    private void DocsBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Документи та зображення (*.pdf;*.png;*.jpg)|*.pdf;*.png;*.jpg|Усі файли (*.*)|*.*",
            Title = "Імпортувати документ або PDF"
        };
        if (dlg.ShowDialog() == true)
        {
            if (dlg.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                ShowAiStatus("Імпортовано PDF-документ");
            }
            else
            {
                InsertImageFromFile(dlg.FileName, 0, 0);
            }
        }
    }

    private async void VoiceBtn_Click(object sender, RoutedEventArgs e)
    {
        await ToggleVoiceDictationAsync();
    }

    private async void RightDockAiConvertBtn_Click(object sender, RoutedEventArgs e)
    {
        bool isNowChecked = RightDockAiConvertBtn.IsChecked ?? false;
        _isAutoPropysyEnabled = isNowChecked;

        if (isNowChecked)
        {
            if (RightDockAiConvertLabel != null) RightDockAiConvertLabel.Text = "Автопропис";
            RightDockAiConvertBtn.ToolTip = "Автопропис: УВІМК (Рукописне введення автоматично перетворюється на пропис при паузі в письмі)";
            ShowAiStatus("Автопропис: УВІМК ✨");

            // Если на доске уже есть штрихи, сразу оцифровываем их в каллиграфическую пропись!
            if (MainCanvas.Strokes.Count > 0 && !_isProcessingOcr)
            {
                var selectedStrokes = MainCanvas.GetSelectedStrokes();
                var toConvert = selectedStrokes.Count > 0
                    ? new StrokeCollection(selectedStrokes)
                    : new StrokeCollection(MainCanvas.Strokes);

                _currentWordStrokes.Clear();
                _recentDrawnStrokes.Clear();
                _isProcessingOcr = true;
                try
                {
                    await ProcessAndSnapInkAsync(toConvert);
                }
                finally
                {
                    _isProcessingOcr = false;
                }
            }
        }
        else
        {
            if (RightDockAiConvertLabel != null) RightDockAiConvertLabel.Text = "У пропис";
            RightDockAiConvertBtn.ToolTip = "Автопропис: ВИМК (Натисніть, щоб увімкнути автоматичне перетворення у пропис)";
            _inactivityTimer.Stop();
            _currentWordStrokes.Clear();
            ShowAiStatus("Автопропис: ВИМК (ручний режим)");
        }
    }

    private Windows.Media.SpeechRecognition.SpeechRecognizer? _speechRecognizer;
    private bool _isVoiceDictating = false;
    private int _voiceCurrentRow = 3;
    private double _voiceCurrentLeft = 95.0;

    private static string ProcessDictatedPunctuation(string rawText, out bool isNewParagraph)
    {
        isNewParagraph = false;
        if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;

        string text = rawText.Trim();

        if (text.Equals("новий рядок", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("абзац", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("з нового рядка", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("наступний рядок", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("перенос", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("новая строка", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("с новой строки", StringComparison.OrdinalIgnoreCase))
        {
            isNewParagraph = true;
            return string.Empty;
        }

        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(кома|запятая)\b", ",", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(крапка|точка)\b", ".", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(знак оклику|восклицательный знак)\b", "!", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(знак питання|вопросительный знак)\b", "?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(двокрапка|двоеточие)\b", ":", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(крапка з комою|точка с запятой)\b", ";", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(тире|дефіс)\b", " — ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(дужка відкривається|відкрити дужку)\b", " (", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(дужка закривається|закрити дужку)\b", ") ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(лапки відкриваються|відкрити лапки)\b", " «", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(лапки закриваються|закрити лапки)\b", "» ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(плюс)\b", " + ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(мінус)\b", " − ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(дорівнює)\b", " = ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+([,\.!?:;»\)])", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"([«\(])\s+", "$1");

        return text;
    }

    private int GetLowestOccupiedNotebookRow()
    {
        int maxRow = 1;
        if (NotebookHeaderContainer.Visibility == Visibility.Visible)
        {
            maxRow = 2;
        }

        foreach (UIElement child in MainCanvas.Children)
        {
            double top = InkCanvas.GetTop(child);
            if (!double.IsNaN(top) && top > 0)
            {
                double h = (child is FrameworkElement fe && fe.ActualHeight > 0) ? fe.ActualHeight : 30.0;
                int row = (int)Math.Round((top + h * 0.5) / LineHeight);
                if (row > maxRow) maxRow = row;
            }
        }

        foreach (var stroke in MainCanvas.Strokes)
        {
            var b = stroke.GetBounds();
            if (!b.IsEmpty && b.Bottom > 0)
            {
                int row = (int)Math.Round(b.Bottom / LineHeight);
                if (row > maxRow) maxRow = row;
            }
        }

        return maxRow;
    }

    private void AppendVoiceDictationText(string rawText)
    {
        Dispatcher.Invoke(() =>
        {
            bool isNewParagraph;
            string processed = ProcessDictatedPunctuation(rawText, out isNewParagraph);

            if (isNewParagraph)
            {
                _voiceCurrentRow++;
                _voiceCurrentLeft = 95.0;
                return;
            }

            if (string.IsNullOrWhiteSpace(processed)) return;

            if (_voiceCurrentLeft <= 96.0 && processed.Length > 0 && char.IsLetter(processed[0]))
            {
                processed = char.ToUpper(processed[0]) + (processed.Length > 1 ? processed.Substring(1) : "");
            }

            double fontSize = LineHeight * 0.84;
            double baselineOffset = fontSize * 1.875;
            double rightMargin = Math.Max(500.0, MainCanvas.ActualWidth > 0 ? MainCanvas.ActualWidth - 65.0 : 900.0);

            var ft = new FormattedText(
                processed,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface(_propysyFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                fontSize,
                Brushes.Black,
                1.0);

            double textWidth = ft.WidthIncludingTrailingWhitespace;

            if (_voiceCurrentLeft + textWidth > rightMargin)
            {
                _voiceCurrentRow++;
                _voiceCurrentLeft = 95.0;
            }

            double top = Math.Max(0, (_voiceCurrentRow + 1) * LineHeight - baselineOffset);
            double left = _voiceCurrentLeft;

            var textElem = CreateDraggablePropysyText(processed, _currentPenColor, fontSize);
            InkCanvas.SetLeft(textElem, left);
            InkCanvas.SetTop(textElem, top);
            MainCanvas.Children.Add(textElem);

            _voiceCurrentLeft += textWidth + 12.0;
            ScheduleAutoSave();
        });
    }

    private void UpdateVoiceButtonUi(bool active)
    {
        if (VoiceBtn == null) return;
        if (active)
        {
            VoiceBtn.Background = new SolidColorBrush(Color.FromArgb(60, 239, 68, 68));
            VoiceBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        }
        else
        {
            VoiceBtn.Background = Brushes.Transparent;
            VoiceBtn.BorderBrush = Brushes.Transparent;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    private static void TriggerWindowsVoiceTyping()
    {
        try
        {
            const byte VK_LWIN = 0x5B;
            const byte VK_H = 0x48;
            const uint KEYEVENTF_KEYUP = 0x0002;
            keybd_event(VK_LWIN, 0, 0, 0);
            keybd_event(VK_H, 0, 0, 0);
            keybd_event(VK_H, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, 0);
        }
        catch { }
    }

    private void OnWhiteboardDictationReceived(string text)
    {
        Dispatcher.Invoke(() =>
        {
            AppendVoiceDictationText(text);
        });
    }

    private async Task ToggleVoiceDictationAsync()
    {
        if (_isVoiceDictating)
        {
            _isVoiceDictating = false;
            try
            {
                var win = Window.GetWindow(this) as MainWindow;
                if (win?.VoiceService != null)
                {
                    win.VoiceService.SetWhiteboardDictation(false);
                    win.VoiceService.WhiteboardDictationReceived -= OnWhiteboardDictationReceived;
                }
            }
            catch { }

            try
            {
                if (_speechRecognizer != null)
                {
                    await _speechRecognizer.ContinuousRecognitionSession.StopAsync();
                }
            }
            catch { }

            ShowAiStatus("Голосовий запис вимкнено");
            UpdateVoiceButtonUi(false);
            return;
        }

        try
        {
            _voiceCurrentRow = Math.Max(2, GetLowestOccupiedNotebookRow() + 1);
            _voiceCurrentLeft = 95.0;

            var mainWindow = Window.GetWindow(this) as MainWindow;
            bool usedVoiceService = false;

            if (mainWindow?.VoiceService != null)
            {
                try
                {
                    mainWindow.VoiceService.WhiteboardDictationReceived -= OnWhiteboardDictationReceived;
                    mainWindow.VoiceService.WhiteboardDictationReceived += OnWhiteboardDictationReceived;
                    mainWindow.VoiceService.SetWhiteboardDictation(true, "uk-UA");
                    usedVoiceService = true;
                }
                catch (Exception ex)
                {
                    App.Log(ex);
                }
            }

            if (!usedVoiceService)
            {
                bool hasTopicLangs = false;
                try
                {
                    hasTopicLangs = Windows.Media.SpeechRecognition.SpeechRecognizer.SupportedTopicLanguages.Count > 0;
                }
                catch { }

                if (hasTopicLangs)
                {
                    if (_speechRecognizer == null)
                    {
                        Windows.Globalization.Language? speechLang = null;
                        foreach (var l in Windows.Media.SpeechRecognition.SpeechRecognizer.SupportedTopicLanguages)
                        {
                            if (l.LanguageTag.StartsWith("uk", StringComparison.OrdinalIgnoreCase) ||
                                l.LanguageTag.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
                            {
                                speechLang = l;
                                break;
                            }
                        }
                        speechLang ??= Windows.Media.SpeechRecognition.SpeechRecognizer.SystemSpeechLanguage;
                        _speechRecognizer = new Windows.Media.SpeechRecognition.SpeechRecognizer(speechLang);
                        await _speechRecognizer.CompileConstraintsAsync();

                        _speechRecognizer.ContinuousRecognitionSession.ResultGenerated += (sender, args) =>
                        {
                            if (args.Result?.Text != null && !string.IsNullOrWhiteSpace(args.Result.Text))
                            {
                                AppendVoiceDictationText(args.Result.Text);
                            }
                        };

                        _speechRecognizer.ContinuousRecognitionSession.Completed += (sender, args) =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                _isVoiceDictating = false;
                                UpdateVoiceButtonUi(false);
                            });
                        };
                    }

                    await _speechRecognizer.ContinuousRecognitionSession.StartAsync();
                }
                else
                {
                    TriggerWindowsVoiceTyping();
                }
            }

            _isVoiceDictating = true;
            UpdateVoiceButtonUi(true);
            ShowAiStatus("🎙️ Голосовий запис увімкнено. Говоріть...");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            _isVoiceDictating = false;
            UpdateVoiceButtonUi(false);
            ShowAiStatus("Мікрофон: говоріть або скористайтесь Win+H");
        }
    }

private void LinkBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowAiStatus("Вставка посилання доступна");
    }

    private void WebEmbedBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowAiStatus("Вставка веб-сторінки доступна");
    }

    private void CropBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowAiStatus("Виділіть область для копіювання");
    }

    private void SelectLassoBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTool(WhiteboardTool.Pointer);
    }

    private void WhiteboardMenuBtn_Click(object sender, RoutedEventArgs e)
    {
        if (WhiteboardMenuPopup != null)
        {
            WhiteboardMenuPopup.IsOpen = !WhiteboardMenuPopup.IsOpen;
        }
    }

    private void ZoomBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ZoomPopup != null)
        {
            ZoomPopup.IsOpen = !ZoomPopup.IsOpen;
        }
    }

    private void ZoomSelect_Click(object sender, RoutedEventArgs e)
    {
        if (ZoomPopup != null) ZoomPopup.IsOpen = false;
        if (sender is FrameworkElement fe && fe.Tag is string tag && double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out double sc))
        {
            MainCanvas.LayoutTransform = new ScaleTransform(sc, sc);
            if (ZoomLevelText != null) ZoomLevelText.Text = $"{(int)(sc * 100)}%";
        }
    }

    private bool _toolbarCollapsed = false;
    private void MoreBtn_Click(object sender, RoutedEventArgs e)
    {
        _toolbarCollapsed = !_toolbarCollapsed;
        ShowAiStatus(_toolbarCollapsed ? "Панель згорнуто" : "Панель розгорнуто");
    }

    private void FontBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTool(WhiteboardTool.Text);
    }

    private void FontBtnTextMode_Click(object sender, RoutedEventArgs e)
    {
        if (FontFormatPopup != null)
        {
            FontFormatPopup.IsOpen = !FontFormatPopup.IsOpen;
        }
    }

    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Font family change
    }

    private void FontSize_Click(object sender, RoutedEventArgs e)
    {
        // Font size click
    }

    private void FontBoldToggle_Click(object sender, RoutedEventArgs e)
    {
        // Font bold toggle
    }

    private void FontItalicToggle_Click(object sender, RoutedEventArgs e)
    {
        // Font italic toggle
    }

    private void FontUnderlineToggle_Click(object sender, RoutedEventArgs e)
    {
        // Font underline toggle
    }

    private void AlignLeftBtn_Click(object sender, RoutedEventArgs e)
    {
        // Align left
    }

    private void AlignCenterBtn_Click(object sender, RoutedEventArgs e)
    {
        // Align center
    }

    private void AlignRightBtn_Click(object sender, RoutedEventArgs e)
    {
        // Align right
    }

    private void BulletListBtn_Click(object sender, RoutedEventArgs e)
    {
        // Bullet list
    }

    private void NumberListBtn_Click(object sender, RoutedEventArgs e)
    {
        // Number list
    }

    private void IndentBtn_Click(object sender, RoutedEventArgs e)
    {
        // Indent
    }

    private void DateStampBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DateStampPopup != null)
        {
            DateStampPopup.IsOpen = !DateStampPopup.IsOpen;
        }
    }

    private void DateStampItem_Click(object sender, RoutedEventArgs e)
    {
        if (DateStampPopup != null) DateStampPopup.IsOpen = false;
        if (sender is FrameworkElement fe && fe.Tag is string tag)
        {
            string dateStr = tag switch
            {
                "WordDate" => "Двадцять сьоме жовтня",
                "ShortDate" => "27 жовтня",
                "DotDate" => "27.10.2026",
                "ClassWork" => "Класна робота",
                "HomeWork" => "Домашня робота",
                _ => DateTime.Now.ToString("dd.MM.yyyy")
            };

            if (tag == "WordDate" || tag == "ShortDate" || tag == "DotDate")
            {
                if (HeaderDateBox != null) HeaderDateBox.Text = dateStr;
            }
            else
            {
                if (HeaderWorkTypeBox != null) HeaderWorkTypeBox.Text = dateStr;
            }
            if (NotebookHeaderContainer != null) NotebookHeaderContainer.Visibility = Visibility.Visible;
            ScheduleAutoSave();
        }
    }

}

public interface IWhiteboardAction
{
    void Undo();
    void Redo();
}

public class StrokesAddedAction : IWhiteboardAction
{
    private readonly List<Stroke> _strokes;
    private readonly InkCanvas _canvas;

    public StrokesAddedAction(IEnumerable<Stroke> strokes, InkCanvas canvas)
    {
        _strokes = strokes.ToList();
        _canvas = canvas;
    }

    public void Undo()
    {
        foreach (var s in _strokes)
        {
            if (_canvas.Strokes.Contains(s)) _canvas.Strokes.Remove(s);
        }
    }

    public void Redo()
    {
        foreach (var s in _strokes)
        {
            if (!_canvas.Strokes.Contains(s)) _canvas.Strokes.Add(s);
        }
    }
}

public class StrokesRemovedAction : IWhiteboardAction
{
    private readonly List<Stroke> _strokes;
    private readonly InkCanvas _canvas;

    public StrokesRemovedAction(IEnumerable<Stroke> strokes, InkCanvas canvas)
    {
        _strokes = strokes.ToList();
        _canvas = canvas;
    }

    public void Undo()
    {
        foreach (var s in _strokes)
        {
            if (!_canvas.Strokes.Contains(s)) _canvas.Strokes.Add(s);
        }
    }

    public void Redo()
    {
        foreach (var s in _strokes)
        {
            if (_canvas.Strokes.Contains(s)) _canvas.Strokes.Remove(s);
        }
    }
}

public class ElementAddedAction : IWhiteboardAction
{
    private readonly UIElement _element;
    private readonly InkCanvas _canvas;

    public ElementAddedAction(UIElement element, InkCanvas canvas)
    {
        _element = element;
        _canvas = canvas;
    }

    public void Undo()
    {
        if (_canvas.Children.Contains(_element)) _canvas.Children.Remove(_element);
    }

    public void Redo()
    {
        if (!_canvas.Children.Contains(_element)) _canvas.Children.Add(_element);
    }
}

public class ElementRemovedAction : IWhiteboardAction
{
    private readonly UIElement _element;
    private readonly InkCanvas _canvas;

    public ElementRemovedAction(UIElement element, InkCanvas canvas)
    {
        _element = element;
        _canvas = canvas;
    }




    public void Undo()
    {
        if (!_canvas.Children.Contains(_element)) _canvas.Children.Add(_element);
    }

    public void Redo()
    {
        if (_canvas.Children.Contains(_element)) _canvas.Children.Remove(_element);
    }
}

public class ClearCanvasAction : IWhiteboardAction
{
    private readonly StrokeCollection _strokes;
    private readonly List<UIElement> _elements;
    private readonly InkCanvas _canvas;

    public ClearCanvasAction(StrokeCollection strokes, List<UIElement> elements, InkCanvas canvas)
    {
        _strokes = new StrokeCollection(strokes);
        _elements = new List<UIElement>(elements);
        _canvas = canvas;
    }

    public void Undo()
    {
        _canvas.Strokes.Add(_strokes);
        foreach (var el in _elements)
        {
            if (!_canvas.Children.Contains(el)) _canvas.Children.Add(el);
        }
    }

    public void Redo()
    {
        _canvas.Strokes.Clear();
        _canvas.Children.Clear();
    }
}


public class CompositeWhiteboardAction : IWhiteboardAction
{
    private readonly List<IWhiteboardAction> _actions;
    public CompositeWhiteboardAction(IEnumerable<IWhiteboardAction> actions)
    {
        _actions = actions.ToList();
    }
    public void Undo()
    {
        for (int i = _actions.Count - 1; i >= 0; i--) _actions[i].Undo();
    }
    public void Redo()
    {
        foreach (var a in _actions) a.Redo();
    }
}

// =========================================================================
// КРЕСЛЯРСЬКА ЛІНІЙКА (З ПОВНІСТЮ РОБОЧИМИ КНОПКАМИ)
// =========================================================================

public sealed class WhiteboardRulerItem : Grid
{
    private readonly RotateTransform _rotateTransform = new();
    private readonly TextBlock _angleLabel;
    private readonly Border _body;
    private readonly Button _closeBtn;
    private readonly Border _rotateHandle;
    private readonly Border _leftGrip;
    private readonly Border _rightGrip;

    private int _leftCm = 0;
    private int _rightCm = 12;

    public int LeftCm => _leftCm;
    public int RightCm => _rightCm;

    private bool _isDragging;
    private bool _isRotating;
    private bool _isResizingLeft;
    private bool _isResizingRight;
    private Point _mouseStart;
    private Point _itemStart;
    private int _startLeftCm;
    private int _startRightCm;

    public event Action? CloseRequested;
    public event Action? Changed;

    public double Angle => _rotateTransform.Angle;

    public WhiteboardRulerItem(double width = 512, double height = 64, double initialAngle = 0.0)
    {
        int initRight = (int)Math.Max(5, Math.Round((width - 32.0) / 40.0));
        _leftCm = 0;
        _rightCm = Math.Clamp(initRight, 6, 40);

        Height = 64;
        Width = ((_rightCm - _leftCm) * 40.0) + 32.0;
        ClipToBounds = false;
        Cursor = Cursors.SizeAll;

        _rotateTransform.Angle = initialAngle;
        _rotateTransform.CenterX = Width / 2.0;
        _rotateTransform.CenterY = Height / 2.0;
        RenderTransform = _rotateTransform;

        _body = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(242, 248, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(220, 148, 163, 184)),
            BorderThickness = new Thickness(1.5),
            SnapsToDevicePixels = true
        };
        _body.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 12,
            ShadowDepth = 2,
            Opacity = 0.22
        };
        Children.Add(_body);

        RebuildScaleTicks();

        // Повзунки розтягування без кнопок плюс/мінус
        _leftGrip = CreateResizeGrip(WpfHorizontalAlignment.Left, "Потягніть вліво для додавання від'ємних см (−1, −2, −3...)");
        Children.Add(_leftGrip);

        _rightGrip = CreateResizeGrip(WpfHorizontalAlignment.Right, "Потягніть вправо для збільшення довжини (+1, +2, +3...)");
        Children.Add(_rightGrip);

        // Показ кута нахилу
        _angleLabel = new TextBlock
        {
            Text = $"{Math.Round(initialAngle, 0)}°",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            VerticalAlignment = WpfVerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 48, 8),
            Cursor = Cursors.Hand,
            ToolTip = "Клацніть для перемикання 0° / 90°"
        };
        _angleLabel.PreviewMouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            _rotateTransform.Angle = (_rotateTransform.Angle == 0.0) ? 90.0 : 0.0;
            _angleLabel.Text = $"{_rotateTransform.Angle}°";
            Changed?.Invoke();
        };
        Children.Add(_angleLabel);

        // Ручка обертання
        _rotateHandle = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            VerticalAlignment = WpfVerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 22, 6),
            Cursor = Cursors.Hand,
            ToolTip = "Потягніть для обертання лінійки"
        };
        var rotateIcon = new WpfPath
        {
            Data = Geometry.Parse("M 12,4 V 1 L 8,5 L 12,9 V 6 C 15.31,6 18,8.69 18,12 C 18,13.01 17.75,13.97 17.3,14.8 L 18.76,16.26 C 19.54,15.03 20,13.57 20,12 C 20,7.58 16.42,4 12,4 Z M 12,18 C 8.69,18 6,15.31 6,12 C 6,10.99 6.25,10.03 6.7,9.2 L 5.24,7.74 C 4.46,8.97 4,10.43 4,12 C 4,16.42 7.58,20 12,20 V 23 L 16,19 L 12,15 V 18 Z"),
            Width = 11,
            Height = 11,
            Stretch = Stretch.Uniform,
            Fill = Brushes.White,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center
        };
        _rotateHandle.Child = rotateIcon;
        Children.Add(_rotateHandle);

        // Кнопка закриття (x)
        _closeBtn = new Button
        {
            Width = 18,
            Height = 18,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            VerticalAlignment = WpfVerticalAlignment.Top,
            Margin = new Thickness(0, -7, -7, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Сховати лінійку"
        };
        var delTemplate = new ControlTemplate(typeof(Button));
        var delFactory = new FrameworkElementFactory(typeof(Border));
        delFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        delFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(239, 68, 68)));
        var delIcon = new FrameworkElementFactory(typeof(WpfPath));
        delIcon.SetValue(WpfPath.DataProperty, Geometry.Parse("M 4,4 L 10,10 M 10,4 L 4,10"));
        delIcon.SetValue(WpfPath.StrokeProperty, Brushes.White);
        delIcon.SetValue(WpfPath.StrokeThicknessProperty, 1.5);
        delIcon.SetValue(WpfPath.StretchProperty, Stretch.Uniform);
        delIcon.SetValue(WpfPath.WidthProperty, 6.0);
        delIcon.SetValue(WpfPath.HeightProperty, 6.0);
        delIcon.SetValue(WpfPath.HorizontalAlignmentProperty, WpfHorizontalAlignment.Center);
        delIcon.SetValue(WpfPath.VerticalAlignmentProperty, WpfVerticalAlignment.Center);
        delFactory.AppendChild(delIcon);
        delTemplate.VisualTree = delFactory;
        _closeBtn.Template = delTemplate;
        _closeBtn.PreviewMouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            CloseRequested?.Invoke();
        };
        _closeBtn.Click += (s, e) =>
        {
            e.Handled = true;
            CloseRequested?.Invoke();
        };
        Children.Add(_closeBtn);

        SetupInteractions();
    }

    private static Border CreateResizeGrip(WpfHorizontalAlignment alignment, string tooltip)
    {
        var grip = new Border
        {
            Width = 14,
            Height = 36,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(160, 226, 232, 240)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(200, 148, 163, 184)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = alignment,
            VerticalAlignment = WpfVerticalAlignment.Center,
            Margin = (alignment == WpfHorizontalAlignment.Left) ? new Thickness(2, 0, 0, 0) : new Thickness(0, 0, 2, 0),
            Cursor = Cursors.SizeWE,
            ToolTip = tooltip
        };

        var dotsStack = new StackPanel
        {
            VerticalAlignment = WpfVerticalAlignment.Center,
            HorizontalAlignment = WpfHorizontalAlignment.Center
        };
        for (int i = 0; i < 3; i++)
        {
            dotsStack.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 2.5,
                Height = 2.5,
                Fill = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 1.5, 0, 1.5)
            });
        }
        grip.Child = dotsStack;
        return grip;
    }

    private void RebuildScaleTicks()
    {
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            if (Children[i] is Canvas)
            {
                Children.RemoveAt(i);
            }
        }

        var scaleCanvas = new Canvas { IsHitTestVisible = false };
        double x0 = 16.0 + (-_leftCm) * 40.0;

        for (int cm = _leftCm; cm <= _rightCm; cm++)
        {
            double cmX = x0 + cm * 40.0;

            int maxMm = (cm == _rightCm) ? 0 : 9;
            for (int mm = 0; mm <= maxMm; mm++)
            {
                double tickX = cmX + mm * 4.0;
                double tickH = (mm == 0) ? 24.0 : (mm == 5) ? 15.0 : 8.0;
                double tickThick = (mm == 0) ? 1.4 : 0.8;
                System.Windows.Media.Brush tickBrush = (cm == 0 && mm == 0)
                    ? new SolidColorBrush(Color.FromRgb(2, 132, 199))
                    : (cm < 0)
                        ? new SolidColorBrush(Color.FromRgb(71, 85, 105))
                        : new SolidColorBrush(Color.FromRgb(30, 41, 59));

                var tickLine = new Line
                {
                    X1 = tickX,
                    Y1 = 0,
                    X2 = tickX,
                    Y2 = tickH,
                    Stroke = tickBrush,
                    StrokeThickness = tickThick,
                    SnapsToDevicePixels = true
                };
                scaleCanvas.Children.Add(tickLine);
            }

            var numBlock = new TextBlock
            {
                Text = cm.ToString(),
                FontSize = (cm == 0) ? 11.5 : 10.5,
                FontWeight = (cm == 0) ? FontWeights.ExtraBold : FontWeights.Bold,
                Foreground = (cm == 0)
                    ? new SolidColorBrush(Color.FromRgb(2, 132, 199))
                    : (cm < 0)
                        ? new SolidColorBrush(Color.FromRgb(100, 116, 139))
                        : new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                Width = 28,
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(numBlock, cmX - 14.0);
            Canvas.SetTop(numBlock, 26.0);
            scaleCanvas.Children.Add(numBlock);
        }

        Children.Insert(1, scaleCanvas);
    }

    public void SetCmRange(int newLeftCm, int newRightCm)
    {
        if (newLeftCm == _leftCm && newRightCm == _rightCm) return;

        var parentCanvas = Parent as UIElement;
        Point pZeroBefore = new Point(0, 0);
        if (parentCanvas != null)
        {
            double curX0 = 16.0 + (-_leftCm) * 40.0;
            pZeroBefore = this.TranslatePoint(new Point(curX0, 0), parentCanvas);
        }

        _leftCm = newLeftCm;
        _rightCm = newRightCm;
        Width = ((_rightCm - _leftCm) * 40.0) + 32.0;
        _rotateTransform.CenterX = Width / 2.0;
        _rotateTransform.CenterY = Height / 2.0;

        RebuildScaleTicks();
        UpdateLayout();

        if (parentCanvas != null)
        {
            double newX0 = 16.0 + (-_leftCm) * 40.0;
            Point pZeroAfter = this.TranslatePoint(new Point(newX0, 0), parentCanvas);
            double shiftX = pZeroBefore.X - pZeroAfter.X;
            double shiftY = pZeroBefore.Y - pZeroAfter.Y;
            if (Math.Abs(shiftX) > 0.001 || Math.Abs(shiftY) > 0.001)
            {
                InkCanvas.SetLeft(this, InkCanvas.GetLeft(this) + shiftX);
                InkCanvas.SetTop(this, InkCanvas.GetTop(this) + shiftY);
            }
        }
    }

    private void SetupInteractions()
    {
        // 1. Лівий повзунок - розтягування вліво (додавання мінусових см)
        _leftGrip.PreviewMouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            _isResizingLeft = true;
            _mouseStart = e.GetPosition(Parent as IInputElement);
            _startLeftCm = _leftCm;
            _leftGrip.CaptureMouse();
        };

        _leftGrip.MouseMove += (s, e) =>
        {
            if (_isResizingLeft)
            {
                Point cur = e.GetPosition(Parent as IInputElement);
                double dx = cur.X - _mouseStart.X;
                double dy = cur.Y - _mouseStart.Y;
                double rad = _rotateTransform.Angle * Math.PI / 180.0;
                double deltaAlongRuler = dx * Math.Cos(rad) + dy * Math.Sin(rad);
                int deltaCm = (int)Math.Round(deltaAlongRuler / 40.0);
                int targetLeft = Math.Clamp(_startLeftCm + deltaCm, -40, _rightCm - 2);
                if (targetLeft != _leftCm)
                {
                    SetCmRange(targetLeft, _rightCm);
                    Changed?.Invoke();
                }
                e.Handled = true;
            }
        };

        _leftGrip.MouseLeftButtonUp += (s, e) =>
        {
            if (_isResizingLeft)
            {
                _isResizingLeft = false;
                _leftGrip.ReleaseMouseCapture();
                Changed?.Invoke();
                e.Handled = true;
            }
        };

        // 2. Правий повзунок - розтягування вправо (додавання плюсових см)
        _rightGrip.PreviewMouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            _isResizingRight = true;
            _mouseStart = e.GetPosition(Parent as IInputElement);
            _startRightCm = _rightCm;
            _rightGrip.CaptureMouse();
        };

        _rightGrip.MouseMove += (s, e) =>
        {
            if (_isResizingRight)
            {
                Point cur = e.GetPosition(Parent as IInputElement);
                double dx = cur.X - _mouseStart.X;
                double dy = cur.Y - _mouseStart.Y;
                double rad = _rotateTransform.Angle * Math.PI / 180.0;
                double deltaAlongRuler = dx * Math.Cos(rad) + dy * Math.Sin(rad);
                int deltaCm = (int)Math.Round(deltaAlongRuler / 40.0);
                int targetRight = Math.Clamp(_startRightCm + deltaCm, _leftCm + 2, 50);
                if (targetRight != _rightCm)
                {
                    SetCmRange(_leftCm, targetRight);
                    Changed?.Invoke();
                }
                e.Handled = true;
            }
        };

        _rightGrip.MouseLeftButtonUp += (s, e) =>
        {
            if (_isResizingRight)
            {
                _isResizingRight = false;
                _rightGrip.ReleaseMouseCapture();
                Changed?.Invoke();
                e.Handled = true;
            }
        };

        // 3. Ручка обертання
        _rotateHandle.MouseLeftButtonDown += (s, e) =>
        {
            _isRotating = true;
            _mouseStart = e.GetPosition(Parent as IInputElement);
            _itemStart = new Point(InkCanvas.GetLeft(this), InkCanvas.GetTop(this));
            _rotateHandle.CaptureMouse();
            e.Handled = true;
        };

        _rotateHandle.MouseMove += (s, e) =>
        {
            if (_isRotating)
            {
                Point cur = e.GetPosition(Parent as IInputElement);
                double centerCanvasX = _itemStart.X + Width / 2.0;
                double centerCanvasY = _itemStart.Y + Height / 2.0;
                double rad = Math.Atan2(cur.Y - centerCanvasY, cur.X - centerCanvasX);
                double deg = rad * 180.0 / Math.PI;
                if (deg < 0) deg += 360;

                double rounded = Math.Round(deg, 0);
                foreach (double snap in new[] { 0, 30, 45, 60, 90, 120, 135, 150, 180, 210, 225, 240, 270, 300, 315, 330, 360 })
                {
                    if (Math.Abs(rounded - snap) <= 2.0)
                    {
                        rounded = (snap == 360) ? 0 : snap;
                        break;
                    }
                }

                _rotateTransform.Angle = rounded;
                _angleLabel.Text = $"{_rotateTransform.Angle}°";
                e.Handled = true;
            }
        };

        _rotateHandle.MouseLeftButtonUp += (s, e) =>
        {
            if (_isRotating)
            {
                _isRotating = false;
                _rotateHandle.ReleaseMouseCapture();
                Changed?.Invoke();
                e.Handled = true;
            }
        };

        // 4. Переміщення лінійки по тілу
        PreviewMouseLeftButtonDown += (s, e) =>
        {
            Point hit = e.GetPosition(this);
            var hitResult = VisualTreeHelper.HitTest(this, hit);
            if (hitResult?.VisualHit != null)
            {
                if (IsChildOf(hitResult.VisualHit, _closeBtn) ||
                    IsChildOf(hitResult.VisualHit, _leftGrip) ||
                    IsChildOf(hitResult.VisualHit, _rightGrip) ||
                    IsChildOf(hitResult.VisualHit, _rotateHandle) ||
                    IsChildOf(hitResult.VisualHit, _angleLabel))
                {
                    return;
                }
            }
            _isDragging = true;
            _mouseStart = e.GetPosition(Parent as IInputElement);
            _itemStart = new Point(InkCanvas.GetLeft(this), InkCanvas.GetTop(this));
            CaptureMouse();
            e.Handled = true;
        };

        MouseMove += (s, e) =>
        {
            if (_isDragging)
            {
                Point cur = e.GetPosition(Parent as IInputElement);
                double newX = Math.Max(0, _itemStart.X + (cur.X - _mouseStart.X));
                double newY = Math.Max(0, _itemStart.Y + (cur.Y - _mouseStart.Y));
                InkCanvas.SetLeft(this, newX);
                InkCanvas.SetTop(this, newY);
                e.Handled = true;
            }
        };

        MouseLeftButtonUp += (s, e) =>
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                Changed?.Invoke();
                e.Handled = true;
            }
        };
    }

    public static bool IsChildOf(DependencyObject? child, DependencyObject? parent)
    {
        DependencyObject? current = child;
        while (current != null)
        {
            if (current == parent) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }
}


// =========================================================================
// ШКІЛЬНИЙ ТРАНСПОРТИР 180° (WHITEBOARD PROTRACTOR)
// =========================================================================

public sealed class WhiteboardProtractorItem : Grid
{
    private readonly RotateTransform _rotateTransform = new();
    private readonly WpfPath _body;
    private bool _isDragging;
    private Point _mouseStart;
    private Point _itemStart;

    public event Action? CloseRequested;
    public event Action? Changed;
    public double Angle => _rotateTransform.Angle;

    public WhiteboardProtractorItem(double width, double height)
    {
        Width = width;
        Height = height;
        ClipToBounds = false;
        Cursor = Cursors.SizeAll;

        _rotateTransform.CenterX = width / 2.0;
        _rotateTransform.CenterY = height;
        RenderTransform = _rotateTransform;

        _body = new WpfPath
        {
            Data = Geometry.Parse($"M 0,{height} A {width / 2.0},{height} 0 0 1 {width},{height} Z"),
            Fill = new SolidColorBrush(Color.FromArgb(170, 254, 240, 138)),
            Stroke = new SolidColorBrush(Color.FromRgb(202, 138, 4)),
            StrokeThickness = 2,
            Cursor = Cursors.SizeAll
        };
        Children.Add(_body);

        var ticksCanvas = new Canvas { IsHitTestVisible = false };
        double cx = width / 2.0;
        double cy = height;
        double radius = width / 2.0 - 5.0;

        for (int deg = 0; deg <= 180; deg += 5)
        {
            double rad = deg * Math.PI / 180.0;
            double tickLen = (deg % 10 == 0) ? 14.0 : 7.0;
            double x1 = cx - radius * Math.Cos(rad);
            double y1 = cy - radius * Math.Sin(rad);
            double x2 = cx - (radius - tickLen) * Math.Cos(rad);
            double y2 = cy - (radius - tickLen) * Math.Sin(rad);

            var tickLine = new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                StrokeThickness = (deg % 10 == 0) ? 1.5 : 0.8
            };
            ticksCanvas.Children.Add(tickLine);

            if (deg % 30 == 0)
            {
                var label = new TextBlock
                {
                    Text = $"{deg}°",
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
                };
                double lx = cx - (radius - 24.0) * Math.Cos(rad) - 8;
                double ly = cy - (radius - 24.0) * Math.Sin(rad) - 6;
                Canvas.SetLeft(label, lx);
                Canvas.SetTop(label, ly);
                ticksCanvas.Children.Add(label);
            }
        }
        Children.Add(ticksCanvas);

        var closeBtn = new Button
        {
            Width = 18,
            Height = 18,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = WpfVerticalAlignment.Top,
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Сховати транспортир"
        };
        var delTemplate = new ControlTemplate(typeof(Button));
        var delFactory = new FrameworkElementFactory(typeof(Border));
        delFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        delFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(239, 68, 68)));
        var delIcon = new FrameworkElementFactory(typeof(WpfPath));
        delIcon.SetValue(WpfPath.DataProperty, Geometry.Parse("M 4,4 L 10,10 M 10,4 L 4,10"));
        delIcon.SetValue(WpfPath.StrokeProperty, Brushes.White);
        delIcon.SetValue(WpfPath.StrokeThicknessProperty, 1.6);
        delIcon.SetValue(WpfPath.StretchProperty, Stretch.Uniform);
        delIcon.SetValue(WpfPath.WidthProperty, 7.0);
        delIcon.SetValue(WpfPath.HeightProperty, 7.0);
        delIcon.SetValue(WpfPath.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        delIcon.SetValue(WpfPath.VerticalAlignmentProperty, WpfVerticalAlignment.Center);
        delFactory.AppendChild(delIcon);
        delTemplate.VisualTree = delFactory;
        closeBtn.Template = delTemplate;
        closeBtn.PreviewMouseLeftButtonDown += (s, e) => { e.Handled = true; CloseRequested?.Invoke(); };
        closeBtn.Click += (s, e) => { e.Handled = true; CloseRequested?.Invoke(); };
        Children.Add(closeBtn);

        // Перемещение по телу (кнопка закрытия не перехватывается!)
        PreviewMouseLeftButtonDown += (s, e) =>
        {
            Point hit = e.GetPosition(this);
            var hitResult = VisualTreeHelper.HitTest(this, hit);
            if (hitResult?.VisualHit != null && WhiteboardControl.IsChildOf(hitResult.VisualHit, closeBtn)) return;

            _isDragging = true;
            _mouseStart = e.GetPosition(Parent as IInputElement);
            _itemStart = new Point(InkCanvas.GetLeft(this), InkCanvas.GetTop(this));
            CaptureMouse();
            e.Handled = true;
        };

        MouseMove += (s, e) =>
        {
            if (_isDragging)
            {
                Point cur = e.GetPosition(Parent as IInputElement);
                double newX = Math.Max(0, _itemStart.X + (cur.X - _mouseStart.X));
                double newY = Math.Max(0, _itemStart.Y + (cur.Y - _mouseStart.Y));
                InkCanvas.SetLeft(this, newX);
                InkCanvas.SetTop(this, newY);
                e.Handled = true;
            }
        };

        MouseLeftButtonUp += (s, e) =>
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                Changed?.Invoke();
                e.Handled = true;
            }
        };
    }
}

// =========================================================================
// ШКІЛЬНИЙ КРЕСЛЯРСЬКИЙ КОСИНЕЦЬ 45°/90° (WHITEBOARD SET SQUARE ITEM)
// =========================================================================

public sealed class WhiteboardSetSquareItem : Grid
{
    private readonly RotateTransform _rotateTransform = new();
    private readonly TextBlock _angleLabel;
    private readonly Button _closeBtn;
    private readonly Button _rotateBtn;
    private readonly WpfPath _body;
    private bool _isDragging;
    private Point _mouseStart;
    private Point _itemStart;

    public event Action? CloseRequested;
    public event Action? Changed;
    public double Angle => _rotateTransform.Angle;

    public WhiteboardSetSquareItem(double width = 240, double height = 240, double initialAngle = 0.0)
    {
        Width = width;
        Height = height;
        ClipToBounds = false;
        Cursor = Cursors.SizeAll;

        _rotateTransform.Angle = initialAngle;
        _rotateTransform.CenterX = width / 2.0;
        _rotateTransform.CenterY = height / 2.0;
        RenderTransform = _rotateTransform;

        // Внешний и внутренний треугольники
        var outerFig = new PathFigure { StartPoint = new Point(0, height), IsClosed = true };
        outerFig.Segments.Add(new LineSegment(new Point(width, height), true));
        outerFig.Segments.Add(new LineSegment(new Point(0, 0), true));

        var innerFig = new PathFigure { StartPoint = new Point(36, height - 36), IsClosed = true };
        innerFig.Segments.Add(new LineSegment(new Point(width - 72, height - 36), true));
        innerFig.Segments.Add(new LineSegment(new Point(36, 72), true));

        var geo = new PathGeometry { FillRule = FillRule.EvenOdd };
        geo.Figures.Add(outerFig);
        geo.Figures.Add(innerFig);

        _body = new WpfPath
        {
            Data = geo,
            Fill = new SolidColorBrush(Color.FromArgb(190, 220, 252, 231)),
            Stroke = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
            StrokeThickness = 2.0,
            SnapsToDevicePixels = true,
            Cursor = Cursors.SizeAll
        };
        _body.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 14,
            ShadowDepth = 3,
            Opacity = 0.25
        };
        Children.Add(_body);

        // Разметка шкалы в миллиметрах
        var scaleCanvas = new Canvas { IsHitTestVisible = false };
        // Горизонтальный катет (снизу)
        for (int x = 10; x <= (int)(width - 15); x += 10)
        {
            bool isCm = (x % 50 == 0);
            double tickH = isCm ? 12 : ((x % 25 == 0) ? 8 : 5);
            var tick = new Line
            {
                X1 = x, Y1 = height,
                X2 = x, Y2 = height - tickH,
                Stroke = new SolidColorBrush(Color.FromRgb(22, 101, 52)),
                StrokeThickness = isCm ? 1.4 : 0.8
            };
            scaleCanvas.Children.Add(tick);
            if (isCm && x < width - 40)
            {
                var lbl = new TextBlock
                {
                    Text = $"{x / 25}",
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52))
                };
                Canvas.SetLeft(lbl, x - 5);
                Canvas.SetTop(lbl, height - 24);
                scaleCanvas.Children.Add(lbl);
            }
        }
        // Вертикальный катет (слева)
        for (int y = 10; y <= (int)(height - 15); y += 10)
        {
            double posFromBottom = height - y;
            bool isCm = (y % 50 == 0);
            double tickW = isCm ? 12 : ((y % 25 == 0) ? 8 : 5);
            var tick = new Line
            {
                X1 = 0, Y1 = posFromBottom,
                X2 = tickW, Y2 = posFromBottom,
                Stroke = new SolidColorBrush(Color.FromRgb(22, 101, 52)),
                StrokeThickness = isCm ? 1.4 : 0.8
            };
            scaleCanvas.Children.Add(tick);
            if (isCm && y < height - 40)
            {
                var lbl = new TextBlock
                {
                    Text = $"{y / 25}",
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52))
                };
                Canvas.SetLeft(lbl, 14);
                Canvas.SetTop(lbl, posFromBottom - 6);
                scaleCanvas.Children.Add(lbl);
            }
        }
        // Метка прямого угла 90° в углу (0, height)
        var rightAngleMark = new WpfPath
        {
            Data = Geometry.Parse($"M 14,{height} L 14,{height - 14} L 0,{height - 14}"),
            Stroke = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
            StrokeThickness = 1.4
        };
        scaleCanvas.Children.Add(rightAngleMark);
        Children.Add(scaleCanvas);

        // Метка угла и кнопка вращения
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = WpfVerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 16, 10)
        };

        _angleLabel = new TextBlock
        {
            Text = $"{Math.Round(initialAngle, 0)}°",
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52)),
            VerticalAlignment = WpfVerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        actionsPanel.Children.Add(_angleLabel);

        _rotateBtn = new Button
        {
            Width = 22,
            Height = 22,
            Cursor = Cursors.Hand,
            ToolTip = "Повернути косинець на 45°",
            Background = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
            Foreground = Brushes.White,
            Content = "⟳",
            FontSize = 12
        };
        _rotateBtn.PreviewMouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            _rotateTransform.Angle = (_rotateTransform.Angle + 45.0) % 360.0;
            _angleLabel.Text = $"{_rotateTransform.Angle}°";
            Changed?.Invoke();
        };
        _rotateBtn.Click += (s, e) =>
        {
            e.Handled = true;
            _rotateTransform.Angle = (_rotateTransform.Angle + 45.0) % 360.0;
            _angleLabel.Text = $"{_rotateTransform.Angle}°";
            Changed?.Invoke();
        };
        actionsPanel.Children.Add(_rotateBtn);
        Children.Add(actionsPanel);

        // Кнопка закрытия
        _closeBtn = new Button
        {
            Width = 20,
            Height = 20,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = WpfVerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Сховати косинець"
        };
        var delTemplate = new ControlTemplate(typeof(Button));
        var delFactory = new FrameworkElementFactory(typeof(Border));
        delFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        delFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(239, 68, 68)));
        var delIcon = new FrameworkElementFactory(typeof(WpfPath));
        delIcon.SetValue(WpfPath.DataProperty, Geometry.Parse("M 4,4 L 10,10 M 10,4 L 4,10"));
        delIcon.SetValue(WpfPath.StrokeProperty, Brushes.White);
        delIcon.SetValue(WpfPath.StrokeThicknessProperty, 1.6);
        delIcon.SetValue(WpfPath.StretchProperty, Stretch.Uniform);
        delIcon.SetValue(WpfPath.WidthProperty, 7.0);
        delIcon.SetValue(WpfPath.HeightProperty, 7.0);
        delIcon.SetValue(WpfPath.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        delIcon.SetValue(WpfPath.VerticalAlignmentProperty, WpfVerticalAlignment.Center);
        delFactory.AppendChild(delIcon);
        delTemplate.VisualTree = delFactory;
        _closeBtn.Template = delTemplate;
        _closeBtn.PreviewMouseLeftButtonDown += (s, e) => { e.Handled = true; CloseRequested?.Invoke(); };
        _closeBtn.Click += (s, e) => { e.Handled = true; CloseRequested?.Invoke(); };
        Children.Add(_closeBtn);

        // Перемещение по телу косинца
        PreviewMouseLeftButtonDown += (s, e) =>
        {
            Point hit = e.GetPosition(this);
            var hitResult = VisualTreeHelper.HitTest(this, hit);
            if (hitResult?.VisualHit != null && (WhiteboardControl.IsChildOf(hitResult.VisualHit, _closeBtn) || WhiteboardControl.IsChildOf(hitResult.VisualHit, _rotateBtn))) return;

            _isDragging = true;
            _mouseStart = e.GetPosition(Parent as IInputElement);
            _itemStart = new Point(InkCanvas.GetLeft(this), InkCanvas.GetTop(this));
            CaptureMouse();
            e.Handled = true;
        };
        MouseMove += (s, e) =>
        {
            if (_isDragging)
            {
                Point cur = e.GetPosition(Parent as IInputElement);
                double newX = Math.Max(0, _itemStart.X + (cur.X - _mouseStart.X));
                double newY = Math.Max(0, _itemStart.Y + (cur.Y - _mouseStart.Y));
                InkCanvas.SetLeft(this, newX);
                InkCanvas.SetTop(this, newY);
                e.Handled = true;
            }
        };
        MouseLeftButtonUp += (s, e) =>
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                Changed?.Invoke();
                e.Handled = true;
            }
        };
    }
}

// =========================================================================
// ПАТЕРНИ ТА ПУНКТИРНІ ШТРИХИ (DASHED / DOTTED STROKES)
// =========================================================================

public enum WhiteboardStrokePattern
{
    Solid,
    Double,
    Wavy,
    Dashed,
    DashDot,
    Dotted
}

public sealed class WhiteboardDashedStroke : Stroke
{
    public static readonly Guid StrokePatternGuid = new Guid("8C4F8D1E-149B-4D2A-91C2-823E1D4A7F92");

    public WhiteboardStrokePattern Pattern { get; set; }

    public WhiteboardDashedStroke(StylusPointCollection points, DrawingAttributes da, WhiteboardStrokePattern pattern)
        : base(points, da)
    {
        Pattern = pattern;
        if (!ContainsPropertyData(StrokePatternGuid))
        {
            AddPropertyData(StrokePatternGuid, (int)pattern);
        }
    }

    protected override void DrawCore(DrawingContext dc, DrawingAttributes da)
    {
        if (Pattern == WhiteboardStrokePattern.Solid)
        {
            base.DrawCore(dc, da);
            return;
        }

        var points = StylusPoints;
        if (points == null || points.Count < 2)
        {
            base.DrawCore(dc, da);
            return;
        }

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure((Point)points[0], false, false);
            var segs = new List<Point>(points.Count - 1);
            for (int i = 1; i < points.Count; i++)
            {
                segs.Add((Point)points[i]);
            }
            ctx.PolyLineTo(segs, true, false);
        }
        geom.Freeze();

        DashStyle dashStyle = Pattern switch
        {
            WhiteboardStrokePattern.Dashed => DashStyles.Dash,
            WhiteboardStrokePattern.Dotted => DashStyles.Dot,
            WhiteboardStrokePattern.DashDot => DashStyles.DashDot,
            _ => DashStyles.Solid
        };

        var pen = new Pen(new SolidColorBrush(da.Color), da.Width)
        {
            DashStyle = dashStyle,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            DashCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();

        dc.DrawGeometry(null, pen, geom);
    }
}

// =========================================================================
// ПРАВИЛА ПЕРЕНОСУ СЛІВ В УКРАЇНСЬКІЙ МОВІ (UKRAINIAN HYPHENATOR)
// =========================================================================

public static class UkrHyphenator
{
    private static readonly HashSet<char> Vowels = new()
    {
        'а', 'е', 'є', 'и', 'і', 'ї', 'о', 'у', 'ю', 'я',
        'ё', 'ы', 'э',
        'a', 'e', 'i', 'o', 'u', 'y'
    };

    private static bool IsVowel(char c) => Vowels.Contains(char.ToLowerInvariant(c));

    public static List<int> GetHyphenationPositions(string word)
    {
        var positions = new List<int>();
        if (string.IsNullOrEmpty(word) || word.Length < 4) return positions;

        int len = word.Length;
        for (int i = 2; i <= len - 2; i++)
        {
            char prev = char.ToLowerInvariant(word[i - 1]);
            char next = char.ToLowerInvariant(word[i]);

            // Не відривати ь, ъ, й, апостроф
            if (next == 'ь' || next == 'ъ' || next == 'й' || next == '\'' || next == '’') continue;
            if (prev == '\'' || prev == '’' || prev == '-') continue;

            // Не розривати дж, дз
            if (prev == 'д' && (next == 'ж' || next == 'з')) continue;

            // Перевірка наявності голосних у лівій та правій частинах
            bool leftHasVowel = false;
            for (int k = 0; k < i; k++)
            {
                if (IsVowel(word[k])) { leftHasVowel = true; break; }
            }
            if (!leftHasVowel) continue;

            bool rightHasVowel = false;
            for (int k = i; k < len; k++)
            {
                if (IsVowel(word[k])) { rightHasVowel = true; break; }
            }
            if (!rightHasVowel) continue;

            // Розподіл складів
            if (IsVowel(prev) || (!IsVowel(prev) && !IsVowel(next)) || (IsVowel(prev) && IsVowel(next)))
            {
                positions.Add(i);
            }
        }
        return positions;
    }
}

// =========================================================================
// СТІКЕР (WHITEBOARD STICKY NOTE ITEM)
// =========================================================================

public sealed class WhiteboardStickerItem : Grid
{
    public string Id { get; }
    public string ColorHex { get; }
    public string Text => _textBox.Text;

    private readonly TextBox _textBox;
    private readonly Border _selectionBorder;
    private readonly Border _resizeHandle;
    private readonly Button _deleteBtn;

    private bool _isSelected;
    private bool _isDragging;
    private bool _isResizing;
    private Point _mouseStart;
    private Point _itemStart;
    private double _startWidth;
    private double _startHeight;

    public event Action<WhiteboardStickerItem>? DeleteRequested;
    public event Action<WhiteboardStickerItem>? Selected;
    public event Action? Changed;

    public WhiteboardStickerItem(string id, string colorHex, string text, double width, double height)
    {
        Id = id;
        ColorHex = colorHex;
        Width = (width > 0 && width != 200) ? width : 125.0;
        MinWidth = 95.0;
        MinHeight = 55.0;

        if (double.IsNaN(height) || height <= 0 || height == 170.0)
        {
            Height = double.NaN;
        }
        else
        {
            Height = height;
        }

        Color bg;
        try { bg = (Color)ColorConverter.ConvertFromString(colorHex); } catch { bg = Color.FromRgb(254, 240, 138); }

        Focusable = true;
        ClipToBounds = false;

        var body = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(bg),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true
        };
        body.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 14,
            ShadowDepth = 3,
            Opacity = 0.28
        };
        Children.Add(body);

        var layoutGrid = new Grid();
        layoutGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        layoutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Cursor = Cursors.SizeAll
        };
        var headerTitle = new TextBlock
        {
            Text = "Стікер",
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Margin = new Thickness(8, 4, 0, 0)
        };
        header.Child = headerTitle;
        Grid.SetRow(header, 0);
        layoutGrid.Children.Add(header);

        _textBox = new TextBox
        {
            Text = text,
            FontSize = 13.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 6, 8, 6),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CaretBrush = Brushes.Black,
            Cursor = Cursors.IBeam
        };
        _textBox.TextChanged += (_, _) => Changed?.Invoke();
        Grid.SetRow(_textBox, 1);
        layoutGrid.Children.Add(_textBox);

        Children.Add(layoutGrid);

        _selectionBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(34, 178, 76)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        Children.Add(_selectionBorder);

        _resizeHandle = new Border
        {
            Width = 14,
            Height = 14,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(34, 178, 76)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = WpfVerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -6, -6),
            Cursor = Cursors.SizeNWSE,
            Visibility = Visibility.Collapsed,
            ToolTip = "Потягніть для зміни розміру стікера"
        };
        Children.Add(_resizeHandle);

        _deleteBtn = new Button
        {
            Width = 20,
            Height = 20,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = WpfVerticalAlignment.Top,
            Margin = new Thickness(0, -9, -9, 0),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
            ToolTip = "Видалити стікер"
        };
        var delTemplate = new ControlTemplate(typeof(Button));
        var delFactory = new FrameworkElementFactory(typeof(Border));
        delFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        delFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(239, 68, 68)));
        var delIcon = new FrameworkElementFactory(typeof(WpfPath));
        delIcon.SetValue(WpfPath.DataProperty, Geometry.Parse("M 4,4 L 10,10 M 10,4 L 4,10"));
        delIcon.SetValue(WpfPath.StrokeProperty, Brushes.White);
        delIcon.SetValue(WpfPath.StrokeThicknessProperty, 1.6);
        delIcon.SetValue(WpfPath.StretchProperty, Stretch.Uniform);
        delIcon.SetValue(WpfPath.WidthProperty, 7.0);
        delIcon.SetValue(WpfPath.HeightProperty, 7.0);
        delIcon.SetValue(WpfPath.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        delIcon.SetValue(WpfPath.VerticalAlignmentProperty, WpfVerticalAlignment.Center);
        delFactory.AppendChild(delIcon);
        delTemplate.VisualTree = delFactory;
        _deleteBtn.Template = delTemplate;
        _deleteBtn.PreviewMouseLeftButtonDown += (s, e) => { e.Handled = true; DeleteRequested?.Invoke(this); };
        _deleteBtn.Click += (s, e) => { e.Handled = true; DeleteRequested?.Invoke(this); };
        Children.Add(_deleteBtn);

        PreviewMouseLeftButtonDown += (s, e) =>
        {
            SetSelected(true);
            Selected?.Invoke(this);

            Point hit = e.GetPosition(this);
            var hitResult = VisualTreeHelper.HitTest(this, hit);
            if (hitResult?.VisualHit != null)
            {
                if (WhiteboardControl.IsChildOf(hitResult.VisualHit, _deleteBtn)) return;
                if (WhiteboardControl.IsChildOf(hitResult.VisualHit, _resizeHandle))
                {
                    _isResizing = true;
                    _mouseStart = e.GetPosition(Parent as IInputElement);
                    _startWidth = ActualWidth > 0 ? ActualWidth : Width;
                    _startHeight = ActualHeight > 0 ? ActualHeight : (double.IsNaN(Height) ? ActualHeight : Height);
                    CaptureMouse();
                    e.Handled = true;
                    return;
                }
                if (WhiteboardControl.IsChildOf(hitResult.VisualHit, _textBox))
                {
                    return; // Всі крім тексту: дозволяємо редагувати текст
                }
            }

            _isDragging = true;
            _mouseStart = e.GetPosition(Parent as IInputElement);
            _itemStart = new Point(InkCanvas.GetLeft(this), InkCanvas.GetTop(this));
            CaptureMouse();
            e.Handled = true;
        };

        header.PreviewMouseLeftButtonDown += (s, e) =>
        {
            SetSelected(true);
            Selected?.Invoke(this);
            _isDragging = true;
            _mouseStart = e.GetPosition(Parent as IInputElement);
            _itemStart = new Point(InkCanvas.GetLeft(this), InkCanvas.GetTop(this));
            CaptureMouse();
            e.Handled = true;
        };

        _resizeHandle.PreviewMouseLeftButtonDown += (s, e) =>
        {
            _isResizing = true;
            _mouseStart = e.GetPosition(Parent as IInputElement);
            _startWidth = ActualWidth > 0 ? ActualWidth : Width;
            _startHeight = ActualHeight > 0 ? ActualHeight : (double.IsNaN(Height) ? ActualHeight : Height);
            CaptureMouse();
            e.Handled = true;
        };

        MouseMove += (s, e) =>
        {
            if (_isResizing)
            {
                Point cur = e.GetPosition(Parent as IInputElement);
                Width = Math.Max(95, _startWidth + (cur.X - _mouseStart.X));
                Height = Math.Max(50, _startHeight + (cur.Y - _mouseStart.Y));
                e.Handled = true;
                return;
            }

            if (_isDragging)
            {
                Point cur = e.GetPosition(Parent as IInputElement);
                double newX = Math.Max(0, _itemStart.X + (cur.X - _mouseStart.X));
                double newY = Math.Max(0, _itemStart.Y + (cur.Y - _mouseStart.Y));
                InkCanvas.SetLeft(this, newX);
                InkCanvas.SetTop(this, newY);
                e.Handled = true;
            }
        };

        MouseLeftButtonUp += (s, e) =>
        {
            if (_isDragging || _isResizing)
            {
                _isDragging = false;
                _isResizing = false;
                ReleaseMouseCapture();
                Changed?.Invoke();
                e.Handled = true;
            }
        };

        MouseEnter += (_, _) =>
        {
            if (!_isSelected) { _selectionBorder.Visibility = Visibility.Visible; _deleteBtn.Visibility = Visibility.Visible; }
        };
        MouseLeave += (_, _) =>
        {
            if (!_isSelected && !_isDragging && !_isResizing) { _selectionBorder.Visibility = Visibility.Collapsed; _deleteBtn.Visibility = Visibility.Collapsed; }
        };
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        _selectionBorder.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        _resizeHandle.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        _deleteBtn.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
    }
}

// =========================================================================
// ІНТЕРАКТИВНЕ ЗОБРАЖЕННЯ
// =========================================================================

public sealed class WhiteboardImageItem : Grid
{
    public string Id { get; }
    public string Base64Data { get; }

    private readonly Image _imageControl;
    private readonly Border _selectionBorder;
    private readonly Border _resizeHandle;
    private readonly Button _deleteBtn;

    private bool _isSelected;
    private bool _isDragging;
    private bool _isResizing;
    private Point _mouseStartPoint;
    private Point _itemStartPoint;
    private double _startWidth;
    private double _startHeight;
    private double _aspectRatio = 1.0;

    public event Action<WhiteboardImageItem>? DeleteRequested;
    public event Action<WhiteboardImageItem>? Selected;
    public event Action? Changed;

    public WhiteboardImageItem(string id, BitmapSource bitmap, string base64Data, double width, double height)
    {
        Id = id;
        Base64Data = base64Data;
        Width = width;
        Height = height;

        _aspectRatio = (width > 0 && height > 0) ? (width / height) : 1.0;

        Focusable = true;
        ClipToBounds = false;
        Cursor = Cursors.SizeAll;

        _imageControl = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = WpfVerticalAlignment.Stretch
        };
        Children.Add(_imageControl);

        _selectionBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(34, 178, 76)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        Children.Add(_selectionBorder);

        _resizeHandle = new Border
        {
            Width = 14,
            Height = 14,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(34, 178, 76)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = WpfVerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -6, -6),
            Cursor = Cursors.SizeNWSE,
            Visibility = Visibility.Collapsed,
            ToolTip = "Потягніть, щоб змінити розмір (більше / менше)"
        };
        Children.Add(_resizeHandle);

        _deleteBtn = new Button
        {
            Width = 22,
            Height = 22,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = WpfVerticalAlignment.Top,
            Margin = new Thickness(0, -10, -10, 0),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
            ToolTip = "Видалити зображення"
        };

        var delTemplate = new ControlTemplate(typeof(Button));
        var delFactory = new FrameworkElementFactory(typeof(Border));
        delFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        delFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(239, 68, 68)));
        var delIcon = new FrameworkElementFactory(typeof(WpfPath));
        delIcon.SetValue(WpfPath.DataProperty, Geometry.Parse("M 5,5 L 11,11 M 11,5 L 5,11"));
        delIcon.SetValue(WpfPath.StrokeProperty, Brushes.White);
        delIcon.SetValue(WpfPath.StrokeThicknessProperty, 1.8);
        delIcon.SetValue(WpfPath.StretchProperty, Stretch.Uniform);
        delIcon.SetValue(WpfPath.WidthProperty, 8.0);
        delIcon.SetValue(WpfPath.HeightProperty, 8.0);
        delIcon.SetValue(WpfPath.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        delIcon.SetValue(WpfPath.VerticalAlignmentProperty, WpfVerticalAlignment.Center);
        delFactory.AppendChild(delIcon);
        delTemplate.VisualTree = delFactory;
        _deleteBtn.Template = delTemplate;

        _deleteBtn.PreviewMouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            DeleteRequested?.Invoke(this);
        };
        _deleteBtn.Click += (s, e) =>
        {
            e.Handled = true;
            DeleteRequested?.Invoke(this);
        };
        Children.Add(_deleteBtn);

        PreviewMouseLeftButtonDown += (s, e) =>
        {
            SetSelected(true);
            Selected?.Invoke(this);

            Point hit = e.GetPosition(this);
            var hitResult = VisualTreeHelper.HitTest(this, hit);

            if (hitResult?.VisualHit != null && IsChildOf(hitResult.VisualHit, _deleteBtn)) return;

            if (hitResult?.VisualHit != null && IsChildOf(hitResult.VisualHit, _resizeHandle))
            {
                _isResizing = true;
                _mouseStartPoint = e.GetPosition(Parent as IInputElement);
                _startWidth = ActualWidth > 0 ? ActualWidth : Width;
                _startHeight = ActualHeight > 0 ? ActualHeight : Height;
                _aspectRatio = (_startWidth > 0 && _startHeight > 0) ? (_startWidth / _startHeight) : 1.0;
                CaptureMouse();
                e.Handled = true;
                return;
            }

            _isDragging = true;
            _mouseStartPoint = e.GetPosition(Parent as IInputElement);
            _itemStartPoint = new Point(InkCanvas.GetLeft(this), InkCanvas.GetTop(this));
            CaptureMouse();
            e.Handled = true;
        };

        MouseMove += (s, e) =>
        {
            if (_isResizing)
            {
                Point cur = e.GetPosition(Parent as IInputElement);
                double deltaX = cur.X - _mouseStartPoint.X;
                double newWidth = Math.Max(40.0, _startWidth + deltaX);
                double newHeight = newWidth / _aspectRatio;
                if (newHeight < 40.0)
                {
                    newHeight = 40.0;
                    newWidth = newHeight * _aspectRatio;
                }
                Width = newWidth;
                Height = newHeight;
                e.Handled = true;
                return;
            }

            if (_isDragging)
            {
                Point cur = e.GetPosition(Parent as IInputElement);
                double newX = Math.Max(0, _itemStartPoint.X + (cur.X - _mouseStartPoint.X));
                double newY = Math.Max(0, _itemStartPoint.Y + (cur.Y - _mouseStartPoint.Y));
                InkCanvas.SetLeft(this, newX);
                InkCanvas.SetTop(this, newY);
                e.Handled = true;
            }
        };

        MouseLeftButtonUp += (s, e) =>
        {
            if (_isResizing || _isDragging)
            {
                _isResizing = false;
                _isDragging = false;
                ReleaseMouseCapture();
                Changed?.Invoke();
                e.Handled = true;
            }
        };

        MouseEnter += (_, _) =>
        {
            if (!_isSelected) { _selectionBorder.Visibility = Visibility.Visible; _deleteBtn.Visibility = Visibility.Visible; }
        };
        MouseLeave += (_, _) =>
        {
            if (!_isSelected && !_isDragging && !_isResizing) { _selectionBorder.Visibility = Visibility.Collapsed; _deleteBtn.Visibility = Visibility.Collapsed; }
        };
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        _selectionBorder.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        _resizeHandle.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        _deleteBtn.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
    }

    public static bool IsChildOf(DependencyObject? child, DependencyObject? parent)
    {
        DependencyObject? current = child;
        while (current != null)
        {
            if (current == parent) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }
}

// =========================================================================
// КЛІЄНТ AI-РОЗПІЗНАВАННЯ (CUN.AI / GEMINI 2.5 FLASH)
// =========================================================================

public static 
    // =========================================================================
    // ИНТЕЛЛЕКТУАЛЬНЫЙ КОНТЕКСТНЫЙ ДВИЖОК РУССКОЙ КАЛЛИГРАФИИ И ИИ-ПРОПИСИ
    class WhiteboardAiClient
{
    public static async Task<string?> RecognizeHandwritingAsync(string base64Png)
    {
        try
        {
            string token = Authentication.SupabaseOptions.PublishableKey;
            try
            {
                var store = new Authentication.ProtectedAuthSessionStore();
                var session = store.Load();
                if (!string.IsNullOrWhiteSpace(session?.AccessToken))
                {
                    token = session.AccessToken;
                }
            }
            catch { }

            var prompt = """
Ты — высокоточный OCR-модуль интеллектуальной интерактивной доски.
Твоя задача: ТОЧНО и БУКВАЛЬНО распознать рукописный текст, слова, предложения, математические выражения или числа с изображения.

ПРАВИЛА РАСПОЗНАВАНИЯ:
1. ТЕКСТ И СЛОВА:
   - Распознавай естественный рукописный текст на русском, украинском и английском языках (сохраняй регистр букв, слова, пробелы и знаки препинания).
   - Никогда не выдумывай бессмысленные случайные буквы! Если на изображении случайные черкания или текст неразборчив — верни: none.
2. МАТЕМАТИКА И ФОРМУЛЫ:
   - Точно распознавай числа (0-9), знаки (+, −, ×, ÷, =), уравнения, степени (², ³), корни (√), дроби, скобки.
3. ФОРМАТ ОТВЕТА:
   - Верни ИСКЛЮЧИТЕЛЬНО чистый распознанный текст без кавычек, пояснений и блоков кода.
   - Если разборчивого текста нет — верни только: none.
""";

            var request = new List<AssistantMessage>
            {
                new()
                {
                    Role = "system",
                    Text = prompt
                },
                new()
                {
                    Role = "user",
                    Text = "Распознай рукописный текст или формулу с этого рисунка:",
                    Images = [new AssistantImage { Base64 = base64Png, MimeType = "image/png", Name = "ink.png" }]
                }
            };

            var sb = new StringBuilder();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));

            await AssistantClient.StreamAsync(token, "fast", request, delta =>
            {
                sb.Append(delta);
                return Task.CompletedTask;
            }, cts.Token);

            string resultText = CleanOcrText(sb.ToString());
            if (string.IsNullOrWhiteSpace(resultText)) return null;

            var lower = resultText.ToLowerInvariant();
            if (lower == "none" || lower == "порожньо" || lower == "немає" || lower == "null" || lower == "no text" || lower == "текст відсутній" || lower == "малюнок")
            {
                return null;
            }

            return resultText;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return null;
        }
    }

    public static string CleanOcrText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        text = text.Trim();

        if (text.StartsWith("```") && text.EndsWith("```"))
        {
            int firstLf = text.IndexOf('\n');
            int lastFence = text.LastIndexOf("```");
            if (firstLf >= 0 && lastFence > firstLf)
            {
                text = text.Substring(firstLf + 1, lastFence - firstLf - 1).Trim();
            }
        }
        else if (text.StartsWith("```"))
        {
            int firstLf = text.IndexOf('\n');
            if (firstLf >= 0) text = text.Substring(firstLf + 1).Trim();
            if (text.EndsWith("```")) text = text.Substring(0, text.Length - 3).Trim();
        }

        text = text.Trim('"', '\'', '«', '»', '`', '“', '”', '’');

        string[] prefixes = {
            "the word is", "the text is", "word:", "text:", "слово:", "текст:",
            "написано:", "розпізнано:", "розпізнаний текст:", "відповідь:", "результат:"
        };
        foreach (var p in prefixes)
        {
            if (text.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(p.Length).Trim(' ', ':', '"', '\'');
            }
        }

        // Сохраняем полный русский алфавит (ы, э, ъ, ё) без искажений

        // Схлопування літер, якщо розпізнавач розділив їх пробілами
        text = CollapseSpacedLetters(text);

        if (text.EndsWith('.') && !text.Contains('?') && !text.Contains('!') && !text.Substring(0, text.Length - 1).Contains('.'))
        {
            text = text.Substring(0, text.Length - 1).TrimEnd();
        }

        return text.Trim();
    }

    private static string CollapseSpacedLetters(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains(' ')) return text;

        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        for (int l = 0; l < lines.Length; l++)
        {
            string line = lines[l];
            var tokens = System.Text.RegularExpressions.Regex.Split(line, @"\s{2,}");
            for (int t = 0; t < tokens.Length; t++)
            {
                string tok = tokens[t].Trim();
                var chars = tok.Split(' ');
                if (chars.Length >= 3 && chars.All(c => c.Length == 1 && IsCyrillicLetter(c[0])))
                {
                    tokens[t] = string.Concat(chars);
                }
            }
            lines[l] = string.Join("  ", tokens);
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static bool IsCyrillicLetter(char c)
    {
        return (c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я') ||
               c == 'є' || c == 'Є' || c == 'і' || c == 'І' || c == 'ї' || c == 'Ї' || c == 'ґ' || c == 'Ґ' || c == '\'';
    }

    public static bool IsMathFormula(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains('=') || text.Contains('+') || text.Contains('−') || text.Contains('×') ||
               text.Contains('÷') || text.Contains('√') || text.Contains('^') || text.Contains('≤') ||
               text.Contains('≥') || text.Contains('≠') || text.Contains('²') || text.Contains('³');
    }
}

// =========================================================================
// МОДЕЛІ ДАНИХ СЕСІЇ
// =========================================================================

public class WhiteboardSessionData
{
    [JsonPropertyName("activeBoardId")]
    public string ActiveBoardId { get; set; } = string.Empty;

    [JsonPropertyName("boards")]
    public List<WhiteboardBoardData> Boards { get; set; } = new();
}

public class WhiteboardBoardData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("title")]
    public string Title { get; set; } = "Аркуш 1";

    [JsonPropertyName("backgroundKind")]
    public string BackgroundKind { get; set; } = "Slanted";

    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor { get; set; } = "#FFFFFF";

    [JsonPropertyName("showHeader")]
    public bool ShowHeader { get; set; } = false;

    [JsonPropertyName("headerDate")]
    public string HeaderDate { get; set; } = "27 жовтня";

    [JsonPropertyName("headerWorkType")]
    public string HeaderWorkType { get; set; } = "Класна робота";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("defaultFont")]
    public string DefaultFont { get; set; } = "Propysy";

    [JsonPropertyName("sheetGeometry")]
    public string SheetGeometry { get; set; } = "LeftMargin";

    [JsonPropertyName("isRulerVisible")]
    public bool IsRulerVisible { get; set; } = false;

    [JsonPropertyName("rulerX")]
    public double RulerX { get; set; } = 100.0;

    [JsonPropertyName("rulerY")]
    public double RulerY { get; set; } = 150.0;

    [JsonPropertyName("rulerAngle")]
    public double RulerAngle { get; set; } = 0.0;

    [JsonPropertyName("strokesIsfBase64")]
    public string StrokesIsfBase64 { get; set; } = string.Empty;

    [JsonPropertyName("texts")]
    public List<WhiteboardTextData> Texts { get; set; } = new();

    [JsonPropertyName("images")]
    public List<WhiteboardImageData> Images { get; set; } = new();

    [JsonPropertyName("stickers")]
    public List<WhiteboardStickerData> Stickers { get; set; } = new();
}

public class WhiteboardTextData
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("colorHex")]
    public string ColorHex { get; set; } = "#1E293B";

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 42.0;
}

public class WhiteboardImageData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("base64Data")]
    public string Base64Data { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
}

public class WhiteboardStickerData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("colorHex")]
    public string ColorHex { get; set; } = "#FEF08A";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; } = 125.0;

    [JsonPropertyName("height")]
    public double Height { get; set; } = double.NaN;
}

public class WhiteboardVertexTag
{
    public Guid ShapeId { get; set; }
    public string Label { get; set; }
    public WhiteboardVertexTag(Guid shapeId, string label)
    {
        ShapeId = shapeId;
        Label = label;
    }



}