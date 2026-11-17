using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Shared._WG.RolledSleeves;

/// <summary>Adds the roll sleeves and roll down verbs to jumpsuits.</summary>
public sealed partial class WGRolledSleevesSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private static readonly SpriteSpecifier VerbIcon =
        new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/fold.svg.192dpi.png"));

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGRolledSleevesComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(Entity<WGRolledSleevesComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        // Only the wearer restyles a worn suit.
        if (_inventory.TryGetContainingEntity(ent.Owner, out var wearer) && wearer != args.User)
            return;

        var user = args.User;
        var style = ent.Comp.Style;

        // Sleeves don't roll while the top is tied round the waist.
        if (style != WGJumpsuitStyle.RolledDown)
        {
            var rolled = style == WGJumpsuitStyle.SleevesRolled;
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString(rolled ? "wg-rolled-sleeves-verb-unroll" : "wg-rolled-sleeves-verb-roll"),
                Icon = VerbIcon,
                Act = () => SetStyle(ent, rolled ? WGJumpsuitStyle.Normal : WGJumpsuitStyle.SleevesRolled, user),
            });
        }

        var down = style == WGJumpsuitStyle.RolledDown;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(down ? "wg-rolled-sleeves-verb-pull-up" : "wg-rolled-sleeves-verb-roll-down"),
            Icon = VerbIcon,
            Act = () => SetStyle(ent, down ? WGJumpsuitStyle.Normal : WGJumpsuitStyle.RolledDown, user),
        });
    }

    /// <summary>Changes how the suit is worn and tells the user.</summary>
    public void SetStyle(Entity<WGRolledSleevesComponent> ent, WGJumpsuitStyle style, EntityUid user)
    {
        if (ent.Comp.Style == style)
            return;

        var msg = style switch
        {
            WGJumpsuitStyle.SleevesRolled => "wg-rolled-sleeves-popup-roll",
            WGJumpsuitStyle.RolledDown => "wg-rolled-sleeves-popup-roll-down",
            _ when ent.Comp.Style == WGJumpsuitStyle.RolledDown => "wg-rolled-sleeves-popup-pull-up",
            _ => "wg-rolled-sleeves-popup-unroll",
        };

        ent.Comp.Style = style;
        Dirty(ent);

        var ev = new WGJumpsuitStyleChangedEvent();
        RaiseLocalEvent(ent, ref ev);
        _popup.PopupClient(Loc.GetString(msg, ("item", ent.Owner)), user, user);
    }
}

/// <summary>Raised on a jumpsuit when its style changes, so the client redraws it.</summary>
[ByRefEvent]
public record struct WGJumpsuitStyleChangedEvent;
