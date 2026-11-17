using System.Globalization;
using System.Linq;
using Content.Server.Administration;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WG.ShipCommand;

/// <summary>
/// A commanded ship's price rules from the admin console (the ship command console's Prices tab does the same): list
/// them, add or replace one, switch one (or the owner's and admins' free service) on and off, or remove one.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WGShipPriceCommand : LocalizedEntityCommands
{
    [Dependency] private WGShipCommandSystem _command = default!;
    [Dependency] private WGShipServiceSystem _services = default!;

    public override string Command => "wg_shipprice";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!WGShipCommandConsole.TryGetGrid<WGShipCommandComponent>(shell, EntityManager, args[1], out var grid, out var command)
            || !WGShipCommandConsole.CanConfigure(shell, _command, grid))
            return;

        var ship = (grid, command);
        switch (args[0])
        {
            case "list" when args.Length == 2:
                shell.WriteLine(Loc.GetString("cmd-wg_shipprice-free", ("owner", command.OwnerServedFree), ("admins", command.AdminsServedFree)));
                foreach (var rule in command.PriceRules)
                {
                    shell.WriteLine(Loc.GetString("cmd-wg_shipprice-line",
                        ("name", rule.Name),
                        ("audience", rule.Audience == WGShipPriceAudience.Company ? $"company:{rule.Company}" : rule.Audience.ToString()),
                        ("services", rule.Services.ToString()),
                        ("multiplier", rule.Multiplier.ToString("0.##", CultureInfo.InvariantCulture)),
                        ("enabled", rule.Enabled)));
                }
                break;
            case "add" when args.Length == 6:
                if (args[2].Length > WGShipConsoleSystem.MaxRuleName || args[2] is "owner" or "admins"
                    || !TryParseAudience(args[3], out var audience, out var company)
                    || company?.Length > WGShipConsoleSystem.MaxRuleName
                    || !TryParseServices(args[4], out var services)
                    || (services & ~WGShipServiceKind.All) != 0
                    || !float.TryParse(args[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var multiplier)
                    || !float.IsFinite(multiplier) || multiplier is < 0 or > 10)
                {
                    shell.WriteError(Loc.GetString("cmd-wg_shipprice-bad-rule"));
                    return;
                }

                _services.SetRule(ship, new WGShipPriceRule
                {
                    Name = args[2],
                    Audience = audience,
                    Company = company,
                    Services = services,
                    Multiplier = multiplier,
                }, shell.Player?.AttachedEntity);
                shell.WriteLine(Loc.GetString("cmd-wg_shipprice-added", ("name", args[2])));
                break;
            case "toggle" when args.Length == 3:
                if (!_services.ToggleRule(ship, args[2], shell.Player?.AttachedEntity, out var enabled))
                {
                    shell.WriteError(Loc.GetString("cmd-wg_shipprice-no-rule", ("name", args[2])));
                    return;
                }

                shell.WriteLine(Loc.GetString("cmd-wg_shipprice-toggled", ("name", args[2]), ("enabled", enabled)));
                break;
            case "remove" when args.Length == 3:
                if (!_services.RemoveRule(ship, args[2], shell.Player?.AttachedEntity))
                {
                    shell.WriteError(Loc.GetString("cmd-wg_shipprice-no-rule", ("name", args[2])));
                    return;
                }

                shell.WriteLine(Loc.GetString("cmd-wg_shipprice-removed", ("name", args[2])));
                break;
            default:
                shell.WriteLine(Help);
                break;
        }
    }

    private static bool TryParseAudience(string arg, out WGShipPriceAudience audience, out string? company)
    {
        company = null;
        if (arg.StartsWith("company:", StringComparison.OrdinalIgnoreCase) && arg.Length > "company:".Length)
        {
            audience = WGShipPriceAudience.Company;
            company = arg["company:".Length..];
            return true;
        }

        return Enum.TryParse(arg, true, out audience)
               && audience is WGShipPriceAudience.Everyone or WGShipPriceAudience.Staff or WGShipPriceAudience.Officers;
    }

    private static bool TryParseServices(string arg, out WGShipServiceKind services)
    {
        services = WGShipServiceKind.None;
        foreach (var part in arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<WGShipServiceKind>(part, true, out var kind) || kind == WGShipServiceKind.None)
                return false;

            services |= kind;
        }

        return services != WGShipServiceKind.None;
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(new[] { "list", "add", "toggle", "remove" }, Loc.GetString("cmd-wg_shipprice-hint-sub")),
            2 => CompletionResult.FromHintOptions(new[] { "here" }, Loc.GetString("cmd-wg_ship-hint-grid")),
            3 when args[0] == "toggle" => CompletionResult.FromHintOptions(new[] { "owner", "admins" }, Loc.GetString("cmd-wg_shipprice-hint-name")),
            3 => CompletionResult.FromHint(Loc.GetString("cmd-wg_shipprice-hint-name")),
            4 when args[0] == "add" => CompletionResult.FromHintOptions(new[] { "everyone", "staff", "officers", "company:" }, Loc.GetString("cmd-wg_shipprice-hint-audience")),
            5 when args[0] == "add" => CompletionResult.FromHintOptions(
                Enum.GetNames<WGShipServiceKind>().Where(n => n != nameof(WGShipServiceKind.None)).Select(n => n.ToLowerInvariant()),
                Loc.GetString("cmd-wg_shipprice-hint-services")),
            6 when args[0] == "add" => CompletionResult.FromHint(Loc.GetString("cmd-wg_shipprice-hint-multiplier")),
            _ => CompletionResult.Empty,
        };
    }
}
