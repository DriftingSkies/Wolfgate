using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Robust.Shared.Network;

namespace Content.Shared._WF.Xenonids.Parasite.Surgery;

/// <summary>Wires the burst chest repair surgery to the parasite system; it is offered only while the host can still be mended.</summary>
public sealed class XenoBurstSurgerySystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedXenoParasiteSystem _parasite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<XenoBurstSurgeryConditionComponent, SurgeryValidEvent>(OnValid);
        SubscribeLocalEvent<XenoBurstClearStepComponent, SurgeryStepEvent>(OnClear);
        SubscribeLocalEvent<XenoBurstClearStepComponent, SurgeryStepCompleteCheckEvent>(OnClearCheck);
        SubscribeLocalEvent<XenoBurstMendStepComponent, SurgeryStepEvent>(OnMend);
        SubscribeLocalEvent<XenoBurstMendStepComponent, SurgeryStepCompleteCheckEvent>(OnMendCheck);
    }

    private void OnValid(Entity<XenoBurstSurgeryConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (!_parasite.CanRepairBurst(args.Body))
            args.Cancelled = true;
    }

    private void OnClear(Entity<XenoBurstClearStepComponent> ent, ref SurgeryStepEvent args)
    {
        if (_net.IsServer)
            _parasite.TryClearBurst(args.Body);
    }

    private void OnClearCheck(Entity<XenoBurstClearStepComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (!TryComp(args.Body, out VictimBurstComponent? burst) || !burst.Cleared)
            args.Cancelled = true;
    }

    private void OnMend(Entity<XenoBurstMendStepComponent> ent, ref SurgeryStepEvent args)
    {
        if (_net.IsServer)
            _parasite.TryRepairBurst(args.Body);
    }

    private void OnMendCheck(Entity<XenoBurstMendStepComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (HasComp<VictimBurstComponent>(args.Body))
            args.Cancelled = true;
    }
}
