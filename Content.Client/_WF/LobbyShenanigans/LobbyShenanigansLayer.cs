using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Reflection;
using Robust.Shared.Timing;

namespace Content.Client._WF.LobbyShenanigans;

/// <summary>
/// Covers the lobby and runs every <see cref="LobbyShenanigan"/> over it when the server sets one off. Takes a click
/// only on something a shenanigan shows, so players can pick it up; everywhere else the lobby below gets it.
/// </summary>
public sealed class LobbyShenanigansLayer : Control
{
    [Dependency] private IReflectionManager _reflection = default!;
    [Dependency] private IDynamicTypeFactory _factory = default!;
    [Dependency] private IEntitySystemManager _systems = default!;
    [Dependency] private IInputManager _input = default!;

    // A long hitch would otherwise be simulated all at once.
    private const float MaxFrame = 0.1f;

    public readonly List<LobbyShenanigan> Shenanigans;
    private LobbyShenanigansSystem? _system;
    private LobbyShenanigan? _held;

    public LobbyShenanigansLayer()
    {
        IoCManager.InjectDependencies(this);
        MouseFilter = MouseFilterMode.Stop;
        Shenanigans = _reflection.GetAllChildren<LobbyShenanigan>()
            .Select(t => _factory.CreateInstance<LobbyShenanigan>(t))
            .OrderBy(s => s.DrawOrder)
            .ToList();
        foreach (var shenanigan in Shenanigans)
            shenanigan.All = Shenanigans;
    }

    private void OnTriggered(string id)
    {
        if (Shenanigans.FirstOrDefault(s => s.Id == id) is { } shenanigan)
            shenanigan.Trigger(PixelSize, UIScale);
    }

    private void OnCleared()
    {
        _held = null;
        foreach (var shenanigan in Shenanigans)
            shenanigan.Clear();
    }

    protected override bool HasPoint(Vector2 point)
    {
        var pixel = point * UIScale;
        return Shenanigans.Any(s => s.HitTest(pixel));
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;
        _held = Shenanigans.FirstOrDefault(s => s.TryGrab(args.RelativePixelPosition));
        if (_held != null)
            args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function != EngineKeyFunctions.UIClick || _held == null)
            return;
        _held.Release();
        _held = null;
        args.Handle();
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        if (!_systems.TryGetEntitySystem(out _system))
            return;
        _system.Triggered += OnTriggered;
        _system.Cleared += OnCleared;
    }

    // Kept from entering: on a disconnect the entity systems are gone before the lobby leaves the screen.
    protected override void ExitedTree()
    {
        base.ExitedTree();
        _held?.Release();
        _held = null;
        if (_system != null)
        {
            _system.Triggered -= OnTriggered;
            _system.Cleared -= OnCleared;
        }
        _system = null;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!VisibleInTree)
            return;
        var dt = MathF.Min(args.DeltaSeconds, MaxFrame);
        // Followed every frame, not only on mouse moves, so a fast flick cannot leave it behind.
        _held?.Drag(_input.MouseScreenPosition.Position - GlobalPixelPosition, dt);
        foreach (var shenanigan in Shenanigans)
            shenanigan.Update(dt, PixelSize, UIScale);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        foreach (var shenanigan in Shenanigans)
            shenanigan.Draw(handle, UIScale);
    }
}
