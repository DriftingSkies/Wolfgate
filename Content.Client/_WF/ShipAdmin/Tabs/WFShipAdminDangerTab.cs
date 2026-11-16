using Content.Client.UserInterface.Controls;
using Content.Shared._WF.ShipAdmin;
using Content.Shared.Administration;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.ShipAdmin.Tabs;

/// <summary>What can't be taken back: emptying a ship of players, stripping it to a bare hull, deleting it.</summary>
public sealed class WFShipAdminDangerTab : WFShipAdminTab
{
    private readonly WFShipAdminText _aboard;
    private readonly ConfirmButton _eject;
    private readonly ConfirmButton _strip;
    private readonly CheckBox _bring;
    private readonly ConfirmButton _delete;

    public override string Title => Loc.GetString("wf-ship-admin-tab-danger");

    public WFShipAdminDangerTab()
    {
        _aboard = Status();
        Body.AddChild(_aboard);

        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-danger-eject-heading")));
        Body.AddChild(Note(Loc.GetString("wf-ship-admin-danger-eject-note")));
        _eject = Confirm("wf-ship-admin-danger-eject");
        _eject.OnPressed += _ => Send(new WFShipAdminActionMessage(Grid, WFShipAdminAction.Eject));
        Body.AddChild(Left(_eject));

        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-danger-strip-heading")));
        Body.AddChild(Note(Loc.GetString("wf-ship-admin-danger-strip-note")));
        _strip = Confirm("wf-ship-admin-danger-strip");
        _strip.OnPressed += _ => Send(new WFShipAdminActionMessage(Grid, WFShipAdminAction.Strip));
        Body.AddChild(Left(_strip));

        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-danger-delete-heading")));
        Body.AddChild(Note(Loc.GetString("wf-ship-admin-danger-delete-note")));
        _bring = new CheckBox { Text = Loc.GetString("wf-ship-admin-danger-bring"), ToolTip = Loc.GetString("wf-ship-admin-danger-bring-tooltip"), Pressed = true };
        Body.AddChild(_bring);
        _delete = Confirm("wf-ship-admin-danger-delete");
        _delete.OnPressed += _ => Send(new WFShipAdminActionMessage(Grid, WFShipAdminAction.Delete, _bring.Pressed));
        Body.AddChild(Left(_delete));
    }

    protected override void Show(WFShipAdminDetail detail, AdminFlags flags)
    {
        _aboard.Text = Loc.GetString("wf-ship-admin-danger-aboard", ("count", detail.OccupantCount));
        Gate(_eject, WFShipAdminAction.Eject, flags, detail.OccupantCount > 0);
        Gate(_strip, WFShipAdminAction.Strip, flags);
        Gate(_delete, WFShipAdminAction.Delete, flags);
    }

    private static ConfirmButton Confirm(string key)
    {
        return new ConfirmButton
        {
            Text = Loc.GetString(key),
            ConfirmationText = Loc.GetString("wf-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
        };
    }
}
