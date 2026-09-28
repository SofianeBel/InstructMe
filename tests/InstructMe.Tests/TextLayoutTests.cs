using System.Windows;
using InstructMe.Input;
using InstructMe.Text;

namespace InstructMe.Tests;

public class TextLayoutTests
{
    // The journal from the mockup, plus an unrelated label far below it.
    //   line 0: Make the people happy — reach a
    //   line 1: satisfaction level of 60.
    //   line 2: TENT
    private static DetectedText Journal() => DetectedText.FromLines(
    [
        Line(100, 300, "Make", "the", "people", "happy", "—", "reach", "a"),
        Line(100, 322, "satisfaction", "level", "of", "60."),
        Line(900, 600, "TENT"),
    ]);

    private static IEnumerable<(string, Rect)> Line(double x, double y, params string[] words)
    {
        foreach (var w in words)
        {
            double width = w.Length * 9;
            yield return (w, new Rect(x, y, width, 18));
            x += width + 6;
        }
    }

    private static DetectedWord Word(DetectedText text, string value) => text.Words.Single(w => w.Text == value);

    [Fact]
    public void Row_split_by_the_ocr_is_joined_but_separate_columns_are_not()
    {
        var text = DetectedText.FromLines(
        [
            Line(100, 300, "reach", "a", "satisfaction", "level"),
            Line(331, 300, "of", "60."),     // "level" ends at x = 325
            Line(1400, 300, "Day:", "05"),                          // same row, far away
        ]);

        Assert.Equal(2, text.Lines.Count);
        Assert.Equal("reach a satisfaction level of 60.", text.Lines[0].Text);
        Assert.Equal(text.Words.Count, text.Words.Select(w => w.Index).Distinct().Count());
    }

    [Fact]
    public void Right_moves_along_the_line()
    {
        var text = Journal();
        Assert.Equal("a", WordNavigator.Move(text, Word(text, "reach"), NavDirection.Right).Text);
    }

    [Fact]
    public void Down_moves_to_the_word_below_not_the_far_label()
    {
        var text = Journal();
        var below = WordNavigator.Move(text, Word(text, "the"), NavDirection.Down);
        Assert.Equal(1, below.LineIndex);
    }

    [Fact]
    public void Up_from_the_top_line_keeps_the_selection()
    {
        var text = Journal();
        var make = Word(text, "Make");
        Assert.Same(make, WordNavigator.Move(text, make, NavDirection.Up));
    }

    [Fact]
    public void Wrapped_lines_form_one_sentence_and_the_distant_label_is_excluded()
    {
        var text = Journal();
        Assert.Equal("Make the people happy — reach a satisfaction level of 60.", ContextBuilder.Sentence(text, 0));
        Assert.Equal("TENT", ContextBuilder.Sentence(text, 2));
    }

    [Fact]
    public void Selection_extends_to_a_phrase_and_strips_punctuation()
    {
        var text = Journal();
        var selection = WordSelection.Of(Word(text, "of"), text).Extend(text);
        Assert.Equal("of 60", selection.Phrase(text));

        // Extending past the end of the line does nothing.
        Assert.Equal(selection, selection.Extend(text));
        Assert.Equal("of", selection.Shrink().Phrase(text));
    }

    [Fact]
    public void Shift_click_on_an_earlier_word_selects_the_whole_range()
    {
        var text = Journal();
        var selection = WordSelection.Of(Word(text, "happy"), text).ExtendTo(Word(text, "the"), text);
        Assert.Equal("the people happy", selection.Phrase(text));
    }
}
