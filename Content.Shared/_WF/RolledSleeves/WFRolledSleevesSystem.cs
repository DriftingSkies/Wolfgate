using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Shared._WF.RolledSleeves;

/// <summary>Adds the roll sleeves and roll down verbs to jumpsuits.</summary>
public sealed partial class WFRolledSleevesSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private static readonly SpriteSpecifier VerbIcon =
        new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/fold.svg.192dpi.png"));

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFRolledSleevesComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(Entity<WFRolledSleevesComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        // Only the wearer restyles a worn suit.
        if (_inventory.TryGetContainingEntity(ent.Owner, out var wearer) && wearer != args.User)
            return;

        var user = args.User;
        var style = ent.Comp.Style;

        // Sleeves don't roll while the top is tied round the waist.
        if (style != WFJumpsuitStyle.RolledDown)
        {
            var rolled = style == WFJumpsuitStyle.SleevesRolled;
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString(rolled ? "wf-rolled-sleeves-verb-unroll" : "wf-rolled-sleeves-verb-roll"),
                Icon = VerbIcon,
                Act = () => SetStyle(ent, rolled ? WFJumpsuitStyle.Normal : WFJumpsuitStyle.SleevesRolled, user),
            });
        }

        var down = style == WFJumpsuitStyle.RolledDown;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(down ? "wf-rolled-sleeves-verb-pull-up" : "wf-rolled-sleeves-verb-roll-down"),
            Icon = VerbIcon,
            Act = () => SetStyle(ent, down ? WFJumpsuitStyle.Normal : WFJumpsuitStyle.RolledDown, user),
        });
    }

    /// <summary>Changes how the suit is worn and tells the user.</summary>
    public void SetStyle(Entity<WFRolledSleevesComponent> ent, WFJumpsuitStyle style, EntityUid user)
    {
        if (ent.Comp.Style == style)
            return;

        var msg = style switch
        {
            WFJumpsuitStyle.SleevesRolled => "wf-rolled-sleeves-popup-roll",
            WFJumpsuitStyle.RolledDown => "wf-rolled-sleeves-popup-roll-down",
            _ when ent.Comp.Style == WFJumpsuitStyle.RolledDown => "wf-rolled-sleeves-popup-pull-up",
            _ => "wf-rolled-sleeves-popup-unroll",
        };

        ent.Comp.Style = style;
        Dirty(ent);

        var ev = new WFJumpsuitStyleChangedEvent();
        RaiseLocalEvent(ent, ref ev);
        _popup.PopupClient(Loc.GetString(msg, ("item", ent.Owner)), user, user);
    }
}

/// <summary>Raised on a jumpsuit when its style changes, so the client redraws it.</summary>
[ByRefEvent]
public record struct WFJumpsuitStyleChangedEvent;
