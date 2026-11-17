using Content.Shared._WG.MeridianTraits.Hemophage;
using Robust.Client.GameObjects;

namespace Content.Client._WG.MeridianTraits.Hemophage;

/// <summary>Predicts a hemophage's speed and fragility, and shows corrupted organs as the tumor has left them.</summary>
public sealed partial class WGHemophageSystem : SharedWGHemophageSystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGTumorCorruptedComponent, AfterAutoHandleStateEvent>(OnCorruptedState);
        SubscribeLocalEvent<WGTumorCorruptedComponent, ComponentStartup>(OnCorruptedStartup);
    }

    private void OnCorruptedState(Entity<WGTumorCorruptedComponent> ent, ref AfterAutoHandleStateEvent args) => UpdateCorrupted(ent);

    private void OnCorruptedStartup(Entity<WGTumorCorruptedComponent> ent, ref ComponentStartup args) => UpdateCorrupted(ent);

    private void UpdateCorrupted(Entity<WGTumorCorruptedComponent> ent)
    {
        if (!ent.Comp.Corrupted || !TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (ent.Comp.Sprite is { } corrupted)
        {
            _sprite.LayerSetRsi((ent, sprite), 0, corrupted.RsiPath, corrupted.RsiState);
            return;
        }

        _sprite.SetColor((ent, sprite), ent.Comp.Tint);
    }
}
