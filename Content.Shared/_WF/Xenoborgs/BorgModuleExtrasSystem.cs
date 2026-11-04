using Content.Shared._NF.Silicons.Borgs;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Whitelist;

namespace Content.Shared._WF.Xenoborgs;

/// <summary>
/// Runs wizden's <see cref="ComponentBorgModuleComponent"/> and <see cref="BorgModuleWhitelistComponent"/> on top of
/// the older server borg system: grants module components on install and blocks incompatible module inserts.
/// </summary>
public sealed partial class BorgModuleExtrasSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ComponentBorgModuleComponent, BorgModuleInstalledEvent>(OnComponentModuleInstalled);
        SubscribeLocalEvent<ComponentBorgModuleComponent, BorgModuleUninstalledEvent>(OnComponentModuleUninstalled);
        SubscribeLocalEvent<BorgModuleComponent, BorgCanInsertModuleEvent>(OnCanInsertModule);
        SubscribeLocalEvent<BorgModuleWhitelistComponent, ExaminedEvent>(OnWhitelistExamine);
    }

    private void OnComponentModuleInstalled(Entity<ComponentBorgModuleComponent> ent, ref BorgModuleInstalledEvent args)
    {
        EntityManager.AddComponents(args.ChassisEnt, ent.Comp.Components);
    }

    private void OnComponentModuleUninstalled(Entity<ComponentBorgModuleComponent> ent, ref BorgModuleUninstalledEvent args)
    {
        if (TerminatingOrDeleted(args.ChassisEnt))
            return;

        EntityManager.RemoveComponents(args.ChassisEnt, ent.Comp.Components);
    }

    private void OnWhitelistExamine(Entity<BorgModuleWhitelistComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.WhitelistInfo is { } info)
            args.PushMarkup(Loc.GetString(info));
    }

    /// <summary>
    /// Cancels the insert when either module blacklists the other, both grant the same component,
    /// or the new module's prerequisite isn't installed.
    /// </summary>
    private void OnCanInsertModule(Entity<BorgModuleComponent> ent, ref BorgCanInsertModuleEvent args)
    {
        if (args.Cancelled)
            return;

        TryComp<BorgModuleWhitelistComponent>(ent, out var newWhitelist);
        TryComp<ComponentBorgModuleComponent>(ent, out var newComponents);
        var prerequisiteFulfilled = newWhitelist?.ModuleWhitelist == null;

        foreach (var contained in args.Chassis.Comp.ModuleContainer.ContainedEntities)
        {
            if (!prerequisiteFulfilled && _whitelist.IsWhitelistPass(newWhitelist!.ModuleWhitelist, contained))
                prerequisiteFulfilled = true;

            if (!Incompatible(ent, newWhitelist, newComponents, contained))
                continue;

            if (args.User is { } user)
                _popup.PopupEntity(Loc.GetString("borg-module-incompatible", ("existing", contained)), args.Chassis, user);
            args.Cancelled = true;
            return;
        }

        if (prerequisiteFulfilled)
            return;

        if (args.User is { } prerequisiteUser)
            _popup.PopupEntity(Loc.GetString("borg-module-prerequisite-unfulfilled"), args.Chassis, prerequisiteUser);
        args.Cancelled = true;
    }

    private bool Incompatible(EntityUid module,
        BorgModuleWhitelistComponent? moduleWhitelist,
        ComponentBorgModuleComponent? moduleComponents,
        EntityUid contained)
    {
        if (moduleWhitelist != null && _whitelist.IsWhitelistPass(moduleWhitelist.ModuleBlacklist, contained))
            return true;

        if (TryComp<BorgModuleWhitelistComponent>(contained, out var containedWhitelist) &&
            _whitelist.IsWhitelistPass(containedWhitelist.ModuleBlacklist, module))
            return true;

        if (moduleComponents == null || !TryComp<ComponentBorgModuleComponent>(contained, out var containedComponents))
            return false;

        foreach (var name in moduleComponents.Components.Keys)
        {
            if (containedComponents.Components.ContainsKey(name))
                return true;
        }

        return false;
    }
}
