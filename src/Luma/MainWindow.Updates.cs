using Luma.Updates;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;
using WpfCursors = System.Windows.Input.Cursors;
using WpfDoubleAnimation = System.Windows.Media.Animation.DoubleAnimation;

namespace Luma;

public partial class MainWindow
{
    private readonly IUpdateService _updates;
    private readonly CancellationTokenSource _updateLoopCancellation = new();
    private UpdateCheckResult? _readyUpdate;
    private bool _updatePromptBusy;
    private string _announcedUpdateVersion = "";
    private readonly DispatcherTimer _updateNotificationTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    private async Task StartUpdateLoopAsync()
    {
        if (!_updates.IsConfigured || ((App)WpfApplication.Current).IsPrivateSession) return;
        _auth.SessionChanged += AuthChangedForUpdates;
        _updates.UpdateAvailable += UpdateFound;
        try
        {
            while (!_updateLoopCancellation.IsCancellationRequested)
            {
                await CheckUpdatesCore(false, _updateLoopCancellation.Token);
                await Task.Delay(TimeSpan.FromMinutes(5), _updateLoopCancellation.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void UpdateFound(SignedUpdateManifest manifest) => Dispatcher.Invoke(() =>
    {
        if (_announcedUpdateVersion == manifest.Version) return;
        _announcedUpdateVersion = manifest.Version;
        ShowUpdateNotificationBanner(manifest.Version, manifest.Title, manifest.Notes);
    });

    private void AuthChangedForUpdates(object? s, EventArgs e) => Dispatcher.InvokeAsync(async () => await CheckUpdatesCore(false, _updateLoopCancellation.Token));

    private async Task CheckUpdatesCore(bool manual, CancellationToken ct = default)
    {
        if (_updatePromptBusy) return;
        _updatePromptBusy = true;
        try
        {
            var token = await _auth.GetAccessTokenAsync(ct);
            var r = await _updates.CheckAsync(Version.Parse(AppVersion), token, ct);
            if (r.Status == UpdateCheckStatus.Downloaded && r.Manifest is not null && r.PackagePath is not null)
            {
                _readyUpdate = r;
                ShowUpdateNotificationBanner(r.Manifest.Version, r.Manifest.Title, r.Manifest.Notes);
                return;
            }
            if (manual)
            {
                ShowToast("Обновления Luma", r.Message, r.Status == UpdateCheckStatus.Failed);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { _updatePromptBusy = false; }
    }

    private async Task CheckForUpdatesManuallyAsync() => await CheckUpdatesCore(true);

    public void PositionUpdateNotification()
    {
        if (UpdateNotificationPopup is null || SiteShell is null) return;
        UpdateNotificationPopup.HorizontalOffset = Math.Max(ToastMargin, SiteShell.ActualWidth - 360 - ToastMargin);
        UpdateNotificationPopup.VerticalOffset = ToastMargin;
    }

    public void ShowUpdateNotificationBanner(string version, string title, string? notes = null)
    {
        if (UpdateNotificationPopup is null) return;

        UpdateNotificationTitle.Text = "Доступно автообновление";
        var verText = string.IsNullOrWhiteSpace(version) ? "NEW" : (version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : "v" + version);
        UpdateNotificationTag.Text = verText;
        UpdateNotificationSubtitle.Text = string.IsNullOrWhiteSpace(title)
            ? "Luma готова к установке обновления в один клик."
            : title;

        UpdateVersionText.Text = verText;
        UpdateTitleText.Text = string.IsNullOrWhiteSpace(title) ? "Доступно новое обновление" : title;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            UpdateNotesText.Text = notes;
        }

        PositionUpdateNotification();
        UpdateNotificationPopup.IsOpen = true;

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PositionUpdateNotification));

        _updateNotificationTimer.Stop();
        _updateNotificationTimer.Tick -= UpdateNotificationTimer_Tick;
        _updateNotificationTimer.Tick += UpdateNotificationTimer_Tick;
        _updateNotificationTimer.Start();
    }

    private void UpdateNotificationTimer_Tick(object? sender, EventArgs e)
    {
        _updateNotificationTimer.Stop();
    }

    private void UpdateNotificationDismiss_Click(object sender, RoutedEventArgs e)
    {
        _updateNotificationTimer.Stop();
        if (UpdateNotificationPopup is not null)
        {
            UpdateNotificationPopup.IsOpen = false;
        }
    }

    private void UpdateNotificationAction_Click(object sender, RoutedEventArgs e)
    {
        UpdateNotificationDismiss_Click(sender, e);
        if (_readyUpdate?.PackagePath is not null && _readyUpdate.Manifest is not null)
        {
            InstallReadyUpdate();
        }
        else
        {
            UpdateOverlay.Visibility = Visibility.Visible;
        }
    }

    private void ShowUpdatePrompt(SignedUpdateManifest m)
    {
        ShowUpdateNotificationBanner(m.Version, m.Title, m.Notes);
    }

    private void UpdateLater_Click(object s, RoutedEventArgs e) => UpdateOverlay.Visibility = Visibility.Collapsed;

    private void UpdateNow_Click(object s, RoutedEventArgs e)
    {
        UpdateOverlay.Visibility = Visibility.Collapsed;
        if (_readyUpdate?.PackagePath is not null && _readyUpdate.Manifest is not null)
        {
            InstallReadyUpdate();
        }
        else
        {
            ShowToast("Автообновление Luma", "Тестовый пакет успешно проверен и готов к установке");
        }
    }

    private void InstallReadyUpdate()
    {
        if (_readyUpdate?.PackagePath is null || _readyUpdate.Manifest is null) return;
        Cursor = WpfCursors.Wait;
        try
        {
            Save();
            UpdateInstaller.Start(_readyUpdate.PackagePath, _readyUpdate.Manifest);
            ((App)WpfApplication.Current).ExitCompletely();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            Cursor = WpfCursors.Arrow;
            ShowToast("Не удалось начать обновление", ex.Message, true);
        }
    }

    private void StopUpdateLoop()
    {
        _auth.SessionChanged -= AuthChangedForUpdates;
        _updates.UpdateAvailable -= UpdateFound;
        if (!_updateLoopCancellation.IsCancellationRequested) _updateLoopCancellation.Cancel();
    }
}
