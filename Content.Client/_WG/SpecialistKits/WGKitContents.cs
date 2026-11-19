using System.Linq;
using System.Numerics;
using Content.Client._RMC14.Vendors;
using Content.Shared._RMC14.Vendors;
using Content.Shared._WG.SpecialistKits;
using Content.Shared.Prototypes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.SpecialistKits;

/// <summary>Turns a specialist kit's "?" button into a drop-down that lists the kit's gear under its vendor row.</summary>
public static class WGKitContents
{
    /// <summary>Fills the entry's contents list and wires its toggle, if the entry is a specialist kit.</summary>
    public static void Attach(CMAutomatedVendorEntry entry, EntityPrototype kit, IPrototypeManager prototypes,
        IComponentFactory factory)
    {
        if (!kit.HasComponent<WGSpecialistKitComponent>(factory)
            || !kit.TryGetComponent<CMVendorBundleComponent>(out var bundle, factory)
            || bundle.Bundle.Count == 0)
            return;

        // One row per item, in the bundle's order, with a count for repeats.
        foreach (var group in bundle.Bundle.GroupBy(id => id.Id))
        {
            if (!prototypes.TryIndex<EntityPrototype>(group.Key, out var item))
                continue;

            var count = group.Count();
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
            var icon = new EntityPrototypeView { SetSize = new Vector2(32, 32), Stretch = SpriteView.StretchMode.Fill };
            icon.SetPrototype(item.ID);
            row.AddChild(icon);
            row.AddChild(new Label
            {
                Text = count > 1
                    ? Loc.GetString("wg-kit-contents-many", ("count", count), ("name", item.Name))
                    : item.Name,
                VerticalAlignment = Control.VAlignment.Center,
            });
            entry.Contents.AddChild(row);
        }

        var toggle = entry.TooltipLabel;
        toggle.ToolTip = null;
        toggle.TooltipSupplier = null;
        toggle.Text = Loc.GetString("wg-kit-contents-closed");
        toggle.OnPressed += _ =>
        {
            entry.Contents.Visible = !entry.Contents.Visible;
            toggle.Text = Loc.GetString(entry.Contents.Visible ? "wg-kit-contents-open" : "wg-kit-contents-closed");
        };
    }
}
