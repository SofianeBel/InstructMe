using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InstructMe.Capture;
using InstructMe.Definitions;
using InstructMe.Input;
using InstructMe.Native;
using InstructMe.Text;

namespace InstructMe.Overlay;

/// <summary>
/// Full-screen reading mode: shows the frozen capture, lets the player pick words
/// with the mouse, keyboard, or a controller, and shows the definition card.
/// </summary>
public partial class OverlayWindow : Window
{
    private static readonly Brush HoverFill = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
    private static readonly Brush HoverEdge = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));
    private static readonly Brush NoHitFill = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0));

    private readonly CapturedFrame _frame;
    private readonly TextDetector _detector;
    private readonly DefinitionService _definitions;
    private readonly Pronouncer _pronouncer;
    private readonly IntPtr _previousWindow;
    private readonly XInputPoller _gamepad = new();

    private DetectedText? _text;
    private WordSelection? _selection;
    private WordDefinition? _definition;
    private CancellationTokenSource? _lookup;
    private string _glassSignature = "";

    internal OverlayWindow(CapturedFrame frame, TextDetector detector, DefinitionService definitions,
        Pronouncer pronouncer, IntPtr previousWindow)
    {
        _frame = frame;
        _detector = detector;
        _definitions = definitions;
        _pronouncer = pronouncer;
        _previousWindow = previousWindow;

        InitializeComponent();

        FrozenImage.Source = frame.Image;
        // A small copy is enough for a strong blur, and it is much cheaper to render.
        var small = new TransformedBitmap(frame.Image, new ScaleTransform(0.25, 0.25));
        small.Freeze();
        BlurredImage.Source = small;
        BlurredImage.Width = frame.Image.PixelWidth;
        BlurredImage.Height = frame.Image.PixelHeight;
        BlurredImage.Stretch = Stretch.Fill;
        GlassBackdrop.Clip = Geometry.Empty;
        SpeakButton.IsEnabled = pronouncer.IsAvailable;
        SpeakSentenceButton.IsEnabled = pronouncer.IsAvailable;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnKeyDown;
        LayoutUpdated += (_, _) => UpdateGlass();
        UiLayer.SizeChanged += (_, _) => PlaceChrome();
        Card.SizeChanged += (_, _) => PlaceCard();
        _gamepad.Action += OnGamepadAction;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Cover the captured monitor exactly, in physical pixels.
        var hwnd = new WindowInteropHelper(this).Handle;
        var b = _frame.ScreenBounds;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, NativeMethods.SWP_SHOWWINDOW);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);
        Activate();
        Keyboard.Focus(this);
        _gamepad.Start();

        StatusText.Text = "Détection du texte…";
        try
        {
            _text = await _detector.DetectAsync(_frame.Image);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"OCR indisponible : {ex.Message}";
            return;
        }

        foreach (var word in _text.Words) AddWordTarget(word);

        StatusText.Text = _text.Words.Count == 0 ? "Aucun texte détecté" : "Lecture · Capture figée";
        if (!_detector.IsEnglish)
            StatusText.Text += $"  ·  OCR anglais absent ({_detector.LanguageTag})";
    }

    private void AddWordTarget(DetectedWord word)
    {
        var target = new Border
        {
            Width = word.Bounds.Width + 6,
            Height = word.Bounds.Height + 6,
            CornerRadius = new CornerRadius(5),
            Background = NoHitFill,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Tag = word,
        };
        Canvas.SetLeft(target, word.Bounds.X - 3);
        Canvas.SetTop(target, word.Bounds.Y - 3);
        target.MouseEnter += (_, _) => { target.Background = HoverFill; target.BorderBrush = HoverEdge; };
        target.MouseLeave += (_, _) => { target.Background = NoHitFill; target.BorderBrush = null; };
        target.MouseLeftButtonDown += OnWordClick;
        WordLayer.Children.Insert(0, target); // keep the selection box on top
    }

    private void OnWordClick(object sender, MouseButtonEventArgs e)
    {
        if (_text is null || sender is not FrameworkElement { Tag: DetectedWord word }) return;
        e.Handled = true;

        bool extend = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _selection is not null;
        Select(extend ? _selection!.Value.ExtendTo(word, _text) : WordSelection.Of(word, _text));
        OpenCard();
    }

    private void OnBackgroundClick(object sender, MouseButtonEventArgs e)
    {
        _selection = null;
        SelectionBox.Visibility = Visibility.Collapsed;
        HideCard();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Enter or Key.Space: OpenCard(); break;
            case Key.P when shift: SpeakSentence(); break;
            case Key.P: SpeakWord(); break;
            case Key.Right when shift: ExtendSelection(); break;
            case Key.Left when shift: ShrinkSelection(); break;
            case Key.Up: Navigate(NavDirection.Up); break;
            case Key.Down: Navigate(NavDirection.Down); break;
            case Key.Left: Navigate(NavDirection.Left); break;
            case Key.Right: Navigate(NavDirection.Right); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnGamepadAction(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.Cancel: Close(); break;
            case GamepadAction.Confirm: OpenCard(); break;
            case GamepadAction.Extend: ExtendSelection(); break;
            case GamepadAction.Shrink: ShrinkSelection(); break;
            case GamepadAction.SpeakWord: SpeakWord(); break;
            case GamepadAction.SpeakSentence: SpeakSentence(); break;
            case GamepadAction.Up: Navigate(NavDirection.Up); break;
            case GamepadAction.Down: Navigate(NavDirection.Down); break;
            case GamepadAction.Left: Navigate(NavDirection.Left); break;
            case GamepadAction.Right: Navigate(NavDirection.Right); break;
        }
    }

    private void Navigate(NavDirection direction)
    {
        if (_text is null || _text.Words.Count == 0) return;

        if (_selection is not { } current)
        {
            var center = new Point(_frame.Image.PixelWidth / 2.0, _frame.Image.PixelHeight / 2.0);
            Select(WordSelection.Of(WordNavigator.Nearest(_text, center)!, _text));
            return;
        }

        var from = direction == NavDirection.Right ? current.Last(_text) : current.First(_text);
        Select(WordSelection.Of(WordNavigator.Move(_text, from, direction), _text));
        HideCard(); // A opens the card for the new word
    }

    private void ExtendSelection()
    {
        if (_text is null || _selection is not { } current) return;
        Select(current.Extend(_text));
        if (Card.Visibility == Visibility.Visible) OpenCard();
    }

    private void ShrinkSelection()
    {
        if (_text is null || _selection is not { } current) return;
        Select(current.Shrink());
        if (Card.Visibility == Visibility.Visible) OpenCard();
    }

    private void Select(WordSelection selection)
    {
        if (_text is null) return;
        _selection = selection;
        _definition = null;
        var r = selection.Bounds(_text);
        r.Inflate(5, 4);
        SelectionBox.Width = r.Width;
        SelectionBox.Height = r.Height;
        Canvas.SetLeft(SelectionBox, r.X);
        Canvas.SetTop(SelectionBox, r.Y);
        SelectionBox.Visibility = Visibility.Visible;
    }

    private async void OpenCard()
    {
        if (_text is null || _selection is not { } selection) return;
        var phrase = selection.Phrase(_text);
        if (phrase.Length == 0) return;
        var sentence = ContextBuilder.Sentence(_text, selection.LineIndex);

        _lookup?.Cancel();
        var lookup = _lookup = new CancellationTokenSource();
        _definition = null;

        CardMeta.Text = "ANGLAIS";
        CardWord.Text = phrase;
        CardIpa.Text = "";
        CardIpa.Visibility = Visibility.Collapsed;
        CardTranslation.Text = "…";
        CardMeaning.Text = _definitions.HasApiKey
            ? "Analyse du contexte…"
            : "Aucune clé API Claude. Ajoutez-la dans les réglages (icône InstructMe de la barre des tâches).";
        Card.Visibility = Visibility.Visible;
        PlaceCard();
        if (!_definitions.HasApiKey) return;

        try
        {
            var definition = await _definitions.DefineAsync(phrase, sentence, lookup.Token);
            if (lookup.IsCancellationRequested) return;

            _definition = definition;
            CardMeta.Text = $"ANGLAIS · {definition.PartOfSpeech.ToUpperInvariant()}";
            CardWord.Text = definition.Word;
            CardIpa.Text = definition.Ipa;
            CardIpa.Visibility = string.IsNullOrWhiteSpace(definition.Ipa) ? Visibility.Collapsed : Visibility.Visible;
            CardTranslation.Text = definition.Translation;
            CardMeaning.Text = definition.ContextMeaning;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (lookup.IsCancellationRequested) return;
            CardTranslation.Text = "Indisponible";
            CardMeaning.Text = ex switch
            {
                Anthropic.Exceptions.AnthropicUnauthorizedException => "Clé API refusée. Vérifiez la clé dans les réglages d'InstructMe.",
                Anthropic.Exceptions.AnthropicRateLimitException => "Trop de demandes. Réessayez dans un instant.",
                System.Net.Http.HttpRequestException => "Pas de connexion au service de définitions.",
                _ => ex.Message,
            };
        }
    }

    private void HideCard()
    {
        _lookup?.Cancel();
        Card.Visibility = Visibility.Collapsed;
    }

    private void OnSpeakClick(object sender, RoutedEventArgs e) => SpeakWord();

    private void OnSpeakSentenceClick(object sender, RoutedEventArgs e) => SpeakSentence();

    private void SpeakWord()
    {
        if (_text is null || _selection is not { } selection) return;
        // The word corrected by Claude is better than the raw OCR text.
        Speak(_definition?.Word ?? selection.Phrase(_text));
    }

    private void SpeakSentence()
    {
        if (_text is null || _selection is not { } selection) return;
        Speak(ContextBuilder.Sentence(_text, selection.LineIndex));
    }

    private async void Speak(string text)
    {
        try { await _pronouncer.SpeakAsync(text); }
        catch (OperationCanceledException) { /* A newer request replaced this one. */ }
        catch { StatusText.Text = "Voix indisponible"; }
    }

    /// <summary>Converts capture pixels to window coordinates (the Viewbox scale).</summary>
    private Rect ToWindow(Rect r)
    {
        double sx = Root.ActualWidth / _frame.Image.PixelWidth;
        double sy = Root.ActualHeight / _frame.Image.PixelHeight;
        return new Rect(r.X * sx, r.Y * sy, r.Width * sx, r.Height * sy);
    }

    private void PlaceChrome()
    {
        double w = UiLayer.ActualWidth, h = UiLayer.ActualHeight;
        StatusPill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        HintBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(StatusPill, (w - StatusPill.DesiredSize.Width) / 2);
        Canvas.SetTop(StatusPill, 28);
        Canvas.SetLeft(HintBar, (w - HintBar.DesiredSize.Width) / 2);
        Canvas.SetTop(HintBar, h - HintBar.DesiredSize.Height - 36);
        PlaceCard();
    }

    private void PlaceCard()
    {
        if (_text is null || _selection is not { } selection || Card.Visibility != Visibility.Visible) return;
        var position = CardPlacement.Place(
            ToWindow(selection.Bounds(_text)),
            ToWindow(ContextBuilder.Bounds(_text, selection.LineIndex)),
            new Size(Card.ActualWidth > 0 ? Card.ActualWidth : Card.Width, Card.ActualHeight > 0 ? Card.ActualHeight : 260),
            new Size(UiLayer.ActualWidth, UiLayer.ActualHeight));
        Canvas.SetLeft(Card, position.X);
        Canvas.SetTop(Card, position.Y);
    }

    /// <summary>Clips the blurred layer to the shape of each visible glass panel.</summary>
    private void UpdateGlass()
    {
        var panels = new[] { StatusPill, HintBar, Card }
            .Where(p => p.IsVisible && p.ActualWidth > 0)
            .Select(p => (Rect: new Rect(Canvas.GetLeft(p), Canvas.GetTop(p), p.ActualWidth, p.ActualHeight), p.CornerRadius.TopLeft))
            .Where(p => !double.IsNaN(p.Rect.X) && !double.IsNaN(p.Rect.Y))
            .ToList();

        var signature = string.Join('|', panels.Select(p => p.Rect.ToString()));
        if (signature == _glassSignature) return;
        _glassSignature = signature;

        var group = new GeometryGroup();
        foreach (var (rect, radius) in panels)
            group.Children.Add(new RectangleGeometry(rect, radius, radius));
        group.Freeze();
        GlassBackdrop.Clip = group;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _lookup?.Cancel();
        _gamepad.Dispose();
        // Give focus back while this window still owns the foreground, so Windows allows it.
        if (_previousWindow != IntPtr.Zero && NativeMethods.IsWindow(_previousWindow))
        {
            Topmost = false;
            NativeMethods.ForceForeground(_previousWindow);
        }
    }
}
