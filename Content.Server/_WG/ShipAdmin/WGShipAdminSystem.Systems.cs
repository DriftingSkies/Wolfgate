using System.Globalization;
using Content.Server._Crescent.ShipShields;
using Content.Server._Mono.Cleanup;
using Content.Server._Mono.Grid;
using Content.Server._Mono.SpaceArtillery;
using Content.Server._WG.AdminVessels;
using Content.Server._WG.Drydock;
using Content.Server._WG.ShipCommand;
using Content.Server.Construction.Components;
using Content.Shared._Mono;
using Content.Shared._Mono.Grid;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.Drydock;
using Content.Shared._WG.ShipAdmin;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Database;
using Content.Shared.Shuttles.Components;
using Content.Shared.Tiles;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipAdmin;

public sealed partial class WGShipAdminSystem
{
    [Dependency] private GridModifierSystem _gridModifiers = default!;
    [Dependency] private ShipShieldsSystem _shields = default!;
    [Dependency] private WGShipFittingSystem _fitting = default!;
    [Dependency] private WGShipRefitSystem _refits = default!;

    /// <summary>Renames a ship: its grid, its station, its deeds and its shuttle record.</summary>
    public bool Rename(ICommonSession admin, EntityUid grid, string name, out string result)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Length > ShuttleDeedComponent.MaxNameLength)
        {
            result = Loc.GetString("wg-ship-admin-result-bad-name", ("max", ShuttleDeedComponent.MaxNameLength));
            return false;
        }

        var old = Name(grid);
        if (!_vesselSpawn.TryRenameVessel(grid, name, admin.AttachedEntity))
        {
            result = Loc.GetString("wg-ship-admin-result-rename-failed");
            return false;
        }

        Audit(admin, LogImpact.Medium, grid, $"renamed it from '{old}' to '{Name(grid)}'");
        result = Loc.GetString("wg-ship-admin-result-renamed", ("old", old), ("ship", Name(grid)));
        return true;
    }

    /// <summary>Changes the ship's radar colour and whether it or its label shows on radar.</summary>
    public bool SetIff(ICommonSession admin, EntityUid grid, WGShipAdminIffMessage iff, out string result)
    {
        if (iff.Color != null)
        {
            if (!Color.TryFromHex(iff.Color, out var color))
            {
                result = Loc.GetString("wg-ship-admin-result-bad-colour");
                return false;
            }

            _shuttle.SetIFFColor(grid, color);
        }

        if (iff.Hidden is { } hidden)
            SetIffFlag(grid, IFFFlags.Hide, hidden);

        if (iff.LabelHidden is { } labelHidden)
            SetIffFlag(grid, IFFFlags.HideLabel, labelHidden);

        Audit(admin, LogImpact.Medium, grid, $"changed its radar signature (colour {iff.Color}, hidden {iff.Hidden}, label hidden {iff.LabelHidden})");
        result = Loc.GetString("wg-ship-admin-result-iff", ("ship", Name(grid)));
        return true;
    }

    /// <summary>Raises or drops the ship's shields.</summary>
    public bool SetShields(ICommonSession admin, EntityUid grid, bool enabled, out string result)
    {
        if (!_shields.SetWolfgateShieldEnabled(grid, enabled))
        {
            result = Loc.GetString("wg-ship-admin-result-no-shields");
            return false;
        }

        Audit(admin, LogImpact.Medium, grid, enabled ? "raised its shields" : "dropped its shields");
        result = Loc.GetString(enabled ? "wg-ship-admin-result-shields-on" : "wg-ship-admin-result-shields-off", ("ship", Name(grid)));
        return true;
    }

    /// <summary>
    /// Sets the strength the ship's shields have left, in percent. A hundred restores them in full and ends a
    /// recharge or overload; anything less wears off as the generators heal.
    /// </summary>
    public bool SetShieldStrength(ICommonSession admin, EntityUid grid, float percent, out string result)
    {
        if (!float.IsFinite(percent) || percent < 0f || percent > 100f)
        {
            result = Loc.GetString("wg-ship-admin-result-bad-strength");
            return false;
        }

        if (_shields.SetWolfgateShieldStrength(grid, percent / 100f) == 0)
        {
            result = Loc.GetString("wg-ship-admin-result-no-shields");
            return false;
        }

        // Only a full hundred ends a recharge or overload, so only that is reported as a restore.
        var whole = (int) MathF.Round(percent);
        Audit(admin, LogImpact.Medium, grid, $"set its shield strength to {percent.ToString(CultureInfo.InvariantCulture)}%");
        result = percent >= 100f
            ? Loc.GetString("wg-ship-admin-result-shields-restored", ("ship", Name(grid)))
            : Loc.GetString("wg-ship-admin-result-shields-strength", ("ship", Name(grid)), ("percent", whole));
        return true;
    }

    /// <summary>Fills or drains every battery aboard and sets the breakers to match, without an announcement.</summary>
    public bool SetPower(ICommonSession admin, EntityUid grid, bool powered, out string result)
    {
        if (_gridPower.SetPower(powered, grid, false) == 0)
        {
            result = Loc.GetString("wg-ship-admin-result-no-power");
            return false;
        }

        Audit(admin, LogImpact.Medium, grid, powered ? "powered it" : "unpowered it");
        result = Loc.GetString(powered ? "wg-ship-admin-result-power-on" : "wg-ship-admin-result-power-off", ("ship", Name(grid)));
        return true;
    }

    /// <summary>Switches one protection on or off.</summary>
    public bool SetProtect(ICommonSession admin, EntityUid grid, WGShipAdminProtect protect, bool on, out string result)
    {
        switch (protect)
        {
            case WGShipAdminProtect.Blastproof:
                SetBlastproof(grid, on);
                break;
            case WGShipAdminProtect.Pacified:
                Toggle<GridPacifierComponent>(grid, on);
                break;
            case WGShipAdminProtect.CleanupImmune:
                Toggle<CleanupImmuneComponent>(grid, on);
                break;
            case WGShipAdminProtect.GunsDisabled:
                Toggle<SpaceArtilleryDisabledGridComponent>(grid, on);
                break;
            case WGShipAdminProtect.FtlUnrestricted:
                Toggle<WGFtlUnrestrictedComponent>(grid, on);
                break;
            default:
                result = Loc.GetString("wg-ship-admin-result-unknown");
                return false;
        }

        Audit(admin, LogImpact.Medium, grid, $"set {protect} to {on}");
        var protectName = Loc.GetString($"wg-ship-admin-systems-protect-{protect.ToString().ToLowerInvariant()}");
        result = Loc.GetString(on ? "wg-ship-admin-result-protect-on" : "wg-ship-admin-result-protect-off", ("ship", Name(grid)), ("protect", protectName));
        return true;
    }

    /// <summary>The protections a ship has now.</summary>
    public WGShipAdminProtect Protections(EntityUid grid)
    {
        var protect = WGShipAdminProtect.None;
        if (TryComp<ProtectedGridComponent>(grid, out var guarded) && guarded.PreventExplosions)
            protect |= WGShipAdminProtect.Blastproof;

        if (HasComp<GridPacifierComponent>(grid))
            protect |= WGShipAdminProtect.Pacified;

        if (HasComp<CleanupImmuneComponent>(grid))
            protect |= WGShipAdminProtect.CleanupImmune;

        if (HasComp<SpaceArtilleryDisabledGridComponent>(grid))
            protect |= WGShipAdminProtect.GunsDisabled;

        if (HasComp<WGFtlUnrestrictedComponent>(grid))
            protect |= WGShipAdminProtect.FtlUnrestricted;

        return protect;
    }

    /// <summary>Every refit line, with the tier the ship owns of it.</summary>
    public List<WGShipAdminRefit> Refits(EntityUid grid)
    {
        var lines = new List<WGShipAdminRefit>();
        foreach (var refit in _proto.EnumeratePrototypes<WGShipRefitPrototype>())
        {
            lines.Add(new WGShipAdminRefit
            {
                Id = refit.ID,
                Name = Loc.GetString(refit.Name),
                Tier = _refits.GetRefit(grid, refit.ID).Tier,
                MaxTier = refit.Tiers.Count,
            });
        }

        lines.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return lines;
    }

    /// <summary>
    /// Brings a refit line up to a tier, free. The parts are refitted over ticks like a fix, and the ship owns the
    /// tier once they are all done.
    /// </summary>
    public bool StartRefit(ICommonSession admin, EntityUid grid, string refitId, int tier, out string result)
    {
        if (!_proto.TryIndex<WGShipRefitPrototype>(refitId, out var refit) || tier < 1 || tier > refit.Tiers.Count)
        {
            result = Loc.GetString("wg-ship-admin-result-bad-refit");
            return false;
        }

        if (_fixes.ContainsKey(grid))
        {
            result = Loc.GetString("wg-ship-admin-result-fix-running");
            return false;
        }

        var survey = _refits.Survey(grid, refit, tier);
        if (WGShipRefitSystem.Units(survey) == 0)
        {
            result = Loc.GetString("wg-ship-admin-result-refit-no-parts", ("refit", Loc.GetString(refit.Name)));
            return false;
        }

        var job = new FixJob { Admin = admin, Refit = refit, RefitTier = tier };
        foreach (var target in survey.Below)
        {
            job.Items.Enqueue(new WGDrydockWorkItem
            {
                Kind = WGDrydockWorkKind.Upgrade,
                Target = target.Entity,
                Upgrade = refit.ID,
                Tier = tier,
                LocalPos = target.LocalPos,
            });
        }

        job.Total = job.Items.Count;
        _fixes[grid] = job;
        Audit(admin, LogImpact.Medium, grid, $"started a free refit of {refit.ID} to tier {tier} ({job.Total} parts)");
        result = Loc.GetString("wg-ship-admin-result-refit-started", ("ship", Name(grid)), ("refit", Loc.GetString(refit.Name)), ("count", job.Total));
        return true;
    }

    /// <summary>Swaps the parts in every machine aboard for the best of their kind.</summary>
    public bool FitBestParts(ICommonSession admin, EntityUid grid, out string result)
    {
        var machines = new List<Entity<MachineComponent>>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (TryComp<MachineComponent>(child, out var machine))
                machines.Add((child, machine));
        }

        var upgraded = 0;
        foreach (var machine in machines)
        {
            if (_fitting.UpgradeParts(machine))
                upgraded++;
        }

        Audit(admin, LogImpact.Medium, grid, $"fitted the best parts to {upgraded} machines");
        result = Loc.GetString("wg-ship-admin-result-fitted", ("ship", Name(grid)), ("count", upgraded));
        return true;
    }

    /// <summary>Applies a hull modifier, which swaps parts of the ship for others. It can't be taken back.</summary>
    public bool ApplyModifier(ICommonSession admin, EntityUid grid, string modifier, out string result)
    {
        if (!_proto.HasIndex<GridModificationPrototype>(modifier))
        {
            result = Loc.GetString("wg-ship-admin-result-bad-modifier");
            return false;
        }

        _gridModifiers.ModifyGrid(grid, new List<ProtoId<GridModificationPrototype>> { modifier });
        Audit(admin, LogImpact.Medium, grid, $"applied hull modifier {modifier}");
        result = Loc.GetString("wg-ship-admin-result-modified", ("ship", Name(grid)), ("modifier", modifier));
        return true;
    }

    private void SetIffFlag(EntityUid grid, IFFFlags flag, bool on)
    {
        if (on)
            _shuttle.AddIFFFlag(grid, flag);
        else
            _shuttle.RemoveIFFFlag(grid, flag);
    }

    private void Toggle<T>(EntityUid grid, bool on) where T : Component, new()
    {
        if (on)
            EnsureComp<T>(grid);
        else
            RemComp<T>(grid);
    }

    /// <summary>
    /// Blast protection on a grid that may carry protections of its own: only the two this switch owns are changed,
    /// and the component goes when nothing on it is left set.
    /// </summary>
    private void SetBlastproof(EntityUid grid, bool on)
    {
        Toggle<WGBlastproofFloorsComponent>(grid, on);
        if (!on && !HasComp<ProtectedGridComponent>(grid))
            return;

        var guarded = EnsureComp<ProtectedGridComponent>(grid);
        guarded.PreventExplosions = on;
        guarded.PreventEmpEvents = on;
        Dirty(grid, guarded);
        if (!on && !guarded.PreventFloorRemoval && !guarded.PreventFloorPlacement && !guarded.PreventRCDUse
            && !guarded.PreventArtifactTriggers && !guarded.KillHostileMobs)
        {
            RemComp<ProtectedGridComponent>(grid);
        }
    }
}
