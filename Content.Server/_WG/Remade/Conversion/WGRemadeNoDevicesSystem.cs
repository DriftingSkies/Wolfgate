using Content.Server.Shuttles.Components;
using Content.Shared._Mono.FireControl;
using Content.Shared._WG.Remade;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Content.Shared.UserInterface;

namespace Content.Server._WG.Remade.Conversion;

/// <summary>Keeps the Remade off PDAs, helms and gunnery consoles.</summary>
public sealed class WGRemadeNoDevicesSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGRemadeNoDevicesComponent, UserOpenActivatableUIAttemptEvent>(OnOpenAttempt);
    }

    private void OnOpenAttempt(EntityUid uid, WGRemadeNoDevicesComponent component, UserOpenActivatableUIAttemptEvent args)
    {
        if (args.Cancelled || !IsDevice(args.Target))
            return;

        args.Cancel();
        _popup.PopupEntity(Loc.GetString("wg-remade-no-devices"), uid, uid);
    }

    /// <summary>Whether a Remade is kept off this.</summary>
    public bool IsDevice(EntityUid target)
    {
        return HasComp<PdaComponent>(target)
            || HasComp<ShuttleConsoleComponent>(target)
            || HasComp<FireControlConsoleComponent>(target);
    }
}
