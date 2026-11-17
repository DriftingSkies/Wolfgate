using Content.Server._WG.Administration.Systems;
using Content.Server.Administration;
using Content.Shared._WG.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WG.Administration.Commands;

/// <summary>
/// Offers an entity to every current ghost through a sign-up prompt, making it a ghost role first if it isn't one.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class OfferToGhostsCommand : LocalizedEntityCommands
{
    [Dependency] private GhostOfferSystem _ghostOffer = default!;

    public override string Command => WolfgateAdminCommands.OfferToGhosts;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 4)
        {
            shell.WriteError(Loc.GetString("cmd-offertoghosts-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netEntity) || !EntityManager.TryGetEntity(netEntity, out var uid))
        {
            shell.WriteError(Loc.GetString("cmd-offertoghosts-invalid-entity", ("entity", args[0])));
            return;
        }

        var offered = _ghostOffer.TryOffer(uid.Value, shell.Player,
            args.Length > 1 ? args[1] : null,
            args.Length > 2 ? args[2] : null,
            args.Length > 3 ? args[3] : null,
            out var message);

        if (offered)
            shell.WriteLine(message);
        else
            shell.WriteError(message);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHint(Loc.GetString("cmd-offertoghosts-hint-entity")),
            2 => CompletionResult.FromHint(Loc.GetString("cmd-offertoghosts-hint-name")),
            3 => CompletionResult.FromHint(Loc.GetString("cmd-offertoghosts-hint-description")),
            4 => CompletionResult.FromHint(Loc.GetString("cmd-offertoghosts-hint-rules")),
            _ => CompletionResult.Empty,
        };
    }
}
