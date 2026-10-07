using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Luma.Voice;

public static class DesktopCapture
{
    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;
    private const int CURSOR_SHOWING = 0x00000001;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hObject, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hObjectSource, int nXSrc, int nYSrc, int dwRop);

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(out CURSORINFO pci);

    [DllImport("user32.dll")]
    private static extern bool DrawIcon(IntPtr hDC, int X, int Y, IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    public static string? CaptureScreenBase64()
    {
        try
        {
            var fgHandle = GetForegroundWindow();
            Screen targetScreen = fgHandle != IntPtr.Zero
                ? Screen.FromHandle(fgHandle)
                : Screen.PrimaryScreen ?? Screen.AllScreens[0];

            var bounds = targetScreen.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return null;

            var hdcSrc = GetDC(IntPtr.Zero);
            var hdcDest = CreateCompatibleDC(hdcSrc);
            var hBitmap = CreateCompatibleBitmap(hdcSrc, bounds.Width, bounds.Height);
            var hOld = SelectObject(hdcDest, hBitmap);

            BitBlt(hdcDest, 0, 0, bounds.Width, bounds.Height, hdcSrc, bounds.X, bounds.Y, SRCCOPY | CAPTUREBLT);

            // Draw cursor on the captured screen if visible so Luma can see where user is pointing
            try
            {
                var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
                if (GetCursorInfo(out ci) && ci.flags == CURSOR_SHOWING && ci.hCursor != IntPtr.Zero)
                {
                    int curX = ci.ptScreenPos.x - bounds.X;
                    int curY = ci.ptScreenPos.y - bounds.Y;
                    DrawIcon(hdcDest, curX, curY, ci.hCursor);
                }
            }
            catch { }

            SelectObject(hdcDest, hOld);
            DeleteDC(hdcDest);
            ReleaseDC(IntPtr.Zero, hdcSrc);

            using var rawBmp = Image.FromHbitmap(hBitmap);
            DeleteObject(hBitmap);

            // Downscale to max 1920x1080 if screen is ultra-wide or 4K to optimize speed and vision model latency
            int targetWidth = bounds.Width;
            int targetHeight = bounds.Height;
            const int maxDim = 1920;

            if (targetWidth > maxDim || targetHeight > maxDim)
            {
                double scale = Math.Min((double)maxDim / targetWidth, (double)maxDim / targetHeight);
                targetWidth = Math.Max(1, (int)(targetWidth * scale));
                targetHeight = Math.Max(1, (int)(targetHeight * scale));
            }

            using var scaledBmp = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(scaledBmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(rawBmp, 0, 0, targetWidth, targetHeight);
            }

            // Encode to JPEG 82% quality
            using var ms = new MemoryStream();
            var encoder = GetEncoder(ImageFormat.Jpeg);
            var encParams = new EncoderParameters(1);
            encParams.Param[0] = new EncoderParameter(Encoder.Quality, 82L);

            if (encoder != null)
                scaledBmp.Save(ms, encoder, encParams);
            else
                scaledBmp.Save(ms, ImageFormat.Jpeg);

            return Convert.ToBase64String(ms.ToArray());
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return null;
        }
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        return ImageCodecInfo.GetImageDecoders().FirstOrDefault(codec => codec.FormatID == format.Guid);
    }
}
