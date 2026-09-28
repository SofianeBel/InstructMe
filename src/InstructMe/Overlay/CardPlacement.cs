using System.Windows;

namespace InstructMe.Overlay;

/// <summary>Chooses where the definition card goes so it does not cover the sentence.</summary>
internal static class CardPlacement
{
    public const double Margin = 16;
    public const double Gap = 14;

    /// <param name="selection">The selected words, in window coordinates.</param>
    /// <param name="sentence">The whole text block the card must not cover.</param>
    public static Point Place(Rect selection, Rect sentence, Size card, Size viewport)
    {
        double alignedX = Clamp(selection.Left - 12, Margin, viewport.Width - card.Width - Margin);
        double alignedY = Clamp(selection.Top - 24, Margin, viewport.Height - card.Height - Margin);

        Point[] candidates =
        [
            new(sentence.Right + Gap, alignedY),              // right of the text, as in the mockup
            new(alignedX, sentence.Bottom + Gap),             // below
            new(alignedX, sentence.Top - Gap - card.Height),  // above
            new(sentence.Left - Gap - card.Width, alignedY),  // left
        ];

        foreach (var p in candidates)
        {
            var rect = new Rect(p, card);
            if (Fits(rect, viewport) && !rect.IntersectsWith(sentence)) return p;
        }

        // No free space: keep the selected words visible at least.
        foreach (var p in candidates)
        {
            var rect = new Rect(p, card);
            if (Fits(rect, viewport) && !rect.IntersectsWith(selection)) return p;
        }

        return new Point(alignedX, Clamp(selection.Bottom + Gap, Margin, viewport.Height - card.Height - Margin));
    }

    private static bool Fits(Rect r, Size viewport) =>
        r.Left >= Margin && r.Top >= Margin && r.Right <= viewport.Width - Margin && r.Bottom <= viewport.Height - Margin;

    private static double Clamp(double value, double min, double max) => max < min ? min : Math.Clamp(value, min, max);
}
