using System.Windows.Input;

namespace InstructMe.Input;

/// <summary>A global shortcut such as "Ctrl+Alt+Space", converted to Win32 RegisterHotKey values.</summary>
internal readonly record struct HotkeyGesture(uint Modifiers, uint VirtualKey, string Text)
{
    public const uint ModAlt = 0x1;
    public const uint ModControl = 0x2;
    public const uint ModShift = 0x4;
    public const uint ModWin = 0x8;

    public static HotkeyGesture Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("The shortcut is empty.");

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        uint modifiers = 0;
        Key? key = null;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModControl; break;
                case "alt": modifiers |= ModAlt; break;
                case "shift": modifiers |= ModShift; break;
                case "win" or "windows": modifiers |= ModWin; break;
                default:
                    if (key is not null)
                        throw new FormatException($"The shortcut \"{text}\" has more than one key.");
                    if (!Enum.TryParse<Key>(part, ignoreCase: true, out var parsed) || parsed == Key.None)
                        throw new FormatException($"Unknown key \"{part}\" in shortcut \"{text}\".");
                    key = parsed;
                    break;
            }
        }

        if (key is null)
            throw new FormatException($"The shortcut \"{text}\" has no key.");

        return new HotkeyGesture(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key.Value), text);
    }
}
