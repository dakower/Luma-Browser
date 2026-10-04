using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Luma.Setup;

public partial class SetupWindow : Window
{
    private static string ProductVersion => Assembly.GetExecutingAssembly().GetName().Version is { } version
        ? $"{version.Major}.{version.Minor}.{version.Build}"
        : "2.1.5";
    private static readonly string DefaultUserDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Luma");
    private static readonly string DefaultMachineDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Luma");
    private FrameworkElement _current = null!;
    private double _progress;
    private string _selectedTheme = "purple";
    private bool _installing;
    private bool _suppressPathEvents;
    private InstallConfig? _activeConfig;
    private readonly CubicEase _ease = new() { EasingMode = EasingMode.EaseInOut };

    private sealed class InstallConfig
    {
        public string InstallDir { get; set; } = DefaultUserDir;
        public bool AllUsers { get; set; }
        public bool DesktopShortcut { get; set; } = true;
        public bool StartMenuShortcut { get; set; } = true;
        public bool AutoStart { get; set; }
        public bool OfferDefaultBrowser { get; set; } = true;
        public string Theme { get; set; } = "purple";
        public string Language { get; set; } = "en";
    }

    private sealed record InstallProgress(double Value, string Status, string Detail, string Right);

    // ---- Localization: English by default; Русский / Українська selectable on the options screen ----
    private static string _lang = "en";
    private static readonly Dictionary<string, (string Ru, string Uk)> Loc = new()
    {
        ["The internet,\nbuilt around you."] = ("Интернет,\nсобранный вокруг вас.", "Інтернет,\nзібраний навколо вас."),
        ["Install Luma — a fast browser with spaces,\nvertical tabs and a calm interface."] = ("Установите Luma — быстрый браузер с пространствами,\nвертикальными вкладками и спокойным интерфейсом.", "Встановіть Luma — швидкий браузер із просторами,\nвертикальними вкладками та спокійним інтерфейсом."),
        ["Start installation"] = ("Начать установку", "Почати встановлення"),
        ["Options"] = ("Параметры", "Параметри"),
        ["By continuing, you accept the terms of use"] = ("Продолжая, вы принимаете условия использования", "Продовжуючи, ви приймаєте умови використання"),
        ["How would you like to install?"] = ("Как установить?", "Як встановити?"),
        ["Choose access and install location."] = ("Выберите доступ и место установки.", "Виберіть доступ і місце встановлення."),
        ["Language"] = ("Язык", "Мова"),
        ["Applies to the installer and the browser."] = ("Применяется к установщику и браузеру.", "Застосовується до встановлювача та браузера."),
        ["Just for me"] = ("Только для меня", "Лише для мене"),
        ["No administrator rights required"] = ("Без запроса прав администратора", "Без запиту прав адміністратора"),
        ["For all users"] = ("Для всех пользователей", "Для всіх користувачів"),
        ["Installs to Program Files, Windows will ask for confirmation"] = ("Установка в Program Files с подтверждением Windows", "Встановлення в Program Files з підтвердженням Windows"),
        ["Install folder"] = ("Папка установки", "Папка встановлення"),
        ["Browse…"] = ("Обзор…", "Огляд…"),
        ["Additional"] = ("Дополнительно", "Додатково"),
        ["Desktop shortcut"] = ("Ярлык на рабочем столе", "Ярлик на робочому столі"),
        ["Start menu shortcut"] = ("Ярлык в меню «Пуск»", "Ярлик у меню «Пуск»"),
        ["Launch with Windows"] = ("Запускать вместе с Windows", "Запускати разом із Windows"),
        ["Offer as default browser"] = ("Предложить браузер по умолчанию", "Запропонувати браузер за замовчуванням"),
        ["Reset to default path"] = ("Вернуть стандартный путь", "Повернути стандартний шлях"),
        ["Back"] = ("Назад", "Назад"),
        ["Continue"] = ("Продолжить", "Продовжити"),
        ["Choose your mood"] = ("Выберите настроение", "Оберіть настрій"),
        ["Your first space will be yours."] = ("Первое пространство уже будет вашим.", "Перший простір уже буде вашим."),
        ["Install"] = ("Установить", "Встановити"),
        ["Building your browser"] = ("Собираем ваш браузер", "Збираємо ваш браузер"),
        ["Preparing your space…"] = ("Подготавливаем пространство…", "Готуємо простір…"),
        ["Verifying the install package"] = ("Проверяем установочный пакет", "Перевіряємо пакет встановлення"),
        ["Installing Luma"] = ("Установка Luma", "Встановлення Luma"),
        ["Preparing"] = ("Подготовка", "Підготовка"),
        ["Luma is ready"] = ("Luma готов", "Luma готова"),
        ["Browser installed"] = ("Браузер установлен", "Браузер встановлено"),
        ["Open browser"] = ("Открыть браузер", "Відкрити браузер"),
        ["✓ Integrity verified"] = ("✓ Целостность проверена", "✓ Цілісність перевірено"),
        ["✓ Can be removed via Windows"] = ("✓ Можно удалить через Windows", "✓ Можна видалити через Windows"),
        ["Choose the Luma install folder"] = ("Выберите папку установки Luma", "Виберіть папку встановлення Luma"),
        ["Available to all users · administrator rights required"] = ("Доступно всем пользователям · потребуются права администратора", "Доступно всім користувачам · потрібні права адміністратора"),
        ["Available only to your Windows profile"] = ("Доступно только вашему профилю Windows", "Доступно лише вашому профілю Windows"),
        ["Free {0}"] = ("Свободно {0}", "Вільно {0}"),
        ["Invalid path"] = ("Некорректный путь", "Некоректний шлях"),
        ["Enter the full install path."] = ("Укажите полный путь установки.", "Вкажіть повний шлях встановлення."),
        ["Luma can't be installed directly in the drive root."] = ("Нельзя устанавливать Luma прямо в корень диска.", "Не можна встановлювати Luma прямо в корінь диска."),
        ["Not enough free space on the selected drive."] = ("На выбранном диске недостаточно свободного места.", "На вибраному диску недостатньо вільного місця."),
        ["The selected folder is not empty and is not a Luma installation."] = ("Выбранная папка не пуста и не является установкой Luma.", "Вибрана папка не порожня і не є встановленням Luma."),
        ["Couldn't use the selected folder: "] = ("Не удалось использовать выбранную папку: ", "Не вдалося використати вибрану папку: "),
        ["Couldn't determine the installer path."] = ("Не удалось определить путь установщика.", "Не вдалося визначити шлях встановлювача."),
        ["Installation for all users was cancelled."] = ("Установка для всех пользователей отменена.", "Встановлення для всіх користувачів скасовано."),
        ["Install settings are corrupted."] = ("Параметры установки повреждены.", "Параметри встановлення пошкоджено."),
        ["Couldn't continue the installation:\n\n"] = ("Не удалось продолжить установку:\n\n", "Не вдалося продовжити встановлення:\n\n"),
        ["Installation complete"] = ("Установка завершена", "Встановлення завершено"),
        ["All components verified"] = ("Все компоненты проверены", "Усі компоненти перевірено"),
        ["Done"] = ("Готово", "Готово"),
        ["Luma is installed in\n{0}"] = ("Luma установлена в\n{0}", "Luma встановлено в\n{0}"),
        ["Couldn't install Luma:\n\n"] = ("Не удалось установить Luma:\n\n", "Не вдалося встановити Luma:\n\n"),
        ["\n\nThe previous version was restored if it was installed."] = ("\n\nПредыдущая версия восстановлена, если она была установлена.", "\n\nПопередню версію відновлено, якщо вона була встановлена."),
        ["Invalid install folder."] = ("Некорректная папка установки.", "Некоректна папка встановлення."),
        ["Verifying package…"] = ("Проверяем пакет…", "Перевіряємо пакет…"),
        ["SHA-256 checksum"] = ("Контрольная сумма SHA-256", "Контрольна сума SHA-256"),
        ["Verifying"] = ("Проверка", "Перевірка"),
        ["Preparing files…"] = ("Подготавливаем файлы…", "Готуємо файли…"),
        ["Creating a safe temporary folder"] = ("Создаём безопасную временную папку", "Створюємо безпечну тимчасову папку"),
        ["Extracting Luma…"] = ("Распаковываем Luma…", "Розпаковуємо Luma…"),
        ["The install package is missing Luma.exe."] = ("В установочном пакете отсутствует Luma.exe.", "У пакеті встановлення відсутній Luma.exe."),
        ["Updating the app…"] = ("Обновляем приложение…", "Оновлюємо застосунок…"),
        ["Closing the installed Luma"] = ("Закрываем установленную Luma", "Закриваємо встановлену Luma"),
        ["Updating"] = ("Обновление", "Оновлення"),
        ["Configuring Windows…"] = ("Настраиваем Windows…", "Налаштовуємо Windows…"),
        ["Shortcuts, protocols and uninstall entry"] = ("Ярлыки, протоколы и удаление", "Ярлики, протоколи та видалення"),
        ["Setup"] = ("Настройка", "Налаштування"),
        ["Finishing…"] = ("Завершаем…", "Завершуємо…"),
        ["Removing temporary files"] = ("Удаляем временные файлы", "Видаляємо тимчасові файли"),
        ["Cleanup"] = ("Очистка", "Очищення"),
        ["WebView2 Runtime found"] = ("WebView2 Runtime найден", "WebView2 Runtime знайдено"),
        ["Using the already installed compatible component"] = ("Используем уже установленный совместимый компонент", "Використовуємо вже встановлений сумісний компонент"),
        ["The install package is missing the WebView2 Runtime repair tool."] = ("В установочном пакете отсутствует средство восстановления WebView2 Runtime.", "У пакеті встановлення відсутній засіб відновлення WebView2 Runtime."),
        ["Repairing WebView2 Runtime…"] = ("Восстанавливаем WebView2 Runtime…", "Відновлюємо WebView2 Runtime…"),
        ["Installing the system web component"] = ("Установка системного веб-компонента", "Встановлення системного веб-компонента"),
        ["WebView2 Runtime is ready"] = ("WebView2 Runtime готов", "WebView2 Runtime готовий"),
        ["The browser component was installed or repaired"] = ("Компонент браузера установлен или восстановлен", "Компонент браузера встановлено або відновлено"),
        ["Windows confirmation required…"] = ("Требуется подтверждение Windows…", "Потрібне підтвердження Windows…"),
        ["Retrying the WebView2 repair with administrator rights"] = ("Повторяем восстановление WebView2 с правами администратора", "Повторюємо відновлення WebView2 з правами адміністратора"),
        ["Windows did not register a compatible WebView2 Runtime after repair. Installer codes: {0}"] = ("Windows не зарегистрировала совместимый WebView2 Runtime после восстановления. Коды установщика: {0}", "Windows не зареєструвала сумісний WebView2 Runtime після відновлення. Коди встановлювача: {0}"),
        [". Restart Windows and run the installation again."] = (". Перезагрузите Windows и повторите установку.", ". Перезавантажте Windows і повторіть встановлення."),
        ["Couldn't start the WebView2 Runtime installation."] = ("Не удалось запустить установку WebView2 Runtime.", "Не вдалося запустити встановлення WebView2 Runtime."),
        ["Embedded package not found."] = ("Встроенный пакет не найден.", "Вбудований пакет не знайдено."),
        ["Package checksum not found."] = ("Контрольная сумма пакета не найдена.", "Контрольну суму пакета не знайдено."),
        ["The install package is corrupted: SHA-256 mismatch."] = ("Установочный пакет повреждён: SHA-256 не совпадает.", "Пакет встановлення пошкоджено: SHA-256 не збігається."),
        ["The package contains an unsafe path."] = ("Пакет содержит небезопасный путь.", "Пакет містить небезпечний шлях."),
        ["Installer executable not found."] = ("Не найден исполняемый файл установщика.", "Не знайдено виконуваний файл встановлювача."),
        ["Fast vertical browser"] = ("Быстрый вертикальный браузер", "Швидкий вертикальний браузер"),
        ["Windows Script Host is unavailable."] = ("Windows Script Host недоступен.", "Windows Script Host недоступний."),
        ["GB"] = ("ГБ", "ГБ"),
        ["MB"] = ("МБ", "МБ"),
        ["Uninstall Luma Browser?\n\nYes — remove the browser and local profile.\nNo — remove the browser but keep the profile.\nCancel — change nothing."] = ("Удалить Luma Browser?\n\nДа — удалить браузер и локальный профиль.\nНет — удалить браузер, но сохранить профиль.\nОтмена — ничего не менять.", "Видалити Luma Browser?\n\nТак — видалити браузер і локальний профіль.\nНі — видалити браузер, але зберегти профіль.\nСкасувати — нічого не змінювати."),
        ["Uninstall Luma"] = ("Удаление Luma", "Видалення Luma"),
        ["Removing Luma…"] = ("Удаляем Luma…", "Видаляємо Luma…"),
        ["Closing the browser"] = ("Закрываем браузер", "Закриваємо браузер"),
        ["Uninstalling"] = ("Удаление", "Видалення"),
        ["Cleaning up files and shortcuts"] = ("Очищаем файлы и ярлыки", "Очищуємо файли та ярлики"),
        ["Please wait for the installation to finish."] = ("Дождитесь завершения установки.", "Дочекайтеся завершення встановлення."),
    };

