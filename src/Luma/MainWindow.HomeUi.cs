using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using Ellipse = System.Windows.Shapes.Ellipse;
using FontFamily = System.Windows.Media.FontFamily;
using HAlign = System.Windows.HorizontalAlignment;
using IconPath = System.Windows.Shapes.Path;
using Image = System.Windows.Controls.Image;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;
using TextBox = System.Windows.Controls.TextBox;
using VAlign = System.Windows.VerticalAlignment;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DataObject = System.Windows.DataObject;
using DragEventArgs = System.Windows.DragEventArgs;

namespace Luma;

/// <summary>Zen-styled building blocks, the widget gallery, the studio drawer and the look of the start page.</summary>
public partial class MainWindow
{
    private bool _homeStudioOpen;
    private string _studioTab = "widgets";
    private string? _studioExpanded;
    private double _studioScrollOffset;

    // ---------------------------------------------------------------- primitives
    private FrameworkElement HIcon(string key, double size = 15, string brushKey = "ZenIconBrush", double thickness = 1.5)
    {
        var geometry = TryFindResource("ZLine" + key) as Geometry ?? Geometry.Empty;
        var path = new IconPath
        {
            Data = geometry,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Stretch = System.Windows.Media.Stretch.None,
            Width = 16,
            Height = 16
        };
        path.SetResourceReference(Shape.StrokeProperty, brushKey);
        var canvas = new Canvas { Width = 16, Height = 16 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false, HorizontalAlignment = HAlign.Center, VerticalAlignment = VAlign.Center };
    }

    private static void SetBgKey(Border b, string? key)
    {
        if (key is null) b.Background = Brushes.Transparent;
        else b.SetResourceReference(Border.BackgroundProperty, key);
    }

    private Button HBtn(UIElement content, Action onClick, string? tip, string? normalKey, string hoverKey, double radius, Thickness pad, double? height = null, double? width = null, Brush? normalBrush = null)
    {
        var bd = new Border { CornerRadius = new CornerRadius(radius), Padding = pad, Child = content };
        void Normal()
        {
            if (normalBrush is not null) bd.Background = normalBrush;
            else SetBgKey(bd, normalKey);
        }
        Normal();
        var btn = new Button { Template = CreateSimpleBorderButtonTemplate(), Content = bd, Cursor = Cursors.Hand, Focusable = false, Background = Brushes.Transparent };
        if (height.HasValue) btn.Height = height.Value;
        if (width.HasValue) btn.Width = width.Value;
        if (tip is not null) btn.ToolTip = tip;
        bd.MouseEnter += (_, _) => bd.SetResourceReference(Border.BackgroundProperty, hoverKey);
        bd.MouseLeave += (_, _) => Normal();
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private Button HChip(string text, bool selected, Action onClick, string? icon = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HAlign.Center, VerticalAlignment = VAlign.Center };
        if (icon is not null)
        {
            row.Children.Add(HIcon(icon, 13, selected ? "ZenTextBrush" : "ZenIconBrush"));
            var gap = new Border { Width = 6 };
            row.Children.Add(gap);
        }
        var t = Txt(text, 12, selected);
        t.SetResourceReference(TextBlock.ForegroundProperty, selected ? "ZenTextBrush" : "ZenMutedBrush");
        row.Children.Add(t);
        return HBtn(row, onClick, null, selected ? "Zen_5E5E64" : "Zen_4B4B50", selected ? "Zen_5E5E64" : "Zen_56565A", 8, new Thickness(10, 0, 10, 0), 30);
    }

