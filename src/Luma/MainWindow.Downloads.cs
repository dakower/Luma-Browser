using Microsoft.Web.WebView2.Core;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;

namespace Luma;

public partial class MainWindow
{
    public sealed class DownloadItemView : INotifyPropertyChanged
    {
        private readonly DownloadEntry _entry;
        public DownloadItemView(DownloadEntry entry) => _entry = entry;
        public DownloadEntry Entry => _entry;
        public string Id => _entry.Id;
        public string FilePath => _entry.FilePath;
        public string FileName => Path.GetFileName(_entry.FilePath);
        public string DirectoryName => Path.GetDirectoryName(_entry.FilePath) ?? "";
        public string FileTypeLabel
        {
            get
            {
                var extension = Path.GetExtension(_entry.FilePath).TrimStart('.').ToUpperInvariant();
                return string.IsNullOrWhiteSpace(extension) ? "FILE" : extension[..Math.Min(extension.Length, 5)];
            }
        }
        public Geometry FileIconData
        {
            get
            {
                var extension = Path.GetExtension(_entry.FilePath).ToLowerInvariant();
                var data = extension switch
                {
                    ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" => "M5 3H19A2 2 0 0 1 21 5V19A2 2 0 0 1 19 21H5A2 2 0 0 1 3 19V5A2 2 0 0 1 5 3ZM8.5 10A1.5 1.5 0 1 0 8.5 7A1.5 1.5 0 1 0 8.5 10ZM21 15L16 10L5 21",
                    ".mp3" or ".wav" or ".flac" or ".m4a" or ".aac" or ".ogg" => "M9 18V5L21 3V16M9 18A3 3 0 1 1 3 18A3 3 0 1 1 9 18M21 16A3 3 0 1 1 15 16A3 3 0 1 1 21 16M9 9L21 7",
                    ".mp4" or ".mkv" or ".webm" or ".mov" or ".avi" => "M15 10L20 7V17L15 14V10ZM5 5H13A2 2 0 0 1 15 7V17A2 2 0 0 1 13 19H5A2 2 0 0 1 3 17V7A2 2 0 0 1 5 5Z",
                    ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "M4 7H20V20A2 2 0 0 1 18 22H6A2 2 0 0 1 4 20V7ZM3 2H21V7H3V2ZM10 11H14",
                    _ => "M14 2H6A2 2 0 0 0 4 4V20A2 2 0 0 0 6 22H18A2 2 0 0 0 20 20V8ZM14 2V8H20M16 13H8M16 17H8M10 9H8"
                };
                return Geometry.Parse(data);
            }
        }
        public string Status => _entry.Status;
        public bool IsActive => _entry.Status == "downloading";
        public double Progress => _entry.TotalBytes > 0 ? Math.Clamp((double)_entry.BytesReceived / _entry.TotalBytes, 0, 1) : 0;
        public string ProgressText => _entry.Status switch
        {
            "completed" => Size(_entry.BytesReceived > 0 ? _entry.BytesReceived : _entry.TotalBytes),
            "cancelled" => "Отменено",
            "interrupted" => "Загрузка прервана",
            _ when _entry.TotalBytes > 0 => $"{Size(_entry.BytesReceived)} из {Size(_entry.TotalBytes)}",
            _ => $"Загружено {Size(_entry.BytesReceived)}"
        };
        public string StatusText => _entry.Status switch
        {
            "completed" => "Готово",
            "cancelled" => "Отменено",
            "interrupted" => "Ошибка",
            _ => "Загрузка"
        };
        public string StatusColor => _entry.Status switch
        {
            "completed" => "#79D9A5",
            "cancelled" or "interrupted" => "#F39AA7",
            _ => "#B5AEDF"
        };
        public string? PreviewPath
        {
            get
            {
                if (_entry.Status != "completed" || !File.Exists(_entry.FilePath)) return null;
                var extension = Path.GetExtension(_entry.FilePath).ToLowerInvariant();
                return extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" ? _entry.FilePath : null;
            }
        }
        private ImageSource? _preview; private string? _previewPath;
        /// <summary>Thumbnail loaded into memory (OnLoad) so the downloaded image file is never kept locked by the UI.</summary>
        public ImageSource? PreviewImage
        {
            get
            {
                var path = PreviewPath;
                if (path is null) { _preview = null; _previewPath = null; return null; }
                if (_preview is not null && string.Equals(_previewPath, path, StringComparison.OrdinalIgnoreCase)) return _preview;
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bitmap.DecodePixelWidth = 128;
                    bitmap.UriSource = new Uri(path);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    _preview = bitmap; _previewPath = path;
                }
                catch { _preview = null; _previewPath = null; }
                return _preview;
            }
        }
        private ImageSource? _icon; private string? _iconStatus;
        /// <summary>The real Windows icon of the file (own icon for .exe, associated app icon for documents).</summary>
        public ImageSource? FileIcon
        {
            get
            {
                if (PreviewImage is not null) return null;
                if (_iconStatus != _entry.Status) { _iconStatus = _entry.Status; _icon = FileIcons.Get(_entry.FilePath); }
                return _icon;
            }
        }
        public Visibility VectorIconVisibility => FileIcon is null && PreviewImage is null ? Visibility.Visible : Visibility.Collapsed;
        public string StartedText => _entry.StartedAt.ToLocalTime().ToString("dd.MM HH:mm");
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Refresh()
        {
            foreach (var name in new[] { nameof(Status), nameof(IsActive), nameof(Progress), nameof(ProgressText), nameof(StatusText), nameof(StatusColor), nameof(PreviewPath), nameof(PreviewImage), nameof(FileIcon), nameof(VectorIconVisibility) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        private static string Size(long value)
        {
            if (value <= 0) return "0 Б";
            string[] units = ["Б", "КБ", "МБ", "ГБ"];
            var size = (double)value; var unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.#} {units[unit]}";
        }
    }

    private readonly ObservableCollection<DownloadItemView> _downloads = [];
    private readonly Dictionary<string, CoreWebView2DownloadOperation> _downloadOperations = new(StringComparer.Ordinal);
    public ObservableCollection<DownloadItemView> Downloads => _downloads;
    private DispatcherTimer? _downloadWatchdog;

    private void InitializeDownloads()
    {
        // Safety net: if a completion event is ever missed, the row must not spin forever.
        _downloadWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _downloadWatchdog.Tick += (_, _) =>
        {
            var privateSession = ((App)WpfApplication.Current).IsPrivateSession;
            foreach (var item in _downloads.Where(value => value.IsActive).ToList())
                if (_downloadOperations.TryGetValue(item.Id, out var operation)) SyncDownload(item.Entry, item, operation, privateSession);
        };
        _downloadWatchdog.Start();
        if (((App)WpfApplication.Current).IsPrivateSession) { DownloadsList.ItemsSource = _downloads; UpdateDownloadChrome(); return; }
        var stateChanged = false;
        foreach (var entry in _state.Downloads.OrderByDescending(item => item.StartedAt).Take(100))
        {
            if (entry.Status == "downloading")
            {
                entry.Status = "interrupted";
                stateChanged = true;
            }
            _downloads.Add(new DownloadItemView(entry));
        }
        if (stateChanged) _stateStore.Save();
        DownloadsList.ItemsSource = _downloads;
        UpdateDownloadChrome();
    }

    private void HandleDownloadStarting(CoreWebView2DownloadStartingEventArgs args)
    {
        try
        {
            var folder = string.IsNullOrWhiteSpace(_state.DownloadPath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                : _state.DownloadPath;
            Directory.CreateDirectory(folder);
            var suggested = Path.GetFileName(args.ResultFilePath);
            if (string.IsNullOrWhiteSpace(suggested))
            {
                suggested = Uri.TryCreate(args.DownloadOperation.Uri, UriKind.Absolute, out var uri)
                    ? Path.GetFileName(uri.LocalPath) : "download";
                if (string.IsNullOrWhiteSpace(suggested)) suggested = "download";
            }
            var target = UniqueDownloadPath(folder, suggested);
            args.ResultFilePath = target;
            args.Handled = true;

            var operation = args.DownloadOperation;
            var entry = new DownloadEntry
            {
                FilePath = target,
                SourceUrl = operation.Uri ?? "",
                MimeType = operation.MimeType ?? "",
                TotalBytes = DownloadTotalBytes(operation.TotalBytesToReceive),
                BytesReceived = operation.BytesReceived,
                Status = "downloading",
                StartedAt = _clock.UtcNow
            };
            var isPrivate = ((App)WpfApplication.Current).IsPrivateSession;
            if (!isPrivate)
            {
                _state.Downloads.Insert(0, entry);
                if (_state.Downloads.Count > 300) _state.Downloads.RemoveRange(300, _state.Downloads.Count - 300);
            }
            var item = new DownloadItemView(entry);
            _downloads.Insert(0, item);
            _downloadOperations[entry.Id] = operation;
            if (!isPrivate) _stateStore.Save();
            UpdateDownloadChrome();
            if (!isPrivate) PushDownloadsToSettings();
            CloseMenusExcept(DownloadPopup);
            DownloadPopup.IsOpen = true;

            operation.BytesReceivedChanged += (_, _) => Dispatcher.BeginInvoke(() => SyncDownload(entry, item, operation, isPrivate));
            operation.StateChanged += (_, _) => Dispatcher.BeginInvoke(() => SyncDownload(entry, item, operation, isPrivate));
            // Very small files can finish before the handlers above are attached.
            Dispatcher.BeginInvoke(() => SyncDownload(entry, item, operation, isPrivate));
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("Не удалось начать загрузку", ex.Message, true);
        }
    }

    /// <summary>Idempotent: brings entry, UI and saved state in line with the real download state. Each step is isolated so
    /// one failing step (toast, save, settings push) can never leave the row spinning after the file is complete.</summary>
    private void SyncDownload(DownloadEntry entry, DownloadItemView item, CoreWebView2DownloadOperation operation, bool isPrivate)
    {
        var wasActive = entry.Status == "downloading";
        CoreWebView2DownloadState state;
        try
        {
            state = operation.State;
            entry.BytesReceived = operation.BytesReceived;
            entry.TotalBytes = DownloadTotalBytes(operation.TotalBytesToReceive);
            if (state == CoreWebView2DownloadState.Completed)
            {
                if (wasActive) { entry.Status = "completed"; entry.CompletedAt = _clock.UtcNow; }
                if (entry.TotalBytes > 0 && entry.BytesReceived < entry.TotalBytes) entry.BytesReceived = entry.TotalBytes;
            }
            else if (state == CoreWebView2DownloadState.Interrupted)
            {
                if (wasActive) entry.Status = operation.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled ? "cancelled" : "interrupted";
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return;
        }
        var finished = state != CoreWebView2DownloadState.InProgress;
        if (finished) _downloadOperations.Remove(entry.Id);
        try { item.Refresh(); } catch (Exception ex) { App.Log(ex); }
        try { UpdateDownloadChrome(); } catch (Exception ex) { App.Log(ex); }
        if (!finished || !wasActive) return;
        if (!isPrivate)
        {
            try { _stateStore.Save(); } catch (Exception ex) { App.Log(ex); }
            try { PushDownloadsToSettings(); } catch (Exception ex) { App.Log(ex); }
        }
        if (state == CoreWebView2DownloadState.Completed)
            try { ShowToast("Загрузка завершена", item.FileName); } catch (Exception ex) { App.Log(ex); }
    }

    private static long DownloadTotalBytes(ulong? value)
    {
        if (!value.HasValue) return 0;
        return value.Value > (ulong)long.MaxValue ? long.MaxValue : (long)value.Value;
    }

    private static string UniqueDownloadPath(string folder, string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(invalid, '_');
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path)) return path;
        var name = Path.GetFileNameWithoutExtension(fileName); var extension = Path.GetExtension(fileName);
        for (var index = 1; index < 10000; index++)
        {
            path = Path.Combine(folder, $"{name} ({index}){extension}");
            if (!File.Exists(path)) return path;
        }
        return Path.Combine(folder, $"{name}-{Guid.NewGuid():N}{extension}");
    }

    private void UpdateDownloadChrome()
    {
        if (DownloadBadge is null) return;
        var active = _downloads.Count(item => item.IsActive);
        DownloadBadge.Visibility = Visibility.Collapsed;
        DownloadActivityIndicator.Visibility = active > 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadIdleIcon.Opacity = active > 0 ? .42 : 1;
        if (active > 0)
        {
            if (!DownloadActivityRotate.HasAnimatedProperties)
                DownloadActivityRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(820)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            DownloadActivityRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
            DownloadActivityRotate.Angle = 0;
        }
        DownloadsEmpty.Visibility = _downloads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadsClearButton.IsEnabled = _downloads.Any(item => !item.IsActive);
    }

    private void Downloads_Click(object sender, RoutedEventArgs e)
    {
        var open = !DownloadPopup.IsOpen;
        CloseMenusExcept(DownloadPopup);
        DownloadPopup.IsOpen = open;
    }

    private void DownloadsOpen_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DownloadItemView item) return;
        try
        {
            if (item.IsActive) { ShowToast("Загрузка ещё не завершена", item.FileName); return; }
            if (item.Status != "completed" || !File.Exists(item.FilePath)) { ShowToast("Файл не найден", item.FilePath, true); return; }
            Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
            DownloadPopup.IsOpen = false;
        }
        catch (Exception ex) { App.Log(ex); ShowToast("Не удалось открыть файл", ex.Message, true); }
    }

