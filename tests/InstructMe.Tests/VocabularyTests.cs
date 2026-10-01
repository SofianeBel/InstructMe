using System.IO;
using InstructMe.Definitions;
using InstructMe.Vocabulary;

namespace InstructMe.Tests;

public sealed class VocabularyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "InstructMe.Tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "vocabulary.json");
    internal static WordDefinition Definition => new("give up", "give up", "verbe à particule", "/ɡɪv ʌp/",
        "abandonner", "Ici : abandonner votre quête.");

    public VocabularyTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Reopening_the_store_preserves_context_and_marks_a_new_session()
    {
        var first = new VocabularyStore(FilePath);
        first.Record("give up", "Don't give up on your quest.", "model-a", Definition);
        var reopened = new VocabularyStore(FilePath);

        var entry = Assert.Single(reopened.Entries);
        Assert.Equal("Don't give up on your quest.", entry.Sentence);
        Assert.Equal("abandonner", entry.Definition.Translation);
        Assert.Empty(reopened.Search("", currentSession: true));

        reopened.Record("give up", entry.Sentence, "model-a", Definition);
        var repeated = Assert.Single(reopened.Search("", currentSession: true));
        Assert.Equal(2, repeated.Lookups);
        Assert.Equal(entry.FirstSeen, repeated.FirstSeen);
        Assert.Equal(reopened.SessionId, repeated.LastSessionId);
    }

    [Fact]
    public void Cached_meanings_are_separate_for_different_sentences_and_models()
    {
        var store = new VocabularyStore(FilePath);
        store.Record(" give  up ", "Don't give up.\n", "model-a", Definition);

        Assert.Equal(Definition, store.Find("give up", "Don't  give up.", "model-a"));
        Assert.Null(store.Find("give up", "Give up your seat.", "model-a"));
        Assert.Null(store.Find("give up", "Don't give up.", "model-b"));
    }

    [Fact]
    public void Searching_matches_translation_and_context_without_losing_history()
    {
        var store = new VocabularyStore(FilePath);
        store.Record("give up", "Don't give up on your quest.", "model-a", Definition);

        Assert.Single(store.Search("ABANDONNER", currentSession: true));
        Assert.Single(store.Search("quest", currentSession: false));
        Assert.Empty(store.Search("absent", currentSession: false));
        Assert.Single(store.Entries);
    }

    [Fact]
    public void An_unreadable_carnet_is_backed_up_before_new_words_are_saved()
    {
        const string broken = "{incomplete";
        File.WriteAllText(FilePath, broken);
        var store = new VocabularyStore(FilePath);
        Assert.NotNull(store.LoadWarning);
        var backup = Assert.Single(Directory.GetFiles(_directory, "vocabulary.json.backup-*"));
        Assert.Equal(broken, File.ReadAllText(backup));

        store.Record("give up", "Don't give up.", "model-a", Definition);
        Assert.Single(new VocabularyStore(FilePath).Entries);
        Assert.Equal(broken, File.ReadAllText(backup));
    }

    [Fact]
    public void A_save_failure_keeps_the_translation_in_memory_and_reports_it()
    {
        Directory.CreateDirectory(FilePath);
        var store = new VocabularyStore(FilePath);
        store.Record("give up", "Don't give up.", "model-a", Definition);

        Assert.Single(store.Entries);
        Assert.NotNull(store.SaveError);
    }

    [Fact]
    public async Task A_saved_definition_is_available_offline_after_reopening()
    {
        var store = new VocabularyStore(FilePath);
        store.Record("give up", "Don't give up.", "model-a", Definition);
        int requests = 0;
        using var service = new DefinitionService(new AppSettings { Model = "model-a" }, new VocabularyStore(FilePath),
            fetch: (_, _, _) => { requests++; throw new IOException("Offline"); });

        Assert.Equal(Definition, await service.DefineAsync("give up", "Don't give up.", CancellationToken.None));
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task A_timeout_can_be_retried_and_does_not_save_a_failed_lookup()
    {
        var store = new VocabularyStore(FilePath);
        int requests = 0;
        using var service = new DefinitionService(new AppSettings(), store,
            fetch: (_, _, _) => ++requests == 1 ? new TaskCompletionSource<WordDefinition>().Task : Task.FromResult(Definition),
            timeout: TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<TimeoutException>(() => service.DefineAsync("give up", "Don't give up.", CancellationToken.None));
        Assert.Empty(store.Entries);
        Assert.Equal(Definition, await service.DefineAsync("give up", "Don't give up.", CancellationToken.None));
        Assert.Single(store.Entries);
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task Cancelled_lookups_are_not_saved_and_do_not_block_the_next_lookup()
    {
        var store = new VocabularyStore(FilePath);
        var started = new TaskCompletionSource();
        int requests = 0;
        using var service = new DefinitionService(new AppSettings(), store, fetch: async (_, _, token) =>
        {
            if (++requests == 1)
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
            return Definition;
        });
        using var cancelled = new CancellationTokenSource();
        var lookup = service.DefineAsync("give up", "Don't give up.", cancelled.Token);
        await started.Task;
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup);
        Assert.Empty(store.Entries);
        Assert.Equal(Definition, await service.DefineAsync("give up", "Don't give up.", CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_repeat_lookups_share_one_successful_request()
    {
        var store = new VocabularyStore(FilePath);
        var response = new TaskCompletionSource<WordDefinition>();
        int requests = 0;
        using var service = new DefinitionService(new AppSettings(), store,
            fetch: (_, _, _) => { requests++; return response.Task; });
        var first = service.DefineAsync("give up", "Don't give up.", CancellationToken.None);
        var second = service.DefineAsync("give up", "Don't give up.", CancellationToken.None);
        response.SetResult(Definition);

        await Task.WhenAll(first, second);
        Assert.Equal(1, requests);
        Assert.Equal(2, Assert.Single(store.Entries).Lookups);
    }

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
        foreach (var directory in Directory.GetDirectories(_directory)) Directory.Delete(directory);
        Directory.Delete(_directory);
    }
}
