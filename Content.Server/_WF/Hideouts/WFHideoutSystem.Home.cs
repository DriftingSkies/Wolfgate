using System.Linq;
using System.Text.RegularExpressions;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Planets;
using Content.Shared._WF.Hideouts;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.Planets;
using Content.Shared.Database;

namespace Content.Server._WF.Hideouts;

/// <summary>
/// The home world: an outpost keeps the world it was founded on and lands there every round, for a small upkeep in
/// place of the load markup once a module stands. The owner moves it to another world for a fee.
/// </summary>
public sealed partial class WFHideoutSystem
{
    private static readonly Regex HomeWorldField = new(@"homeWorld: (\S+)", RegexOptions.Compiled);

    private void InitializeHome()
    {
        SubscribeLocalEvent<WFHideoutComponent, WFOutpostMarkupEvent>(OnMarkup);
        SubscribeLocalEvent<WFOutpostDropWorldEvent>(OnDropWorld);
    }

    public WFHideoutRulesPrototype HideoutRules => _proto.Index<WFHideoutRulesPrototype>(WFHideoutRulesPrototype.Default);

    /// <summary>The surface of the world a ground layer belongs to, if it is a planet's.</summary>
    public string? SurfaceOf(EntityUid ground)
    {
        return TryComp<WFPlanetLayerComponent>(ground, out var layer) && TryGetEntity(layer.Network, out var network)
               && TryComp<WFPlanetNetworkComponent>(network, out var planet)
            ? planet.Surface.Id
            : null;
    }

    /// <summary>Sets an outpost's home world to the one it stands on, if it has none yet.</summary>
    private void EnsureHome(Entity<WFHideoutComponent, WFOutpostComponent> outpost)
    {
        if (outpost.Comp1.HomeWorld == null && SurfaceOf(outpost.Comp2.Ground) is { } surface)
            outpost.Comp1.HomeWorld = surface;
    }

    /// <summary>The worlds an outpost may call home: every planet surface that takes outposts.</summary>
    public List<WFPlanetSurfacePrototype> HomeWorlds()
    {
        return _proto.EnumeratePrototypes<WFPlanetSurfacePrototype>().Where(s => s.Outposts && s.Pois != null)
            .OrderBy(s => s.ID).ToList();
    }

    /// <summary>A home's name for the console: its planet type's name, or its id.</summary>
    public string WorldName(WFPlanetSurfacePrototype surface)
    {
        return _proto.TryIndex(surface.PlanetType, out var type) && !string.IsNullOrEmpty(type.Name) ? Loc.GetString(type.Name) : surface.ID;
    }

    private void OnMarkup(Entity<WFHideoutComponent> outpost, ref WFOutpostMarkupEvent args)
    {
        // A home with a module built lands for its upkeep, not the outpost load markup.
        if (outpost.Comp.Levels.Count > 0)
            args.Markup = Math.Min(args.Markup, HideoutRules.HomeMarkup);
    }

    private void OnDropWorld(ref WFOutpostDropWorldEvent args)
    {
        var match = HomeWorldField.Match(args.Yaml);
        if (!match.Success)
            return;

        var home = match.Groups[1].Value;
        var networks = EntityQueryEnumerator<WFPlanetNetworkComponent>();
        while (networks.MoveNext(out _, out var network))
        {
            if (network.Surface.Id == home && !TerminatingOrDeleted(network.GroundMap))
            {
                args.Ground = network.GroundMap;
                return;
            }
        }
    }

    /// <summary>
    /// Moves an outpost's home world for the fee; it lands there from its next landing. <paramref name="reason"/> is a loc
    /// id when it fails.
    /// </summary>
    public bool TryMoveHome(EntityUid user, Entity<WFOutpostComponent> outpost, string world, out string reason)
    {
        if (!_outposts.IsOwnerOf(user, outpost.Comp) || !TryComp<WFHideoutComponent>(outpost, out var hideout))
        {
            reason = "wf-hideout-build-not-owner";
            return false;
        }

        if (HomeWorlds().All(s => s.ID != world))
        {
            reason = "wf-hideout-move-unknown";
            return false;
        }

        if (hideout.HomeWorld == world)
        {
            reason = "wf-hideout-move-same";
            return false;
        }

        var fee = HideoutRules.MoveFee;
        if (fee > 0 && !_bank.TryBankWithdraw(user, fee))
        {
            reason = "wf-hideout-cannot-afford";
            return false;
        }

        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"{ToPrettyString(user):user} moved outpost {ToPrettyString(outpost):grid}'s home from {hideout.HomeWorld} to {world} for {fee}");
        hideout.HomeWorld = world;
        _popup.PopupEntity(Loc.GetString("wf-hideout-moved"), user, user);
        reason = string.Empty;
        return true;
    }

    private void OnMoveHomeMessage(Entity<WFOutpostConsoleComponent> console, ref WFHideoutMoveHomeMessage args)
    {
        if (!TryOutpost(console, out var outpost))
            return;

        if (!TryMoveHome(args.Actor, outpost, args.World, out var reason))
            _popup.PopupEntity(Loc.GetString(reason), args.Actor, args.Actor);
        _outposts.RefreshConsole(outpost);
    }
}
