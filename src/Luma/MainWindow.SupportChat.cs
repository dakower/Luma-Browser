using Luma.Authentication;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Luma;

public sealed class HomeSupportChatMessage
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("thread_id")] public string ThreadId { get; init; } = "";
    [JsonPropertyName("sender_type")] public string SenderType { get; init; } = "user";
    [JsonPropertyName("sender_name")] public string SenderName { get; init; } = "";
    [JsonPropertyName("body")] public string Body { get; init; } = "";
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }

    public bool IsAdmin => SenderType is "admin" or "system";
    public string Author => IsAdmin ? (string.IsNullOrWhiteSpace(SenderName) ? "dakower" : SenderName) : "Вы";
    public string TimeText => CreatedAt.LocalDateTime.ToString("HH:mm");
    public Visibility AdminVisibility => IsAdmin ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UserVisibility => IsAdmin ? Visibility.Collapsed : Visibility.Visible;
}

public partial class MainWindow
{
    private static readonly HttpClient SupportHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly ObservableCollection<HomeSupportChatMessage> _homeSupportMessages = [];
    private readonly DispatcherTimer _supportChatRealtimeTimer = new() { Interval = TimeSpan.FromMilliseconds(1000) };
    private readonly DispatcherTimer _supportChatBackgroundTimer = new() { Interval = TimeSpan.FromSeconds(25) };
    private string? _supportThreadId;
    private bool _homeSupportOpen;
    private bool _supportChatBusySending;
    private bool _supportChatPolling;
    private DateTimeOffset _lastViewedSupportChatTime = DateTimeOffset.Now;

    private string GetSupportGuestId()
    {
        if (string.IsNullOrWhiteSpace(_state.SupportGuestId))
        {
            _state.SupportGuestId = Guid.NewGuid().ToString("N");
            Save();
        }
        return _state.SupportGuestId;
    }

    private string GetSupportDisplayName()
    {
        var user = _auth.CurrentUser;
        if (user is not null && !string.IsNullOrWhiteSpace(user.DisplayName))
            return user.DisplayName;
        if (user is not null && !string.IsNullOrWhiteSpace(user.Email))
            return user.Email.Split('@')[0];
        return "Пользователь Luma";
    }

    private void InitializeSupportChat()
    {
        _supportThreadId = _state.SupportThreadId;
        HomeSupportMessagesList.ItemsSource = _homeSupportMessages;

        _supportChatRealtimeTimer.Tick += async (_, _) =>
        {
            if (_homeSupportOpen && !_supportChatBusySending)
            {
                await RefreshSupportChatAsync(silent: true);
            }
        };

        _supportChatBackgroundTimer.Tick += async (_, _) =>
        {
            if (!_homeSupportOpen)
            {
                await RefreshSupportChatAsync(silent: true);
            }
        };
        _supportChatBackgroundTimer.Start();
    }

    private async Task<JsonDocument> SupportRequestAsync(object payload, CancellationToken token = default)
    {
        var accessToken = await _auth.GetAccessTokenAsync(token);
        using var request = new HttpRequestMessage(HttpMethod.Post, SupabaseOptions.ProjectUrl + "/functions/v1/luma-support");
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        request.Headers.TryAddWithoutValidation("apikey", SupabaseOptions.PublishableKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await SupportHttp.SendAsync(request, token);
        var text = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            string message = "Сервис чата временно недоступен.";
            try
            {
                if (JsonDocument.Parse(text).RootElement.TryGetProperty("error", out var err))
                {
                    var code = err.GetString();
                    if (code == "message_empty") message = "Сообщение не может быть пустым.";
                }
            }
            catch { }
            throw new Exception(message);
        }
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private void HomeSupport_Click(object sender, RoutedEventArgs e)
    {
        if (_homeSupportOpen) CloseSupportChat();
        else OpenSupportChat();
    }

    private void OpenSupportChat()
    {
        if (_homeSupportOpen) return;
        _homeSupportOpen = true;

        if (HomeCustomizeDrawerOverlay.Visibility == Visibility.Visible)
            HomeCustomizeDrawerOverlay.Visibility = Visibility.Collapsed;

        HomeSupportBadge.Visibility = Visibility.Collapsed;
        _lastViewedSupportChatTime = DateTimeOffset.Now;
        HomeSupportIconRotate.Angle = 180;

        HomeSupportChatCard.Visibility = Visibility.Visible;
        HomeSupportChatCard.Opacity = 0;
        HomeSupportChatTranslate.Y = 20;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        HomeSupportChatCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        HomeSupportChatTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });

        HomeSupportAuthPrompt.Visibility = Visibility.Collapsed;
        HomeSupportChatPanel.Visibility = Visibility.Visible;
        HomeSupportComposer.Visibility = Visibility.Visible;

