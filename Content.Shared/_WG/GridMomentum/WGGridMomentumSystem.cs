using System.Numerics;

namespace Content.Shared._WG.GridMomentum;

/// <summary>Forgets a removed grid on every <see cref="WGGridMomentumComponent"/>, so its uid is never networked after deletion.</summary>
public sealed class WGGridMomentumSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoval);
    }

    private void OnGridRemoval(GridRemovalEvent ev)
    {
        var grid = ev.EntityUid;
        var query = EntityQueryEnumerator<WGGridMomentumComponent>();
        while (query.MoveNext(out var uid, out var momentum))
        {
            if (momentum.Grid != grid && momentum.Left != grid)
                continue;

            if (momentum.Grid == grid)
                momentum.Grid = null;

            if (momentum.Left == grid)
            {
                momentum.Left = null;
                momentum.Frame = Vector2.Zero;
            }

            Dirty(uid, momentum);
        }
    }
}
