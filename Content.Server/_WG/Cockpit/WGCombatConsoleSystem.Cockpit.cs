using Content.Server._WG.Cockpit;
using Content.Shared._Mono.FireControl;
using Content.Shared._WG.CombatConsole;

namespace Content.Server._WG.CombatConsole;

public sealed partial class WGCombatConsoleSystem
{
    /// <summary>Uses the normal group and flare handlers for an authorized cockpit operator.</summary>
    public bool WgCockpitCommand(EntityUid uid, FireControlConsoleComponent console, EntityUid actor,
        BoundUserInterfaceMessage command)
    {
        if (!EntityManager.System<WGCockpitGunnerySystem>().CanOperate(actor, uid))
            return false;
        switch (command)
        {
            case WGSaveWeaponGroupMessage group:
                OnSaveGroup(uid, console, new WGSaveWeaponGroupMessage(group.Slot, group.Weapons) { Actor = actor });
                return true;
            case WGAutomaticFlaresMessage automatic:
                OnAutomatic(uid, console, new WGAutomaticFlaresMessage(automatic.Enabled) { Actor = actor });
                return true;
            case WGDispenseFlaresMessage:
                OnDispense(uid, console, new WGDispenseFlaresMessage { Actor = actor });
                return true;
            default:
                return false;
        }
    }
}
