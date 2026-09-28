using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;
using InstructMe.Native;

namespace InstructMe.Capture;

/// <summary>A frozen image of one monitor, in physical pixels.</summary>
internal sealed record CapturedFrame(BitmapSource Image, Int32Rect ScreenBounds, string Method);

/// <summary>
/// Captures a monitor with Windows Graphics Capture (works with most borderless games),
/// and falls back to a GDI screen copy when that API is not available.
/// </summary>
internal static class ScreenCapture
{
    public static async Task<CapturedFrame> CaptureMonitorAsync(IntPtr monitor)
    {
        var bounds = NativeMethods.GetMonitorBounds(monitor);

        if (GraphicsCaptureSession.IsSupported())
        {
            try
            {
                var image = await CaptureWithGraphicsCaptureAsync(monitor);
                return new CapturedFrame(image, bounds, "Windows Graphics Capture");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Graphics Capture failed, using GDI: {ex}");
            }
        }

        return new CapturedFrame(CaptureWithGdi(bounds), bounds, "GDI");
    }

    private static async Task<BitmapSource> CaptureWithGraphicsCaptureAsync(IntPtr monitor)
    {
        try { await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless); }
        catch { /* Older Windows builds: the yellow border stays, capture still works. */ }

        var item = CreateItemForMonitor(monitor);
        using var device = Direct3D.CreateDevice();
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        try { session.IsBorderRequired = false; } catch { }

        var arrived = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        pool.FrameArrived += (p, _) =>
        {
            var frame = p.TryGetNextFrame();
            if (frame is not null && !arrived.TrySetResult(frame)) frame.Dispose();
        };
        session.StartCapture();

        using var captured = await arrived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var software = await SoftwareBitmap.CreateCopyFromSurfaceAsync(captured.Surface, BitmapAlphaMode.Ignore);
        return ToBitmapSource(software);
    }

    private static BitmapSource ToBitmapSource(SoftwareBitmap bitmap)
    {
        int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
        int stride;
        using (var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read))
            stride = buffer.GetPlaneDescription(0).Stride;

        var pixels = new byte[stride * height];
        bitmap.CopyToBuffer(pixels.AsBuffer());

        // Bgr32 ignores alpha: some games write alpha = 0 into their swap chain.
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
        source.Freeze();
        return source;
    }

    private static BitmapSource CaptureWithGdi(Int32Rect bounds)
    {
        using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new System.Drawing.Size(bounds.Width, bounds.Height));

        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bounds.Width, bounds.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);
        try
        {
            var source = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Bgr32, null,
                data.Scan0, data.Stride * bounds.Height, data.Stride);
            source.Freeze();
            return source;
        }
        finally { bitmap.UnlockBits(data); }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    private static GraphicsCaptureItem CreateItemForMonitor(IntPtr monitor)
    {
        var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"); // IGraphicsCaptureItem
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        IntPtr pointer = interop.CreateForMonitor(monitor, ref iid);
        try { return GraphicsCaptureItem.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    /// <summary>Creates the WinRT Direct3D device that Graphics Capture needs.</summary>
    private static class Direct3D
    {
        private static readonly Guid IdxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

        [DllImport("d3d11.dll", ExactSpelling = true)]
        private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags,
            IntPtr featureLevels, uint featureLevelCount, uint sdkVersion,
            out IntPtr device, out int featureLevel, out IntPtr immediateContext);

        [DllImport("d3d11.dll", ExactSpelling = true)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        public static IDirect3DDevice CreateDevice()
        {
            const int hardware = 1, warp = 5;
            const uint bgraSupport = 0x20, sdkVersion = 7;

            int hr = D3D11CreateDevice(IntPtr.Zero, hardware, IntPtr.Zero, bgraSupport, IntPtr.Zero, 0, sdkVersion,
                out var d3dDevice, out _, out var context);
            if (hr < 0)
                hr = D3D11CreateDevice(IntPtr.Zero, warp, IntPtr.Zero, bgraSupport, IntPtr.Zero, 0, sdkVersion,
                    out d3dDevice, out _, out context);
            Marshal.ThrowExceptionForHR(hr);

            try
            {
                var iid = IdxgiDevice;
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, in iid, out var dxgiDevice));
                try
                {
                    Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable));
                    try { return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable); }
                    finally { Marshal.Release(inspectable); }
                }
                finally { Marshal.Release(dxgiDevice); }
            }
            finally
            {
                Marshal.Release(context);
                Marshal.Release(d3dDevice);
            }
        }
    }
}
