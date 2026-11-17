using System.Linq;
using Content.Shared._WG.Companies;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Companies;

/// <summary>The join buttons for a company, shared by the Companies list and a company's profile.</summary>
public static class WGCompanyJoinActions
{
    /// <summary>Apply, cancel or join buttons for a company the character isn't in, or a note when they can't use them.</summary>
    public static Control? Build(WGCompanyClientSystem system, WGCompanyState? state, WGCompanyListing company)
    {
        if (state == null)
            return null;
        // A player has one character per company, so another character's company can't be joined.
        if (state.Characters.Any(character => character.CompanyId == company.Id))
            return new Label { Text = Loc.GetString("wg-company-same-player"), FontColorOverride = Color.FromHex("#C8C8C8") };
        if (state.Own != null || state.CharacterName == null)
            return null;
        var actions = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6, HorizontalExpand = true };
        if (company.Ranks?.FirstOrDefault(rank => rank.Id == company.OpenRankId) is { } openRank)
        {
            var join = new Button { Text = Loc.GetString("wg-company-join-open", ("rank", openRank.Title)), Disabled = !state.CanJoin, StyleClasses = { "ButtonAccept" } };
            join.OnPressed += _ => system.Send(new WGCompanyRequest(WGCompanyAction.JoinOpen) { CompanyId = company.Id });
            actions.AddChild(join);
        }
        if (state.Applications.Contains(company.Id))
        {
            var cancel = new Button { Text = Loc.GetString("wg-company-apply-cancel") };
            cancel.OnPressed += _ => system.Send(new WGCompanyRequest(WGCompanyAction.CancelApplication) { CompanyId = company.Id });
            actions.AddChild(cancel);
        }
        else
        {
            var apply = new Button { Text = Loc.GetString("wg-company-apply"), Disabled = !state.CanJoin };
            apply.OnPressed += _ => new WGCompanyApplyWindow(company, system.Send).OpenCentered();
            actions.AddChild(apply);
        }
        return actions;
    }
}
