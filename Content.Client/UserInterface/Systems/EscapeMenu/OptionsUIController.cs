using Content.Client.Options.UI;
using JetBrains.Annotations;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Console;

namespace Content.Client.UserInterface.Systems.EscapeMenu;

[UsedImplicitly]
public sealed partial class OptionsUIController : UIController
{
    [Dependency] private IConsoleHost _con = default!;

    public override void Initialize()
    {
        _con.RegisterCommand("options", Loc.GetString("cmd-options-desc"), Loc.GetString("cmd-options-help"), OptionsCommand);
    }

    private void OptionsCommand(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            ToggleWindow();
            return;
        }
        OpenWindow();

        if (!int.TryParse(args[0], out var tab))
        {
            shell.WriteError(Loc.GetString("cmd-parse-failure-int", ("arg", args[0])));
            return;
        }

        // WOLFGATE(Options) START: the stock tabs now sit on the SS14 master tab
        // _optionsWindow.Tabs.CurrentTab = tab;
        _optionsWindow.WgShowStockTab(tab);
        // WOLFGATE END
    }

    private OptionsMenu _optionsWindow = default!;

    private void EnsureWindow()
    {
        if (_optionsWindow is { Disposed: false })
            return;

        _optionsWindow = UIManager.CreateWindow<OptionsMenu>();
    }

    public void OpenWindow()
    {
        EnsureWindow();

        _optionsWindow.UpdateTabs();

        _optionsWindow.OpenCentered();
        _optionsWindow.MoveToFront();
    }

    // WOLFGATE(Options) START: open the options window with a given Wolfgate tab selected
    /// <summary>Opens the options window on one of the Wolfgate tabs, such as <see cref="OptionsMenu.ConsentTabIndex"/>.</summary>
    public void OpenWindow(int tab)
    {
        OpenWindow();
        _optionsWindow.WgShowWolfgateTab(tab);
    }
    // WOLFGATE END

    public void ToggleWindow()
    {
        EnsureWindow();

        if (_optionsWindow.IsOpen)
        {
            _optionsWindow.Close();
        }
        else
        {
            OpenWindow();
        }
    }
}
