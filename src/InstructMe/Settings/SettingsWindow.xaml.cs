using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using InstructMe.Definitions;
using InstructMe.Input;

namespace InstructMe.Settings;

/// <summary>
/// Edits the user settings. The app applies them with <c>apply</c>, which returns
/// an error message when it cannot, so the window stays open and shows it.
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly (string Id, string Name)[] Models =
    [
        ("claude-haiku-4-5", "Claude Haiku 4.5 · rapide et économique"),
        ("claude-sonnet-5-5", "Claude Sonnet 5.5 · équilibré"),
        ("claude-opus-5-5", "Claude Opus 5.5 · le plus précis"),
    ];

    private static readonly (string Id, string Name)[] Voices =
    [
        ("en-US-EmmaMultilingualNeural", "Emma · américain"),
        ("en-US-AvaMultilingualNeural", "Ava · américain"),
        ("en-US-AndrewMultilingualNeural", "Andrew · américain"),
        ("en-US-BrianMultilingualNeural", "Brian · américain"),
        ("en-GB-SoniaNeural", "Sonia · britannique"),
        ("en-GB-RyanNeural", "Ryan · britannique"),
    ];

    private readonly AppSettings _original;
    private readonly Func<AppSettings, string?> _apply;
    private string _hotkey = "";
    private CancellationTokenSource? _test;

    internal SettingsWindow(AppSettings current, Func<AppSettings, string?> apply)
    {
        _original = current;
        _apply = apply;
        InitializeComponent();
        ShowSettings(current, includeKey: true);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !HotkeyBox.IsKeyboardFocused && !ModelBox.IsDropDownOpen && !VoiceBox.IsDropDownOpen)
                Close();
        };
        Closed += (_, _) => _test?.Cancel();
    }

    private void ShowSettings(AppSettings settings, bool includeKey)
    {
        _hotkey = settings.Hotkey;
        HotkeyBox.Text = settings.Hotkey;
        LaunchAtStartupToggle.IsChecked = settings.LaunchAtStartup;

        Fill(ModelBox, Models, settings.Model);

        bool neural = !string.IsNullOrWhiteSpace(settings.Voice);
        Fill(VoiceBox, Voices, neural ? settings.Voice : Voices[0].Id);
        NeuralVoiceToggle.IsChecked = neural;
        OnNeuralVoiceChanged(this, new RoutedEventArgs());

        if (includeKey) ApiKeyBox.Password = settings.AnthropicApiKey ?? "";
        UpdateKeyStatus();
        SaveError.Text = "";
    }

    /// <summary>Lists the known values, plus the current one when it is custom (set in settings.json).</summary>
    private static void Fill(ComboBox box, (string Id, string Name)[] known, string current)
    {
        box.Items.Clear();
        foreach (var (id, name) in known) box.Items.Add(new ComboBoxItem { Content = name, Tag = id });
        if (!known.Any(k => k.Id == current)) box.Items.Add(new ComboBoxItem { Content = current, Tag = current });
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == current);
    }

    private static string SelectedId(ComboBox box) => (string)((ComboBoxItem)box.SelectedItem).Tag;

    private AppSettings Collect()
    {
        var next = _original.Clone();
        next.Hotkey = _hotkey;
        next.LaunchAtStartup = LaunchAtStartupToggle.IsChecked == true;
        next.Model = SelectedId(ModelBox);
        next.Voice = NeuralVoiceToggle.IsChecked == true ? SelectedId(VoiceBox) : "";
        next.AnthropicApiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? null : ApiKeyBox.Password.Trim();
        return next;
    }

    // ---- Shortcut ----

    private void OnHotkeyFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyBox.Text = "Appuyez sur les touches…";
        HotkeyHint.Text = "Utilisez Ctrl, Alt, Maj ou Win avec une touche. Échap : annuler.";
    }

    private void OnHotkeyBlur(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyBox.Text = _hotkey;
        HotkeyHint.Text = "Ouvre et ferme la lecture. Un Stream Deck peut envoyer le même raccourci.";
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        // With Alt, WPF reports the real key in SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape || key == Key.Tab)
        {
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(this, this);
            if (key == Key.Tab) MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed)
            return;

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            HotkeyBox.Text = "Ajoutez Ctrl, Alt, Maj ou Win.";
            return;
        }

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        var text = string.Join("+", parts);

        try
        {
            HotkeyGesture.Parse(text);
        }
        catch (FormatException)
        {
            HotkeyBox.Text = "Touche non prise en charge.";
            return;
        }

        _hotkey = text;
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(this, this);
    }

    // ---- Voice ----

    private void OnNeuralVoiceChanged(object sender, RoutedEventArgs e)
    {
        bool neural = NeuralVoiceToggle.IsChecked == true;
        VoiceBox.IsEnabled = neural;
        VoiceHint.Text = neural
            ? "Voix Microsoft gratuite. Sans Internet, la voix de Windows prend le relais."
            : "Seules les voix anglaises installées dans Windows sont utilisées.";
    }

    // ---- Claude API ----

    private void OnApiKeyChanged(object sender, RoutedEventArgs e)
    {
        UpdateKeyStatus();
        HideTestResult();
    }

    private void OnModelChanged(object sender, SelectionChangedEventArgs e) => HideTestResult();

    private void UpdateKeyStatus()
    {
        bool saved = !string.IsNullOrWhiteSpace(ApiKeyBox.Password);

        if (saved)
        {
            SetBadge("Clé saisie", "#1F1E8E3E", "#1E6E34");
            KeyHint.Text = "Cette clé a priorité sur ANTHROPIC_API_KEY. Elle reste sur cet ordinateur et n'est envoyée qu'à Anthropic.";
        }
        else if (AppSettings.EnvironmentApiKey() is not null)
        {
            SetBadge("Variable d'environnement", "#1A0A84FF", "#0A5CC2");
            KeyHint.Text = "ANTHROPIC_API_KEY est utilisée. Entrez une clé ci-dessous pour la remplacer dans InstructMe.";
        }
        else
        {
            SetBadge("Aucune clé", "#1FC4302B", "#A3261F");
            KeyHint.Text = "Sans clé, InstructMe ne peut pas expliquer les mots. Créez une clé sur console.anthropic.com.";
        }
    }

    private void SetBadge(string text, string fill, string ink)
    {
        KeyBadgeText.Text = text;
        KeyBadge.Background = (Brush)new BrushConverter().ConvertFromString(fill)!;
        KeyBadgeText.Foreground = (Brush)new BrushConverter().ConvertFromString(ink)!;
    }

    private async void OnTestClick(object sender, RoutedEventArgs e)
    {
        var candidate = Collect();
        var apiKey = candidate.ResolveApiKey();
        if (apiKey is null)
        {
            ShowTestResult("Entrez d'abord une clé API.", ok: false);
            return;
        }

        _test?.Cancel();
        var test = _test = new CancellationTokenSource();
        TestButton.IsEnabled = false;
        ShowTestResult("Connexion à Claude…", ok: null);
        try
        {
            var error = await DefinitionService.TestAsync(apiKey, candidate.Model, test.Token);
            ShowTestResult(error ?? $"Connexion réussie avec {candidate.Model}.", ok: error is null);
        }
        catch (OperationCanceledException)
        {
            // The window was closed, or a new test started.
        }
        finally
        {
            if (_test == test) TestButton.IsEnabled = true;
        }
    }

    private void ShowTestResult(string text, bool? ok)
    {
        TestResult.Text = ok switch { true => "✓  " + text, false => "✕  " + text, null => text };
        TestResult.Foreground = (Brush)FindResource(ok switch { true => "Success", false => "Danger", null => "InkSoft" });
        TestResult.Visibility = Visibility.Visible;
    }

    private void HideTestResult() => TestResult.Visibility = Visibility.Collapsed;

    // ---- Footer ----

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        // The key is personal: a reset of the preferences keeps it.
        ShowSettings(new AppSettings(), includeKey: false);
        HideTestResult();
    }

    private void OnOpenFileClick(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(AppSettings.FilePath) { UseShellExecute = true });

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var error = _apply(Collect());
        if (error is null) Close();
        else SaveError.Text = error;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
