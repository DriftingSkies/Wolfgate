using System.Globalization;
using Content.Server._WG.Companies;
using Content.Server.Radio.EntitySystems;
using Content.Shared._WG.Roles;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Roles;

/// <summary>Sends the arrival line for <see cref="WGRadioOnJoinComponent"/>, naming the same title the station announcement would.</summary>
public sealed partial class WGRadioOnJoinSystem : EntitySystem
{
    [Dependency] private WGCompanySystem _company = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private RadioSystem _radio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGRadioOnJoinComponent, PlayerSpawnCompleteEvent>(OnSpawned);
    }

    private void OnSpawned(EntityUid uid, WGRadioOnJoinComponent comp, PlayerSpawnCompleteEvent args)
    {
        if (args.Silent)
            return;

        var title = _company.RankTitleFor(args.Player, args.Profile, args.JobId)
            ?? CustomJobTitleRules.GetTitle(args.Profile, args.JobId, _prototype)
            ?? JobName(args.JobId);

        var message = Loc.GetString("wg-radio-on-join", ("character", Name(uid)), ("job", title));
        _radio.SendRadioMessage(args.Station, message, comp.RadioChannel, uid);
    }

    private string JobName(string? job)
    {
        return job != null && _prototype.TryIndex<JobPrototype>(job, out var proto)
            ? CultureInfo.CurrentCulture.TextInfo.ToTitleCase(proto.LocalizedName)
            : Loc.GetString("wg-radio-on-join-no-job");
    }
}
