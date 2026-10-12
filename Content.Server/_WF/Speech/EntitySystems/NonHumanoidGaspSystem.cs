using Content.Server.Body.Components;
using Content.Server.Chat.Systems;
using Content.Server.Speech.EntitySystems;
using Content.Server.Zombies;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Humanoid;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Speech.EntitySystems;

/// <summary>
/// Silences the gasp of anything that is not a humanoid: critters borrow humanoid emote sets (mothroach, kobold,
/// spiders, nymphs) and would otherwise suffocate in a human voice.
/// </summary>
public sealed class NonHumanoidGaspSystem : EntitySystem
{
    public static readonly ProtoId<EmotePrototype> Gasp = "Gasp";

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RespiratorComponent, EmoteEvent>(OnEmote,
            before: new[] { typeof(VocalSystem), typeof(MumbleAccentSystem), typeof(ZombieSystem) });
    }

    private void OnEmote(Entity<RespiratorComponent> ent, ref EmoteEvent args)
    {
        if (args.Handled || args.Emote.ID != Gasp || HasComp<HumanoidAppearanceComponent>(ent))
            return;

        args.Handled = true;
    }
}
