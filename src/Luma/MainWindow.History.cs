using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Luma;

public partial class MainWindow
{
    private void RefreshHistoryView()
    {
        var query = HistorySearchBox.Text.Trim();
        var items = _state.History
            .Where(h => query.Length == 0 || h.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || h.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(h => h.VisitedAt).Take(300).ToList();
        HistoryList.ItemsSource = items;
        HistoryCountText.Text = query.Length == 0 ? $"Записей: {_state.History.Count}" : $"Найдено: {items.Count}";
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientUi();
        HistoryList.ItemsSource = null;
        HistoryCountText.Text = "";
        HistoryOverlay.Width = Math.Max(1, SiteShell.ActualWidth);
        HistoryOverlay.Height = Math.Max(1, SiteShell.ActualHeight);
        HistoryPopup.IsOpen = true;
        HistoryOverlay.Visibility = Visibility.Visible;
        HistoryOverlay.BeginAnimation(OpacityProperty, null);
        HistoryOverlay.Opacity = 1;
        HistoryCardScale.ScaleX = HistoryCardScale.ScaleY = .985;
        var ms = _state.AnimationsEnabled ? 150 : 0;
        var scale = new DoubleAnimation(.985, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        HistoryCardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scale);
        HistoryCardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scale);
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshHistoryView));
        HistoryOverlay.Focus();
        Keyboard.Focus(HistorySearchBox);
    }

    private void CloseHistory()
    {
        if (HistoryOverlay.Visibility != Visibility.Visible) return;
        var ms = _state.AnimationsEnabled ? 110 : 0;
        var scale = new DoubleAnimation(HistoryCardScale.ScaleX, .99, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        scale.Completed += (_, _) =>
        {
            HistoryCardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            HistoryCardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
            HistoryOverlay.Visibility = Visibility.Collapsed;
            HistoryPopup.IsOpen = false;
            HistorySearchBox.Clear();
        };
        HistoryCardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scale, HandoffBehavior.SnapshotAndReplace);
        HistoryCardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scale, HandoffBehavior.SnapshotAndReplace);
    }

    private void HistoryClose_Click(object sender, RoutedEventArgs e) { CloseHistory(); e.Handled = true; }
    private void HistoryBackdrop_Click(object sender, MouseButtonEventArgs e) { if (ReferenceEquals(e.OriginalSource, HistoryOverlay)) CloseHistory(); }
    private void HistoryCard_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;
    private void HistorySearch_TextChanged(object sender, TextChangedEventArgs e) { if (HistoryOverlay.Visibility == Visibility.Visible) RefreshHistoryView(); }
    private async void HistoryRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(HistoryList, source) is not ListBoxItem { DataContext: HistoryEntry entry }) return;
        e.Handled = true;
        CloseHistory();
        try { await AddTabAsync(entry.Url); }
        catch (Exception ex) { App.Log(ex); }
    }
    private void HistoryClear_Click(object sender, RoutedEventArgs e)
    {
        if (_state.History.Count == 0) return;
        Confirm("Очистить всю историю?", "Это удалит локальный журнал посещений. Spaces, папки и вкладки не изменятся.", () => { _state.History.Clear(); _stateStore.Save(); RefreshHistoryView(); });
        e.Handled = true;
    }
}
