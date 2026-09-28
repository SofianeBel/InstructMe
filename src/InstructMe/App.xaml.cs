using System.Diagnostics;
using System.IO;
using System.Windows;
using InstructMe.Capture;
using InstructMe.Definitions;
using InstructMe.Input;
using InstructMe.Native;
using InstructMe.Overlay;
using InstructMe.Text;
using Forms = System.Windows.Forms;

namespace InstructMe;

/// <summary>
/// Background app: waits for the global shortcut, captures the monitor of the active
/// window, and opens the reading overlay on it.
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    private GlobalHotkey? _hotkey;
    private Forms.NotifyIcon? _tray;
    private TextDetector? _detector;
    private DefinitionService? _definitions;
    private Pronouncer? _pronouncer;
    private OverlayWindow? _overlay;
    private bool _opening;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "InstructMe.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("InstructMe est déjà lancé.", "InstructMe");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            // A bug in the overlay must not stop the background app: log it and close the overlay.
            LogError(args.Exception);
            args.Handled = true;
            _overlay?.Close();
            _tray?.ShowBalloonTip(4000, "InstructMe", $"Erreur : {args.Exception.Message}", Forms.ToolTipIcon.Error);
        };

        try
        {
            var settings = AppSettings.Load();
            var gesture = HotkeyGesture.Parse(settings.Hotkey);
            _detector = TextDetector.Create();
            _definitions = new DefinitionService(settings);
            _pronouncer = new Pronouncer();
            _hotkey = new GlobalHotkey(gesture);
            _hotkey.Pressed += OnHotkey;
            CreateTray(gesture);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "InstructMe", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void CreateTray(HotkeyGesture gesture)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir les réglages", null, (_, _) =>
            Process.Start(new ProcessStartInfo(AppSettings.FilePath) { UseShellExecute = true }));
        menu.Items.Add("Quitter", null, (_, _) => Shutdown());

        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Information,
            Text = $"InstructMe · {gesture.Text}",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.ShowBalloonTip(3000, "InstructMe", $"Appuyez sur {gesture.Text} en jeu pour lire un mot.", Forms.ToolTipIcon.None);
    }

    private async void OnHotkey()
    {
        // The same shortcut closes the overlay, so a Stream Deck button works as a toggle.
        if (_overlay is not null)
        {
            _overlay.Close();
            return;
        }
        if (_opening) return;
        _opening = true;

        try
        {
            var previous = NativeMethods.GetForegroundWindow();
            var monitor = NativeMethods.MonitorFromWindow(previous, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var frame = await ScreenCapture.CaptureMonitorAsync(monitor);

            _overlay = new OverlayWindow(frame, _detector!, _definitions!, _pronouncer!, previous);
            _overlay.Closed += (_, _) => _overlay = null;
            _overlay.Show();
        }
        catch (Exception ex)
        {
            _tray?.ShowBalloonTip(4000, "InstructMe", $"Capture impossible : {ex.Message}", Forms.ToolTipIcon.Error);
        }
        finally
        {
            _opening = false;
        }
    }

    private static void LogError(Exception ex)
    {
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "error.log");
            File.AppendAllText(path, $"[{DateTime.Now:O}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* Logging must never throw. */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _pronouncer?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
