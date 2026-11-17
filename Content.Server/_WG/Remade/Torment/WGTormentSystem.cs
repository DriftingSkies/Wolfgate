using Content.Server._WG.Remade.Director;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Mobs.Components;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WG.Remade.Torment;

/// <summary>
/// The Remade smites: torment toggles a player into hallucinating everything a Crescendo stone does, and biohazard lockdown
/// bolts the doors round them for a minute under the alarm.
/// </summary>
public sealed class WGTormentSystem : EntitySystem
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private WGRemadeDirectorSystem _director = default!;

    private const float LockdownSeconds = 60f;

    public override void Initialize()
    {
        base.Initialize();

        // Broadcast, like the stock smites, so it sits with them on any mob.
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (!HasComp<MobStateComponent>(args.Target) || !TryComp<ActorComponent>(args.User, out var actor)
            || !_admins.HasAdminFlag(actor.PlayerSession, AdminFlags.Fun))
        {
            return;
        }

        var target = args.Target;
        var tormented = HasComp<WGTormentedComponent>(target);
        var name = Loc.GetString(tormented ? "wg-remade-torment-end-name" : "wg-remade-torment-name").ToLowerInvariant();
        args.Verbs.Add(new Verb
        {
            Text = name,
            Category = VerbCategory.Smite,
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/smite.svg.192dpi.png")),
            Act = () => Toggle(target),
            Impact = LogImpact.Extreme,
            Message = string.Join(": ", name, Loc.GetString(tormented ? "wg-remade-torment-end-description" : "wg-remade-torment-description")),
        });

        var lockdown = Loc.GetString("wg-remade-lockdown-smite-name").ToLowerInvariant();
        args.Verbs.Add(new Verb
        {
            Text = lockdown,
            Category = VerbCategory.Smite,
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/smite.svg.192dpi.png")),
            Act = () => _director.Lockdown(target, LockdownSeconds, WGRemadeDirectorSystem.LockdownRange),
            Impact = LogImpact.Extreme,
            Message = string.Join(": ", lockdown, Loc.GetString("wg-remade-lockdown-smite-description")),
        });
    }

    /// <summary>Turns the torment on or off. Returns whether it is on now.</summary>
    public bool Toggle(EntityUid target)
    {
        if (RemComp<WGTormentedComponent>(target))
            return false;

        EnsureComp<WGTormentedComponent>(target);
        return true;
    }
}
