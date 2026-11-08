using Content.Server._WF.Outposts.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Server.Power.EntitySystems;
using Content.Shared.Atmos;
using Robust.Shared.Timing;

namespace Content.Server._WF.Outposts;

/// <summary>Outpost life support: CO2 crackers turn exhaled air back into oxygen, synthesizers make water and food.</summary>
public sealed class WFOutpostLifeSupportSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SolutionContainerSystem _solutions = default!;

    private static readonly TimeSpan SynthInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextSynth;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCo2CrackerComponent, AtmosDeviceUpdateEvent>(OnCrackerUpdate);
    }

    private void OnCrackerUpdate(Entity<WFCo2CrackerComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        if (!_power.IsPowered(ent.Owner))
            return;

        var air = _atmos.GetContainingMixture(ent.Owner, args.Grid, args.Map, excite: true);
        if (air == null)
            return;

        var moles = MathF.Min(air.GetMoles(Gas.CarbonDioxide), ent.Comp.Rate * args.dt);
        if (moles <= 0f)
            return;

        air.AdjustMoles(Gas.CarbonDioxide, -moles);
        air.AdjustMoles(Gas.Oxygen, moles);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (now < _nextSynth)
            return;

        _nextSynth = now + SynthInterval;
        var seconds = (float) SynthInterval.TotalSeconds;
        var query = EntityQueryEnumerator<WFReagentSynthesizerComponent>();
        while (query.MoveNext(out var uid, out var synth))
        {
            if (!_power.IsPowered(uid) || !_solutions.TryGetSolution(uid, synth.Solution, out var solution))
                continue;

            _solutions.TryAddReagent(solution.Value, synth.Reagent, synth.Rate * seconds, out _);
        }
    }
}
