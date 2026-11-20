using System.Linq;
using Content.Shared.DoAfter;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Shared._WG.CancelInteractions;

/// <summary>
/// "Cancel interactions": stops every do-after the player is performing, such as using a tool or climbing.
/// Interactions other people perform on the player are on their own do-afters and aren't touched.
/// </summary>
public sealed class WGCancelInteractionSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(WGCancelInteractionKeys.WGCancelInteractions,
                InputCmdHandler.FromDelegate(OnCancelKey, handle: false, outsidePrediction: false))
            .Register<WGCancelInteractionSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<WGCancelInteractionSystem>();
    }

    private void OnCancelKey(ICommonSession? session)
    {
        if (session?.AttachedEntity is { } user)
            CancelAll(user);
    }

    /// <summary>Cancels every running do-after of <paramref name="user"/>. Returns how many were cancelled.</summary>
    public int CancelAll(EntityUid user)
    {
        if (!TryComp<DoAfterComponent>(user, out var comp))
            return 0;

        var cancelled = 0;
        foreach (var index in comp.DoAfters.Keys.ToList())
        {
            if (!_doAfter.IsRunning(user, index, comp))
                continue;

            _doAfter.Cancel(user, index, comp);
            cancelled++;
        }

        return cancelled;
    }
}