    private FrameworkElement HChipRow((string Value, string Label)[] options, string current, Action<string> onPick)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (value, label) in options)
        {
            var chip = HChip(label, string.Equals(value, current, StringComparison.OrdinalIgnoreCase), () => onPick(value));
            chip.Margin = new Thickness(0, 0, 6, 6);
            panel.Children.Add(chip);
        }
        return panel;
    }

    private FrameworkElement HToggle(bool on, Action<bool> onChange)
    {
        var track = new Border { Width = 36, Height = 20, CornerRadius = new CornerRadius(10) };
        var knob = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = Brushes.White, VerticalAlignment = VAlign.Center };
        void Paint()
        {
            track.SetResourceReference(Border.BackgroundProperty, on ? "AccentBrush" : "Zen_5E5E64");
            knob.HorizontalAlignment = on ? HAlign.Right : HAlign.Left;
            knob.Margin = on ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
        }
        Paint();
        var g = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent, Width = 36, Height = 20 };
        g.Children.Add(track);
        g.Children.Add(knob);
        g.MouseLeftButtonUp += (_, _) => { on = !on; Paint(); onChange(on); };
        return g;
    }

    private FrameworkElement HStepper(double value, double min, double max, double step, Func<double, string> format, Action<double> onChange)
    {
        var cur = value;
        var label = Txt(format(cur), 12);
        label.Width = 54;
        label.TextAlignment = TextAlignment.Center;
        label.VerticalAlignment = VAlign.Center;
        var minus = HBtn(HIcon("Minus", 12), () => { cur = Math.Max(min, Math.Round(cur - step, 2)); label.Text = format(cur); onChange(cur); }, null, "Zen_4B4B50", "Zen_56565A", 8, new Thickness(0), 28, 28);
        var plus = HBtn(HIcon("Plus", 12), () => { cur = Math.Min(max, Math.Round(cur + step, 2)); label.Text = format(cur); onChange(cur); }, null, "Zen_4B4B50", "Zen_56565A", 8, new Thickness(0), 28, 28);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HAlign.Right };
        row.Children.Add(minus);
        row.Children.Add(label);
        row.Children.Add(plus);
        return row;
    }

    private FrameworkElement HRow(string label, FrameworkElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = Txt(label, 12.5);
        t.VerticalAlignment = VAlign.Center;
        t.TextWrapping = TextWrapping.Wrap;
        t.TextTrimming = TextTrimming.None;
        g.Children.Add(t);
        control.VerticalAlignment = VAlign.Center;
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    private FrameworkElement HSection(string title)
    {
        var t = Muted(title.ToUpperInvariant(), 10.5);
        t.FontWeight = FontWeights.Bold;
        t.Margin = new Thickness(2, 16, 0, 6);
        return t;
    }

    private Border HInput(string value, string placeholder, Action<string> onChanged, bool multiline = false)
    {
        var shell = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, multiline ? 6 : 0, 10, multiline ? 6 : 0), MinHeight = multiline ? 70 : 32 };
        shell.SetResourceReference(Border.BackgroundProperty, "Zen_2C2C32");
        var box = new TextBox
        {
            Text = value,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 12.5,
            VerticalContentAlignment = multiline ? VAlign.Top : VAlign.Center,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap
        };
        box.SetResourceReference(TextBox.ForegroundProperty, "ZenTextBrush");
        box.SetResourceReference(TextBox.CaretBrushProperty, "ZenTextBrush");
        var hint = Muted(placeholder, 12.5);
        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = multiline ? VAlign.Top : VAlign.Center;
        hint.Visibility = string.IsNullOrEmpty(value) ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) =>
        {
            hint.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
            onChanged(box.Text);
        };
        var g = new Grid();
        g.Children.Add(box);
        g.Children.Add(hint);
        shell.Child = g;
        return shell;
    }

    private Button HSwatch(string hex, bool selected, Action onClick)
    {
        var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = RawBrush(hex), BorderThickness = new Thickness(selected ? 2 : 1) };
        dot.SetResourceReference(Border.BorderBrushProperty, selected ? "ZenTextBrush" : "Zen_5E5E64");
        var btn = new Button { Template = CreateSimpleBorderButtonTemplate(), Content = dot, Cursor = Cursors.Hand, Focusable = false, Margin = new Thickness(0, 0, 8, 8) };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private Popup ShowHomePopup(FrameworkElement anchor, FrameworkElement content, double width, PlacementMode placement = PlacementMode.Bottom)
    {
        var pop = new Popup
        {
            PlacementTarget = anchor,
            Placement = placement,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade
        };
        var shell = new Border
        {
            Width = width + 24,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Background = Brushes.Transparent,
            Child = new Border
            {
                Margin = new Thickness(12),
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Child = content,
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.45, Color = Colors.Black }
            }
        };
        var inner = (Border)shell.Child;
        inner.SetResourceReference(Border.BackgroundProperty, "Zen_3D3D42");
        inner.SetResourceReference(Border.BorderBrushProperty, "Zen_505056");
        pop.Child = shell;
        EmptyBackground.Children.Add(pop);
        pop.Closed += (_, _) => EmptyBackground.Children.Remove(pop);
        pop.IsOpen = true;
        return pop;
    }

    // ---------------------------------------------------------------- gallery / editors
    private void ShowWidgetGallery(FrameworkElement anchor)
    {
        var list = new StackPanel();
        var title = Muted(T("Добавить виджет", "Add a widget"), 11);
        title.FontWeight = FontWeights.Bold;
        title.Margin = new Thickness(4, 0, 0, 8);
        list.Children.Add(title);
        Popup? pop = null;
        foreach (var c in HomeCatalog)
        {
            if (c.Single && _homeLayout.Widgets.Any(x => x.Type == c.Type)) continue;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = HIcon(c.Icon, 16);
            icon.HorizontalAlignment = HAlign.Left;
            row.Children.Add(icon);
            var texts = new StackPanel();
            texts.Children.Add(Txt(T(c.Ru, c.En), 12.5, true));
            var desc = Muted(T(c.RuDesc, c.EnDesc), 10.5);
            desc.TextWrapping = TextWrapping.NoWrap;
            desc.TextTrimming = TextTrimming.CharacterEllipsis;
            texts.Children.Add(desc);
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);
            var type = c.Type;
            var btn = HBtn(row, () => { if (pop is not null) pop.IsOpen = false; AddHomeWidget(type); }, null, null, "Zen_4B4B50", 8, new Thickness(8, 7, 8, 7));
            btn.Margin = new Thickness(0, 0, 0, 2);
            list.Children.Add(btn);
        }
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 420, Content = list };
        pop = ShowHomePopup(anchor, scroll, 300);
    }

    private void ShowWidgetSettings(HomeWidgetState w, FrameworkElement anchor)
    {
        var host = new StackPanel();
        var title = Txt(HomeWidgetName(w.Type), 12.5, true);
        title.Margin = new Thickness(0, 0, 0, 8);
        host.Children.Add(title);
        BuildWidgetOptions(w, host);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 380, Content = host };
        ShowHomePopup(anchor, scroll, 290);
    }

    private void AddHomeWidget(string type)
    {
        var info = HomeCatalog.FirstOrDefault(c => c.Type == type);
        if (info.Type is null) return;
        var w = new HomeWidgetState { Type = type, Span = info.Span };
        _homeLayout.Widgets.Add(w);
        _studioExpanded = w.Id;
        HomeLayoutChanged();
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => _homeScroll?.ScrollToEnd()));
    }

    private void ShowShortcutEditor(HomeWidgetState w, FrameworkElement anchor)
    {
        string name = "", url = "";
        var host = new StackPanel();
        var title = Muted(T("Новый ярлык", "New shortcut"), 11);
        title.FontWeight = FontWeights.Bold;
        title.Margin = new Thickness(2, 0, 0, 8);
        host.Children.Add(title);
        host.Children.Add(HInput("", T("Название (необязательно)", "Name (optional)"), v => name = v));
        var urlBox = HInput("", T("Адрес, например github.com", "Address, e.g. github.com"), v => url = v);
        urlBox.Margin = new Thickness(0, 6, 0, 0);
        host.Children.Add(urlBox);
        Popup? pop = null;
        var add = HBtn(Txt(T("Добавить", "Add"), 12.5, true), () =>
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            var list = ReadShortcuts(w);
            list.Add(new HomeShortcut { T = name.Trim(), U = url.Trim() });
            w.Data = JsonSerializer.Serialize(list);
            if (pop is not null) pop.IsOpen = false;
            HomeLayoutChanged();
        }, null, "Zen_5E5E64", "Zen_686870", 8, new Thickness(0), 32);
        add.Margin = new Thickness(0, 10, 0, 0);
        if (add.Content is Border ab && ab.Child is TextBlock at) at.HorizontalAlignment = HAlign.Center;
        if (add.Content is Border ab2) ab2.Padding = new Thickness(0, 6, 0, 0);
        host.Children.Add(add);
        pop = ShowHomePopup(anchor, host, 260);
    }

    internal void ShowPinnedSiteEditor(HomeWidgetState w, FrameworkElement anchor, HomePinnedSite? editing = null)
    {
        string name = editing?.Title ?? "";
        string url = editing?.Url ?? "";
        string logoText = editing?.LogoText ?? "";
        string bg = editing?.Bg ?? "#FFFFFF";
        string fg = editing?.Fg ?? "#1E2024";
        string badgeBg = editing?.BadgeBg ?? "";
        string badgeText = editing?.BadgeText ?? "";

        var host = new StackPanel();
        var title = Muted(T(editing is null ? "Новый закрепленный сайт" : "Редактировать сайт", editing is null ? "New pinned site" : "Edit pinned site"), 11);
        title.FontWeight = FontWeights.Bold;
        title.Margin = new Thickness(2, 0, 0, 8);
        host.Children.Add(title);

        var presetsLabel = Muted(T("БЫСТРЫЕ ШАБЛОНЫ (КАК В OPERA)", "QUICK TEMPLATES"), 9.5);
        presetsLabel.Margin = new Thickness(2, 0, 0, 4);
        host.Children.Add(presetsLabel);

        var presetsPanel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        (string Label, string PUrl, string PTitle, string PBg, string PFg, string PLogo, string? PBadgeBg, string? PBadgeText)[] presets =
        [
            ("Booking", "https://www.booking.com", "Booking.com", "#003580", "#FFFFFF", "Booking.com", null, null),
            ("AliExpress", "https://www.aliexpress.com", "AliExpress", "#FFFFFF", "#E62E04", "AliExpress", null, null),
            ("e·katalog", "https://ek.ua", "e·katalog", "#FFFFFF", "#1F64AF", "e·katalog", null, null),
            ("Comfy", "https://comfy.ua", "Comfy", "#FFFFFF", "#FFFFFF", "", "#43B02A", "COMFY"),
            ("Prom", "https://prom.ua", "Prom", "#FFFFFF", "#6C27BD", "prom", null, null),
            ("Temu", "https://www.temu.com", "Temu", "#FFFFFF", "#FFFFFF", "", "#FA541C", "TEMU"),
            ("Rozetka", "https://rozetka.com.ua", "Rozetka", "#FFFFFF", "#00A046", "ROZETKA", null, null),
            ("Preply", "https://preply.com", "Preply", "#F95C79", "#1A1A1A", "Preply", null, null),
            ("YouTube", "https://youtube.com", "YouTube", "#FFFFFF", "#FF0000", "YouTube", null, null),
            ("GitHub", "https://github.com", "GitHub", "#181A20", "#FFFFFF", "GitHub", null, null),
            ("Discord", "https://discord.com", "Discord", "#5865F2", "#FFFFFF", "Discord", null, null),
            ("Telegram", "https://web.telegram.org", "Telegram", "#24A1DE", "#FFFFFF", "Telegram", null, null),
        ];

        TextBox? nameTb = null;
        TextBox? urlTb = null;
        TextBox? logoTb = null;

        foreach (var p in presets)
        {
            var chip = HChip(p.Label, false, () =>
            {
                url = p.PUrl;
                name = p.PTitle;
                bg = p.PBg;
                fg = p.PFg;
                logoText = p.PLogo;
                badgeBg = p.PBadgeBg ?? "";
                badgeText = p.PBadgeText ?? "";
                if (urlTb is not null) urlTb.Text = url;
                if (nameTb is not null) nameTb.Text = name;
                if (logoTb is not null) logoTb.Text = logoText;
            });
            chip.Margin = new Thickness(0, 0, 4, 4);
            presetsPanel.Children.Add(chip);
        }
        host.Children.Add(presetsPanel);

        var nameInput = HInput(name, T("Название (например Booking.com)", "Name (e.g. Booking.com)"), v => name = v);
        if (nameInput.Child is Grid ng && ng.Children.OfType<TextBox>().FirstOrDefault() is { } ntb) nameTb = ntb;
        host.Children.Add(nameInput);

        var urlInput = HInput(url, T("Адрес сайта (например booking.com)", "Site address"), v =>
        {
            url = v;
            if (string.IsNullOrEmpty(name))
            {
                name = BrowserTab.DomainOf(v);
                if (nameTb is not null) nameTb.Text = name;
            }
        });
        urlInput.Margin = new Thickness(0, 6, 0, 0);
        if (urlInput.Child is Grid ug && ug.Children.OfType<TextBox>().FirstOrDefault() is { } utb) urlTb = utb;
        host.Children.Add(urlInput);

        var logoInput = HInput(logoText, T("Текст на карточке (например Booking.com)", "Card text logo"), v => logoText = v);
        logoInput.Margin = new Thickness(0, 6, 0, 0);
        if (logoInput.Child is Grid lg && lg.Children.OfType<TextBox>().FirstOrDefault() is { } ltb) logoTb = ltb;
        host.Children.Add(logoInput);

        var colorsLabel = Muted(T("ЦВЕТ КАРТОЧКИ", "CARD COLOR"), 9.5);
        colorsLabel.Margin = new Thickness(2, 8, 0, 4);
        host.Children.Add(colorsLabel);

        var swatchPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        string[] swatches = ["#FFFFFF", "#003580", "#F95C79", "#00A046", "#5865F2", "#FA541C", "#E50914", "#181A20", "#334155"];
        foreach (var hex in swatches)
        {
            var sw = HSwatch(hex, string.Equals(bg, hex, StringComparison.OrdinalIgnoreCase), () =>
            {
                bg = hex;
                if (hex == "#FFFFFF") fg = "#1E2024";
                else fg = "#FFFFFF";
            });
            swatchPanel.Children.Add(sw);
        }
        host.Children.Add(swatchPanel);

        Popup? pop = null;
        var add = HBtn(Txt(T(editing is null ? "Добавить" : "Сохранить", editing is null ? "Add" : "Save"), 12.5, true), () =>
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            var list = ReadPinnedSites(w);
            if (editing is not null)
            {
                editing.Title = string.IsNullOrWhiteSpace(name) ? BrowserTab.DomainOf(url) : name.Trim();
                editing.Url = url.Trim();
                editing.Bg = bg;
                editing.Fg = fg;
                editing.LogoText = string.IsNullOrWhiteSpace(logoText) ? editing.Title : logoText.Trim();
                editing.BadgeBg = badgeBg;
                editing.BadgeText = badgeText;
            }
            else
            {
                list.Add(new HomePinnedSite
                {
                    Title = string.IsNullOrWhiteSpace(name) ? BrowserTab.DomainOf(url) : name.Trim(),
                    Url = url.Trim(),
                    Bg = bg,
                    Fg = fg,
                    LogoText = string.IsNullOrWhiteSpace(logoText) ? (string.IsNullOrWhiteSpace(name) ? BrowserTab.DomainOf(url) : name.Trim()) : logoText.Trim(),
                    BadgeBg = badgeBg,
                    BadgeText = badgeText
                });
            }
            w.Data = JsonSerializer.Serialize(list);
            if (pop is not null) pop.IsOpen = false;
            HomeLayoutChanged();
        }, null, "Zen_5E5E64", "Zen_686870", 8, new Thickness(0), 32);
        add.Margin = new Thickness(0, 10, 0, 0);
        if (add.Content is Border ab && ab.Child is TextBlock at) at.HorizontalAlignment = HAlign.Center;
        if (add.Content is Border ab2) ab2.Padding = new Thickness(0, 6, 0, 0);
        host.Children.Add(add);

        pop = ShowHomePopup(anchor, host, 310);
    }

    // ---------------------------------------------------------------- dock + studio shell
    private void BuildHomeDock()
    {
        _homeDock = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HAlign.Right,
            VerticalAlignment = VAlign.Bottom,
            Margin = new Thickness(0, 0, 20, 18)
        };
        Panel.SetZIndex(_homeDock, 5);

        Button FloatBtn(UIElement content, Action act, string tip)
        {
            var bd = new Border
            {
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Width = 36,
                Height = 36,
                Child = content
            };
            var b = new Button
            {
                Template = CreateSimpleBorderButtonTemplate(),
                Content = bd,
                Cursor = Cursors.Hand,
                Focusable = false,
                ToolTip = tip,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            bd.MouseEnter += (_, _) =>
            {
                bd.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
                bd.BorderBrush = new SolidColorBrush(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF));
            };
            bd.MouseLeave += (_, _) =>
            {
                bd.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
                bd.BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            };
            b.Click += (_, _) => act();
            return b;
        }

        var builder = FloatBtn(HIcon("Pencil", 15, "ZenTextBrush", 1.5), ToggleHomeStudio, T("Конструктор стартовой", "Start page builder"));

        _homeDock.Children.Add(builder);
        EmptyBackground.Children.Add(_homeDock);
    }

    internal void UpdateHomeDockVisibility()
    {
        if (_homeDock is null) return;
        _homeDock.Visibility = _showAccountSurface ? Visibility.Collapsed : Visibility.Visible;
        _homeDock.Margin = new Thickness(0, 0, _homeStudioOpen ? 20 + 380 : 20, 18);
    }

    private void ToggleHomeStudio()
    {
        if (_homeStudioOpen) CloseHomeStudio(); else OpenHomeStudio();
    }

    internal void OpenHomeStudio(string tab = "widgets", string? widgetId = null)
    {
        _studioTab = tab;
        if (widgetId is not null) _studioExpanded = widgetId;
        var wasOpen = _homeStudioOpen;
        _homeStudioOpen = true;
        _homeEditMode = true;
        HomeCustomizeDrawerOverlay.Visibility = Visibility.Visible;
        if (!wasOpen)
        {
            HomeCustomizeDrawerOverlay.Opacity = 0;
            HomeStudioTranslate.X = 28;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            HomeCustomizeDrawerOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            HomeStudioTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });
        }
        UpdateHomeDockVisibility();
        RebuildHomeBuilder();
    }

    internal void CloseHomeStudio()
    {
        if (!_homeStudioOpen && HomeCustomizeDrawerOverlay.Visibility != Visibility.Visible) return;
        _homeStudioOpen = false;
        _homeEditMode = false;
        HomeCustomizeDrawerOverlay.Visibility = Visibility.Collapsed;
        SaveHomeLayout();
        UpdateHomeDockVisibility();
        RebuildHomeBuilder();
    }

        private void AlignHomeGrid()
    {
        var l = _homeLayout;
        double viewW = _homeScroll?.ActualWidth > 150 ? _homeScroll.ActualWidth : (_homeScroll?.ViewportWidth > 150 ? _homeScroll.ViewportWidth : 1200);
        double viewH = _homeScroll?.ActualHeight > 150 ? _homeScroll.ActualHeight : (_homeScroll?.ViewportHeight > 150 ? _homeScroll.ViewportHeight : 800);
        double effectiveW = Math.Min(l.Width, Math.Max(320.0, viewW - 32.0));
        double gap = l.Gap;
        double defaultStartX = Math.Max(16.0, Math.Round((viewW - effectiveW) / 2.0));
        double curY = l.VAlign == "top" ? 36.0 : Math.Max(28.0, Math.Round((viewH - 420.0) / 2.0));
        double col2W = Math.Max(40.0, (effectiveW - gap) / 2.0);

        HomeWidgetState? prevSpan2 = null;
        foreach (var w in l.Widgets.Where(x => x.Visible))
        {
            if (w.Span >= 4 || w.Type is "clock" or "headline" or "text" or "search" or "pinned" or "media")
            {
                if (prevSpan2 is not null)
                {
                    curY += 130;
                    prevSpan2 = null;
                }
                w.X = defaultStartX;
                w.Y = curY;
                curY += (w.Type is "clock" ? 72 : (w.Type is "text" or "headline" ? 44 : (w.Type is "search" ? 64 : 124)));
            }
            else if (w.Span == 2 || w.Type is "stats" or "pomodoro" or "calendar")
            {
                w.Span = 2;
                if (prevSpan2 is null)
                {
                    w.X = defaultStartX;
                    w.Y = curY;
                    prevSpan2 = w;
                }
                else
                {
                    w.X = defaultStartX + col2W + gap;
                    w.Y = prevSpan2.Y;
                    curY += 130;
                    prevSpan2 = null;
                }
            }
            else
            {
                w.X = defaultStartX;
                w.Y = curY;
                curY += 80;
            }
        }
        HomeLayoutChanged();
    }

