#nullable disable
// Modified for Wolfgate on 2026-11-03: keeps the light-gun position but draws no crosshair bitmap.

namespace Content.Client._WF.MameArcade.Engine
{
    public class Crosshair
    {
        public struct crosshair_global
        {
            public bool[] used;
            public bool[] visible;
            public int[] x;
            public int[] y;
            public byte fade;
            public byte animation_counter;
        }
        public static crosshair_global global;
        public static void crosshair_init()
        {
            global.used = new bool[8];
            global.visible = new bool[8];
            global.x = new int[8];
            global.y = new int[8];
            switch (Machine.sName)
            {
                case "opwolf":
                case "opwolfa":
                case "opwolfj":
                case "opwolfu":
                case "opwolfb":
                case "opwolfp":
                    global.used[0] = true;
                    global.visible[0] = true;
                    break;
            }
        }
        public static void animate_opwolf()
        {
            int player;
            global.animation_counter += 0x04;
            if (global.animation_counter < 0x80)
            {
                global.fade = (byte)(0xa0 + (0x60 * (global.animation_counter & 0x7f) / 0x80));
            }
            else
            {
                global.fade = (byte)(0xa0 + (0x60 * (~global.animation_counter & 0x7f) / 0x80));
            }
            for (player = 0; player < 8; player++)
            {
                if (global.used[player])
                {
                    global.x[player] = ((Taito.p1x * 320) / 256) - 0;
                    global.y[player] = Taito.p1y - 10;
                }
            }
        }
    }
}
