using System.Linq;
using Content.Server._CorvaxNext.Silicons.Borgs;
using Content.Server._EinsteinEngines.Radio;
using Content.Shared._CorvaxNext.Silicons.Borgs;
using Content.Shared._CorvaxNext.Silicons.Borgs.Components;
using Content.Shared._WG.BorgSkins;
using Content.Shared._WG.Cyborgs;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Cyborgs;

/// <summary>
/// Gives a cyborg its built-in channels plus its keys' channels, where the stock key path leaves only the keys'.
/// </summary>
public sealed class WGBorgRadioKeysServerSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGBorgRadioKeysComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<WGBorgRadioKeysComponent, EncryptionChannelsChangedEvent>(OnKeysChanged,
            after: new[] { typeof(IntrinsicRadioKeySystem) });
        SubscribeLocalEvent<WGBorgRadioKeysComponent, WGBorgSubtypeSelectedEvent>(OnTypeSelected);
        SubscribeLocalEvent<WGBorgRadioKeysComponent, ReturnMindIntoAiEvent>(OnAiReturned,
            after: new[] { typeof(AiRemoteControlSystem) });
    }

    /// <summary>
    /// Gives a cyborg an AI has just let go its own channels back, where the AI's would otherwise stay. Does nothing
    /// unless an AI took control and has since gone, so a repeat can't apply stale channels.
    /// </summary>
    public void RestoreAfterAi(EntityUid uid)
    {
        if (!TryComp<WGBorgRadioKeysComponent>(uid, out var comp)
            || !TryComp<AiRemoteControllerComponent>(uid, out var remote)
            || remote.AiHolder != null
            || remote.PreviouslyTransmitterChannels == null && remote.PreviouslyActiveRadioChannels == null)
            return;

        // Taking control saves them afresh, so they are spent once restored.
        remote.PreviouslyTransmitterChannels = null;
        remote.PreviouslyActiveRadioChannels = null;

        if (TryComp<EncryptionKeyHolderComponent>(uid, out var keys))
            Apply((uid, comp), keys);
    }

    // The back-to-AI action moves the mind out but restores no channels; only removing the controller does.
    private void OnAiReturned(Entity<WGBorgRadioKeysComponent> ent, ref ReturnMindIntoAiEvent args)
    {
        RestoreAfterAi(ent);
    }

    // Init runs before any startup, so this reads the prototype's channels before the key holder's startup clears them.
    private void OnInit(Entity<WGBorgRadioKeysComponent> ent, ref ComponentInit args)
    {
        if (TryComp<IntrinsicRadioTransmitterComponent>(ent, out var transmitter))
            ent.Comp.BaseTransmit = new(transmitter.Channels);

        if (TryComp<ActiveRadioComponent>(ent, out var receiver))
            ent.Comp.BaseReceive = new(receiver.Channels);
    }

    private void OnKeysChanged(Entity<WGBorgRadioKeysComponent> ent, ref EncryptionChannelsChangedEvent args)
    {
        Apply(ent, args.Component);
    }

    // Picking a type has just set the type's channels, saved aside instead if an AI is driving the cyborg.
    private void OnTypeSelected(Entity<WGBorgRadioKeysComponent> ent, ref WGBorgSubtypeSelectedEvent args)
    {
        var ai = DrivingAi(ent);

        if (TryComp<IntrinsicRadioTransmitterComponent>(ent, out var transmitter))
            ent.Comp.BaseTransmit = ai?.PreviouslyTransmitterChannels is { } savedTransmit ? ToSet(savedTransmit) : new(transmitter.Channels);

        if (TryComp<ActiveRadioComponent>(ent, out var receiver))
            ent.Comp.BaseReceive = ai?.PreviouslyActiveRadioChannels is { } savedReceive ? ToSet(savedReceive) : new(receiver.Channels);

        if (TryComp<EncryptionKeyHolderComponent>(ent, out var keys))
            Apply(ent, keys);
    }

    private void Apply(Entity<WGBorgRadioKeysComponent> ent, EncryptionKeyHolderComponent keys)
    {
        var ai = DrivingAi(ent);

        if (TryComp<IntrinsicRadioTransmitterComponent>(ent, out var transmitter))
        {
            var own = Merge(ent.Comp.BaseTransmit, keys.Channels);
            transmitter.Channels = own;

            // A driving AI keeps its own channels, and the cyborg's come back when it leaves.
            if (ai != null)
            {
                ai.PreviouslyTransmitterChannels = own.Select(c => c.Id).ToArray();
                if (TryComp<IntrinsicRadioTransmitterComponent>(ai.AiHolder, out var aiTransmitter))
                    transmitter.Channels = new(aiTransmitter.Channels);
            }
        }

        if (TryComp<ActiveRadioComponent>(ent, out var receiver))
        {
            var own = Merge(ent.Comp.BaseReceive, keys.Channels);
            receiver.Channels = own;

            if (ai != null)
            {
                ai.PreviouslyActiveRadioChannels = own.Select(c => c.Id).ToArray();
                if (TryComp<ActiveRadioComponent>(ai.AiHolder, out var aiReceiver))
                    receiver.Channels = new(aiReceiver.Channels);
            }
        }
    }

    private AiRemoteControllerComponent? DrivingAi(EntityUid uid)
    {
        return TryComp<AiRemoteControllerComponent>(uid, out var remote) && remote.AiHolder != null ? remote : null;
    }

    private static HashSet<ProtoId<RadioChannelPrototype>> Merge(HashSet<ProtoId<RadioChannelPrototype>> builtIn, HashSet<string> keys)
    {
        var channels = new HashSet<ProtoId<RadioChannelPrototype>>(builtIn);
        foreach (var key in keys)
            channels.Add(key);

        return channels;
    }

    private static HashSet<ProtoId<RadioChannelPrototype>> ToSet(string[] channels)
    {
        return channels.Select(c => new ProtoId<RadioChannelPrototype>(c)).ToHashSet();
    }
}
