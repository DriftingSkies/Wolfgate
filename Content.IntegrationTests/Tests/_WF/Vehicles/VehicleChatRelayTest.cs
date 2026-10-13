#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Shared._WF.Vehicles.Weapons;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>Speech passing through a vehicle's hull, between its crew on the interior's map and people beside it.</summary>
[TestFixture]
public sealed class VehicleChatRelayTest
{
    [Test]
    public async Task CrewAndPeopleBesideTheHullHearEachOther()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var outside = await pair.CreateTestMap();
        var cabin = await pair.CreateTestMap();
        var session = pair.Player!;

        await server.WaitAssertion(() =>
        {
            var hull = entMan.SpawnEntity("WFVehicleTestChassis", new MapCoordinates(new Vector2(0.5f, 0.5f), outside.MapId));
            var near = entMan.SpawnEntity(null, new MapCoordinates(new Vector2(3.5f, 0.5f), outside.MapId));
            var far = entMan.SpawnEntity(null, new MapCoordinates(new Vector2(60.5f, 0.5f), outside.MapId));

            var crew = entMan.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(0.5f, 0.5f), cabin.MapId));
            entMan.EnsureComponent<VehicleWeaponsOperatorComponent>(crew).Vehicle = hull;
            server.PlayerMan.SetAttachedEntity(session, crew);

            Assert.Multiple(() =>
            {
                Assert.That(Expand(near).ContainsKey(session), "the crew did not hear someone beside the hull");
                Assert.That(Expand(far).ContainsKey(session), Is.False, "the crew heard someone far from the hull");
            });

            // The crewman's own line reaches them with its bubble still on them: they see the cabin, not the hull.
            var own = Expand(crew, session);
            Assert.That(own[session].BubbleSource, Is.Null);

            // Looking out at the hull, the bubble is drawn on it.
            entMan.System<SharedEyeSystem>().SetTarget(crew, hull);
            own = Expand(crew, session);
            Assert.That(own[session].BubbleSource, Is.EqualTo(hull));
        });

        await pair.CleanReturnAsync();

        Dictionary<ICommonSession, ChatSystem.ICChatRecipientData> Expand(EntityUid source, ICommonSession? already = null)
        {
            var recipients = new Dictionary<ICommonSession, ChatSystem.ICChatRecipientData>();
            if (already != null)
                recipients[already] = new ChatSystem.ICChatRecipientData(0f, false);

            entMan.EventBus.RaiseEvent(EventSource.Local, new ExpandICChatRecipientsEvent(source, ChatSystem.VoiceRange, recipients));
            return recipients;
        }
    }
}
