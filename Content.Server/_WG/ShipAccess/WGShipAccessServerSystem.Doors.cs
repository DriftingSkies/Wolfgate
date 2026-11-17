using Content.Server.Shuttles.Components;
using Content.Shared._WG.ShipAccess;
using Content.Shared.Database;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Power;

namespace Content.Server._WG.ShipAccess;

public sealed partial class WGShipAccessServerSystem
{
    [Dependency] private SharedDoorSystem _door = default!;

    /// <summary>Per-door rules: set from the console, sealed doors bolted, door lists trimmed with the allow list.</summary>
    private void InitializeDoors()
    {
        SubscribeLocalEvent<WGDoorAccessRuleComponent, MapInitEvent>(OnRuleMapInit);
        SubscribeLocalEvent<WGDoorAccessRuleComponent, PowerChangedEvent>(OnRulePowerChanged);
        SubscribeLocalEvent<WGDoorAccessRuleComponent, DoorStateChangedEvent>(OnRuleDoorStateChanged);
        SubscribeLocalEvent<WGDoorAccessRuleComponent, DoorBoltsChangedEvent>(OnRuleBoltsChanged);
    }

    /// <summary>
    /// How long a door sealed while open stays open before it is closed: the airlock's own auto-close pace, so
    /// the close never lands on someone stepping through, and the door's usual one-second retries follow.
    /// </summary>
    private static readonly TimeSpan SealCloseDelay = TimeSpan.FromSeconds(2);

    /// <summary>A loaded door re-asserts its reader and bolts, since neither follows from the rule on its own.</summary>
    private void OnRuleMapInit(Entity<WGDoorAccessRuleComponent> door, ref MapInitEvent args)
    {
        QueueReader(door);
        if (door.Comp.Rule == WGDoorAccessRule.Sealed)
            Seal(door);
    }

    /// <summary>Bolts need power, so a sealed door bolts, and an unsealed one unbolts, as soon as it is powered again.</summary>
    private void OnRulePowerChanged(Entity<WGDoorAccessRuleComponent> door, ref PowerChangedEvent args)
    {
        if (!args.Powered)
            return;

        if (door.Comp.Rule == WGDoorAccessRule.Sealed)
            Seal(door);
        else if (door.Comp.UnboltWhenPowered)
            Unseal(door);
    }

    /// <summary>
    /// A door sealed while not shut is bolted once it shuts: bolts dropped mid-close cancel the close and leave it
    /// bolted open. Until then it is scheduled to close whenever it comes to rest open. Only the seal itself waits
    /// like this; afterwards the door's bolts can be hacked, emagged and remoted like any airlock's.
    /// </summary>
    private void OnRuleDoorStateChanged(Entity<WGDoorAccessRuleComponent> door, ref DoorStateChangedEvent args)
    {
        if (!door.Comp.SealPending || door.Comp.Rule != WGDoorAccessRule.Sealed)
            return;

        if (args.State == DoorState.Closed)
            Seal(door);
        else if (args.State == DoorState.Open)
            _door.SetNextStateChange(door, SealCloseDelay);
    }

    /// <summary>
    /// Undocking and FTL arrivals lift a dock airlock's bolts on their own, and the next dock would open it, so a
    /// sealed dock airlock is bolted again. Other doors keep whatever a wire or remote does to their bolts.
    /// </summary>
    private void OnRuleBoltsChanged(Entity<WGDoorAccessRuleComponent> door, ref DoorBoltsChangedEvent args)
    {
        if (!args.BoltsDown && door.Comp.Rule == WGDoorAccessRule.Sealed && HasComp<DockingComponent>(door))
            Seal(door);
    }

