using Content.Server._WG.RoundSetup;
using Content.Shared.Radio;
using Robust.Shared.Utility;

namespace Content.Server.Radio.EntitySystems;

public sealed partial class RadioSystem
{
    /// <summary>Preserves normal channel labels while naming each isolated crew channel.</summary>
    private string WGCrewChannelName(EntityUid source, RadioChannelPrototype channel) => channel.ID == WGRoundSetupSystem.CrewChannel
        ? FormattedMessage.EscapeText(EntityManager.System<WGRoundSetupSystem>().CrewRadioName(source))
        : WGCompanyChannelName(source, channel);
}
