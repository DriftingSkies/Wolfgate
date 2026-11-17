using Content.Shared._WG.RMCCore.Tether;
using Robust.Server.GameStates;
using Robust.Shared.Player;

namespace Content.Server._WG.RMCCore.Tether;

public sealed partial class RMCTetherSystem : SharedRMCTetherSystem
{
    [Dependency] private ISharedPlayerManager _player = default!;
    [Dependency] private PvsOverrideSystem _pvs = default!; // WOLFGATE: RMCPvsSystem wraps this engine system

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RMCTetherComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<RMCTetherComponent, ComponentRemove>(OnComponentRemove);
    }

    private void OnMapInit(Entity<RMCTetherComponent> ent, ref MapInitEvent ev)
    {
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity == null)
                continue;

            _pvs.AddSessionOverride(ent, session);
        }
    }

    private void OnComponentRemove(Entity<RMCTetherComponent> ent, ref ComponentRemove ev)
    {
        foreach (var session in _player.Sessions)
        {
            _pvs.RemoveSessionOverride(ent, session);
        }
    }
}
