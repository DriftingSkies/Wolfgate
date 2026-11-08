using Content.Shared._WF.PlanetPois;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.NPC.Components;
using Content.Shared.Popups;
using Content.Shared.Tools;
using Content.Shared.Tools.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.PlanetPois;

/// <summary>The core of an automated outpost: hacked with a multitool, it stands down the turrets guarding the site.</summary>
[RegisterComponent]
public sealed partial class WFPoiCoreComponent : Component
{
    /// <summary>How far its guards may stand from it.</summary>
    [DataField]
    public float Range = 24f;

    [DataField]
    public TimeSpan HackDelay = TimeSpan.FromSeconds(15);

    [DataField]
    public ProtoId<ToolQualityPrototype> HackQuality = "Pulsing";

    [DataField]
    public bool Hacked;
}

/// <summary>A turret an automated outpost's core commands.</summary>
[RegisterComponent]
public sealed partial class WFPoiCoreGuardComponent : Component;

/// <summary>Hacks automated outpost cores and stands their guards down.</summary>
public sealed class WFPoiCoreSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedToolSystem _tool = default!;

    private readonly HashSet<Entity<WFPoiCoreGuardComponent>> _guards = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPoiCoreComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFPoiCoreComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFPoiCoreComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WFPoiCoreComponent, WFPoiCoreHackDoAfterEvent>(OnHacked);
    }

    private void OnMapInit(Entity<WFPoiCoreComponent> ent, ref MapInitEvent args)
    {
        _appearance.SetData(ent, WFPoiCoreVisuals.Hacked, ent.Comp.Hacked);
    }

    private void OnExamined(Entity<WFPoiCoreComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(ent.Comp.Hacked ? "wf-poi-core-examine-hacked" : "wf-poi-core-examine-active"));
    }

    private void OnInteractUsing(Entity<WFPoiCoreComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.Hacked || !_tool.HasQuality(args.Used, ent.Comp.HackQuality))
            return;

        args.Handled = _tool.UseTool(args.Used, args.User, ent, (float) ent.Comp.HackDelay.TotalSeconds, ent.Comp.HackQuality,
            new WFPoiCoreHackDoAfterEvent());
    }

    private void OnHacked(Entity<WFPoiCoreComponent> ent, ref WFPoiCoreHackDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || ent.Comp.Hacked)
            return;

        args.Handled = true;
        var count = StandDown(ent);
        _popup.PopupEntity(Loc.GetString("wf-poi-core-hacked", ("count", count)), ent, args.User);
    }

    /// <summary>Marks the core hacked and takes every guard in its range off its faction, so it fires on no one. Returns how many.</summary>
    public int StandDown(Entity<WFPoiCoreComponent> core)
    {
        core.Comp.Hacked = true;
        _appearance.SetData(core, WFPoiCoreVisuals.Hacked, true);

        _guards.Clear();
        _lookup.GetEntitiesInRange(Transform(core).Coordinates, core.Comp.Range, _guards);
        foreach (var guard in _guards)
        {
            RemComp<NpcFactionMemberComponent>(guard);
            RemComp<WFPoiCoreGuardComponent>(guard);
        }

        return _guards.Count;
    }
}
