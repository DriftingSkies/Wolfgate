#nullable enable
using System.Linq;
using Content.Client.PDA;
using Content.Server._WF.NanoChat;
using Content.Server.CartridgeLoader;
using Content.Server.Silicons.Borgs;
using Content.Shared._WF.NanoChat;
using Content.Shared.CartridgeLoader;
using Content.Shared.Mind;
using Content.Shared.PDA;
using Content.Shared.Players;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.UserInterface;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Cyborgs;

/// <summary>
/// A cyborg's built-in PDA has a vagrant's apps and an action to open it, and its NanoChat reads the cyborg's issued ID
/// card, held or stowed, so it can message a PDA and be messaged back. Its player can open it on the client.
/// </summary>
[TestFixture]
public sealed class BorgPdaTest
{
    private const string Borg = "BorgChassisEngineer";
    private const string Cell = "PowerCellHigh";
    private const string Tool = "BorgModuleTool";
    private const string Card = "WFBorgIdCard";
    private const string Pda = "ContractorPDA";
    private const string Server = "TelecomServerFilledCommon";

    [Test]
    public async Task BorgPdaHasVagrantAppsAndChatsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid borg = default;
        EntityUid pda = default;
        await server.WaitPost(() =>
        {
            var telecom = entMan.SpawnEntity(Server, map.GridCoords);
            entMan.System<SharedPowerReceiverSystem>().SetNeedsPower(telecom, false);
            borg = entMan.SpawnEntity(Borg, map.GridCoords);
            pda = entMan.SpawnEntity(Pda, map.GridCoords);
        });

        // The power net updates twice a second.
        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            string? Proto(EntityUid uid) => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
            var loader = entMan.System<CartridgeLoaderSystem>();
            var nanoChat = entMan.System<NanoChatSystem>();

            // The same apps as a vagrant's PDA.
            var installed = loader.GetInstalled(borg).Select(Proto).ToHashSet();
            var vagrant = loader.GetInstalled(pda).Select(Proto).ToHashSet();
            Assert.That(vagrant, Is.Not.Empty);
            Assert.That(installed, Is.EquivalentTo(vagrant), "A cyborg's PDA apps differ from a vagrant's; match BaseBorgChassis to BasePDA.");

            var intrinsic = entMan.GetComponent<IntrinsicUIComponent>(borg);
            Assert.That(intrinsic.UIs.TryGetValue(PdaUiKey.Key, out var entry) && entry.ToggleActionEntity is { Valid: true },
                "The cyborg has no action to open its PDA.");
            Assert.That(entMan.GetComponent<PdaComponent>(borg).OwnerName, Is.EqualTo(entMan.GetComponent<MetaDataComponent>(borg).EntityName));

            // The tool module puts the cyborg's ID card in hand.
            var borgs = entMan.System<BorgSystem>();
            borgs.InstallAllModules(borg);
            var modules = entMan.GetComponent<BorgChassisComponent>(borg).ModuleContainer.ContainedEntities;
            borgs.UnselectModule(borg);
            borgs.SelectModule(borg, modules.Single(m => Proto(m) == Tool));

            Assert.That(nanoChat.TryGetCard(borg, out var borgCard), "The cyborg's PDA reads no card.");
            Assert.That(Proto(borgCard!.Value), Is.EqualTo(Card));
            Assert.That(nanoChat.GetPda(borgCard.Value), Is.EqualTo(borg));

            Assert.That(nanoChat.TryGetCard(pda, out var pdaCard));
            var borgNumber = borgCard.Value.Comp.Number!.Value;
            var pdaNumber = pdaCard!.Value.Comp.Number!.Value;

            Assert.That(loader.TryGetProgram<NanoChatCartridgeComponent>(pda, out var pdaApp, out _));
            Send(entMan, pda, pdaApp!.Value, borgNumber, "hello cyborg");
            Assert.That(borgCard.Value.Comp.Messages.TryGetValue(pdaNumber, out var received), "Nothing reached the cyborg.");
            Assert.That(received!.Select(m => m.Content), Is.EqualTo(new[] { "hello cyborg" }));

            Assert.That(loader.TryGetProgram<NanoChatCartridgeComponent>(borg, out var borgApp, out _), "The cyborg has no NanoChat.");
            Send(entMan, borg, borgApp!.Value, pdaNumber, "beep");
            Assert.That(pdaCard.Value.Comp.Messages[borgNumber].Select(m => m.Content), Is.EqualTo(new[] { "hello cyborg", "beep" }));
            Assert.That(borgCard.Value.Comp.Messages[pdaNumber][^1].DeliveryFailed, Is.False);

            // Putting the module away stows the card in it, and the PDA still reads it.
            borgs.UnselectModule(borg);
            Assert.That(nanoChat.TryGetCard(borg, out var stowed) && stowed.Value.Owner == borgCard.Value.Owner,
                "The cyborg's PDA lost its card when the module was put away.");
            Assert.That(nanoChat.GetPda(borgCard.Value), Is.EqualTo(borg));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A cyborg's player opens its PDA and NanoChat, and the client draws the window.</summary>
    [Test]
    public async Task BorgOpensPdaOnClientTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var minds = entMan.System<SharedMindSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var session = server.PlayerMan.GetSessionById(client.Session!.UserId);

        EntityUid borg = default;
        await server.WaitPost(() =>
        {
            borg = entMan.SpawnEntity(Borg, map.GridCoords);
            var cell = entMan.SpawnEntity(Cell, map.GridCoords);
            containers.Insert(cell, containers.GetContainer(borg, "cell_slot"), force: true);

            minds.WipeMind(session.ContentData()?.Mind);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, borg);
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.System<IntrinsicUISystem>().InteractUI(borg, PdaUiKey.Key), "The cyborg could not open its PDA.");

            var loader = entMan.System<CartridgeLoaderSystem>();
            Assert.That(loader.TryGetProgram<NanoChatCartridgeComponent>(borg, out var app, out _));
            loader.ActivateProgram(borg, app!.Value);
        });
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var clientBorg = client.EntMan.GetEntity(entMan.GetNetEntity(borg));
            Assert.That(client.EntMan.System<SharedUserInterfaceSystem>()
                .TryGetOpenUi<PdaBoundUserInterface>(clientBorg, PdaUiKey.Key, out _), "The PDA window did not open on the client.");
        });

        // A client still attached to a cyborg throws while the pair's cleanup deletes it.
        await server.WaitPost(() => minds.WipeMind(session.ContentData()?.Mind));
        await pair.RunTicksSync(5);

        await pair.CleanReturnAsync();
    }

    private static void Send(IEntityManager entMan, EntityUid loader, EntityUid cartridge, uint number, string text)
    {
        var msg = new NanoChatUiMessageEvent(NanoChatUiMessageType.SendMessage, number, text)
        {
            LoaderUid = entMan.GetNetEntity(loader),
            Actor = loader,
            User = loader,
        };
        entMan.EventBus.RaiseLocalEvent<CartridgeMessageEvent>(cartridge, msg);
    }
}
