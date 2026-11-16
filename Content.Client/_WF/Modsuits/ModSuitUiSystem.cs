using Content.Shared._WF.Modsuits;
using Content.Shared._WF.Modsuits.Components;

namespace Content.Client._WF.Modsuits;

public sealed partial class ModSuitUiSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModSuitComponent, AfterAutoHandleStateEvent>(OnSuitState);
        SubscribeLocalEvent<ModSuitComponent, ModSuitUpdatedEvent>(OnSuitUpdated);
        SubscribeLocalEvent<ModModuleComponent, AfterAutoHandleStateEvent>(OnModuleState);
    }

    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    private readonly HashSet<EntityUid> _dirty = new();

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_dirty.Count == 0)
            return;

        foreach (var suit in _dirty)
        {
            if (_ui.TryGetOpenUi(suit, ModSuitUiKey.Key, out var bui))
                bui.Update();
        }

        _dirty.Clear();
    }
    private void OnSuitState(Entity<ModSuitComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _dirty.Add(ent);
    }
    private void OnSuitUpdated(Entity<ModSuitComponent> ent, ref ModSuitUpdatedEvent args)
    {
        _dirty.Add(ent);
    }
    private void OnModuleState(Entity<ModModuleComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (ent.Comp.Suit is { } suit)
            _dirty.Add(suit);
    }
}
