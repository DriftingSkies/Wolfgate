using Content.Server.Atmos.EntitySystems;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Moves heat into or out of the air around <see cref="FaunaAirTemperatureComponent"/> mobs.
/// </summary>
public sealed partial class FaunaAirTemperatureSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaAirTemperatureComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextTick)
                continue;

            comp.NextTick = now + comp.Interval;
            if (!_mobState.IsAlive(uid) || _atmosphere.GetContainingMixture(uid) is not { } air)
                continue;

            var power = comp.BoostFrom is { } boost && air.Temperature >= boost ? comp.BoostPower : comp.Power;
            if (power > 0 ? air.Temperature >= comp.Target : air.Temperature <= comp.Target)
                continue;

            // Read again with excite so atmos picks up the heat change.
            _atmosphere.GetContainingMixture(uid, excite: true);
            _atmosphere.AddHeat(air, power * (float) comp.Interval.TotalSeconds);
        }
    }
}
