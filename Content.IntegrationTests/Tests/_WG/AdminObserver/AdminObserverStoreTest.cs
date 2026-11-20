#nullable enable
using System.Linq;
using Content.Client._WG.AdminObserver;
using Content.IntegrationTests.Pair;
using Content.Server._WG.AdminObserver;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared._WG.AdminObserver;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WG.AdminObserver;

/// <summary>An admin's observer document is saved, comes back after a reconnect, and is closed to everyone else.</summary>
[TestFixture]
[TestOf(typeof(WGAdminObserverStore))]
public sealed class AdminObserverStoreTest
{
    private const string Crate = "CrateGenericSteel";

    [Test]
    public async Task SavedDocumentSurvivesReconnect()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;

        var admins = server.ResolveDependency<IAdminManager>();
        var players = server.ResolveDependency<IPlayerManager>();
        var db = server.ResolveDependency<IServerDbManager>();
        var clientNet = client.ResolveDependency<IClientNetManager>();
        var store = server.System<WGAdminObserverStore>();

        var session = players.Sessions.Single();
        var user = session.UserId;
        var name = session.Name;

        // Without the flag nothing loads and nothing can be changed.
        await server.WaitPost(() =>
        {
            if (admins.IsAdmin(session, includeDeAdmin: true))
                admins.DeAdmin(session);
        });
        await client.WaitPost(() => client.System<WGAdminObserverClientSystem>().Request());
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(store.Get(session), Is.Null);
                Assert.That(store.IsLoaded(user), Is.False, "A request without the flag should load nothing.");
                Assert.That(store.Change(session, data => data.DefaultPreset = "x"), Is.False);
            });
        });
        Assert.That(client.System<WGAdminObserverClientSystem>().Data, Is.Null);

        // With it, the document loads and reaches the client.
        await Promote(pair, session);
        await WaitFor(pair, () => store.Get(session) != null, "The document never loaded.");
        await pair.RunTicksSync(5);
        Assert.That(client.System<WGAdminObserverClientSystem>().Data, Is.Not.Null, "The client never got its document.");

        await server.WaitAssertion(() =>
        {
            var changed = store.Change(session, data =>
            {
                data.Marker.Wording = WGMarkerWording.Staff;
                data.Presets.Add(new WGAvatarPreset { Name = "Crate", Kind = WGAvatarKind.Object, Prototype = Crate });
                data.Presets.Add(new WGAvatarPreset { Name = "Gone", Kind = WGAvatarKind.Object, Prototype = "WGNoSuchEntity" });
                data.DefaultPreset = "Crate";
            });
            Assert.That(changed);
        });
        await pair.RunTicksSync(5);

        var seen = client.System<WGAdminObserverClientSystem>().Data;
        Assert.Multiple(() =>
        {
            Assert.That(seen?.Marker.Wording, Is.EqualTo(WGMarkerWording.Staff));
            Assert.That(seen?.Presets.Select(p => p.Name), Does.Contain("Crate").And.Not.Contain("Gone"), "An unknown prototype should be dropped.");
        });

        // The write follows the change by itself.
        string? saved = null;
        for (var i = 0; i < 20 && saved == null; i++)
        {
            await pair.RunTicksSync(30);
            saved = await Await(pair, () => db.GetAdminObserverDataAsync(user.UserId));
        }

        Assert.That(saved, Does.Contain(Crate), "The document was never written.");

        // A change made just before leaving is written on the way out.
        await server.WaitAssertion(() =>
        {
            Assert.That(store.Change(session, data => data.Marker.Wording = WGMarkerWording.RankAndName));
        });
        await client.WaitPost(() => clientNet.ClientDisconnect("For testing"));
        await pair.RunTicksSync(10);
        await WaitFor(pair, () => !store.IsLoaded(user), "The document stayed in memory after the admin left.");

        await client.WaitPost(() => clientNet.ClientConnect(null!, 0, name));
        await pair.RunTicksSync(10);

        session = players.Sessions.Single();
        Assert.That(session.UserId, Is.EqualTo(user));
        await Promote(pair, session);
        await WaitFor(pair, () => store.Get(session) != null, "The document never loaded after reconnecting.");
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var data = store.Get(session)!;
            Assert.Multiple(() =>
            {
                Assert.That(data.Marker.Wording, Is.EqualTo(WGMarkerWording.RankAndName));
                Assert.That(data.Presets.Select(p => p.Name), Does.Contain("Crate").And.Not.Contain("Gone"));
                Assert.That(data.DefaultPreset, Is.EqualTo("Crate"));
            });
        });
        Assert.That(client.System<WGAdminObserverClientSystem>().Data?.DefaultPreset, Is.EqualTo("Crate"));

        // Losing the flag closes it again and tells the client.
        await server.WaitPost(() => admins.DeAdmin(session));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() => Assert.That(store.Get(session), Is.Null));
        Assert.That(client.System<WGAdminObserverClientSystem>().Data, Is.Null);

        await pair.CleanReturnAsync();
    }

    private static async Task Promote(TestPair pair, ICommonSession session)
    {
        var admins = pair.Server.ResolveDependency<IAdminManager>();
        await pair.Server.WaitPost(() => admins.PromoteHost(session));
        await WaitFor(pair, () => admins.HasAdminFlag(session, AdminFlags.Admin), "The player never became an admin.");
    }

    private static async Task WaitFor(TestPair pair, Func<bool> done, string message)
    {
        var met = false;
        for (var i = 0; i < 300 && !met; i++)
        {
            await pair.RunTicksSync(1);
            await pair.Server.WaitPost(() => met = done());
        }

        Assert.That(met, message);
    }

    private static async Task<T> Await<T>(TestPair pair, Func<Task<T>> start)
    {
        Task<T> task = default!;
        await pair.Server.WaitPost(() => task = start());
        for (var i = 0; i < 600 && !task.IsCompleted; i++)
        {
            await pair.RunTicksSync(1);
        }

        Assert.That(task.IsCompleted, "The database call never finished.");
        return await task;
    }
}
