#nullable disable
// Modified for Wolfgate on 2026-11-03: emulation never reads the wall clock; the frontend paces frames.

namespace Content.Client._WF.MameArcade.Engine
{
    public class Wintime
    {
        public static long ticks_per_second = 1000;
        public static void wintime_init()
        {
        }
        public static long osd_ticks()
        {
            return 0;
        }
        public static void osd_sleep(long duration)
        {
        }
    }
}
