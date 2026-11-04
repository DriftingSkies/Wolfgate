#nullable disable
// Modified for Wolfgate on 2026-11-03: replaces MAME.NET's DirectInput keyboard with state the frontend sets each frame.

namespace Content.Client._WF.MameArcade.Engine
{
    /// <summary>The keys MAME.NET's drivers read. Names follow its DirectInput mapping.</summary>
    public enum Key : byte
    {
        A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
        Up, Down, Left, Right,
        NumPad1, NumPad2, NumPad3, NumPad4, NumPad5, NumPad6,
        Escape, LeftShift, RightShift, F3, F7, F8, F10,
        Count,
    }

    public class Keyboard
    {
        public static bool bF10;

        /// <summary>Keys held for the next frame, written by the frontend between frames.</summary>
        public static readonly bool[] Held = new bool[(int)Key.Count];

        struct KeyState
        {
            public bool IsPressed;
            public bool IsTriggered;
            public bool WasPressed;
        };
        private static KeyState[] m_KeyStates = new KeyState[(int)Key.Count];
        public static bool IsPressed(Key key)
        {
            return m_KeyStates[(int)key].IsPressed;
        }
        public static bool IsTriggered(Key key)
        {
            return m_KeyStates[(int)key].IsTriggered;
        }
        public static void Reset()
        {
            System.Array.Clear(Held);
            m_KeyStates = new KeyState[(int)Key.Count];
        }
        public static void Update()
        {
            for (int i = 0; i < m_KeyStates.Length; i++)
            {
                m_KeyStates[i].IsPressed = Held[i];
                if (m_KeyStates[i].IsPressed)
                {
                    m_KeyStates[i].IsTriggered = !m_KeyStates[i].WasPressed;
                    m_KeyStates[i].WasPressed = true;
                }
                else
                {
                    m_KeyStates[i].WasPressed = false;
                    m_KeyStates[i].IsTriggered = false;
                }
            }
        }
    }
}
