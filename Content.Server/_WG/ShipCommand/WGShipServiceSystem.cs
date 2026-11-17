using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Cargo.Systems;
using Content.Server._WG.NpcCrew;
using Content.Shared._Mono.Company;
using Content.Shared._WG.Drydock;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Database;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._WG.ShipCommand;

/// <summary>
/// Prices and logs the services aboard a commanded ship. A service calls <see cref="Quote"/> for what a customer pays
/// and <see cref="Record"/> once they have paid; both do nothing for a service on any other grid, so the hooks are
/// safe everywhere. The service's own grid decides, not the customer's, since a hail can reach a service from afar.
/// </summary>
public sealed class WGShipServiceSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private WGShipCommandSystem _command = default!;

    /// <summary>How many uses a ship's round ledger keeps; older ones remain in the admin log.</summary>
    public const int LedgerSize = 500;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGCrewTreatedEvent>(OnCrewTreated);
        SubscribeLocalEvent<WGBargainComponent, StackSplitEvent>(OnBargainSplit);
        SubscribeLocalEvent<WGBargainComponent, WGStackMergeEvent>(OnBargainMerge);
        SubscribeLocalEvent<WGBargainComponent, WGFlatpackUnpackedEvent>(OnBargainUnpacked);
    }

    /// <summary>A treatment by the ship's NPC medics, free for now, goes in the service log like any other service use.</summary>
    private void OnCrewTreated(ref WGCrewTreatedEvent args)
    {
        Record(args.Medic, args.Patient, WGShipServiceKind.Medical, args.Action.ToString(), 0, 0, null);
    }

    /// <summary>
    /// Whether a service aboard a commanded ship turns this customer away, with the words to show them. The barred are
    /// refused everywhere aboard; nobody is refused off a commanded ship.
    /// </summary>
    public bool Refuses(EntityUid service, EntityUid customer, [NotNullWhen(true)] out string? reason)
    {
        reason = null;
        if (Transform(service).GridUid is not { } grid || !HasComp<WGShipCommandComponent>(grid)
            || _command.GetAuthority(customer, grid) != WGShipAuthority.Barred)
            return false;

        reason = Loc.GetString("wg-ship-service-refused", ("ship", Name(grid)));
        return true;
    }

    /// <summary>The price a customer pays for a service; the base price off a commanded ship.</summary>
    public int Quote(EntityUid service, EntityUid customer, WGShipServiceKind kind, int basePrice)
    {
        return Quote(service, customer, kind, basePrice, out _);
    }

    /// <summary>
    /// The price a customer pays for a service, and the name of the rule that set it (null at the usual price). The
    /// owner, and admins, are served free while the ship's switches say so; otherwise the lowest multiplier among
    /// the enabled rules that cover the customer and the service applies. The barred always pay the base price.
    /// </summary>
    public int Quote(EntityUid service, EntityUid customer, WGShipServiceKind kind, int basePrice, out string? rule)
    {
        rule = null;
        if (basePrice <= 0 || Transform(service).GridUid is not { } grid || !TryComp<WGShipCommandComponent>(grid, out var command))
            return basePrice;

        var authority = _command.GetAuthority(customer, grid);
        if (authority == WGShipAuthority.Barred)
            return basePrice;

        if (authority == WGShipAuthority.Owner && command.OwnerServedFree)
        {
            rule = "owner";
            return 0;
        }

        if (authority == WGShipAuthority.Admin && command.AdminsServedFree)
        {
            rule = "admins";
            return 0;
        }

        float? best = null;
        foreach (var candidate in command.PriceRules)
        {
            if (!candidate.Enabled || (candidate.Services & kind) == 0 || !Covers(candidate, customer, authority, grid)
                || best is { } current && candidate.Multiplier >= current)
                continue;

            best = candidate.Multiplier;
            rule = candidate.Name;
        }

        return best is { } multiplier ? Math.Max(0, (int) MathF.Round(basePrice * Math.Max(0f, multiplier))) : basePrice;
    }

    /// <summary>Marks a ship bought below its list price: the discount comes off what it is sold for.</summary>
    public void MarkBargain(EntityUid bought, int listPrice, int paid)
    {
        if (listPrice > 0 && paid < listPrice)
            EnsureComp<WGBargainComponent>(bought).Discount = listPrice - Math.Max(0, paid);
    }

    /// <summary>
    /// Marks goods or a vehicle bought below its list price, so none of it resells for more than was paid: the discount,
    /// or what it appraises at over the price paid if that is more, is spread over it and everything inside it by each
    /// one's own price, which <see cref="PricingSystem"/> then takes it off.
    /// </summary>
    public void MarkBargainGoods(EntityUid bought, double listPrice, double paid)
    {
        if (paid >= listPrice)
            return;

        var parts = new List<(EntityUid Part, double Price)>();
        CollectParts(bought, parts);
        var value = parts.Sum(part => part.Price);
        var discount = Math.Max(listPrice, value) - Math.Max(0, paid);
        if (discount <= 0)
            return;

        if (value <= 0)
        {
            EnsureComp<WGBargainComponent>(bought).Discount += discount;
            return;
        }

        foreach (var (part, price) in parts)
        {
            if (price > 0)
                EnsureComp<WGBargainComponent>(part).Discount += discount * price / value;
        }
    }

    /// <summary>
    /// Carries a bargain over to what something turns into, such as a flatpack unpacked or a beacon opened, so the new
    /// thing resells for no more than the old one would have.
    /// </summary>
    public void CarryBargain(EntityUid from, EntityUid to)
    {
        if (!HasComp<WGBargainComponent>(from))
            return;

        MarkBargainGoods(to, _pricing.GetUndiscountedPrice(from), _pricing.GetPrice(from));
    }

    /// <summary>What a ship sells for: its appraisal less the discount it was bought at, never below zero.</summary>
    public int ResaleValue(EntityUid sold, int value)
    {
        return TryComp<WGBargainComponent>(sold, out var bargain)
            ? (int) Math.Max(0, Math.Floor(value - bargain.Discount))
            : value;
    }

    /// <summary>An entity and everything in its containers, each with its own price as if bought at the list price.</summary>
    private void CollectParts(EntityUid uid, List<(EntityUid Part, double Price)> parts)
    {
        parts.Add((uid, _pricing.GetUndiscountedPrice(uid, includeContents: false)));
        if (!TryComp<ContainerManagerComponent>(uid, out var containers))
            return;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var contained in container.ContainedEntities)
                CollectParts(contained, parts);
        }
    }

    /// <summary>A split-off stack takes its share of the bargain, by count.</summary>
    private void OnBargainSplit(Entity<WGBargainComponent> ent, ref StackSplitEvent args)
    {
        if (!TryComp<StackComponent>(ent, out var kept) || !TryComp<StackComponent>(args.NewId, out var split))
            return;

        var total = kept.Count + split.Count;
        if (total <= 0)
            return;

        var moved = ent.Comp.Discount * split.Count / total;
        ent.Comp.Discount -= moved;
        EnsureComp<WGBargainComponent>(args.NewId).Discount += moved;
    }

    /// <summary>Items merged into another stack take their share of the bargain with them, by count.</summary>
    private void OnBargainMerge(Entity<WGBargainComponent> ent, ref WGStackMergeEvent args)
    {
        if (args.Moved <= 0 || args.Count <= 0)
            return;

        var moved = ent.Comp.Discount * args.Moved / args.Count;
        ent.Comp.Discount -= moved;
        EnsureComp<WGBargainComponent>(args.Recipient).Discount += moved;
    }

    /// <summary>A flatpack bought at a bargain unpacks into a machine that resells for no more.</summary>
    private void OnBargainUnpacked(Entity<WGBargainComponent> ent, ref WGFlatpackUnpackedEvent args)
    {
        CarryBargain(ent, args.Spawned);
    }

    private bool Covers(WGShipPriceRule rule, EntityUid customer, WGShipAuthority authority, EntityUid grid)
    {
        return rule.Audience switch
        {
            WGShipPriceAudience.Everyone => true,
            WGShipPriceAudience.Staff => authority >= WGShipAuthority.Staff,
            WGShipPriceAudience.Officers => authority >= WGShipAuthority.Officer,
            WGShipPriceAudience.Person => rule.Person is { } person && _command.IsPerson(customer, person),
            WGShipPriceAudience.Company => rule.Company is { Length: > 0 } company
                                           && TryComp<CompanyComponent>(customer, out var member)
                                           && member.CompanyName.Id == company,
            _ => false,
        };
    }

    /// <summary>
    /// Logs a paid (or free) use of a service aboard a commanded ship: one admin log line, linked to the customer,
    /// and one entry in the ship's round ledger. Nothing for a service elsewhere.
    /// </summary>
    public void Record(EntityUid service, EntityUid customer, WGShipServiceKind kind, string what, int basePrice, int charged, string? rule)
    {
        if (Transform(service).GridUid is not { } grid || !HasComp<WGShipCommandComponent>(grid))
            return;

        var ruleName = rule ?? "none";
        _adminLog.Add(LogType.WGShipService, rule == null ? LogImpact.Low : LogImpact.Medium,
            $"{ToPrettyString(customer):player} used {kind} {ToPrettyString(service):service} on {ToPrettyString(grid):grid}: {what}, charged {charged:charged} of {basePrice:base}, rule {ruleName:rule}");

        var ledger = EnsureComp<WGShipServiceLedgerComponent>(grid);
        ledger.Entries.Add(new WGShipServiceEntry(_timing.CurTime, Name(customer), kind, Name(service), what, basePrice, charged, rule));
        if (ledger.Entries.Count > LedgerSize)
            ledger.Entries.RemoveRange(0, ledger.Entries.Count - LedgerSize);
    }

    /// <summary>Adds a price rule, or replaces the one with the same name. Admin-logged.</summary>
    public void SetRule(Entity<WGShipCommandComponent> grid, WGShipPriceRule rule, EntityUid? actor)
    {
        grid.Comp.PriceRules.RemoveAll(r => r.Name == rule.Name);
        grid.Comp.PriceRules.Add(rule);
        Dirty(grid);
        LogRules(grid, actor, $"set rule {rule.Name}: {rule.Audience} {rule.Company} on {rule.Services} at x{rule.Multiplier}, enabled {rule.Enabled}");
    }

    /// <summary>
    /// Switches a price rule on or off by name; "owner" and "admins" switch their free service. False when there is
    /// no such rule.
    /// </summary>
    public bool ToggleRule(Entity<WGShipCommandComponent> grid, string name, EntityUid? actor, out bool enabled)
    {
        var comp = grid.Comp;
        switch (name)
        {
            case "owner":
                enabled = comp.OwnerServedFree = !comp.OwnerServedFree;
                break;
            case "admins":
                enabled = comp.AdminsServedFree = !comp.AdminsServedFree;
                break;
            default:
                var rule = comp.PriceRules.Find(r => r.Name == name);
                if (rule == null)
                {
                    enabled = false;
                    return false;
                }

                enabled = rule.Enabled = !rule.Enabled;
                break;
        }

        Dirty(grid);
        LogRules(grid, actor, $"switched rule {name} {(enabled ? "on" : "off")}");
        return true;
    }

    /// <summary>Removes a price rule by name. False when there is none.</summary>
    public bool RemoveRule(Entity<WGShipCommandComponent> grid, string name, EntityUid? actor)
    {
        if (grid.Comp.PriceRules.RemoveAll(r => r.Name == name) == 0)
            return false;

        Dirty(grid);
        LogRules(grid, actor, $"removed rule {name}");
        return true;
    }

    private void LogRules(EntityUid grid, EntityUid? actor, string change)
    {
        if (actor is { } user)
            _adminLog.Add(LogType.WGShipServiceRules, LogImpact.Medium, $"{ToPrettyString(user):actor} on {ToPrettyString(grid):grid}: {change}");
        else
            _adminLog.Add(LogType.WGShipServiceRules, LogImpact.Medium, $"{ToPrettyString(grid):grid}: {change}");
    }
}