    /// <summary>Sets a door's rule. Sealing bolts and closes it; leaving Sealed unbolts it. False when nothing changed.</summary>
    public bool SetDoorRule(Entity<WGShipAccessComponent> ship, EntityUid door, WGDoorAccessRule rule)
    {
        if (!ApplyDoorRule(door, rule, out var old))
            return false;

        RefreshReader(door);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"Door {ToPrettyString(door):door} on {ToPrettyString(ship.Owner):grid} was set to rule {rule} (was {old})");
        return true;
    }

    /// <summary>
    /// Sets every door on the ship to one rule, as the console's set-all control does, and returns how many changed.
    /// A zone's doors are left to the zone.
    /// </summary>
    public int SetAllDoorRules(Entity<WGShipAccessComponent> ship, WGDoorAccessRule rule)
    {
        var changed = 0;
        var children = Transform(ship.Owner).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (IsRuledDoor(child) && !InZone(child) && ApplyDoorRule(child, rule, out _))
                changed++;
        }

        if (changed == 0)
            return 0;

        RefreshShip(ship);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"Every door on {ToPrettyString(ship.Owner):grid} was set to rule {rule} ({changed} changed)");
        return changed;
    }

    /// <summary>
    /// Gives a zone's doors the rule its mode calls for and rewrites their readers, also where the rule stays the
    /// same, since who the zone admits may have changed. The zone's owner logs the switch. Returns how many rules changed.
    /// </summary>
    public int ApplyZoneRule(IEnumerable<EntityUid> doors, WGDoorAccessRule rule)
    {
        var changed = 0;
        foreach (var door in doors)
        {
            if (ApplyDoorRule(door, rule, out _))
                changed++;

            RefreshReader(door);
        }

        return changed;
    }

    /// <summary>Whether a door belongs to a zone, whose switch sets its rule.</summary>
    public bool InZone(EntityUid door)
    {
        return TryComp<WGDoorAccessRuleComponent>(door, out var rule) && rule.Zone != null;
    }

    /// <summary>Writes a door's rule and its bolts, leaving the reader to the caller. False when nothing changed.</summary>
    private bool ApplyDoorRule(EntityUid door, WGDoorAccessRule rule, out WGDoorAccessRule old)
    {
        old = WGDoorAccessRule.Default;
        if (!Enum.IsDefined(rule))
            return false;
        if (!TryComp<WGDoorAccessRuleComponent>(door, out var comp))
        {
            // A door without the component already follows the ship.
            if (rule == WGDoorAccessRule.Default)
                return false;

            comp = AddComp<WGDoorAccessRuleComponent>(door);
        }

        old = comp.Rule;
        if (old == rule)
            return false;

        comp.Rule = rule;
        Dirty(door, comp);

        if (rule == WGDoorAccessRule.Sealed)
            Seal((door, comp));
        else if (old == WGDoorAccessRule.Sealed)
            Unseal((door, comp));

        return true;
    }

    /// <summary>Adds or removes an allow-listed card's key on a door's own list; false when it is not on the ship's list or nothing changed.</summary>
    public bool SetDoorPlayer(Entity<WGShipAccessComponent> ship, EntityUid door, WGShipAccessKey key, bool listed)
    {
        if (!_access.TryGetEntry(ship.Comp, key, out var entry) && listed)
            return false;

        var comp = EnsureComp<WGDoorAccessRuleComponent>(door);
        if (listed == comp.Players.Contains(key))
            return false;

        if (listed)
            comp.Players.Add(key);
        else
            comp.Players.Remove(key);

        Dirty(door, comp);
        RefreshReader(door);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{entry?.Name ?? "A card"} was {(listed ? "added to" : "removed from")} door {ToPrettyString(door):door} on {ToPrettyString(ship.Owner):grid}");
        return true;
    }

    /// <summary>Takes a key off every door list on the ship, after it left the allow list.</summary>
    private void RemoveDoorPlayer(EntityUid grid, WGShipAccessKey key)
    {
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (TryComp<WGDoorAccessRuleComponent>(child, out var rule) && rule.Players.Remove(key))
                Dirty(child, rule);
        }
    }

    /// <summary>The doors on the ship whose own list holds a key.</summary>
    private List<EntityUid> DoorsListing(EntityUid grid, WGShipAccessKey key)
    {
        var doors = new List<EntityUid>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (TryComp<WGDoorAccessRuleComponent>(child, out var rule) && rule.Players.Contains(key))
                doors.Add(child);
        }

        return doors;
    }

    /// <summary>Empties every door list on the ship, after the allow list was cleared.</summary>
    private void ClearDoorPlayers(EntityUid grid)
    {
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!TryComp<WGDoorAccessRuleComponent>(child, out var rule) || rule.Players.Count == 0)
                continue;

            rule.Players.Clear();
            Dirty(child, rule);
        }
    }

    /// <summary>
    /// Closes and bolts a sealed door, adding bolts to a door that has none. A door that is not shut yet is asked
    /// to close and is bolted when it does; an unpowered one is bolted when power returns. True when bolted now.
    /// </summary>
    private bool Seal(Entity<WGDoorAccessRuleComponent> door)
    {
        door.Comp.UnboltWhenPowered = false;
        if (!TryComp<DoorBoltComponent>(door, out var bolt))
        {
            bolt = AddComp<DoorBoltComponent>(door);
            door.Comp.AddedBolt = true;
        }

        if (TryComp<DoorComponent>(door, out var doorComp) && doorComp.State is not (DoorState.Closed or DoorState.Welded))
        {
            door.Comp.SealPending = true;
            if (doorComp.State == DoorState.Open)
                _door.SetNextStateChange(door, SealCloseDelay, doorComp);

            return false;
        }

        door.Comp.SealPending = false;
        return _door.TrySetBoltDown((door, bolt), true) || bolt.BoltsDown;
    }

    /// <summary>
    /// Unbolts a door that leaves Sealed and takes the bolts away again if sealing added them. Bolts the door
    /// already had need power to come up, so without it the door is flagged to unbolt when power returns.
    /// </summary>
    private void Unseal(Entity<WGDoorAccessRuleComponent> door)
    {
        door.Comp.UnboltWhenPowered = false;
        door.Comp.SealPending = false;
        if (!TryComp<DoorBoltComponent>(door, out var bolt))
            return;

        _door.SetBoltsDown((door, bolt), false);
        if (door.Comp.AddedBolt)
        {
            RemComp<DoorBoltComponent>(door);
            door.Comp.AddedBolt = false;
            return;
        }

        door.Comp.UnboltWhenPowered = _door.IsBolted(door, bolt);
    }

    /// <summary>
    /// Wipes what a seller set before a used ship goes to its next buyer: the access record, the ship code, every
    /// door's code, rule and seal, and the readers' own access comes back. A seal lifted without power keeps a
    /// bare rule until the bolts come up.
    /// </summary>
    public void ClearForResale(EntityUid grid)
    {
        RemComp<WGShipAccessComponent>(grid);
        RemComp<WGShipAccessCodeComponent>(grid);

        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (_accessReader.GetMainAccessReader(child, out var reader))
                RestoreReader(reader.Value);

            RemComp<WGDoorCodeComponent>(child);
            if (!TryComp<WGDoorAccessRuleComponent>(child, out var rule))
                continue;

            // The rule goes first, so nothing still reads the door as sealed while its bolts come up.
            if (rule.Rule == WGDoorAccessRule.Sealed)
            {
                rule.Rule = WGDoorAccessRule.Default;
                Unseal((child, rule));
            }

            if (!rule.UnboltWhenPowered)
            {
                RemComp<WGDoorAccessRuleComponent>(child);
                continue;
            }

            rule.Rule = WGDoorAccessRule.Default;
            rule.Players.Clear();
            rule.HasOwnCode = false;
            Dirty(child, rule);
        }
    }

    private void OnSetDoorRule(Entity<ShuttleConsoleComponent> console, ref WGShipAccessSetDoorRuleMessage args)
    {
        if (!TryGetEditableShip(console, args.Actor, out var ship) || !TryGetShipDoor(console, ship, args.Door, args.Actor, out var door))
            return;

        // A zone's doors follow its switch, and only a zone switch gives a door the Zone rule.
        if (args.Rule == WGDoorAccessRule.Zone || InZone(door))
        {
            Popup(console, args.Actor, "ship-access-door-in-zone");
            return;
        }

        if (args.Rule == WGDoorAccessRule.Crew && !TryLinkCrew(ship, args.Actor))
        {
            Popup(console, args.Actor, "ship-access-no-crew");
            return;
        }
        if (args.Rule == WGDoorAccessRule.Company && !TryLinkCompany(ship, args.Actor))
        {
            Popup(console, args.Actor, "ship-access-no-company");
            return;
        }

        // Sealing an open or unpowered door is deferred, so say so rather than leave the owner guessing.
        if (SetDoorRule(ship, door, args.Rule) && args.Rule == WGDoorAccessRule.Sealed
            && (!_door.IsBolted(door) || Comp<WGDoorAccessRuleComponent>(door).SealPending))
            Popup(console, args.Actor, "ship-access-seal-pending");
    }

    private void OnSetAllDoorRules(Entity<ShuttleConsoleComponent> console, ref WGShipAccessSetAllDoorRulesMessage args)
    {
        if (!TryGetEditableShip(console, args.Actor, out var ship) || args.Rule == WGDoorAccessRule.Zone)
            return;

        if (args.Rule == WGDoorAccessRule.Crew && !TryLinkCrew(ship, args.Actor))
        {
            Popup(console, args.Actor, "ship-access-no-crew");
            return;
        }
        if (args.Rule == WGDoorAccessRule.Company && !TryLinkCompany(ship, args.Actor))
        {
            Popup(console, args.Actor, "ship-access-no-company");
            return;
        }
        var changed = SetAllDoorRules(ship, args.Rule);
        _popup.PopupEntity(Loc.GetString("ship-access-all-doors-set", ("count", changed)), console.Owner, args.Actor);
    }

    private void OnSetDoorPlayer(Entity<ShuttleConsoleComponent> console, ref WGShipAccessSetDoorPlayerMessage args)
    {
        if (TryGetEditableShip(console, args.Actor, out var ship) && TryGetShipDoor(console, ship, args.Door, args.Actor, out var door))
            SetDoorPlayer(ship, door, args.Key, args.Listed);
    }

    /// <summary>Resolves a console message's door and checks it is a door on the console's own grid. Firelocks take no rule.</summary>
    private bool TryGetShipDoor(Entity<ShuttleConsoleComponent> console, Entity<WGShipAccessComponent> ship, NetEntity netDoor, EntityUid actor, out EntityUid door)
    {
        if (TryGetEntity(netDoor, out var uid) && IsRuledDoor(uid.Value) && Transform(uid.Value).GridUid == ship.Owner)
        {
            door = uid.Value;
            return true;
        }

        door = default;
        Popup(console, actor, "ship-access-door-not-on-ship");
        return false;
    }
}
