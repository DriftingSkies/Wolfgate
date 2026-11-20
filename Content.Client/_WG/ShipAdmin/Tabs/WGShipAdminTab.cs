using Content.Client.UserInterface.Controls;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Administration;
using Content.Shared.Eui;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.ShipAdmin.Tabs;

/// <summary>One tab of the admin ship panel: shows part of the selected ship and raises the messages its buttons send.</summary>
public abstract class WGShipAdminTab : ScrollContainer
{
    /// <summary>The width of the key column of a <see cref="Line"/>; the longest key, "Shipyard account", fits in it.</summary>
    private const float KeyWidth = 170;

    /// <summary>Raised with a message for the server.</summary>
    public event Action<EuiMessageBase>? OnMessage;

    /// <summary>Where the tab puts its controls.</summary>
    protected readonly BoxContainer Body;

    /// <summary>The ship the tab last showed.</summary>
    protected NetEntity Grid;

    protected WGShipAdminTab()
    {
        HScrollEnabled = false;
        VerticalExpand = true;
        Body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
            Margin = new Thickness(6),
        };
        AddChild(Body);
    }

    /// <summary>The tab's title in the panel.</summary>
    public abstract string Title { get; }

    /// <summary>Shows a ship, greying what the viewer's flags don't allow.</summary>
    public void Update(WGShipAdminDetail detail, AdminFlags flags)
    {
        // A confirmation asked for the last ship must not be given for this one.
        if (Grid != detail.Grid)
            CancelConfirmations(Body);

        Grid = detail.Grid;
        Show(detail, flags);
    }

    private static void CancelConfirmations(Control parent)
    {
        foreach (var child in parent.Children)
        {
            if (child is ConfirmButton confirm)
                confirm.WgCancel();
            else
                CancelConfirmations(child);
        }
    }

    protected abstract void Show(WGShipAdminDetail detail, AdminFlags flags);

    /// <summary>Called with the ship list each time it changes, for tabs that pick another ship from it.</summary>
    public virtual void SetShips(List<WGShipAdminRow> ships)
    {
    }

    /// <summary>Called with the player companies each time the list is sent, for tabs that pick one.</summary>
    public virtual void SetCompanies(List<WGShipAdminCompany> companies)
    {
    }

    protected void Send(EuiMessageBase message)
    {
        OnMessage?.Invoke(message);
    }

    /// <summary>Disables a button the viewer's flags don't allow and says which permission it needs.</summary>
    protected static void Gate(BaseButton button, WGShipAdminAction action, AdminFlags flags, bool enabled = true)
    {
        var allowed = WGShipAdminPermissions.Allowed(flags, action);
        button.Disabled = !allowed || !enabled;
        if (!allowed)
            button.ToolTip = Loc.GetString("wg-ship-admin-needs-flag", ("flag", WGShipAdminPermissions.Required(action).ToString()));
    }

    protected static Label Heading(string text)
    {
        return new Label { Text = text, StyleClasses = { "LabelHeading" }, Margin = new Thickness(0, 6, 0, 0) };
    }

    /// <summary>
    /// A "key: value" line. The key keeps one column width in every tab; the value wraps, or is cut short when
    /// <paramref name="wrap"/> is off, as it must be in a row that ends in buttons.
    /// </summary>
    protected static Control Line(string key, string value, bool wrap = true)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 8 };
        row.AddChild(new Label
        {
            Text = key,
            StyleClasses = { "LabelKeyText" },
            SetWidth = KeyWidth,
            ClipText = true,
            VerticalAlignment = VAlignment.Top,
        });
        row.AddChild(wrap
            ? new WGShipAdminText(value)
            : new Label { Text = value, HorizontalExpand = true, ClipText = true });
        return row;
    }

    /// <summary>Controls side by side.</summary>
    protected static BoxContainer Row(params Control[] controls)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        foreach (var control in controls)
            row.AddChild(control);

        return row;
    }

    /// <summary>A dropdown with a search box, as every list in the panel has.</summary>
    protected static WGShipAdminDropdown Dropdown(float minWidth = 220)
    {
        return new WGShipAdminDropdown { MinWidth = minWidth, HorizontalAlignment = HAlignment.Left };
    }

    /// <summary>Keeps a control at its own width on the left.</summary>
    protected static Control Left(Control control)
    {
        control.HorizontalAlignment = HAlignment.Left;
        return control;
    }

    /// <summary>Secondary text, wrapped to the tab's width.</summary>
    protected static WGShipAdminText Note(string text)
    {
        return new WGShipAdminText(text, WGShipAdminText.Subdued);
    }

    /// <summary>A line of status text, wrapped to the tab's width.</summary>
    protected static WGShipAdminText Status()
    {
        return new WGShipAdminText();
    }
}