private void RenderHomeStudio()
    {
        if (HomeStudioRoot is null) return;
        var previous = HomeStudioRoot.Children.OfType<ScrollViewer>().FirstOrDefault();
        if (previous is not null) _studioScrollOffset = previous.VerticalOffset;

        HomeStudioRoot.Children.Clear();
        HomeStudioRoot.RowDefinitions.Clear();
        HomeStudioRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        HomeStudioRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        HomeStudioRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // header
        var header = new Grid { Margin = new Thickness(16, 14, 12, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel();
        titles.Children.Add(Txt(T("Конструктор", "Builder"), 15, true));
        titles.Children.Add(Muted(T("Соберите стартовую под себя", "Make the start page yours"), 11));
        header.Children.Add(titles);
        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        var align = HBtn(HIcon("Grid", 12), AlignHomeGrid, T("Выровнять по сетке", "Align widgets"), "Zen_4B4B50", "Zen_56565A", 8, new Thickness(10, 0, 10, 0), 30);
        align.Margin = new Thickness(0, 0, 8, 0);
        btns.Children.Add(align);
        var done = HBtn(Txt(T("Готово", "Done"), 12.5, true), CloseHomeStudio, T("Сохранить и закрыть (Esc)", "Save and close (Esc)"), "Zen_5E5E64", "Zen_686870", 8, new Thickness(14, 0, 14, 0), 30);
        btns.Children.Add(done);
        Grid.SetColumn(btns, 1);
        header.Children.Add(btns);
        HomeStudioRoot.Children.Add(header);

        // tabs
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 6) };
        void Tab(string id, string ru, string en, string icon)
        {
            var chip = HChip(T(ru, en), _studioTab == id, () => { _studioTab = id; RenderHomeStudio(); }, icon);
            chip.Margin = new Thickness(0, 0, 6, 0);
            tabs.Children.Add(chip);
        }
        Tab("widgets", "Виджеты", "Widgets", "Grid");
        Tab("look", "Вид", "Look", "Drop");
        Tab("presets", "Шаблоны", "Presets", "Layers");
        Grid.SetRow(tabs, 1);
        HomeStudioRoot.Children.Add(tabs);

        var body = new StackPanel { Margin = new Thickness(16, 4, 16, 18) };
        switch (_studioTab)
        {
            case "look": BuildStudioLook(body); break;
            case "presets": BuildStudioPresets(body); break;
            default: BuildStudioWidgets(body); break;
        }
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = body };
        Grid.SetRow(scroll, 2);
        HomeStudioRoot.Children.Add(scroll);
        var offset = _studioScrollOffset;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => scroll.ScrollToVerticalOffset(offset)));
    }

    // ---------------------------------------------------------------- studio: widgets
    private void BuildStudioWidgets(StackPanel body)
    {
        Button? add = null;
        var addRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HAlign.Center, VerticalAlignment = VAlign.Center };
        addRow.Children.Add(HIcon("Plus", 14));
        var addText = Txt(T("Добавить виджет", "Add widget"), 12.5, true);
        addText.Margin = new Thickness(8, 0, 0, 0);
        addRow.Children.Add(addText);
        add = HBtn(addRow, () => ShowWidgetGallery(add!), null, "Zen_5E5E64", "Zen_686870", 10, new Thickness(0), 38);
        add.Margin = new Thickness(0, 4, 0, 6);
        body.Children.Add(add);

        var centerRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HAlign.Center, VerticalAlignment = VAlign.Center };
        centerRow.Children.Add(HIcon("Maximize", 12));
        var centerText = Muted(T("Выровнять виджеты по центру", "Center all widgets"), 11.5);
        centerText.Margin = new Thickness(6, 0, 0, 0);
        centerRow.Children.Add(centerText);
        var centerBtn = HBtn(centerRow, () =>
        {
            foreach (var w in _homeLayout.Widgets) { w.X = null; w.Y = null; }
            SaveHomeLayout();
            RebuildHomeBuilder();
        }, T("Сбросить координаты и центрировать", "Reset coordinates and center"), "Zen_3D3D42", "Zen_4B4B50", 8, new Thickness(8, 0, 8, 0), 28);
        centerBtn.Margin = new Thickness(0, 0, 0, 10);
        body.Children.Add(centerBtn);

        if (_homeLayout.Widgets.Count == 0)
            body.Children.Add(Muted(T("Виджетов нет. Добавьте первый.", "No widgets yet. Add the first one."), 12, true));

        foreach (var w in _homeLayout.Widgets.ToList())
        {
            var card = new Border { CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(10, 8, 8, 8), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "Zen_4B4B50");
            card.SetResourceReference(Border.BorderBrushProperty, "Zen_505056");
            var stack = new StackPanel();

            // Drag-and-drop support on studio list cards
            card.AllowDrop = true;
            card.DragOver += (_, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.StringFormat))
                {
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                    card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                }
            };
            card.DragLeave += (_, _) => card.SetResourceReference(Border.BorderBrushProperty, "Zen_505056");
            card.Drop += (_, e) =>
            {
                card.SetResourceReference(Border.BorderBrushProperty, "Zen_505056");
                if (e.Data.GetData(DataFormats.StringFormat) is string srcId && srcId != w.Id)
                {
                    ReorderHomeWidgets(srcId, w.Id);
                    e.Handled = true;
                }
            };

            var head = new Grid { Cursor = Cursors.SizeAll };
            Point cardDragStart = default;
            bool cardDown = false;
            head.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject dep && FindVisualAncestor<Button>(dep) is not null) return;
                cardDown = true;
                cardDragStart = e.GetPosition(null);
            };
            head.PreviewMouseMove += (_, e) =>
            {
                if (cardDown && e.LeftButton == MouseButtonState.Pressed)
                {
                    var diff = cardDragStart - e.GetPosition(null);
                    if (Math.Abs(diff.X) > 4 || Math.Abs(diff.Y) > 4)
                    {
                        cardDown = false;
                        var data = new DataObject(DataFormats.StringFormat, w.Id);
                        DragDrop.DoDragDrop(card, data, DragDropEffects.Move);
                    }
                }
            };
            head.PreviewMouseLeftButtonUp += (_, _) => cardDown = false;

            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = HomeCatalog.FirstOrDefault(c => c.Type == w.Type);
            var icon = HIcon(info.Icon ?? "Grid", 15);
            icon.HorizontalAlignment = HAlign.Left;
            head.Children.Add(icon);
            var name = Txt(HomeWidgetName(w.Type), 12.5, true);
            name.VerticalAlignment = VAlign.Center;
            name.Opacity = w.Visible ? 1 : 0.5;
            Grid.SetColumn(name, 1);
            head.Children.Add(name);

            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            Button Mini(string ic, string tip, Action act, bool active = false) =>
                HBtn(HIcon(ic, 12, active ? "ZenTextBrush" : "ZenIconBrush"), act, tip, active ? "Zen_5E5E64" : null, "Zen_56565A", 6, new Thickness(0), 24, 24);
            tools.Children.Add(Mini("Up", T("Выше", "Up"), () => MoveHomeWidget(w, -1)));
            tools.Children.Add(Mini("Down", T("Ниже", "Down"), () => MoveHomeWidget(w, +1)));
            tools.Children.Add(Mini("Eye", w.Visible ? T("Скрыть", "Hide") : T("Показать", "Show"), () => { w.Visible = !w.Visible; HomeLayoutChanged(); }, !w.Visible));
            var expanded = _studioExpanded == w.Id;
            tools.Children.Add(Mini("Sliders", T("Параметры", "Options"), () => { _studioExpanded = expanded ? null : w.Id; RenderHomeStudio(); }, expanded));
            tools.Children.Add(Mini("Trash", T("Удалить", "Remove"), () => { _homeLayout.Widgets.Remove(w); if (_studioExpanded == w.Id) _studioExpanded = null; HomeLayoutChanged(); }));
            Grid.SetColumn(tools, 2);
            head.Children.Add(tools);
            stack.Children.Add(head);

            if (expanded)
            {
                var opts = new StackPanel { Margin = new Thickness(0, 8, 0, 2) };
                BuildWidgetOptions(w, opts);
                stack.Children.Add(opts);
            }
            card.Child = stack;
            body.Children.Add(card);
        }
    }

    private void BuildWidgetOptions(HomeWidgetState w, StackPanel host)
    {
        void Changed() { NormalizeHomeLayout(); SaveHomeLayout(); RebuildHomeBuilder(); }

        void Chips(string label, string key, string def, (string, string)[] options)
        {
            host.Children.Add(Muted(label, 11));
            var row = HChipRow(options, O(w, key, def), v => { SetO(w, key, v); Changed(); });
            row.Margin = new Thickness(0, 4, 0, 2);
            host.Children.Add(row);
        }
        void Step(string label, string key, int def, int min, int max, int step, string suffix = "")
            => host.Children.Add(HRow(label, HStepper(OInt(w, key, def), min, max, step, v => $"{(int)v}{suffix}", v => { SetO(w, key, ((int)v).ToString()); Changed(); })));
        void Switch(string label, string key, bool def)
            => host.Children.Add(HRow(label, HToggle(O(w, key, def ? "1" : "0") == "1", v => { SetO(w, key, v ? "1" : "0"); Changed(); })));
        void Text(string label, string key, string placeholder)
        {
            host.Children.Add(Muted(label, 11));
            var box = HInput(O(w, key, ""), placeholder, v => { SetO(w, key, v); QueueHomeSave(); });
            box.Margin = new Thickness(0, 4, 0, 4);
            host.Children.Add(box);
            AttachRebuildOnBlur(box);
        }

        host.Children.Add(HRow(T("Ширина", "Width"), HStepper(w.Span, 1, 4, 1, v => $"{(int)v}/4", v => { w.Span = (int)v; Changed(); })));

        switch (w.Type)
        {
            case "search":
                Step(T("Высота строки", "Bar height"), "h", 50, 40, 76, 2, " px");
                Text(T("Подсказка в строке", "Placeholder"), "ph", T("Поиск в интернете или адрес сайта...", "Search the web or enter an address..."));
                break;
            case "clock":
                Chips(T("Формат", "Format"), "fmt", "24", [("24", "24 ч"), ("12", "12 ч")]);
                Switch(T("Секунды", "Seconds"), "sec", false);
                Switch(T("Показывать дату", "Show date"), "date", true);
                Step(T("Размер цифр", "Digit size"), "size", 64, 28, 140, 6, " px");
                break;
            case "greeting":
                Step(T("Размер текста", "Text size"), "size", 26, 14, 56, 2, " px");
                Switch(T("Обращаться по имени", "Use my name"), "name", true);
                break;
            case "headline":
                Text(T("Свой текст", "Custom text"), "text", T("Куда отправимся?", "Where to?"));
                Step(T("Размер текста", "Text size"), "size", 26, 14, 64, 2, " px");
                break;
            case "actions":
            {
                host.Children.Add(Muted(T("Какие кнопки показывать", "Which buttons to show"), 11));
                var current = O(w, "items", "ai,history,settings,account").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                foreach (var a in HomeActionCatalog)
                {
                    var id = a.Id;
                    host.Children.Add(HRow(T(a.Ru, a.En), HToggle(current.Contains(id), on =>
                    {
                        if (on && !current.Contains(id)) current.Add(id);
                        if (!on) current.Remove(id);
                        var ordered = HomeActionCatalog.Select(x => x.Id).Where(current.Contains);
                        SetO(w, "items", string.Join(',', ordered));
                        Changed();
                    })));
                }
                Chips(T("Вид кнопок", "Button style"), "mode", "labels", [("labels", T("С подписями", "With labels")), ("icons", T("Только иконки", "Icons only"))]);
                break;
            }
            case "recent":
                Chips(T("Сколько сайтов", "How many"), "count", "4", [("4", "4"), ("8", "8"), ("12", "12")]);
                break;
            case "notes":
                Step(T("Высота", "Height"), "h", 150, 80, 420, 20, " px");
                break;
            case "pomodoro":
                Step(T("Фокус, минут", "Focus, minutes"), "work", 25, 5, 90, 5);
                Step(T("Перерыв, минут", "Break, minutes"), "break", 5, 1, 30, 1);
                break;
            case "calendar":
                Chips(T("Неделя начинается с", "Week starts on"), "week", "mon", [("mon", T("Понедельника", "Monday")), ("sun", T("Воскресенья", "Sunday"))]);
                break;
            case "spacer":
                Step(T("Высота отступа", "Spacer height"), "h", 32, 4, 240, 4, " px");
                break;
            case "text":
            {
                host.Children.Add(Muted(T("Текст", "Text"), 11));
                var box = HInput(w.Data, T("Ваш текст или цитата", "Your text or quote"), v => { w.Data = v; QueueHomeSave(); }, true);
                box.Margin = new Thickness(0, 4, 0, 6);
                host.Children.Add(box);
                AttachRebuildOnBlur(box);
                Step(T("Размер", "Size"), "size", 16, 10, 72, 2, " px");
                Chips(T("Выравнивание", "Alignment"), "align", "center", [("left", T("Слева", "Left")), ("center", T("По центру", "Center")), ("right", T("Справа", "Right"))]);
                Switch(T("Жирный", "Bold"), "bold", false);
                break;
            }
            case "pinned":
            {
                var add = HBtn(Txt(T("+ Добавить закрепленный сайт", "+ Add pinned site"), 12), () => ShowPinnedSiteEditor(w, host), null, "Zen_5E5E64", "Zen_686870", 8, new Thickness(10, 0, 10, 0), 30);
                add.Margin = new Thickness(0, 6, 0, 0);
                host.Children.Add(add);

                var reset = HBtn(Txt(T("Сбросить к шаблонам (как в Opera)", "Reset to Opera presets"), 12), () => { w.Data = JsonSerializer.Serialize(GetDefaultPinnedSites()); HomeLayoutChanged(); }, null, "Zen_56565A", "Zen_5E5E64", 8, new Thickness(10, 0, 10, 0), 30);
                reset.Margin = new Thickness(0, 6, 0, 0);
                host.Children.Add(reset);
                break;
            }
            case "shortcuts":
            {
                var clear = HBtn(Txt(T("Удалить все ярлыки", "Remove all shortcuts"), 12), () => { w.Data = ""; HomeLayoutChanged(); }, null, "Zen_56565A", "Zen_5E5E64", 8, new Thickness(10, 0, 10, 0), 30);
                clear.Margin = new Thickness(0, 6, 0, 0);
                host.Children.Add(clear);
                break;
            }
            case "todo":
            {
                var clear = HBtn(Txt(T("Убрать выполненные", "Clear completed"), 12), () => { var list = ReadTodos(w); list.RemoveAll(x => x.D); w.Data = JsonSerializer.Serialize(list); HomeLayoutChanged(); }, null, "Zen_56565A", "Zen_5E5E64", 8, new Thickness(10, 0, 10, 0), 30);
                clear.Margin = new Thickness(0, 6, 0, 0);
                host.Children.Add(clear);
                break;
            }
        }
    }

    /// <summary>Text options are saved while typing but only rebuild the page once the box loses focus.</summary>
    private void AttachRebuildOnBlur(Border shell)
    {
        if (shell.Child is Grid g && g.Children.OfType<TextBox>().FirstOrDefault() is { } tb)
            tb.LostKeyboardFocus += (_, _) => { SaveHomeLayout(); RebuildHomeBuilder(); };
    }

    // ---------------------------------------------------------------- studio: look
    private static readonly string[] HomeSwatches = ["#0E0F12", "#14151A", "#1B1B1F", "#1E2430", "#26203A", "#10241F", "#2B1D1D", "#2B2618", "#3D3D42", "#000000"];

    private static readonly (string Name, string A, string B)[] HomeGradients =
    [
        ("Aurora", "#1B2A49", "#3B1F5E"), ("Sunset", "#2B1B2E", "#5E2F2F"), ("Ocean", "#0F2027", "#2C5364"), ("Forest", "#10241F", "#2B4A3A"),
        ("Graphite", "#16171B", "#2E3138"), ("Dusk", "#1E1B2E", "#3A2D5C"), ("Ember", "#241410", "#5A2D1A"), ("Mono", "#0B0B0D", "#26262B")
    ];

    private void BuildStudioLook(StackPanel body)
    {
        void LookChanged() { NormalizeHomeLayout(); SaveHomeLayout(); RebuildHomeBuilder(); }
        var l = _homeLayout;

        body.Children.Add(HSection(T("Фон", "Background")));
        body.Children.Add(HChipRow([("theme", T("Тема", "Theme")), ("solid", T("Цвет", "Color")), ("gradient", T("Градиент", "Gradient")), ("wallpaper", T("Обои", "Wallpaper"))], l.Bg, v => { l.Bg = v; LookChanged(); }));

        if (l.Bg == "solid")
        {
            var sw = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            foreach (var hex in HomeSwatches) sw.Children.Add(HSwatch(hex, string.Equals(l.BgColor, hex, StringComparison.OrdinalIgnoreCase), () => { l.BgColor = hex; LookChanged(); }));
            body.Children.Add(sw);
            body.Children.Add(HInput(l.BgColor, "#RRGGBB", v => { if (TryParseHex(v)) { l.BgColor = v; SaveHomeLayout(); ApplyHomeLook(); } }));
        }
        else if (l.Bg == "gradient")
        {
            var sw = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            foreach (var g in HomeGradients)
            {
                var chip = new Border { Width = 46, Height = 30, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 8, 8), BorderThickness = new Thickness(l.BgColor == g.A && l.BgColor2 == g.B ? 2 : 1) };
                chip.Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(g.A), (Color)ColorConverter.ConvertFromString(g.B), 135);
                chip.SetResourceReference(Border.BorderBrushProperty, l.BgColor == g.A && l.BgColor2 == g.B ? "ZenTextBrush" : "Zen_5E5E64");
                var btn = new Button { Template = CreateSimpleBorderButtonTemplate(), Content = chip, Cursor = Cursors.Hand, Focusable = false, ToolTip = g.Name };
                var gg = g;
                btn.Click += (_, _) => { l.BgColor = gg.A; l.BgColor2 = gg.B; LookChanged(); };
                sw.Children.Add(btn);
            }
            body.Children.Add(sw);
            body.Children.Add(HRow(T("Угол", "Angle"), HStepper(l.GradientAngle, 0, 360, 45, v => $"{(int)v}°", v => { l.GradientAngle = (int)v; SaveHomeLayout(); ApplyHomeLook(); })));
        }
        else if (l.Bg == "wallpaper")
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            var pick = HBtn(Txt(T("Выбрать обои…", "Choose image…"), 12), ChooseHomeWallpaper, null, "Zen_5E5E64", "Zen_686870", 8, new Thickness(12, 0, 12, 0), 30);
            var reset = HBtn(Txt(T("Сбросить", "Reset"), 12), () => { _state.HomeWallpaperPath = null; l.Bg = "theme"; LookChanged(); }, null, "Zen_4B4B50", "Zen_56565A", 8, new Thickness(12, 0, 12, 0), 30);
            reset.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(pick);
            row.Children.Add(reset);
            body.Children.Add(row);
            body.Children.Add(HRow(T("Затемнение", "Dim"), HStepper(Math.Round(_state.HomeWallpaperDim * 100), 0, 90, 5, v => $"{(int)v}%", v => { _state.HomeWallpaperDim = v / 100.0; SaveHomeLayout(); ApplyHomeLook(); })));
            body.Children.Add(HRow(T("Размытие", "Blur"), HStepper(l.WallpaperBlur, 0, 40, 4, v => $"{(int)v}", v => { l.WallpaperBlur = (int)v; SaveHomeLayout(); ApplyHomeLook(); })));
        }

        body.Children.Add(HSection(T("Карточки", "Cards")));
        body.Children.Add(HChipRow([("filled", T("Заливка", "Filled")), ("outline", T("Контур", "Outline")), ("glass", T("Стекло", "Glass")), ("flat", T("Плоские", "Flat"))], l.CardStyle, v => { l.CardStyle = v; LookChanged(); }));
        body.Children.Add(HRow(T("Скругление", "Corner radius"), HStepper(l.Radius, 0, 30, 2, v => $"{(int)v}", v => { l.Radius = (int)v; LookChanged(); })));

        body.Children.Add(HSection(T("Строка поиска", "Search bar")));
        body.Children.Add(HChipRow([("pill", T("Капсула", "Pill")), ("rounded", T("Мягкая", "Soft")), ("square", T("Квадрат", "Square"))], _state.HomeSearchShape ?? "pill", v => { _state.HomeSearchShape = v; SaveHomeLayout(); ApplyHomeLook(); RenderHomeStudio(); }));
        body.Children.Add(HChipRow([("solid", T("Классика", "Classic")), ("glass", T("Стекло", "Glass")), ("outline", T("Контур", "Outline")), ("glow", T("Неон", "Glow"))], _state.HomeSearchStyle ?? "solid", v => { _state.HomeSearchStyle = v; SaveHomeLayout(); ApplyHomeLook(); RenderHomeStudio(); }));

        body.Children.Add(HSection(T("Шрифт", "Font")));
        (string Family, string Name)[] fonts =
        [
            ("Segoe UI Variable Display, Segoe UI", "Segoe UI"), ("Inter, Segoe UI", "Inter"), ("Cascadia Code, Consolas", "Cascadia"),
            ("Georgia", "Georgia"), ("Bahnschrift", "Bahnschrift"), ("Trebuchet MS", "Trebuchet")
        ];
        var fontRow = new WrapPanel();
        foreach (var f in fonts)
        {
            var selected = string.Equals(_state.HomeFontFamily, f.Family, StringComparison.OrdinalIgnoreCase);
            var t = Txt(f.Name, 12.5, selected);
            t.FontFamily = new FontFamily(f.Family);
            t.SetResourceReference(TextBlock.ForegroundProperty, selected ? "ZenTextBrush" : "ZenMutedBrush");
            var chip = HBtn(t, () => { _state.HomeFontFamily = f.Family; SaveHomeLayout(); ApplyHomeLook(); RenderHomeStudio(); }, null, selected ? "Zen_5E5E64" : "Zen_4B4B50", selected ? "Zen_5E5E64" : "Zen_56565A", 8, new Thickness(10, 0, 10, 0), 30);
            chip.Margin = new Thickness(0, 0, 6, 6);
            fontRow.Children.Add(chip);
        }
        body.Children.Add(fontRow);

        body.Children.Add(HSection(T("Раскладка", "Layout")));
        body.Children.Add(HRow(T("Ширина страницы", "Page width"), HStepper(l.Width, 440, 1000, 40, v => $"{(int)v}", v => { l.Width = (int)v; LookChanged(); })));
        body.Children.Add(HRow(T("Промежутки", "Spacing"), HStepper(l.Gap, 2, 28, 2, v => $"{(int)v}", v => { l.Gap = (int)v; LookChanged(); })));
        body.Children.Add(Muted(T("Положение по вертикали", "Vertical position"), 11));
        var va = HChipRow([("center", T("По центру", "Center")), ("top", T("Сверху", "Top"))], l.VAlign, v => { l.VAlign = v; LookChanged(); });
        va.Margin = new Thickness(0, 4, 0, 0);
        body.Children.Add(va);
    }

    private static bool TryParseHex(string v)
    {
        try { ColorConverter.ConvertFromString(v); return v.StartsWith('#') && (v.Length == 7 || v.Length == 9); }
        catch { return false; }
    }

    private void ChooseHomeWallpaper()
    {
        var dlg = new OpenFileDialog
        {
            Title = T("Выберите фоновое изображение", "Choose a background image"),
            Filter = "Images (*.jpg;*.jpeg;*.png;*.webp;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            _state.HomeWallpaperPath = dlg.FileName;
            _homeLayout.Bg = "wallpaper";
            SaveHomeLayout();
            RebuildHomeBuilder();
        }
    }

    // ---------------------------------------------------------------- studio: presets
    private void BuildStudioPresets(StackPanel body)
    {
        (string Id, string Ru, string En, string RuDesc, string EnDesc, string Icon)[] presets =
        [
            ("minimal", "Минимализм", "Minimal", "Только строка поиска", "Just the search bar", "Search"),
            ("classic", "Классика", "Classic", "Логотип, часы, поиск, действия, недавние", "Logo, clock, search, actions, recent", "Home"),
            ("work", "Работа", "Work", "Заметки, задачи, таймер, календарь", "Notes, tasks, timer, calendar", "Check"),
            ("focus", "Фокус", "Focus", "Часы, поиск и таймер", "Clock, search and timer", "Timer"),
            ("dash", "Дашборд", "Dashboard", "Essentials, статистика, календарь", "Essentials, stats, calendar", "Chart"),
        ];
        body.Children.Add(Muted(T("Шаблон заменит набор виджетов. Оформление (фон, шрифт, карточки) останется вашим.", "A preset replaces the widgets. Your look (background, font, cards) stays."), 11.5));
        foreach (var p in presets)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = HIcon(p.Icon, 17);
            icon.HorizontalAlignment = HAlign.Left;
            row.Children.Add(icon);
            var texts = new StackPanel();
            texts.Children.Add(Txt(T(p.Ru, p.En), 13, true));
            var d = Muted(T(p.RuDesc, p.EnDesc), 11);
            d.TextWrapping = TextWrapping.NoWrap;
            d.TextTrimming = TextTrimming.CharacterEllipsis;
            texts.Children.Add(d);
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);
            var id = p.Id;
            var btn = HBtn(row, () => ApplyHomePreset(id), null, "Zen_4B4B50", "Zen_56565A", 10, new Thickness(12, 10, 12, 10));
            btn.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(btn);
        }

        body.Children.Add(HSection(T("Перенос", "Share")));
        var io = new StackPanel { Orientation = Orientation.Horizontal };
        var export = HBtn(Txt(T("Копировать раскладку", "Copy layout"), 12), () =>
        {
            try { System.Windows.Clipboard.SetText(JsonSerializer.Serialize(_homeLayout)); ShowToast(T("Раскладка скопирована", "Layout copied"), T("Вставьте её на другом устройстве через «Вставить»", "Paste it elsewhere with “Paste”")); }
            catch (Exception ex) { App.Log(ex); }
        }, null, "Zen_4B4B50", "Zen_56565A", 8, new Thickness(12, 0, 12, 0), 30);
        var import = HBtn(Txt(T("Вставить", "Paste"), 12), () =>
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<HomeLayoutState>(System.Windows.Clipboard.GetText());
                if (parsed is null) throw new InvalidOperationException();
                _homeLayout = parsed;
                HomeLayoutChanged();
                ShowToast(T("Раскладка применена", "Layout applied"));
            }
            catch { ShowToast(T("В буфере нет раскладки", "No layout in the clipboard"), "", true); }
        }, null, "Zen_4B4B50", "Zen_56565A", 8, new Thickness(12, 0, 12, 0), 30);
        import.Margin = new Thickness(6, 0, 0, 0);
        io.Children.Add(export);
        io.Children.Add(import);
        body.Children.Add(io);
    }

    private void ApplyHomePreset(string id)
    {
        var preset = HomePreset(id);
        _homeLayout.Widgets = preset.Widgets;
        _homeLayout.VAlign = preset.VAlign;
        _homeLayout.Width = preset.Width;
        _studioExpanded = null;
        HomeLayoutChanged();
    }

    // ---------------------------------------------------------------- look
    private static Point GradientPoint(double angle, bool end)
    {
        var rad = angle * Math.PI / 180.0;
        var dx = Math.Cos(rad) / 2; var dy = Math.Sin(rad) / 2;
        return end ? new Point(0.5 + dx, 0.5 + dy) : new Point(0.5 - dx, 0.5 - dy);
    }

    private void ApplyHomeLook()
    {
        if (!_homeBuilderReady) return;
        var l = _homeLayout;

        // font
        var family = string.IsNullOrWhiteSpace(_state.HomeFontFamily) ? "Segoe UI Variable Display, Segoe UI" : _state.HomeFontFamily;
        var font = new FontFamily(family);
        HomeClockText.FontFamily = font;
        HomeDateText.FontFamily = font;
        HomeHeadlineText.FontFamily = font;
        HomeSearchBox.FontFamily = font;
        HomeSearchPlaceholder.FontFamily = font;
        if (_homeWrap is not null) _homeWrap.SetValue(TextElement.FontFamilyProperty, font);

        // search bar
        var h = double.IsNaN(HomeSearchShell.Height) ? 50 : HomeSearchShell.Height;
        HomeSearchShell.CornerRadius = (_state.HomeSearchShape ?? "pill") switch
        {
            "square" => new CornerRadius(4),
            "rounded" => new CornerRadius(14),
            _ => new CornerRadius(h / 2)
        };
        switch (_state.HomeSearchStyle ?? "solid")
        {
            case "glass":
                HomeSearchShell.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
                HomeSearchShell.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
                HomeSearchShell.BorderThickness = new Thickness(1.2);
                HomeSearchShadow.Color = Colors.Black; HomeSearchShadow.BlurRadius = 30; HomeSearchShadow.Opacity = 0.4;
                break;
            case "outline":
                HomeSearchShell.Background = Brushes.Transparent;
                HomeSearchShell.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                HomeSearchShell.BorderThickness = new Thickness(1.6);
                HomeSearchShadow.Color = Colors.Transparent; HomeSearchShadow.Opacity = 0;
                break;
            case "glow":
                HomeSearchShell.SetResourceReference(Border.BackgroundProperty, "ZenCardBrush");
                HomeSearchShell.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                HomeSearchShell.BorderThickness = new Thickness(1.4);
                HomeSearchShadow.Color = (TryFindResource("AccentBrush") as SolidColorBrush)?.Color ?? Colors.MediumPurple;
                HomeSearchShadow.BlurRadius = 26; HomeSearchShadow.Opacity = 0.5;
                break;
            default:
                HomeSearchShell.SetResourceReference(Border.BackgroundProperty, "ZenCardBrush");
                HomeSearchShell.SetResourceReference(Border.BorderBrushProperty, "ZenCardBorderBrush");
                HomeSearchShell.BorderThickness = new Thickness(1);
                if (HomeSearchShadow is not null) { HomeSearchShadow.Color = Colors.Transparent; HomeSearchShadow.Opacity = 0; }
                break;
        }

        // background
        switch (l.Bg)
        {
            case "solid":
                EmptyBackground.Background = RawBrush(l.BgColor);
                break;
            case "gradient":
                EmptyBackground.Background = new LinearGradientBrush
                {
                    StartPoint = GradientPoint(l.GradientAngle, false),
                    EndPoint = GradientPoint(l.GradientAngle, true),
                    GradientStops = { new GradientStop((Color)ColorConverter.ConvertFromString(l.BgColor), 0), new GradientStop((Color)ColorConverter.ConvertFromString(l.BgColor2), 1) }
                };
                break;
            default:
                EmptyBackground.SetResourceReference(Panel.BackgroundProperty, "ShellBrush");
                break;
        }
        foreach (var orb in EmptyBackground.Children.OfType<Ellipse>())
            orb.Visibility = l.Bg == "theme" ? Visibility.Visible : Visibility.Collapsed;

        var wallpaper = l.Bg == "wallpaper" && !string.IsNullOrWhiteSpace(_state.HomeWallpaperPath) && System.IO.File.Exists(_state.HomeWallpaperPath);
        if (wallpaper)
        {
            try
            {
                if (HomeCustomWallpaper.Source is not BitmapImage current || current.UriSource?.LocalPath != _state.HomeWallpaperPath)
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(_state.HomeWallpaperPath!);
                    bmp.EndInit();
                    bmp.Freeze();
                    HomeCustomWallpaper.Source = bmp;
                }
                HomeCustomWallpaper.Visibility = Visibility.Visible;
                HomeCustomWallpaper.Effect = l.WallpaperBlur > 0 ? new BlurEffect { Radius = l.WallpaperBlur, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance } : null;
                HomeWallpaperDimOverlay.Opacity = Math.Clamp(_state.HomeWallpaperDim, 0, 0.9);
                HomeWallpaperDimOverlay.Visibility = Visibility.Visible;
            }
            catch
            {
                wallpaper = false;
            }
        }
        if (!wallpaper)
        {
            HomeCustomWallpaper.Source = null;
            HomeCustomWallpaper.Visibility = Visibility.Collapsed;
            HomeWallpaperDimOverlay.Visibility = Visibility.Collapsed;
        }
    }

    // ---------------------------------------------------------------- clock format used by UpdateHomeClock
    private string FormatHomeClock(DateTime now)
    {
        var w = _homeLayout.Widgets.FirstOrDefault(x => x.Type == "clock");
        if (w is null) return now.ToString("HH:mm");
        var twelve = O(w, "fmt", "24") == "12";
        var seconds = O(w, "sec", "0") == "1";
        var format = (twelve ? "h:mm" : "HH:mm") + (seconds ? ":ss" : "") + (twelve ? " tt" : "");
        return now.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    }
}
