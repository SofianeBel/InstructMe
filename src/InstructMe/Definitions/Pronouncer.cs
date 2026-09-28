using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace InstructMe.Definitions;

/// <summary>Reads an English word aloud with a Windows text-to-speech voice.</summary>
internal sealed class Pronouncer : IDisposable
{
    private readonly SpeechSynthesizer? _synthesizer;
    private readonly MediaPlayer _player = new();

    public Pronouncer()
    {
        var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("en-US", StringComparison.OrdinalIgnoreCase))
            ?? SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        if (voice is not null) _synthesizer = new SpeechSynthesizer { Voice = voice };
    }

    public bool IsAvailable => _synthesizer is not null;

    public async Task SpeakAsync(string text)
    {
        if (_synthesizer is null || string.IsNullOrWhiteSpace(text)) return;
        var stream = await _synthesizer.SynthesizeTextToStreamAsync(text);
        _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
        _player.Play();
    }

    public void Dispose()
    {
        _player.Dispose();
        _synthesizer?.Dispose();
    }
}