    private void DownloadsReveal_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DownloadItemView item) return;
        RevealDownload(item.FilePath);
    }

    private void DownloadsRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DownloadItemView item) return;
        if (_downloadOperations.Remove(item.Id, out var operation)) try { operation.Cancel(); } catch (Exception ex) { App.Log(ex); }
        _downloads.Remove(item);
        if (!((App)WpfApplication.Current).IsPrivateSession)
        {
            _state.Downloads.RemoveAll(entry => entry.Id == item.Id);
            _stateStore.Save();
            PushDownloadsToSettings();
        }
        UpdateDownloadChrome();
    }

    private void DownloadsClear_Click(object sender, RoutedEventArgs e)
    {
        var removable = _downloads.Where(item => !item.IsActive).ToList();
        foreach (var item in removable) _downloads.Remove(item);
        var ids = removable.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        if (!((App)WpfApplication.Current).IsPrivateSession)
        {
            _state.Downloads.RemoveAll(entry => ids.Contains(entry.Id));
            _stateStore.Save();
            PushDownloadsToSettings();
        }
        UpdateDownloadChrome();
    }

    private void DownloadsFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = string.IsNullOrWhiteSpace(_state.DownloadPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") : _state.DownloadPath;
        try { Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
        catch (Exception ex) { App.Log(ex); ShowToast("Не удалось открыть папку", ex.Message, true); }
    }

    private void RevealDownload(string filePath)
    {
        try
        {
            if (File.Exists(filePath)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
            else if (Directory.Exists(Path.GetDirectoryName(filePath))) Process.Start(new ProcessStartInfo(Path.GetDirectoryName(filePath)!) { UseShellExecute = true });
            else ShowToast("Файл не найден", filePath, true);
        }
        catch (Exception ex) { App.Log(ex); ShowToast("Не удалось открыть папку", ex.Message, true); }
    }

    private void OpenDownloadById(string id)
    {
        var item = _downloads.FirstOrDefault(value => value.Id == id);
        if (item is not null) DownloadsOpen_Click(new WpfButton { Tag = item }, new RoutedEventArgs());
    }

    private void RevealDownloadById(string id)
    {
        var item = _downloads.FirstOrDefault(value => value.Id == id);
        if (item is not null) RevealDownload(item.FilePath);
    }

    private void RemoveDownloadById(string id)
    {
        var item = _downloads.FirstOrDefault(value => value.Id == id);
        if (item is not null) DownloadsRemove_Click(new WpfButton { Tag = item }, new RoutedEventArgs());
    }

    private void ClearDownloadHistory() => DownloadsClear_Click(this, new RoutedEventArgs());

    private void PushDownloadsToSettings()
    {
        var data = System.Text.Json.JsonSerializer.Serialize(_state.Downloads.OrderByDescending(item => item.StartedAt).Take(300));
        foreach (var tab in _spaces.SelectMany(space => space.Tabs).Where(tab => tab.IsInternal && tab.InternalPageKind == "luma://settings"))
            if (tab.View.CoreWebView2 is not null) _ = tab.View.CoreWebView2.ExecuteScriptAsync($"window.lumaDownloadsChanged?.({data})");
    }
}
