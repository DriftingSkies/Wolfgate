using System.Diagnostics.CodeAnalysis;
using Content.Server._HL.Silicons.Synths.Battery;
using Content.Shared.Power.Components;

namespace Content.Server.Power.EntitySystems;

/// <summary>Lets a charger find a synth's cell, which sits in an organ slot rather than a power cell slot.</summary>
public sealed partial class ChargerSystem
{
    [Dependency] private SynthBatterySystem _wgSynthBattery = default!;

    /// <summary>The cell in a synth's battery organ slot, if the entity is a synth with one.</summary>
    private bool WgTryGetSynthBattery(EntityUid uid,
        [NotNullWhen(true)] out EntityUid? batteryUid,
        [NotNullWhen(true)] out BatteryComponent? component)
    {
        batteryUid = null;
        component = null;
        if (!_wgSynthBattery.TryGetBattery(uid, out var battery))
            return false;

        batteryUid = battery.Value.Owner;
        component = battery.Value.Comp;
        return true;
    }
}
