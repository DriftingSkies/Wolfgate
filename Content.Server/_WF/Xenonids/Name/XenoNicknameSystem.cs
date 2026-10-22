using System.Linq;
using Content.Server.Administration;
using Content.Shared._WF.Xenonids.Evolution;
using Content.Shared._WF.Xenonids.Name;
using Content.Shared.Actions;
using Content.Shared.NameModifier.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server._WF.Xenonids.Name;

/// <summary>
/// A xeno may name itself once: a tag of up to four letters or digits that replaces the XX in "Drone (XX-297)".
/// The tag follows it through evolution, and the action goes away once used.
/// </summary>
public sealed class XenoNicknameSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private NameModifierSystem _nameModifier = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private QuickDialogSystem _quickDialog = default!;
    [Dependency] private XenoNameSystem _xenoName = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<XenoNicknameComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<XenoNicknameComponent, XenoNicknameActionEvent>(OnAction);
        SubscribeLocalEvent<NewXenoEvolvedEvent>(OnEvolved, after: new[] { typeof(SharedXenoNameSystem) });
        SubscribeLocalEvent<XenoDevolvedEvent>(OnDevolved, after: new[] { typeof(SharedXenoNameSystem) });
    }

    private void OnMapInit(Entity<XenoNicknameComponent> ent, ref MapInitEvent args)
    {
        RefreshAction(ent.AsNullable());
    }

    private void OnEvolved(ref NewXenoEvolvedEvent ev)
    {
        RefreshAction(ev.NewXeno);
    }

    private void OnDevolved(ref XenoDevolvedEvent ev)
    {
        RefreshAction(ev.NewXeno);
    }

    private void OnAction(Entity<XenoNicknameComponent> ent, ref XenoNicknameActionEvent args)
    {
        if (args.Handled || !TryComp(ent, out ActorComponent? actor))
            return;

        args.Handled = true;
        if (IsNamed(ent))
        {
            _popup.PopupEntity(Loc.GetString("wf-xeno-nickname-already"), ent, ent);
            RefreshAction(ent.AsNullable());
            return;
        }

        var xeno = ent.Owner;
        _quickDialog.OpenDialog(actor.PlayerSession,
            Loc.GetString("wf-xeno-nickname-title"),
            Loc.GetString("wf-xeno-nickname-prompt", ("max", ent.Comp.MaxLength)),
            (string name) =>
            {
                if (TerminatingOrDeleted(xeno))
                    return;

                if (!TrySetNickname(xeno, name, out var error))
                    _popup.PopupEntity(error, xeno, xeno, PopupType.MediumCaution);
            });
    }

    /// <summary>Sets the xeno's tag if it is still unnamed and the tag is one to MaxLength letters or digits.</summary>
    public bool TrySetNickname(EntityUid xeno, string name, out string error)
    {
        error = string.Empty;
        var max = CompOrNull<XenoNicknameComponent>(xeno)?.MaxLength ?? 4;
        if (IsNamed(xeno))
        {
            error = Loc.GetString("wf-xeno-nickname-already");
            return false;
        }

        name = name.Trim().ToUpperInvariant();
        if (name.Length == 0 || name.Length > max || !name.All(char.IsAsciiLetterOrDigit))
        {
            error = Loc.GetString("wf-xeno-nickname-invalid", ("max", max));
            return false;
        }

        _xenoName.SetPrefix(xeno, name, named: true);
        _nameModifier.RefreshNameModifiers(xeno);
        _popup.PopupEntity(Loc.GetString("wf-xeno-nickname-set", ("name", name)), xeno, xeno);
        RefreshAction(xeno);
        return true;
    }

    private bool IsNamed(EntityUid xeno)
    {
        return TryComp(xeno, out XenoNameComponent? name) && name.Named;
    }

    /// <summary>The action exists only while the xeno has not named itself.</summary>
    private void RefreshAction(Entity<XenoNicknameComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        if (IsNamed(ent))
        {
            if (ent.Comp.ActionEntity is { } action)
            {
                _actions.RemoveAction(ent.Owner, action);
                ent.Comp.ActionEntity = null;
            }

            return;
        }

        if (ent.Comp.ActionEntity == null || !Exists(ent.Comp.ActionEntity))
            _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }
}
