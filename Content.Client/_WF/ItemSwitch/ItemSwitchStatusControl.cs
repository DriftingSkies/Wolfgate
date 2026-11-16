using Content.Client.Items.UI;
using Content.Client.Message;
using Content.Client.Stylesheets;
using Content.Shared._WF.ItemSwitch.Components;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.ItemSwitch;

/// <summary>Shows the item's current switch state in the held-item status bar.</summary>
public sealed class ItemSwitchStatusControl : PollingItemStatusControl<ItemSwitchStatusControl.Data>
{
    private readonly Entity<ItemSwitchComponent> _parent;
    private readonly RichTextLabel _label;

    public ItemSwitchStatusControl(Entity<ItemSwitchComponent> parent)
    {
        _parent = parent;
        _label = new RichTextLabel { StyleClasses = { StyleNano.StyleClassItemStatus } };
        if (parent.Comp.ShowLabel)
            AddChild(_label);

        UpdateDraw();
    }

    protected override Data PollData()
    {
        return new Data(_parent.Comp.State);
    }

    protected override void Update(in Data data)
    {
        var stateName = Loc.TryGetString($"itemswitch-state-{data.State}", out var localized)
            ? localized
            : data.State;

        _label.SetMarkup(Loc.GetString("itemswitch-component-on-examine-detailed-message",
            ("state", stateName)));
    }

    public record struct Data(string State);
}
