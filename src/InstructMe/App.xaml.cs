using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using InstructMe.Capture;
using InstructMe.Definitions;
using InstructMe.Input;
using InstructMe.Native;
using InstructMe.Overlay;
using InstructMe.Settings;
using InstructMe.Text;
using InstructMe.Updates;
using InstructMe.Vocabulary;
using Velopack;
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
    private VocabularyStore? _vocabulary;
    private VocabularyWindow? _vocabularyWindow;
    private bool _opening;
    private readonly AppUpdater _updater = new();
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private Forms.ToolStripMenuItem? _updateItem;
    private string? _announcedVersion;
    private bool _checkingUpdates;

    [STAThread]
    private static void Main()
    {
        // Velopack runs the app with special arguments during install, update, and uninstall.
        // It must come first: for those runs it exits from inside Run().
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ =>
            {
                try { WindowsStartup.Apply(false); }
                catch { /* The uninstall must go on. */ }
                StopOtherInstances();
            })
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    /// <summary>
    /// Velopack does not always find the app in the tray before an uninstall,
    /// and a running copy keeps its folder locked.
    /// </summary>
    private static void StopOtherInstances()
    {
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("InstructMe"))
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId
                        || !string.Equals(process.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch { /* Already closed, or not ours. */ }
            }
        }
    }

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
            SyncWindowsStartup();
            _gesture = HotkeyGesture.Parse(_settings.Hotkey);
            _detector = TextDetector.Create();
            _vocabulary = new VocabularyStore();
            _definitions = new DefinitionService(_settings, _vocabulary);
            _pronouncer = new Pronouncer(_settings.Voice);
            _hotkey = new GlobalHotkey(_gesture);
            _hotkey.Pressed += OnHotkey;
            CreateTray();
            // The first check waits a little: at sign-in, the network is often not ready yet.
            _updateTimer.Tick += (_, _) =>
            {
                _updateTimer.Interval = TimeSpan.FromHours(6);
                CheckForUpdates();
            };
            _updateTimer.Start();
            if (e.Args.Contains("--vocabulary", StringComparer.OrdinalIgnoreCase)) OpenVocabulary();
            else if (_definitions.HasApiKey is false) OpenSettings();
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
        _updateItem = new Forms.ToolStripMenuItem { Visible = false };
        _updateItem.Click += (_, _) => Quit(restartAfterUpdate: true);
        menu.Items.Add(_updateItem);
        menu.Items.Add("Réglages", null, (_, _) => OpenSettings());
        menu.Items.Add("Mon vocabulaire", null, (_, _) => OpenVocabulary());
        menu.Items.Add("Quitter", null, (_, _) => Quit(restartAfterUpdate: false));

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

    private void OpenVocabulary()
    {
        _overlay?.Close();
        if (_vocabularyWindow is not null)
        {
            if (_vocabularyWindow.WindowState == WindowState.Minimized) _vocabularyWindow.WindowState = WindowState.Normal;
            _vocabularyWindow.Activate();
            return;
        }
        _vocabularyWindow = new VocabularyWindow(_vocabulary!, _pronouncer!, _gesture.Text);
        _vocabularyWindow.Closed += (_, _) => _vocabularyWindow = null;
        _vocabularyWindow.Show();
        _vocabularyWindow.Activate();
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
            WindowsStartup.Apply(next.LaunchAtStartup);
            next.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
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
            _vocabularyWindow?.UpdatePronouncer(_pronouncer);
        }
        _definitions?.Dispose();
        _definitions = new DefinitionService(next, _vocabulary);
        _settings = next;
        _gesture = gesture;
        _vocabularyWindow?.UpdateShortcut(gesture.Text);
        if (_tray is not null) _tray.Text = $"InstructMe · {gesture.Text}";
        return null;
    }

    private async void CheckForUpdates()
    {
        if (_checkingUpdates) return;
        _checkingUpdates = true;
        try
        {
            var version = await _updater.DownloadAsync();
            if (version is null || version == _announcedVersion) return;
            _announcedVersion = version;
            _updateItem!.Text = $"Installer la version {version} et redémarrer";
            _updateItem.Visible = true;
            _tray?.ShowBalloonTip(5000, "InstructMe",
                $"La version {version} est prête. Elle s'installera quand vous quitterez InstructMe.", Forms.ToolTipIcon.None);
        }
        catch (Exception ex)
        {
            // Offline or GitHub unavailable: try again at the next check.
            LogError(ex);
        }
        finally
        {
            _checkingUpdates = false;
        }
    }

    /// <summary>Quits the app. A downloaded version is installed first, then the app restarts if asked.</summary>
    private void Quit(bool restartAfterUpdate)
    {
        try
        {
            _updater.InstallOnExit(restartAfterUpdate);
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
        Shutdown();
    }

    /// <summary>Keeps the Run entry in line with the setting. A failure must not stop the app.</summary>
    private void SyncWindowsStartup()
    {
        try
        {
            WindowsStartup.Apply(_settings.LaunchAtStartup);
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
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

            _overlay = new OverlayWindow(frame, _detector!, _definitions!, _pronouncer!, previous, _vocabulary!, OpenVocabulary);
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
        _updateTimer.Stop();
        _hotkey?.Dispose();
        _definitions?.Dispose();
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
