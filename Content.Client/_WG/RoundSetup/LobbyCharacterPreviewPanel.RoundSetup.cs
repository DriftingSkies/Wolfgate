using Robust.Client.UserInterface;

namespace Content.Client.Lobby.UI;

public sealed partial class LobbyCharacterPreviewPanel
{
    /// <summary>Fits the existing character preview into the preparation card.</summary>
    public void UseRoundSetupCard()
    {
        Loaded.SetWidth = 220;
        ViewBox.Margin = new Thickness(8);
        Summary.HorizontalAlignment = HAlignment.Stretch;
        Company.HorizontalAlignment = HAlignment.Stretch;
        CharacterSetup.Visible = false;
    }
}
