using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.IO;

namespace Luma;

/// <summary>Detects and repairs the Evergreen WebView2 Runtime before any browser view is created.</summary>
internal static class WebViewRuntime
{
    public const string BootstrapperFileName = "MicrosoftEdgeWebview2Setup.exe";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool attemptedRepair;

    public static bool IsAvailable(out string version)
    {
        try
        {
            version = CoreWebView2Environment.GetAvailableBrowserVersionString() ?? "";
            return !string.IsNullOrWhiteSpace(version);
        }
        catch
        {
            version = "";
            return false;
        }
    }

    public static async Task<bool> EnsureAvailableAsync(bool allowRepair = true)
    {
        if (IsAvailable(out _)) return true;
        if (!allowRepair) return false;
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsAvailable(out _)) return true;
            if (attemptedRepair) return false;
            attemptedRepair = true;
            var setup = Path.Combine(AppContext.BaseDirectory, BootstrapperFileName);
            if (!File.Exists(setup))
            {
                App.Warn($"WebView2 Runtime is unavailable and {BootstrapperFileName} is missing.");
                return false;
            }

            using var process = Process.Start(new ProcessStartInfo(setup)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = "/silent /install",
            });
            if (process is null) return false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (process.ExitCode != 0) App.Warn($"WebView2 repair exited with code {process.ExitCode}.");
            for (var attempt = 0; attempt < 20; attempt++)
            {
                if (IsAvailable(out var version)) { App.Warn($"WebView2 Runtime is ready: {version}."); return true; }
                await Task.Delay(500).ConfigureAwait(false);
            }
            return false;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return false;
        }
        finally { Gate.Release(); }
    }
}
