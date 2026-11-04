using System.Diagnostics;
using System.Threading.Tasks;
using Content.Shared._WF.CCVar;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Serialization.Manager;

namespace Content.Server._WF.Performance;

/// <summary>
/// Compiles the serializer delegates of every component type on a worker thread at startup. Otherwise the first ship
/// save or copy compiles dozens of them in one tick.
/// </summary>
public sealed class WFSerializerWarmupSystem : EntitySystem
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
