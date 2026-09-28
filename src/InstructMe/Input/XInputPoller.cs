using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace InstructMe.Input;

/// <summary>Polls Xbox-compatible controllers through XInput while the overlay is open.</summary>
internal sealed partial class XInputPoller : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [LibraryImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static partial int XInputGetState(int userIndex, out XInputState state);

    private const int ErrorSuccess = 0;
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(1);

    private readonly DispatcherTimer _timer;
    private readonly GamepadInterpreter _interpreter = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly bool[] _connected = new bool[4];
    private TimeSpan? _lastScan;

    public event Action<GamepadAction>? Action;

    public XInputPoller()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start() => _timer.Start();

    private void Poll()
    {
        var now = _clock.Elapsed;
        // Querying an empty slot is slow, so only look for new controllers once per second.
        bool rescan = _lastScan is not { } last || now - last >= RescanInterval;
        if (rescan) _lastScan = now;

        ushort buttons = 0;
        short x = 0, y = 0;
        for (int i = 0; i < 4; i++)
        {
            if (!_connected[i] && !rescan) continue;
            _connected[i] = XInputGetState(i, out var state) == ErrorSuccess;
            if (!_connected[i]) continue;

            buttons |= state.Gamepad.Buttons;
            if (Math.Abs((int)state.Gamepad.ThumbLX) > Math.Abs((int)x)) x = state.Gamepad.ThumbLX;
            if (Math.Abs((int)state.Gamepad.ThumbLY) > Math.Abs((int)y)) y = state.Gamepad.ThumbLY;
        }

        foreach (var action in _interpreter.Update(buttons, x, y, now))
            Action?.Invoke(action);
    }

    public void Dispose() => _timer.Stop();
}
