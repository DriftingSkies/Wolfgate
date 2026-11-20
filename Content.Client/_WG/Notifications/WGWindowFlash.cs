using Robust.Client.Graphics;
using Robust.Shared.Configuration;

namespace Content.Client._WG.Notifications;

/// <summary>Flashes the game window for an event, if the player's setting for that event allows it.</summary>
public static class WGWindowFlash
{
    /// <summary>Requests window attention when <paramref name="setting"/> is on.</summary>
    public static void Request(CVarDef<bool> setting)
    {
        if (IoCManager.Resolve<IConfigurationManager>().GetCVar(setting))
            IoCManager.Resolve<IClyde>().RequestWindowAttention();
    }
}
