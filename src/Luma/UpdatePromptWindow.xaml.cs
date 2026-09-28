using Luma.Updates;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Luma;

public partial class UpdatePromptWindow : Window
{
    private readonly bool _mandatory;
    private bool _installAccepted;

    public UpdatePromptWindow(SignedUpdateManifest manifest)
    {
        InitializeComponent();
        ApplyTheme();
        _mandatory = manifest.Mandatory;
        VersionText.Text = $"Luma {manifest.Version}";
        TitleText.Text = string.IsNullOrWhiteSpace(manifest.Title) ? "Новая версия готова" : manifest.Title;
        NotesText.Text = string.IsNullOrWhiteSpace(manifest.Notes) ? "Исправления и улучшения стабильности." : manifest.Notes;
        LaterButton.Visibility = _mandatory ? Visibility.Collapsed : Visibility.Visible;
        CloseButton.Visibility = _mandatory ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyTheme()
    {
        var theme = Registry.CurrentUser.OpenSubKey(@"Software\Luma")?.GetValue("Theme") as string ?? "dark";
        var p = theme switch
        {
            "blue" => (Accent: "#6687C8", Soft: "#243550", Surface: "#172334", Raised: "#1D2C40", Inset: "#0B1420", Border: "#2A3C53", Strong: "#405873", MutedAccent: "#AFC3EA", Muted: "#8090A5", Secondary: "#CFD8E6"),
            "purple" => (Accent: "#756BB7", Soft: "#302A42", Surface: "#211E29", Raised: "#292531", Inset: "#17141C", Border: "#37323F", Strong: "#4A4355", MutedAccent: "#B9B0DE", Muted: "#8D8797", Secondary: "#D7D2DC"),
            "sand" => (Accent: "#A77C64", Soft: "#3A2D27", Surface: "#28211D", Raised: "#302722", Inset: "#191411", Border: "#40352F", Strong: "#594940", MutedAccent: "#D8B9A7", Muted: "#A08D82", Secondary: "#E2D7D0"),
            "mint" => (Accent: "#4F9E94", Soft: "#203B36", Surface: "#172724", Raised: "#1D302C", Inset: "#0C1715", Border: "#29413C", Strong: "#3B5B54", MutedAccent: "#A9D8D1", Muted: "#7F9E99", Secondary: "#CEE0DD"),
            _ => (Accent: "#7468C7", Soft: "#2F2B47", Surface: "#202123", Raised: "#27282B", Inset: "#17181A", Border: "#323338", Strong: "#45464E", MutedAccent: "#B5AEDF", Muted: "#8B8C92", Secondary: "#D1D2D6")
        };
        string[] keys = ["UpdateAccent", "UpdateSoft", "UpdateSurface", "UpdateRaised", "UpdateInset", "UpdateBorder", "UpdateStrong", "UpdateMutedAccent", "UpdateMuted", "UpdateSecondary"];
        string[] values = [p.Accent, p.Soft, p.Surface, p.Raised, p.Inset, p.Border, p.Strong, p.MutedAccent, p.Muted, p.Secondary];
        for (var i = 0; i < keys.Length; i++) Resources[keys[i]] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(values[i]));
    }

    private void Later_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
    private void Install_Click(object sender, RoutedEventArgs e) { _installAccepted = true; DialogResult = true; }
    private void Window_Drag(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_mandatory && !_installAccepted) { e.Cancel = true; return; }
        base.OnClosing(e);
    }
}
