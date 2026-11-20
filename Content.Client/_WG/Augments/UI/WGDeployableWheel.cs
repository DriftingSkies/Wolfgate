using System.Numerics;
using Content.Client._WG.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared._WG.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.Augments.UI;

/// <summary>
/// The wheel a deployable implant with several items opens: one sector per item, the one that is out lit in the
/// skin's accent and stowed by picking it again.
/// </summary>
public sealed class WGDeployableWheel : RadialMenu
{
    /// <summary>Raised with the index of the item the player picked.</summary>
    public event Action<int>? OnPicked;

    public WGDeployableWheel(IReadOnlyList<EntProtoId> items, int? deployed)
    {
        var entities = IoCManager.Resolve<IEntityManager>();
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        var accent = WolfgateSkins.Get(IoCManager.Resolve<IConfigurationManager>().GetCVar(WolfgateCVars.UiStyle)).Accent;

        BackButtonStyleClass = "RadialMenuBackButton";
        CloseButtonStyleClass = "RadialMenuCloseButton";
        HorizontalExpand = true;
        VerticalExpand = true;
        // Only how close to the screen's edge the wheel's center can open.
        MinSize = new Vector2(450, 450);

        var main = new RadialContainer
        {
            Name = "Main",
            HorizontalExpand = true,
            VerticalExpand = true,
            InitialRadius = 100,
            ReserveSpaceForHiddenChildren = false,
        };

        for (var i = 0; i < items.Count; i++)
        {
            if (!prototypes.TryIndex<EntityPrototype>(items[i], out var proto))
                continue;

            var index = i;
            var button = new RadialMenuTextureButtonWithSector
            {
                SetSize = new Vector2(64, 64),
                ToolTip = i == deployed ? Loc.GetString("wg-augment-deployable-stow", ("item", proto.Name)) : proto.Name,
            };
            if (i == deployed)
            {
                button.DrawBorder = true;
                button.BorderColor = accent;
                button.HoverBorderColor = accent;
                button.BackgroundColor = accent.WithAlpha(0.3f);
                button.HoverBackgroundColor = accent.WithAlpha(0.45f);
            }

            button.AddChild(new EntityPrototypeView(items[i], entities)
            {
                SetSize = new Vector2(64, 64),
                // A view sizes itself for a sprite turned any way, and would fit this one in at less than double.
                Stretch = SpriteView.StretchMode.None,
                Scale = new Vector2(2, 2),
                OverrideDirection = Direction.South,
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
                MouseFilter = MouseFilterMode.Ignore,
            });
            button.OnButtonUp += _ =>
            {
                OnPicked?.Invoke(index);
                Close();
            };
            main.AddChild(button);
        }

        AddChild(main);
    }
}
