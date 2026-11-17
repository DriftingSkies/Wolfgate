using Content.Shared.Stacks;
using Content.Shared.UserInterface;

namespace Content.Shared._WG.ScratchTickets;

/// <summary>Keeps a pile of scratch tickets from opening as a card; the server pulls one ticket out instead.</summary>
public sealed class WGScratchTicketPileSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGScratchTicketComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
    }

    private void OnOpenAttempt(Entity<WGScratchTicketComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (TryComp<StackComponent>(ent, out var stack) && stack.Count > 1)
            args.Cancel();
    }
}
