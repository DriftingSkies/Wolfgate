using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WG.Drydock;
using Content.Shared._WG.Planets.Flight;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Drydock;

/// <summary>
/// Landing thruster refits (<see cref="WGLandingThrusterRefit"/>): every linear thruster on a ship converted for
/// atmospheric flight, keeping its parts and ratings. Each thruster is a unit.
/// </summary>
public sealed partial class WGLandingThrusterRefitSystem : EntitySystem
{
    [Dependency] private ThrusterSystem _thrusters = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGRefitSurveyEvent<WGLandingThrusterRefit>>(OnSurvey);
        SubscribeLocalEvent<WGRefitProspectEvent<WGLandingThrusterRefit>>(OnProspect);
        SubscribeLocalEvent<WGRefitApplyEvent<WGLandingThrusterRefit>>(OnApply);
    }

    /// <summary>Counts the ship's converted linear thrusters, and lists the rest.</summary>
    private void OnSurvey(WGRefitSurveyEvent<WGLandingThrusterRefit> args)
    {
        var survey = args.Survey;
        var children = Transform(survey.Ship).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            var xform = Transform(uid);
            if (!xform.Anchored || !IsLinear(uid, out _))
                continue;

            if (HasComp<WGLandingThrusterComponent>(uid))
            {
                survey.UnitsAtTier++;
                continue;
            }

            var target = new WGRefitTarget(uid, xform.LocalPosition, survey.Tier);
            target.Lacking[0] = 1;
            survey.Below.Add(target);
        }
    }

    /// <summary>A rebuilt linear thruster comes back unconverted.</summary>
    private void OnProspect(WGRefitProspectEvent<WGLandingThrusterRefit> args)
    {
        var prospect = args.Prospect;
        if (_prototype.TryIndex(prospect.Prototype, out var proto) &&
            proto.TryComp<ThrusterComponent>(out var thruster, Factory) &&
            thruster.Type == ThrusterType.Linear &&
            !proto.TryComp<WGLandingThrusterComponent>(out _, Factory))
        {
            prospect.Lacking[0]++;
        }
    }

    private void OnApply(WGRefitApplyEvent<WGLandingThrusterRefit> args)
    {
        if (!IsLinear(args.Target, out var thruster) || HasComp<WGLandingThrusterComponent>(args.Target))
            return;

        EnsureComp<WGLandingThrusterComponent>(args.Target);
        _thrusters.WgRefreshAtmosphereThruster(args.Target, thruster);
    }

    private bool IsLinear(EntityUid uid, out ThrusterComponent thruster)
    {
        return TryComp(uid, out thruster!) && thruster.Type == ThrusterType.Linear;
    }
}
