using System.Linq;
using Content.Client._WG.Companies;
using Content.Shared._WG.Companies;

namespace Content.Client.Lobby.UI;

/// <summary>Adds the character's player company to their entry in the character list.</summary>
public sealed partial class CharacterPickerButton
{
    private int? _companySlot;
    private string _description = string.Empty;
    private WGCompanyClientSystem? _companies;

    /// <summary>Follows the company of the character in this slot; call before the button enters the tree.</summary>
    public void ShowCompany(int slot)
    {
        _companySlot = slot;
        _description = DescriptionLabel.Text ?? string.Empty;
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        if (_companySlot == null)
            return;
        _companies = _entManager.System<WGCompanyClientSystem>();
        _companies.Received += OnCompanyState;
        if (_companies.State is { } state)
            OnCompanyState(state);
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        if (_companies != null)
            _companies.Received -= OnCompanyState;
    }

    private void OnCompanyState(WGCompanyState state)
    {
        var membership = state.Characters.FirstOrDefault(entry => entry.Slot == _companySlot);
        DescriptionLabel.Text = membership == null
            ? _description
            : _description + "\n" + Loc.GetString("wg-company-creator-member", ("company", membership.Company), ("tag", membership.Tag), ("rank", membership.Rank));
    }
}
