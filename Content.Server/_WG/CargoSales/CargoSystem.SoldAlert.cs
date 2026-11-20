using Content.Server._Mono.Cargo;
using Content.Shared.Radio.Components;
using Robust.Shared.Map;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    /// <summary>
    /// Radios a sold alert from off any map, so a world whose comms are down doesn't swallow it. A jammable alert still
    /// goes out from the console, where a jammer can reach it.
    /// </summary>
    private void WgSendSoldAlert(EntityUid speaker, string message, SoldAlertComponent alert, EntityUid console)
    {
        var radio = console;
        if (!alert.Jammable)
        {
            radio = Spawn(null, MapCoordinates.Nullspace);
            EnsureComp<TelecomExemptComponent>(radio);
        }

        foreach (var channel in alert.Channels)
        {
            _radio.SendRadioMessage(speaker, message, channel, radio, jammable: alert.Jammable);
        }

        if (radio != console)
            QueueDel(radio);
    }
}
