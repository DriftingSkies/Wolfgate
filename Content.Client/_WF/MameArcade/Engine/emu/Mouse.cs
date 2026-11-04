#nullable disable
// Modified for Wolfgate on 2026-11-03: no mouse; drivers that read one get no movement.

namespace Content.Client._WF.MameArcade.Engine
{
    public class Mouse
    {
        public static int deltaX, deltaY;
        public static byte[] buttons = new byte[8];
        public static void Update()
        {
            deltaX = 0;
            deltaY = 0;
        }
    }
}
