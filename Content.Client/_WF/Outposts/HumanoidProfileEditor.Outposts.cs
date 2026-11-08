using Content.Client._WF.Outposts;
using Content.Shared._WF.Outposts;
using Content.Shared.Clothing;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI;

/// <summary>
/// The Outposts tab: this character's settler loadout and title, and the account's outpost saves, shared by all its
/// characters.
/// </summary>
public sealed partial class HumanoidProfileEditor
{
    private void InitializeOutposts()
    {
        var tab = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        if (_prototypeManager.TryIndex<JobPrototype>(WFOutpostLimits.SettlerJob, out var settler))
            tab.AddChild(BuildSettlerRow(settler));
        tab.AddChild(new WFOutpostSavesPanel { VerticalExpand = true });
        TabContainer.AddChild(tab);
        TabContainer.SetTabTitle(tab.GetPositionInParent(), Loc.GetString("wf-outpost-editor-tab"));
    }

    private BoxContainer BuildSettlerRow(JobPrototype settler)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            Margin = new Thickness(8, 8, 8, 0),
        };
        row.AddChild(new Label
        {
            Text = Loc.GetString("wf-outpost-editor-settler", ("job", settler.LocalizedName)),
            HorizontalExpand = true,
            ClipText = true,
        });

        var loadout = new Button { Text = Loc.GetString("loadout-window") };
        loadout.OnPressed += _ => OpenSettlerLoadout(settler);
        row.AddChild(loadout);
        AddCustomJobTitleButton(settler, row);
        return row;
    }

    /// <summary>Opens this character's settler loadout, as the Jobs tab does for a job.</summary>
    private void OpenSettlerLoadout(JobPrototype settler)
    {
        var id = LoadoutSystem.GetJobPrototype(settler.ID);
        if (!_prototypeManager.TryIndex<RoleLoadoutPrototype>(id, out var proto))
            return;

        RoleLoadout? loadout = null;
        Profile?.Loadouts.TryGetValue(id, out loadout);
        loadout = loadout?.Clone();
        if (loadout == null)
        {
            loadout = new RoleLoadout(proto.ID);
            loadout.SetDefault(Profile, _playerManager.LocalSession, _prototypeManager);
        }

        OpenLoadout(settler, loadout, proto);
    }
}
