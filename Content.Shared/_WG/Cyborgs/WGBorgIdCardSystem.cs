using Content.Shared._NF.Silicons.Borgs;
using Content.Shared.Access.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.Containers;

namespace Content.Shared._WG.Cyborgs;

/// <summary>
/// Finds a cyborg's issued ID card, and has access readers count it while its module is put away.
/// </summary>
public sealed partial class WGBorgIdCardSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgChassisComponent, GetAdditionalAccessEvent>(OnGetAdditionalAccess);
    }

    /// <summary>The chassis container that holds a crew cyborg's card until a tool module takes it.</summary>
    public const string ContainerId = "wg-borg-id";

    /// <summary>The cyborg's issued card: in its hand, stowed in an installed module or kept in its chassis. False once it has parted with it.</summary>
    public bool TryGetCard(EntityUid borg, out EntityUid card)
    {
        card = default;
        if (!TryComp<BorgChassisComponent>(borg, out var chassis))
            return false;

        foreach (var held in _hands.EnumerateHeld(borg))
        {
            if (!HasComp<WGBorgIdCardComponent>(held))
                continue;

            card = held;
            return true;
        }

        // Startup creates the container, so a chassis the client has just received may not have it yet.
        if (chassis.ModuleContainer != null)
        {
            foreach (var module in chassis.ModuleContainer.ContainedEntities)
            {
                if (TryComp<DroppableBorgModuleComponent>(module, out var droppable)
                    && _container.TryGetContainer(module, droppable.ContainerId, out var stowed)
                    && TryFindCard(stowed, out card))
                    return true;
            }
        }

        return _container.TryGetContainer(borg, ContainerId, out var own) && TryFindCard(own, out card);
    }

    /// <summary>The items a cyborg's installed modules have put away, such as a voucher in a tool module that isn't selected.</summary>
    public IEnumerable<EntityUid> StowedItems(EntityUid borg)
    {
        if (!TryComp<BorgChassisComponent>(borg, out var chassis) || chassis.ModuleContainer == null)
            yield break;

        foreach (var module in chassis.ModuleContainer.ContainedEntities)
        {
            if (!TryComp<DroppableBorgModuleComponent>(module, out var droppable)
                || !_container.TryGetContainer(module, droppable.ContainerId, out var stowed))
                continue;

            foreach (var item in stowed.ContainedEntities)
                yield return item;
        }
    }

    private bool TryFindCard(BaseContainer container, out EntityUid card)
    {
        foreach (var item in container.ContainedEntities)
        {
            if (!HasComp<WGBorgIdCardComponent>(item))
                continue;

            card = item;
            return true;
        }

        card = default;
        return false;
    }

    private void OnGetAdditionalAccess(Entity<BorgChassisComponent> ent, ref GetAdditionalAccessEvent args)
    {
        if (TryGetCard(ent, out var card))
            args.Entities.Add(card);
    }
}