    internal static string T(string en) => _lang == "en" || !Loc.TryGetValue(en, out var v) ? en : _lang == "ru" ? v.Ru : v.Uk;

    private static string NormalizeLang(string? value) => value is "ru" or "uk" ? value : "en";

    private static string ReadSavedLanguage()
    {
        try { return NormalizeLang(Registry.CurrentUser.OpenSubKey(@"Software\Luma")?.GetValue("Language") as string); }
        catch { return "en"; }
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject, string> _sourceText = new();

    private void ApplyLanguage()
    {
        TranslateTree(this);
        UpdateLocationMeta();
    }

    private void TranslateTree(DependencyObject node)
    {
        if (node is TextBlock block && !string.IsNullOrEmpty(block.Text))
        {
            if (!_sourceText.TryGetValue(block, out var src) && Loc.ContainsKey(block.Text)) { src = block.Text; _sourceText.Add(block, src); }
            if (src is not null) block.Text = T(src);
        }
        else if (node is ContentControl { Content: string content } control)
        {
            if (!_sourceText.TryGetValue(control, out var src) && Loc.ContainsKey(content)) { src = content; _sourceText.Add(control, src); }
            if (src is not null) control.Content = T(src);
        }
        foreach (var child in LogicalTreeHelper.GetChildren(node))
            if (child is DependencyObject d) TranslateTree(d);
    }

    private void Language_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not RadioButton { Tag: string tag }) return;
        _lang = NormalizeLang(tag);
        ApplyLanguage();
    }

    public SetupWindow()
    {
        InitializeComponent();
        _current = WelcomeScreen;
        SetInstallPath(DefaultUserDir);
        Loaded += (_, _) => StartAmbientMotion();
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.GetPosition(this).Y <= 62 && e.ButtonState == MouseButtonState.Pressed) DragMove();
        };

        var elevatedConfig = ArgumentValue("--elevated-config");
        var uninstallDir = ArgumentValue("--dir");
        if (!string.IsNullOrWhiteSpace(elevatedConfig))
            Loaded += async (_, _) => await ContinueElevatedInstallAsync(elevatedConfig);
        else if (Environment.GetCommandLineArgs().Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
            Loaded += async (_, _) => await RunUninstallAsync(uninstallDir ?? DefaultUserDir, string.Equals(ArgumentValue("--scope"), "machine", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ArgumentValue(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++) if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private void StartAmbientMotion()
    {
        AnimateGlow(GlowPurple, 5.2, -290, 105, -55, 365, .94, 1.14, .62, .78);
        AnimateGlow(GlowMint, 6.0, 275, -125, 70, -355, 1.12, .92, .58, .74);
    }

    private static void AnimateGlow(System.Windows.Shapes.Ellipse glow, double seconds, double fromX, double toX, double fromY, double toY, double fromScale, double toScale, double fromOpacity, double toOpacity)
    {
        var move = new TranslateTransform(); var scale = new ScaleTransform(fromScale, fromScale);
        glow.RenderTransformOrigin = new Point(.5, .5); glow.RenderTransform = new TransformGroup { Children = new TransformCollection { scale, move } };
        var duration = TimeSpan.FromSeconds(seconds); var easing = new SineEase { EasingMode = EasingMode.EaseInOut };
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, toX, duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = easing });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, toY, TimeSpan.FromSeconds(seconds * 1.17)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = easing, BeginTime = TimeSpan.FromMilliseconds(180) });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(fromScale, toScale, duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(fromScale, toScale, TimeSpan.FromSeconds(seconds * 1.08)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = easing });
        glow.BeginAnimation(OpacityProperty, new DoubleAnimation(fromOpacity, toOpacity, TimeSpan.FromSeconds(seconds * .91)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = easing });
    }

    private async void Start_Click(object sender, RoutedEventArgs e) => await GoTo(OptionsScreen, false);
    private async void Parameters_Click(object sender, RoutedEventArgs e) => await GoTo(OptionsScreen, false);
    private async void OptionsBack_Click(object sender, RoutedEventArgs e) => await GoTo(WelcomeScreen, true);
    private async void ThemeBack_Click(object sender, RoutedEventArgs e) => await GoTo(OptionsScreen, true);

    private async void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInstallPath(out var error)) { MessageBox.Show(error, "Luma Setup", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        await GoTo(ThemeScreen, false);
    }

    private async Task GoTo(FrameworkElement next, bool backwards)
    {
        if (_current == next) return;
        var direction = backwards ? 1d : -1d;
        var outMove = new TranslateTransform(); var outScale = new ScaleTransform(1, 1);
        _current.RenderTransformOrigin = new Point(.5, .5); _current.RenderTransform = new TransformGroup { Children = new TransformCollection { outScale, outMove } };
        var duration = TimeSpan.FromMilliseconds(190);
        _current.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration) { EasingFunction = _ease });
        outMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 20 * direction, duration) { EasingFunction = _ease });
        outScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, .98, duration) { EasingFunction = _ease });
        outScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, .98, duration) { EasingFunction = _ease });
        await Task.Delay(duration);
        _current.Visibility = Visibility.Collapsed; next.Visibility = Visibility.Visible; next.Opacity = 0;
        var inMove = new TranslateTransform(-20 * direction, 0); var inScale = new ScaleTransform(.98, .98);
        next.RenderTransformOrigin = new Point(.5, .5); next.RenderTransform = new TransformGroup { Children = new TransformCollection { inScale, inMove } };
        var enter = TimeSpan.FromMilliseconds(400);
        next.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, enter) { EasingFunction = _ease });
        inMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-20 * direction, 0, enter) { EasingFunction = _ease });
        inScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.98, 1, enter) { EasingFunction = _ease });
        inScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.98, 1, enter) { EasingFunction = _ease });
        _current = next; await Task.Delay(enter);
    }

    private void InstallMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || InstallPathBox is null || ForEveryone is null) return;
        SetInstallPath(ForEveryone.IsChecked == true ? DefaultMachineDir : DefaultUserDir);
    }

    private void SetInstallPath(string path)
    {
        _suppressPathEvents = true; InstallPathBox.Text = path; _suppressPathEvents = false; UpdateLocationMeta();
    }

    private void ResetInstallPath_Click(object sender, RoutedEventArgs e) => SetInstallPath(ForEveryone.IsChecked == true ? DefaultMachineDir : DefaultUserDir);

    private void BrowseInstallPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = T("Choose the Luma install folder"), InitialDirectory = Directory.Exists(InstallPathBox.Text) ? InstallPathBox.Text : Path.GetDirectoryName(InstallPathBox.Text) };
        if (dialog.ShowDialog(this) == true) SetInstallPath(Path.Combine(dialog.FolderName, "Luma"));
    }

    private void InstallPathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressPathEvents && IsLoaded) UpdateLocationMeta();
    }

    private void UpdateLocationMeta()
    {
        if (InstallScopeText is null || FreeSpaceText is null || OptionsContinueButton is null) return;
        var all = ForEveryone?.IsChecked == true;
        InstallScopeText.Text = all ? T("Available to all users · administrator rights required") : T("Available only to your Windows profile");
        try
        {
            var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(InstallPathBox.Text.Trim()));
            var root = Path.GetPathRoot(full);
            var drive = string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root);
            FreeSpaceText.Text = drive is null ? "" : string.Format(T("Free {0}"), FormatBytes(drive.AvailableFreeSpace));
            OptionsContinueButton.IsEnabled = Path.IsPathFullyQualified(full);
        }
        catch { FreeSpaceText.Text = T("Invalid path"); OptionsContinueButton.IsEnabled = false; }
    }

    private bool ValidateInstallPath(out string error)
    {
        error = "";
        try
        {
            var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(InstallPathBox.Text.Trim()));
            if (!Path.IsPathFullyQualified(path)) { error = T("Enter the full install path."); return false; }
            if (string.Equals(path, Path.GetPathRoot(path), StringComparison.OrdinalIgnoreCase)) { error = T("Luma can't be installed directly in the drive root."); return false; }
            var root = Path.GetPathRoot(path)!; var drive = new DriveInfo(root);
            if (drive.AvailableFreeSpace < 350L * 1024 * 1024) { error = T("Not enough free space on the selected drive."); return false; }
            var existing = new DirectoryInfo(path);
            if (existing.Exists && existing.EnumerateFileSystemInfos().Any() && !File.Exists(Path.Combine(path, "Luma.exe"))) { error = T("The selected folder is not empty and is not a Luma installation."); return false; }
            return true;
        }
        catch (Exception ex) { error = T("Couldn't use the selected folder: ") + ex.Message; return false; }
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || ProgressArc is null || LinearFill is null || DoneIcon is null || sender is not RadioButton radio || radio.Tag is not string theme) return;
        _selectedTheme = theme;
        var palette = theme switch
        {
            "sand" => (Bg: "#F7F1EC", Top: "#E9DED5", Text: "#332A25", Secondary: "#796D65", Weak: "#9A8D84", Card: "#FFFDFC", Border: "#DDD0C7", Button: "#312923", A: "#B9896D", B: "#D8AE91"),
            "mint" => (Bg: "#F0F7F5", Top: "#DDEAE7", Text: "#21312F", Secondary: "#627873", Weak: "#879B96", Card: "#FBFEFD", Border: "#CFDEDA", Button: "#20322F", A: "#319A8E", B: "#68C9B8"),
            "graphite" => (Bg: "#201D25", Top: "#2A2630", Text: "#F6F2F8", Secondary: "#B6AEBB", Weak: "#8D8591", Card: "#2D2933", Border: "#45404B", Button: "#0F0E12", A: "#7D68D8", B: "#B86BC1"),
            _ => (Bg: "#F8F6FA", Top: "#E9E6EC", Text: "#2B2730", Secondary: "#79747F", Weak: "#99949E", Card: "#FFFFFF", Border: "#DDD8E2", Button: "#28232D", A: "#7957F2", B: "#D65BC4")
        };
        Resources["InstallerBackgroundBrush"] = Brush(palette.Bg); Resources["TopBarBrush"] = Brush(palette.Top); Resources["PrimaryTextBrush"] = Brush(palette.Text); Resources["SecondaryTextBrush"] = Brush(palette.Secondary); Resources["WeakTextBrush"] = Brush(palette.Weak); Resources["CardBrush"] = Brush(palette.Card); Resources["BorderLineBrush"] = Brush(palette.Border); Resources["PrimaryButtonBrush"] = Brush(palette.Button); Resources["AccentBrush"] = Brush(palette.A);
        var gradient = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(palette.A), (Color)ColorConverter.ConvertFromString(palette.B), 45);
        ProgressArc.Stroke = gradient; LinearFill.Background = gradient; DoneIcon.Background = gradient;
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private InstallConfig ReadConfig() => new()
    {
        InstallDir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(InstallPathBox.Text.Trim())),
        AllUsers = ForEveryone.IsChecked == true,
        DesktopShortcut = DesktopShortcutCheck.IsChecked == true,
        StartMenuShortcut = StartMenuShortcutCheck.IsChecked == true,
        AutoStart = AutoStartCheck.IsChecked == true,
        OfferDefaultBrowser = DefaultBrowserCheck.IsChecked == true,
        Theme = _selectedTheme,
        Language = _lang
    };

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInstallPath(out var error)) { MessageBox.Show(error, "Luma Setup", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var config = ReadConfig();
        if (config.AllUsers && !IsAdministrator()) { Elevate(config); return; }
        await BeginInstallAsync(config);
    }

    private void Elevate(InstallConfig config)
    {
        var file = Path.Combine(Path.GetTempPath(), $"luma-install-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(config));
        var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException(T("Couldn't determine the installer path."));
        var info = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" };
        info.ArgumentList.Add("--elevated-config"); info.ArgumentList.Add(file);
        try { Process.Start(info); Close(); }
        catch { try { File.Delete(file); } catch { } MessageBox.Show(T("Installation for all users was cancelled."), "Luma Setup", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private async Task ContinueElevatedInstallAsync(string configPath)
    {
        try
        {
            var config = JsonSerializer.Deserialize<InstallConfig>(await File.ReadAllTextAsync(configPath)) ?? throw new InvalidDataException(T("Install settings are corrupted."));
            try { File.Delete(configPath); } catch { }
            _lang = NormalizeLang(config.Language); ApplyLanguage();
            _selectedTheme = config.Theme; Theme_Checked(new RadioButton { Tag = config.Theme }, new RoutedEventArgs());
            WelcomeScreen.Visibility = Visibility.Collapsed; ProgressScreen.Visibility = Visibility.Visible; _current = ProgressScreen;
            await BeginInstallAsync(config, false);
        }
        catch (Exception ex) { MessageBox.Show(T("Couldn't continue the installation:\n\n") + ex.Message, "Luma Setup", MessageBoxButton.OK, MessageBoxImage.Error); Close(); }
    }

    private async Task BeginInstallAsync(InstallConfig config, bool animateTransition = true)
    {
        if (_installing) return;
        _installing = true; _activeConfig = config; _progress = 0; SetProgress(0); PulseGlow();
        if (animateTransition) await GoTo(ProgressScreen, false);
        var reporter = new Progress<InstallProgress>(p => { SetProgress(p.Value); SetStatus(p.Status, p.Detail, p.Right); });
        try
        {
            var work = Task.Run(() => InstallFiles(config, reporter));
            await Task.WhenAll(work, Task.Delay(1100));
            SetProgress(100); SetStatus(T("Installation complete"), T("All components verified"), T("Done"));
            DoneLocationText.Text = string.Format(T("Luma is installed in\n{0}"), config.InstallDir);
            await GoTo(DoneScreen, false); AnimateDone();
        }
        catch (Exception ex)
        {
            MessageBox.Show(T("Couldn't install Luma:\n\n") + ex.Message + T("\n\nThe previous version was restored if it was installed."), "Luma Setup", MessageBoxButton.OK, MessageBoxImage.Error);
            await GoTo(ThemeScreen, true);
        }
        finally { _installing = false; }
    }

    private void SetStatus(string status, string detail, string right)
    {
        ProgressStatus.Text = status; ProgressDetail.Text = detail; ProgressRightText.Text = right;
    }

    private void SetProgress(double value)
    {
        _progress = Math.Clamp(value, 0, 100); PercentText.Text = $"{Math.Round(_progress):0}%"; LinearFill.Width = 390 * _progress / 100;
        var center = new Point(58, 58); const double radius = 53; var angle = _progress / 100 * 359.999; var start = new Point(center.X, center.Y - radius); var radians = (angle - 90) * Math.PI / 180; var end = new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        var figure = new PathFigure { StartPoint = start, IsClosed = false }; figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, angle > 180, SweepDirection.Clockwise, true)); ProgressArc.Data = new PathGeometry(new[] { figure });
    }

    private void PulseGlow() => RingGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, new DoubleAnimation(.16, .34, TimeSpan.FromMilliseconds(1500)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = _ease });

    private void AnimateDone()
    {
        DoneScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.85, 1, TimeSpan.FromMilliseconds(430)) { EasingFunction = _ease }); DoneScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.85, 1, TimeSpan.FromMilliseconds(430)) { EasingFunction = _ease }); DoneCheck.Opacity = 0; DoneCheck.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)) { BeginTime = TimeSpan.FromMilliseconds(180), EasingFunction = _ease });
    }

    private void InstallFiles(InstallConfig config, IProgress<InstallProgress> progress)
    {
        var installDir = Path.GetFullPath(config.InstallDir); var parent = Directory.GetParent(installDir)?.FullName ?? throw new InvalidOperationException(T("Invalid install folder."));
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".Luma-staging-{Guid.NewGuid():N}"); var backup = Path.Combine(parent, $".Luma-backup-{Guid.NewGuid():N}"); var payloadFile = Path.Combine(Path.GetTempPath(), $"LumaPayload-{Guid.NewGuid():N}.zip");
        var movedOld = false; var movedNew = false;
        try
        {
            progress.Report(new(4, T("Verifying package…"), T("SHA-256 checksum"), T("Verifying")));
            ExtractEmbeddedPayload(payloadFile);
            VerifyPayload(payloadFile);
            progress.Report(new(10, T("Preparing files…"), T("Creating a safe temporary folder"), T("Preparing")));
            Directory.CreateDirectory(staging);
            using (var archive = ZipFile.OpenRead(payloadFile))
            {
                var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();
                for (var i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i]; var target = SafeDestination(staging, entry.FullName); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var input = entry.Open(); using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None); input.CopyTo(output);
                    progress.Report(new(12 + 58d * (i + 1) / Math.Max(1, entries.Length), T("Extracting Luma…"), entry.Name, $"{i + 1}/{entries.Length}"));
                }
            }
            if (!File.Exists(Path.Combine(staging, "Luma.exe"))) throw new InvalidDataException(T("The install package is missing Luma.exe."));
            progress.Report(new(73, T("Updating the app…"), T("Closing the installed Luma"), T("Updating")));
            StopInstalledBrowser(installDir);
            if (Directory.Exists(installDir)) { Directory.Move(installDir, backup); movedOld = true; }
            Directory.Move(staging, installDir); movedNew = true;
            InstallWebView2Runtime(installDir, progress);
            progress.Report(new(88, T("Configuring Windows…"), T("Shortcuts, protocols and uninstall entry"), T("Setup")));
            InstallIntegration(config);
            progress.Report(new(96, T("Finishing…"), T("Removing temporary files"), T("Cleanup")));
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
        }
        catch
        {
            try { if (movedNew && Directory.Exists(installDir)) Directory.Delete(installDir, true); } catch { }
            try { if (movedOld && Directory.Exists(backup)) Directory.Move(backup, installDir); } catch { }
            throw;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            try { File.Delete(payloadFile); } catch { }
        }
    }

    private static void InstallWebView2Runtime(string installDir, IProgress<InstallProgress> progress)
    {
        if (InstalledBrowserSeesWebView2(installDir))
        {
            progress.Report(new(84, T("WebView2 Runtime found"), T("Using the already installed compatible component"), T("Done")));
            return;
        }
        var setup = Path.Combine(installDir, "MicrosoftEdgeWebview2Setup.exe");
        if (!File.Exists(setup)) throw new InvalidDataException(T("The install package is missing the WebView2 Runtime repair tool."));
        progress.Report(new(78, T("Repairing WebView2 Runtime…"), T("Installing the system web component"), "WebView2"));
        var firstCode = RunWebView2Setup(setup, false);
        if (WaitForWebView2(installDir))
        {
            progress.Report(new(84, T("WebView2 Runtime is ready"), T("The browser component was installed or repaired"), T("Done")));
            return;
        }
        int? elevatedCode = null;
        if (!IsAdministrator())
        {
            progress.Report(new(81, T("Windows confirmation required…"), T("Retrying the WebView2 repair with administrator rights"), "UAC"));
            elevatedCode = RunWebView2Setup(setup, true);
            if (WaitForWebView2(installDir))
            {
                progress.Report(new(84, T("WebView2 Runtime is ready"), T("The browser component was installed or repaired"), T("Done")));
                return;
            }
        }
        throw new InvalidOperationException(string.Format(T("Windows did not register a compatible WebView2 Runtime after repair. Installer codes: {0}"), firstCode) + (elevatedCode.HasValue ? $", {elevatedCode.Value}" : "") + T(". Restart Windows and run the installation again."));
    }

    private static int RunWebView2Setup(string setup, bool elevated)
    {
        try
        {
            var info = new ProcessStartInfo(setup)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = "/silent /install",
            };
            if (elevated) info.Verb = "runas";
            using var process = Process.Start(info) ?? throw new InvalidOperationException(T("Couldn't start the WebView2 Runtime installation."));
            if (!process.WaitForExit(4 * 60 * 1000))
            {
                try { process.Kill(true); } catch { }
                return -1;
            }
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return 1223; }
    }

    private static bool WaitForWebView2(string installDir)
    {
        for (var attempt = 0; attempt < 24; attempt++)
        {
            if (InstalledBrowserSeesWebView2(installDir)) return true;
            Thread.Sleep(500);
        }
        return false;
    }

    private static bool InstalledBrowserSeesWebView2(string installDir)
    {
        try
        {
            var browser = Path.Combine(installDir, "Luma.exe");
            if (!File.Exists(browser)) return false;
            using var process = Process.Start(new ProcessStartInfo(browser)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = "--check-webview2-runtime",
            });
            if (process is null || !process.WaitForExit(20_000)) { try { process?.Kill(true); } catch { } return false; }
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void ExtractEmbeddedPayload(string destination)
    {
        var assembly = Assembly.GetExecutingAssembly(); var name = assembly.GetManifestResourceNames().Single(x => x.EndsWith("Payload.zip", StringComparison.OrdinalIgnoreCase));
        using var source = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException(T("Embedded package not found.")); using var output = File.Create(destination); source.CopyTo(output);
    }

    private static void VerifyPayload(string payload)
    {
        var assembly = Assembly.GetExecutingAssembly(); var name = assembly.GetManifestResourceNames().Single(x => x.EndsWith("Payload.sha256", StringComparison.OrdinalIgnoreCase));
        using var expectedStream = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException(T("Package checksum not found.")); using var reader = new StreamReader(expectedStream); var expected = reader.ReadToEnd().Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        using var stream = File.OpenRead(payload); var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(T("The install package is corrupted: SHA-256 mismatch."));
    }

    private static string SafeDestination(string root, string relative)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; var target = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!target.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(T("The package contains an unsafe path.")); return target;
    }

    private static void StopInstalledBrowser(string installDir)
    {
        var target = Path.GetFullPath(Path.Combine(installDir, "Luma.exe"));
        foreach (var process in Process.GetProcessesByName("Luma"))
        {
            try
            {
                var file = process.MainModule?.FileName; if (string.IsNullOrWhiteSpace(file) || !Path.GetFullPath(file).Equals(target, StringComparison.OrdinalIgnoreCase)) continue;
                if (process.CloseMainWindow() && process.WaitForExit(6000)) continue; process.Kill(true); process.WaitForExit(4000);
            }
            catch { }
        }
    }

    private void InstallIntegration(InstallConfig config)
    {
        var exe = Path.Combine(config.InstallDir, "Luma.exe");
        if (config.DesktopShortcut) CreateShortcut(DesktopShortcutPath(config), exe, config.InstallDir); else DeleteFile(DesktopShortcutPath(config));
        var startFolder = StartMenuFolder(config); if (config.StartMenuShortcut) { Directory.CreateDirectory(startFolder); CreateShortcut(Path.Combine(startFolder, "Luma.lnk"), exe, config.InstallDir); } else try { if (Directory.Exists(startFolder)) Directory.Delete(startFolder, true); } catch { }
        using (var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if (config.AutoStart) run!.SetValue("Luma", $"\"{exe}\" --background"); else run!.DeleteValue("Luma", false); }
        Registry.CurrentUser.CreateSubKey(@"Software\Luma")!.SetValue("Theme", config.Theme); Registry.CurrentUser.CreateSubKey(@"Software\Luma")!.SetValue("Language", NormalizeLang(config.Language));
        RegisterBrowser(config, exe);
        var setupExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException(T("Installer executable not found."));
        var uninstaller = Path.Combine(config.InstallDir, "Uninstall.exe"); File.Copy(setupExe, uninstaller, true);
        RegisterUninstaller(config, exe, uninstaller);
    }

    private static void RegisterBrowser(InstallConfig config, string exe)
    {
        using var root = RegistryKey.OpenBaseKey(config.AllUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);
        using var classes = root.CreateSubKey(@"Software\Classes");
        using (var prog = classes.CreateSubKey("LumaURL")) { prog.SetValue("", "Luma URL"); prog.SetValue("URL Protocol", ""); prog.DefaultIcon().SetValue("", $"\"{exe}\",0"); prog.CreateSubKey(@"shell\open\command")!.SetValue("", $"\"{exe}\" \"%1\""); }
        using var client = root.CreateSubKey(@"Software\Clients\StartMenuInternet\Luma"); client.SetValue("", "Luma Browser"); client.CreateSubKey(@"shell\open\command")!.SetValue("", $"\"{exe}\"");
        var caps = client.CreateSubKey("Capabilities")!; caps.SetValue("ApplicationName", "Luma Browser"); caps.SetValue("ApplicationDescription", T("Fast vertical browser")); caps.SetValue("ApplicationIcon", $"{exe},0"); var urls = caps.CreateSubKey("URLAssociations")!; urls.SetValue("http", "LumaURL"); urls.SetValue("https", "LumaURL");
        root.CreateSubKey(@"Software\RegisteredApplications")!.SetValue("Luma", @"Software\Clients\StartMenuInternet\Luma\Capabilities");
    }

    private static void RegisterUninstaller(InstallConfig config, string exe, string uninstaller)
    {
        using var root = RegistryKey.OpenBaseKey(config.AllUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = root.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Luma");
        key.SetValue("DisplayName", "Luma Browser"); key.SetValue("DisplayVersion", ProductVersion); key.SetValue("Publisher", "Luma Browser"); key.SetValue("DisplayIcon", $"{exe},0"); key.SetValue("InstallLocation", config.InstallDir); key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord); key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, DirectorySize(config.InstallDir) / 1024), RegistryValueKind.DWord);
        key.SetValue("UninstallString", $"\"{uninstaller}\" --uninstall --scope {(config.AllUsers ? "machine" : "user")} --dir \"{config.InstallDir}\"");
        key.SetValue("QuietUninstallString", $"\"{uninstaller}\" --uninstall --quiet --scope {(config.AllUsers ? "machine" : "user")} --dir \"{config.InstallDir}\"");
    }

    private static void CreateShortcut(string path, string target, string workingDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException(T("Windows Script Host is unavailable.")); dynamic shell = Activator.CreateInstance(type)!; dynamic shortcut = shell.CreateShortcut(path); shortcut.TargetPath = target; shortcut.WorkingDirectory = workingDirectory; shortcut.IconLocation = target + ",0"; shortcut.Save();
    }

    private static string DesktopShortcutPath(InstallConfig c) => Path.Combine(Environment.GetFolderPath(c.AllUsers ? Environment.SpecialFolder.CommonDesktopDirectory : Environment.SpecialFolder.DesktopDirectory), "Luma.lnk");
    private static string StartMenuFolder(InstallConfig c) => Path.Combine(Environment.GetFolderPath(c.AllUsers ? Environment.SpecialFolder.CommonStartMenu : Environment.SpecialFolder.StartMenu), "Programs", "Luma");
    private static void DeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private static long DirectorySize(string path) { try { return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); } catch { return 0; } }
    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1024d / 1024 / 1024:0.0} " + T("GB") : $"{bytes / 1024d / 1024:0} " + T("MB");
    private static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private async Task RunUninstallAsync(string installDir, bool allUsers)
    {
        _lang = ReadSavedLanguage(); ApplyLanguage();
        if (allUsers && !IsAdministrator())
        {
            var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName!; var info = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" }; info.ArgumentList.Add("--uninstall"); info.ArgumentList.Add("--scope"); info.ArgumentList.Add("machine"); info.ArgumentList.Add("--dir"); info.ArgumentList.Add(installDir);
            try { Process.Start(info); } catch { } Close(); return;
        }
        var quiet = Environment.GetCommandLineArgs().Any(a => a.Equals("--quiet", StringComparison.OrdinalIgnoreCase));
        var deleteProfile = false;
        if (!quiet)
        {
            var answer = MessageBox.Show(T("Uninstall Luma Browser?\n\nYes — remove the browser and local profile.\nNo — remove the browser but keep the profile.\nCancel — change nothing."), T("Uninstall Luma"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) { Close(); return; } deleteProfile = answer == MessageBoxResult.Yes;
        }
        WelcomeScreen.Visibility = Visibility.Collapsed; ProgressScreen.Visibility = Visibility.Visible; _current = ProgressScreen; SetProgress(20); SetStatus(T("Removing Luma…"), T("Closing the browser"), T("Uninstalling")); await Task.Delay(250);
        StopInstalledBrowser(installDir); var config = new InstallConfig { InstallDir = installDir, AllUsers = allUsers }; RemoveIntegration(config); SetProgress(75); SetStatus(T("Removing Luma…"), T("Cleaning up files and shortcuts"), T("Cleanup"));
        if (deleteProfile) try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma"), true); } catch { }
        ScheduleDirectoryRemoval(installDir); SetProgress(100); await Task.Delay(300); Close();
    }

    private static void RemoveIntegration(InstallConfig config)
    {
        DeleteFile(DesktopShortcutPath(config)); try { var folder = StartMenuFolder(config); if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
        using (var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) run?.DeleteValue("Luma", false);
        using var root = RegistryKey.OpenBaseKey(config.AllUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);
        try { root.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Luma", false); } catch { }
        try { root.DeleteSubKeyTree(@"Software\Clients\StartMenuInternet\Luma", false); } catch { }
        try { root.CreateSubKey(@"Software\RegisteredApplications")?.DeleteValue("Luma", false); } catch { }
        try { root.DeleteSubKeyTree(@"Software\Classes\LumaURL", false); } catch { }
    }

    private static void ScheduleDirectoryRemoval(string installDir)
    {
        var script = $"Start-Sleep -Seconds 2; Remove-Item -LiteralPath '{installDir.Replace("'", "''")}' -Recurse -Force -ErrorAction SilentlyContinue";
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }; info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-ExecutionPolicy"); info.ArgumentList.Add("Bypass"); info.ArgumentList.Add("-WindowStyle"); info.ArgumentList.Add("Hidden"); info.ArgumentList.Add("-Command"); info.ArgumentList.Add(script); Process.Start(info);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_activeConfig is null) { Close(); return; }
        var launch = new ProcessStartInfo(Path.Combine(_activeConfig.InstallDir, "Luma.exe")) { UseShellExecute = true };
        launch.ArgumentList.Add("--show-welcome");
        Process.Start(launch);
        if (_activeConfig.OfferDefaultBrowser) try { Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=Luma") { UseShellExecute = true }); } catch { }
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_installing) { MessageBox.Show(T("Please wait for the installation to finish."), "Luma Setup", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        Close();
    }
}

internal static class RegistryExtensions
{
    public static RegistryKey DefaultIcon(this RegistryKey key) => key.CreateSubKey("DefaultIcon")!;
}

