using Content.Server.Medical;
using Content.Server.Medical.Components;
using Content.Shared._WG.Augments;
using Content.Shared.Actions;
using Content.Shared.MedicalScanner;
using Robust.Server.GameObjects;

namespace Content.Server._WG.Augments;

/// <summary>
/// Gives a body with an iatric monitor its scan action, which opens the health analyzer's window on the body itself
/// and closes it again.
/// </summary>
public sealed class WGIatricMonitorSystem : EntitySystem
{
    [Dependency] private HealthAnalyzerSystem _analyzer = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGIatricMonitorComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<WGIatricMonitorComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WGIatricMonitorComponent, WGIatricScanEvent>(OnScan);
        SubscribeLocalEvent<WGIatricScannerComponent, BoundUIClosedEvent>(OnClosed);
    }

    private void OnInit(Entity<WGIatricMonitorComponent> ent, ref ComponentInit args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<WGIatricMonitorComponent> ent, ref ComponentShutdown args)
    {
        QueueDel(ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }

    private void OnScan(Entity<WGIatricMonitorComponent> ent, ref WGIatricScanEvent args)
    {
        var action = args.Action.Owner;
        if (action != ent.Comp.ActionEntity || !TryComp<HealthAnalyzerComponent>(action, out var analyzer))
            return;

        args.Handled = true;
        if (_ui.IsUiOpen(action, HealthAnalyzerUiKey.Key, ent.Owner))
        {
            _ui.CloseUi(action, HealthAnalyzerUiKey.Key, ent.Owner);
            return;
        }

        _ui.OpenUi(action, HealthAnalyzerUiKey.Key, ent.Owner);
        _analyzer.BeginAnalyzingEntity((action, analyzer), ent);
    }

    private void OnClosed(Entity<WGIatricScannerComponent> ent, ref BoundUIClosedEvent args)
    {
        if (HealthAnalyzerUiKey.Key.Equals(args.UiKey)
            && TryComp<HealthAnalyzerComponent>(ent, out var analyzer)
            && analyzer.ScannedEntity is { } patient)
            _analyzer.StopAnalyzingEntity((ent, analyzer), patient);
    }
}
