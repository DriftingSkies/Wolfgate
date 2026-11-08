using Content.Shared._WF.Fauna;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Fauna;

/// <summary>
/// Applies the colours the server picked to the layers of a <see cref="FaunaRandomTintComponent"/> mob.
/// </summary>
public sealed partial class FaunaRandomTintSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaRandomTintComponent, AfterAutoHandleStateEvent>(OnHandleState);
    }

    private void OnHandleState(Entity<FaunaRandomTintComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        Entity<SpriteComponent?> spriteEnt = (ent.Owner, sprite);
        foreach (var (key, palette) in ent.Comp.Layers)
        {
            if (ent.Comp.Selected.TryGetValue(palette, out var colour)
                && _sprite.LayerMapTryGet(spriteEnt, key, out var layer, false))
                _sprite.LayerSetColor(spriteEnt, layer, colour);
        }
    }
}
