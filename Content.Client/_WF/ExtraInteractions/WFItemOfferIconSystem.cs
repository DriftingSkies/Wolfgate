using Content.Shared._WF.ExtraInteractions;
using Content.Shared.CombatMode;
using Content.Shared.Interaction;
using Robust.Client.Graphics;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Client._WF.ExtraInteractions;

/// <summary>
/// Draws each offered item in front of its offerer and turns a click on it into a take request.
/// </summary>
public sealed class WFItemOfferIconSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFItemOfferSystem _offer = default!;

    /// <summary>How far from the icon's centre a click still lands on it, in tiles.</summary>
    private const float ClickRadius = 0.4f;

    public override void Initialize()
    {
        base.Initialize();

        _overlays.AddOverlay(new WFItemOfferOverlay(EntityManager));

        // A local handler that returns true keeps the click from the server, so it doesn't also act on what lies under the icon.
        CommandBinds.Builder
            .BindBefore(EngineKeyFunctions.Use, new PointerInputCmdHandler(OnUse), typeof(SharedInteractionSystem))
            .Register<WFItemOfferIconSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<WFItemOfferOverlay>();
        CommandBinds.Unregister<WFItemOfferIconSystem>();
    }

    private bool OnUse(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (session?.AttachedEntity is not { } user || _combat.IsInCombatMode(user) || !coords.IsValid(EntityManager))
            return false;

        var click = _transform.ToMapCoordinates(coords);
        var query = EntityQueryEnumerator<WFItemOfferComponent, TransformComponent>();
        while (query.MoveNext(out var offerer, out _, out var xform))
        {
            if (offerer == user || xform.MapID != click.MapId)
                continue;

            if ((_offer.GetIconPosition(offerer) - click.Position).Length() > ClickRadius)
                continue;

            RaisePredictiveEvent(new WFTakeOfferedItemEvent(GetNetEntity(offerer)));
            return true;
        }

        return false;
    }
}
