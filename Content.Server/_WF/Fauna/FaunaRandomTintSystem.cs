using Content.Shared._WF.Fauna;
using Robust.Shared.Random;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Picks the colours of <see cref="FaunaRandomTintComponent"/> mobs when they spawn.
/// </summary>
public sealed partial class FaunaRandomTintSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPointLightSystem _light = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaRandomTintComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<FaunaRandomTintComponent> ent, ref MapInitEvent args)
    {
        foreach (var (name, colours) in ent.Comp.Palettes)
        {
            if (colours.Count > 0 && !ent.Comp.Selected.ContainsKey(name))
                ent.Comp.Selected[name] = _random.Pick(colours);
        }

        if (ent.Comp.LightPalette is { } light && ent.Comp.Selected.TryGetValue(light, out var colour))
            _light.SetColor(ent, colour);

        Dirty(ent);
    }
}
