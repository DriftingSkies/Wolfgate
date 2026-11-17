#nullable enable
using System.Linq;
using System.Reflection;
using Content.Server._Mono.PDV.Components;
using Content.Server._WG.Biotype;
using Content.Client.UserInterface.Systems.Chat;
using Content.Server.Chat.Systems;
using Content.Server.Shuttles.Systems;
using Content.Server.VoiceMask;
using Content.Shared._Mono.Claws.Components;
using Content.Shared._NF.Contraband.Events;
using Content.Shared._WG.Biotype;
using Content.Shared.Chat;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Contraband;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Speech;
using Content.Shared.Stunnable;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>
/// Fixes for reports that came in through the feedback panel and have no module of their own.
/// </summary>
[TestFixture]
public sealed class PlayerReportFixesTest
{
    private static readonly ProtoId<SpeciesPrototype> ReptilianProto = "Reptilian";

    /// <summary>The bounty pays for an executed heretic; any other corpse stays off the books.</summary>
    [Test]
    public async Task DeadHereticSellsAndOtherCorpsesDoNot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        var heretic = EntityUid.Invalid;
        var corpse = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var mobState = entMan.System<MobStateSystem>();

            EntityUid Body(bool isHeretic)
            {
                var body = entMan.SpawnEntity("MobHuman", map.GridCoords);
                entMan.EnsureComponent<ContrabandComponent>(body).TurnInValues["Doubloon"] = 250;
                if (isHeretic)
                    entMan.AddComponent<HereticComponent>(body);
                mobState.ChangeMobState(body, MobState.Dead);
                return body;
            }

            var pallet = entMan.SpawnEntity("ContrabandPalletPirate", map.GridCoords);
            Assert.That(entMan.GetComponent<TransformComponent>(pallet).Anchored, "The pallet needs to be anchored to sell from.");
            heretic = Body(true);
            corpse = Body(false);

            var console = entMan.SpawnEntity("ComputerContrabandPalletConsolePirate", map.GridCoords);
            var message = new ContrabandPalletSellMessage { Actor = console };
            entMan.EventBus.RaiseLocalEvent(console, message);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(heretic), Is.True, "The heretic's body didn't sell.");
            Assert.That(entMan.Deleted(corpse), Is.False, "An ordinary corpse sold.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A voice mask's speech style replaces the wearer's own, and a language's verbs, in local chat; a name override keeps the mask out of it.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task VoiceMaskSpeechStyleChangesTheVerb(bool nameOverride)
    {
        const string Style = "Reptilian";

        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();

        var body = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            var mindSys = entMan.System<SharedMindSystem>();
            body = entMan.SpawnEntity("MobAvali", map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(pair.Player!.UserId).Owner, body);
            var mask = entMan.SpawnEntity("ClothingMaskGasVoiceChameleon", map.GridCoords);
            Assert.That(entMan.System<InventorySystem>().TryEquip(body, mask, "mask", silent: true, force: true));
            entMan.GetComponent<VoiceMaskComponent>(mask).VoiceMaskSpeechVerb = Style;
        });
        await pair.RunTicksSync(10);

        var history = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>().History;
        var seen = history.Count;
        await server.WaitPost(() => entMan.System<ChatSystem>().TrySendInGameICMessage(body, "hello", InGameICChatType.Speak,
            false, nameOverride: nameOverride ? "Announcer" : null, ignoreActionBlocker: true));
        await pair.RunTicksSync(10);

        var verbs = protos.Index<SpeechVerbPrototype>(Style).SpeechVerbStrings.Select(id => loc.GetString(id)).ToList();
        // The wearer's own verbs share "chirps" with the mask's style, so that word can't show the mask was left out.
        if (nameOverride)
            verbs = verbs.Except(protos.Index<SpeechVerbPrototype>("Parrot").SpeechVerbStrings.Select(id => loc.GetString(id))).ToList();

        var wrapped = history.Skip(seen).Select(entry => entry.Msg.WrappedMessage).LastOrDefault(line => line.Contains("ello"));
        Assert.That(wrapped, Is.Not.Null, "The player never heard the line.");
        Assert.That(verbs.Any(wrapped!.Contains), Is.EqualTo(!nameOverride), $"Wrong speech style use: {wrapped}");

        await pair.CleanReturnAsync();
    }

    /// <summary>A Diona keeps its footing through an FTL jump; everyone else goes over.</summary>
    [Test]
    public async Task DionaStaysUpThroughFtl()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var diona = entMan.SpawnEntity("MobDiona", map.GridCoords);
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);

            var jump = typeof(ShuttleSystem).GetMethod("DoTheDinosaur", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(jump, Is.Not.Null, "DoTheDinosaur moved; repoint this test.");
            jump!.Invoke(entMan.System<ShuttleSystem>(), new object[] { entMan.GetComponent<TransformComponent>(map.Grid) });

            Assert.That(entMan.HasComponent<KnockedDownComponent>(human), Is.True, "FTL no longer knocks crew down.");
            Assert.That(entMan.HasComponent<KnockedDownComponent>(diona), Is.False, "FTL knocked a Diona down.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Wirecutters on yourself clip your claws whichever biotype the lizard is.</summary>
    [TestCase(WGBiotype.Organic)]
    [TestCase(WGBiotype.Synthetic)]
    public async Task ReptilianClipsOwnClaws(WGBiotype biotype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var protos = server.ResolveDependency<IPrototypeManager>();

        var lizard = EntityUid.Invalid;
        ProtoId<Content.Shared._Mono.Claws.ClawPrototype> grown = default;

        await server.WaitPost(() =>
        {
            lizard = entMan.SpawnEntity("MobReptilian", map.GridCoords);
            var biotypes = entMan.System<WGBiotypeSystem>();
            biotypes.Apply(lizard, biotype, protos.Index<SpeciesPrototype>(ReptilianProto));
            Assert.That(biotypes.GetBiotype(lizard), Is.EqualTo(biotype), "The lizard didn't become this biotype.");
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            var claws = entMan.GetComponent<ClawsComponent>(lizard);
            grown = claws.Claws[2];
            claws.ClawStage = grown;

            var cutters = entMan.SpawnEntity("Wirecutter", map.GridCoords);
            Assert.That(entMan.System<SharedHandsSystem>().TryPickupAnyHand(lizard, cutters), "The lizard couldn't hold the wirecutters.");
            entMan.System<SharedInteractionSystem>().UserInteraction(lizard, entMan.GetComponent<TransformComponent>(lizard).Coordinates, lizard);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(5f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<ClawsComponent>(lizard).ClawStage, Is.Not.EqualTo(grown), "The claws weren't clipped."));

        await pair.CleanReturnAsync();
    }
}
