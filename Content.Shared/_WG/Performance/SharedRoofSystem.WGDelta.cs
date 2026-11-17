using Content.Shared.Light.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared.Light.EntitySystems;

/// <summary>
/// Networks roofs as chunk deltas. A map's roof is sent to every player, and its full state is the whole map's roof,
/// tens of kilobytes for a cavern, so one changed tile used to resend all of it.
/// </summary>
/// <remarks>Roof chunks are never removed, only cleared to zero, so a delta needs no removal list.</remarks>
public abstract partial class SharedRoofSystem
{
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoofComponent, ComponentGetState>(WGOnGetState);
        SubscribeLocalEvent<RoofComponent, ComponentHandleState>(WGOnHandleState);
    }

    private void WGMarkChunk(RoofComponent roof, Vector2i chunk)
    {
        roof.WGChunkTicks[chunk] = _timing.CurTick;
    }

    private void WGOnGetState(Entity<RoofComponent> ent, ref ComponentGetState args)
    {
        if (args.FromTick <= ent.Comp.CreationTick)
        {
            args.State = new WGRoofState(ent.Comp.Color, new Dictionary<Vector2i, ulong>(ent.Comp.Data));
            return;
        }

        var changed = new Dictionary<Vector2i, ulong>();
        foreach (var (chunk, tick) in ent.Comp.WGChunkTicks)
        {
            if (tick >= args.FromTick && ent.Comp.Data.TryGetValue(chunk, out var bits))
                changed[chunk] = bits;
        }

        args.State = new WGRoofDeltaState(ent.Comp.Color, changed);
    }

    private void WGOnHandleState(Entity<RoofComponent> ent, ref ComponentHandleState args)
    {
        switch (args.Current)
        {
            case WGRoofDeltaState delta:
                ent.Comp.Color = delta.Color;
                foreach (var (chunk, bits) in delta.Chunks)
                {
                    ent.Comp.Data[chunk] = bits;
                }
                break;
            case WGRoofState state:
                ent.Comp.Color = state.Color;
                ent.Comp.Data.Clear();
                foreach (var (chunk, bits) in state.Data)
                {
                    ent.Comp.Data[chunk] = bits;
                }
                break;
        }
    }

    [Serializable, NetSerializable]
    private sealed class WGRoofState(Color color, Dictionary<Vector2i, ulong> data) : ComponentState
    {
        public Color Color = color;
        public Dictionary<Vector2i, ulong> Data = data;
    }

    [Serializable, NetSerializable]
    private sealed class WGRoofDeltaState(Color color, Dictionary<Vector2i, ulong> chunks)
        : ComponentState, IComponentDeltaState<WGRoofState>
    {
        public Color Color = color;
        public Dictionary<Vector2i, ulong> Chunks = chunks;

        public void ApplyToFullState(WGRoofState state)
        {
            state.Color = Color;
            foreach (var (chunk, bits) in Chunks)
            {
                state.Data[chunk] = bits;
            }
        }

        public WGRoofState CreateNewFullState(WGRoofState state)
        {
            var data = new Dictionary<Vector2i, ulong>(state.Data);
            foreach (var (chunk, bits) in Chunks)
            {
                data[chunk] = bits;
            }

            return new WGRoofState(Color, data);
        }
    }
}
