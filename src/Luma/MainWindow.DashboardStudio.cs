using System.Windows;
using System.Windows.Input;

namespace Luma;

public partial class MainWindow
{
    private void InitializeDashboardStudio() => InitializeHomeBuilder();

    public void ApplyHomeDashboardSettings()
    {
        if (HomeSurfaceContent is null) return;
        RebuildHomeBuilder();
    }

    private void HomeCustomize_Click(object sender, RoutedEventArgs e) => ToggleHomeStudio();
    private void HomeCustomizeClose_Click(object sender, RoutedEventArgs e) => CloseHomeStudio();
    private void HomeCustomizeDrawer_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
