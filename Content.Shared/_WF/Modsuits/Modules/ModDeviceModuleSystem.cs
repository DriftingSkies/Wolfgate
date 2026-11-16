using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Modsuits.Modules;

public sealed partial class ModDeviceModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModDeviceModuleComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ModDeviceModuleComponent, ModModuleActivateAttemptEvent>(OnActivateAttempt);
        SubscribeLocalEvent<ModDeviceModuleComponent, ModModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<ModDeviceModuleComponent, ModModuleDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<ModDeviceItemComponent, GotUnequippedHandEvent>(OnDeviceDropped);
        SubscribeLocalEvent<ModDeviceItemComponent, ComponentShutdown>(OnDeviceShutdown);
    }

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ModSuitSystem _modSuit = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    private readonly HashSet<EntityUid> _pendingReturn = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingReturn.Count == 0)
            return;

        foreach (var device in _pendingReturn)
        {
            if (TerminatingOrDeleted(device)
                || !TryComp<ModDeviceItemComponent>(device, out var item)
                || item.Module is not { } module
                || !TryComp<ModDeviceModuleComponent>(module, out var moduleComp))
                continue;

            if (TryComp<ModModuleComponent>(module, out var moduleBase) && moduleBase.Active)
                continue;

            _container.Insert(device, moduleComp.DeviceContainer);
        }

        _pendingReturn.Clear();
    }
    private void OnInit(Entity<ModDeviceModuleComponent> ent, ref ComponentInit args)
    {
        ent.Comp.DeviceContainer = _container.EnsureContainer<ContainerSlot>(ent, ent.Comp.ContainerId);
    }
    private void OnActivateAttempt(Entity<ModDeviceModuleComponent> ent, ref ModModuleActivateAttemptEvent args)
    {
        if (args.User is not { } user
            || !TryComp<HandsComponent>(user, out var hands)
            || hands.CountFreeHands() > 0)
            return;

        args.Cancelled = true;
        args.Reason = "modsuit-module-hands-full";
    }
    private void OnActivated(Entity<ModDeviceModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
        if (args.Wearer is not { } wearer || GetOrSpawnDevice(ent) is not { } device)
            return;

        _pendingReturn.Remove(device);

        if (!_hands.TryPickupAnyHand(wearer, device, checkActionBlocker: false))
            Deactivate(ent);
    }
    private void OnDeactivated(Entity<ModDeviceModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        if (ent.Comp.DeviceEntity is not { } device
            || _pendingReturn.Contains(device)
            || ent.Comp.DeviceContainer.Contains(device)
            || TerminatingOrDeleted(device))
            return;

        _container.Insert(device, ent.Comp.DeviceContainer);
    }
    private void OnDeviceDropped(Entity<ModDeviceItemComponent> ent, ref GotUnequippedHandEvent args)
    {
        if (_timing.ApplyingState
            || ent.Comp.Module is not { } module
            || !TryComp<ModDeviceModuleComponent>(module, out var moduleComp)
            || !TryComp<ModModuleComponent>(module, out var moduleBase)
            || !moduleBase.Active)
            return;

        _pendingReturn.Add(ent);
        Deactivate((module, moduleComp));
    }
    private void OnDeviceShutdown(Entity<ModDeviceItemComponent> ent, ref ComponentShutdown args)
    {
        _pendingReturn.Remove(ent);

        if (ent.Comp.Module is not { } module
            || !TryComp<ModDeviceModuleComponent>(module, out var moduleComp)
            || moduleComp.DeviceEntity != ent.Owner)
            return;

        moduleComp.DeviceEntity = null;
        Dirty(module, moduleComp);
    }

    private EntityUid? GetOrSpawnDevice(Entity<ModDeviceModuleComponent> ent)
    {
        if (ent.Comp.DeviceEntity is { } existing && !TerminatingOrDeleted(existing))
            return existing;

        if (ent.Comp.DeviceContainer.ContainedEntity is { } contained)
            return contained;

        if (!PredictedTrySpawnInContainer(ent.Comp.Device, ent, ent.Comp.ContainerId, out var device))
            return null;

        var item = EnsureComp<ModDeviceItemComponent>(device.Value);
        item.Module = ent;
        Dirty(device.Value, item);

        ent.Comp.DeviceEntity = device;
        Dirty(ent);
        return device;
    }

    public void SetDevice(Entity<ModDeviceModuleComponent> ent, EntProtoId device)
    {
        if (ent.Comp.Device == device)
            return;

        if (!_modSuit.TryGetModuleSuit(ent, out var suit))
            return;

        var wasActive = TryComp<ModModuleComponent>(ent, out var moduleBase) && moduleBase.Active;

        if (wasActive)
            _modSuit.SetModuleActive(suit, (ent.Owner, moduleBase!), false);

        if (ent.Comp.DeviceEntity is { } old)
        {
            _pendingReturn.Remove(old);
            ent.Comp.DeviceEntity = null;
            PredictedQueueDel(old);
        }

        if (ent.Comp.DeviceContainer.ContainedEntity is { } contained)
        {
            _container.Remove(contained, ent.Comp.DeviceContainer);
            PredictedQueueDel(contained);
        }

        ent.Comp.Device = device;
        Dirty(ent);

        if (wasActive)
            _modSuit.SetModuleActive(suit, (ent.Owner, moduleBase!), true);
    }

    private void Deactivate(Entity<ModDeviceModuleComponent> ent)
    {
        if (TryComp<ModModuleComponent>(ent, out var moduleBase)
            && moduleBase.Active
            && _modSuit.TryGetModuleSuit(ent, out var suit))
            _modSuit.SetModuleActive(suit, (ent.Owner, moduleBase), false);
    }
}
