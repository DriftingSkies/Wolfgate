using Content.Client._WG.Administration.UI.GhostShortcuts;
using Content.Client.Administration.Managers;
using Content.Shared._WG.Administration.GhostShortcuts;
using Content.Shared.Administration;
using Content.Shared.DragDrop;
using Content.Shared.Ghost;

namespace Content.Client._WG.Administration.GhostShortcuts;

/// <summary>
/// Lets admins drag a ghost onto a body to put its player in control. The drop travels as a normal drag-drop
/// request; the server answers with a prompt when the body already has a player.
/// </summary>
public sealed partial class GhostPossessSystem : SharedGhostPossessSystem
{
    [Dependency] private IClientAdminManager _admin = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GhostComponent, CanDragEvent>(OnCanDrag);
        SubscribeLocalEvent<GhostComponent, CanDropDraggedEvent>(OnCanDropDragged);
        SubscribeNetworkEvent<GhostPossessOccupiedEvent>(OnOccupied);
    }

    private void OnCanDrag(Entity<GhostComponent> ghost, ref CanDragEvent args)
    {
        if (_admin.HasFlag(AdminFlags.Fun))
            args.Handled = true;
    }

    private void OnCanDropDragged(Entity<GhostComponent> ghost, ref CanDropDraggedEvent args)
    {
        // Only claim real bodies: leaving the event unhandled keeps other entities unhighlighted rather than marked invalid.
        if (!_admin.HasFlag(AdminFlags.Fun) || !IsBodyTarget(args.Target))
            return;

        args.CanDrop = true;
        args.Handled = true;
    }

    private void OnOccupied(GhostPossessOccupiedEvent ev)
    {
        var prompt = new GhostPossessPromptWindow(ev);
        prompt.Replace += () => RaiseNetworkEvent(new GhostPossessRequestEvent(ev.Ghost, ev.Body, true, ev.Occupant));
        prompt.OpenCentered();
    }
}
