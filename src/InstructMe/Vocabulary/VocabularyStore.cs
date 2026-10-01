using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using InstructMe.Definitions;

namespace InstructMe.Vocabulary;

internal sealed record VocabularyEntry(
    string Phrase,
    string Sentence,
    string Model,
    WordDefinition Definition,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    int Lookups,
    Guid LastSessionId)
{
    [JsonIgnore] public string LastSeenLabel => LastSeen.ToLocalTime().ToString("dd/MM/yyyy · HH:mm");
    [JsonIgnore] public string LookupLabel => Lookups == 1 ? "1 consultation" : $"{Lookups} consultations";
}

/// <summary>Successful translations, kept locally with their exact context and model.</summary>
internal sealed class VocabularyStore
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InstructMe", "vocabulary.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private readonly object _sync = new();
    private readonly Dictionary<(string Phrase, string Sentence, string Model), VocabularyEntry> _entries = new();
    private bool _canSave = true;

    public Guid SessionId { get; } = Guid.NewGuid();
    public string? LoadWarning { get; private set; }
    public string? SaveError { get; private set; }
    public event Action? Changed;

    public VocabularyStore(string? path = null)
    {
        _path = path ?? FilePath;
        Load();
    }

    public IReadOnlyList<VocabularyEntry> Entries
    {
        get { lock (_sync) return _entries.Values.OrderByDescending(e => e.LastSeen).ToList(); }
    }

    internal static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public WordDefinition? Find(string phrase, string sentence, string model)
    {
        lock (_sync)
            return _entries.GetValueOrDefault((Normalize(phrase), Normalize(sentence), model))?.Definition;
    }

    public void Record(string phrase, string sentence, string model, WordDefinition definition)
    {
        definition.Validate();
        lock (_sync)
        {
            var key = (Normalize(phrase), Normalize(sentence), model);
            var now = DateTimeOffset.UtcNow;
            var previous = _entries.GetValueOrDefault(key);
            _entries[key] = new VocabularyEntry(key.Item1, key.Item2, model, definition,
                previous?.FirstSeen ?? now, now, (previous?.Lookups ?? 0) + 1, SessionId);
            Save();
        }
        Changed?.Invoke();
    }

    public IReadOnlyList<VocabularyEntry> Search(string query, bool currentSession)
    {
        query = query.Trim();
        return Entries.Where(e => (!currentSession || e.LastSessionId == SessionId)
            && (query.Length == 0 || new[] { e.Phrase, e.Definition.Word, e.Definition.Translation, e.Sentence }
                .Any(s => s.Contains(query, StringComparison.OrdinalIgnoreCase)))).ToList();
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var entries = JsonSerializer.Deserialize<List<VocabularyEntry>>(File.ReadAllText(_path), JsonOptions)
                ?? throw new JsonException("Empty vocabulary.");
            foreach (var entry in entries)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.Phrase) || string.IsNullOrWhiteSpace(entry.Sentence)
                    || string.IsNullOrWhiteSpace(entry.Model) || entry.Definition is null || entry.Lookups < 1)
                    throw new JsonException("Invalid vocabulary entry.");
                entry.Definition.Validate();
            }
            foreach (var entry in entries)
                _entries[(Normalize(entry.Phrase), Normalize(entry.Sentence), entry.Model)] = entry;
        }
        catch (JsonException)
        {
            // Preserve the unreadable file before any successful lookup replaces it.
            try
            {
                File.Copy(_path, _path + $".backup-{Guid.NewGuid():N}");
                LoadWarning = "Le carnet précédent était illisible. Une copie de sauvegarde a été conservée.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _canSave = false;
                SaveError = "Le carnet est illisible et ne peut pas être sauvegardé. Les nouveaux mots restent en mémoire.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _canSave = false;
            SaveError = "Le carnet ne peut pas être lu. Les nouveaux mots restent en mémoire.";
        }
    }

    private void Save()
    {
        if (!_canSave) return;
        var temporary = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(_entries.Values.ToList(), JsonOptions));
            File.Move(temporary, _path, overwrite: true);
            SaveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveError = "Enregistrement du carnet impossible. Les nouveaux mots restent en mémoire pour cette session.";
        }
    }
}
