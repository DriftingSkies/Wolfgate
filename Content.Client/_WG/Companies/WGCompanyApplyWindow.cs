using System.Numerics;
using Content.Shared._WG.Companies;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._WG.Companies;

/// <summary>The application form: the rank asked for and why the character wants to join.</summary>
public sealed class WGCompanyApplyWindow : DefaultWindow
{
    public WGCompanyApplyWindow(WGCompanyListing company, Action<WGCompanyRequest> send)
    {
        Title = Loc.GetString("wg-company-apply-title", ("company", company.Name));
        MinSize = new Vector2(460, 360);
        var ranks = company.Ranks ?? Array.Empty<WGCompanyOpenRank>();
        var layout = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, Margin = new Thickness(10) };

        layout.AddChild(new Label { Text = Loc.GetString("wg-company-apply-rank") });
        var rank = new OptionButton { HorizontalExpand = true };
        rank.AddItem(Loc.GetString("wg-company-apply-any-rank"), 0);
        for (var i = 0; i < ranks.Length; i++)
            rank.AddItem(ranks[i].Title, i + 1);
        // Applicants usually start at the bottom, so the lowest rank is the default.
        rank.SelectId(ranks.Length);
        rank.OnItemSelected += args => rank.SelectId(args.Id);
        layout.AddChild(rank);

        layout.AddChild(new Label { Text = Loc.GetString("wg-company-apply-message") });
        var message = new TextEdit { MinHeight = 140, VerticalExpand = true, HorizontalExpand = true,
            Placeholder = new Rope.Leaf(Loc.GetString("wg-company-apply-placeholder")) };
        layout.AddChild(message);
        var count = new Label { FontColorOverride = Color.Gray };
        layout.AddChild(count);
        var submit = new Button { Text = Loc.GetString("wg-company-apply-send") };
        submit.AddStyleClass("ButtonAccept");
        layout.AddChild(submit);

        void Count()
        {
            var length = Rope.Collapse(message.TextRope).Trim().Length;
            count.Text = Loc.GetString("wg-company-apply-count", ("count", length), ("max", WGCompanyPolicy.MaximumApplicationLength));
            submit.Disabled = length > WGCompanyPolicy.MaximumApplicationLength;
        }
        message.OnTextChanged += _ => Count();
        Count();
        submit.OnPressed += _ =>
        {
            send(new WGCompanyRequest(WGCompanyAction.Apply)
            {
                CompanyId = company.Id,
                RankId = rank.SelectedId == 0 ? null : ranks[rank.SelectedId - 1].Id,
                Description = Rope.Collapse(message.TextRope),
            });
            Close();
        };
        Contents.AddChild(layout);
    }
}
