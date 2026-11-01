using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
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
    // БЛОК 3: НИЖНЯ ПАНЕЛЬ — РЕЖИМ 1 (ТЕКСТОВИЙ ПРОЦЕСОР ДЛЯ ЗОШИТІВ)
    // =========================================================================

    public bool IsTextModeActive { get; private set; } = false;
    private Color _currentInkColor = Color.FromRgb(30, 58, 138); // Blue default #1E3A8A
    private TextBox? _activeNotebookTextBox = null;

    public void SwitchToTextMode()
    {
        IsTextModeActive = true;
        _activeTool = WhiteboardTool.Text;

        if (TextFormatPanel != null) TextFormatPanel.Visibility = Visibility.Visible;
        if (CursorToolsPanel != null) CursorToolsPanel.Visibility = Visibility.Collapsed;
        if (SubjectConstructorsPanel != null) SubjectConstructorsPanel.Visibility = Visibility.Visible;

        MainCanvas.EditingMode = InkCanvasEditingMode.None;
        MainCanvas.Cursor = Cursors.IBeam;

        ShowAiStatus("Режим зошита: письмовий текст НУШ");
    }

    public void SwitchToGraphicsMode()
    {
        IsTextModeActive = false;
        if (TextFormatPanel != null) TextFormatPanel.Visibility = Visibility.Collapsed;
        if (CursorToolsPanel != null) CursorToolsPanel.Visibility = Visibility.Visible;
        if (SubjectConstructorsPanel != null) SubjectConstructorsPanel.Visibility = Visibility.Collapsed;

        MainCanvas.EditingMode = InkCanvasEditingMode.Ink;
        MainCanvas.Cursor = Cursors.Pen;
        SetActiveTool(WhiteboardTool.Pencil);

        ShowAiStatus("Режим малювання та графічних інструментів");
    }

    private void TextModeToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IsTextModeActive)
        {
            SwitchToGraphicsMode();
        }
        else
        {
            SwitchToTextMode();
        }
    }

    // 1. ПЕРЕМИКАННЯ ШРИФТІВ: РУКОПИСНИЙ (PROPYSY) <-> ДРУКОВАНИЙ
    private void FontStyleToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool isProp = (CurrentDefaultFontKind == "Propysy");
        CurrentDefaultFontKind = isProp ? "Printed" : "Propysy";

        string fontName = CurrentDefaultFontKind == "Propysy" ? "Propysy" : "Segoe UI";
        double fontSize = CurrentDefaultFontKind == "Propysy" ? 34.0 : 26.0;

        if (HeaderDateBox != null)
        {
            HeaderDateBox.FontFamily = new FontFamily(fontName);
            HeaderDateBox.FontSize = fontSize;
        }
        if (HeaderWorkTypeBox != null)
        {
            HeaderWorkTypeBox.FontFamily = new FontFamily(fontName);
            HeaderWorkTypeBox.FontSize = fontSize;
        }

        if (_activeNotebookTextBox != null)
        {
            _activeNotebookTextBox.FontFamily = new FontFamily(fontName);
            _activeNotebookTextBox.FontSize = fontSize;
        }

        UpdateFontToggleVisual(CurrentDefaultFontKind);
        ShowAiStatus($"Шрифт: {(CurrentDefaultFontKind == "Propysy" ? "Рукописний (Прописи НУШ)" : "Друкований")}");
    }

    private void UpdateFontToggleVisual(string kind)
    {
        if (FontToggleLabel != null)
        {
            FontToggleLabel.Text = (kind == "Propysy") ? "Аа (Пропис)" : "Аа (Друк)";
        }
    }

    // 2. ПАЛІТРА ЧОРНИЛ (СИНІЙ, ФІОЛЕТОВИЙ, ЗЕЛЕНИЙ, ЧЕРВОНИЙ, ЧОРНИЙ)
    private void InkColorChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement chip && chip.Tag is string hex)
        {
            try
            {
                var col = (Color)ColorConverter.ConvertFromString(hex);
                _currentInkColor = col;
                _currentPenColor = col;

                if (_activeNotebookTextBox != null)
                {
                    _activeNotebookTextBox.Foreground = new SolidColorBrush(col);
                }

                HighlightInkColorChip(hex);
            }
            catch { }
        }
    }

    private void HighlightInkColorChip(string hex)
    {
        var chips = new FrameworkElement[] { InkBlueChip, InkPurpleChip, InkGreenChip, InkRedChip, InkBlackChip };
        foreach (var c in chips)
        {
            if (c is Border b)
            {
                bool match = ((string)b.Tag == hex);
                b.BorderThickness = new Thickness(match ? 2.5 : 1.0);
                b.BorderBrush = match ? new SolidColorBrush(Color.FromRgb(15, 23, 42)) : new SolidColorBrush(Color.FromRgb(203, 213, 225));
            }
        }
    }

    // =========================================================================
    // 7 СПЕЦІАЛІЗОВАНИХ ПРЕДМЕТНИХ КОНСТРУКТОРІВ
    // =========================================================================

    // -------------------------------------------------------------------------
    // 1. БУДОВА СЛОВА (МОРФЕМИ: ПРЕФІКС ¬, КОРІНЬ ⌢, СУФІКС ∧, ЗАКІНЧЕННЯ □, ОСНОВА [ ])
    // -------------------------------------------------------------------------
    private void MorphemesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (MorphemesPopup != null) MorphemesPopup.IsOpen = !MorphemesPopup.IsOpen;
    }

    private void MorphemePrefixBtn_Click(object sender, RoutedEventArgs e) => InsertMorphemeDecoration("Prefix");
    private void MorphemeRootBtn_Click(object sender, RoutedEventArgs e) => InsertMorphemeDecoration("Root");
    private void MorphemeSuffixBtn_Click(object sender, RoutedEventArgs e) => InsertMorphemeDecoration("Suffix");
    private void MorphemeEndingBtn_Click(object sender, RoutedEventArgs e) => InsertMorphemeDecoration("Ending");
    private void MorphemeBaseBtn_Click(object sender, RoutedEventArgs e) => InsertMorphemeDecoration("Base");

    private void InsertMorphemeDecoration(string kind)
    {
        double targetX = 140.0;
        double targetY = 160.0;
        double targetWidth = 48.0;

        if (_activeNotebookTextBox != null)
        {
            targetX = Canvas.GetLeft(_activeNotebookTextBox) + 10;
            targetY = Canvas.GetTop(_activeNotebookTextBox) - 18;
            if (_activeNotebookTextBox.SelectionLength > 0)
            {
                targetWidth = Math.Max(24.0, _activeNotebookTextBox.SelectionLength * 16.0);
            }
        }

        var morpheme = CreateMorphemeVisual(kind, targetWidth);
        Canvas.SetLeft(morpheme, targetX);
        Canvas.SetTop(morpheme, kind == "Base" ? targetY + 46 : targetY);
        MakeElementDraggable(morpheme);
        MainCanvas.Children.Add(morpheme);

        ShowAiStatus($"Додано елемент будови слова: {kind}");
    }

    private FrameworkElement CreateMorphemeVisual(string kind, double width)
    {
        var strokeBrush = new SolidColorBrush(_currentInkColor);
        strokeBrush.Freeze();

        switch (kind)
        {
            case "Prefix":
                var prefixPath = new WpfPath
                {
                    Data = Geometry.Parse($"M 0,10 H {width} V 18"),
                    Stroke = strokeBrush,
                    StrokeThickness = 2.0,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                return new Border { Child = prefixPath, Background = Brushes.Transparent, Cursor = Cursors.SizeAll };

            case "Root":
                var arcPath = new WpfPath
                {
                    Data = Geometry.Parse($"M 0,16 Q {width / 2.0},0 {width},16"),
                    Stroke = strokeBrush,
                    StrokeThickness = 2.0,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                return new Border { Child = arcPath, Background = Brushes.Transparent, Cursor = Cursors.SizeAll };

            case "Suffix":
                var suffixPath = new WpfPath
                {
                    Data = Geometry.Parse($"M 0,16 L {width / 2.0},2 L {width},16"),
                    Stroke = strokeBrush,
                    StrokeThickness = 2.0,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                return new Border { Child = suffixPath, Background = Brushes.Transparent, Cursor = Cursors.SizeAll };

            case "Ending":
                return new Border
                {
                    Width = Math.Max(28.0, width),
                    Height = 30.0,
                    BorderBrush = strokeBrush,
                    BorderThickness = new Thickness(1.8),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.SizeAll
                };

            case "Base":
            default:
                var basePath = new WpfPath
                {
                    Data = Geometry.Parse($"M 0,0 V 8 H {width} V 0"),
                    Stroke = strokeBrush,
                    StrokeThickness = 2.0,
                    StrokeStartLineCap = PenLineCap.Square,
                    StrokeEndLineCap = PenLineCap.Square
                };
                return new Border { Child = basePath, Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        }
    }

    // -------------------------------------------------------------------------
    // 2. СИНТАКСИЧНИЙ РОЗБІР ТА НАГОЛОС
    // -------------------------------------------------------------------------
    private void SyntaxBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SyntaxPopup != null) SyntaxPopup.IsOpen = !SyntaxPopup.IsOpen;
    }

    private void SyntaxAccentBtn_Click(object sender, RoutedEventArgs e)
    {
        InsertTextAtCaret("\u0301");
        ShowAiStatus("Додано знак наголосу (´)");
    }

    private void SyntaxSubjectBtn_Click(object sender, RoutedEventArgs e) => InsertSyntaxUnderline("Subject");
    private void SyntaxPredicateBtn_Click(object sender, RoutedEventArgs e) => InsertSyntaxUnderline("Predicate");
    private void SyntaxObjectBtn_Click(object sender, RoutedEventArgs e) => InsertSyntaxUnderline("Object");
    private void SyntaxAttributeBtn_Click(object sender, RoutedEventArgs e) => InsertSyntaxUnderline("Attribute");
    private void SyntaxAdverbialBtn_Click(object sender, RoutedEventArgs e) => InsertSyntaxUnderline("Adverbial");

    private void InsertSyntaxUnderline(string kind)
    {
        double width = 80.0;
        double x = 140.0;
        double y = 200.0;

        if (_activeNotebookTextBox != null)
        {
            x = Canvas.GetLeft(_activeNotebookTextBox) + 6;
            y = Canvas.GetTop(_activeNotebookTextBox) + _activeNotebookTextBox.FontSize + 8;
            if (_activeNotebookTextBox.SelectionLength > 0)
            {
                width = Math.Max(30.0, _activeNotebookTextBox.SelectionLength * 16.0);
            }
        }

        var stroke = new SolidColorBrush(_currentInkColor);
        stroke.Freeze();

        WpfPath path = new WpfPath { Stroke = stroke, StrokeThickness = 2.0, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

        switch (kind)
        {
            case "Subject":
                path.Data = Geometry.Parse($"M 0,4 H {width}");
                break;

            case "Predicate":
                var geomGroup = new GeometryGroup();
                geomGroup.Children.Add(new LineGeometry(new Point(0, 2), new Point(width, 2)));
                geomGroup.Children.Add(new LineGeometry(new Point(0, 7), new Point(width, 7)));
                path.Data = geomGroup;
                break;

            case "Object":
                path.Data = Geometry.Parse($"M 0,4 H {width}");
                path.StrokeDashArray = new DoubleCollection { 4, 3 };
                break;

            case "Attribute":
                var waveGeo = new StreamGeometry();
                using (var ctx = waveGeo.Open())
                {
                    ctx.BeginFigure(new Point(0, 4), false, false);
                    double step = 8.0;
                    for (double cur = 0; cur < width; cur += step * 2)
                    {
                        ctx.BezierTo(new Point(cur + step / 2, 0), new Point(cur + step, 8), new Point(cur + step * 2, 4), true, true);
                    }
                }
                waveGeo.Freeze();
                path.Data = waveGeo;
                break;

            case "Adverbial":
                path.Data = Geometry.Parse($"M 0,4 H {width}");
                path.StrokeDashArray = new DoubleCollection { 6, 2, 1.5, 2 };
                break;
        }

        var container = new Border { Child = path, Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        Canvas.SetLeft(container, x);
        Canvas.SetTop(container, y);
        MakeElementDraggable(container);
        MainCanvas.Children.Add(container);

        ShowAiStatus($"Додано член речення: {kind}");
    }

    // -------------------------------------------------------------------------
    // 3. ФОНЕТИЧНИЙ АНАЛІЗ (ДУЖКИ, М'ЯКІСТЬ, ДОВГОТА, АФРИКАТИ, ФІШКИ)
    // -------------------------------------------------------------------------
    private void PhoneticsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (PhoneticsPopup != null) PhoneticsPopup.IsOpen = !PhoneticsPopup.IsOpen;
    }

    private void PhoneticBracketSquare_Click(object sender, RoutedEventArgs e) => WrapSelectedText("[", "]");
    private void PhoneticBracketSlash_Click(object sender, RoutedEventArgs e) => WrapSelectedText("/", "/");
    private void PhoneticSoftSign_Click(object sender, RoutedEventArgs e) => InsertTextAtCaret("’");
    private void PhoneticLengthSign_Click(object sender, RoutedEventArgs e) => InsertTextAtCaret(":");
    private void PhoneticAfricateSign_Click(object sender, RoutedEventArgs e) => InsertTextAtCaret("\u0361");

    private void PhoneticChipVowel_Click(object sender, RoutedEventArgs e) => InsertPhoneticSoundChip("Vowel");
    private void PhoneticChipHardConsonant_Click(object sender, RoutedEventArgs e) => InsertPhoneticSoundChip("Hard");
    private void PhoneticChipSoftConsonant_Click(object sender, RoutedEventArgs e) => InsertPhoneticSoundChip("Soft");

    private void InsertPhoneticSoundChip(string kind)
    {
        double x = 150.0;
        double y = 240.0;
        if (_activeNotebookTextBox != null)
        {
            x = Canvas.GetLeft(_activeNotebookTextBox) + 12;
            y = Canvas.GetTop(_activeNotebookTextBox) + _activeNotebookTextBox.FontSize + 14;
        }

        FrameworkElement chip;
        switch (kind)
        {
            case "Vowel":
                chip = new Ellipse { Width = 18, Height = 18, Fill = new SolidColorBrush(Color.FromRgb(220, 38, 38)) };
                break;

            case "Soft":
                var pSoft = new WpfPath
                {
                    Data = Geometry.Parse("M 0,3 H 20 M 0,9 H 20"),
                    Stroke = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                    StrokeThickness = 2.5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                chip = new Border { Child = pSoft, Width = 22, Height = 14, Background = Brushes.Transparent };
                break;

            case "Hard":
            default:
                var pHard = new WpfPath
                {
                    Data = Geometry.Parse("M 0,5 H 20"),
                    Stroke = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                    StrokeThickness = 2.5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                chip = new Border { Child = pHard, Width = 22, Height = 12, Background = Brushes.Transparent };
                break;
        }

        var wrapper = new Border { Child = chip, Cursor = Cursors.SizeAll, Margin = new Thickness(4) };
        Canvas.SetLeft(wrapper, x);
        Canvas.SetTop(wrapper, y);
        MakeElementDraggable(wrapper);
        MainCanvas.Children.Add(wrapper);

        ShowAiStatus($"Додано звукову схему: {kind}");
    }

    // -------------------------------------------------------------------------
    // 4. IPA ТРАНСКРИПЦІЯ (INTERNATIONAL PHONETIC ALPHABET)
    // -------------------------------------------------------------------------
    private void IpaKeyboardToggle_Click(object sender, RoutedEventArgs e)
    {
        if (IpaKeyboardPopup != null)
        {
            IpaKeyboardPopup.IsOpen = !IpaKeyboardPopup.IsOpen;
        }
    }

    private void IpaSymbolKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Content is string symbol)
        {
            InsertTextAtCaret(symbol);
        }
    }

    // -------------------------------------------------------------------------
    // 5. ДРОБИ (FRACTIONS CONSTRUCTOR)
    // -------------------------------------------------------------------------
    private void OpenFractionModal_Click(object sender, RoutedEventArgs e)
    {
        if (FractionModal != null)
        {
            FractionModal.Visibility = Visibility.Visible;
            if (FractionWholeBox != null) FractionWholeBox.Text = "";
            if (FractionNumeratorBox != null) FractionNumeratorBox.Text = "1";
            if (FractionDenominatorBox != null) FractionDenominatorBox.Text = "2";
            UpdateFractionPreview();
        }
    }

    private void CloseFractionModal_Click(object sender, RoutedEventArgs e)
    {
        if (FractionModal != null) FractionModal.Visibility = Visibility.Collapsed;
    }

    private void FractionInput_TextChanged(object sender, TextChangedEventArgs e) => UpdateFractionPreview();

    private void UpdateFractionPreview()
    {
        if (FractionPreviewBlock == null) return;
        string whole = FractionWholeBox?.Text?.Trim() ?? "";
        string num = FractionNumeratorBox?.Text?.Trim() ?? "1";
        string den = FractionDenominatorBox?.Text?.Trim() ?? "2";

        FractionPreviewBlock.Text = string.IsNullOrEmpty(whole) ? $"{num} / {den}" : $"{whole} {num}/{den}";
    }

    private void InsertFractionBtn_Click(object sender, RoutedEventArgs e)
    {
        string whole = FractionWholeBox?.Text?.Trim() ?? "";
        string num = FractionNumeratorBox?.Text?.Trim() ?? "1";
        string den = FractionDenominatorBox?.Text?.Trim() ?? "2";

        CloseFractionModal_Click(sender, e);

        var fractionStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = WpfVerticalAlignment.Center };
        if (!string.IsNullOrEmpty(whole))
        {
            fractionStack.Children.Add(new TextBlock
            {
                Text = whole,
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Cambria Math, Segoe UI"),
                Foreground = new SolidColorBrush(_currentInkColor),
                VerticalAlignment = WpfVerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
        }

        var fracVertical = new StackPanel { HorizontalAlignment = WpfHorizontalAlignment.Center };
        fracVertical.Children.Add(new TextBlock
        {
            Text = num,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Cambria Math, Segoe UI"),
            Foreground = new SolidColorBrush(_currentInkColor),
            HorizontalAlignment = WpfHorizontalAlignment.Center
        });
        fracVertical.Children.Add(new Border
        {
            Height = 2.0,
            Background = new SolidColorBrush(_currentInkColor),
            Margin = new Thickness(0, 1, 0, 1),
            MinWidth = Math.Max(20.0, Math.Max(num.Length, den.Length) * 12.0)
        });
        fracVertical.Children.Add(new TextBlock
        {
            Text = den,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Cambria Math, Segoe UI"),
            Foreground = new SolidColorBrush(_currentInkColor),
            HorizontalAlignment = WpfHorizontalAlignment.Center
        });

        fractionStack.Children.Add(fracVertical);

        var border = new Border
        {
            Child = fractionStack,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
            Padding = new Thickness(4)
        };

        Canvas.SetLeft(border, 160);
        Canvas.SetTop(border, 220);
        MakeElementDraggable(border);
        MainCanvas.Children.Add(border);

        ShowAiStatus("Дріб успішно вставлено на аркуш");
    }

    // -------------------------------------------------------------------------
    // 6. НОТНИЙ СТАН І НОТИ (MUSIC STAVE & NOTES)
    // -------------------------------------------------------------------------
    private void MusicNotesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (MusicNotesPopup != null) MusicNotesPopup.IsOpen = !MusicNotesPopup.IsOpen;
    }

    private void MusicClefTreble_Click(object sender, RoutedEventArgs e) => InsertMusicSymbol("𝄞", 38);
    private void MusicClefBass_Click(object sender, RoutedEventArgs e) => InsertMusicSymbol("𝄢", 38);
    private void MusicNoteQuarter_Click(object sender, RoutedEventArgs e) => InsertMusicSymbol("♩", 34);
    private void MusicNoteEighth_Click(object sender, RoutedEventArgs e) => InsertMusicSymbol("♪", 34);
    private void MusicNoteBeamed_Click(object sender, RoutedEventArgs e) => InsertMusicSymbol("♫", 34);
    private void MusicAccidentalSharp_Click(object sender, RoutedEventArgs e) => InsertMusicSymbol("♯", 28);
    private void MusicAccidentalFlat_Click(object sender, RoutedEventArgs e) => InsertMusicSymbol("♭", 28);
    private void MusicAccidentalNatural_Click(object sender, RoutedEventArgs e) => InsertMusicSymbol("♮", 28);

    private void InsertMusicSymbol(string glyph, double size)
    {
        var textBlock = new TextBlock
        {
            Text = glyph,
            FontSize = size,
            FontFamily = new FontFamily("Segoe UI Symbol, Cambria Math, Segoe UI"),
            Foreground = new SolidColorBrush(_currentInkColor),
            VerticalAlignment = WpfVerticalAlignment.Center
        };

        var border = new Border
        {
            Child = textBlock,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
            Padding = new Thickness(2)
        };

        Canvas.SetLeft(border, 160);
        Canvas.SetTop(border, 200);
        MakeElementDraggable(border);
        MainCanvas.Children.Add(border);

        ShowAiStatus($"Вставлено нотний символ: {glyph}");
    }

    // -------------------------------------------------------------------------
    // ДОПОМІЖНІ МЕТОДИ ДЛЯ ТЕКСТУ ТА ПЕРЕТЯГУВАННЯ
    // -------------------------------------------------------------------------
    private void InsertTextAtCaret(string text)
    {
        if (_activeNotebookTextBox != null)
        {
            int start = _activeNotebookTextBox.SelectionStart;
            _activeNotebookTextBox.Text = _activeNotebookTextBox.Text.Insert(start, text);
            _activeNotebookTextBox.SelectionStart = start + text.Length;
            _activeNotebookTextBox.Focus();
        }
        else
        {
            CreateFloatingTextBlock(text, 160, 200);
        }
    }

    private void WrapSelectedText(string prefix, string suffix)
    {
        if (_activeNotebookTextBox != null && _activeNotebookTextBox.SelectionLength > 0)
        {
            int start = _activeNotebookTextBox.SelectionStart;
            int len = _activeNotebookTextBox.SelectionLength;
            string selected = _activeNotebookTextBox.SelectedText;
            _activeNotebookTextBox.Text = _activeNotebookTextBox.Text.Remove(start, len).Insert(start, $"{prefix}{selected}{suffix}");
            _activeNotebookTextBox.SelectionStart = start + prefix.Length;
            _activeNotebookTextBox.SelectionLength = len;
            _activeNotebookTextBox.Focus();
        }
        else
        {
            InsertTextAtCaret($"{prefix}{suffix}");
        }
    }

    private void CreateFloatingTextBlock(string text, double x, double y)
    {
        var tb = new TextBox
        {
            Text = text,
            FontSize = CurrentDefaultFontKind == "Propysy" ? 34.0 : 26.0,
            FontFamily = new FontFamily(CurrentDefaultFontKind == "Propysy" ? "Propysy" : "Segoe UI"),
            Foreground = new SolidColorBrush(_currentInkColor),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.IBeam,
            AcceptsReturn = true
        };

        tb.GotFocus += (s, e) => _activeNotebookTextBox = tb;
        tb.LostFocus += (s, e) => { if (_activeNotebookTextBox == tb) _activeNotebookTextBox = null; };

        var wrapper = new Border
        {
            Child = tb,
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 59, 130, 246)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2)
        };
        tb.GotFocus += (s, e) => { _activeNotebookTextBox = tb; wrapper.BorderThickness = new Thickness(1); };
        tb.LostFocus += (s, e) => { if (_activeNotebookTextBox == tb) _activeNotebookTextBox = null; wrapper.BorderThickness = new Thickness(0); };

        Canvas.SetLeft(wrapper, x);
        Canvas.SetTop(wrapper, y);
        MakeElementDraggable(wrapper);
        MainCanvas.Children.Add(wrapper);
        tb.Focus();
    }

    private void MakeElementDraggable(FrameworkElement element)
    {
        bool isDragging = false;
        Point clickPoint = default;

        element.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 1 && Keyboard.Modifiers != ModifierKeys.Control)
            {
                isDragging = true;
                clickPoint = e.GetPosition(MainCanvas);
                element.CaptureMouse();
                e.Handled = true;
            }
        };

        element.MouseMove += (s, e) =>
        {
            if (isDragging)
            {
                Point current = e.GetPosition(MainCanvas);
                double deltaX = current.X - clickPoint.X;
                double deltaY = current.Y - clickPoint.Y;

                double curLeft = Canvas.GetLeft(element);
                double curTop = Canvas.GetTop(element);
                if (double.IsNaN(curLeft)) curLeft = 0;
                if (double.IsNaN(curTop)) curTop = 0;

                Canvas.SetLeft(element, curLeft + deltaX);
                Canvas.SetTop(element, curTop + deltaY);

                clickPoint = current;
                e.Handled = true;
            }
        };

        element.MouseLeftButtonUp += (s, e) =>
        {
            if (isDragging)
            {
                isDragging = false;
                element.ReleaseMouseCapture();
                e.Handled = true;
            }
        };
    }

    private void SubjectToolsMenuBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SubjectToolsMenuPopup != null)
        {
            SubjectToolsMenuPopup.IsOpen = !SubjectToolsMenuPopup.IsOpen;
        }
    }

    private void SubjectToolItem_Click(object sender, RoutedEventArgs e)
    {
        if (SubjectToolsMenuPopup != null) SubjectToolsMenuPopup.IsOpen = false;
        if (sender is FrameworkElement fe && fe.Tag is string tag)
        {
            switch (tag)
            {
                case "Morphemes":
                    if (MorphemesPopup != null) MorphemesPopup.IsOpen = true;
                    break;
                case "Syntax":
                    if (SyntaxPopup != null) SyntaxPopup.IsOpen = true;
                    break;
                case "Phonetics":
                    if (PhoneticsPopup != null) PhoneticsPopup.IsOpen = true;
                    break;
                case "Ipa":
                    if (IpaKeyboardPopup != null) IpaKeyboardPopup.IsOpen = true;
                    break;
                case "Column":
                    OpenColumnArithmeticModal("Add");
                    break;
                case "Fractions":
                    OpenFractionModal_Click(sender, e);
                    break;
                case "Music":
                    if (MusicNotesPopup != null) MusicNotesPopup.IsOpen = true;
                    break;
            }
        }
    }
}