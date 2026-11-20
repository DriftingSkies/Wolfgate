using Content.Shared._White.Overlays;
using Content.Shared.Actions;
using Content.Shared.NightVision;
using Content.Shared.Overlays;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._WG.Augments;

/// <summary>Cycles <see cref="WGThermalTintsComponent"/> eyes through their bands, switching the body's thermal and night vision with them.</summary>
public sealed partial class WGThermalTintsSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedNightVisionSystem _nightVision = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>The colour of the swap glitch when the eyes switch off.</summary>
    private static readonly Color OffColor = Color.FromHex("#9aa3a8");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGThermalTintsComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WGThermalTintsComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WGThermalTintsComponent, WGCycleThermalTintEvent>(OnCycle);
    }

    private void OnMapInit(Entity<WGThermalTintsComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<WGThermalTintsComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnCycle(Entity<WGThermalTintsComponent> ent, ref WGCycleThermalTintEvent args)
    {
        if (args.Handled || !TryComp<ThermalVisionComponent>(ent, out var thermal))
            return;

        args.Handled = true;
        var next = ent.Comp.Index + 1 < ent.Comp.Tints.Count ? ent.Comp.Index + 1 : -1;
        SetTint(ent, next, thermal);

        if (!_timing.IsFirstTimePredicted)
            return;

        if (next < 0)
        {
            _audio.PlayLocal(ent.Comp.OffSound, ent, ent);
        }
        else
        {
            _audio.PlayLocal(ent.Comp.SwapSound, ent, ent);
            _audio.PlayLocal(ent.Comp.ChirpSound, ent, ent);
        }

        var swapped = new WGThermalTintSwappedEvent(next < 0 ? OffColor : ent.Comp.Tints[next].Color);
        RaiseLocalEvent(ent, ref swapped);
    }

    /// <summary>Sets the eyes to a band, or off for -1, and switches the thermal and night vision to match.</summary>
    public void SetTint(Entity<WGThermalTintsComponent> ent, int index, ThermalVisionComponent thermal)
    {
        ent.Comp.Index = index;
        Dirty(ent);
        ApplyTint(ent.Comp, thermal);

        var band = index >= 0 && index < ent.Comp.Tints.Count ? ent.Comp.Tints[index] : null;
        if (TryComp<NightVisionComponent>(ent, out var night))
        {
            if (band is { NightVision: true } && night.PhosphorColor != band.Color)
            {
                night.PhosphorColor = band.Color;
                Dirty(ent, night);
            }

            _nightVision.SetEnabled((ent, night), band is { NightVision: true });
        }

        var active = band is { NightVision: false };
        if (thermal.IsActive == active)
        {
            Dirty(ent, thermal);
            return;
        }

        thermal.IsActive = active;
        Dirty(ent, thermal);
        // The thermal vision's own toggle raises this; the client redraws its overlays on it.
        var toggled = new SwitchableOverlayToggledEvent(ent, active);
        RaiseLocalEvent(ent, ref toggled);
    }

    /// <summary>Copies the current thermal band onto the thermal vision. The wash tint isn't networked, so the client calls this on state.</summary>
    public static void ApplyTint(WGThermalTintsComponent comp, ThermalVisionComponent thermal)
    {
        if (comp.Index < 0 || comp.Index >= comp.Tints.Count || comp.Tints[comp.Index].NightVision)
            return;

        var tint = comp.Tints[comp.Index];
        thermal.Color = tint.Color;
        thermal.Tint = tint.Tint;
    }
}
