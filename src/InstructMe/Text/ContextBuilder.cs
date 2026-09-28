using System.Windows;

namespace InstructMe.Text;

/// <summary>
/// OCR returns separate lines. A game sentence often wraps over several lines,
/// so we join the lines that form one text block around the selected line.
/// </summary>
internal static class ContextBuilder
{
    private const int MaxLinesEachSide = 3;

    public static IReadOnlyList<DetectedLine> Block(DetectedText text, int lineIndex)
    {
        var target = text.Lines[lineIndex];
        var block = new List<DetectedLine> { target };

        var current = target;
        for (int i = 0; i < MaxLinesEachSide; i++)
        {
            var above = text.Lines
                .Where(l => !block.Contains(l) && IsNeighbour(upper: l, lower: current))
                .MaxBy(l => l.Bounds.Bottom);
            if (above is null) break;
            block.Add(above);
            current = above;
        }

        current = target;
        for (int i = 0; i < MaxLinesEachSide; i++)
        {
            var below = text.Lines
                .Where(l => !block.Contains(l) && IsNeighbour(upper: current, lower: l))
                .MinBy(l => l.Bounds.Top);
            if (below is null) break;
            block.Add(below);
            current = below;
        }

        return block.OrderBy(l => l.Bounds.Top).ToList();
    }

    public static string Sentence(DetectedText text, int lineIndex) =>
        string.Join(' ', Block(text, lineIndex).Select(l => l.Text));

    public static Rect Bounds(DetectedText text, int lineIndex) =>
        DetectedLine.Union(Block(text, lineIndex).Select(l => l.Bounds));

    private static bool IsNeighbour(DetectedLine upper, DetectedLine lower)
    {
        Rect a = upper.Bounds, b = lower.Bounds;
        double lineHeight = Math.Max(a.Height, b.Height);
        double gap = b.Top - a.Bottom;
        bool overlapsHorizontally = a.Left < b.Right && b.Left < a.Right;
        bool similarSize = Math.Min(a.Height, b.Height) / lineHeight > 0.6;
        return overlapsHorizontally && similarSize && gap > -lineHeight * 0.3 && gap < lineHeight * 0.9;
    }
}
