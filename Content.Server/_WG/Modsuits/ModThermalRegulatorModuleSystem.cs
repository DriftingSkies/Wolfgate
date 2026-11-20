using Content.Server.Body.Components;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._WG.Modsuits.Components;
using Content.Shared._WG.Modsuits.Modules;

namespace Content.Server._WG.Modsuits;

/// <summary>Holds the wearer's body temperature at the module's target while it is active.</summary>
public sealed partial class ModThermalRegulatorModuleSystem : EntitySystem
{
    [Dependency] private TemperatureSystem _temperature = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ModThermalRegulatorModuleComponent, ModModuleComponent>();
        while (query.MoveNext(out _, out var regulator, out var module))
        {
            if (!module.Active
                || !TryComp<ModSuitComponent>(module.Suit, out var suit)
                || suit.Wearer is not { } wearer
                || !TryComp<TemperatureComponent>(wearer, out var temperature))
                continue;

            var target = TryComp<ThermalRegulatorComponent>(wearer, out var body)
                ? body.NormalBodyTemperature
                : regulator.TargetTemperature;
            var difference = target - temperature.CurrentTemperature;
            if (MathF.Abs(difference) < 0.01f)
                continue;

            var step = MathF.Sign(difference) * MathF.Min(MathF.Abs(difference), regulator.Rate * frameTime);
            var heat = step * _temperature.GetHeatCapacity(wearer, temperature);
            _temperature.ChangeHeat(wearer, heat, true, temperature);
        }
    }
}
