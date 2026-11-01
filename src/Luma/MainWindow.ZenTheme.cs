using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Luma;

/// <summary>
/// One shared "Zen chrome" palette for the whole shell: the left panel, the library drawer, popups,
/// the start page widgets and the profile all pull their neutral greys from the same place, so
/// choosing a theme re-tints everything at once instead of only the page background.
/// </summary>
public partial class MainWindow
{
    // Every neutral grey the XAML used to hard-code. Each one is now a DynamicResource "Zen_RRGGBB".
    private static readonly string[] ZenGrayKeys =
    [
        "28282E", "2A2A2E", "2B2B2D", "2C2C2E", "2E2E34", "2E2F35", "35353A", "383842", "3A3A44", "3C3C3F",
        "3D3D41", "3D3D42", "3F3F44", "3F3F46", "404044", "424246", "444449", "48484D", "49494E", "4B4B50",
        "4E5058", "505056", "525257", "525258", "54545A", "55555A", "56565A", "58585E", "5A5A60", "5E5E64",
        "606065", "636368", "63636A", "686870", "4A4A4F", "4C4C52", "2C2C32", "2F2F36", "2D2D35", "2A2C32",
        "2C2D33", "343840", "3D3E46", "6E6E75", "32323A", "35353D", "525257"
    ];

    private static readonly Color ZenBaseChrome = Color.FromRgb(0x3D, 0x3D, 0x42);
    private static int _zenDr, _zenDg, _zenDb;

    /// <summary>Sidebar / chrome colour of every theme (the original Zen grey is the "dark" theme).</summary>
    private static Color ZenChromeFor(string theme) => theme switch
    {
        "graphite" => Color.FromRgb(0x3D, 0x41, 0x48),
        "purple" => Color.FromRgb(0x47, 0x40, 0x5C),
        "blue" => Color.FromRgb(0x34, 0x41, 0x5C),
        "mint" => Color.FromRgb(0x32, 0x48, 0x41),
        "sand" => Color.FromRgb(0x4C, 0x41, 0x37),
        _ => ZenBaseChrome
    };

    private static void SetZenShift(Color chrome)
    {
        _zenDr = chrome.R - ZenBaseChrome.R;
        _zenDg = chrome.G - ZenBaseChrome.G;
        _zenDb = chrome.B - ZenBaseChrome.B;
    }

    /// <summary>Moves a neutral Zen-range grey towards the active theme tint. Everything else is untouched.</summary>
    internal static Color ZenShift(Color c)
    {
        if (_zenDr == 0 && _zenDg == 0 && _zenDb == 0) return c;
        if (Math.Abs(c.R - c.G) > 10 || Math.Abs(c.G - c.B) > 10 || Math.Abs(c.R - c.B) > 10) return c;
        if (c.G < 0x28 || c.G > 0x74) return c;
        static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);
        return Color.FromArgb(c.A, Clamp(c.R + _zenDr), Clamp(c.G + _zenDg), Clamp(c.B + _zenDb));
    }

    private static Color ZenColor(string hex) => ZenShift((Color)ColorConverter.ConvertFromString(hex));

    private static SolidColorBrush ZenFrozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>Publishes the themed greys as window resources so every DynamicResource follows the theme.</summary>
    private void ApplyZenChromeResources()
    {
        foreach (var hex in ZenGrayKeys.Distinct())
            Resources["Zen_" + hex] = ZenFrozen(ZenColor("#" + hex));

        Resources["SidebarBackgroundBrush"] = ZenFrozen(ZenColor("#3D3D42"));
        Resources["SidebarDrawerBackgroundBrush"] = ZenFrozen(ZenColor("#3D3D42"));
        Resources["SidebarItemHoverBrush"] = ZenFrozen(ZenColor("#4B4B50"));
        Resources["SidebarItemActiveBrush"] = ZenFrozen(ZenColor("#56565A"));
        Resources["SidebarSeparatorBrush"] = ZenFrozen(ZenColor("#4B4B50"));
        Resources["InputBackgroundBrush"] = ZenFrozen(ZenColor("#4B4B50"));
        Resources["InputHoverBackgroundBrush"] = ZenFrozen(ZenColor("#56565A"));
        Resources["ZenChromeColor"] = ZenColor("#3D3D42");
        Resources["ZenChromeBrush"] = ZenFrozen(ZenColor("#3D3D42"));
        Resources["ZenCardBrush"] = ZenFrozen(ZenColor("#202025"));
        Resources["ZenCardHoverBrush"] = ZenFrozen(ZenColor("#27272D"));
        Resources["ZenCardActiveBrush"] = ZenFrozen(ZenColor("#303038"));
        Resources["ZenCardBorderBrush"] = ZenFrozen(ZenColor("#34343E"));
        Resources["ZenInputBrush"] = ZenFrozen(ZenColor("#1A1A1E"));
        Resources["ZenIconBrush"] = ZenFrozen(Color.FromRgb(0xC8, 0xC8, 0xCE));
        Resources["ZenTextBrush"] = ZenFrozen(Color.FromRgb(0xF4, 0xF4, 0xF7));
        Resources["ZenMutedBrush"] = ZenFrozen(Color.FromRgb(0x9E, 0x9E, 0xAA));
    }
}
