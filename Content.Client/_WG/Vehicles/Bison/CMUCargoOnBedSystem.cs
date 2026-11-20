using Content.Shared._WG.Vehicles.Bison;
using Robust.Client.GameObjects;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._WG.Vehicles.Bison;

/// <summary>Draws cargo on a Bison's bed above the carrier and upright, and puts it back where it was when unloaded.</summary>
public sealed class CMUCargoOnBedSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CMUCargoOnBedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CMUCargoOnBedComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<CMUCargoOnBedComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp(ent, out SpriteComponent? sprite))
            return;

        ent.Comp.OldDrawDepth = sprite.DrawDepth;
        ent.Comp.OldNoRotation = sprite.NoRotation;
        _sprite.SetDrawDepth((ent.Owner, sprite), (int) DrawDepth.OverMobs);
        // The carrier's sprite snaps to four facings, so its cargo stays upright too.
        sprite.NoRotation = true;
    }

    private void OnShutdown(Entity<CMUCargoOnBedComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.OldDrawDepth is not { } depth || !TryComp(ent, out SpriteComponent? sprite))
            return;

        _sprite.SetDrawDepth((ent.Owner, sprite), depth);
        sprite.NoRotation = ent.Comp.OldNoRotation;
    }
}
