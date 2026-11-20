using Content.Client._WG.Augments.UI;
using Content.Shared._White.Overlays;
using Content.Shared._WG.Augments;
using Robust.Client.Graphics;
using Robust.Client.Player;

namespace Content.Client._WG.Augments;

/// <summary>Keeps the thermal wash in step with the eyes' tint and plays the swap glitch for the local player.</summary>
public sealed partial class WGThermalTintsVisualsSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;

    private WGThermalGlitchOverlay _glitch = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGThermalTintsComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WGThermalTintsComponent, WGThermalTintSwappedEvent>(OnSwapped);

        _glitch = new WGThermalGlitchOverlay();
        _overlays.AddOverlay(_glitch);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay(_glitch);
    }

    private void OnState(Entity<WGThermalTintsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (TryComp<ThermalVisionComponent>(ent, out var thermal))
            WGThermalTintsSystem.ApplyTint(ent.Comp, thermal);
    }

    private void OnSwapped(Entity<WGThermalTintsComponent> ent, ref WGThermalTintSwappedEvent args)
    {
        if (ent.Owner == _player.LocalEntity)
            _glitch.Start(args.Color);
    }
}
