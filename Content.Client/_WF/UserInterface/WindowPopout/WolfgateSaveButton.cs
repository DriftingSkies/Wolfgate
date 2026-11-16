using Content.Client.Resources;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.UserInterface.WindowPopout;

/// <summary>
/// Title bar button that saves the chat log to a file.
/// </summary>
public sealed partial class WolfgateSaveButton : TextureButton
{
    public const string StyleClassSaveButton = "saveButton";

    private const string SaveTexture = "/Textures/_WF/UserInterface/Interface/Window/save.png";

    [Dependency] private IResourceCache _cache = default!;

    public WolfgateSaveButton()
    {
        IoCManager.InjectDependencies(this);
        AddStyleClass(StyleClassSaveButton);
        VerticalAlignment = VAlignment.Center;
        Margin = new Thickness(0, 0, 4, 0);
        TextureNormal = _cache.GetTexture(SaveTexture);
        ToolTip = Loc.GetString("wf-chat-tabs-save");
    }
}
