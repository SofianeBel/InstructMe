using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;

namespace InstructMe.Definitions;

/// <summary>
/// Reads English words and sentences aloud. It uses a free Microsoft neural voice
/// when online, and an installed Windows voice when the service is not available.
/// </summary>
internal sealed class Pronouncer : IDisposable
{
    private readonly EdgeVoice? _neural;
    private readonly SpeechSynthesizer? _windows;
    private readonly MediaPlayer _player = new();
    private readonly ConcurrentDictionary<string, byte[]> _cache = new();
    private CancellationTokenSource? _current;

    public Pronouncer(string? neuralVoice)
    {
        if (!string.IsNullOrWhiteSpace(neuralVoice)) _neural = new EdgeVoice(neuralVoice);

        var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("en-US", StringComparison.OrdinalIgnoreCase))
            ?? SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        if (voice is not null) _windows = new SpeechSynthesizer { Voice = voice };
    }

    public bool IsAvailable => _neural is not null || _windows is not null;

    public void Stop()
    {
        _current?.Cancel();
        _player.Pause();
    }

    public async Task SpeakAsync(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;

        // A new request stops the one before it.
        _current?.Cancel();
        var current = _current = new CancellationTokenSource();
        _player.Pause();

        var (stream, contentType) = await SynthesizeAsync(text, current.Token);
        if (current.IsCancellationRequested) return;
        _player.Source = MediaSource.CreateFromStream(stream, contentType);
        _player.Play();
    }

    private async Task<(IRandomAccessStream Stream, string ContentType)> SynthesizeAsync(string text, CancellationToken token)
    {
        Exception? neuralError = null;
        if (_neural is not null)
        {
            try
            {
                if (!_cache.TryGetValue(text, out var mp3))
                {
                    mp3 = await _neural.SynthesizeAsync(text, token);
                    if (_cache.Count > 200) _cache.Clear();
                    _cache[text] = mp3;
                }
                var memory = new InMemoryRandomAccessStream();
                await memory.WriteAsync(mp3.AsBuffer());
                memory.Seek(0);
                return (memory, "audio/mpeg");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { neuralError = ex; }
        }

        if (_windows is not null)
        {
            var speech = await _windows.SynthesizeTextToStreamAsync(text);
            return (speech, speech.ContentType);
        }
        throw neuralError ?? new InvalidOperationException("Aucune voix anglaise disponible.");
    }

    public void Dispose()
    {
        _current?.Cancel();
        _player.Dispose();
        _windows?.Dispose();
    }
}
