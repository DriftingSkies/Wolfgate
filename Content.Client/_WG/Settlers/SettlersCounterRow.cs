using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared._WG.Settlers;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Settlers;

/// <summary>
/// One stepper per resource, each up to its limit, after an optional label. The icons sit in a separate
/// <see cref="Header"/> row above, so stacked rows line up and stay narrow.
/// </summary>
public sealed class SettlersCounterRow : BoxContainer
{
    public const float LabelWidth = 44f;

    private readonly Label[] _labels = new Label[SettlersRules.ResourceCount];
    private readonly Button[] _less = new Button[SettlersRules.ResourceCount];
    private readonly Button[] _more = new Button[SettlersRules.ResourceCount];

    public readonly int[] Counts = new int[SettlersRules.ResourceCount];

    /// <summary>The most of each resource that may be set.</summary>
    public readonly int[] Limits = new int[SettlersRules.ResourceCount];

    public event Action? OnChanged;

    public SettlersCounterRow(string? label = null)
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 4;
        Array.Fill(Limits, SettlersRules.BankStock);
        AddLabel(this, label);

        for (var resource = 0; resource < SettlersRules.ResourceCount; resource++)
        {
            var index = resource;
            var cell = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                Align = AlignMode.Center,
                SeparationOverride = 1,
                ToolTip = SettlersArt.ResourceName(resource),
                MouseFilter = MouseFilterMode.Pass,
            };

            _less[resource] = new Button { Text = "-", MinWidth = 18f, StyleClasses = { StyleBase.ButtonSquare } };
            _more[resource] = new Button { Text = "+", MinWidth = 18f, StyleClasses = { StyleBase.ButtonSquare } };
            _labels[resource] = new Label { MinWidth = 16f, Align = Label.AlignMode.Center, VerticalAlignment = VAlignment.Center };
            _less[resource].OnPressed += _ => Set(index, Counts[index] - 1);
            _more[resource].OnPressed += _ => Set(index, Counts[index] + 1);
            cell.AddChild(_less[resource]);
            cell.AddChild(_labels[resource]);
            cell.AddChild(_more[resource]);
            AddChild(cell);
        }

        Refresh();
    }

    /// <summary>The resource icons, spaced to sit over the steppers of rows below.</summary>
    public static BoxContainer Header(bool labelled)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        if (labelled)
            AddLabel(row, string.Empty);

        for (var resource = 0; resource < SettlersRules.ResourceCount; resource++)
        {
            row.AddChild(new TextureRect
            {
                Texture = SettlersArt.ResourceTexture(resource),
                Stretch = TextureRect.StretchMode.KeepAspectCentered,
                MinSize = new Vector2(24f, 24f),
                HorizontalExpand = true,
                ToolTip = SettlersArt.ResourceName(resource),
                MouseFilter = MouseFilterMode.Pass,
            });
        }

        return row;
    }

    private static void AddLabel(BoxContainer row, string? text)
    {
        if (text == null)
            return;

        row.AddChild(new Label
        {
            Text = text,
            MinWidth = LabelWidth,
            VerticalAlignment = VAlignment.Center,
            FontColorOverride = SettlersArt.Muted,
        });
    }

    private void Set(int resource, int count)
    {
        count = Math.Clamp(count, 0, Limits[resource]);
        if (count == Counts[resource])
            return;

        Counts[resource] = count;
        Refresh();
        OnChanged?.Invoke();
    }

    public void Clear()
    {
        Array.Clear(Counts);
        Refresh();
    }

    /// <summary>Sets new limits and pulls any count over them back down.</summary>
    public void SetLimits(int[] limits)
    {
        Array.Copy(limits, Limits, Limits.Length);
        for (var resource = 0; resource < Counts.Length; resource++)
        {
            Counts[resource] = Math.Min(Counts[resource], Limits[resource]);
        }

        Refresh();
    }

    private void Refresh()
    {
        for (var resource = 0; resource < Counts.Length; resource++)
        {
            _labels[resource].Text = Counts[resource].ToString();
            _labels[resource].FontColorOverride = Counts[resource] > 0 ? null : SettlersArt.Muted;
            _less[resource].Disabled = Counts[resource] <= 0;
            _more[resource].Disabled = Counts[resource] >= Limits[resource];
        }
    }
}
