using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Point = System.Drawing.Point;

namespace ShipmentTracker.Desktop;

internal static class ParcelIcon
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var outline = new Pen(Color.FromArgb(36, 32, 21), 4) { LineJoin = LineJoin.Round };
        using var top = new SolidBrush(Color.FromArgb(255, 222, 90));
        using var left = new SolidBrush(Color.FromArgb(255, 204, 0));
        using var right = new SolidBrush(Color.FromArgb(229, 164, 0));
        Point[] topFace = [new(32, 5), new(57, 18), new(32, 32), new(7, 18)];
        Point[] leftFace = [new(7, 18), new(32, 32), new(32, 59), new(7, 45)];
        Point[] rightFace = [new(32, 32), new(57, 18), new(57, 45), new(32, 59)];
        graphics.FillPolygon(top, topFace);
        graphics.FillPolygon(left, leftFace);
        graphics.FillPolygon(right, rightFace);
        graphics.DrawPolygon(outline, topFace);
        graphics.DrawPolygon(outline, leftFace);
        graphics.DrawPolygon(outline, rightFace);
        graphics.DrawLine(outline, 20, 12, 45, 25);
        graphics.DrawLine(outline, 45, 25, 45, 35);
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            if (!DestroyIcon(handle))
                System.Diagnostics.Trace.TraceError("Could not release the temporary parcel icon.");
        }
    }

    public static BitmapSource WindowImage(Icon icon)
    {
        var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        image.Freeze();
        return image;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
