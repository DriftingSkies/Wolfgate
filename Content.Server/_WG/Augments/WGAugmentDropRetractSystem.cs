using Content.Shared._Goobstation.MantisBlades;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Input;
using Content.Shared.Interaction.Components;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WG.Augments;

/// <summary>Folds an arm augment's item back in when its owner presses drop with it in the active hand.</summary>
public sealed class WGAugmentDropRetractSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private WGDeployableImplantSystem _deployable = default!;

    public override void Initialize()
    {
        base.Initialize();
        CommandBinds.Builder
            .BindBefore(ContentKeyFunctions.Drop, new PointerInputCmdHandler(OnDropPressed), typeof(SharedHandsSystem))
            .Register<WGAugmentDropRetractSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<WGAugmentDropRetractSystem>();
    }

    private bool OnDropPressed(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (session?.AttachedEntity is not { } user
            || !_hands.TryGetActiveItem(user, out var item)
            || !HasComp<UnremoveableComponent>(item))
            return false;

        return TryRetract(user, item.Value);
    }

    /// <summary>Retracts the item if an augment in the body put it out, and returns whether one did.</summary>
    public bool TryRetract(EntityUid body, EntityUid item)
    {
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (TryComp<MantisBladeArmComponent>(part, out var mantis) && mantis.BladeUid == item)
            {
                // The arm's own toggle retracts the blade it holds, with its sound and EMP check.
                RaiseLocalEvent(part, new ToggleMantisBladeEvent());
                return true;
            }

            if (_deployable.TryStowItem(part, item))
                return true;
        }

        return false;
    }
}
