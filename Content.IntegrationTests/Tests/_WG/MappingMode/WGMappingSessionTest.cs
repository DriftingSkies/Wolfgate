#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.MappingMode;
using Content.Shared._WG.MappingMode;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using static Content.IntegrationTests.Tests._WG.MappingMode.WGMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WG.MappingMode;

/// <summary>
/// Sessions open on a fresh map, take members by code up to the cap, pass ownership on when the owner leaves, and
/// close with their maps once they stand empty past the grace.
/// </summary>
[TestFixture]
public sealed class WGMappingSessionTest
{
    [Test]
    public async Task CreateJoinLeaveAndHandover()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WGMappingModeSystem>();
        await Enable(pair);
        var players = await AddMappers(pair, 3);
        var (a, b, c) = (players[0], players[1], players[2]);

        WGMappingSession session = default!;
        EntityUid observerA = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Session Test", out var created), Is.True);
            session = created!;
            observerA = a.AttachedEntity!.Value;

            Assert.Multiple(() =>
            {
                Assert.That(session.Owner, Is.EqualTo(a.UserId));
                Assert.That(WGMappingSessionCodes.IsValid(session.Code), Is.True);
                Assert.That(entMan.HasComponent<WGMappingObserverComponent>(observerA), Is.True);
                Assert.That(entMan.GetComponent<TransformComponent>(observerA).MapUid, Is.EqualTo(session.RootMap));
                Assert.That(entMan.HasComponent<MapComponent>(session.RootMap), Is.True);
                Assert.That(mapping.IsSessionMap(session.RootMap, session), Is.True);
                Assert.That(mapping.SessionMaps(session.Id), Is.EqualTo(new[] { session.RootMap }));
                Assert.That(entMan.GetComponent<MetaDataComponent>(session.RootMap).EntityPaused, Is.True);
            });

