using System.IO;
using System.Text.Json;

namespace InstructMe;

/// <summary>User settings stored in %APPDATA%\InstructMe\settings.json.</summary>
internal sealed class AppSettings
{
    public string Hotkey { get; set; } = "Ctrl+Alt+L";
    public string Model { get; set; } = "claude-haiku-4-5";

    /// <summary>Free Microsoft neural voice (online). Empty: use only the installed Windows voices.</summary>
    public string Voice { get; set; } = "en-US-EmmaMultilingualNeural";

    /// <summary>Optional. Takes priority over the ANTHROPIC_API_KEY environment variable.</summary>
    public string? AnthropicApiKey { get; set; }

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InstructMe", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // Keep "Ctrl+Alt+L" readable in the file instead of "Ctrl+Alt+L".
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            var defaults = new AppSettings();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(defaults, JsonOptions));
            return defaults;
        }
        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>The fallback key from ANTHROPIC_API_KEY, or null.</summary>
    public static string? EnvironmentApiKey()
    {
        // Also read the saved user variable: after `setx`, apps started from an old
        // terminal do not get the new value in their own environment.
        foreach (var target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User })
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY", target);
            if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment.Trim();
        }
        return null;
    }

    public string? ResolveApiKey() =>
        string.IsNullOrWhiteSpace(AnthropicApiKey) ? EnvironmentApiKey() : AnthropicApiKey.Trim();
}
