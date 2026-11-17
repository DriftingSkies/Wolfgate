using Content.Shared._WG.RMCClothing.Patterns;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Client._WG.RMCClothing.Patterns;

/// <summary>Redraws camouflage clothing when its pattern changes.</summary>
public sealed class WGCamoPatternVisualsSystem : EntitySystem
{
    [Dependency] private WGCamoPatternSystem _patterns = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGCamoPatternComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WGCamoPatternComponent, WGCamoPatternChangedEvent>(OnChanged);
    }

    private void OnState(Entity<WGCamoPatternComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _patterns.ApplyPattern(ent);
    }

    private void OnChanged(Entity<WGCamoPatternComponent> ent, ref WGCamoPatternChangedEvent args)
    {
        // The look is a prototype, so its sprite component has no owner.
        if (TryComp<SpriteComponent>(ent, out var sprite) &&
            args.Look.TryGetComponent<SpriteComponent>(out var lookSprite, EntityManager.ComponentFactory))
            _sprite.CopySprite((EntityUid.Invalid, lookSprite), (ent, sprite));
    }
}
