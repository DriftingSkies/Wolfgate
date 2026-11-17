using System.Linq;
using Content.Client.Guidebook.Controls;
using Content.Client.UserInterface.Systems.Guidebook;
using Content.Client.UserInterface.Systems.Info;
using Content.Shared._WG.Newbie;
using Content.Shared.Guidebook;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.Newbie;

/// <summary>
/// Opens the welcome popup, unless the player turned it off, then the crew popup for a player on their first hour.
/// </summary>
public sealed partial class WGNewbieWelcomeSystem : EntitySystem
{
    private static readonly ProtoId<GuideEntryPrototype> RuleEntry = "WGRuleConflictSixNewPlayers";

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    private WGNewbieWelcomeWindow? _welcome;
    private WGNewbieCrewWindow? _crew;
    private bool _shuttingDown;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGNewbieWelcomeEvent>(OnWelcome);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _shuttingDown = true;
        _welcome?.Close();
        _crew?.Close();
    }

    private void OnWelcome(WGNewbieWelcomeEvent ev)
    {
        if (!_cfg.GetCVar(WGNewbieCVars.ShowWelcome))
        {
            if (ev.Fresh)
                OpenCrew();
            return;
        }

        _welcome?.Close();
        var window = _welcome = _ui.CreateWindow<WGNewbieWelcomeWindow>();
        window.OnOpenRules += OpenRule;
        window.OnClose += () => OnWelcomeClosed(window, ev.Fresh);
        window.OpenCentered();
    }

    private void OnWelcomeClosed(WGNewbieWelcomeWindow window, bool fresh)
    {
        if (window.DontShowAgain)
        {
            _cfg.SetCVar(WGNewbieCVars.ShowWelcome, false);
            _cfg.SaveToFile();
        }

        if (_welcome == window)
            _welcome = null;

        // The crew advice comes after the welcome, so a new player never has two popups at once.
        if (fresh && !_shuttingDown)
            OpenCrew();
    }

    /// <summary>
    /// Opens the guidebook on the new player rule. Single rules aren't in the guidebook's tree, so it opens on the
    /// ruleset and follows the rule's link from there, the way the ruleset page does.
    /// </summary>
    private void OpenRule()
    {
        var ruleset = _ui.GetUIController<InfoUIController>().GetCoreRuleEntry().ID;
        _ui.GetUIController<GuidebookUIController>().OpenGuidebook(selected: ruleset);
        _ui.WindowRoot.Children.OfType<GuidebookWindow>().FirstOrDefault()?.HandleClick(RuleEntry);
    }

    private void OpenCrew()
    {
        _crew?.Close();
        _crew = _ui.CreateWindow<WGNewbieCrewWindow>();
        _crew.OnClose += () => _crew = null;
        _crew.OpenCentered();
    }
}
