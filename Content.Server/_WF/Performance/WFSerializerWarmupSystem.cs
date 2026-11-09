using System.Diagnostics;
using System.Threading.Tasks;
using Content.Shared._WF.CCVar;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Serialization.Manager;

namespace Content.Server._WF.Performance;

/// <summary>
/// Compiles the write and copy delegates of every component type on a worker thread at startup. Otherwise the first
/// ship save or map load of the round compiles dozens of them in one tick.
/// </summary>
public sealed partial class WFSerializerWarmupSystem : EntitySystem
{
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    public override void Initialize()
    {
        base.Initialize();

        // The dummy ticker marks test servers, which would only waste the thread.
        if (!_cfg.GetCVar(WFIdleHelmCVars.SerializerWarmup) || _cfg.GetCVar(CCVars.GameDummyTicker))
            return;

        // The Lupine engine does the same warm-up itself after PostInit; two threads compiling the same delegates
        // only slow each other down.
        if (_cfg.GetCVar(Robust.Shared.CVars.SerializationPrecompile))
            return;

        var types = new List<Type>(_factory.AllRegisteredTypes);
        var serialization = _serialization;
        var sawmill = Log;
        Task.Run(() =>
        {
            var timer = Stopwatch.StartNew();
            var failed = 0;
            foreach (var type in types)
            {
                try
                {
                    var instance = Activator.CreateInstance(type);
                    serialization.WriteValue(type, instance, true);

                    // Map loads copy each component into the entity as IComponent; compile and run that path once.
                    if (instance is IComponent source && Activator.CreateInstance(type) is IComponent target)
                        serialization.CopyTo(source, ref target, skipHook: true, notNullableOverride: true);
                }
                catch (Exception)
                {
                    failed++;
                }
            }

            sawmill.Info($"Serializer warm-up of {types.Count} component types took {timer.ElapsedMilliseconds} ms, {failed} skipped.");
        });
    }
}
