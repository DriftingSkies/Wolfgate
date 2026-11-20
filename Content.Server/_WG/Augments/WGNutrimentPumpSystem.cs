using Content.Shared._WG.Augments;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._WG.Augments;

/// <summary>Feeds a body with a nutriment pump whenever it is starving.</summary>
public sealed class WGNutrimentPumpSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGNutrimentPumpComponent, HungerComponent>();
        while (query.MoveNext(out var uid, out var pump, out var hunger))
        {
            if (now < pump.NextCheck)
                continue;

            pump.NextCheck = now + pump.Interval;
            Feed((uid, pump), hunger);
        }
    }

    /// <summary>One look at the body's hunger; true when the pump fed it.</summary>
    public bool Feed(Entity<WGNutrimentPumpComponent> ent, HungerComponent hunger)
    {
        if (_mobState.IsDead(ent) || _hunger.GetHungerThreshold(hunger) > ent.Comp.Threshold)
            return false;

        _hunger.ModifyHunger(ent, ent.Comp.Amount, hunger);
        _popup.PopupEntity(Loc.GetString("wg-augment-nutriment-pump-feed"), ent, ent);
        return true;
    }
}
