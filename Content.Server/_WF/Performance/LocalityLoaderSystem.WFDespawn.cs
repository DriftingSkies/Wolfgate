using Content.Server._NF.Salvage;
using Content.Shared.Humanoid;
using Content.Shared.Mech.Components;
using Content.Shared.Mind.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Vehicle.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.Worldgen.Systems;

public sealed partial class LocalityLoaderSystem
{
    private readonly Dictionary<EntityUid, List<EntityUid>> _wfRestrictionsByGrid = new();
    private readonly Stack<EntityUid> _wfWalk = new();
    private GameTick _wfRestrictionsTick = GameTick.Zero;

    /// <summary>Queues the despawn of mob restrictions linked to a grid, using an index built once per tick.</summary>
    private void WFDespawnRestrictions(EntityUid grid)
    {
        if (_wfRestrictionsTick != _wfTiming.CurTick)
        {
            _wfRestrictionsTick = _wfTiming.CurTick;
            _wfRestrictionsByGrid.Clear();
            var query = AllEntityQuery<NFSalvageMobRestrictionsComponent>();
            while (query.MoveNext(out var uid, out var restrictions))
            {
                if (!restrictions.DespawnIfOffLinkedGrid)
                    continue;

                if (!_wfRestrictionsByGrid.TryGetValue(restrictions.LinkedGridEntity, out var list))
                    _wfRestrictionsByGrid[restrictions.LinkedGridEntity] = list = new List<EntityUid>();

                list.Add(uid);
            }
        }

        if (!_wfRestrictionsByGrid.Remove(grid, out var linked))
            return;

        foreach (var uid in linked)
        {
            QueueDel(uid);
        }
    }

    /// <summary>True if anything under the grid is a player, a mind holder, or something that carries one.</summary>
    private bool WFMayHoldPlayers(EntityUid grid)
    {
        _wfWalk.Clear();
        _wfWalk.Push(grid);
        while (_wfWalk.Count > 0)
        {
            var parent = _wfWalk.Pop();
            var children = Transform(parent).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (HasComp<ActorComponent>(child)
                    || HasComp<HumanoidAppearanceComponent>(child)
                    || HasComp<BorgChassisComponent>(child)
                    || HasComp<RiderComponent>(child)
                    || HasComp<MechPilotComponent>(child)
                    || TryComp<PullerComponent>(child, out var puller) && puller.Pulling != null
                    || TryComp<MindContainerComponent>(child, out var mind) && mind.HasMind)
                {
                    _wfWalk.Clear();
                    return true;
                }

                _wfWalk.Push(child);
            }
        }

        return false;
    }
}
