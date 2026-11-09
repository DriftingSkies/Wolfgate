using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Content.Client.Resources;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Input;
using Robust.Shared.Utility;

namespace Content.Client._WF.LoreBook;

/// <summary>
/// <c>[lorelink="the Federation" link="TSFMC_Entry"]</c>: a gold link with a book icon that opens the lore book at
/// that page, from the guidebook or any other document.
/// </summary>
[UsedImplicitly]
public sealed class WFLoreLinkTag : IMarkupTag
{
    public string Name => "lorelink";

    public bool TryGetControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        if (!node.Value.TryGetString(out var text)
            || !node.Attributes.TryGetValue("link", out var linkParameter)
            || !linkParameter.TryGetString(out var link))
        {
            control = null;
            return false;
        }

        // The rules window on first join renders this before the entity systems exist.
        var cache = IoCManager.Resolve<IResourceCache>();
        var label = new Label
        {
            Text = text,
            FontColorOverride = WFLoreBookWindow.Gold,
            VerticalAlignment = Control.VAlignment.Center,
        };
        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            MouseFilter = Control.MouseFilterMode.Stop,
            DefaultCursorShape = Control.CursorShape.Hand,
            ToolTip = Loc.GetString("wf-lore-book-link-tooltip"),
        };
        box.AddChild(new TextureRect
        {
            Texture = cache.GetTexture(WFLoreBookSystem.IconPath),
            TextureScale = new(0.25f, 0.25f),
            Stretch = TextureRect.StretchMode.KeepCentered,
            ModulateSelfOverride = WFLoreBookWindow.Gold,
            VerticalAlignment = Control.VAlignment.Center,
            Margin = new Thickness(0, 0, 3, 0),
        });
        box.AddChild(label);

        box.OnMouseEntered += _ => label.FontColorOverride = WFLoreBookWindow.GoldBright;
        box.OnMouseExited += _ => label.FontColorOverride = WFLoreBookWindow.Gold;
        box.OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick
                || IoCManager.Resolve<IEntityManager>().EntitySysManager.GetEntitySystemOrNull<WFLoreBookSystem>() == null)
                return;

            IoCManager.Resolve<IUserInterfaceManager>().GetUIController<WFLoreBookUIController>().Open(link);
            args.Handle();
        };

        control = box;
        return true;
    }
}
