using System.Linq;
using System.Text;
using Content.Server._WG.NpcCrew.Components;
using Content.Server.Speech;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>
/// Taking orders. A player near the bar says what they want ("a gin and tonic, please"): every drink on the menu has
/// Fluent keywords, matched as whole words, longest first, so one sentence can order several. A short message naming
/// a drink is an order; a longer one needs a request ("can I get", "please") or the bartender named. Asking for the
/// menu lists it; a request he can't place gets a polite no and the menu. The same orders are on his verb menu.
/// </summary>
public sealed partial class WGCrewBartenderSystem
{
    /// <summary>Each keyword list, split and normalised, by its Fluent id.</summary>
    private readonly Dictionary<string, List<string>> _phrases = new();

    private void InitializeOrders()
    {
        SubscribeLocalEvent<WGCrewBartenderComponent, ListenEvent>(OnListen);
        SubscribeLocalEvent<WGCrewBartenderComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnListen(EntityUid uid, WGCrewBartenderComponent comp, ListenEvent args)
    {
        Hear((uid, comp), args.Source, args.Message);
    }

    /// <summary>Answers something a player said within earshot: an order, a request for the menu, or nothing.</summary>
    public void Hear(Entity<WGCrewBartenderComponent> ent, EntityUid speaker, string message)
    {
        if (speaker == ent.Owner || !HasComp<ActorComponent>(speaker) || !_mobs.IsAlive(speaker) || !CanWork(ent)
            || _weapons.HasLiveThreat(ent) || !_examine.InRangeUnOccluded(ent.Owner, speaker, ent.Comp.ServeRange))
            return;

        var text = Normalize(message);
        var drinks = MatchDrinks(ent.Comp, text);
        var request = HasPhrase(text, "wg-bartender-order-cues");
        var addressed = HasPhrase(text, "wg-bartender-address-cues");
        var casual = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= ent.Comp.CasualWords;
        var atCounter = AtCounter(ent, speaker);
        var menu = HasPhrase(text, "wg-bartender-menu-cues");
        // "Is beer on the menu?" asks about the menu; "can I get a beer?" orders one.
        if (menu && !request && (addressed || casual || atCounter))
        {
            Reply(ent, () => SayMenu(ent));
            return;
        }

        if (drinks.Count > 0 && (request || addressed || casual))
        {
            foreach (var drink in drinks)
                TryOrder(ent, speaker, drink);
            return;
        }

        if (menu && (addressed || request || casual || atCounter))
        {
            Reply(ent, () => SayMenu(ent));
            return;
        }

        if (addressed || request && atCounter)
            Reply(ent, () => Say(ent, "wg-bartender-unknown", ("menu", MenuList(ent.Comp))));
    }

    /// <summary>Whether a speaker stands at his counter: near him, or near his post.</summary>
    private bool AtCounter(Entity<WGCrewBartenderComponent> ent, EntityUid speaker)
    {
        var at = Transform(speaker).Coordinates;
        var range = ent.Comp.CounterRange;
        return _transform.InRange(at, Transform(ent.Owner).Coordinates, range) || _transform.InRange(at, Post(ent.Owner), range);
    }

    /// <summary>Says a reply that isn't an order, at most once every <see cref="WGCrewBartenderComponent.ReplyGap"/>.</summary>
    private void Reply(Entity<WGCrewBartenderComponent> ent, Action say)
    {
        var now = _timing.CurTime;
        if (now < ent.Comp.NextReply)
            return;

        ent.Comp.NextReply = now + ent.Comp.ReplyGap;
        say();
    }

    /// <summary>
    /// Takes an order, or says why not: the ship refuses the customer, the queue is full, the drink is off the menu
    /// or out, or there is no clean glass for it. True when it was queued.
    /// </summary>
    public bool TryOrder(Entity<WGCrewBartenderComponent> ent, EntityUid customer, ProtoId<WGCrewDrinkPrototype> drinkId)
    {
        var comp = ent.Comp;
        if (!CanWork(ent) || !comp.Menu.Contains(drinkId) || !_prototypes.TryIndex(drinkId, out var drink))
            return false;

        var name = Name(customer);
        if (Refused(ent, customer))
        {
            Reply(ent, () => Say(ent, "wg-bartender-refuse", ("name", name)));
            return false;
        }

        var current = comp.Current != null ? 1 : 0;
        if (comp.Queue.Count + current >= comp.MaxQueue)
        {
            Say(ent, "wg-bartender-busy", ("name", name));
            return false;
        }

        var mine = comp.Queue.Count(order => order.Customer == customer) + (comp.Current?.Customer == customer ? 1 : 0);
        if (mine >= comp.MaxPerCustomer)
        {
            Say(ent, "wg-bartender-one-at-a-time", ("name", name));
            return false;
        }

        if (!CanPour(ent, drink))
        {
            Say(ent, "wg-bartender-out", ("drink", DrinkName(drink)), ("name", name));
            return false;
        }

        // Every order not yet poured needs a glass off the rack.
        var needGlasses = comp.Queue.Count + (comp.Current != null && comp.Glass == null ? 1 : 0) + 1;
        if (comp.Glasses < needGlasses)
        {
            Say(ent, "wg-bartender-no-glass", ("name", name));
            return false;
        }

        var ahead = comp.Queue.Count + current;
        comp.Queue.Add(new WGCrewDrinkOrder { Customer = customer, Drink = drinkId, Placed = _timing.CurTime });
        if (ahead == 0)
            SayVariant(ent, "wg-bartender-ack", ("drink", DrinkName(drink)), ("name", name));
        else
            Say(ent, "wg-bartender-ack-queued", ("drink", DrinkName(drink)), ("name", name), ("ahead", ahead));
        return true;
    }

    /// <summary>
    /// Whether he won't serve this customer: someone who struck him, or whoever his ship turns away (the same check
    /// its medics make, <see cref="WGCrewMayTreatEvent"/>: a commanded ship refuses the barred and the crew's attackers).
    /// </summary>
    public bool Refused(Entity<WGCrewBartenderComponent> ent, EntityUid customer)
    {
        if (TryComp<WGCrewComponent>(ent, out var crew) && crew.Struck.ContainsKey(customer))
            return true;

        if (_crew.HomeGrid(ent.Owner) is not { } home)
            return false;

        var ev = new WGCrewMayTreatEvent(ent.Owner, customer);
        RaiseLocalEvent(home, ref ev);
        return ev.Refused;
    }

    private void SayMenu(Entity<WGCrewBartenderComponent> ent)
    {
        Say(ent, "wg-bartender-menu", ("menu", MenuList(ent.Comp)));
    }

    /// <summary>The menu as one spoken list.</summary>
    private string MenuList(WGCrewBartenderComponent comp)
    {
        var names = new List<string>();
        foreach (var id in comp.Menu)
        {
            if (_prototypes.TryIndex(id, out var drink))
                names.Add(DrinkName(drink));
        }

        return string.Join(Loc.GetString("wg-bartender-menu-separator"), names);
    }

    private void OnGetVerbs(Entity<WGCrewBartenderComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !HasComp<ActorComponent>(args.User) || !CanWork(ent))
            return;

        var user = args.User;
        var category = new VerbCategory("wg-bartender-verb-order", null);
        var priority = ent.Comp.Menu.Count;
        foreach (var id in ent.Comp.Menu)
        {
            if (!_prototypes.TryIndex(id, out var drink))
                continue;

            var drinkId = id;
            args.Verbs.Add(new Verb
            {
                Text = Capitalize(DrinkName(drink)),
                Category = category,
                Priority = priority--,
                Act = () => TryOrder(ent, user, drinkId),
            });
        }

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("wg-bartender-verb-menu"),
            Category = category,
            Priority = priority,
            Act = () => Reply(ent, () => SayMenu(ent)),
        });
    }

    private static string Capitalize(string text)
    {
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }

    #region Parsing

    /// <summary>
    /// Lower case, letters, digits, apostrophes and ampersands kept, everything else a single space, padded with a
    /// space each side so phrases match as whole words.
    /// </summary>
    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length + 2);
        builder.Append(' ');
        var space = true;
        foreach (var raw in text.ToLowerInvariant())
        {
            var c = raw is '’' or '‘' ? '\'' : raw;
            if (char.IsLetterOrDigit(c) || c is '\'' or '&')
            {
                builder.Append(c);
                space = false;
            }
            else if (!space)
            {
                builder.Append(' ');
                space = true;
            }
        }

        if (!space)
            builder.Append(' ');
        return builder.ToString();
    }

    /// <summary>A comma-separated Fluent list, each entry normalised and trimmed.</summary>
    private List<string> Phrases(string locId)
    {
        if (_phrases.TryGetValue(locId, out var cached))
            return cached;

        var phrases = new List<string>();
        foreach (var entry in Loc.GetString(locId).Split(','))
        {
            var phrase = Normalize(entry).Trim();
            if (phrase.Length > 0)
                phrases.Add(phrase);
        }

        _phrases[locId] = phrases;
        return phrases;
    }

    private bool HasPhrase(string text, string locId)
    {
        return Phrases(locId).Any(phrase => text.Contains($" {phrase} "));
    }

    /// <summary>
    /// The menu's drinks a message names, in the order it names them. The longest keyword wins where two overlap, so
    /// "whiskey cola" is a whiskey cola and not a whiskey.
    /// </summary>
    public List<ProtoId<WGCrewDrinkPrototype>> MatchDrinks(WGCrewBartenderComponent comp, string text)
    {
        var matches = new List<(int Start, int End, ProtoId<WGCrewDrinkPrototype> Drink)>();
        foreach (var id in comp.Menu)
        {
            if (!_prototypes.TryIndex(id, out var drink))
                continue;

            foreach (var phrase in Phrases(drink.Keywords))
            {
                var needle = $" {phrase} ";
                var at = text.IndexOf(needle, StringComparison.Ordinal);
                while (at >= 0)
                {
                    matches.Add((at, at + needle.Length, id));
                    at = text.IndexOf(needle, at + 1, StringComparison.Ordinal);
                }
            }
        }

        var taken = new List<(int Start, int End, ProtoId<WGCrewDrinkPrototype> Drink)>();
        foreach (var match in matches.OrderByDescending(m => m.End - m.Start))
        {
            // Spans share their padding spaces, so touching ones don't overlap.
            if (taken.Any(t => match.Start < t.End - 1 && t.Start < match.End - 1))
                continue;
            taken.Add(match);
        }

        var drinks = new List<ProtoId<WGCrewDrinkPrototype>>();
        foreach (var match in taken.OrderBy(m => m.Start))
        {
            if (!drinks.Contains(match.Drink))
                drinks.Add(match.Drink);
        }

        return drinks;
    }

    #endregion
}
