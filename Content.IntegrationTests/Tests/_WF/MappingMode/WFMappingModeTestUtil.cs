#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Administration.Managers;
using Content.Server.Voting.Managers;
using Content.Shared._WF.MappingMode;
using Content.Shared.CCVar;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>Setup shared by the mapping mode tests: the mode switch, plain mappers and requests sent as a player.</summary>
public static class WFMappingModeTestUtil
{
    /// <summary>A lobby pair that is thrown away afterwards, since the mode forces cvars it doesn't restore.</summary>
    public static PoolSettings Settings => new() { InLobby = true, Dirty = true };

    /// <summary>
    /// Turns mapping mode on. Local players stop being promoted to host, so dummies are plain mappers; with
    /// <paramref name="demoteClient"/> the test client loses its host flags too. Lobby votes are cancelled and kept
    /// off, since dummy sessions can't receive the messages votes send them directly.
    /// </summary>
    public static async Task Enable(TestPair pair, bool demoteClient = true)
    {
        var server = pair.Server;
        var admins = server.ResolveDependency<IAdminManager>();
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, false);
            foreach (var vote in server.ResolveDependency<IVoteManager>().ActiveVotes.ToArray())
            {
                vote.Cancel();
            }

            server.CfgMan.SetCVar(CCVars.ConsoleLoginLocal, false);
            server.CfgMan.SetCVar(WFMappingModeCVars.Enabled, true);
            if (demoteClient)
                admins.ReloadAdmin(pair.Player!);
        });

        for (var i = 0; i < 100 && demoteClient && admins.IsAdmin(pair.Player!, includeDeAdmin: true); i++)
        {
            await pair.RunTicksSync(1);
        }

        await pair.RunTicksSync(5);
        Assert.That(demoteClient && admins.IsAdmin(pair.Player!, includeDeAdmin: true), Is.False,
            "The test client should have lost its host flags.");
    }

    /// <summary>Turns mapping mode off and lets local players be hosts again; votes stay off.</summary>
    public static async Task Disable(TestPair pair)
    {
        var server = pair.Server;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(WFMappingModeCVars.Enabled, false);
            server.CfgMan.SetCVar(CCVars.ConsoleLoginLocal, CCVars.ConsoleLoginLocal.DefaultValue);
        });
        await pair.RunTicksSync(5);
    }

    /// <summary>Adds dummy players and waits until they sit in the lobby.</summary>
    public static async Task<ICommonSession[]> AddMappers(TestPair pair, int count)
    {
        var sessions = await pair.Server.AddDummySessions(count);
        for (var i = 0; i < 100 && sessions.Any(s => s.Status != SessionStatus.InGame); i++)
        {
            await pair.RunTicksSync(1);
        }

        await pair.RunTicksSync(5);
        var admins = pair.Server.ResolveDependency<IAdminManager>();
        Assert.Multiple(() =>
        {
            foreach (var session in sessions)
            {
                Assert.That(session.Status, Is.EqualTo(SessionStatus.InGame), session.Name);
                Assert.That(session.AttachedEntity, Is.Null, $"{session.Name} should be in the lobby.");
                Assert.That(admins.IsAdmin(session, includeDeAdmin: true), Is.False, $"{session.Name} should be no admin.");
            }
        });

        return sessions;
    }

    /// <summary>Raises a request on the server as if <paramref name="session"/> had sent it over the network.</summary>
    public static void RaiseAs<T>(IEntityManager entMan, ICommonSession session, T message) where T : notnull
    {
        var type = typeof(EntitySessionEventArgs).Assembly
            .GetType("Robust.Shared.GameObjects.EntitySessionMessage`1", true)!
            .MakeGenericType(typeof(T));
        var wrapped = Activator.CreateInstance(type, new EntitySessionEventArgs(session), message)!;
        entMan.EventBus.RaiseEvent(EventSource.Network, wrapped);
    }
}
