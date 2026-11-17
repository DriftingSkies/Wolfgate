using Content.Server.Chat.Systems;
using Content.Shared._WG.Xenonids.Neurotoxin;

namespace Content.Server._WG.Xenonids;

public sealed partial class NeurotoxinSystem : SharedNeurotoxinSystem
{
    [Dependency] ChatSystem _chat = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NeurotoxinComponent, NeurotoxinEmoteEvent>(OnEmote);
    }

    public void OnEmote(Entity<NeurotoxinComponent> victim, ref NeurotoxinEmoteEvent args)
    {
        _chat.TryEmoteWithChat(victim, args.Emote);
    }
}
