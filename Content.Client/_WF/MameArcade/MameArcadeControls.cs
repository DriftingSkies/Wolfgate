using Content.Client._WF.MameArcade.Engine;
using RobustKey = Robust.Client.Input.Keyboard.Key;

namespace Content.Client._WF.MameArcade;

/// <summary>One player's arcade controls for one frame, as sent to the other player in a 2-player game.</summary>
[Flags]
public enum MameControls : ushort
{
    None = 0,
    Up = 1 << 0,
    Down = 1 << 1,
    Left = 1 << 2,
    Right = 1 << 3,
    Button1 = 1 << 4,
    Button2 = 1 << 5,
    Button3 = 1 << 6,
    Button4 = 1 << 7,
    Button5 = 1 << 8,
    Button6 = 1 << 9,
    Start = 1 << 10,
    Coin = 1 << 11,
}

/// <summary>Maps the keyboard to controls, and each seat's controls to the keys MAME.NET's drivers read.</summary>
public static class MameArcadeKeys
{
    private const int ControlCount = 12;

    // The order of the bits in MameControls; MAME.NET's default keys for player 1 and player 2.
    private static readonly Key[] PlayerOne =
    {
        Key.W, Key.S, Key.A, Key.D, Key.J, Key.K, Key.L, Key.U, Key.I, Key.O, Key.D1, Key.D5,
    };

    private static readonly Key[] PlayerTwo =
    {
        Key.Up, Key.Down, Key.Left, Key.Right,
        Key.NumPad1, Key.NumPad2, Key.NumPad3, Key.NumPad4, Key.NumPad5, Key.NumPad6,
        Key.D2, Key.D6,
    };

    /// <summary>Both seats use the same keys on their own keyboard.</summary>
    public static MameControls ToControl(RobustKey key) => key switch
    {
        RobustKey.W or RobustKey.Up => MameControls.Up,
        RobustKey.S or RobustKey.Down => MameControls.Down,
        RobustKey.A or RobustKey.Left => MameControls.Left,
        RobustKey.D or RobustKey.Right => MameControls.Right,
        RobustKey.J => MameControls.Button1,
        RobustKey.K => MameControls.Button2,
        RobustKey.L => MameControls.Button3,
        RobustKey.U => MameControls.Button4,
        RobustKey.I => MameControls.Button5,
        RobustKey.O => MameControls.Button6,
        RobustKey.Return or RobustKey.NumpadEnter or RobustKey.Num1 => MameControls.Start,
        RobustKey.Num5 or RobustKey.BackSpace => MameControls.Coin,
        _ => MameControls.None,
    };

    /// <summary>Sets the emulator's held keys from both seats' controls.</summary>
    public static void Apply(MameControls playerOne, MameControls playerTwo, bool[] held)
    {
        Array.Clear(held);
        for (var i = 0; i < ControlCount; i++)
        {
            var bit = (MameControls) (1 << i);
            if ((playerOne & bit) != 0)
                held[(int) PlayerOne[i]] = true;
            if ((playerTwo & bit) != 0)
                held[(int) PlayerTwo[i]] = true;
        }
    }
}
