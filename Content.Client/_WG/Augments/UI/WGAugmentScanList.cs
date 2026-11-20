using System.Linq;
using Content.Shared._WG.Augments;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.Augments.UI;

/// <summary>The cyberware a health analyzer scan found: a heading and one row per augment, or nothing.</summary>
public sealed class WGAugmentScanList : BoxContainer
{
    private static readonly Color MutedColor = Color.FromHex("#A0A0A8");

    private readonly IPrototypeManager _proto = IoCManager.Resolve<IPrototypeManager>();
    private readonly List<ProtoId<WGAugmentPrototype>> _shown = new();

    public WGAugmentScanList()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        Visible = false;
    }

    /// <summary>Shows these augments, redrawing only when they changed.</summary>
    public void Populate(IReadOnlyList<ProtoId<WGAugmentPrototype>>? augments)
    {
        if (augments == null || augments.Count == 0)
        {
            _shown.Clear();
            RemoveAllChildren();
            Visible = false;
            return;
        }

        if (_shown.SequenceEqual(augments))
            return;

        _shown.Clear();
        _shown.AddRange(augments);
        RemoveAllChildren();
        Visible = true;

        AddChild(new Label
        {
            Text = Loc.GetString("wg-analyzer-cyberware"),
            StyleClasses = { "LabelHeading" },
            Margin = new Thickness(0, 8, 0, 0),
        });
        AddChild(new PanelContainer { StyleClasses = { "LowDivider" }, Margin = new Thickness(0, 2, 0, 4) });

        foreach (var id in augments)
        {
            if (!_proto.TryIndex(id, out var augment))
                continue;

            var row = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 8,
                ToolTip = WGAugmentRules.Description(augment, _proto),
            };
            row.AddChild(new Label
            {
                Text = WGAugmentEditor.SlotName(augment.Slot),
                FontColorOverride = MutedColor,
                MinSize = new System.Numerics.Vector2(90, 0),
            });
            row.AddChild(new Label
            {
                Text = WGAugmentRules.Name(augment, _proto),
                HorizontalExpand = true,
                ClipText = true,
            });
            AddChild(row);
        }
    }
}
