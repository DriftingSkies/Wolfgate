using Content.Shared._WF.Outposts;

namespace Content.Client._WF.Outposts;

/// <summary>Asks the server whether the player can join the round at their outpost, and joins there.</summary>
public sealed class WFOutpostJoinSystem : EntitySystem
{
    /// <summary>The outpost the player can wake up at, or null, as the server answered.</summary>
    public event Action<string?>? OfferReceived;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFOutpostJoinOfferEvent>(OnOffer);
    }

    private void OnOffer(WFOutpostJoinOfferEvent ev)
    {
        OfferReceived?.Invoke(ev.Outpost);
    }

    /// <summary>Asks whether the player can join the round at their outpost.</summary>
    public void Query()
    {
        RaiseNetworkEvent(new WFOutpostJoinQueryEvent());
    }

    /// <summary>Joins the round in a free cryopod on the player's outpost.</summary>
    public void Join()
    {
        RaiseNetworkEvent(new WFOutpostJoinRequestEvent());
    }
}
