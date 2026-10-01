using System.Text.Json;
using System.Windows;
using InstructMe.Definitions;
using InstructMe.Text;

namespace InstructMe.Tests;

public class OcrRecoveryTests
{
    [Fact]
    public void A_reverse_drag_is_clipped_to_the_capture_and_rounded_outwards()
    {
        var region = OcrRegion.FromPoints(new Point(1300, 900), new Point(-20.5, 100.4), 1200, 800);
        Assert.Equal(new Int32Rect(0, 100, 1200, 700), region);
    }

    [Fact]
    public void A_click_or_tiny_drag_does_not_start_an_ocr_scan()
    {
        Assert.Null(OcrRegion.FromPoints(new Point(20, 20), new Point(20, 20), 1920, 1080));
        Assert.Null(OcrRegion.FromPoints(new Point(20, 20), new Point(26, 100), 1920, 1080));
    }

    [Theory]
    [InlineData(2.0, 210, 320, 40, 10)]
    [InlineData(0.5, 240, 380, 160, 40)]
    public void Cropped_word_bounds_map_back_to_capture_pixels(double scale, double x, double y, double width, double height)
    {
        var bounds = OcrRegion.ToCapture(new Rect(20, 40, 80, 20), scale, new Int32Rect(200, 300, 600, 200));
        Assert.Equal(new Rect(x, y, width, height), bounds);
    }

    [Fact]
    public void Manual_input_can_work_without_any_detected_context()
    {
        var input = LookupText.Create("  give up  ", "  ", out var error);
        Assert.Null(error);
        Assert.Equal(new LookupText("give up", "give up"), input);
    }

    [Fact]
    public void Manual_correction_does_not_include_unselected_full_screen_text()
    {
        var text = DetectedText.FromLines([
            new[] { ("Quest", new Rect(100, 100, 50, 20)) },
            new[] { ("HUD", new Rect(900, 800, 40, 20)) },
        ]);
        Assert.Equal(new LookupText("", ""), LookupText.ForCorrection(text, null, focusedArea: false));
        Assert.Equal(new LookupText("", "Quest HUD"), LookupText.ForCorrection(text, null, focusedArea: true));
        Assert.Equal(new LookupText("Quest", "Quest"),
            LookupText.ForCorrection(text, WordSelection.Of(text.Words[0], text), focusedArea: false));
    }

    [Fact]
    public void Empty_or_overlong_input_is_rejected_before_a_lookup()
    {
        Assert.Null(LookupText.Create(" ", "Don't give up.", out var empty));
        Assert.NotNull(empty);
        Assert.Null(LookupText.Create(new string('x', 161), "", out var longPhrase));
        Assert.NotNull(longPhrase);
        Assert.Null(LookupText.Create("give up", new string('x', 2401), out var longContext));
        Assert.NotNull(longContext);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"word\":\"give up\",\"translation\":\"\"}")]
    public void Incomplete_definitions_cannot_be_shown_or_saved(string json)
    {
        Assert.Throws<JsonException>(() => WordDefinition.Parse(json));
    }
}
