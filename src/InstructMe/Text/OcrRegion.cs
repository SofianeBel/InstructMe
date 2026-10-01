using System.Windows;

namespace InstructMe.Text;

/// <summary>Converts a dragged area and cropped OCR bounds to capture pixels.</summary>
internal static class OcrRegion
{
    public static Int32Rect? FromPoints(Point start, Point end, int width, int height)
    {
        int left = (int)Math.Floor(Math.Clamp(Math.Min(start.X, end.X), 0, width));
        int top = (int)Math.Floor(Math.Clamp(Math.Min(start.Y, end.Y), 0, height));
        int right = (int)Math.Ceiling(Math.Clamp(Math.Max(start.X, end.X), 0, width));
        int bottom = (int)Math.Ceiling(Math.Clamp(Math.Max(start.Y, end.Y), 0, height));
        return right - left < 8 || bottom - top < 8 ? null : new Int32Rect(left, top, right - left, bottom - top);
    }

    public static Rect ToCapture(Rect bounds, double scale, Int32Rect region) =>
        new(region.X + bounds.X / scale, region.Y + bounds.Y / scale, bounds.Width / scale, bounds.Height / scale);
}
