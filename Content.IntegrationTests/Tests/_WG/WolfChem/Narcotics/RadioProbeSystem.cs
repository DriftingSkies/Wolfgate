#nullable enable
using System.Collections.Generic;
using Content.Server.Radio;
using Content.Shared.GameTicking;
using Robust.Shared.GameObjects;
using Robust.Shared.Reflection;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Narcotics;

/// <summary>Records every radio send, so a test can see an automated alert go out. Listens to the broadcast only.</summary>
[Reflect(false)]
public sealed class RadioProbeSystem : EntitySystem
{
    public readonly List<(string Channel, EntityUid Source)> Sent = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RadioSendAttemptEvent>(OnSend);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Sent.Clear());
    }

    private void OnSend(ref RadioSendAttemptEvent ev)
    {
        Sent.Add((ev.Channel.ID, ev.RadioSource));
    }
}
