using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace InstructMe.Text;

/// <summary>Finds words on a captured image with the OCR engine built into Windows.</summary>
internal sealed class TextDetector
{
    // Game UI text is often small. Upscaling helps the OCR engine, up to its size limit.
    private const double MaxUpscale = 2.0;

    private readonly OcrEngine _engine;
    private readonly SemaphoreSlim _recognition = new(1, 1);

    public string LanguageTag { get; }
    public bool IsEnglish { get; }

    private TextDetector(OcrEngine engine)
    {
        _engine = engine;
        LanguageTag = engine.RecognizerLanguage.LanguageTag;
        IsEnglish = LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase);
    }

    public static TextDetector Create()
    {
        var english = OcrEngine.AvailableRecognizerLanguages
            .FirstOrDefault(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        var engine = (english is not null ? OcrEngine.TryCreateFromLanguage(english) : null)
            ?? OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("No Windows OCR language is installed.");
        return new TextDetector(engine);
    }

    public async Task<DetectedText> DetectAsync(BitmapSource image, Int32Rect? region = null,
        CancellationToken cancellationToken = default)
    {
        await _recognition.WaitAsync(cancellationToken);
        try
        {
            var area = region ?? new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight);
            BitmapSource source = region is null ? image : new CroppedBitmap(image, area);
            double scale = Math.Min(MaxUpscale, OcrEngine.MaxImageDimension / (double)Math.Max(source.PixelWidth, source.PixelHeight));
            using var bitmap = ToSoftwareBitmap(source, scale);
            // Finish the native operation before releasing its bitmap or starting another scan.
            var result = await _engine.RecognizeAsync(bitmap);
            cancellationToken.ThrowIfCancellationRequested();

            return DetectedText.FromLines(result.Lines.Select(line => line.Words.Select(word =>
            {
                var r = word.BoundingRect;
                return (word.Text, OcrRegion.ToCapture(new Rect(r.X, r.Y, r.Width, r.Height), scale, area));
            })));
        }
        finally { _recognition.Release(); }
    }

    private static SoftwareBitmap ToSoftwareBitmap(BitmapSource image, double scale)
    {
        BitmapSource source = Math.Abs(scale - 1) < 0.01
            ? image
            : new TransformedBitmap(image, new ScaleTransform(scale, scale));
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);

        return SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8,
            bgra.PixelWidth, bgra.PixelHeight, BitmapAlphaMode.Premultiplied);
    }
}
