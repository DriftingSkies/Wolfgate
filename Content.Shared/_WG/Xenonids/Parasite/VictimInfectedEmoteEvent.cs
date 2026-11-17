using Content.Shared.Chat.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Xenonids.Parasite;
[ByRefEvent]
public record struct VictimInfectedEmoteEvent(ProtoId<EmotePrototype> Emote);
