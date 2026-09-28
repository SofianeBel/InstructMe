using System.Windows;
using InstructMe.Input;

namespace InstructMe.Text;

/// <summary>Spatial navigation between detected words for the D-pad and the arrow keys.</summary>
internal static class WordNavigator
{
    public static DetectedWord Move(DetectedText text, DetectedWord from, NavDirection direction)
    {
        // Left and right follow the reading order on the same line first.
        if (direction is NavDirection.Left or NavDirection.Right)
        {
            var line = text.Lines[from.LineIndex].Words;
            int position = from.Index - line[0].Index + (direction == NavDirection.Right ? 1 : -1);
            if (position >= 0 && position < line.Count) return line[position];
        }

        var origin = Center(from.Bounds);
        DetectedWord? best = null;
        double bestScore = double.MaxValue;

        foreach (var word in text.Words)
        {
            if (word.Index == from.Index) continue;
            var c = Center(word.Bounds);
            double dx = c.X - origin.X, dy = c.Y - origin.Y;

            (double along, double across, double minAlong, double acrossWeight) = direction switch
            {
                NavDirection.Right => (dx, dy, 1.0, 2.0),
                NavDirection.Left => (-dx, dy, 1.0, 2.0),
                NavDirection.Down => (dy, dx, from.Bounds.Height * 0.5, 0.6),
                _ => (-dy, dx, from.Bounds.Height * 0.5, 0.6),
            };
            if (along < minAlong) continue;

            double score = along + Math.Abs(across) * acrossWeight;
            if (score < bestScore)
            {
                bestScore = score;
                best = word;
            }
        }

        return best ?? from;
    }

    /// <summary>The word closest to a point, used for the first controller selection.</summary>
    public static DetectedWord? Nearest(DetectedText text, Point point) =>
        text.Words.MinBy(w => (Center(w.Bounds) - point).LengthSquared);

    private static Point Center(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
}
