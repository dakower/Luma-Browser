using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Luma;

/// <summary>Real Windows shell icons for downloaded files (the file's own icon for installers, the associated app's icon for documents).</summary>
internal static class FileIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    // Types whose icon is stored inside the file itself (installer of Chrome shows the Chrome icon, etc.).
    private static bool HasOwnIcon(string extension) =>
        extension is ".exe" or ".msi" or ".lnk" or ".ico" or ".scr" or ".appx" or ".msix" or ".url" or ".cpl";

    public static ImageSource? Get(string path)
    {
        try
        {
            var extension = Path.GetExtension(path);
            var own = HasOwnIcon(extension.ToLowerInvariant()) && File.Exists(path);
            var key = own ? path : (string.IsNullOrEmpty(extension) ? "." : extension);
            if (Cache.TryGetValue(key, out var cached)) return cached;
            if (Cache.Count > 400) Cache.Clear();
            var icon = own
                ? FromFile(path) ?? FromShell(path, false)
                : FromShell("file" + (string.IsNullOrEmpty(extension) ? "" : extension), true);
            Cache[key] = icon;
            return icon;
        }
        catch { return null; }
    }

    private static ImageSource? FromFile(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            return icon is null ? null : ToSource(icon.Handle);
        }
        catch { return null; }
    }

    private static ImageSource? FromShell(string path, bool byAttributes)
    {
        try
        {
            var info = new SHFILEINFO();
            var flags = SHGFI_ICON | (byAttributes ? SHGFI_USEFILEATTRIBUTES : 0u);
            var result = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            try { return ToSource(info.hIcon); }
            finally { DestroyIcon(info.hIcon); }
        }
        catch { return null; }
    }

    private static ImageSource? ToSource(IntPtr handle)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }
}
