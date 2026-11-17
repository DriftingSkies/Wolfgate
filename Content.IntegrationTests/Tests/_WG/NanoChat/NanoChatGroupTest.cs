#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WG.NanoChat;
using Content.Server.CartridgeLoader;
using Content.Shared._WG.NanoChat;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.NanoChat;

/// <summary>
/// A room's owner adds contacts and kicks members, members leave, and messages reach whoever is in it. A company group
/// follows company membership and can't be left.
/// </summary>
[TestFixture]
[TestOf(typeof(NanoChatGroupSystem))]
public sealed class NanoChatGroupTest
{
    private const string Pda = "PassengerPDA";
    private const string Server = "TelecomServerFilledCommon";

    [Test]
    public async Task RoomsAddKickAndLeave()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var pdas = await Spawn(pair, 3);

        uint groupId = 0;
        await server.WaitAssertion(() =>
        {
            var (a, b, c) = (CardOf(entMan, pdas[0]), CardOf(entMan, pdas[1]), CardOf(entMan, pdas[2]));
            var groups = entMan.System<NanoChatGroupSystem>();

            Act(entMan, pdas[0], NanoChatUiMessageType.NewChat, b.Comp.Number, "Bee");
            Act(entMan, pdas[0], NanoChatUiMessageType.NewChat, c.Comp.Number, "Cee");

            // 1234 isn't a contact, so it stays out.
            Act(entMan, pdas[0], NanoChatUiMessageType.CreateGroup, content: "Crew",
                numbers: new List<uint> { b.Comp.Number!.Value, c.Comp.Number!.Value, 1234 });
            Assert.That(a.Comp.CurrentChat, Is.Not.Null, "Making a group should open it.");
            groupId = a.Comp.CurrentChat!.Value;
            Assert.That(SharedNanoChatSystem.IsGroup(groupId));

            var group = groups.Get(groupId)!;
            Assert.That(group.Owner, Is.EqualTo(a.Comp.Number));
            Assert.That(group.Members, Is.EqualTo(new[] { a.Comp.Number!.Value, b.Comp.Number!.Value, c.Comp.Number!.Value }));
            Assert.That(b.Comp.Recipients[groupId].Name, Is.EqualTo("Crew"));
            Assert.That(c.Comp.Recipients.ContainsKey(groupId));

            Act(entMan, pdas[0], NanoChatUiMessageType.SendMessage, groupId, "hello crew");
            foreach (var member in new[] { b, c })
            {
                var received = member.Comp.Messages[groupId].Single();
                Assert.That(received.Content, Is.EqualTo("hello crew"));
                Assert.That(received.SenderName, Is.Not.Null.And.Not.Empty, "A group message should name its sender.");
                Assert.That(member.Comp.Recipients[groupId].HasUnread);
            }

            // Only the owner kicks.
            Act(entMan, pdas[1], NanoChatUiMessageType.KickMember, groupId, numbers: new List<uint> { c.Comp.Number!.Value });
            Assert.That(group.Members, Does.Contain(c.Comp.Number!.Value), "A member who isn't the owner kicked someone.");

            Act(entMan, pdas[0], NanoChatUiMessageType.KickMember, groupId, numbers: new List<uint> { c.Comp.Number!.Value });
            Assert.That(group.Members, Does.Not.Contain(c.Comp.Number!.Value));
            Assert.That(c.Comp.Recipients.ContainsKey(groupId), Is.False, "A kicked member still has the group.");
        });

        // Past the one-second sending limit.
        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            var (a, b, c) = (CardOf(entMan, pdas[0]), CardOf(entMan, pdas[1]), CardOf(entMan, pdas[2]));
            var groups = entMan.System<NanoChatGroupSystem>();

            Act(entMan, pdas[0], NanoChatUiMessageType.SendMessage, groupId, "after the kick");
            Assert.That(b.Comp.Messages[groupId].Select(m => m.Content), Is.EqualTo(new[] { "hello crew", "after the kick" }));
            Assert.That(c.Comp.Messages.ContainsKey(groupId), Is.False, "A kicked member got a group message.");

            // The owner adds the kicked member back, who gets the history.
            Act(entMan, pdas[0], NanoChatUiMessageType.AddMembers, groupId, numbers: new List<uint> { c.Comp.Number!.Value });
            Assert.That(c.Comp.Messages[groupId].Select(m => m.Content), Is.EqualTo(new[] { "hello crew", "after the kick" }));

            // Deleting the chat leaves the room.
            Act(entMan, pdas[1], NanoChatUiMessageType.DeleteChat, groupId);
            Assert.That(b.Comp.Recipients.ContainsKey(groupId), Is.False);
            Assert.That(groups.Get(groupId)!.Members, Does.Not.Contain(b.Comp.Number!.Value));

