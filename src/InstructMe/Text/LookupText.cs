namespace InstructMe.Text;

internal sealed record LookupText(string Phrase, string Sentence)
{
    public const int MaxPhraseLength = 160;
    public const int MaxSentenceLength = 2400;

    public static LookupText ForCorrection(DetectedText? text, WordSelection? selection, bool focusedArea)
    {
        if (text is null) return new("", "");
        if (selection is { } selected)
            return new(selected.Phrase(text), ContextBuilder.Sentence(text, selected.LineIndex));
        return new("", focusedArea ? string.Join(' ', text.Lines.Select(l => l.Text)) : "");
    }

    public static LookupText? Create(string phrase, string sentence, out string? error)
    {
        phrase = phrase.Trim();
        sentence = sentence.Trim();
        error = phrase.Length switch
        {
            0 => "Saisissez le mot ou l’expression à expliquer.",
            > MaxPhraseLength => $"L’expression doit contenir au maximum {MaxPhraseLength} caractères.",
            _ => sentence.Length > MaxSentenceLength
                ? $"Le contexte doit contenir au maximum {MaxSentenceLength} caractères." : null,
        };
        return error is null ? new LookupText(phrase, sentence.Length == 0 ? phrase : sentence) : null;
    }
}
