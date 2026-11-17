using System.Linq;
using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using ConsoleTheme = Content.Client._WG.CombatConsole.WGInstrumentTheme;
using Content.Client._WG.CombatConsole;

namespace Content.Client._WG.ShipAccess;

public sealed partial class ShipAccessScreen
{
    /// <summary>Distributes the existing access editor across framed, independently readable regions.</summary>
    private void WgRefitInstruments()
    {
        var status = OwnerLabel.Parent!;
        ConsoleTheme.Detach(status);
        status.Margin = new Thickness(0);
        var settings = ConsoleTheme.Panel("ship-access-title", ConsoleTheme.Column(status, ReadOnlyLabel, CodeBox));
        settings.Name = "WgAccessSettings";
        ReadOnlyLabel.ToolTip = ReadOnlyLabel.Text;
        foreach (var label in new[] { ReadOnlyLabel, OwnerLabel, DoorNameLabel, DoorRuleLabel, CodeAlertLabel })
            label.MouseFilter = MouseFilterMode.Pass;
        LockedCheck.HorizontalAlignment = HAlignment.Stretch;
        foreach (var label in new[] { ReadOnlyLabel, AllowListEmptyLabel, NearbyEmptyLabel, DoorNoneLabel, DoorPlayersEmptyLabel, CodeAlertLabel })
        {
            label.HorizontalExpand = true;
            label.HorizontalAlignment = HAlignment.Stretch;
            label.Align = Label.AlignMode.Center;
        }
        WgRefitCodeBox(CodeBox, ShipCodeLabel, ShipCodeRevealButton, ShipCodeEdit,
            ShipCodeSetButton, ShipCodeClearButton, ShipCodeHint, CodeAlertLabel);

        var nearbyCaption = NearbyBox.Children.OfType<Label>().First();
        var allowed = new WGAccessListRegion(AllowListContainer) { Name = "WgAccessAllowedList" };
        var nearby = new WGAccessListRegion(NearbyContainer) { Name = "WgAccessNearbyList" };
        var nearbyContents = ConsoleTheme.Column(nearbyCaption, nearby, NearbyEmptyLabel);
        nearbyContents.VerticalExpand = true;
        NearbyBox.RemoveAllChildren();
        NearbyBox.AddChild(nearbyContents);
        NearbyBox.VerticalExpand = true;
        var peopleContents = ConsoleTheme.Column(allowed, AllowListEmptyLabel, NearbyBox);
        peopleContents.VerticalExpand = true;
        var people = ConsoleTheme.Panel("ship-access-allow-list", peopleContents, true);
        people.Name = "WgAccessPeople";

        var allDoorControls = ConsoleTheme.Column(AllDoorsRuleButton, AllDoorsApplyButton);
        ConsoleTheme.Detach(AllDoorsHint);
        AllDoorsBox.RemoveAllChildren();
        AllDoorsBox.AddChild(ConsoleTheme.Label("ship-access-all-doors"));
        AllDoorsBox.AddChild(allDoorControls);
        AllDoorsBox.AddChild(AllDoorsHint);
        var doorNameCaption = DoorNameLabel.Parent!.Children.OfType<Label>().First(label => label != DoorNameLabel && label != DoorRuleLabel);
        var doorRuleCaption = DoorRuleLabel.Parent!.Children.OfType<Label>().Last(label => label != DoorNameLabel && label != DoorRuleLabel);
        var doorStatus = ConsoleTheme.Column(ConsoleTheme.Row(doorNameCaption, DoorNameLabel), ConsoleTheme.Row(doorRuleCaption, DoorRuleLabel));
        var playersCaption = DoorPlayersBox.Children.OfType<Label>().First();
        var players = new WGAccessListRegion(DoorPlayersContainer) { Name = "WgAccessDoorList" };
        var playerContents = ConsoleTheme.Column(playersCaption, players, DoorPlayersEmptyLabel);
        playerContents.VerticalExpand = true;
        DoorPlayersBox.RemoveAllChildren();
        DoorPlayersBox.AddChild(playerContents);
        DoorPlayersBox.VerticalExpand = true;
        WgRefitCodeBox(DoorCodeBox, DoorCodeLabel, DoorCodeRevealButton, DoorCodeEdit,
            DoorCodeSetButton, DoorCodeClearButton, DoorCodeHint);
        var selected = ConsoleTheme.Column(doorStatus, DoorRuleButton, DoorRuleHint, DoorCodeBox, DoorPlayersBox);
        selected.VerticalExpand = true;
        DoorBox.RemoveAllChildren();
        DoorBox.AddChild(selected);
        DoorBox.VerticalExpand = true;
        var doorContents = ConsoleTheme.Column(AllDoorsBox, DoorNoneLabel, DoorBox);
        doorContents.VerticalExpand = true;
        var doors = ConsoleTheme.Panel("ship-access-door", doorContents, true);
        doors.Name = "WgAccessDoors";

        foreach (var hint in new[] { ShipCodeHint, DoorCodeHint, DoorRuleHint, AllDoorsHint })
            hint.Visible = false;
        ShipCodeEdit.ToolTip = ShipCodeHint.ToolTip;
        DoorCodeEdit.ToolTip = DoorCodeHint.ToolTip;
        AllDoorsRuleButton.ToolTip = AllDoorsHint.ToolTip;
        LegendContainer.Columns = 1;
        LegendContainer.HorizontalAlignment = HAlignment.Stretch;
        LegendContainer.HorizontalExpand = true;
        var plot = ConsoleTheme.Column(ConsoleTheme.Scope("wg-access-door-diagram", DoorMap, Vector2.Zero), DoorsEmptyLabel);
        plot.Name = "WgAccessPlot";
        plot.HorizontalExpand = plot.VerticalExpand = true;
        var layout = new WGShipAccessLayout(plot, settings, people, doors, LegendContainer, allowed, nearby, players);
        RemoveAllChildren();
        Margin = new Thickness(0);
        VerticalExpand = true;
        AddChild(layout);
        WgStyleAccess(this);
    }