        _supportChatRealtimeTimer.Start();
        _ = RefreshSupportChatAsync(silent: false);
        Dispatcher.BeginInvoke(() => HomeSupportInputBox.Focus(), DispatcherPriority.Input);
    }

    private void CloseSupportChat()
    {
        if (!_homeSupportOpen) return;
        _homeSupportOpen = false;
        _supportChatRealtimeTimer.Stop();
        HomeSupportIconRotate.Angle = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            if (!_homeSupportOpen)
                HomeSupportChatCard.Visibility = Visibility.Collapsed;
        };
        HomeSupportChatCard.BeginAnimation(OpacityProperty, fade);
        HomeSupportChatTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(0, 18, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
    }

    private void HomeSupportClose_Click(object sender, RoutedEventArgs e) => CloseSupportChat();

    private void HomeSupportChatCard_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void HomeSupportOpenAccount_Click(object sender, RoutedEventArgs e)
    {
        CloseSupportChat();
        OpenHomePage(account: true);
    }

    private void HomeSupportInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        HomeSupportPlaceholder.Visibility = string.IsNullOrEmpty(HomeSupportInputBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HomeSupportInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            HomeSupportSend_Click(sender, e);
        }
    }

    private async void HomeSupportSend_Click(object sender, RoutedEventArgs e)
    {
        if (_supportChatBusySending) return;
        var text = HomeSupportInputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        HomeSupportInputBox.Clear();
        HomeSupportPlaceholder.Visibility = Visibility.Visible;
        HomeSupportSendButton.IsEnabled = false;
        HomeSupportSendingStatus.Visibility = Visibility.Visible;
        _supportChatBusySending = true;

        var optimisticMsg = new HomeSupportChatMessage
        {
            Id = "temp-" + Guid.NewGuid().ToString("N"),
            SenderType = "user",
            SenderName = GetSupportDisplayName(),
            Body = text,
            CreatedAt = DateTimeOffset.Now
        };
        _homeSupportMessages.Add(optimisticMsg);
        HomeSupportEmptyPrompt.Visibility = Visibility.Collapsed;
        ScrollSupportMessagesToEnd();

        try
        {
            using var doc = await SupportRequestAsync(new
            {
                action = "send",
                threadId = _supportThreadId,
                body = text,
                guestId = GetSupportGuestId(),
                displayName = GetSupportDisplayName(),
                version = AppVersion
            });

            if (doc.RootElement.TryGetProperty("threadId", out var tIdEl))
            {
                var newThreadId = tIdEl.GetString();
                if (!string.IsNullOrEmpty(newThreadId) && _supportThreadId != newThreadId)
                {
                    _supportThreadId = newThreadId;
                    _state.SupportThreadId = newThreadId;
                    Save();
                }
            }

            _supportChatBusySending = false;
            await RefreshSupportChatAsync(silent: true);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("Не удалось отправить", ex.Message, true);
        }
        finally
        {
            _supportChatBusySending = false;
            HomeSupportSendingStatus.Visibility = Visibility.Collapsed;
            HomeSupportSendButton.IsEnabled = true;
        }
    }

    private async Task RefreshSupportChatAsync(bool silent)
    {
        if (_feedbackBusy || _supportChatPolling) return;
        _supportChatPolling = true;

        try
        {
            using var json = await SupportRequestAsync(new
            {
                action = "sync",
                threadId = _supportThreadId,
                guestId = GetSupportGuestId()
            });

            if (json.RootElement.TryGetProperty("threadId", out var tIdEl))
            {
                var tId = tIdEl.GetString();
                if (!string.IsNullOrEmpty(tId) && _supportThreadId != tId)
                {
                    _supportThreadId = tId;
                    _state.SupportThreadId = tId;
                    Save();
                }
            }

            List<HomeSupportChatMessage> serverMsgs = [];
            if (json.RootElement.TryGetProperty("messages", out var msgsEl))
            {
                serverMsgs = msgsEl.Deserialize<List<HomeSupportChatMessage>>() ?? [];
            }

            if (!_homeSupportOpen)
            {
                var hasNewAdmin = serverMsgs.Any(m => (m.SenderType is "admin" or "system") && m.CreatedAt > _lastViewedSupportChatTime);
                if (hasNewAdmin) HomeSupportBadge.Visibility = Visibility.Visible;
            }

            bool countDiff = serverMsgs.Count != _homeSupportMessages.Count(m => !m.Id.StartsWith("temp-", StringComparison.Ordinal));
            bool hasUnsaved = _homeSupportMessages.Any(m => m.Id.StartsWith("temp-", StringComparison.Ordinal));

            if (countDiff || hasUnsaved || (_homeSupportMessages.Count == 0 && serverMsgs.Count > 0))
            {
                _homeSupportMessages.Clear();
                foreach (var sm in serverMsgs)
                {
                    _homeSupportMessages.Add(sm);
                }

                HomeSupportEmptyPrompt.Visibility = _homeSupportMessages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                if (_homeSupportOpen)
                {
                    ScrollSupportMessagesToEnd();
                }
            }
            else if (serverMsgs.Count == 0)
            {
                HomeSupportEmptyPrompt.Visibility = _homeSupportMessages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                App.Log(ex);
                ShowToast("Поддержка", "Не удалось загрузить сообщения.", true);
            }
        }
        finally
        {
            _supportChatPolling = false;
        }
    }

    private void ScrollSupportMessagesToEnd()
    {
        Dispatcher.BeginInvoke(() =>
        {
            HomeSupportMessagesList.UpdateLayout();
            if (HomeSupportMessagesList.Items.Count > 0)
            {
                HomeSupportMessagesList.ScrollIntoView(HomeSupportMessagesList.Items[^1]);
            }
            HomeSupportScrollViewer.ScrollToEnd();
        }, DispatcherPriority.Loaded);
    }
}