            // The owner leaving hands the room on; the last one out ends it.
            Act(entMan, pdas[0], NanoChatUiMessageType.LeaveGroup, groupId);
            Assert.That(groups.Get(groupId)!.Owner, Is.EqualTo(c.Comp.Number));
            Act(entMan, pdas[2], NanoChatUiMessageType.LeaveGroup, groupId);
            Assert.That(groups.Get(groupId), Is.Null, "An empty room should be gone.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CompanyGroupFollowsMembership()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var pdas = await Spawn(pair, 3);

        await server.WaitAssertion(() =>
        {
            var (a, b, c) = (CardOf(entMan, pdas[0]), CardOf(entMan, pdas[1]), CardOf(entMan, pdas[2]));
            var groups = entMan.System<NanoChatGroupSystem>();

            groups.SetCompany(a.Comp.Number!.Value, (1, "Acme"));
            groups.SetCompany(b.Comp.Number!.Value, (1, "Acme"));

            var groupId = a.Comp.Recipients.Keys.Single(SharedNanoChatSystem.IsGroup);
            Assert.That(a.Comp.Recipients[groupId].Name, Is.EqualTo("Acme"));
            Assert.That(b.Comp.Recipients.ContainsKey(groupId));
            Assert.That(c.Comp.Recipients.Keys.Any(SharedNanoChatSystem.IsGroup), Is.False, "Someone outside the company got its group.");

            Act(entMan, pdas[0], NanoChatUiMessageType.SendMessage, groupId, "company memo");
            Assert.That(b.Comp.Messages[groupId].Single().Content, Is.EqualTo("company memo"));

            // A company group can't be left or managed by hand.
            Act(entMan, pdas[0], NanoChatUiMessageType.DeleteChat, groupId);
            Act(entMan, pdas[0], NanoChatUiMessageType.KickMember, groupId, numbers: new List<uint> { b.Comp.Number!.Value });
            Assert.That(a.Comp.Recipients.ContainsKey(groupId));
            Assert.That(groups.Get(groupId)!.Members, Does.Contain(b.Comp.Number!.Value));

            // Leaving the company takes the group away; a rename follows the company.
            groups.SetCompany(b.Comp.Number!.Value, null);
            Assert.That(b.Comp.Recipients.ContainsKey(groupId), Is.False);
            groups.SetCompany(a.Comp.Number!.Value, (1, "Acme Mining"));
            Assert.That(a.Comp.Recipients[groupId].Name, Is.EqualTo("Acme Mining"));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A company group holds every member, past a room's member limit.</summary>
    [Test]
    public async Task CompanyGroupHasNoMemberLimit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var pdas = await Spawn(pair, 1);

        await server.WaitAssertion(() =>
        {
            var a = CardOf(entMan, pdas[0]);
            var groups = entMan.System<NanoChatGroupSystem>();
            var own = a.Comp.Number!.Value;
            groups.SetCompany(own, (2, "Bigco"));
            var groupId = a.Comp.Recipients.Keys.Single(SharedNanoChatSystem.IsGroup);

            var others = Enumerable.Range(1, NanoChatGroupSystem.MaxMembers + 10)
                .Select(n => (uint) n)
                .Where(n => n != own)
                .ToList();
            foreach (var number in others)
            {
                groups.SetCompany(number, (2, "Bigco"));
            }

            Assert.That(groups.Get(groupId)!.Members, Has.Count.EqualTo(others.Count + 1));
        });

        await pair.CleanReturnAsync();
    }

    private static async Task<EntityUid[]> Spawn(TestPair pair, int count)
    {
        var entMan = pair.Server.EntMan;
        var map = await pair.CreateTestMap();
        var pdas = new EntityUid[count];
        await pair.Server.WaitPost(() =>
        {
            var telecom = entMan.SpawnEntity(Server, map.GridCoords);
            entMan.System<SharedPowerReceiverSystem>().SetNeedsPower(telecom, false);
            for (var i = 0; i < count; i++)
            {
                pdas[i] = entMan.SpawnEntity(Pda, map.GridCoords);
            }
        });

        // The power net updates twice a second.
        await pair.RunTicksSync(90);
        return pdas;
    }

    private static Entity<NanoChatCardComponent> CardOf(IEntityManager entMan, EntityUid pda)
    {
        var id = entMan.GetComponent<PdaComponent>(pda).ContainedId!.Value;
        return (id, entMan.GetComponent<NanoChatCardComponent>(id));
    }

    private static void Act(IEntityManager entMan,
        EntityUid pda,
        NanoChatUiMessageType type,
        uint? number = null,
        string? content = null,
        List<uint>? numbers = null)
    {
        Assert.That(entMan.System<CartridgeLoaderSystem>().TryGetProgram<NanoChatCartridgeComponent>(pda, out var app, out _));
        var msg = new NanoChatUiMessageEvent(type, number, content, numbers: numbers)
        {
            LoaderUid = entMan.GetNetEntity(pda),
            Actor = pda,
            User = pda,
        };
        entMan.EventBus.RaiseLocalEvent<CartridgeMessageEvent>(app!.Value, msg);
    }
}
