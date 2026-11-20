using Content.Shared.CombatMode;
using Content.Shared.MouseRotator;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.CombatFacing;

/// <summary>Reads each player's <see cref="WGCombatFacingCVars.FaceCursor"/> and applies a change made mid-fight.</summary>
public sealed class WGCombatFacingSystem : EntitySystem
{
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private INetConfigurationManager _netConfig = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ISharedPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WGCombatFaceCursorChangedEvent>(OnChanged);

        if (_net.IsClient)
            Subs.CVar(_netConfig, WGCombatFacingCVars.FaceCursor, OnLocalChanged);
        else
            SubscribeLocalEvent<CombatModeComponent, PlayerAttachedEvent>(OnPlayerAttached);
    }

    /// <summary>True unless the entity's player has turned facing the cursor off. Mobs without a player keep it.</summary>
    public bool WantsFaceCursor(EntityUid uid)
    {
        return !_player.TryGetSessionByEntity(uid, out var session) || WantsFaceCursor(session);
    }

    private bool WantsFaceCursor(ICommonSession session)
    {
        if (_net.IsClient)
            return session != _player.LocalSession || _netConfig.GetCVar(WGCombatFacingCVars.FaceCursor);

        return _netConfig.GetClientCVar(session.Channel, WGCombatFacingCVars.FaceCursor);
    }

    private void OnPlayerAttached(EntityUid uid, CombatModeComponent combat, PlayerAttachedEvent args)
    {
        // Drops a rotator the last player left if the new one has the option off. None is added for one who wants it,
        // which keeps upstream's handover of a mob an NPC left in combat; toggling combat adds it.
        if (combat.IsInCombatMode && HasComp<MouseRotatorComponent>(uid) && !WantsFaceCursor(args.Player))
            _combat.WgSetFaceCursor(uid, false);
    }

    private void OnLocalChanged(bool faceCursor)
    {
        // The server swaps the rotator components and they come back in its state; adding them here, outside a
        // predicted tick, would race that.
        if (_player.LocalEntity != null)
            RaiseNetworkEvent(new WGCombatFaceCursorChangedEvent(faceCursor));
    }

    private void OnChanged(WGCombatFaceCursorChangedEvent ev, EntitySessionEventArgs args)
    {
        // The event can beat the replicated cvar here, so it carries the value.
        if (args.SenderSession.AttachedEntity is { } uid)
            _combat.WgSetFaceCursor(uid, ev.FaceCursor);
    }
}

/// <summary>Sent when a player flips <see cref="WGCombatFacingCVars.FaceCursor"/>, so a fight already on follows it.</summary>
[Serializable, NetSerializable]
public sealed class WGCombatFaceCursorChangedEvent(bool faceCursor) : EntityEventArgs
{
    public readonly bool FaceCursor = faceCursor;
}
