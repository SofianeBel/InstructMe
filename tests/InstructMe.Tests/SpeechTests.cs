using InstructMe.Definitions;
using InstructMe.Input;
using static InstructMe.Input.GamepadInterpreter;

namespace InstructMe.Tests;

public class SpeechTests
{
    [Fact]
    public void Token_matches_the_edge_tts_reference()
    {
        // Value computed with the edge-tts Python algorithm for the same instant.
        Assert.Equal("CA99F0B37F2EAC6F5D719AE4BA7C98978842B3F335D9F42979070A8DB149F0A5",
            EdgeVoice.SecMsGec(DateTimeOffset.FromUnixTimeSeconds(1790000000)));
    }

    [Fact]
    public void Game_text_cannot_break_the_ssml()
    {
        var ssml = EdgeVoice.Ssml("en-US-EmmaMultilingualNeural", "Tom & Jerry's <b>\"plan\"</b>");
        Assert.Contains("Tom &amp; Jerry&apos;s &lt;b&gt;&quot;plan&quot;&lt;/b&gt;", ssml);
    }

    [Fact]
    public void Y_speaks_the_word_and_X_speaks_the_sentence()
    {
        var pad = new GamepadInterpreter();
        pad.Update(0, 0, 0, TimeSpan.Zero);
        Assert.Equal([GamepadAction.SpeakWord], pad.Update(ButtonY, 0, 0, TimeSpan.FromMilliseconds(16)));
        Assert.Equal([GamepadAction.SpeakSentence], pad.Update(ButtonX, 0, 0, TimeSpan.FromMilliseconds(32)));
    }
}
