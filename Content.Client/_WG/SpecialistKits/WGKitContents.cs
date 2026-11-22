using System.Linq;
using System.Numerics;
using Content.Client._RMC14.Vendors;
using Content.Shared._RMC14.Vendors;
using Content.Shared._WG.SpecialistKits;
using Content.Shared.Prototypes;
using Content.Shared.Roles;
using Content.Shared.Storage.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WG.SpecialistKits;

/// <summary>
/// A specialist kit's vendor row: clicking it opens a drop-down of the gear in its case, and a separate Take button
/// draws it. The row also shows how many the faction may still take, or which job it is for.
/// </summary>
public static class WGKitContents
{
    private static readonly Color WarningColor = Color.FromHex("#E0A030");
    private static readonly Color MutedColor = Color.FromHex("#A0A0A0");

    /// <summary>
    /// Wires the entry as a specialist kit if it is one: fills its drop-down, makes the row toggle it and the Take
    /// button vend. Returns false for any other entry, which keeps the vendor's own behaviour.
    /// </summary>
    public static bool Attach(CMAutomatedVendorEntry entry, EntityPrototype kit, IPrototypeManager prototypes,
        IComponentFactory factory, Action take)
    {
        if (!kit.TryComp<WGSpecialistKitComponent>(out var spec, factory)
            || !kit.TryComp<StorageFillComponent>(out var fill, factory)
            || fill.Contents.Count == 0)
            return false;

        if (spec.Warning is { } warning)
        {
            entry.Contents.AddChild(new Label
            {
                Text = Loc.GetString(warning),
                FontColorOverride = WarningColor,
                Margin = new Thickness(0, 0, 0, 4),
            });
        }

        // One row per item, in the case's order, with a count for repeats.
        foreach (var group in fill.Contents.Where(e => e.PrototypeId != null).GroupBy(e => e.PrototypeId!.Value))
        {
            if (!prototypes.TryIndex<EntityPrototype>(group.Key, out var item))
                continue;

            var count = group.Sum(e => e.Amount);
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

        entry.TooltipLabel.Visible = false;
        entry.WGTake.Visible = true;
        entry.WGTake.Text = Loc.GetString("wg-kit-take");
        entry.WGTake.OnPressed += _ => take();
        entry.WGStatus.Visible = true;
        if (spec.Jobs.Count > 0)
        {
            var jobs = spec.Jobs.Select(j => prototypes.TryIndex(j, out var job) ? job.LocalizedName : j.Id);
            entry.WGStatus.Text = Loc.GetString("wg-kit-status-job", ("jobs", string.Join(", ", jobs)));
        }

        var label = entry.Panel.Button.Label.Text ?? kit.Name;
        entry.Panel.Button.Label.Text = Loc.GetString("wg-kit-row-closed", ("name", label));
        entry.Panel.Button.OnPressed += _ =>
        {
            entry.Contents.Visible = !entry.Contents.Visible;
            entry.Panel.Button.Label.Text = Loc.GetString(
                entry.Contents.Visible ? "wg-kit-row-open" : "wg-kit-row-closed", ("name", label));
        };
        return true;
    }

    /// <summary>
    /// Keeps a kit row's drop-down usable while the kit can't be taken, and greys out only its Take button, with the
    /// reason as a tooltip. Does nothing for an entry <see cref="Attach"/> didn't wire.
    /// </summary>
    public static void Refresh(CMAutomatedVendorEntry entry, EntProtoId kitId, bool disabled, bool chosen,
        WGSpecialistVendorComponent? rack)
    {
        if (!entry.WGTake.Visible)
            return;

        entry.Panel.Button.Disabled = false;
        entry.Amount.Visible = false;

        var full = false;
        if (rack != null && rack.Left.TryGetValue(kitId, out var left))
        {
            full = left <= 0;
            entry.WGStatus.Text = Loc.GetString("wg-kit-status-left", ("left", left));
            entry.WGStatus.FontColorOverride = full ? Color.Red : MutedColor;
        }

        entry.WGTake.Disabled = disabled || full;
        entry.WGTake.ToolTip = chosen ? Loc.GetString("wg-kit-take-chosen")
            : full ? Loc.GetString("wg-specialist-kit-full")
            : null;
    }

    /// <summary>The section header on a specialist rack: its name and how many of its picks the player has used.</summary>
    public static FormattedMessage SectionName(CMVendorSection section, CMVendorUserComponent? user)
    {
        var name = Loc.TryGetString(section.Name, out var localized) ? localized : section.Name;
        var text = name.ToUpperInvariant();
        if (section.Choices is { } choices)
        {
            var taken = user?.Choices.GetValueOrDefault(choices.Id) ?? 0;
            text = Loc.GetString("wg-kit-section-taken", ("name", text), ("taken", taken), ("total", choices.Amount));
        }

        var msg = new FormattedMessage();
        msg.PushTag(new MarkupNode("bold", null, null));
        msg.AddText(text);
        msg.Pop();
        return msg;
    }
}
