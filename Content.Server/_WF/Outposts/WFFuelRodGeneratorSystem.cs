using Content.Server._WF.Outposts.Components;
using Content.Server.Power.Generator;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Containers;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Generators that burn a reactor fuel rod: one is loaded by using it on the generator, it burns down as the generator
/// runs, and the eject button takes it out again, spent or not.
/// </summary>
public sealed class WFFuelRodGeneratorSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WFFuelRodGeneratorComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<WFFuelRodGeneratorComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WFFuelRodGeneratorComponent, GeneratorGetFuelEvent>(OnGetFuel);
        SubscribeLocalEvent<WFFuelRodGeneratorComponent, GeneratorUseFuel>(OnUseFuel);
        SubscribeLocalEvent<WFFuelRodGeneratorComponent, GeneratorEmpty>(OnEmpty);
        SubscribeLocalEvent<WFFuelRodGeneratorComponent, ExaminedEvent>(OnGeneratorExamined);
        SubscribeLocalEvent<WFFuelRodChargeComponent, ExaminedEvent>(OnRodExamined);
    }

    private void OnInit(Entity<WFFuelRodGeneratorComponent> ent, ref ComponentInit args)
    {
        _container.EnsureContainer<ContainerSlot>(ent, WFFuelRodGeneratorComponent.ContainerId);
    }

    private void OnInteractUsing(Entity<WFFuelRodGeneratorComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || MetaData(args.Used).EntityPrototype?.ID != ent.Comp.Rod)
            return;

        args.Handled = true;
        var slot = _container.EnsureContainer<ContainerSlot>(ent, WFFuelRodGeneratorComponent.ContainerId);
        if (slot.ContainedEntity != null)
        {
            _popup.PopupEntity(Loc.GetString("wf-fuel-rod-generator-full", ("generator", ent)), ent, args.User);
            return;
        }

        if (!EnsureComp<WFFuelRodChargeComponent>(args.Used, out var charge))
            charge.Fuel = charge.Full = ent.Comp.RodFuel;

        if (!_container.Insert(args.Used, slot))
            return;

        _popup.PopupEntity(Loc.GetString("wf-fuel-rod-generator-loaded", ("rod", args.Used), ("generator", ent)), ent, args.User);
    }

    private void OnGetFuel(Entity<WFFuelRodGeneratorComponent> ent, ref GeneratorGetFuelEvent args)
    {
        if (TryGetRod(ent, out var charge))
            args.Fuel = charge.Fuel;
    }

    private void OnUseFuel(Entity<WFFuelRodGeneratorComponent> ent, GeneratorUseFuel args)
    {
        if (TryGetRod(ent, out var charge))
            charge.Fuel = Math.Max(0f, charge.Fuel - args.FuelUsed);
    }

    private void OnEmpty(Entity<WFFuelRodGeneratorComponent> ent, GeneratorEmpty args)
    {
        if (_container.TryGetContainer(ent, WFFuelRodGeneratorComponent.ContainerId, out var slot))
            _container.EmptyContainer(slot);
    }

    private void OnGeneratorExamined(Entity<WFFuelRodGeneratorComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(TryGetRod(ent, out var charge)
            ? Loc.GetString("wf-fuel-rod-generator-examine-loaded", ("percent", Percent(charge)))
            : Loc.GetString("wf-fuel-rod-generator-examine-empty"));
    }

    private void OnRodExamined(Entity<WFFuelRodChargeComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-fuel-rod-examine-charge", ("percent", Percent(ent.Comp))));
    }

    private bool TryGetRod(EntityUid generator, out WFFuelRodChargeComponent charge)
    {
        charge = default!;
        return _container.TryGetContainer(generator, WFFuelRodGeneratorComponent.ContainerId, out var slot)
            && slot.ContainedEntities.Count > 0
            && TryComp(slot.ContainedEntities[0], out charge!);
    }

    private static int Percent(WFFuelRodChargeComponent charge)
    {
        return charge.Full <= 0 ? 0 : (int) MathF.Ceiling(100f * charge.Fuel / charge.Full);
    }
}
