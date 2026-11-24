using Content.Server.CartridgeLoader;
using Content.Server.PDA;
using Content.Shared._WG.PdaUi;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;

namespace Content.Server._WG.PdaUi;

/// <summary>
/// Lets the PDA settings view turn program notifications on and off.
/// The flag lives on <see cref="CartridgeLoaderComponent"/> and gates
/// <see cref="CartridgeLoaderSystem.SendNotification"/>.
/// </summary>
public sealed class PdaNotificationsSystem : EntitySystem
{
    [Dependency] private PdaSystem _pda = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PdaComponent, PdaToggleNotificationsMessage>(OnToggle);
    }

    private void OnToggle(EntityUid uid, PdaComponent pda, PdaToggleNotificationsMessage args)
    {
        if (!PdaUiKey.Key.Equals(args.UiKey))
            return;

        if (!TryComp<CartridgeLoaderComponent>(uid, out var loader))
            return;

        loader.NotificationsEnabled = !loader.NotificationsEnabled;
        _pda.UpdatePdaUi(uid, pda);
    }
}
