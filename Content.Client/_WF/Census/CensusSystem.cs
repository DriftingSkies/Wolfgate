using Content.Shared._WF.Census;

namespace Content.Client._WF.Census;

/// <summary>Carries the census window's traffic: the open censuses and answers to submissions in, answers out.</summary>
public sealed class CensusSystem : EntitySystem
{
    /// <summary>Raised with the open censuses whenever they change.</summary>
    public event Action<CensusListEvent>? List;

    /// <summary>Raised when the server has stored or refused the player's answers.</summary>
    public event Action<CensusResultEvent>? Answered;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<CensusListEvent>(ev => List?.Invoke(ev));
        SubscribeNetworkEvent<CensusResultEvent>(ev => Answered?.Invoke(ev));
    }

    public void RequestList()
    {
        RaiseNetworkEvent(new CensusListRequestEvent());
    }

    public void Submit(CensusSubmitEvent answers)
    {
        RaiseNetworkEvent(answers);
    }
}
