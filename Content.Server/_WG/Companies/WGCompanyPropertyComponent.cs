namespace Content.Server._WG.Companies;

/// <summary>Something that came aboard a company hangar ship at launch; it sells for nothing.</summary>
[RegisterComponent]
public sealed partial class WGCompanyPropertyComponent : Component
{
    [DataField]
    public int CompanyId;
}
