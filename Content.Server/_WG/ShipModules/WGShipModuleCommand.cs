using System.Linq;
using Content.Server._WG.ShipAdmin;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared._WG.ShipModules;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipModules;

/// <summary>Fits a module into a slot of any ship, or empties the slot, free and at once.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WGShipModuleCommand : LocalizedEntityCommands
{
    [Dependency] private WGShipModuleSystem _modules = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    private const string None = "none";

    public override string Command => "shipmodule";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 3
            || !int.TryParse(args[1], out var number)
            || number < 1
            || number > WGShipModulesComponent.SlotCount)
        {
            shell.WriteLine(Help);
            return;
        }

        if (WGShipRefitCommand.ResolveGrid(EntityManager, shell, args[0]) is not { } grid)
        {
            shell.WriteError(Loc.GetString("cmd-shipmaster-not-grid", ("grid", args[0])));
            return;
        }

        var slot = number - 1;
        var who = shell.Player?.Name ?? "Server";
        if (args[2] == None)
        {
            _modules.Unfit(grid, slot);
            shell.WriteLine(Loc.GetString("cmd-shipmodule-emptied", ("slot", number)));
            _adminLog.Add(LogType.AdminCommands, LogImpact.Medium,
                $"{who} emptied module slot {number} of {EntityManager.ToPrettyString(grid):grid}");
            return;
        }

        if (!_prototype.TryIndex<WGShipModulePrototype>(args[2], out var module))
        {
            shell.WriteError(Loc.GetString("cmd-shipmodule-unknown", ("module", args[2])));
            return;
        }

        if (!_modules.Fit(grid, slot, module.ID))
        {
            shell.WriteError(Loc.GetString("cmd-shipmodule-fitted-elsewhere", ("module", Loc.GetString(module.Name)),
                ("slot", _modules.FittedSlot(grid, module.ID) + 1 ?? 0)));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-shipmodule-fitted", ("module", Loc.GetString(module.Name)), ("slot", number)));
        _adminLog.Add(LogType.AdminCommands, LogImpact.Medium,
            $"{who} fitted {module.ID} into module slot {number} of {EntityManager.ToPrettyString(grid):grid}");
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        switch (args.Length)
        {
            case 1:
                return CompletionResult.FromHintOptions(new[] { WGShipRefitCommand.Here }, Loc.GetString("cmd-shiprefit-hint-grid"));
            case 2:
                var slots = Enumerable.Range(1, WGShipModulesComponent.SlotCount).Select(s => s.ToString());
                return CompletionResult.FromHintOptions(slots, Loc.GetString("cmd-shipmodule-hint-slot"));
            case 3:
                var modules = _prototype.EnumeratePrototypes<WGShipModulePrototype>()
                    .OrderBy(p => p.ID)
                    .Select(p => new CompletionOption(p.ID, Loc.GetString(p.Name)))
                    .Prepend(new CompletionOption(None));
                return CompletionResult.FromHintOptions(modules, Loc.GetString("cmd-shipmodule-hint-module"));
            default:
                return CompletionResult.Empty;
        }
    }
}
