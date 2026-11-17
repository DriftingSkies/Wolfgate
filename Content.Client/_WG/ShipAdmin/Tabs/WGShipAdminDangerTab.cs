using Content.Client.UserInterface.Controls;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Administration;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.ShipAdmin.Tabs;

/// <summary>What can't be taken back: emptying a ship of players, stripping it to a bare hull, deleting it.</summary>
public sealed class WGShipAdminDangerTab : WGShipAdminTab
{
    private readonly WGShipAdminText _aboard;
    private readonly ConfirmButton _eject;
    private readonly ConfirmButton _strip;
    private readonly CheckBox _bring;
    private readonly ConfirmButton _delete;

    public override string Title => Loc.GetString("wg-ship-admin-tab-danger");

    public WGShipAdminDangerTab()
    {
        _aboard = Status();
        Body.AddChild(_aboard);

        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-danger-eject-heading")));
        Body.AddChild(Note(Loc.GetString("wg-ship-admin-danger-eject-note")));
        _eject = Confirm("wg-ship-admin-danger-eject");
        _eject.OnPressed += _ => Send(new WGShipAdminActionMessage(Grid, WGShipAdminAction.Eject));
        Body.AddChild(Left(_eject));

        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-danger-strip-heading")));
        Body.AddChild(Note(Loc.GetString("wg-ship-admin-danger-strip-note")));
        _strip = Confirm("wg-ship-admin-danger-strip");
        _strip.OnPressed += _ => Send(new WGShipAdminActionMessage(Grid, WGShipAdminAction.Strip));
        Body.AddChild(Left(_strip));

        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-danger-delete-heading")));
        Body.AddChild(Note(Loc.GetString("wg-ship-admin-danger-delete-note")));
        _bring = new CheckBox { Text = Loc.GetString("wg-ship-admin-danger-bring"), ToolTip = Loc.GetString("wg-ship-admin-danger-bring-tooltip"), Pressed = true };
        Body.AddChild(_bring);
        _delete = Confirm("wg-ship-admin-danger-delete");
        _delete.OnPressed += _ => Send(new WGShipAdminActionMessage(Grid, WGShipAdminAction.Delete, _bring.Pressed));
        Body.AddChild(Left(_delete));
    }

    protected override void Show(WGShipAdminDetail detail, AdminFlags flags)
    {
        _aboard.Text = Loc.GetString("wg-ship-admin-danger-aboard", ("count", detail.OccupantCount));
        Gate(_eject, WGShipAdminAction.Eject, flags, detail.OccupantCount > 0);
        Gate(_strip, WGShipAdminAction.Strip, flags);
        Gate(_delete, WGShipAdminAction.Delete, flags);
    }

    private static ConfirmButton Confirm(string key)
    {
        return new ConfirmButton
        {
            Text = Loc.GetString(key),
            ConfirmationText = Loc.GetString("wg-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
        };
    }
}
