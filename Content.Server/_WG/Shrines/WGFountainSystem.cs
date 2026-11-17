using Content.Shared._WG.Shrines;
using Content.Shared.Chemistry.EntitySystems;

namespace Content.Server._WG.Shrines;

/// <summary>Shows a shrine fountain dry once its solution is gone.</summary>
public sealed class WGFountainSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGFountainComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
    }

    private void OnSolutionChanged(Entity<WGFountainComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (args.SolutionId == ent.Comp.Solution)
            _appearance.SetData(ent, WGFountainVisuals.Dry, args.Solution.Volume <= 0);
    }
}
