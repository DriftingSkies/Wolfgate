using System.Diagnostics.CodeAnalysis;
using Content.Client._WG.Stylesheets;
using Content.Shared._WG.CCVar;
using Robust.Client.Console;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Utility;

namespace Content.Client._WG.Administration.AdminFollow;

/// <summary>
/// <c>[adminfollow=NETENTITY/]</c>: a clickable link on an admin alert that follows its entity as an observer, through
/// the <c>follow</c> command, which aghosts an admin in a body first. Added by <see cref="AdminFollowLinkSystem"/>.
/// </summary>
public sealed partial class AdminFollowLinkTag : IMarkupTagHandler
{
    public const string TagName = "adminfollow";

    [Dependency] private IClientConsoleHost _console = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    public string Name => TagName;

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        control = null;
        if (!TryGetTarget(node, out var target))
            return false;

        var skin = WolfgateSkins.Get(_cfg.GetCVar(WolfgateCVars.UiStyle));
        var label = new Label
        {
            Text = Loc.GetString("wg-admin-follow-chat-link"),
            ToolTip = Loc.GetString("wg-admin-follow-chat-link-tooltip"),
            FontColorOverride = skin.AccentDim,
            MouseFilter = Control.MouseFilterMode.Stop,
            DefaultCursorShape = Control.CursorShape.Hand,
        };
        label.OnMouseEntered += _ => label.FontColorOverride = skin.Accent;
        label.OnMouseExited += _ => label.FontColorOverride = skin.AccentDim;
        label.OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            args.Handle();
            _console.ExecuteCommand($"follow {target}");
        };

        control = label;
        return true;
    }

    private static bool TryGetTarget(MarkupNode node, out NetEntity target)
    {
        target = NetEntity.Invalid;
        if (node.Value.TryGetLong(out var number))
        {
            target = new NetEntity((int) number.Value);
            return target.Valid;
        }

        if (node.Value.TryGetString(out var text) && int.TryParse(text, out var id))
        {
            target = new NetEntity(id);
            return target.Valid;
        }

        return false;
    }
}
