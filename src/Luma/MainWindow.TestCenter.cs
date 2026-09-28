using Luma.Authentication;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;
using WpfMessageBox = System.Windows.MessageBox;
using WpfClipboard = System.Windows.Clipboard;

namespace Luma;

public partial class MainWindow
{
    public sealed class FeedbackReportView : INotifyPropertyChanged
    {
        private string _status = "new";
        public string Id { get; init; } = "";
        public string Category { get; init; } = "bug";
        public string Subject { get; init; } = "";
        public DateTimeOffset UpdatedAt { get; set; }
        public string Status { get => _status; set { if (_status == value) return; _status = value; Changed(); Changed(nameof(StatusText)); Changed(nameof(StatusColor)); } }
        public string ShortId => "#" + (Id.Length >= 8 ? Id[..8].ToUpperInvariant() : Id.ToUpperInvariant());
        public string UpdatedText => UpdatedAt.LocalDateTime.ToString("dd.MM · HH:mm");
        public string StatusText => Status switch { "checking" => "Проверяется", "need_info" => "Нужна информация", "fixing" => "Исправляется", "fixed" => "Исправлено", "cannot_reproduce" => "Не воспроизводится", "duplicate" => "Дубликат", _ => "Новый" };
        public string StatusColor => Status switch { "fixed" => "#79D9A5", "cannot_reproduce" or "duplicate" => "#9A9BA1", "need_info" => "#F0B078", "fixing" => "#D6C47A", "checking" => "#9FC0F2", _ => "#B5AEDF" };
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class FeedbackMessageView
    {
        public string Id { get; init; } = "";
        public string ReportId { get; init; } = "";
        public string SenderType { get; init; } = "tester";
        public string SenderName { get; init; } = "";
        public string Body { get; init; } = "";
        public string AttachmentName { get; init; } = "";
        public string AttachmentUrl { get; init; } = "";
        public DateTimeOffset CreatedAt { get; init; }
        public bool IsAdmin => SenderType is "admin" or "system";
        public string Author => IsAdmin ? (string.IsNullOrWhiteSpace(SenderName) ? "dakower" : SenderName) : "Вы";
        public string TimeText => CreatedAt.LocalDateTime.ToString("HH:mm");
        public Visibility AttachmentVisibility => string.IsNullOrWhiteSpace(AttachmentUrl) ? Visibility.Collapsed : Visibility.Visible;
        public string BubbleColor => IsAdmin ? "#27282B" : "#2F2B47";
        public string OutlineColor => IsAdmin ? "#45464E" : "#5B5188";
    }

