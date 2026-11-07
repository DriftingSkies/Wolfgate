using Content.Shared._WF.MeridianTraits.Hemophage;
using Robust.Client.GameObjects;

namespace Content.Client._WF.MeridianTraits.Hemophage;

/// <summary>Predicts a hemophage's speed and fragility, and shows corrupted organs as the tumor has left them.</summary>
public sealed partial class WFHemophageSystem : SharedWFHemophageSystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFTumorCorruptedComponent, AfterAutoHandleStateEvent>(OnCorruptedState);
        SubscribeLocalEvent<WFTumorCorruptedComponent, ComponentStartup>(OnCorruptedStartup);
    }

    private void OnCorruptedState(Entity<WFTumorCorruptedComponent> ent, ref AfterAutoHandleStateEvent args) => UpdateCorrupted(ent);

    private void OnCorruptedStartup(Entity<WFTumorCorruptedComponent> ent, ref ComponentStartup args) => UpdateCorrupted(ent);

    private void UpdateCorrupted(Entity<WFTumorCorruptedComponent> ent)
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
