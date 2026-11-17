using Content.Server._Goobstation.Temperature;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared.Atmos.Components;
using Robust.Shared.Timing;

namespace Content.Server._WG.Wolfmed.Life;

/// <summary>Heats a burning body that dies of overheating, as fire did before Monolith#4831.</summary>
// A silicon takes no fire damage: a fire hurts it only by heating the chassis until it overheats. The tile fire
// port made a burning body heat the air and not itself, which left a burning chassis unharmed.
public sealed partial class WolfmedBurningChassisSystem : EntitySystem
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>Heat a fire stack puts into the body each second: the figure upstream's fire used.</summary>
    private const float HeatPerStack = 12500f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private TemperatureSystem _temperature = default!;

    private TimeSpan _next;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        var query = EntityQueryEnumerator<KillOnOverheatComponent, FlammableComponent, TemperatureComponent>();
        while (query.MoveNext(out var uid, out _, out var flammable, out var temperature))
        {
            if (flammable.OnFire && flammable.FireStacks > 0)
                _temperature.ChangeHeat(uid, HeatPerStack * flammable.FireStacks, false, temperature);
        }
    }
}
