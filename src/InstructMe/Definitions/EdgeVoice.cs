using System.IO;
using System.Net.WebSockets;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace InstructMe.Definitions;

/// <summary>
/// Microsoft neural voices from the free online service behind Edge "Read aloud".
/// No account or key. The service is not documented, so callers must keep a fallback.
/// Protocol from https://github.com/rany2/edge-tts.
/// </summary>
internal sealed class EdgeVoice
{
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string ChromiumVersion = "143.0.3650.75";
    private const string Endpoint = "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private readonly string _voice;

    public EdgeVoice(string voice) => _voice = voice;

    /// <summary>Returns the spoken text as MP3 bytes.</summary>
    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var token = timeout.Token;

        using var socket = new ClientWebSocket();
        var major = ChromiumVersion.Split('.')[0];
        socket.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        socket.Options.SetRequestHeader("User-Agent",
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{major}.0.0.0 Safari/537.36 Edg/{major}.0.0.0");
        socket.Options.SetRequestHeader("Pragma", "no-cache");
        socket.Options.SetRequestHeader("Cache-Control", "no-cache");
        socket.Options.SetRequestHeader("Cookie", $"muid={Convert.ToHexString(RandomNumberGenerator.GetBytes(16))};");

        var url = $"{Endpoint}?TrustedClientToken={TrustedClientToken}&ConnectionId={Guid.NewGuid():N}" +
                  $"&Sec-MS-GEC={SecMsGec(DateTimeOffset.UtcNow)}&Sec-MS-GEC-Version=1-{ChromiumVersion}";
        await socket.ConnectAsync(new Uri(url), token);

        var timestamp = DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'",
            System.Globalization.CultureInfo.InvariantCulture);
        await SendTextAsync(socket,
            $"X-Timestamp:{timestamp}\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n" +
            """{"context":{"synthesis":{"audio":{"metadataoptions":{"sentenceBoundaryEnabled":"false","wordBoundaryEnabled":"false"},"outputFormat":"audio-24khz-48kbitrate-mono-mp3"}}}}""" + "\r\n",
            token);
        await SendTextAsync(socket,
            $"X-RequestId:{Guid.NewGuid():N}\r\nContent-Type:application/ssml+xml\r\nX-Timestamp:{timestamp}Z\r\nPath:ssml\r\n\r\n" +
            Ssml(_voice, text),
            token);

        var audio = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var message = new MemoryStream();
        while (true)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new IOException("Le service de voix a fermé la connexion.");
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var bytes = message.GetBuffer().AsSpan(0, (int)message.Length);
            if (result.MessageType == WebSocketMessageType.Text)
            {
                if (Encoding.UTF8.GetString(bytes).Contains("Path:turn.end", StringComparison.Ordinal)) break;
            }
            else if (bytes.Length >= 2)
            {
                // Binary frame: 2-byte big-endian header length, the headers, then the audio.
                int headerLength = (bytes[0] << 8) | bytes[1];
                if (2 + headerLength <= bytes.Length) audio.Write(bytes[(2 + headerLength)..]);
            }
        }

        if (audio.Length == 0) throw new IOException("Le service de voix n'a pas renvoyé d'audio.");
        try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
        catch (WebSocketException) { /* The service may already be gone: the audio is complete. */ }
        return audio.ToArray();
    }

    /// <summary>
    /// SHA-256 of the Windows file time (rounded down to 5 minutes) plus the client token.
    /// </summary>
    internal static string SecMsGec(DateTimeOffset now)
    {
        long seconds = now.ToUnixTimeSeconds() + 11644473600;
        seconds -= seconds % 300;
        var input = $"{seconds * 10_000_000}{TrustedClientToken}";
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(input)));
    }

    internal static string Ssml(string voice, string text) =>
        "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>" +
        $"<voice name='{SecurityElement.Escape(voice)}'><prosody pitch='+0Hz' rate='+0%' volume='+0%'>" +
        SecurityElement.Escape(text) +
        "</prosody></voice></speak>";

    private static Task SendTextAsync(ClientWebSocket socket, string text, CancellationToken token) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, token);
}
