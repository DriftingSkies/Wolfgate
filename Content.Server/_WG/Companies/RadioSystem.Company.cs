using Content.Server._WG.Companies;
using Content.Shared.Radio;
using Robust.Shared.Utility;

namespace Content.Server.Radio.EntitySystems;

public sealed partial class RadioSystem
{
    /// <summary>Names each company's isolated channel after the speaker's company.</summary>
    private string WGCompanyChannelName(EntityUid source, RadioChannelPrototype channel) => channel.ID == WGCompanySystem.CompanyChannel
        ? FormattedMessage.EscapeText(EntityManager.System<WGCompanySystem>().CompanyRadioName(source))
        : channel.LocalizedName;
}
