using Content.Client.CartridgeLoader;
using Content.Client._WG.PdaUi; // WOLFGATE(PdaUi)
using Content.Shared._WG.PdaUi; // WOLFGATE(PdaUi)
using Content.Shared.CartridgeLoader;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.PDA;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.PDA
{
    [UsedImplicitly]
    public sealed class PdaBoundUserInterface : CartridgeLoaderBoundUserInterface
    {
        private readonly PdaSystem _pdaSystem;

        [ViewVariables]
        private PdaOsMenu? _menu; // WOLFGATE(PdaUi): TGUI-style shell

        public PdaBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
            _pdaSystem = EntMan.System<PdaSystem>();
        }

        protected override void Open()
        {
            base.Open();

            if (_menu == null)
                CreateMenu();
        }

        private void CreateMenu()
        {
            _menu = this.CreateWindowCenteredLeft<PdaOsMenu>(); // WOLFGATE(PdaUi): TGUI-style shell

            _menu.FlashLightToggleButton.OnToggled += _ =>
            {
                SendMessage(new PdaToggleFlashlightMessage());
            };

            _menu.EjectIdButton.OnPressed += _ =>
            {
                SendPredictedMessage(new ItemSlotButtonPressedEvent(PdaComponent.PdaIdSlotId));
            };

            _menu.EjectPenButton.OnPressed += _ =>
            {
                SendPredictedMessage(new ItemSlotButtonPressedEvent(PdaComponent.PdaPenSlotId));
            };

            _menu.EjectPaiButton.OnPressed += _ =>
            {
                SendPredictedMessage(new ItemSlotButtonPressedEvent(PdaComponent.PdaPaiSlotId));
            };

            // WOLFGATE(PdaStorage): eject the inserted data disk
            _menu.EjectDiskButton.OnPressed += _ =>
            {
                SendPredictedMessage(new ItemSlotButtonPressedEvent(PdaComponent.PdaDiskSlotId));
            };

            _menu.ActivateMusicButton.OnPressed += _ =>
            {
                SendMessage(new PdaShowMusicMessage());
            };

            _menu.AccessRingtoneButton.OnPressed += _ =>
            {
                SendMessage(new PdaShowRingtoneMessage());
            };

            _menu.ShowUplinkButton.OnPressed += _ =>
            {
                SendMessage(new PdaShowUplinkMessage());
            };

            _menu.LockUplinkButton.OnPressed += _ =>
            {
                SendMessage(new PdaLockUplinkMessage());
            };

            // WOLFGATE(PdaUi): program notification toggle
            _menu.NotificationsButton.OnPressed += _ =>
            {
                SendMessage(new PdaToggleNotificationsMessage());
            };

            // WOLFGATE(PdaUi) START: reopen the running program instead of asking the server again
            _menu.OnProgramItemPressed += uid =>
            {
                if (uid == ActiveProgram)
                {
                    var title = EntMan.GetComponentOrNull<CartridgeComponent>(uid)?.ProgramName;
                    _menu.ToProgramView(title is null
                        ? Loc.GetString("comp-pda-io-program-fallback-title")
                        : Loc.GetString(title));
                    return;
                }

                ActivateCartridge(uid);
            };
            // WOLFGATE END
            _menu.OnInstallButtonPressed += InstallCartridge;
            _menu.OnUninstallButtonPressed += UninstallCartridge;
            _menu.ProgramCloseButton.OnPressed += _ => DeactivateActiveCartridge();

            var borderColorComponent = GetBorderColorComponent();
            if (borderColorComponent == null)
                return;

            _menu.BorderColor = borderColorComponent.BorderColor;
            _menu.AccentHColor = borderColorComponent.AccentHColor;
            _menu.AccentVColor = borderColorComponent.AccentVColor;
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            if (state is not PdaUpdateState updateState)
                return;

            if (_menu == null)
            {
                _pdaSystem.Log.Error("PDA state received before menu was created.");
                return;
            }

            _menu.UpdateState(updateState);
        }

        protected override void AttachCartridgeUI(Control cartridgeUIFragment, string? title)
        {
            _menu?.ProgramView.AddChild(cartridgeUIFragment);
            // WOLFGATE(PdaUi) START: restyle the fragment's stock controls and title it with its own icon
            PdaAppTheme.Apply(cartridgeUIFragment);

            var programName = ActiveProgram is { } active
                ? RetrieveCartridgeComponent(active)?.ProgramName
                : null;

            _menu?.ToProgramView(
                title ?? Loc.GetString("comp-pda-io-program-fallback-title"),
                programName is null ? null : PdaProgramIcons.Get(programName));
            // WOLFGATE END
        }

        protected override void DetachCartridgeUI(Control cartridgeUIFragment)
        {
            if (_menu is null)
                return;

            _menu.ToHomeScreen();
            _menu.HideProgramHeader();
            _menu.ProgramView.RemoveChild(cartridgeUIFragment);
        }

        protected override void UpdateAvailablePrograms(List<(EntityUid, CartridgeComponent)> programs)
        {
            _menu?.UpdateAvailablePrograms(programs);
        }

        private PdaBorderColorComponent? GetBorderColorComponent()
        {
            return EntMan.GetComponentOrNull<PdaBorderColorComponent>(Owner);
        }
    }
}
