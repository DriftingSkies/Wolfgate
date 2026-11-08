using Content.Server._WF.Outposts.Components;
using Content.Server._WF.ShipAccess;
using Content.Shared._Mono.Company;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access.Components;
using Content.Shared.Mind;
using Content.Shared.UserInterface;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Outposts take ship access (ShipAccess): registered to the owner's account, edited on the console's Access tab, and
/// with the allow list kept in saves by account and character, keyed again to each character's card when they play.
/// </summary>
public sealed partial class WFOutpostSystem
{
    [Dependency] private WFShipAccessServerSystem _shipAccess = default!;
    [Dependency] private WFShipAccessSystem _access = default!;

    /// <summary>How often allow list entries are matched to the players holding them, and saved grants to new cards.</summary>
    private static readonly TimeSpan AccessCheckInterval = TimeSpan.FromSeconds(2);

    private TimeSpan _nextAccessCheck;

    private void InitializeAccess()
    {
        SubscribeLocalEvent<WFOutpostConsoleComponent, ActivatableUIOpenAttemptEvent>(OnConsoleOpenAttempt);
    }

    private void OnConsoleOpenAttempt(Entity<WFOutpostConsoleComponent> console, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled || console.Comp.Outpost is not { } grid || !TryComp<WFOutpostComponent>(grid, out var outpost)
            || IsOwnerOf(args.User, outpost))
            return;

        args.Cancel();
        _popup.PopupEntity(Loc.GetString("wf-outpost-console-not-yours"), console, args.User);
    }

    /// <summary>Whether the user's player owns the outpost, whatever body they are in.</summary>
    public bool IsOwnerOf(EntityUid user, WFOutpostComponent outpost)
    {
        return TryComp<ActorComponent>(user, out var actor) && actor.PlayerSession.UserId == outpost.Owner;
    }

    /// <summary>Whether the user may lay plates and roof panels on the outpost: its owner, or a card listed there as a builder's.</summary>
    public bool CanBuild(EntityUid user, Entity<WFOutpostComponent> outpost)
    {
        if (IsOwnerOf(user, outpost.Comp))
            return true;

        return TryComp<WFShipAccessComponent>(outpost, out var access)
               && _access.TryGetCard(user, out var card)
               && _access.TryGetKey(card, out var key)
               && _access.TryGetEntry(access, key, out var entry)
               && entry.Builder;
    }

    /// <summary>
    /// Puts ship access on a founded outpost, registered to its owner's account; a member of a faction company founds
    /// it under that company, so it starts in Faction mode. A loaded outpost keeps its saved mode, lock and door rules
    /// and is registered to its owner again.
    /// </summary>
    private void SetupAccess(Entity<WFOutpostComponent> outpost, EntityUid? founder)
    {
        if (HasComp<WFShipAccessComponent>(outpost))
        {
            _shipAccess.AddOwnerUser(outpost, outpost.Comp.Owner);
            return;
        }

        if (founder is { } user && _access.TryGetCard(user, out var card)
            && TryComp<IdCardComponent>(card, out var id) && !string.IsNullOrEmpty(id.CompanyName))
        {
            var company = EnsureComp<CompanyComponent>(outpost);
            company.CompanyName = id.CompanyName;
            Dirty(outpost, company);
        }

        _shipAccess.SetupRegisteredShip(outpost, outpost.Comp.OwnerName, new[] { outpost.Comp.Owner });
    }

    /// <summary>
    /// Writes the allow list into <see cref="WFOutpostComponent.Access"/> by account and character, for a save: each
    /// entry whose holder has been seen, and the saved grants not yet keyed to a card this round.
    /// </summary>
    private void CaptureAccess(Entity<WFOutpostComponent> outpost)
    {
        var grants = new List<WFOutpostAccessGrant>(outpost.Comp.PendingAccess);
        if (TryComp<WFShipAccessComponent>(outpost, out var access))
        {
            foreach (var entry in access.AllowList)
            {
                if (!outpost.Comp.Holders.TryGetValue(entry.Key, out var holder) && !TryFindHolder(entry.Key, out holder))
                    continue;

                outpost.Comp.Holders[entry.Key] = holder;
                grants.Add(new WFOutpostAccessGrant
                {
                    User = holder.User.ToString(),
                    Character = holder.Character,
                    Name = entry.Name,
                    Label = entry.Label,
                    Builder = entry.Builder,
                });
            }
        }

        outpost.Comp.Access = grants;
    }

    /// <summary>
    /// The player and character whose card carries a record key, among the bodies minds own; a mind keeps its account
    /// while its player is away, so a disconnected holder is still found.
    /// </summary>
    private bool TryFindHolder(WFShipAccessKey key, out (NetUserId User, string Character) holder)
    {
        var minds = EntityQueryEnumerator<MindComponent>();
        while (minds.MoveNext(out var mind))
        {
            if (mind.UserId is not { } user || mind.OwnedEntity is not { } body
                || !TryGetCardKey(body, out _, out var carried) || carried != key)
                continue;

            holder = (user, CharacterOf(mind, body));
            return true;
        }

        holder = default;
        return false;
    }

    /// <summary>The card a body wears, else the one it would swipe, and the record key on it.</summary>
    private bool TryGetCardKey(EntityUid body, out EntityUid card, out WFShipAccessKey key)
    {
        key = default;
        return (_access.TryGetWornCard(body, out card) || _access.TryGetCard(body, out card))
               && _access.TryGetKey(card, out key);
    }

    private string CharacterOf(MindComponent mind, EntityUid body)
    {
        return string.IsNullOrEmpty(mind.CharacterName) ? Name(body) : mind.CharacterName;
    }

    /// <summary>Every couple of seconds: notes who holds each listed card, and lists saved grants whose character is playing.</summary>
    private void UpdateAccess()
    {
        var now = _timing.CurTime;
        if (now < _nextAccessCheck)
            return;

        _nextAccessCheck = now + AccessCheckInterval;
        var query = EntityQueryEnumerator<WFOutpostComponent, WFShipAccessComponent>();
        while (query.MoveNext(out var uid, out var outpost, out var access))
        {
            foreach (var entry in access.AllowList)
            {
                if (!outpost.Holders.ContainsKey(entry.Key) && TryFindHolder(entry.Key, out var holder))
                    outpost.Holders[entry.Key] = holder;
            }

            if (outpost.PendingAccess.Count > 0)
                Rekey((uid, outpost), (uid, access));
        }
    }

    /// <summary>Lists the card of each saved grant's character that is in a body now; the rest wait for them.</summary>
    private void Rekey(Entity<WFOutpostComponent> outpost, Entity<WFShipAccessComponent> ship)
    {
        var minds = EntityQueryEnumerator<MindComponent>();
        while (minds.MoveNext(out var mind) && outpost.Comp.PendingAccess.Count > 0)
        {
            if (mind.UserId is not { } user || mind.OwnedEntity is not { } body)
                continue;

            var character = CharacterOf(mind, body);
            var index = outpost.Comp.PendingAccess.FindIndex(g => g.User == user.ToString() && g.Character == character);
            if (index < 0 || !TryGetCardKey(body, out var card, out var key))
                continue;

            var grant = outpost.Comp.PendingAccess[index];
            _shipAccess.TryAddCard(ship, card, grant.Name, grant.Label);
            _shipAccess.SetBuilder(ship, key, grant.Builder);
            outpost.Comp.Holders[key] = (user, character);
            outpost.Comp.PendingAccess.RemoveAt(index);
        }
    }
}
