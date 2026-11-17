using System.Collections.Generic;
using Content.Server.Radio;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.RoundSetup;

/// <summary>Records delivered radio events after the real channel and membership filters.</summary>
public sealed class WGCrewRadioTestSystem : EntitySystem
{
    public readonly List<(EntityUid Receiver, string Text)> Deliveries = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGCrewRadioTestReceiverComponent, RadioReceiveEvent>(OnReceive);
    }

    private void OnReceive(EntityUid uid, WGCrewRadioTestReceiverComponent component, ref RadioReceiveEvent args)
    {
        if (args.Channel.ID == "WGCrew")
            Deliveries.Add((uid, args.OriginalChatMsg.WrappedMessage));
    }
}

/// <summary>Observes test recipients without changing their radio eligibility.</summary>
[RegisterComponent]
public sealed partial class WGCrewRadioTestReceiverComponent : Component;
