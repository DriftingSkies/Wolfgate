using Content.Client._WG.Administration.UI.SpawnOutfit; // WOLFGATE(Administration)
using Content.Client.Eui;
using Content.Shared.Administration;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client.Administration.UI.SetOutfit
{
    [UsedImplicitly]
    public sealed class SetOutfitEui : BaseEui
    {
        // WOLFGATE(Administration) START: set outfit uses the Spawn as Outfit picker in set mode
        // private readonly SetOutfitMenu _window;
        private readonly SpawnOutfitMenu _window;
        // WOLFGATE END
        private IEntityManager _entManager;

        public SetOutfitEui()
        {
            _entManager = IoCManager.Resolve<IEntityManager>();
            // WOLFGATE(Administration) START: set outfit uses the Spawn as Outfit picker in set mode
            // _window = new SetOutfitMenu();
            _window = new SpawnOutfitMenu(OutfitMenuMode.Set);
            // WOLFGATE END
            _window.OnClose += OnClosed;
        }

        private void OnClosed()
        {
            SendMessage(new CloseEuiMessage());
        }

        public override void Opened()
        {
            _window.OpenCentered();
        }

        public override void Closed()
        {
            base.Closed();
            _window.Close();
        }

        public override void HandleState(EuiStateBase state)
        {
            var outfitState = (SetOutfitEuiState) state;
            // WOLFGATE(Administration) START: set outfit uses the Spawn as Outfit picker in set mode
            // _window.TargetEntityId = outfitState.TargetNetEntity;
            _window.Target = outfitState.TargetNetEntity;
            // WOLFGATE END

        }
    }
}
