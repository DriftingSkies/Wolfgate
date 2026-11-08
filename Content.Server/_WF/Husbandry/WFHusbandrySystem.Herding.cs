using System.Linq;
using System.Numerics;
using Content.Server._WF.Husbandry.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Shared.Interaction.Events;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Husbandry;

/// <summary>Herding: a whistle tells the livestock in earshot to follow, stay or go to the nearest pen.</summary>
public sealed partial class WFHusbandrySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private HTNSystem _htn = default!;

    /// <summary>What livestock does: wanders, unless a whistle gave it someone to follow or a spot to keep to.</summary>
    public static readonly ProtoId<HTNCompoundPrototype> LivestockTask = "WFLivestockCompound";

    /// <summary>How far round an animal a whistle looks for a pen to send it to.</summary>
    private const float PenSearch = 40f;

    private readonly HashSet<Entity<WFLivestockComponent>> _herd = new();
    private readonly HashSet<Entity<WFAnimalFeederComponent>> _nearFeeders = new();

    private void InitializeHerding()
    {
        SubscribeLocalEvent<WFHerdingWhistleComponent, UseInHandEvent>(OnWhistleUse);
        SubscribeLocalEvent<WFHerdingWhistleComponent, GetVerbsEvent<AlternativeVerb>>(OnWhistleVerbs);
    }

    private void OnWhistleUse(Entity<WFHerdingWhistleComponent> ent, ref UseInHandEvent args)
    {
        Herd(args.User, ent.Comp.Command, ent.Comp.Range);
    }

    private void OnWhistleVerbs(Entity<WFHerdingWhistleComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        foreach (var command in Enum.GetValues<WFHerdCommand>())
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString($"wf-herd-verb-{command.ToString().ToLowerInvariant()}"),
                Priority = command == ent.Comp.Command ? 1 : 0,
                Act = () =>
                {
                    ent.Comp.Command = command;
                    Herd(user, command, ent.Comp.Range);
                },
            });
        }
    }

    /// <summary>Gives every animal of livestock within <paramref name="range"/> of the whistler a command; returns how many took it.</summary>
    public int Herd(EntityUid user, WFHerdCommand command, float range)
    {
        _herd.Clear();
        _lookup.GetEntitiesInRange(Transform(user).Coordinates, range, _herd);
        var count = _herd.Count(animal => Command(animal, command, user));
        var key = count > 0 ? $"wf-herd-{command.ToString().ToLowerInvariant()}" : "wf-herd-none";
        _popup.PopupEntity(Loc.GetString(key, ("count", count)), user, user);
        return count;
    }

    /// <summary>Points an animal of livestock at its whistler, its own spot or the nearest pen with room.</summary>
    public bool Command(EntityUid animal, WFHerdCommand command, EntityUid whistler)
    {
        if (HasComp<ActorComponent>(animal) || !IsAlive(animal) || !TryComp<HTNComponent>(animal, out var htn))
            return false;

        EntityCoordinates target;
        float range, close;
        switch (command)
        {
            case WFHerdCommand.Follow:
                target = new EntityCoordinates(whistler, Vector2.Zero);
                (range, close) = (3f, 1.5f);
                break;
            case WFHerdCommand.Stay:
                target = Transform(animal).Coordinates;
                (range, close) = (1.5f, 0.5f);
                break;
            default:
                if (!TryFindPen(animal, out target))
                    return false;
                (range, close) = (1f, 0.5f);
                break;
        }

        htn.Blackboard.SetValue(NPCBlackboard.FollowTarget, target);
        htn.Blackboard.SetValue("FollowRange", range);
        htn.Blackboard.SetValue("FollowCloseRange", close);
        _htn.Replan(htn);
        return true;
    }

    /// <summary>A spot inside the nearest pen with room for an animal, or the one it stands in.</summary>
    public bool TryFindPen(EntityUid animal, out EntityCoordinates target)
    {
        target = default;
        var from = _transform.GetMapCoordinates(animal);
        _nearFeeders.Clear();
        _lookup.GetEntitiesInRange(from, PenSearch, _nearFeeders);

        WFPen? best = null;
        Entity<MapGridComponent> bestGrid = default;
        var bestDistance = float.MaxValue;
        var grids = new HashSet<EntityUid>();
        foreach (var feeder in _nearFeeders)
        {
            if (Transform(feeder).GridUid is not { } gridUid || !grids.Add(gridUid) || !TryComp<MapGridComponent>(gridUid, out var grid))
                continue;

            foreach (var pen in GetPens((gridUid, grid)))
            {
                if (pen.Open.Count == 0 || pen.Livestock.Count >= pen.Capacity && !pen.Livestock.Contains(animal))
                    continue;

                var middle = _transform.ToMapCoordinates(_maps.GridTileToLocal(gridUid, grid, pen.FeederTiles[0]));
                var distance = (middle.Position - from.Position).LengthSquared();
                if (distance >= bestDistance)
                    continue;

                best = pen;
                bestGrid = (gridUid, grid);
                bestDistance = distance;
            }
        }

        if (best == null)
            return false;

        target = _maps.GridTileToLocal(bestGrid, bestGrid, _random.Pick(best.Open));
        return true;
    }

    /// <summary>Hands an animal the livestock behaviour, keeping no herding command it had.</summary>
    private void SetLivestockTask(EntityUid uid)
    {
        if (!TryComp<HTNComponent>(uid, out var htn))
            return;

        htn.RootTask = new HTNCompoundTask { Task = LivestockTask };
        htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.FollowTarget);
        _htn.Replan(htn);
    }
}
