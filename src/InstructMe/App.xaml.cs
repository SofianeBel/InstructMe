using System.ComponentModel;
using System.IO;
using System.Windows;
using InstructMe.Capture;
using InstructMe.Definitions;
using InstructMe.Input;
using InstructMe.Native;
using InstructMe.Overlay;
using InstructMe.Settings;
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
    private AppSettings _settings = new();
    private HotkeyGesture _gesture;
    private GlobalHotkey? _hotkey;
    private Forms.NotifyIcon? _tray;
    private TextDetector? _detector;
    private DefinitionService? _definitions;
    private Pronouncer? _pronouncer;
    private OverlayWindow? _overlay;
    private SettingsWindow? _settingsWindow;
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
            _settings = AppSettings.Load();
            _gesture = HotkeyGesture.Parse(_settings.Hotkey);
            _detector = TextDetector.Create();
            _definitions = new DefinitionService(_settings);
            _pronouncer = new Pronouncer(_settings.Voice);
            _hotkey = new GlobalHotkey(_gesture);
            _hotkey.Pressed += OnHotkey;
            CreateTray();
            if (_definitions.HasApiKey is false) OpenSettings();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "InstructMe", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Réglages", null, (_, _) => OpenSettings());
        menu.Items.Add("Quitter", null, (_, _) => Shutdown());

        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Information,
            Text = $"InstructMe · {_gesture.Text}",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
        _tray.ShowBalloonTip(3000, "InstructMe", $"Appuyez sur {_gesture.Text} en jeu pour lire un mot.", Forms.ToolTipIcon.None);
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _overlay?.Close();
        _settingsWindow = new SettingsWindow(_settings, ApplySettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Saves and applies new settings. Returns a French error for the player, or null.</summary>
    private string? ApplySettings(AppSettings next)
    {
        HotkeyGesture gesture;
        try
        {
            gesture = HotkeyGesture.Parse(next.Hotkey);
        }
        catch (FormatException)
        {
            return $"Raccourci non valide : {next.Hotkey}.";
        }

        // Register the new shortcut before the old one is released, so a failure changes nothing.
        bool sameShortcut = gesture.Modifiers == _gesture.Modifiers && gesture.VirtualKey == _gesture.VirtualKey;
        GlobalHotkey? newHotkey = null;
        if (!sameShortcut)
        {
            try
            {
                newHotkey = new GlobalHotkey(gesture);
            }
            catch (Win32Exception)
            {
                return $"Le raccourci {gesture.Text} est déjà utilisé par une autre application.";
            }
        }

        try
        {
            next.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            newHotkey?.Dispose();
            return $"Enregistrement impossible : {ex.Message}";
        }

        _overlay?.Close();
        if (newHotkey is not null)
        {
            _hotkey?.Dispose();
            _hotkey = newHotkey;
            _hotkey.Pressed += OnHotkey;
        }
        if (next.Voice != _settings.Voice)
        {
            _pronouncer?.Dispose();
            _pronouncer = new Pronouncer(next.Voice);
        }
        _definitions = new DefinitionService(next);
        _settings = next;
        _gesture = gesture;
        if (_tray is not null) _tray.Text = $"InstructMe · {gesture.Text}";
        return null;
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
