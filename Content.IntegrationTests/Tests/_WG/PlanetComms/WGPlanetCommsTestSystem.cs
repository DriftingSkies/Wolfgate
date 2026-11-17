using System.Collections.Generic;
using Content.Server.Radio;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.PlanetComms;

/// <summary>Records which test radios a message reached, after every real filter.</summary>
public sealed class WGPlanetCommsTestSystem : EntitySystem
{
    public readonly List<EntityUid> Deliveries = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGPlanetCommsTestReceiverComponent, RadioReceiveEvent>(OnReceive);
    }

    private void OnReceive(EntityUid uid, WGPlanetCommsTestReceiverComponent component, ref RadioReceiveEvent args)
    {
        Deliveries.Add(uid);
    }
}

/// <summary>Marks a radio the spy watches, without changing what it hears.</summary>
[RegisterComponent]
public sealed partial class WGPlanetCommsTestReceiverComponent : Component;
