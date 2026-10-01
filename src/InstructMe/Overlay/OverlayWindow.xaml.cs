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
using InstructMe.Vocabulary;

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
    private readonly VocabularyStore _vocabulary;
    private readonly Action _openVocabulary;
    private readonly XInputPoller _gamepad = new();

    private DetectedText? _text;
    private WordSelection? _selection;
    private WordDefinition? _definition;
    private CancellationTokenSource? _lookup;
    private CancellationTokenSource? _ocr;
    private bool _closed;
    private bool _detecting;
    private bool _regionSelecting;
    private Point? _regionStart;
    private Int32Rect? _region;
    private LookupText? _correction;
    private Rect _correctionBounds;
    private (string Phrase, string Sentence)? _activeLookup;
    private string _glassSignature = "";

    internal OverlayWindow(CapturedFrame frame, TextDetector detector, DefinitionService definitions,
        Pronouncer pronouncer, IntPtr previousWindow, VocabularyStore vocabulary, Action openVocabulary)
    {
        _frame = frame;
        _detector = detector;
        _definitions = definitions;
        _pronouncer = pronouncer;
        _previousWindow = previousWindow;
        _vocabulary = vocabulary;
        _openVocabulary = openVocabulary;

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
        StatusPill.SizeChanged += (_, _) => PlaceChrome();
        RecoveryToolbar.SizeChanged += (_, _) => PlaceChrome();
        HintBar.SizeChanged += (_, _) => PlaceChrome();
        CorrectionEditor.SizeChanged += (_, _) => PlaceChrome();
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

        await ScanAsync();
    }

    private async Task ScanAsync(Int32Rect? region = null)
    {
        _ocr?.Cancel();
        using var scan = new CancellationTokenSource();
        _ocr = scan;
        HideEditor();
        HideCard();
        _selection = null;
        _correction = null;
        SelectionBox.Visibility = Visibility.Collapsed;
        _detecting = true;
        WordLayer.IsHitTestVisible = false;
        StatusText.Text = region is null ? "Détection du texte…" : "Relecture de la zone…";
        try
        {
            var text = await _detector.DetectAsync(_frame.Image, region, scan.Token);
            if (_closed || scan.IsCancellationRequested) return;
            _text = text;
            _region = region;
            WordLayer.Children.Clear();
            WordLayer.Children.Add(SelectionBox);
            foreach (var word in text.Words) AddWordTarget(word);
            StatusText.Text = text.Words.Count == 0
                ? "Aucun texte détecté · Relisez une zone ou saisissez le texte"
                : region is null ? "Lecture · Capture figée" : "Lecture · Zone relue";
            if (!_detector.IsEnglish) StatusText.Text += $" · OCR anglais absent ({_detector.LanguageTag})";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed && !scan.IsCancellationRequested)
                StatusText.Text = $"OCR indisponible : {ex.Message} · Vous pouvez saisir le texte";
        }
        finally
        {
            if (_ocr == scan)
            {
                _ocr = null;
                _detecting = false;
                WordLayer.IsHitTestVisible = true;
            }
        }
    }

    private void OnRegionClick(object sender, RoutedEventArgs e) => BeginRegionSelection();
    private async void OnFullScanClick(object sender, RoutedEventArgs e)
    {
        CancelRegionSelection();
        await ScanAsync();
    }

    private void BeginRegionSelection()
    {
        if (_regionSelecting) { CancelRegionSelection(); return; }
        _ocr?.Cancel();
        HideEditor();
        HideCard();
        _regionSelecting = true;
        RegionLayer.Visibility = Visibility.Visible;
        StatusText.Text = "Dessinez un rectangle autour du texte · Échap : annuler";
    }

    private void CancelRegionSelection()
    {
        _regionSelecting = false;
        _regionStart = null;
        RegionLayer.ReleaseMouseCapture();
        RegionLayer.Visibility = RegionBox.Visibility = Visibility.Collapsed;
        StatusText.Text = "Lecture · Capture figée";
    }

    private void OnRegionMouseDown(object sender, MouseButtonEventArgs e)
    {
        _regionStart = e.GetPosition(ImageSpace);
        RegionLayer.CaptureMouse();
        RegionBox.Width = RegionBox.Height = 0;
        Canvas.SetLeft(RegionBox, _regionStart.Value.X);
        Canvas.SetTop(RegionBox, _regionStart.Value.Y);
        RegionBox.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void OnRegionMouseMove(object sender, MouseEventArgs e)
    {
        if (_regionStart is not { } start) return;
        var end = e.GetPosition(ImageSpace);
        Canvas.SetLeft(RegionBox, Math.Min(start.X, end.X));
        Canvas.SetTop(RegionBox, Math.Min(start.Y, end.Y));
        RegionBox.Width = Math.Abs(end.X - start.X);
        RegionBox.Height = Math.Abs(end.Y - start.Y);
    }

    private async void OnRegionMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_regionStart is not { } start) return;
        var region = OcrRegion.FromPoints(start, e.GetPosition(ImageSpace), _frame.Image.PixelWidth, _frame.Image.PixelHeight);
        _regionStart = null;
        RegionLayer.ReleaseMouseCapture();
        e.Handled = true;
        if (region is null)
        {
            RegionBox.Visibility = Visibility.Collapsed;
            StatusText.Text = "Zone trop petite · Dessinez un rectangle autour du texte";
            return;
        }
        CancelRegionSelection();
        await ScanAsync(region);
    }

    private void OnCorrectClick(object sender, RoutedEventArgs e) => ShowEditor();
    private void OnVocabularyClick(object sender, RoutedEventArgs e) => _openVocabulary();
    private void OnRetryClick(object sender, RoutedEventArgs e) => OpenCard();
    private void OnCancelCorrectionClick(object sender, RoutedEventArgs e) => HideEditor();

    private void ShowEditor()
    {
        _ocr?.Cancel();
        CancelRegionSelection();
        var text = _correction ?? LookupText.ForCorrection(_text, _selection, _region is not null);
        PhraseBox.Text = text.Phrase;
        ContextBox.Text = text.Sentence;
        _correctionBounds = _text is not null && _selection is { } selected ? selected.Bounds(_text)
            : _region is { } r ? new Rect(r.X, r.Y, r.Width, r.Height)
            : new Rect(_frame.Image.PixelWidth / 2.0, _frame.Image.PixelHeight / 2.0, 1, 1);
        HideCard();
        CorrectionError.Text = "";
        CorrectionEditor.Visibility = Visibility.Visible;
        PlaceChrome();
        PhraseBox.Focus();
        PhraseBox.SelectAll();
    }

    private void HideEditor()
    {
        CorrectionEditor.Visibility = Visibility.Collapsed;
        Keyboard.Focus(this);
    }

    private void OnTranslateCorrectionClick(object sender, RoutedEventArgs e) => TranslateCorrection();

    private void TranslateCorrection()
    {
        var text = LookupText.Create(PhraseBox.Text, ContextBox.Text, out var error);
        CorrectionError.Text = error ?? "";
        if (text is null) return;
        _correction = text;
        HideEditor();
        OpenCard();
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
        _correction = null;
        SelectionBox.Visibility = Visibility.Collapsed;
        HideCard();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (CorrectionEditor.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) { HideEditor(); e.Handled = true; }
            else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                TranslateCorrection();
                e.Handled = true;
            }
            return;
        }
        if (_regionSelecting)
        {
            if (e.Key is Key.Escape or Key.R) { CancelRegionSelection(); e.Handled = true; }
            return;
        }
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Enter or Key.Space: OpenCard(); break;
            case Key.P when shift: SpeakSentence(); break;
            case Key.P: SpeakWord(); break;
            case Key.R: BeginRegionSelection(); break;
            case Key.E: ShowEditor(); break;
            case Key.H: _openVocabulary(); break;
            case Key.F5: OpenCard(); break;
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
        if (CorrectionEditor.Visibility == Visibility.Visible || _regionSelecting)
        {
            if (action == GamepadAction.Cancel) { HideEditor(); CancelRegionSelection(); }
            return;
        }
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
        if (_detecting || _text is null || _text.Words.Count == 0) return;

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
        _correction = null;
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
        if ((_detecting && _correction is null) || _regionSelecting) return;
        var text = _correction ?? (_text is not null && _selection is { } selection
            ? new LookupText(selection.Phrase(_text), ContextBuilder.Sentence(_text, selection.LineIndex)) : null);
        if (text is null) return;
        var phrase = text.Phrase;
        if (phrase.Length == 0) return;
        var sentence = text.Sentence;
        if (_activeLookup == (phrase, sentence) && Card.Visibility == Visibility.Visible
            && (_lookup is not null || _definition is not null)) return;

        _lookup?.Cancel();
        using var lookup = new CancellationTokenSource();
        _lookup = lookup;
        _activeLookup = (phrase, sentence);
        _definition = null;

        CardMeta.Text = "ANGLAIS";
        CardWord.Text = phrase;
        CardIpa.Text = "";
        CardIpa.Visibility = Visibility.Collapsed;
        CardTranslation.Text = "…";
        CardMeaning.Text = "Recherche du sens dans ce contexte…";
        SavedText.Text = "";
        RetryButton.Visibility = Visibility.Collapsed;
        Card.Visibility = Visibility.Visible;
        PlaceCard();

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
            SavedText.Text = _vocabulary.SaveError ?? "Enregistré dans Mon vocabulaire · H pour le retrouver";
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
                Anthropic.Exceptions.AnthropicForbiddenException => "Cette clé n’a pas accès au service. Vérifiez les réglages.",
                Anthropic.Exceptions.AnthropicNotFoundException => "Ce modèle est indisponible. Choisissez un autre modèle dans les réglages.",
                System.Net.Http.HttpRequestException => "Pas de connexion au service de définitions.",
                TimeoutException => "Le service met trop de temps à répondre. Réessayez dans un instant.",
                System.Text.Json.JsonException => "La réponse du service est incomplète. Réessayez.",
                InvalidOperationException => ex.Message,
                _ => "La définition n’a pas pu être obtenue. Réessayez.",
            };
            RetryButton.Visibility = Visibility.Visible;
        }
        finally
        {
            if (_lookup == lookup) _lookup = null;
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
        if (_correction is not null) { Speak(_definition?.Word ?? _correction.Phrase); return; }
        if (_text is null || _selection is not { } selection) return;
        // The word corrected by Claude is better than the raw OCR text.
        Speak(_definition?.Word ?? selection.Phrase(_text));
    }

    private void SpeakSentence()
    {
        if (_correction is not null) { Speak(_correction.Sentence); return; }
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
        if (w < 32 || h < 32) return;
        StatusPill.MaxWidth = HintBar.MaxWidth = RecoveryToolbar.MaxWidth = w - 32;
        Card.MaxWidth = w - 32;
        Card.MaxHeight = Math.Max(160, h - 160);
        CorrectionEditor.MaxWidth = w - 32;
        CorrectionEditor.MaxHeight = Math.Max(160, h - 200);
        StatusPill.Measure(new Size(w - 32, double.PositiveInfinity));
        HintBar.Measure(new Size(w - 32, double.PositiveInfinity));
        RecoveryToolbar.Measure(new Size(w - 32, double.PositiveInfinity));
        CorrectionEditor.Measure(new Size(w - 32, CorrectionEditor.MaxHeight));
        Canvas.SetLeft(StatusPill, (w - StatusPill.DesiredSize.Width) / 2);
        Canvas.SetTop(StatusPill, 28);
        Canvas.SetLeft(RecoveryToolbar, (w - RecoveryToolbar.DesiredSize.Width) / 2);
        Canvas.SetTop(RecoveryToolbar, 78);
        Canvas.SetLeft(CorrectionEditor, (w - CorrectionEditor.DesiredSize.Width) / 2);
        Canvas.SetTop(CorrectionEditor, Math.Max(140, (h - CorrectionEditor.DesiredSize.Height) / 2));
        Canvas.SetLeft(HintBar, (w - HintBar.DesiredSize.Width) / 2);
        Canvas.SetTop(HintBar, h - HintBar.DesiredSize.Height - 36);
        PlaceCard();
    }

    private void PlaceCard()
    {
        if (Card.Visibility != Visibility.Visible) return;
        var bounds = _text is not null && _selection is { } selection ? selection.Bounds(_text) : _correctionBounds;
        var sentence = _text is not null && _selection is { } current
            ? ContextBuilder.Bounds(_text, current.LineIndex) : bounds;
        var position = CardPlacement.Place(
            ToWindow(bounds),
            ToWindow(sentence),
            new Size(Card.ActualWidth > 0 ? Card.ActualWidth : Card.Width, Card.ActualHeight > 0 ? Card.ActualHeight : 260),
            new Size(UiLayer.ActualWidth, UiLayer.ActualHeight));
        Canvas.SetLeft(Card, position.X);
        Canvas.SetTop(Card, position.Y);
    }

    /// <summary>Clips the blurred layer to the shape of each visible glass panel.</summary>
    private void UpdateGlass()
    {
        var panels = new[] { StatusPill, HintBar, Card, RecoveryToolbar, CorrectionEditor }
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
        _closed = true;
        _ocr?.Cancel();
        _lookup?.Cancel();
        _pronouncer.Stop();
        _gamepad.Dispose();
        // Give focus back while this window still owns the foreground, so Windows allows it.
        if (_previousWindow != IntPtr.Zero && NativeMethods.IsWindow(_previousWindow))
        {
            Topmost = false;
            NativeMethods.ForceForeground(_previousWindow);
        }
    }
}
