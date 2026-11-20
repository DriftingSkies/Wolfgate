using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Mobs.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WG.Wolfmed.Life;

/// <summary>The heart attack smite: stops a wound host's heart, with the usual collapse before it goes down.</summary>
public sealed class WolfmedHeartAttackSmiteSystem : EntitySystem
{
    /// <summary>Arrest cause for the smite. Read as a failed heart, but a healthy heart does not restart it.</summary>
    public const string Cause = "infarction";

    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Broadcast, like the stock smites, so it sits with them on any mob.
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor) ||
            !_admins.HasAdminFlag(actor.PlayerSession, AdminFlags.Fun))
            return;

        var target = args.Target;
        if (!_life.OwnsDeath(target) || _life.InArrest(target) || _mobState.IsDead(target))
            return;

        var name = Loc.GetString("wolfmed-smite-heart-attack-name").ToLowerInvariant();
        args.Verbs.Add(new Verb
        {
            Text = name,
            Category = VerbCategory.Smite,
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/smite.svg.192dpi.png")),
            Act = () => _life.StartArrest(target, Cause),
            Impact = LogImpact.Extreme,
            Message = string.Join(": ", name, Loc.GetString("wolfmed-smite-heart-attack-description")),
        });
    }
}
