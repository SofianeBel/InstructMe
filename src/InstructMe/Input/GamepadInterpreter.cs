namespace InstructMe.Input;

internal enum NavDirection { Up, Down, Left, Right }

internal enum GamepadAction { Up, Down, Left, Right, Confirm, Cancel, Extend, Shrink }

/// <summary>
/// Turns raw XInput state into overlay actions: button presses fire once,
/// and a held direction repeats after a short delay.
/// </summary>
internal sealed class GamepadInterpreter
{
    public const ushort DpadUp = 0x0001, DpadDown = 0x0002, DpadLeft = 0x0004, DpadRight = 0x0008;
    public const ushort ButtonLb = 0x0100, ButtonRb = 0x0200, ButtonA = 0x1000, ButtonB = 0x2000;

    private const short StickThreshold = 18000;
    private static readonly TimeSpan FirstRepeat = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan NextRepeat = TimeSpan.FromMilliseconds(110);

    private ushort _previousButtons;
    private NavDirection? _heldDirection;
    private TimeSpan _nextRepeatAt;
    private bool _primed;

    public IReadOnlyList<GamepadAction> Update(ushort buttons, short thumbX, short thumbY, TimeSpan now)
    {
        var actions = new List<GamepadAction>();
        var direction = ReadDirection(buttons, thumbX, thumbY);

        // The first sample is only a baseline: buttons held when the overlay opens do not fire.
        if (!_primed)
        {
            _primed = true;
            _previousButtons = buttons;
            _heldDirection = direction;
            _nextRepeatAt = now + FirstRepeat;
            return actions;
        }

        if (direction != _heldDirection)
        {
            _heldDirection = direction;
            _nextRepeatAt = now + FirstRepeat;
            if (direction is { } d) actions.Add(ToAction(d));
        }
        else if (direction is { } held && now >= _nextRepeatAt)
        {
            _nextRepeatAt = now + NextRepeat;
            actions.Add(ToAction(held));
        }

        ushort pressed = (ushort)(buttons & ~_previousButtons);
        if ((pressed & ButtonA) != 0) actions.Add(GamepadAction.Confirm);
        if ((pressed & ButtonB) != 0) actions.Add(GamepadAction.Cancel);
        if ((pressed & ButtonRb) != 0) actions.Add(GamepadAction.Extend);
        if ((pressed & ButtonLb) != 0) actions.Add(GamepadAction.Shrink);
        _previousButtons = buttons;

        return actions;
    }

    private static NavDirection? ReadDirection(ushort buttons, short x, short y)
    {
        if ((buttons & DpadUp) != 0) return NavDirection.Up;
        if ((buttons & DpadDown) != 0) return NavDirection.Down;
        if ((buttons & DpadLeft) != 0) return NavDirection.Left;
        if ((buttons & DpadRight) != 0) return NavDirection.Right;

        if (Math.Abs((int)x) < StickThreshold && Math.Abs((int)y) < StickThreshold) return null;
        if (Math.Abs((int)x) >= Math.Abs((int)y))
            return x > 0 ? NavDirection.Right : NavDirection.Left;
        return y > 0 ? NavDirection.Up : NavDirection.Down; // XInput Y is positive upward.
    }

    private static GamepadAction ToAction(NavDirection d) => d switch
    {
        NavDirection.Up => GamepadAction.Up,
        NavDirection.Down => GamepadAction.Down,
        NavDirection.Left => GamepadAction.Left,
        _ => GamepadAction.Right,
    };
}
