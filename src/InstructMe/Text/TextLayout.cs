using System.Windows;

namespace InstructMe.Text;

/// <summary>One word found by OCR. Bounds are in capture pixels.</summary>
internal sealed record DetectedWord(int Index, int LineIndex, string Text, Rect Bounds)
{
    private static readonly char[] Punctuation = ".,;:!?\"'“”‘’()[]{}«»…—–-".ToCharArray();

    /// <summary>The word without surrounding punctuation, e.g. "60." becomes "60".</summary>
    public string CleanText => Text.Trim(Punctuation);
}

internal sealed record DetectedLine(int Index, IReadOnlyList<DetectedWord> Words)
{
    public string Text => string.Join(' ', Words.Select(w => w.Text));
    public Rect Bounds => Union(Words.Select(w => w.Bounds));

    internal static Rect Union(IEnumerable<Rect> rects)
    {
        var result = Rect.Empty;
        foreach (var r in rects) result.Union(r);
        return result;
    }
}

internal sealed record DetectedText(IReadOnlyList<DetectedLine> Lines)
{
    public IReadOnlyList<DetectedWord> Words { get; } = Lines.SelectMany(l => l.Words).ToList();

    /// <summary>Builds lines and words with stable indexes from raw OCR output.</summary>
    public static DetectedText FromLines(IEnumerable<IEnumerable<(string Text, Rect Bounds)>> lines)
    {
        var result = new List<DetectedLine>();
        int wordIndex = 0;
        foreach (var line in MergeSameRow(lines.Select(l => l.ToList()).Where(l => l.Count > 0).ToList()))
        {
            int lineIndex = result.Count;
            var words = line.Select(w => new DetectedWord(wordIndex++, lineIndex, w.Text, w.Bounds)).ToList();
            if (words.Count == 0) continue;
            result.Add(new DetectedLine(lineIndex, words));
        }
        return new DetectedText(result);
    }

    /// <summary>
    /// Windows OCR sometimes cuts one row of text into two lines ("…level" and "of 60.").
    /// Joins lines that share a row and have only a word-sized gap between them.
    /// </summary>
    private static List<List<(string Text, Rect Bounds)>> MergeSameRow(List<List<(string Text, Rect Bounds)>> lines)
    {
        bool merged = true;
        while (merged)
        {
            merged = false;
            for (int i = 0; i < lines.Count && !merged; i++)
            for (int j = 0; j < lines.Count && !merged; j++)
            {
                if (i == j) continue;
                Rect left = DetectedLine.Union(lines[i].Select(w => w.Bounds));
                Rect right = DetectedLine.Union(lines[j].Select(w => w.Bounds));
                double height = Math.Max(left.Height, right.Height);
                double rowOffset = Math.Abs((left.Top + left.Bottom) / 2 - (right.Top + right.Bottom) / 2);
                double gap = right.Left - left.Right;
                if (rowOffset < height * 0.4 && gap > -height * 0.2 && gap < height * 1.5)
                {
                    lines[i].AddRange(lines[j]);
                    lines.RemoveAt(j);
                    merged = true;
                }
            }
        }
        return lines;
    }
}

/// <summary>A run of consecutive words on one line: a single word or a phrase like "give up".</summary>
internal readonly record struct WordSelection(int LineIndex, int Start, int End)
{
    public static WordSelection Of(DetectedWord word, DetectedText text) =>
        new(word.LineIndex, PositionInLine(word, text), PositionInLine(word, text));

    public IEnumerable<DetectedWord> Words(DetectedText text) =>
        text.Lines[LineIndex].Words.Skip(Start).Take(End - Start + 1);

    public string Phrase(DetectedText text) =>
        string.Join(' ', Words(text).Select(w => w.CleanText).Where(s => s.Length > 0));

    public Rect Bounds(DetectedText text) => DetectedLine.Union(Words(text).Select(w => w.Bounds));

    public DetectedWord First(DetectedText text) => text.Lines[LineIndex].Words[Start];
    public DetectedWord Last(DetectedText text) => text.Lines[LineIndex].Words[End];

    public WordSelection Extend(DetectedText text) =>
        End + 1 < text.Lines[LineIndex].Words.Count ? this with { End = End + 1 } : this;

    public WordSelection Shrink() => End > Start ? this with { End = End - 1 } : this;

    /// <summary>Shift+click: grows the selection to include a word on the same line.</summary>
    public WordSelection ExtendTo(DetectedWord word, DetectedText text)
    {
        if (word.LineIndex != LineIndex) return Of(word, text);
        int position = PositionInLine(word, text);
        return new WordSelection(LineIndex, Math.Min(Start, position), Math.Max(End, position));
    }

    private static int PositionInLine(DetectedWord word, DetectedText text) =>
        word.Index - text.Lines[word.LineIndex].Words[0].Index;
}
