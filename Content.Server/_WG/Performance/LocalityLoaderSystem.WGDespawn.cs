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
    private readonly Dictionary<EntityUid, List<EntityUid>> _wgRestrictionsByGrid = new();
    private readonly Stack<EntityUid> _wgWalk = new();
    private GameTick _wgRestrictionsTick = GameTick.Zero;

    /// <summary>Queues the despawn of mob restrictions linked to a grid, using an index built once per tick.</summary>
    private void WGDespawnRestrictions(EntityUid grid)
    {
        if (_wgRestrictionsTick != _wgTiming.CurTick)
        {
            _wgRestrictionsTick = _wgTiming.CurTick;
            _wgRestrictionsByGrid.Clear();
            var query = AllEntityQuery<NFSalvageMobRestrictionsComponent>();
            while (query.MoveNext(out var uid, out var restrictions))
            {
                if (!restrictions.DespawnIfOffLinkedGrid)
                    continue;

                if (!_wgRestrictionsByGrid.TryGetValue(restrictions.LinkedGridEntity, out var list))
                    _wgRestrictionsByGrid[restrictions.LinkedGridEntity] = list = new List<EntityUid>();

                list.Add(uid);
            }
        }

        if (!_wgRestrictionsByGrid.Remove(grid, out var linked))
            return;

        foreach (var uid in linked)
        {
            QueueDel(uid);
        }
    }

    /// <summary>True if anything under the grid is a player, a mind holder, or something that carries one.</summary>
    private bool WGMayHoldPlayers(EntityUid grid)
    {
        _wgWalk.Clear();
        _wgWalk.Push(grid);
        while (_wgWalk.Count > 0)
        {
            var parent = _wgWalk.Pop();
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
                    _wgWalk.Clear();
                    return true;
                }

                _wgWalk.Push(child);
            }
        }

        return false;
    }
}
