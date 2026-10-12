using Content.Shared.Database;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WF.SafetyDepositBox;

// Wolfgate's handling of stored items a box no longer takes, kept out of the upstream file.
public sealed partial class SafetyDepositBoxSystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;

    /// <summary>
    /// Puts a withdrawn item its box refuses down at the box, so it isn't lost.
    /// </summary>
    private void DropRefusedItem(EntityUid player, Guid boxId, EntityUid boxEntity, EntityUid itemEntity)
    {
        _transform.DropNextTo(itemEntity, boxEntity);

        // Saved inside the box, it loads with collision off; wake it as leaving a container does.
        if (TryComp<PhysicsComponent>(itemEntity, out var body))
            _physics.WakeBody(itemEntity, body: body);

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(player):actor} got {ToPrettyString(itemEntity):entity} back beside safety deposit box {boxId}, which no longer accepts it");
    }

    /// <summary>
    /// The popup for a finished withdrawal, which says how many items were left at the box.
    /// </summary>
    private string WithdrawPopup(int refused)
    {
        return refused == 0
            ? Loc.GetString("safety-deposit-console-withdraw-success")
            : Loc.GetString("safety-deposit-console-withdraw-refused", ("count", refused));
    }
}
