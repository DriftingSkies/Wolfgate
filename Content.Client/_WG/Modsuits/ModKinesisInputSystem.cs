using System.Numerics;
using Content.Client.Construction;
using Content.Client.Interaction;
using Content.Client.UserInterface.Systems.Actions;
using Content.Shared._WG.Modsuits.Modules;
using Content.Shared.Interaction;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._WG.Modsuits;

public sealed partial class ModKinesisInputSystem : EntitySystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    private const float CursorTolerance = 0.1f;

    private static readonly Color HighlightColor = Color.FromHex("#9ad8ff");
    private const float HighlightBlend = 0.5f;

    private bool _holding;
    private Vector2? _lastCursor;
    private readonly Dictionary<EntityUid, Color> _highlighted = new();
    private readonly List<EntityUid> _stale = new();

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(new CommandBind(EngineKeyFunctions.Use,
                new PointerInputCmdHandler(OnUse, ignoreUp: false, outsidePrediction: true),
                before: new[] { typeof(DragDropSystem), typeof(SharedInteractionSystem), typeof(ConstructionSystem) },
                after: new[] { typeof(ActionUIController) }))
            .Register<ModKinesisInputSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        CommandBinds.Unregister<ModKinesisInputSystem>();
    }

    private bool OnUse(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (_player.LocalEntity is not { } player
            || !HasComp<ModKinesisUserComponent>(player)
            || _ui.GetUIController<ActionUIController>().SelectingTargetFor != null)
            return false;

        if (args.State == BoundKeyState.Down)
        {
            if (args.EntityUid.IsValid() && args.EntityUid != player)
            {
                _holding = true;
                _lastCursor = null;
                RaiseNetworkEvent(new ModKinesisGrabEvent
                {
                    Target = GetNetEntity(args.EntityUid),
                    Cursor = GetNetCoordinates(args.Coordinates),
                });
            }

            return true;
        }

        if (_holding)
        {
            _holding = false;
            RaiseNetworkEvent(new ModKinesisReleaseEvent());
        }

        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted || !_holding)
            return;

        if (_player.LocalEntity is not { } player || !HasComp<ModKinesisUserComponent>(player))
        {
            _holding = false;
            return;
        }

        if (!_input.MouseScreenPosition.IsValid)
            return;

        var mouse = _eye.PixelToMap(_input.MouseScreenPosition);
        if (mouse.MapId == MapId.Nullspace)
            return;

        if (_lastCursor is { } last && (last - mouse.Position).Length() < CursorTolerance)
            return;

        _lastCursor = mouse.Position;
        RaiseNetworkEvent(new ModKinesisCursorEvent
        {
            Cursor = GetNetCoordinates(_transform.ToCoordinates(mouse)),
        });
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<ModKinesisHeldComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite))
        {
            if (_highlighted.ContainsKey(uid))
                continue;

            _highlighted[uid] = sprite.Color;
            var tint = Color.InterpolateBetween(sprite.Color, HighlightColor, HighlightBlend);
            _sprite.SetColor((uid, sprite), tint.WithAlpha(sprite.Color.A));
        }

        _stale.Clear();
        foreach (var uid in _highlighted.Keys)
        {
            if (!HasComp<ModKinesisHeldComponent>(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            var color = _highlighted[uid];
            _highlighted.Remove(uid);
            if (_spriteQuery.TryComp(uid, out var sprite))
                _sprite.SetColor((uid, sprite), color);
        }
    }
}