    private static void WgRefitCodeBox(BoxContainer box, Label value, Button reveal, LineEdit edit,
        Button set, Button clear, RichTextLabel hint, Label? alert = null)
    {
        var caption = value.Parent!.Children.OfType<Label>().Single(label => label != value);
        var readout = ConsoleTheme.Row(caption, value, reveal);
        var input = ConsoleTheme.Row(edit, set, clear);
        ConsoleTheme.Detach(hint);
        if (alert != null) ConsoleTheme.Detach(alert);
        box.RemoveAllChildren();
        box.AddChild(readout);
        box.AddChild(input);
        box.AddChild(hint);
        if (alert != null) box.AddChild(alert);
        box.SeparationOverride = 4;
        edit.MinWidth = 64;
        edit.MinHeight = 32;
        reveal.MinWidth = reveal.SetWidth = 52;
        set.MinWidth = set.SetWidth = 48;
        clear.MinWidth = clear.SetWidth = 56;
    }

    /// <summary>Styles newly created access rows and the original controls with the same console palette.</summary>
    private static void WgStyleAccess(Control control)
    {
        control.Margin = new Thickness(0);
        if (control is Label label)
        {
            if (label.HorizontalExpand && label is not WGWrappedLabel)
                label.ClipText = true;
            label.ToolTip ??= label.Text;
        }
        if (control is Button or CheckBox)
        {
            control.AddStyleClass("WgCompact");
            control.MinHeight = 32;
            control.ToolTip ??= control is Button button ? button.Text : ((CheckBox) control).Text;
            ConsoleTheme.Apply(control);
            return;
        }
        if (control is OptionButton option)
        {
            option.MinHeight = 32;
            option.StyleBoxOverride = new WGConsoleStyleBox("button_up", false, false, compact: true);
        }
        foreach (var child in control.Children)
            WgStyleAccess(child);
        ConsoleTheme.Apply(control);
    }

    /// <summary>Keeps each crew entry to one readout row and one permission row.</summary>
    private static Control WgAccessPersonRow(BoxContainer row)
    {
        var labels = row.Children.OfType<Label>().ToArray();
        var actions = row.Children.Where(child => child is not Label).ToArray();
        foreach (var label in labels)
        {
            label.MouseFilter = MouseFilterMode.Pass;
            label.HorizontalExpand = true;
            label.SizeFlagsStretchRatio = label == labels[0] ? 3 : 2;
        }
        foreach (var action in actions)
            action.HorizontalExpand = true;
        var text = ConsoleTheme.Row(labels);
        var contents = actions.Length == 0 ? text : ConsoleTheme.Column(text, ConsoleTheme.Row(actions));
        contents.SeparationOverride = 4;
        row.RemoveAllChildren();
        row.AddChild(contents);
        row.Orientation = LayoutOrientation.Vertical;
        WgStyleAccess(row);
        return row;
    }
}
