#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Content.IntegrationTests.Pair;
using Content.Server._WG.AdminObserver;
using Content.Server.Administration.Managers;
using Content.Shared._WG.AdminObserver;
using Content.Shared.Administration;
using Content.Shared.Mind;
using Content.Shared.Players;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WG.AdminObserver;

/// <summary>Shared steps of the admin observer tests: an admin player, and an observer for them to sit in.</summary>
public static class AdminKitFixture
{
    public const string Observer = "AdminObserver";

    /// <summary>Makes the pair's player a full admin and waits for their observer document.</summary>
    public static async Task<ICommonSession> Admin(TestPair pair)
    {
        var server = pair.Server;
        var admins = server.ResolveDependency<IAdminManager>();
        var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();
        var store = server.System<WGAdminObserverStore>();

        await server.WaitPost(() => admins.PromoteHost(session));
        await WaitFor(pair, () => admins.HasAdminFlag(session, AdminFlags.Admin) && store.Get(session) != null,
            "The player never became an admin with a loaded document.");
        return session;
    }

    /// <summary>Spawns an admin observer and puts the player in it; leaving the last one saves and deletes it.</summary>
    public static async Task<EntityUid> Enter(TestPair pair, ICommonSession session, EntityCoordinates coords)
    {
        var server = pair.Server;
        var minds = server.System<SharedMindSystem>();
        EntityUid observer = default;
        await server.WaitPost(() =>
        {
            minds.WipeMind(session.ContentData()?.Mind);
            observer = server.EntMan.SpawnEntity(Observer, coords);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, observer);
        });
        await pair.RunTicksSync(5);
        return observer;
    }

    /// <summary>Puts the pair's player in a new living human, with a mind of their own.</summary>
    public static async Task<EntityUid> Living(TestPair pair, EntityCoordinates coords)
    {
        var server = pair.Server;
        var minds = server.System<SharedMindSystem>();
        var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();
        EntityUid human = default;
        await server.WaitPost(() =>
        {
            minds.WipeMind(session.ContentData()?.Mind);
            human = server.EntMan.SpawnEntity("MobHuman", coords);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, human);
        });
        await pair.RunTicksSync(5);
        return human;
    }

    /// <summary>Spawns an admin observer nobody is in.</summary>
    public static async Task<EntityUid> Unmanned(TestPair pair, EntityCoordinates coords)
    {
        EntityUid observer = default;
        await pair.Server.WaitPost(() => observer = pair.Server.EntMan.SpawnEntity(Observer, coords));
        await pair.RunTicksSync(5);
        return observer;
    }

    /// <summary>Takes the player out of whatever they are in.</summary>
    public static async Task Leave(TestPair pair, ICommonSession session)
    {
        var players = pair.Server.ResolveDependency<IPlayerManager>();
        await pair.Server.WaitPost(() => players.SetAttachedEntity(session, null));
        await pair.RunTicksSync(5);
    }

    /// <summary>A layout as text, for comparing two of them.</summary>
    public static string Text(WGKitLayout? layout)
    {
        return JsonSerializer.Serialize(layout);
    }

    /// <summary>Every prototype in a layout, contents included.</summary>
    public static List<string> Prototypes(WGKitLayout layout)
    {
        var found = new List<string>();
        foreach (var node in layout.Slots.Values.Concat(layout.Hands.Values))
        {
            Collect(node, found);
        }

        return found;
    }

    private static void Collect(WGKitNode node, List<string> found)
    {
        found.Add(node.Prototype);
        foreach (var stored in node.Contents)
        {
            Collect(stored.Node, found);
        }
    }

    public static async Task WaitFor(TestPair pair, Func<bool> done, string message)
    {
        var met = false;
        for (var i = 0; i < 300 && !met; i++)
        {
            await pair.RunTicksSync(1);
            await pair.Server.WaitPost(() => met = done());
        }

        Assert.That(met, message);
    }
}
