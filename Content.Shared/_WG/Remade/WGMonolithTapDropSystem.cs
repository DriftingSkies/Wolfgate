using Content.Shared.DragDrop;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Systems;

namespace Content.Shared._WG.Remade;

/// <summary>Lets a humanoid corpse be dragged onto a Tap, on both sides of the network.</summary>
public sealed partial class WGMonolithTapDropSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGMonolithTapComponent, CanDropTargetEvent>(OnCanDrop);
    }

    private void OnCanDrop(Entity<WGMonolithTapComponent> ent, ref CanDropTargetEvent args)
    {
        args.CanDrop = HasComp<HumanoidAppearanceComponent>(args.Dragged) && _mobState.IsDead(args.Dragged);
        args.Handled = true;
    }
}