            // Codes are matched however they are typed.
            Assert.That(mapping.TryGetSessionByCode($" {session.Code.ToLowerInvariant()} ", out var found), Is.True);
            Assert.That(found, Is.SameAs(session));
            Assert.That(mapping.TryJoinSession(b, found!), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(session.Members.Keys, Is.EquivalentTo(new[] { a.UserId, b.UserId }));
                Assert.That(session.Present.Keys, Is.EquivalentTo(new[] { a.UserId, b.UserId }));
                Assert.That(entMan.GetComponent<TransformComponent>(b.AttachedEntity!.Value).MapUid, Is.EqualTo(session.RootMap));
            });

            // The member cap counts everyone already in.
            server.CfgMan.SetCVar(WGMappingModeCVars.MaxMembers, 2);
            Assert.That(mapping.TryJoinSession(c, session), Is.False);
            Assert.That(session.Members.ContainsKey(c.UserId), Is.False);
            Assert.That(c.AttachedEntity, Is.Null);
            server.CfgMan.SetCVar(WGMappingModeCVars.MaxMembers, WGMappingModeCVars.MaxMembers.DefaultValue);

            // The owner leaves: back to the lobby, and the session passes to the member still there.
            mapping.LeaveSession(a.UserId, kicked: false);
            Assert.Multiple(() =>
            {
                Assert.That(a.AttachedEntity, Is.Null);
                Assert.That(mapping.TryGetMembership(a.UserId, out _), Is.False);
                Assert.That(session.Owner, Is.EqualTo(b.UserId));
                Assert.That(session.Members.Keys, Is.EquivalentTo(new[] { b.UserId }));
            });
        });

        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(observerA), Is.True, "The old observer goes with the leave.");

            // Back in by code: a member again, but not the owner.
            Assert.That(mapping.TryJoinSession(a, session), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(session.Owner, Is.EqualTo(b.UserId));
                Assert.That(session.Present.ContainsKey(a.UserId), Is.True);
                Assert.That(mapping.TryGetPresent(a, out var present) && present == session, Is.True);
            });

            // Creating another session quietly ends the old membership.
            Assert.That(mapping.TryCreateSession(a, "Second Session", out var second), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(session.Members.ContainsKey(a.UserId), Is.False);
                Assert.That(second!.Owner, Is.EqualTo(a.UserId));
                Assert.That(second.Code, Is.Not.EqualTo(session.Code));
                Assert.That(mapping.Sessions, Has.Count.EqualTo(2));
            });
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EmptySessionsKicksAndCodes()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WGMappingModeSystem>();
        await Enable(pair);
        var players = await AddMappers(pair, 3);
        var (a, b, c) = (players[0], players[1], players[2]);

        // Create then leave, over and over: each empty session closes at once instead of holding a slot.
        for (var i = 0; i < 4; i++)
        {
            WGMappingSession flood = default!;
            await server.WaitAssertion(() =>
            {
                Assert.That(mapping.TryCreateSession(a, $"Flood {i}", out var created), Is.True);
                flood = created!;
                RaiseAs(entMan, a, new WGMappingLeaveRequest());
                Assert.That(mapping.Sessions, Does.Not.Contain(flood));
            });

            await pair.RunTicksSync(2);
            await server.WaitAssertion(() => Assert.That(entMan.Deleted(flood.RootMap), Is.True));
        }

        WGMappingSession first = default!;
        WGMappingSession second = default!;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.Sessions, Is.Empty);

            // A new session closes the player's previous one when nobody else is in it.
            Assert.That(mapping.TryCreateSession(a, "First", out var created), Is.True);
            first = created!;
            Assert.That(mapping.TryCreateSession(a, "Second", out created), Is.True);
            second = created!;
            Assert.That(mapping.Sessions, Is.EquivalentTo(new[] { second }));

            // The per-player cap counts sessions they own.
            server.CfgMan.SetCVar(WGMappingModeCVars.MaxSessionsPerPlayer, 0);
            Assert.That(mapping.TryCreateSession(b, "Refused", out _), Is.False);
            Assert.That(mapping.Sessions, Has.Count.EqualTo(1));
            server.CfgMan.SetCVar(WGMappingModeCVars.MaxSessionsPerPlayer, WGMappingModeCVars.MaxSessionsPerPlayer.DefaultValue);

            Assert.That(mapping.TryJoinSession(b, second), Is.True);
            Assert.That(mapping.TryJoinSession(c, second), Is.True);

            // The owner removes b.
            RaiseAs(entMan, a, new WGMappingKickRequest(b.UserId));
            Assert.That(second.Members.ContainsKey(b.UserId), Is.False);
        });

        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(first.RootMap), Is.True, "The first session's map went with it.");
            Assert.That(b.AttachedEntity, Is.Null);

            // The kicked player can't come back with the code.
            RaiseAs(entMan, b, new WGMappingJoinRequest(second.Code));
            Assert.Multiple(() =>
            {
                Assert.That(second.Members.ContainsKey(b.UserId), Is.False);
                Assert.That(b.AttachedEntity, Is.Null);
                Assert.That(mapping.TryJoinSession(b, second), Is.False);
            });

            // Only the owner can replace the code; the old one then finds nothing, and members stay.
            var oldCode = second.Code;
            RaiseAs(entMan, c, new WGMappingNewCodeRequest());
            Assert.That(second.Code, Is.EqualTo(oldCode));

            RaiseAs(entMan, a, new WGMappingNewCodeRequest());
            Assert.Multiple(() =>
            {
                Assert.That(second.Code, Is.Not.EqualTo(oldCode));
                Assert.That(WGMappingSessionCodes.IsValid(second.Code), Is.True);
                Assert.That(mapping.TryGetSessionByCode(oldCode, out _), Is.False);
                Assert.That(mapping.TryGetSessionByCode(second.Code, out var found) && found == second, Is.True);
                Assert.That(second.Members.Keys, Is.EquivalentTo(new[] { a.UserId, c.UserId }));
            });
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AbsentMembersAndGrace()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WGMappingModeSystem>();
        await Enable(pair);
        var players = await AddMappers(pair, 2);
        var (a, b) = (players[0], players[1]);

        WGMappingSession session = default!;
        EntityUid observerB = default;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(WGMappingModeCVars.EmptySessionGrace, 2);
            Assert.That(mapping.TryCreateSession(a, "Grace Test", out var created), Is.True);
            session = created!;
            Assert.That(mapping.TryJoinSession(b, session), Is.True);
            observerB = b.AttachedEntity!.Value;
        });

        // A disconnect makes the member absent, but they stay a member and can rejoin.
        await server.RemoveDummySession(b);
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.Deleted(observerB), Is.True);
                Assert.That(session.Members.ContainsKey(b.UserId), Is.True);
                Assert.That(session.Present.ContainsKey(b.UserId), Is.False);
                Assert.That(session.EmptySince, Is.Null, "The owner is still there.");
            });

            // The owner leaves; the absent member is all that's left, so they own it, and nobody is present.
            mapping.LeaveSession(a.UserId, kicked: false);
            Assert.Multiple(() =>
            {
                Assert.That(session.Owner, Is.EqualTo(b.UserId));
                Assert.That(session.Present, Is.Empty);
                Assert.That(session.EmptySince, Is.Not.Null);
                Assert.That(mapping.Sessions, Does.Contain(session));
            });
        });

        // Inside the grace the session waits; past it, it closes and takes its maps with it.
        await pair.RunSeconds(1);
        await server.WaitAssertion(() => Assert.That(mapping.Sessions, Does.Contain(session)));

        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(mapping.Sessions, Does.Not.Contain(session));
                Assert.That(mapping.TryGetMembership(b.UserId, out _), Is.False);
                Assert.That(entMan.Deleted(session.RootMap), Is.True);
                Assert.That(mapping.SessionMaps(session.Id), Is.Empty);
            });

            server.CfgMan.SetCVar(WGMappingModeCVars.EmptySessionGrace, WGMappingModeCVars.EmptySessionGrace.DefaultValue);
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
