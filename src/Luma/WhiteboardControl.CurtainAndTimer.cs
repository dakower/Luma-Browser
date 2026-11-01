using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Point = System.Windows.Point;

namespace Luma;

public partial class WhiteboardControl
{
    // =========================================================================
    // ШКІЛЬНА ШТОРКА (CURTAIN) ДЛЯ ПРИХОВУВАННЯ ВІДПОВІДЕЙ ТА ФОРМУЛ
    // =========================================================================

    /// <summary>
    /// Додавання інтерактивної шкільної шторки на дошку
    /// </summary>
    public void InsertCurtainWidget()
    {
        DeselectAllItems();

        double curtainWidth = 550.0;
        double curtainHeight = 280.0;

        double posX = Math.Max(60.0, (MainCanvas.ActualWidth - curtainWidth) / 2.0);
        double posY = Math.Max(80.0, (MainCanvas.ActualHeight - curtainHeight) / 3.0);

        var curtainContainer = new Border
        {
            Width = curtainWidth,
            Height = curtainHeight,
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), // Шляхетний графітовий колір класної дошки
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            BorderThickness = new Thickness(1.5),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 5,
                Opacity = 0.35,
                Color = Colors.Black
            }
        };

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) }); // Шапка
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Тіло шторки
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) }); // Нижня ручка протягування

        // 1. Верхня шапка шторки (для переміщення по дошці)
        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            CornerRadius = new CornerRadius(7, 7, 0, 0),
            Cursor = Cursors.SizeAll
        };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerTitle = new TextBlock
        {
            Text = "👁️ Шкільна шторка (приховування відповідей)",
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(headerTitle, 0);
        headerGrid.Children.Add(headerTitle);

        var closeBtn = new Button
        {
            Content = "✕",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 12.0,
            Padding = new Thickness(8, 0, 10, 0),
            Cursor = Cursors.Hand
        };
        closeBtn.Click += (_, _) => MainCanvas.Children.Remove(curtainContainer);
        Grid.SetColumn(closeBtn, 1);
        headerGrid.Children.Add(closeBtn);

        headerBorder.Child = headerGrid;
        Grid.SetRow(headerBorder, 0);
        rootGrid.Children.Add(headerBorder);

        // 2. Центральне поле шторки з підказкою для вчителя
        var bodyBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(245, 15, 23, 42)),
            Padding = new Thickness(12)
        };
        var bodyText = new TextBlock
        {
            Text = "Тут приховано розв'язок задачі або відповідь.\nПотягніть униз за ручку, щоб відкрити учням.",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 12.0,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        bodyBorder.Child = bodyText;
        Grid.SetRow(bodyBorder, 1);
        rootGrid.Children.Add(bodyBorder);

        // 3. Нижня ручка для розкриття шторки
        var bottomGrip = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            CornerRadius = new CornerRadius(0, 0, 7, 7),
            Cursor = Cursors.SizeNS
        };
        var bottomText = new TextBlock
        {
            Text = "▲ потягніть для відкриття ▼",
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        bottomGrip.Child = bottomText;
        Grid.SetRow(bottomGrip, 2);
        rootGrid.Children.Add(bottomGrip);

        curtainContainer.Child = rootGrid;

        // Логіка переміщення шторки за шапку
        bool isDragging = false;
        Point dragStartPoint = new();

        headerBorder.MouseLeftButtonDown += (s, e) =>
        {
            isDragging = true;
            dragStartPoint = e.GetPosition(MainCanvas);
            headerBorder.CaptureMouse();
            e.Handled = true;
        };

        headerBorder.MouseMove += (s, e) =>
        {
            if (isDragging)
            {
                var currentPoint = e.GetPosition(MainCanvas);
                double deltaX = currentPoint.X - dragStartPoint.X;
                double deltaY = currentPoint.Y - dragStartPoint.Y;

                double curLeft = InkCanvas.GetLeft(curtainContainer);
                double curTop = InkCanvas.GetTop(curtainContainer);

                InkCanvas.SetLeft(curtainContainer, Math.Max(0, curLeft + deltaX));
                InkCanvas.SetTop(curtainContainer, Math.Max(0, curTop + deltaY));

                dragStartPoint = currentPoint;
                e.Handled = true;
            }
        };

        headerBorder.MouseLeftButtonUp += (s, e) =>
        {
            isDragging = false;
            headerBorder.ReleaseMouseCapture();
            e.Handled = true;
        };

        // Логіка розтягування/відкриття шторки за нижній край
        bool isResizing = false;
        double resizeStartHeight = 0;
        Point resizeStartPoint = new();

        bottomGrip.MouseLeftButtonDown += (s, e) =>
        {
            isResizing = true;
            resizeStartHeight = curtainContainer.ActualHeight > 0 ? curtainContainer.ActualHeight : curtainContainer.Height;
            resizeStartPoint = e.GetPosition(MainCanvas);
            bottomGrip.CaptureMouse();
            e.Handled = true;
        };

        bottomGrip.MouseMove += (s, e) =>
        {
            if (isResizing)
            {
                var cur = e.GetPosition(MainCanvas);
                double diff = cur.Y - resizeStartPoint.Y;
                double newHeight = Math.Max(70.0, resizeStartHeight + diff);
                curtainContainer.Height = newHeight;
                e.Handled = true;
            }
        };

        bottomGrip.MouseLeftButtonUp += (s, e) =>
        {
            isResizing = false;
            bottomGrip.ReleaseMouseCapture();
            e.Handled = true;
        };

        InkCanvas.SetLeft(curtainContainer, posX);
        InkCanvas.SetTop(curtainContainer, posY);
        MainCanvas.Children.Add(curtainContainer);

        ShowAiStatus("Шкільну шторку додано! Накрийте нею відповідь та тягніть за ручку 👁️");
    }

    private void CurtainToolBtn_Click(object sender, RoutedEventArgs e) => InsertCurtainWidget();
    private void LessonTimerToolBtn_Click(object sender, RoutedEventArgs e) => TimerTopBtn_Click(sender, e);
}
