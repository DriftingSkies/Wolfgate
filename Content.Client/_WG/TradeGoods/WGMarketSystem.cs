using Content.Shared._WG.TradeGoods;

namespace Content.Client._WG.TradeGoods;

/// <summary>Keeps the full Market app state the server sent this player privately, per PDA.</summary>
public sealed class WGMarketSystem : EntitySystem
{
    private readonly Dictionary<EntityUid, (WGMarketUiState State, int Version)> _private = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGMarketPrivateStateEvent>(OnPrivateState);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _private.Clear();
    }

    private void OnPrivateState(WGMarketPrivateStateEvent ev)
    {
        var loader = GetEntity(ev.Loader);
        var version = _private.TryGetValue(loader, out var last) ? last.Version + 1 : 1;
        _private[loader] = (ev.State, version);
    }

    /// <summary>The latest private state for a PDA and a counter that changes with each one, or null.</summary>
    public (WGMarketUiState State, int Version)? Private(EntityUid loader)
    {
        return _private.TryGetValue(loader, out var entry) ? entry : null;
    }

    /// <summary>Drops a PDA's private state once its holder no longer gets one.</summary>
    public void Forget(EntityUid loader)
    {
        _private.Remove(loader);
    }
}
