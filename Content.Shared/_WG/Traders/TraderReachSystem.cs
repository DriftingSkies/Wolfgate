using Content.Shared.Interaction.Components;
using Content.Shared.Movement.Components;

namespace Content.Shared._WG.Traders;

/// <summary>
/// Lets customers reach a trader across its table, and keeps clicks on it from turning into hugs.
/// </summary>
public sealed class TraderReachSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TraderComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<TraderComponent, InteractionRangeBonusEvent>(OnRangeBonus);
    }

    private void OnStartup(Entity<TraderComponent> ent, ref ComponentStartup args)
    {
        // Not networked, so the client has to drop its own copy or it predicts the hug.
        RemCompDeferred<InteractionPopupComponent>(ent);

        // A trader is a wall as far as walking into it goes; mob pushing against a static body
        // has the server and the client disagreeing every tick, which reads as rubber banding.
        RemCompDeferred<MobCollisionComponent>(ent);
    }

    private void OnRangeBonus(Entity<TraderComponent> ent, ref InteractionRangeBonusEvent args)
    {
        if (ent.Comp.Table == null)
            return;

        args.Bonus = MathF.Max(args.Bonus, ent.Comp.TableReachBonus);
    }
}
