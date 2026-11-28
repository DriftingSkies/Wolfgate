using Content.Server.Stack;
using Content.Shared._WG.SafetyDepositBox;
using Content.Shared._WG.SafetyDepositBox.Components;
using Content.Shared.Database;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WG.SafetyDepositBox;

// Wolfgate's handling of stored items a box no longer takes, kept out of the upstream file.
public sealed partial class SafetyDepositBoxSystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SafetyDepositAllowListSystem _allowList = default!;
    [Dependency] private StackSystem _stack = default!;

    /// <summary>
    /// Takes out anything the box refuses or has no room left for under its limits, however deep, such as an ID in a
    /// bag or a cell in a device's slot, splits off what of a stack goes over a limit, and hands it back. Returns how
    /// many came out.
    /// </summary>
    private int EjectNestedRefused(EntityUid player, EntityUid boxEntity, StorageComponent storage)
    {
        TryComp<SafetyDepositAllowListComponent>(boxEntity, out var allowList);
        var refused = new List<EntityUid>();
        var over = new Dictionary<EntityUid, int>();
        _allowList.FindRefused((boxEntity, storage, allowList), refused, over);

        foreach (var item in refused)
        {
            _container.TryRemoveFromContainer(item, force: true);
            HandBack(player, boxEntity, item);
        }

        foreach (var (stack, amount) in over)
        {
            if (_stack.Split(stack, amount, Transform(player).Coordinates) is { } split)
                HandBack(player, boxEntity, split);
        }

        return refused.Count + over.Count;
    }

    private void HandBack(EntityUid player, EntityUid boxEntity, EntityUid item)
    {
        _hands.PickupOrDrop(player, item);
        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(player):actor} got {ToPrettyString(item):entity} back from safety deposit box {ToPrettyString(boxEntity)}");
    }

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
