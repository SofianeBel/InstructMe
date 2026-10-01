using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InstructMe.Definitions;

namespace InstructMe.Vocabulary;

public partial class VocabularyWindow : Window
{
    private readonly VocabularyStore _store;
    private Pronouncer _pronouncer;
    private bool _ready;

    internal VocabularyWindow(VocabularyStore store, Pronouncer pronouncer, string shortcut)
    {
        _store = store;
        _pronouncer = pronouncer;
        InitializeComponent();
        _ready = true;
        UpdatePronouncer(pronouncer);
        UpdateShortcut(shortcut);
        _store.Changed += OnStoreChanged;
        Closed += (_, _) => { _store.Changed -= OnStoreChanged; _pronouncer.Stop(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Refresh();
    }

    internal void UpdatePronouncer(Pronouncer pronouncer)
    {
        _pronouncer = pronouncer;
        SpeakWordButton.IsEnabled = SpeakContextButton.IsEnabled = pronouncer.IsAvailable;
    }

    internal void UpdateShortcut(string shortcut) => HotkeyText.Text = $"En jeu : {shortcut} pour ouvrir la lecture.";

    private void OnStoreChanged()
    {
        if (Dispatcher.CheckAccess()) Refresh();
        else Dispatcher.InvokeAsync(Refresh);
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e) { if (_ready) Refresh(); }
    private void OnSessionChanged(object sender, SelectionChangedEventArgs e) { if (_ready) Refresh(); }

    private void Refresh()
    {
        var selected = WordList.SelectedItem as VocabularyEntry;
        var entries = _store.Search(SearchBox.Text, SessionFilter.SelectedIndex == 1);
        WordList.ItemsSource = entries;
        WordList.SelectedItem = entries.FirstOrDefault(e => selected is not null && e.Phrase == selected.Phrase
            && e.Sentence == selected.Sentence && e.Model == selected.Model) ?? entries.FirstOrDefault();
        int sessionCount = _store.Search("", currentSession: true).Count;
        CountText.Text = $"{entries.Count} traduction{(entries.Count > 1 ? "s" : "")} · {sessionCount} consultée{(sessionCount > 1 ? "s" : "")} cette session";
        EmptyText.Text = SearchBox.Text.Trim().Length > 0 ? "Aucun mot ne correspond à cette recherche."
            : SessionFilter.SelectedIndex == 1 ? "Les mots expliqués pendant cette session apparaîtront ici."
            : "Expliquez un mot ou une expression dans la lecture pour commencer votre carnet.";
        EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = _store.SaveError ?? _store.LoadWarning
            ?? "Les traductions sont enregistrées automatiquement sur cet ordinateur.";
    }

    private void OnWordChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        Detail.DataContext = WordList.SelectedItem;
        Detail.Visibility = WordList.SelectedItem is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSpeakWord(object sender, RoutedEventArgs e)
    {
        if (WordList.SelectedItem is VocabularyEntry entry) Speak(entry.Definition.Word);
    }

    private void OnSpeakSentence(object sender, RoutedEventArgs e)
    {
        if (WordList.SelectedItem is VocabularyEntry entry) Speak(entry.Sentence);
    }

    private async void Speak(string text)
    {
        try { await _pronouncer.SpeakAsync(text); }
        catch (OperationCanceledException) { }
        catch { StatusText.Text = "Voix indisponible."; }
    }
}
