using Content.Server._Mono.CorticalBorer;
using Content.Shared._Mono.CorticalBorer;
using Robust.Shared.Containers;

namespace Content.Server._WG.CorticalBorer;

/// <summary>
/// Ends a cortical borer's control of its host when the borer leaves the host.
/// </summary>
public sealed partial class EndControlOnEjectSystem : EntitySystem
{
    [Dependency] private CorticalBorerSystem _borer = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CorticalBorerComponent, EntGotRemovedFromContainerMessage>(OnRemovedFromContainer);
    }

    /// <summary>
    /// Mono's eject takes the borer out and then clears its host, after which nothing can end the control.
    /// </summary>
    private void OnRemovedFromContainer(Entity<CorticalBorerComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        // A borer being deleted can't take its mind back.
        if (TerminatingOrDeleted(ent))
            return;

        _borer.EndControl(ent);
    }
}
