using Content.Shared._Mono.Company;
using Content.Shared.Construction.Components;
using Content.Shared.Popups;
using Content.Shared.Pulling.Events;
using Content.Shared.Storage.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Contracts;

/// <summary>A faction station's receiving crate: only that faction's members may open it.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFContractReceivingComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<ProtoId<CompanyPrototype>> Companies = new();
}

/// <summary>Keeps a receiving crate shut and in place for anyone outside its faction, so delivered goods can't be taken back and sold again.</summary>
public sealed class WFContractReceivingSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WFContractReceivingComponent, StorageOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<WFContractReceivingComponent, BeingPulledAttemptEvent>(OnPullAttempt);
        SubscribeLocalEvent<WFContractReceivingComponent, UnanchorAttemptEvent>(OnUnanchorAttempt);
    }

    private void OnPullAttempt(EntityUid uid, WFContractReceivingComponent comp, BeingPulledAttemptEvent args)
    {
        if (!args.Cancelled && !Member((uid, comp), args.Puller))
            args.Cancel();
    }

    private void OnUnanchorAttempt(EntityUid uid, WFContractReceivingComponent comp, UnanchorAttemptEvent args)
    {
        if (args.Cancelled || Member((uid, comp), args.User))
            return;

        args.Cancel();
        _popup.PopupClient(Loc.GetString("wf-contract-receiving-locked"), uid, args.User);
    }

    /// <summary>Whether a user belongs to one of the crate's factions; anyone does when it names none.</summary>
    private bool Member(Entity<WFContractReceivingComponent> ent, EntityUid user)
    {
        return ent.Comp.Companies.Count == 0
               || CompOrNull<CompanyComponent>(user)?.CompanyName is { } company && ent.Comp.Companies.Contains(company);
    }

    private void OnOpenAttempt(Entity<WFContractReceivingComponent> ent, ref StorageOpenAttemptEvent args)
    {
        if (args.Cancelled || Member(ent, args.User))
            return;

        args.Cancelled = true;
        if (!args.Silent)
            _popup.PopupClient(Loc.GetString("wf-contract-receiving-locked"), ent, args.User);
    }
}
