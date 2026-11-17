using Content.Client._WG.Administration.UI.SpawnOutfit;
using Content.Client.Administration.Managers;
using Content.Shared._WG.Administration;
using Content.Shared.Ghost;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Robust.Client.GameObjects;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Client._WG.Administration.GhostShortcuts;

/// <summary>
/// Ctrl+click on a ghost opens the Spawn as Outfit picker for that ghost's player instead of trying to pull it.
/// </summary>
public sealed partial class GhostOutfitShortcutSystem : EntitySystem
{
    [Dependency] private IClientAdminManager _admin = default!;
    [Dependency] private InputSystem _input = default!;

    public override void Initialize()
    {
        base.Initialize();
        CommandBinds.Builder
            .BindBefore(ContentKeyFunctions.TryPullObject,
                new PointerInputCmdHandler(OnTryPull, outsidePrediction: true),
                typeof(SharedInteractionSystem))
            .Register<GhostOutfitShortcutSystem>();
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<GhostOutfitShortcutSystem>();
        base.Shutdown();
    }

    /// <summary>
    /// Handling the click here keeps the pull request from ever reaching the server.
    /// </summary>
    private bool OnTryPull(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (_input.Predicted
            || !HasComp<GhostComponent>(uid)
            || !_admin.CanCommand(WolfgateAdminCommands.SpawnOutfitGhost))
            return false;

        new SpawnOutfitMenu(OutfitMenuMode.Ghost, GetNetEntity(uid)).OpenCentered();
        return true;
    }
}