    public sealed class TesterTaskView : INotifyPropertyChanged
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public string Description { get; init; } = "";
        private string _result = "";
        public string Result { get => _result; set { if (_result == value) return; _result = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Result))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StateText))); } }
        public int StartedCount { get; init; }
        public int WorksCount { get; init; }
        public int BugCount { get; init; }
        public string StatsText => $"Начали: {StartedCount} · Работает: {WorksCount} · Ошибок: {BugCount}";
        public string StateText => Result switch { "works" => "Работает", "bug" => "Нашёл ошибку", "started" => "В процессе", _ => "Не начато" };
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed record FeedbackAttachment(string Name, string Mime, byte[] Bytes);
    private sealed class FeedbackEnvelope
    {
        [JsonPropertyName("reports")] public List<FeedbackReportDto> Reports { get; set; } = [];
        [JsonPropertyName("messages")] public List<FeedbackMessageDto> Messages { get; set; } = [];
        [JsonPropertyName("tasks")] public List<TesterTaskDto> Tasks { get; set; } = [];
    }
    private sealed class FeedbackReportDto { [JsonPropertyName("id")] public string Id { get; set; } = ""; [JsonPropertyName("category")] public string Category { get; set; } = ""; [JsonPropertyName("subject")] public string Subject { get; set; } = ""; [JsonPropertyName("status")] public string Status { get; set; } = "new"; [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; } }
    private sealed class FeedbackMessageDto { [JsonPropertyName("id")] public string Id { get; set; } = ""; [JsonPropertyName("report_id")] public string ReportId { get; set; } = ""; [JsonPropertyName("sender_type")] public string SenderType { get; set; } = ""; [JsonPropertyName("sender_name")] public string SenderName { get; set; } = ""; [JsonPropertyName("body")] public string Body { get; set; } = ""; [JsonPropertyName("attachment_name")] public string? AttachmentName { get; set; } [JsonPropertyName("attachment_url")] public string? AttachmentUrl { get; set; } [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; } }
    private sealed class TesterTaskDto { [JsonPropertyName("id")] public string Id { get; set; } = ""; [JsonPropertyName("title")] public string Title { get; set; } = ""; [JsonPropertyName("description")] public string Description { get; set; } = ""; [JsonPropertyName("result")] public string? Result { get; set; } [JsonPropertyName("started_count")] public int StartedCount { get; set; } [JsonPropertyName("works_count")] public int WorksCount { get; set; } [JsonPropertyName("bug_count")] public int BugCount { get; set; } }

    private static readonly HttpClient FeedbackHttp = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly ObservableCollection<FeedbackReportView> _feedbackReports = [];
    private readonly ObservableCollection<FeedbackMessageView> _feedbackMessages = [];
    private readonly ObservableCollection<TesterTaskView> _testerTasks = [];
    private readonly DispatcherTimer _feedbackRefreshTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly Dictionary<string, List<FeedbackMessageView>> _feedbackMessagesByReport = new(StringComparer.Ordinal);
    private FeedbackAttachment? _feedbackAttachment;
    private bool _feedbackBusy;
    private bool _testCenterOpen;
    private string _feedbackCategory = "bug";
    private string? _pendingCrashText;
    private byte[]? _lastPagePreview;
    private string _lastPageUrl = "";
    private string _lastPageTitle = "";
    private bool _testerRecording;
    private readonly List<string> _testerActionLog = [];

    private bool HasTestCenterAccess => _auth.CurrentUser is not null && (_auth.Access?.HasBeta == true || _auth.Access?.IsAdmin == true);

    private void InitializeTestCenter()
    {
        FeedbackReportsList.ItemsSource = _feedbackReports;
        FeedbackMessagesList.ItemsSource = _feedbackMessages;
        TesterTasksList.ItemsSource = _testerTasks;
        _feedbackRefreshTimer.Tick += async (_, _) => { if (_testCenterOpen && !_feedbackBusy) await RefreshFeedbackAsync(false); };
        FeedbackCategoryBox.SelectedIndex = 0;
        FeedbackSeverityBox.SelectedIndex = 0;
        UpdateFeedbackReasons();
    }

    private async Task<JsonDocument> FeedbackRequestAsync(object payload, CancellationToken token = default)
    {
        var accessToken = await _auth.GetAccessTokenAsync(token);
        if (string.IsNullOrWhiteSpace(accessToken)) throw new AuthException("Войдите в аккаунт Luma.", 401);
        using var request = new HttpRequestMessage(HttpMethod.Post, SupabaseOptions.ProjectUrl + "/functions/v1/luma-feedback");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("apikey", SupabaseOptions.PublishableKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await FeedbackHttp.SendAsync(request, token);
        var text = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            string message;
            try { message = JsonDocument.Parse(text).RootElement.TryGetProperty("error", out var error) ? error.GetString() ?? "feedback_unavailable" : "feedback_unavailable"; }
            catch { message = "feedback_unavailable"; }
            throw new AuthException(message switch { "beta_access_required" => "Тест-Центр доступен только тестерам Luma.", "attachment_too_large" => "Файл превышает 8 МБ.", "attachment_type" => "Этот тип файла не поддерживается.", _ => "Сервис обратной связи временно недоступен." }, (int)response.StatusCode);
        }
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private static string SelectedTag(System.Windows.Controls.ComboBox box, string fallback)
        => (box.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private void FeedbackCategory_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateFeedbackReasons();

    private void UpdateFeedbackReasons()
    {
        if (FeedbackReasonBox is null) return;
        var category = SelectedTag(FeedbackCategoryBox, "interface");
        var reasons = category switch
        {
            "search" => new[] { "Ничего не найдено", "Результаты не относятся к запросу", "Не найден известный сайт или человек", "Слишком много результатов одного сайта", "Картинки не загрузились", "Видео без превью", "Поиск слишком долгий" },
            "video" => new[] { "Видео не запускается", "Нет превью", "Не работает полноэкранный режим", "Нет звука", "Неверно определяется воспроизведение" },
            "tabs" => new[] { "Вкладка пропала", "Не восстановилась после запуска", "Неверная активная вкладка", "Проблема разделения экрана", "Слишком медленное переключение" },
            "installation" => new[] { "Не устанавливается", "Не запускается после установки", "Ошибка обновления", "SmartScreen или подпись", "Потерялись данные после обновления" },
            "account" => new[] { "Не удаётся войти", "Не создаётся аккаунт", "Не синхронизируется аватар", "Неверные права тестера", "Сессия неожиданно завершилась" },
            "lumaai" => new[] { "Нет ответа", "Неверный ответ", "Ошибка лимита", "Не видит текущую страницу", "Проблема истории диалогов" },
            "performance" => new[] { "Высокая нагрузка", "Зависание", "Медленный запуск", "Большое потребление памяти", "Сбой приложения" },
            "interface" => new[] { "Элемент расположен неправильно", "Кнопка не работает", "Текст не помещается", "Неверная тема или цвет", "Анимация работает неправильно" },
            _ => new[] { "Функция не работает", "Работает не так, как ожидалось", "Ошибка или исключение", "Другое" }
        };
        FeedbackReasonBox.ItemsSource = reasons;
        FeedbackReasonBox.SelectedIndex = 0;
    }

    private static string SanitizeDiagnosticText(string value)
    {
        value ??= "";
        value = Regex.Replace(value, @"(?i)(bearer\s+)[A-Za-z0-9._~-]+", "$1[REDACTED]");
        value = Regex.Replace(value, @"(?i)(token|access_token|refresh_token|authorization|cookie|password|session|secret|api[_-]?key)\s*[:=]\s*[^\s,;]+", "$1=[REDACTED]");
        value = Regex.Replace(value, @"(?i)([?&](?:token|code|key|session|secret|password|auth)\s*=)[^&#\s]+", "$1[REDACTED]");
        value = Regex.Replace(value, @"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}", "[REDACTED_JWT]");
        return value;
    }

    private static string SafeDiagnosticUrl(string raw)
    {
        try
        {
            var uri = new Uri(raw); if (uri.Scheme is not ("http" or "https")) return uri.GetLeftPart(UriPartial.Path);
            var builder = new UriBuilder(uri); var query = builder.Query.TrimStart('?');
            if (query.Length > 0)
            {
                var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair =>
                {
                    var eq = pair.IndexOf('='); var name = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]);
                    return Regex.IsMatch(name, "token|code|key|session|secret|password|auth", RegexOptions.IgnoreCase) ? Uri.EscapeDataString(name) + "=[REDACTED]" : pair;
                });
                builder.Query = string.Join("&", pairs);
            }
            return builder.Uri.AbsoluteUri;
        }
        catch { return ""; }
    }

    private Dictionary<string, object?> FeedbackDiagnostics(bool includePage = false)
    {
        string webView = ""; try { WebViewRuntime.IsAvailable(out webView); } catch { }
        var dpi = VisualTreeHelper.GetDpi(this);
        var screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).Bounds;
        var theme = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Luma")?.GetValue("Theme") as string ?? "dark";
        string logTail = "";
        try { if (File.Exists(App.LogPath)) { var text = File.ReadAllText(App.LogPath); logTail = SanitizeDiagnosticText(text[^Math.Min(text.Length, 16000)..]); } } catch { }
        string? pageUrl = null, pageTitle = null;
        if (includePage)
        {
            var activeUrl = CurrentTab is { IsHome: false } ? CurrentTab.ActiveUrl : _lastPageUrl;
            var activeTitle = CurrentTab is { IsHome: false } ? CurrentTab.DisplayTitle : _lastPageTitle;
            pageUrl = SafeDiagnosticUrl(activeUrl ?? "");
            pageTitle = activeTitle ?? "";
        }
        return new Dictionary<string, object?>
        {
            ["version"] = AppVersion,
            ["windows"] = Environment.OSVersion.VersionString,
            ["framework"] = RuntimeInformation.FrameworkDescription,
            ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["webView"] = webView,
            ["resolution"] = $"{screen.Width}x{screen.Height}",
            ["scalePercent"] = Math.Round(dpi.DpiScaleX * 100),
            ["theme"] = theme,
            ["tabs"] = _spaces.Sum(space => space.Tabs.Count),
            ["spaces"] = _spaces.Count,
            ["privateSession"] = ((App)WpfApplication.Current).IsPrivateSession,
            ["occurredAt"] = DateTimeOffset.Now.ToString("O"),
            ["lastErrors"] = logTail,
            ["pageUrl"] = pageUrl,
            ["pageTitle"] = pageTitle,
            ["recordedActions"] = _testerActionLog.ToArray()
        };
    }

    private string DiagnosticText(bool includePage = false)
    {
        var d = FeedbackDiagnostics(includePage);
        var report = FeedbackReportsList?.SelectedItem as FeedbackReportView;
        return string.Join(Environment.NewLine, new[]
        {
            $"Luma: {d["version"]}", $"Windows: {d["windows"]}", $"WebView2: {d["webView"]}",
            $"Архитектура: {d["processArchitecture"]}", $"Разрешение: {d["resolution"]}", $"Масштаб: {d["scalePercent"]}%",
            $"Тема: {d["theme"]}", $"Вкладок: {d["tabs"]}", $"Пространств: {d["spaces"]}",
            $"Приватный режим: {(((bool)d["privateSession"]!) ? "да" : "нет")}", $"Время: {d["occurredAt"]}",
            $"Report ID: {report?.ShortId ?? "ещё не создан"}", includePage ? $"Страница: {d["pageTitle"]} · {d["pageUrl"]}" : "Страница: не добавлена"
        });
    }

    private async Task RefreshFeedbackAsync(bool showErrors)
    {
        if (!HasTestCenterAccess || _feedbackBusy) return;
        _feedbackBusy = true;
        try
        {
            using var json = await FeedbackRequestAsync(new { action = "list" });
            var envelope = json.RootElement.Deserialize<FeedbackEnvelope>() ?? new();
            var selectedId = (FeedbackReportsList.SelectedItem as FeedbackReportView)?.Id;
            _feedbackReports.Clear();
            foreach (var report in envelope.Reports) _feedbackReports.Add(new FeedbackReportView { Id = report.Id, Category = report.Category, Subject = report.Subject, Status = report.Status, UpdatedAt = report.UpdatedAt });
            _feedbackMessagesByReport.Clear();
            foreach (var message in envelope.Messages)
            {
                if (!_feedbackMessagesByReport.TryGetValue(message.ReportId, out var list)) _feedbackMessagesByReport[message.ReportId] = list = [];
                list.Add(new FeedbackMessageView { Id = message.Id, ReportId = message.ReportId, SenderType = message.SenderType, SenderName = message.SenderName, Body = message.Body, AttachmentName = message.AttachmentName ?? "", AttachmentUrl = message.AttachmentUrl ?? "", CreatedAt = message.CreatedAt });
            }
            _testerTasks.Clear(); foreach (var task in envelope.Tasks) _testerTasks.Add(new TesterTaskView { Id = task.Id, Title = task.Title, Description = task.Description, Result = task.Result ?? "", StartedCount = task.StartedCount, WorksCount = task.WorksCount, BugCount = task.BugCount });
            TesterTasksEmpty.Visibility = _testerTasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            FeedbackReportsEmpty.Visibility = _feedbackReports.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var selection = _feedbackReports.FirstOrDefault(item => item.Id == selectedId) ?? _feedbackReports.FirstOrDefault();
            FeedbackReportsList.SelectedItem = selection;
            ShowFeedbackMessages(selection?.Id);
            FeedbackSyncText.Text = "Синхронизировано · " + DateTime.Now.ToString("HH:mm");
        }
        catch (Exception ex) { App.Log(ex); if (showErrors) ShowToast("Не удалось обновить Тест-Центр", ex.Message, true); }
        finally { _feedbackBusy = false; }
    }

    private void ShowFeedbackMessages(string? reportId)
    {
        _feedbackMessages.Clear();
        if (reportId is not null && _feedbackMessagesByReport.TryGetValue(reportId, out var list)) foreach (var item in list) _feedbackMessages.Add(item);
        FeedbackChatEmpty.Visibility = _feedbackMessages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FeedbackComposerPanel.Visibility = reportId is null ? Visibility.Collapsed : Visibility.Visible;
        if (_feedbackMessages.Count > 0) Dispatcher.BeginInvoke(() => { FeedbackMessagesList.UpdateLayout(); FeedbackMessagesList.ScrollIntoView(_feedbackMessages[^1]); }, DispatcherPriority.Loaded);
    }

    private void TesterCenterToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!HasTestCenterAccess) { ShowToast("Тест-Центр недоступен", "Доступ проверяется по UID тестера.", true); return; }
        ShowTestCenter(!_testCenterOpen);
    }

    private void ShowTestCenter(bool open)
    {
        if (open == _testCenterOpen) return;
        _testCenterOpen = open;
        var leaving = open ? AccountSurfaceContent : TesterCenterSurface;
        var entering = open ? TesterCenterSurface : AccountSurfaceContent;
        var leavingTransform = open ? AccountSurfaceTranslate : TesterCenterTranslate;
        var enteringTransform = open ? TesterCenterTranslate : AccountSurfaceTranslate;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            leaving.Visibility = Visibility.Collapsed;
            entering.Visibility = Visibility.Visible; entering.Opacity = 0; enteringTransform.Y = open ? 30 : -24;
            entering.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
            enteringTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(enteringTransform.Y, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        };
        leaving.BeginAnimation(OpacityProperty, fade);
        leavingTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(0, open ? -22 : 22, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
        if (open) { _feedbackRefreshTimer.Start(); _ = RefreshFeedbackAsync(true); } else _feedbackRefreshTimer.Stop();
    }

    private void FeedbackReports_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var report = FeedbackReportsList.SelectedItem as FeedbackReportView;
        FeedbackChatTitle.Text = report?.Subject ?? "Выберите отчёт";
        FeedbackChatStatus.Text = report is null ? "" : report.ShortId + " · " + report.StatusText;
        FeedbackVerificationPanel.Visibility = report?.Status == "fixed" ? Visibility.Visible : Visibility.Collapsed;
        ShowFeedbackMessages(report?.Id);
    }

    private void ResetFeedbackForm(string category)
    {
        _feedbackCategory = category; FeedbackNewPanel.Visibility = Visibility.Visible; FeedbackSubjectBox.Clear(); FeedbackWhatBox.Clear(); FeedbackExpectedBox.Clear(); FeedbackStepsBox.Clear(); FeedbackDetailsBox.Clear(); FeedbackIncludePageCheck.IsChecked = false; FeedbackCategoryBox.SelectedIndex = category == "idea" ? 8 : 0; FeedbackSeverityBox.SelectedIndex = 0; UpdateFeedbackReasons(); ClearFeedbackAttachment(); FeedbackSubjectBox.Focus();
    }
    private void FeedbackNewBug_Click(object sender, RoutedEventArgs e) => ResetFeedbackForm("bug");
    private void FeedbackNewIdea_Click(object sender, RoutedEventArgs e) => ResetFeedbackForm("idea");
    private void FeedbackNewCancel_Click(object sender, RoutedEventArgs e) { FeedbackNewPanel.Visibility = Visibility.Collapsed; ClearFeedbackAttachment(); }

    private async void FeedbackCreate_Click(object sender, RoutedEventArgs e)
    {
        if (_feedbackBusy) return;
        var subject = FeedbackSubjectBox.Text.Trim();
        var what = FeedbackWhatBox.Text.Trim(); var expected = FeedbackExpectedBox.Text.Trim(); var steps = FeedbackStepsBox.Text.Trim(); var details = FeedbackDetailsBox.Text.Trim();
        var bugArea = SelectedTag(FeedbackCategoryBox, "interface"); var reason = FeedbackReasonBox.SelectedItem?.ToString() ?? "Другое"; var severity = SelectedTag(FeedbackSeverityBox, "minor");
        var body = $"Категория: {bugArea}\nПричина: {reason}\nСерьёзность: {severity}\n\nЧто произошло:\n{what}\n\nЧто ожидалось:\n{expected}\n\nШаги воспроизведения:\n{steps}" + (details.Length > 0 ? $"\n\nДополнение:\n{details}" : "");
        if (subject.Length < 2 || what.Length < 3 || expected.Length < 3 || steps.Length < 3) { ShowToast("Заполните отчёт", "Нужны название, результат, ожидание и шаги воспроизведения.", true); return; }
        _feedbackBusy = true; FeedbackCreateButton.IsEnabled = false;
        try
        {
            var attachment = _feedbackAttachment is null ? null : new { name = _feedbackAttachment.Name, mime = _feedbackAttachment.Mime, data = Convert.ToBase64String(_feedbackAttachment.Bytes) };
            using var result = await FeedbackRequestAsync(new { action = "create", category = _feedbackCategory, subject, body, diagnostics = FeedbackDiagnostics(FeedbackIncludePageCheck.IsChecked == true), context = new { source = "test_center", bugArea, reason, severity, what, expected, steps, details, pageConsent = FeedbackIncludePageCheck.IsChecked == true }, attachment });
            FeedbackNewPanel.Visibility = Visibility.Collapsed; FeedbackSubjectBox.Clear(); FeedbackWhatBox.Clear(); FeedbackExpectedBox.Clear(); FeedbackStepsBox.Clear(); FeedbackDetailsBox.Clear(); ClearFeedbackAttachment();
            if (_feedbackCategory == "crash") { try { if (File.Exists(App.PendingCrashPath)) File.Delete(App.PendingCrashPath); } catch { } _pendingCrashText = null; }
            _feedbackBusy = false;
            await RefreshFeedbackAsync(false); ShowToast("Отчёт отправлен", "Ответ dakower появится в этом чате.");
        }
        catch (Exception ex) { App.Log(ex); ShowToast("Не удалось отправить отчёт", ex.Message, true); }
        finally { _feedbackBusy = false; FeedbackCreateButton.IsEnabled = true; }
    }

    private async void FeedbackSendMessage_Click(object sender, RoutedEventArgs e)
    {
        if (_feedbackBusy || FeedbackReportsList.SelectedItem is not FeedbackReportView report) return;
        var body = FeedbackMessageBox.Text.Trim(); if (body.Length == 0 && _feedbackAttachment is null) return;
        _feedbackBusy = true; FeedbackSendButton.IsEnabled = false;
        try
        {
            var attachment = _feedbackAttachment is null ? null : new { name = _feedbackAttachment.Name, mime = _feedbackAttachment.Mime, data = Convert.ToBase64String(_feedbackAttachment.Bytes) };
            using var result = await FeedbackRequestAsync(new { action = "message", reportId = report.Id, body, attachment });
            FeedbackMessageBox.Clear(); ClearFeedbackAttachment(); _feedbackBusy = false; await RefreshFeedbackAsync(false);
        }
        catch (Exception ex) { App.Log(ex); ShowToast("Сообщение не отправлено", ex.Message, true); }
        finally { _feedbackBusy = false; FeedbackSendButton.IsEnabled = true; }
    }

    private async void TesterTaskAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.Tag is not TesterTaskView task || _feedbackBusy) return;
        var resultName = button.CommandParameter?.ToString() ?? "started";
        _feedbackBusy = true;
        var previous = task.Result;
        task.Result = resultName;
        button.IsEnabled = false;
        try
        {
            using var result = await FeedbackRequestAsync(new { action = "task", taskId = task.Id, result = resultName });
            RecordTesterAction($"Задание «{task.Title}»: {resultName}");
            if (resultName == "bug") { ResetFeedbackForm("bug"); FeedbackSubjectBox.Text = "Ошибка в задании: " + task.Title; FeedbackStepsBox.Text = task.Description; }
            await RefreshFeedbackAsync(false);
        }
        catch (Exception ex) { task.Result = previous; App.Log(ex); ShowToast("Не удалось обновить задание", ex.Message, true); }
        finally { _feedbackBusy = false; button.IsEnabled = true; }
    }

    private void FeedbackCopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try { WpfClipboard.SetText(DiagnosticText(FeedbackIncludePageCheck?.IsChecked == true)); ShowToast("Диагностика скопирована"); }
        catch (Exception ex) { App.Log(ex); ShowToast("Не удалось скопировать", ex.Message, true); }
    }

    private void FeedbackOpenLog_Click(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(App.LogPath)!); if (!File.Exists(App.LogPath)) File.WriteAllText(App.LogPath, "Журнал Luma пока пуст.\n"); Process.Start(new ProcessStartInfo(App.LogPath) { UseShellExecute = true }); }
        catch (Exception ex) { App.Log(ex); ShowToast("Не удалось открыть журнал", ex.Message, true); }
    }

    private void FeedbackOpenData_Click(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(LumaState.DirectoryPath); Process.Start(new ProcessStartInfo(LumaState.DirectoryPath) { UseShellExecute = true }); }
        catch (Exception ex) { App.Log(ex); ShowToast("Не удалось открыть папку", ex.Message, true); }
    }

    private void FeedbackSaveDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Сохранить диагностику Luma", FileName = $"Luma-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.zip", Filter = "ZIP-архив|*.zip" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            using var archive = ZipFile.Open(dialog.FileName, ZipArchiveMode.Create);
            var diagnostic = archive.CreateEntry("diagnostics.txt"); using (var writer = new StreamWriter(diagnostic.Open(), Encoding.UTF8)) writer.Write(DiagnosticText(false));
            if (File.Exists(App.LogPath)) { var log = archive.CreateEntry("crash-sanitized.log"); using var writer = new StreamWriter(log.Open(), Encoding.UTF8); writer.Write(SanitizeDiagnosticText(File.ReadAllText(App.LogPath))); }
            var actions = archive.CreateEntry("recorded-actions.txt"); using (var writer = new StreamWriter(actions.Open(), Encoding.UTF8)) writer.Write(string.Join(Environment.NewLine, _testerActionLog));
            ShowToast("Диагностика сохранена", Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) { App.Log(ex); ShowToast("Не удалось сохранить ZIP", ex.Message, true); }
    }

    private void FeedbackRecording_Click(object sender, RoutedEventArgs e)
    {
        _testerRecording = !_testerRecording;
        if (_testerRecording)
        {
            _testerActionLog.Clear(); RecordTesterAction("Начата запись воспроизведения ошибки"); TesterRecordingButton.Content = "Остановить и оформить"; TesterRecordingStatus.Text = "Запись активна · текст и нажатия клавиш не сохраняются";
        }
        else
        {
            RecordTesterAction("Запись остановлена"); TesterRecordingButton.Content = "Начать запись проблемы"; TesterRecordingStatus.Text = "Записываются только безопасные действия Luma";
            ResetFeedbackForm("bug");
            FeedbackSubjectBox.Text = L("Записанное воспроизведение ошибки", "Recorded bug reproduction", "Записане відтворення помилки");
            FeedbackWhatBox.Text = L("При выполнении записанных действий возникла ошибка или неожиданное поведение.", "The recorded actions caused an error or unexpected behavior.", "Під час виконання записаних дій сталася помилка або неочікувана поведінка.");
            FeedbackExpectedBox.Text = L("Luma должна выполнить эти действия без ошибки и работать ожидаемым образом.", "Luma should complete these actions without errors and behave as expected.", "Luma має виконати ці дії без помилок і працювати очікувано.");
            FeedbackStepsBox.Text = string.Join(Environment.NewLine, _testerActionLog);
        }
    }

    private void RecordTesterAction(string action)
    {
        if (!_testerRecording) return;
        _testerActionLog.Add($"{DateTime.Now:HH:mm:ss} {action}");
        if (_testerActionLog.Count > 120) _testerActionLog.RemoveAt(0);
    }

    private async void FeedbackVerification_Click(object sender, RoutedEventArgs e)
    {
        if (FeedbackReportsList.SelectedItem is not FeedbackReportView report || sender is not WpfButton button) return;
        var outcome = button.CommandParameter?.ToString() == "failed" ? "failed" : "fixed";
        try { using var result = await FeedbackRequestAsync(new { action = "verification", reportId = report.Id, outcome }); await RefreshFeedbackAsync(false); ShowToast(outcome == "fixed" ? "Спасибо за подтверждение" : "Отчёт снова открыт"); }
        catch (Exception ex) { App.Log(ex); ShowToast("Не удалось отправить подтверждение", ex.Message, true); }
    }

    private void FeedbackAttachFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Прикрепить файл к отчёту", Filter = "Поддерживаемые файлы|*.png;*.jpg;*.jpeg;*.webp;*.txt;*.json;*.zip;*.pdf", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName); if (bytes.Length > 8 * 1024 * 1024) { ShowToast("Файл слишком большой", "Максимум 8 МБ.", true); return; }
            var mime = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".txt" => "text/plain", ".json" => "application/json", ".zip" => "application/zip", ".pdf" => "application/pdf", _ => "application/octet-stream" };
            SetFeedbackAttachment(new FeedbackAttachment(Path.GetFileName(dialog.FileName), mime, bytes));
        }
        catch (Exception ex) { App.Log(ex); ShowToast("Не удалось прикрепить файл", ex.Message, true); }
    }

    private async void FeedbackScreenshot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            byte[]? bytes = null;
            var view = CurrentTab?.ActiveView;
            if (view?.CoreWebView2 is not null)
            {
                using var stream = new MemoryStream();
                await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                bytes = stream.ToArray();
            }
            if ((bytes is null || bytes.Length == 0) && _lastPagePreview is { Length: > 0 }) bytes = _lastPagePreview;
            if (bytes is null || bytes.Length == 0) throw new InvalidOperationException("Не удалось получить изображение активной страницы.");
            SetFeedbackAttachment(new FeedbackAttachment("active-page.png", "image/png", bytes));
            ShowToast("Скриншот прикреплён", "Сохранена активная страница, а не окно отчёта.");
        }
        catch (Exception ex) { App.Log(ex); ShowToast("Скриншот не создан", ex.Message, true); }
    }

    private async Task CacheActivePagePreviewAsync()
    {
        try
        {
            var tab = CurrentTab; var view = tab?.ActiveView; if (view?.CoreWebView2 is null || tab?.IsHome == true) return;
            _lastPageUrl = tab?.ActiveUrl ?? ""; _lastPageTitle = tab?.DisplayTitle ?? "";
            using var stream = new MemoryStream(); await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream); _lastPagePreview = stream.ToArray();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    private void SetFeedbackAttachment(FeedbackAttachment attachment) { _feedbackAttachment = attachment; FeedbackAttachmentName.Text = attachment.Name + " · " + Math.Max(1, attachment.Bytes.Length / 1024) + " КБ"; FeedbackAttachmentBar.Visibility = Visibility.Visible; }
    private void ClearFeedbackAttachment() { _feedbackAttachment = null; if (FeedbackAttachmentBar is not null) FeedbackAttachmentBar.Visibility = Visibility.Collapsed; }
    private void FeedbackRemoveAttachment_Click(object sender, RoutedEventArgs e) => ClearFeedbackAttachment();

    private void FeedbackOpenAttachment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FeedbackMessageView message || string.IsNullOrWhiteSpace(message.AttachmentUrl)) return;
        try { Process.Start(new ProcessStartInfo(message.AttachmentUrl) { UseShellExecute = true }); } catch (Exception ex) { App.Log(ex); }
    }

    private string ShowCrashReportChoice()
    {
        var choice = "none";
        var dialog = new Window
        {
            Owner = this, Title = "Luma — отчёт после сбоя", Width = 560, Height = 270,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1B1C1F")), Foreground = System.Windows.Media.Brushes.White,
            ShowInTaskbar = false
        };
        var root = new System.Windows.Controls.Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new System.Windows.Controls.TextBlock { Text = "Похоже, в прошлый раз Luma завершилась с ошибкой", FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var description = new System.Windows.Controls.TextBlock { Text = "Отправить обезличенный отчёт? Перед отправкой можно посмотреть данные. Пароли, cookie, токены и чувствительные параметры URL удаляются.", Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B7B8BF")), Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        System.Windows.Controls.Grid.SetRow(description, 1);
        var actions = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        System.Windows.Controls.Grid.SetRow(actions, 2);
        void Add(string text, string value, bool primary = false)
        {
            var button = new System.Windows.Controls.Button { Content = text, MinWidth = 112, Height = 38, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 0, 14, 0), Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(primary ? "#6657A8" : "#292A2E")), Foreground = System.Windows.Media.Brushes.White, BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(primary ? "#8777C8" : "#46474E")) };
            button.Click += (_, _) => { choice = value; dialog.Close(); }; actions.Children.Add(button);
        }
        Add("Не отправлять", "discard"); Add("Посмотреть данные", "view"); Add("Отправить", "send", true);
        root.Children.Add(heading); root.Children.Add(description); root.Children.Add(actions); dialog.Content = root; dialog.ShowDialog();
        return choice;
    }

    private async Task OfferPendingCrashReportAsync()
    {
        if (!HasTestCenterAccess || ((App)WpfApplication.Current).IsPrivateSession || !File.Exists(App.PendingCrashPath)) return;
        try { _pendingCrashText = File.ReadAllText(App.PendingCrashPath); } catch { return; }
        while (true)
        {
            var choice = ShowCrashReportChoice();
            if (choice == "view")
            {
                var preview = SanitizeDiagnosticText(_pendingCrashText ?? "");
                WpfMessageBox.Show(this, preview[..Math.Min(preview.Length, 12000)], "Данные обезличенного отчёта", MessageBoxButton.OK, MessageBoxImage.Information);
                continue;
            }
            if (choice == "send")
            {
                try
                {
                    using var result = await FeedbackRequestAsync(new { action = "create", category = "crash", subject = "Некорректное завершение Luma", body = "Luma обнаружила, что предыдущая сессия не завершилась корректно.", diagnostics = FeedbackDiagnostics(), context = new { source = "startup_crash_offer", crash = SanitizeDiagnosticText(_pendingCrashText ?? "") } });
                    File.Delete(App.PendingCrashPath); _pendingCrashText = null; ShowToast("Отчёт отправлен", "Спасибо — данные переданы добровольно.");
                }
                catch (Exception ex)
                {
                    App.Log(ex); ShowToast("Не удалось отправить отчёт", ex.Message, true);
                    OpenHomePage(true); ShowTestCenter(true); _feedbackCategory = "crash"; FeedbackNewPanel.Visibility = Visibility.Visible; FeedbackSubjectBox.Text = "Сбой Luma"; FeedbackWhatBox.Text = "Предыдущая сессия Luma завершилась некорректно; автоматическая отправка отчёта не удалась."; FeedbackExpectedBox.Text = "Luma должна завершаться корректно и предлагать добровольную отправку диагностики."; FeedbackStepsBox.Text = "Откройте приложенную обезличенную диагностику и журнал сбоя.";
                }
                return;
            }
            try { File.Delete(App.PendingCrashPath); } catch { }
            _pendingCrashText = null;
            return;
        }
    }

    private async Task SendBadSearchFeedbackAsync(WebView2 view, JsonElement root)
    {
        try
        {
            if (!HasTestCenterAccess) throw new AuthException("Функция доступна тестерам Luma.", 403);
            var query = root.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
            var mode = root.TryGetProperty("mode", out var m) ? m.GetString() ?? "all" : "all";
            var reason = root.TryGetProperty("reason", out var reasonNode) ? reasonNode.GetString() ?? "" : "";
            var expected = root.TryGetProperty("expected", out var expectedNode) ? expectedNode.GetString() ?? "" : "";
            var reasonText = reason switch
            {
                "nothing_found" => "Ничего не найдено", "irrelevant" => "Результаты не относятся к запросу",
                "known_missing" => "Не найден известный сайт или человек", "domain_dominates" => "Слишком много результатов одного сайта",
                "images_failed" => "Картинки не загрузились", "preview_missing" => "Видео без превью",
                "too_slow" => "Поиск слишком долгий", _ => "Другая проблема"
            };
            JsonElement? Clone(string name) => root.TryGetProperty(name, out var value) ? value.Clone() : null;
            var resultCount = root.TryGetProperty("resultCount", out var countNode) && countNode.TryGetInt32(out var count) ? count : 0;
            var elapsedMs = root.TryGetProperty("elapsedMs", out var elapsedNode) && elapsedNode.TryGetInt64(out var elapsed) ? elapsed : 0;
            var body = $"Причина: {reasonText}\nОжидалось: {(string.IsNullOrWhiteSpace(expected) ? "не указано" : expected)}\nРезультатов: {resultCount}\nОбщее время: {elapsedMs} мс";
            using var result = await FeedbackRequestAsync(new
            {
                action = "create", category = "search", subject = "Плохая выдача: " + query[..Math.Min(query.Length, 100)], body,
                diagnostics = FeedbackDiagnostics(),
                context = new { source = "luma_search", query, mode, reason, expected, resultCount, elapsedMs, domains = Clone("domains"), media = Clone("media"), searchDiagnostics = Clone("searchDiagnostics"), results = Clone("results") }
            });
            await view.CoreWebView2.ExecuteScriptAsync("window.lumaSearchFeedbackResult?.({ok:true})");
            ShowToast("Спасибо", "Подробный отчёт о выдаче отправлен.");
        }
        catch (Exception ex)
        {
            App.Log(ex); var message = JsonSerializer.Serialize(ex.Message); if (view.CoreWebView2 is not null) await view.CoreWebView2.ExecuteScriptAsync($"window.lumaSearchFeedbackResult?.({{ok:false,error:{message}}})");
        }
    }
}
