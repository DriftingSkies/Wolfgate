using System.Numerics;
using Content.Server.Sprite;
using Robust.Shared.Random;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Rolls the sprite size of <see cref="FaunaRandomScaleComponent"/> mobs on map init.
/// </summary>
public sealed partial class FaunaRandomScaleSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ScaleVisualsSystem _scale = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaRandomScaleComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<FaunaRandomScaleComponent> ent, ref MapInitEvent args)
    {
        var scale = _random.NextFloat(ent.Comp.Min, ent.Comp.Max);
        _scale.SetSpriteScale(ent, new Vector2(scale, scale));
    }
}
