using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Image = System.Windows.Controls.Image;

namespace Luma;

public static class FolderAnimationHelper
{
    private static readonly Dictionary<string, BitmapImage[]> FrameCache = new();

    public static BitmapImage[] GetFrames(string prefix, int count)
    {
        if (FrameCache.TryGetValue(prefix, out var cached)) return cached;
        var list = new List<BitmapImage>();
        for (int i = 0; i < count; i++)
        {
            BitmapImage? bmp = null;
            try
            {
                var uri = new Uri($"pack://application:,,,/Luma;component/Assets/FolderIcons/{prefix}_{i:D2}.png", UriKind.Absolute);
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = uri;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                bmp = img;
            }
            catch
            {
                try
                {
                    var uri = new Uri($"pack://application:,,,/Assets/FolderIcons/{prefix}_{i:D2}.png", UriKind.Absolute);
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.UriSource = uri;
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.EndInit();
                    img.Freeze();
                    bmp = img;
                }
                catch { }
            }
            if (bmp is not null) list.Add(bmp);
        }
        var arr = list.ToArray();
        FrameCache[prefix] = arr;
        return arr;
    }

    public static BitmapImage? GetFrame(string prefix, int index, int count)
    {
        var frames = GetFrames(prefix, count);
        if (frames.Length == 0) return null;
        index = Math.Clamp(index, 0, frames.Length - 1);
        return frames[index];
    }

    public static void PlayOnce(Image target, string prefix, int count, int frameMs = 25, Action? onCompleted = null)
    {
        var frames = GetFrames(prefix, count);
        if (frames.Length == 0) return;
        int current = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(frameMs) };
        timer.Tick += (_, _) =>
        {
            if (current < frames.Length)
            {
                target.Source = frames[current];
                current++;
            }
            else
            {
                timer.Stop();
                onCompleted?.Invoke();
            }
        };
        timer.Start();
    }

    public static void AnimateTo(Image target, string prefix, int totalFrames, bool open, int frameMs = 18, Action? onCompleted = null)
    {
        var frames = GetFrames(prefix, totalFrames);
        if (frames.Length == 0) return;
        int start = open ? 0 : frames.Length - 1;
        int end = open ? frames.Length - 1 : 0;
        int step = open ? 1 : -1;
        int current = start;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(frameMs) };
        timer.Tick += (_, _) =>
        {
            target.Source = frames[current];
            if (current == end)
            {
                timer.Stop();
                onCompleted?.Invoke();
            }
            else
            {
                current += step;
            }
        };
        timer.Start();
    }
}
