using Content.Shared._WG.Traders;
using Content.Shared.Interaction;

namespace Content.Client._WG.Traders;

/// <summary>
/// A click on a trader starts a conversation on the server. The client would predict a humanoid's pat on the back
/// for it, since the server's removal of that reaction never reaches it, so the click is taken here first.
/// </summary>
public sealed partial class TraderClickSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TraderComponent, InteractHandEvent>(OnInteractHand, before: [typeof(InteractionPopupSystem)]);
    }

    private void OnInteractHand(Entity<TraderComponent> ent, ref InteractHandEvent args)
    {
        args.Handled = true;
    }
}
