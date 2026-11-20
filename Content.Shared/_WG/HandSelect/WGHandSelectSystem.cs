using Content.Shared.ActionBlocker;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Shared._WG.HandSelect;

/// <summary>
/// "Swap to left hand" and "Swap to right hand": the key picks that side's hand outright, rather than cycling.
/// </summary>
public sealed class WGHandSelectSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(WGHandSelectKeys.WGSelectLeftHand,
                InputCmdHandler.FromDelegate(s => Select(s, HandLocation.Left), handle: false, outsidePrediction: false))
            .Bind(WGHandSelectKeys.WGSelectRightHand,
                InputCmdHandler.FromDelegate(s => Select(s, HandLocation.Right), handle: false, outsidePrediction: false))
            .Register<WGHandSelectSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<WGHandSelectSystem>();
    }

    private void Select(ICommonSession? session, HandLocation side)
    {
        if (session?.AttachedEntity is not { } user || !TryComp<HandsComponent>(user, out var hands))
            return;

        if (!_actionBlocker.CanInteract(user, null))
            return;

        TrySelect((user, hands), side);
    }

    /// <summary>Makes the first hand on <paramref name="side"/> active. False if the entity has none there.</summary>
    public bool TrySelect(Entity<HandsComponent> user, HandLocation side)
    {
        foreach (var hand in _hands.EnumerateHands(user, user.Comp))
        {
            if (hand.Location != side)
                continue;

            return _hands.TrySetActiveHand(user, hand.Name, user.Comp);
        }

        return false;
    }
}
