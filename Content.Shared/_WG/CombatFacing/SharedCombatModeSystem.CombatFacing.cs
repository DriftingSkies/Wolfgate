using Content.Shared._WG.CombatFacing;

namespace Content.Shared.CombatMode;

public abstract partial class SharedCombatModeSystem
{
    [Dependency] private WGCombatFacingSystem _wgCombatFacing = default!;

    /// <summary>Whether entering combat mode should turn this entity toward the cursor.</summary>
    private bool WgFacesCursor(EntityUid uid)
    {
        return _wgCombatFacing.WantsFaceCursor(uid);
    }

    /// <summary>Starts or stops facing the cursor for an entity already in combat mode.</summary>
    public void WgSetFaceCursor(EntityUid uid, bool faceCursor)
    {
        if (!TryComp<CombatModeComponent>(uid, out var combat) || !combat.IsInCombatMode || !combat.ToggleMouseRotator)
            return;

        SetMouseRotatorComponents(uid, faceCursor);
    }
}
