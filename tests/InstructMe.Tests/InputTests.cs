using InstructMe.Input;
using static InstructMe.Input.GamepadInterpreter;

namespace InstructMe.Tests;

public class InputTests
{
    [Fact]
    public void Parses_a_shortcut_into_win32_values()
    {
        var gesture = HotkeyGesture.Parse("Ctrl + Alt + Space");
        Assert.Equal(HotkeyGesture.ModControl | HotkeyGesture.ModAlt, gesture.Modifiers);
        Assert.Equal(0x20u, gesture.VirtualKey); // VK_SPACE
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Ctrl+A+B")]
    public void Rejects_invalid_shortcuts(string text) =>
        Assert.Throws<FormatException>(() => HotkeyGesture.Parse(text));

    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Button_held_when_the_overlay_opens_does_not_fire()
    {
        var pad = new GamepadInterpreter();
        Assert.Empty(pad.Update(ButtonB, 0, 0, Ms(0)));   // B still held from the game
        Assert.Empty(pad.Update(ButtonB, 0, 0, Ms(16)));
        Assert.Empty(pad.Update(0, 0, 0, Ms(32)));
        Assert.Equal([GamepadAction.Cancel], pad.Update(ButtonB, 0, 0, Ms(48)));
    }

    [Fact]
    public void Press_fires_once_until_released()
    {
        var pad = new GamepadInterpreter();
        pad.Update(0, 0, 0, Ms(0));
        Assert.Equal([GamepadAction.Confirm], pad.Update(ButtonA, 0, 0, Ms(16)));
        Assert.Empty(pad.Update(ButtonA, 0, 0, Ms(32)));
    }

    [Fact]
    public void Held_stick_repeats_after_a_delay()
    {
        var pad = new GamepadInterpreter();
        pad.Update(0, 0, 0, Ms(0));
        Assert.Equal([GamepadAction.Right], pad.Update(0, 30000, 0, Ms(10)));
        Assert.Empty(pad.Update(0, 30000, 0, Ms(200)));
        Assert.Equal([GamepadAction.Right], pad.Update(0, 30000, 0, Ms(400)));
        Assert.Equal([GamepadAction.Up], pad.Update(0, 0, 30000, Ms(410))); // XInput Y+ is up
    }
}
