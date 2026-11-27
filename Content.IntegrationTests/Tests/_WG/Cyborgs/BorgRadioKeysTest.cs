#nullable enable
using System.Linq;
using Content.Shared._CorvaxNext.Silicons.Borgs;
using Content.Shared._CorvaxNext.Silicons.Borgs.Components;
using Content.Shared.Interaction;
using Content.Shared.Lock;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Wires;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Cyborgs;

/// <summary>
/// A cyborg takes encryption keys once its lock and panel are open, and they add to its built-in channels.
/// </summary>
[TestFixture]
public sealed class BorgRadioKeysTest
{
    private const string Borg = "BorgChassisGeneric";
    private const string User = "MobHuman";
    private const string Key = "EncryptionKeyMedical";
    private static readonly ProtoId<RadioChannelPrototype> KeyChannel = "Medical";
    private const string TestAi = "WGBorgRadioKeysTestAi";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGBorgRadioKeysTestAi
  components:
  - type: IntrinsicRadioTransmitter
    channels:
    - Command
  - type: ActiveRadio
    channels:
    - Command
";

    [Test]
    public async Task BorgKeysAddChannelsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid borg = default;
        EntityUid user = default;
        EntityUid key = default;
        await server.WaitPost(() =>
        {
            borg = entMan.SpawnEntity(Borg, map.GridCoords);
            user = entMan.SpawnEntity(User, map.GridCoords);
            key = entMan.SpawnEntity(Key, map.GridCoords);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var proto = server.ProtoMan.Index<EntityPrototype>(Borg);
            Assert.That(proto.TryGetComponent<IntrinsicRadioTransmitterComponent>(out var protoTransmitter, entMan.ComponentFactory));
            Assert.That(proto.TryGetComponent<ActiveRadioComponent>(out var protoReceiver, entMan.ComponentFactory));
            var builtInTransmit = protoTransmitter!.Channels.ToHashSet();
            var builtInReceive = protoReceiver!.Channels.ToHashSet();
            Assert.That(builtInTransmit, Is.Not.Empty);

            var transmitter = entMan.GetComponent<IntrinsicRadioTransmitterComponent>(borg);
            var receiver = entMan.GetComponent<ActiveRadioComponent>(borg);
            var holder = entMan.GetComponent<EncryptionKeyHolderComponent>(borg);
            var interaction = entMan.System<SharedInteractionSystem>();
            var coords = entMan.GetComponent<TransformComponent>(borg).Coordinates;

            // The empty key slots leave the built-in channels alone.
            Assert.That(transmitter.Channels, Is.EquivalentTo(builtInTransmit), "The empty key slots wiped the cyborg's channels.");
            Assert.That(receiver.Channels, Is.EquivalentTo(builtInReceive));

            // Locked, the slots refuse a key.
            interaction.InteractUsing(user, key, borg, coords, checkCanInteract: false, checkCanUse: false);
            Assert.That(holder.KeyContainer.Contains(key), Is.False, "A locked cyborg took a key.");

            entMan.System<LockSystem>().Unlock(borg, null);
            Assert.That(entMan.System<SharedWiresSystem>().TogglePanel(borg, entMan.GetComponent<WiresPanelComponent>(borg), true));

            interaction.InteractUsing(user, key, borg, coords, checkCanInteract: false, checkCanUse: false);
            Assert.That(holder.KeyContainer.Contains(key), "An unlocked cyborg with its panel open refused a key.");

            Assert.That(transmitter.Channels, Is.EquivalentTo(builtInTransmit.Append(KeyChannel).ToHashSet()),
                "The key's channel didn't add to the built-in ones.");
            Assert.That(receiver.Channels, Is.EquivalentTo(builtInReceive.Append(KeyChannel).ToHashSet()));

            // Taking the key out returns the cyborg to its built-in channels.
            entMan.System<SharedContainerSystem>().Remove(key, holder.KeyContainer);
            Assert.That(transmitter.Channels, Is.EquivalentTo(builtInTransmit));
            Assert.That(receiver.Channels, Is.EquivalentTo(builtInReceive));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// While an AI drives the cyborg its channels stay live and a key added meanwhile is saved for later; the back-to-AI
    /// action then gives the cyborg its own channels and keys back, once. The AI's hold is set up the way
    /// <c>AiRemoteControlSystem.AiTakeControl</c> leaves it, without a player in the core.
    /// </summary>
    [Test]
    public async Task AiReturnRestoresCyborgChannelsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid borg = default;
        EntityUid ai = default;
        EntityUid key = default;
        await server.WaitPost(() =>
        {
            borg = entMan.SpawnEntity(Borg, map.GridCoords);
            ai = entMan.SpawnEntity(TestAi, map.GridCoords);
            key = entMan.SpawnEntity(Key, map.GridCoords);
            entMan.EnsureComponent<AiRemoteControllerComponent>(borg);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var transmitter = entMan.GetComponent<IntrinsicRadioTransmitterComponent>(borg);
            var receiver = entMan.GetComponent<ActiveRadioComponent>(borg);
            var holder = entMan.GetComponent<EncryptionKeyHolderComponent>(borg);
            var remote = entMan.GetComponent<AiRemoteControllerComponent>(borg);
            var aiTransmit = entMan.GetComponent<IntrinsicRadioTransmitterComponent>(ai).Channels.ToHashSet();
            var aiReceive = entMan.GetComponent<ActiveRadioComponent>(ai).Channels.ToHashSet();
            var ownTransmit = transmitter.Channels.ToHashSet();
            var ownReceive = receiver.Channels.ToHashSet();
            Assert.That(ownTransmit, Is.Not.EquivalentTo(aiTransmit), "The test AI shares the cyborg's channels, so this proves nothing.");

            // What AiTakeControl does to the cyborg's radio.
            remote.PreviouslyTransmitterChannels = transmitter.Channels.Select(c => c.Id).ToArray();
            remote.PreviouslyActiveRadioChannels = receiver.Channels.Select(c => c.Id).ToArray();
            transmitter.Channels = new(aiTransmit);
            receiver.Channels = new(aiReceive);
            remote.AiHolder = ai;

            Assert.That(entMan.System<SharedContainerSystem>().Insert(key, holder.KeyContainer), "The key didn't go in.");
            Assert.That(transmitter.Channels, Is.EquivalentTo(aiTransmit), "A key added under the AI replaced the AI's channels.");
            Assert.That(remote.PreviouslyTransmitterChannels, Does.Contain(KeyChannel.Id), "The key added under the AI wasn't saved for the cyborg.");

            // What ReturnMindIntoAi does once the mind is back in the core, then the action event it runs from.
            remote.AiHolder = null;
            entMan.EventBus.RaiseLocalEvent(borg, new ReturnMindIntoAiEvent());

            var withKeyTransmit = ownTransmit.Append(KeyChannel).ToHashSet();
            var withKeyReceive = ownReceive.Append(KeyChannel).ToHashSet();
            Assert.That(transmitter.Channels, Is.EquivalentTo(withKeyTransmit), "The cyborg kept the AI's channels after it left.");
            Assert.That(receiver.Channels, Is.EquivalentTo(withKeyReceive));
            Assert.That(remote.PreviouslyTransmitterChannels, Is.Null, "The saved channels weren't spent.");

            // A repeat with no AI applies nothing stale.
            transmitter.Channels = new(ownTransmit);
            entMan.EventBus.RaiseLocalEvent(borg, new ReturnMindIntoAiEvent());
            Assert.That(transmitter.Channels, Is.EquivalentTo(ownTransmit), "A repeated return changed the channels.");
        });

        await pair.CleanReturnAsync();
    }
}
