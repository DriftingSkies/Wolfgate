using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Shitmed.Humanoid.Events;
using Content.Shared._WF.BloodColour;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;

namespace Content.Server._WF.BloodColour;

/// <summary>Gives a character the blood colour picked in their profile and repaints the blood already in them.</summary>
public sealed class WFBloodColourSystem : EntitySystem
{
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BloodstreamComponent, ProfileLoadFinishedEvent>(OnProfileLoadFinished);
        SubscribeLocalEvent<WFBloodColourComponent, BodyPartRemovedEvent>(OnPartRemoved);
    }

    private void OnProfileLoadFinished(Entity<BloodstreamComponent> ent, ref ProfileLoadFinishedEvent args)
    {
        if (args.Profile != null)
            SetColour(ent.AsNullable(), args.Profile.BloodColor);
    }

    /// <summary>A severed part and everything still on it keep the body's colour for their own wound art.</summary>
    private void OnPartRemoved(Entity<WFBloodColourComponent> ent, ref BodyPartRemovedEvent args)
    {
        // A body being deleted takes its parts apart on the way out; those need nothing.
        if (TerminatingOrDeleted(ent))
            return;

        foreach (var (part, _) in _body.GetBodyPartChildren(args.Part, args.Part.Comp))
        {
            if (TerminatingOrDeleted(part))
                continue;

            var colour = EnsureComp<WFBloodColourComponent>(part);
            colour.Color = ent.Comp.Color;
            colour.Reagent = ent.Comp.Reagent;
            Dirty(part, colour);
        }
    }

    /// <summary>Sets or, with null, clears a mob's blood colour, and repaints its bloodstream.</summary>
    public void SetColour(Entity<BloodstreamComponent?> ent, Color? colour)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        colour = WFBloodColourRules.Validate(colour);
        var current = CompOrNull<WFBloodColourComponent>(ent);
        if (colour == null)
        {
            if (current == null)
                return;

            RemComp<WFBloodColourComponent>(ent);
        }
        else
        {
            if (current != null && current.Color == colour && current.Reagent == ent.Comp.BloodReagent)
                return;

            current = EnsureComp<WFBloodColourComponent>(ent);
            current.Color = colour.Value;
            current.Reagent = ent.Comp.BloodReagent;
            Dirty(ent, current);
        }

        Repaint((ent, ent.Comp));
    }

    /// <summary>
    /// The colour this mob's blood takes, or null for the reagent's own. A mob whose blood reagent changed since
    /// the colour was set (a zombie) bleeds its new reagent's colour.
    /// </summary>
    public Color? GetColour(EntityUid mob)
    {
        return TryComp(mob, out WFBloodColourComponent? colour) &&
               TryComp(mob, out BloodstreamComponent? bloodstream) &&
               colour.Reagent == bloodstream.BloodReagent
            ? colour.Color
            : null;
    }

    /// <summary>Adds the colour data to the reagent data the mob's fresh blood is made with.</summary>
    public void AddBloodData(EntityUid mob, List<ReagentData> data)
    {
        if (GetColour(mob) is { } colour)
            data.Add(new WFBloodColourData { Color = colour });
    }

    /// <summary>Swaps the blood in the bloodstream for the same amount made with the current data.</summary>
    private void Repaint(Entity<BloodstreamComponent> ent)
    {
        if (!_solutions.ResolveSolution(ent.Owner, ent.Comp.BloodSolutionName, ref ent.Comp.BloodSolution, out var solution))
            return;

        var volume = solution.RemoveReagent(ent.Comp.BloodReagent, solution.Volume, ignoreReagentData: true);
        if (volume > 0)
            _solutions.TryAddReagent(ent.Comp.BloodSolution.Value, ent.Comp.BloodReagent, volume, null, _bloodstream.GetEntityBloodData(ent));
    }
}
