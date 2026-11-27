using Content.Shared._WG.Cockpit;
using Robust.Client.UserInterface;
using Robust.Shared.Input.Binding;

namespace Content.Client._WG.Cockpit;

/// <summary>Binds the cockpit's fire-group hotkeys and hands them to the open cockpit.</summary>
public sealed partial class WGCockpitKeysSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        var binds = CommandBinds.Builder;
        for (var i = 0; i < WGCockpitKeys.WeaponGroups.Length; i++)
        {
            var slot = i;
            binds = binds.Bind(WGCockpitKeys.WeaponGroups[i], InputCmdHandler.FromDelegate(
                _ => _ui.GetUIController<WGCockpitUIController>().RecallWeaponGroup(slot), handle: true,
                outsidePrediction: true));
        }

        binds.Register<WGCockpitKeysSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<WGCockpitKeysSystem>();
    }
}
