using Content.Shared._Shitmed.Humanoid.Events;
using Content.Shared._WG.TypingBubble;
using Content.Shared.Chat.TypingIndicator;
using Content.Shared.Mobs.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.TypingBubble;

/// <summary>Gives a character the typing bubble picked in their profile; the client's typing visualizer draws it.</summary>
public sealed partial class WGTypingBubbleSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateComponent, ProfileLoadFinishedEvent>(OnProfileLoadFinished);
    }

    private void OnProfileLoadFinished(Entity<MobStateComponent> ent, ref ProfileLoadFinishedEvent args)
    {
        if (args.Profile != null)
            SetIndicator(ent, WGTypingBubbleRules.Validate(args.Profile.TypingIndicator, _proto));
    }

    /// <summary>Draws the entity's typing bubble as the given one, or as the species' own when null.</summary>
    public void SetIndicator(EntityUid uid, ProtoId<TypingIndicatorPrototype>? indicator)
    {
        if (indicator == null)
        {
            RemComp<WGTypingBubbleComponent>(uid);
            return;
        }

        var comp = EnsureComp<WGTypingBubbleComponent>(uid);
        comp.Indicator = indicator.Value;
        Dirty(uid, comp);
    }
}
