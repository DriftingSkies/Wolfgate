using Content.Shared._WG.WornStorage;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Options.UI.Tabs;

public sealed partial class KeyRebindTab
{
    private void HandleToggleWornStorageClick(BaseButton.ButtonToggledEventArgs args)
    {
        _cfg.SetCVar(WornStorageCVars.ClickOpens, args.Pressed);
        _cfg.SaveToFile();
    }
}
