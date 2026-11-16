using Content.Server.Chat.Systems;
using Content.Shared._WF.Modsuits.Modules;
using Content.Shared.Ghost;
using Robust.Shared.Player;
using static Content.Server.Chat.Systems.ChatSystem;

namespace Content.Server._WF.Modsuits;

/// <summary>Carries an active megaphone module's speech to everyone inside its range.</summary>
public sealed partial class ModLoudspeakerChatSystem : EntitySystem
{
    [Dependency] private ISharedPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ExpandICChatRecipientsEvent>(OnExpandRecipients);
    }

    private void OnExpandRecipients(ExpandICChatRecipientsEvent ev)
    {
        if (!TryComp<ModLoudspeakerActiveComponent>(ev.Source, out var loudspeaker))
            return;

        var coords = Transform(ev.Source).Coordinates;
        var range = MathF.Max(ev.VoiceRange, loudspeaker.Range);

        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } listener || ev.Recipients.ContainsKey(session))
                continue;

            if (!coords.TryDistance(EntityManager, Transform(listener).Coordinates, out var distance) || distance >= range)
                continue;

            ev.Recipients.Add(session, new ICChatRecipientData(distance, HasComp<GhostHearingComponent>(listener)));
        }
    }
}
