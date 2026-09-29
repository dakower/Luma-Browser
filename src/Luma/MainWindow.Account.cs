using Luma.Authentication;
using Luma.Core;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using System.Windows;
using WpfApplication = System.Windows.Application;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfControl = System.Windows.Controls.Control;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;
using WpfClipboard = System.Windows.Clipboard;
using System.Windows.Threading;
using WpfDoubleAnimation = System.Windows.Media.Animation.DoubleAnimation;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKeyboardFocusChangedEventArgs = System.Windows.Input.KeyboardFocusChangedEventArgs;
using WpfScaleTransform = System.Windows.Media.ScaleTransform;

namespace Luma;

public partial class MainWindow
{
    private readonly IAuthService _auth;
    private bool _accountRegisterMode = true;
    private bool _accountBusy;
    private readonly DispatcherTimer _accountUsageTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private int _unsavedUsageSeconds;
    private static readonly HttpClient AvatarHttp = new() { Timeout = TimeSpan.FromSeconds(12) };
    private string? _avatarAccountId;

    private void AccountSessionChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) UpdateAccountMenu();
        else Dispatcher.BeginInvoke(UpdateAccountMenu);
    }

    private void UpdateAccountMenu()
    {
        var user = _auth.CurrentUser;
        AccountMenuText.Text = user?.DisplayName ?? "Аккаунт Luma";
        AccountMenuStatus.Text = user is null ? L("Войти или зарегистрироваться", "Sign in or create an account", "Увійти або зареєструватися") : _auth.Access?.IsAdmin == true ? L("Администратор Luma", "Luma administrator", "Адміністратор Luma") : _auth.Access?.HasBeta == true ? L("Тестер Luma", "Luma tester", "Тестувальник Luma") : user.Email;
        var letter = user is null || string.IsNullOrWhiteSpace(user.DisplayName) ? "L" : user.DisplayName.Trim()[..1].ToUpperInvariant();
        AccountMenuAvatar.Text = letter;
        SidebarProfileFallback.Text = letter;
        SidebarProfileName.Text = user?.DisplayName ?? "Аккаунт Luma";
        SidebarProfileStatus.Text = user is null ? L("Войти или зарегистрироваться", "Sign in or create an account", "Увійти або зареєструватися") : _auth.Access?.IsAdmin == true ? L("Администратор Luma", "Luma administrator", "Адміністратор Luma") : _auth.Access?.HasBeta == true ? L("Тестер Luma", "Luma tester", "Тестувальник Luma") : user.Email;
        RefreshAvatarVisuals();
        _ = SyncAvatarForCurrentAccountAsync();
        RefreshAccountSurface();
    }

    private void RefreshAccountSurface()
    {
        if (AccountSignedOutPanel is null) return;
        var user = _auth.CurrentUser;
        AccountSignedOutPanel.Visibility = user is null ? Visibility.Visible : Visibility.Collapsed;
        AccountProfilePanel.Visibility = user is null ? Visibility.Collapsed : Visibility.Visible;
        AccountSurfaceCloseButton.Visibility = CurrentTab is null ? Visibility.Collapsed : Visibility.Visible;
        if (user is null)
        {
            AccountHeroEyebrow.Text = "LUMA ACCOUNT"; AccountHeroTitle.Text = "Авторизация"; AccountHeroDescription.Text = "Войдите или создайте аккаунт, чтобы открыть\nвозможности Luma.";
            AccountStatsPanel.Visibility = Visibility.Collapsed; AccountLoggedOutBadges.Visibility = Visibility.Visible;
            AccountAdminBadge.Visibility = AccountBetaBadge.Visibility = AccountAdminPanel.Visibility = TesterCenterEntry.Visibility = Visibility.Collapsed;
            if (_testCenterOpen) ShowTestCenter(false);
            SetAccountMode(_accountRegisterMode, false); return;
        }
        AccountHeroEyebrow.Text = "LUMA INSIGHTS"; AccountHeroTitle.Text = "Статистика"; AccountHeroDescription.Text = "Ваше время и привычки в Luma.";
        AccountStatsPanel.Visibility = Visibility.Visible; AccountLoggedOutBadges.Visibility = Visibility.Collapsed; RefreshAccountStatistics();
        var access = _auth.Access; AccountAdminBadge.Visibility = access?.IsAdmin == true ? Visibility.Visible : Visibility.Collapsed; AccountBetaBadge.Visibility = access?.IsAdmin != true && access?.HasBeta == true ? Visibility.Visible : Visibility.Collapsed; AccountAdminPanel.Visibility = access?.IsAdmin == true ? Visibility.Visible : Visibility.Collapsed;
        TesterCenterEntry.Visibility = access?.HasBeta == true || access?.IsAdmin == true ? Visibility.Visible : Visibility.Collapsed;
        if (TesterCenterEntry.Visibility != Visibility.Visible && _testCenterOpen) ShowTestCenter(false);
        AccountProfileName.Text = user.DisplayName;
        AccountProfileEmail.Text = user.Email;
        AccountProfileAvatar.Text = string.IsNullOrWhiteSpace(user.DisplayName) ? "L" : user.DisplayName.Trim()[..1].ToUpperInvariant();
        RefreshAvatarVisuals();
    }

    private string? AvatarPath
    {
        get
        {
            var id = _auth.CurrentUser?.Id;
            if (string.IsNullOrWhiteSpace(id)) return null;
            var safe = string.Concat(id.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
            return Path.Combine(LumaState.DirectoryPath, $"profile-avatar-{safe}.png");
        }
    }

    private BitmapSource? LoadLocalAvatar()
    {
        try
        {
            var path = AvatarPath;
            if (path is null || !File.Exists(path)) return null;
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0]; frame.Freeze(); return frame;
        }
        catch { return null; }
    }

    private string? AvatarRevisionPath => AvatarPath is { } path ? path + ".remote" : null;

    private async Task SyncAvatarForCurrentAccountAsync()
    {
        var user = _auth.CurrentUser;
        var accountId = user?.Id;
        _avatarAccountId = accountId;
        if (user is null) { RefreshAvatarVisuals(); return; }
        var path = AvatarPath;
        var remoteUrl = user.AvatarUrl;
        if (path is null || string.IsNullOrWhiteSpace(remoteUrl)) return;
        var revisionPath = AvatarRevisionPath;
        try
        {
            if (File.Exists(path) && revisionPath is not null && File.Exists(revisionPath) && string.Equals((await File.ReadAllTextAsync(revisionPath)).Trim(), remoteUrl, StringComparison.Ordinal)) return;
        }
        catch (Exception ex) { App.Log(ex); }
        try
        {
            var bytes = await AvatarHttp.GetByteArrayAsync(remoteUrl);
            if (bytes.Length == 0 || _auth.CurrentUser?.Id != accountId || _avatarAccountId != accountId) return;
            Directory.CreateDirectory(LumaState.DirectoryPath);
            var temp = path + ".tmp"; await File.WriteAllBytesAsync(temp, bytes); File.Move(temp, path, true);
            if (revisionPath is not null) await File.WriteAllTextAsync(revisionPath, remoteUrl);
            if (_auth.CurrentUser?.Id == accountId) RefreshAvatarVisuals();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    private void RefreshAvatarVisuals()
    {
        var image = _auth.CurrentUser is null ? null : LoadLocalAvatar();
        var hasImage = image is not null;
        SidebarProfileAvatarImage.Source = image;
        SidebarProfileAvatarImage.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        SidebarProfileFallback.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
        AccountMenuAvatarImage.Source = image;
        AccountMenuAvatarImage.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        AccountMenuAvatar.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
        AccountProfileAvatarImage.Source = image;
        AccountProfileAvatarImage.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        AccountProfileAvatar.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
        AccountRemoveAvatarButton.IsEnabled = _auth.CurrentUser is not null && (hasImage || !string.IsNullOrWhiteSpace(_auth.CurrentUser.AvatarUrl));
        AccountRemoveAvatarButton.Opacity = AccountRemoveAvatarButton.IsEnabled ? 1 : .45;
    }

    private async void AccountChooseAvatar_Click(object sender, RoutedEventArgs e)
    {
        if (_accountBusy) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите аватар Luma",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Все файлы|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        byte[] pngBytes;
        try
        {
            if (!_auth.IsAuthenticated || AvatarPath is not { } avatarPath) { ShowAccountStatus("Сначала войдите в аккаунт Luma."); return; }
            Directory.CreateDirectory(LumaState.DirectoryPath);
            BitmapFrame frame;
            using (var input = System.IO.File.Open(dialog.FileName, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
            {
                var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                frame = decoder.Frames[0];
            }
            BitmapSource source = frame;
            const double maxSide = 512;
            var scale = Math.Min(1d, maxSide / Math.Max(frame.PixelWidth, frame.PixelHeight));
            if (scale < .999) source = new TransformedBitmap(frame, new WpfScaleTransform(scale, scale));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var encoded = new MemoryStream();
            encoder.Save(encoded);
            pngBytes = encoded.ToArray();
            var temp = avatarPath + ".tmp";
            await File.WriteAllBytesAsync(temp, pngBytes);
            File.Move(temp, avatarPath, true);
            RefreshAvatarVisuals();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowAccountStatus("Не удалось установить изображение. Выберите PNG или JPG.");
            return;
        }

        if (!_auth.IsAuthenticated)
        {
            ShowAccountStatus("Аватар сохранён на устройстве. Войдите в аккаунт, чтобы синхронизировать его с сайтом.", true);
            return;
        }
        await RunAccountBusyAsync(async () =>
        {
            var remoteUrl = await _auth.UploadAvatarAsync(pngBytes);
            if (AvatarRevisionPath is { } revisionPath) await File.WriteAllTextAsync(revisionPath, remoteUrl);
            ShowAccountStatus("Аватар обновлён в браузере и синхронизирован с сайтом.", true);
        });
    }

    private async void AccountRemoveAvatar_Click(object sender, RoutedEventArgs e)
    {
        if (_accountBusy) return;
        try { if (AvatarPath is { } avatarPath && File.Exists(avatarPath)) File.Delete(avatarPath); if (AvatarRevisionPath is { } revisionPath && File.Exists(revisionPath)) File.Delete(revisionPath); }
        catch (Exception ex) { App.Log(ex); ShowAccountStatus("Не удалось удалить локальный аватар."); return; }
        RefreshAvatarVisuals();
        if (!_auth.IsAuthenticated)
        {
            ShowAccountStatus("Аватар удалён с устройства. Используется первая буква имени.", true);
            return;
        }
        await RunAccountBusyAsync(async () =>
        {
            await _auth.RemoveAvatarAsync();
            ShowAccountStatus("Аватар удалён в браузере и на сайте.", true);
        });
    }

    private async void Account_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientUi();
        await CacheActivePagePreviewAsync();
        OpenHomePage(true);
        if (_auth.CurrentUser is null) AccountEmailBox.Focus();
    }

    private void AnimateAccountSurface()
    {
        AccountSurfaceContent.BeginAnimation(OpacityProperty, null);
        AccountSurfaceTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        AccountSurfaceContent.Opacity = 0;
        AccountSurfaceTranslate.Y = 18;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        AccountSurfaceContent.BeginAnimation(OpacityProperty, new WpfDoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
        AccountSurfaceTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new WpfDoubleAnimation(18, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
    }

    private void AccountSurfaceClose_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTab is null) return;
        ShowHomeSurface(false);
    }

    private void AccountLoginMode_Click(object sender, RoutedEventArgs e) => SetAccountMode(false);
    private void AccountRegisterMode_Click(object sender, RoutedEventArgs e) => SetAccountMode(true);
    private void SetAccountMode(bool register, bool clearStatus = true)
    {
        _accountRegisterMode = register;
        AccountHeading.Text = register ? "Создайте свой аккаунт" : "С возвращением";
        AccountSubheading.Text = register ? "Всё нужное — в одном красивом пространстве." : "Продолжайте с того места, где остановились.";
        AccountSubmitButton.Content = register ? "Создать аккаунт" : "Войти в Luma";
        AccountDisplayNamePanel.Visibility = AccountConfirmPasswordPanel.Visibility = AccountPasswordHint.Visibility = register ? Visibility.Visible : Visibility.Collapsed;
        AccountForgotPasswordButton.Visibility = register ? Visibility.Collapsed : Visibility.Visible;

        // Do not use FindResource here: these styles belong to the embedded surface,
        // not Window.Resources. Direct visual state avoids the startup/click crash.
        if (register)
        {
            AccountLoginModeButton.Background = WpfBrushes.Transparent;
            AccountLoginModeButton.SetResourceReference(WpfControl.ForegroundProperty, "ChromeMuted");
            AccountRegisterModeButton.SetResourceReference(WpfControl.BackgroundProperty, "AccentSoftBrush");
            AccountRegisterModeButton.Foreground = WpfBrushes.White;
        }
        else
        {
            AccountLoginModeButton.SetResourceReference(WpfControl.BackgroundProperty, "AccentSoftBrush");
            AccountLoginModeButton.Foreground = WpfBrushes.White;
            AccountRegisterModeButton.Background = WpfBrushes.Transparent;
            AccountRegisterModeButton.SetResourceReference(WpfControl.ForegroundProperty, "ChromeMuted");
        }
        if (clearStatus) HideAccountStatus();
        AnimateModeChange();
    }

    private void AnimateModeChange()
    {
        AccountSignedOutPanel.BeginAnimation(OpacityProperty, null);
        AccountSignedOutPanel.BeginAnimation(OpacityProperty, new WpfDoubleAnimation(.72, 1, TimeSpan.FromMilliseconds(190)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private async void AccountSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (_accountBusy) return;
        var email = AccountEmailBox.Text.Trim();
        var password = AccountPasswordInput.Password;
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) { ShowAccountStatus("Введите корректную электронную почту."); FocusAccountField(AccountEmailBox); return; }
        if (_accountRegisterMode)
        {
            if (!AuthPolicy.IsDisplayNameValid(AccountDisplayNameBox.Text)) { ShowAccountStatus("Имя должно содержать от 2 до 40 символов."); FocusAccountField(AccountDisplayNameBox); return; }
            if (!AuthPolicy.IsStrongPassword(password)) { ShowAccountStatus("Пароль: минимум 6 символов, хотя бы одна буква и одна цифра."); FocusAccountField(AccountPasswordInput); return; }
            if (password != AccountConfirmPasswordInput.Password) { ShowAccountStatus("Пароли не совпадают."); FocusAccountField(AccountConfirmPasswordInput); return; }
        }
        else if (string.IsNullOrEmpty(password)) { ShowAccountStatus("Введите пароль."); FocusAccountField(AccountPasswordInput); return; }

        await RunAccountBusyAsync(async () =>
        {
            if (_accountRegisterMode)
            {
                var result = await _auth.SignUpAsync(email, password, AccountDisplayNameBox.Text);
                if (result.RequiresEmailConfirmation)
                {
                    SetAccountMode(false);
                    AccountPasswordInput.Clear(); AccountConfirmPasswordInput.Clear();
                    ShowAccountStatus("Аккаунт создан. Подтвердите почту и войдите.", true);
                    return;
                }
            }
            else await _auth.SignInAsync(email, password);
            UpdateAccountMenu();
            AnimateAccountSurface();
        });
    }

    private static void FocusAccountField(WpfControl field) { field.Focus(); }

    private async void AccountForgotPassword_Click(object sender, RoutedEventArgs e)
    {
        var email = AccountEmailBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) { ShowAccountStatus("Сначала введите электронную почту."); FocusAccountField(AccountEmailBox); return; }
        await RunAccountBusyAsync(async () => { await _auth.SendPasswordResetAsync(email); ShowAccountStatus("Ссылка для восстановления отправлена на почту.", true); });
    }

    private async void AccountSignOut_Click(object sender, RoutedEventArgs e) => await RunAccountBusyAsync(async () =>
    {
        await _auth.SignOutAsync();
        _accountRegisterMode = false;
        _assistantRun?.Cancel();
        _assistantHistory.Clear();
        _state.AssistantConversations.Clear();
        _state.AssistantQuota = new();
        PostAssistant(new { kind = "clear" });
        Save();
        UpdateAccountMenu();
        AnimateAccountSurface();
    });

    private async void AccountCreateBetaCode_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(AdminCodeUsesBox.Text, out var uses)) uses = 1;
        if (!int.TryParse(AdminCodeDaysBox.Text, out var days)) days = 30;
        await RunAccountBusyAsync(async () =>
        {
            var code = await _auth.CreateBetaCodeAsync(AdminCodeLabelBox.Text, uses, days);
            AdminCodeResultText.Text = code; AdminCodeResultCard.Visibility = Visibility.Visible;
            WpfClipboard.SetText(code); ShowAccountStatus("Код создан и скопирован. Он уже работает на сайте.", true);
        });
    }
    private void AccountCopyBetaCode_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrWhiteSpace(AdminCodeResultText.Text)) { WpfClipboard.SetText(AdminCodeResultText.Text); ShowAccountStatus("Код скопирован.", true); } }
    private void StartAccountUsageTracking()
    {
        _accountUsageTimer.Tick += (_, _) =>
        {
            if (!IsActive || ((App)WpfApplication.Current).IsPrivateSession) return;
            _state.TotalUsageSeconds += 60; _unsavedUsageSeconds += 60;
            if (_unsavedUsageSeconds >= 300) { _unsavedUsageSeconds = 0; _stateStore.Save(); }
            if (AccountProfilePanel.Visibility == Visibility.Visible) RefreshAccountStatistics();
        };
        _accountUsageTimer.Start();
    }
    private void StopAccountUsageTracking() => _accountUsageTimer.Stop();
    private void RefreshAccountStatistics()
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, _state.TotalUsageSeconds));
        AccountStatTime.Text = span.TotalHours >= 1
            ? $"{(int)span.TotalHours} {L("ч", "h", "год")} {span.Minutes} {L("мин", "min", "хв")}"
            : $"{Math.Max(0, span.Minutes)} {L("мин", "min", "хв")}";
        AccountStatVisits.Text = _state.History.Count.ToString();
        AccountStatSite.Text = _state.History.Select(item => Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) ? uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase) : "").Where(host => host.Length > 0).GroupBy(host => host, StringComparer.OrdinalIgnoreCase).OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault() ?? "—";
    }

    private void AccountNewTab_Click(object sender, RoutedEventArgs e)
    {
        OpenNewHomeTab();
    }

    private async Task RunAccountBusyAsync(Func<Task> action)
    {
        _accountBusy = true;
        Cursor = WpfCursors.Wait;
        AccountSubmitButton.Content = "Подождите…";
        AccountSubmitButton.IsEnabled = AccountForgotPasswordButton.IsEnabled = AccountLoginModeButton.IsEnabled = AccountRegisterModeButton.IsEnabled = false;
        HideAccountStatus();
        try { await action(); }
        catch (AuthException ex) { ShowAccountStatus(ex.Message); }
        catch (Exception ex) { App.Log(ex); ShowAccountStatus("Не удалось выполнить запрос. Проверьте интернет и попробуйте ещё раз."); }
        finally
        {
            _accountBusy = false; Cursor = WpfCursors.Arrow;
            AccountSubmitButton.IsEnabled = AccountForgotPasswordButton.IsEnabled = AccountLoginModeButton.IsEnabled = AccountRegisterModeButton.IsEnabled = true;
            AccountSubmitButton.Content = _accountRegisterMode ? "Создать аккаунт" : "Войти в Luma";
        }
    }

    private void ShowAccountStatus(string text, bool success = false)
    {
        AccountStatusText.Text = text;
        AccountStatusText.Foreground = new SolidColorBrush(success ? WpfColor.FromRgb(169, 229, 190) : WpfColor.FromRgb(255, 176, 168));
        AccountStatusCard.Background = new SolidColorBrush(success ? WpfColor.FromRgb(22, 49, 37) : WpfColor.FromRgb(54, 29, 38));
        AccountStatusCard.BorderBrush = new SolidColorBrush(success ? WpfColor.FromRgb(47, 101, 72) : WpfColor.FromRgb(111, 54, 70));
        AccountStatusCard.Visibility = Visibility.Visible;
        AccountStatusCard.Opacity = 0;
        AccountStatusTranslate.Y = -6;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        AccountStatusCard.BeginAnimation(OpacityProperty, new WpfDoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        AccountStatusTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new WpfDoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }

    private void HideAccountStatus()
    {
        AccountStatusCard.BeginAnimation(OpacityProperty, null);
        AccountStatusCard.Visibility = Visibility.Collapsed;
    }

    private void AccountField_FocusChanged(object sender, WpfKeyboardFocusChangedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WpfBorder shell }) return;
        var focused = e.RoutedEvent == System.Windows.Input.Keyboard.GotKeyboardFocusEvent;
        var from = focused ? WpfColor.FromRgb(58, 48, 69) : WpfColor.FromRgb(137, 111, 238);
        var to = focused ? WpfColor.FromRgb(137, 111, 238) : WpfColor.FromRgb(58, 48, 69);
        var brush = new SolidColorBrush(from); shell.BorderBrush = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(from, to, TimeSpan.FromMilliseconds(180)));
        shell.Background = new SolidColorBrush(focused ? WpfColor.FromRgb(24, 19, 31) : WpfColor.FromRgb(16, 13, 21));
        if (shell.RenderTransform is WpfScaleTransform scale)
        {
            var target = focused ? 1.008 : 1;
            scale.BeginAnimation(WpfScaleTransform.ScaleXProperty, new WpfDoubleAnimation(target, TimeSpan.FromMilliseconds(170)));
            scale.BeginAnimation(WpfScaleTransform.ScaleYProperty, new WpfDoubleAnimation(target, TimeSpan.FromMilliseconds(170)));
        }
    }

    private void AccountField_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != WpfKey.Enter) return;
        e.Handled = true;
        AccountSubmit_Click(sender, new RoutedEventArgs());
    }
}
