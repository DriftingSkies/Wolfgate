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
        SubscribeLocalEvent<RoofComponent, ComponentGetState>(WFOnGetState);
        SubscribeLocalEvent<RoofComponent, ComponentHandleState>(WFOnHandleState);
    }

    private void WFMarkChunk(RoofComponent roof, Vector2i chunk)
    {
        roof.WFChunkTicks[chunk] = _timing.CurTick;
    }

    private void WFOnGetState(Entity<RoofComponent> ent, ref ComponentGetState args)
    {
        if (args.FromTick <= ent.Comp.CreationTick)
        {
            args.State = new WFRoofState(ent.Comp.Color, new Dictionary<Vector2i, ulong>(ent.Comp.Data));
            return;
        }

        var changed = new Dictionary<Vector2i, ulong>();
        foreach (var (chunk, tick) in ent.Comp.WFChunkTicks)
        {
            if (tick >= args.FromTick && ent.Comp.Data.TryGetValue(chunk, out var bits))
                changed[chunk] = bits;
        }

        args.State = new WFRoofDeltaState(ent.Comp.Color, changed);
    }

    private void WFOnHandleState(Entity<RoofComponent> ent, ref ComponentHandleState args)
    {
        switch (args.Current)
        {
            case WFRoofDeltaState delta:
                ent.Comp.Color = delta.Color;
                foreach (var (chunk, bits) in delta.Chunks)
                {
                    ent.Comp.Data[chunk] = bits;
                }
                break;
            case WFRoofState state:
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
    private sealed class WFRoofState(Color color, Dictionary<Vector2i, ulong> data) : ComponentState
    {
        public Color Color = color;
        public Dictionary<Vector2i, ulong> Data = data;
    }

    [Serializable, NetSerializable]
    private sealed class WFRoofDeltaState(Color color, Dictionary<Vector2i, ulong> chunks)
        : ComponentState, IComponentDeltaState<WFRoofState>
    {
        public Color Color = color;
        public Dictionary<Vector2i, ulong> Chunks = chunks;

        public void ApplyToFullState(WFRoofState state)
        {
            state.Color = Color;
            foreach (var (chunk, bits) in Chunks)
            {
                state.Data[chunk] = bits;
            }
        }

        public WFRoofState CreateNewFullState(WFRoofState state)
        {
            var data = new Dictionary<Vector2i, ulong>(state.Data);
            foreach (var (chunk, bits) in Chunks)
            {
                data[chunk] = bits;
            }

            return new WFRoofState(Color, data);
        }
    }
}
