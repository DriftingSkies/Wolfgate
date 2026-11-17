// SPDX-FileCopyrightText: Copyright (c) 2025 Space Wizards Federation
// SPDX-License-Identifier: MIT
// WOLFGATE(Genitals): ported from HardLight (consent system)

using Content.Shared._Common.Consent;
using Content.Shared._WG.Prototypes; // WOLFGATE(Prototypes)
using Content.Server.Database;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Server._Common.Consent;

public static class PlayerConsentSettingsExtensions
{
    public static PlayerConsentSettings ToPlayerConsentSettings(this ConsentSettings dbConsentSettings)
    {
        return new(dbConsentSettings.ConsentFreetext ?? "", (dbConsentSettings.ConsentToggles ?? new()).ToDictionary(
            // WOLFGATE(Prototypes): rows saved before a toggle rename load under its current id.
            keySelector: t => new ProtoId<ConsentTogglePrototype>(WGLegacyPrototypeIds.Resolve(WGLegacyPrototypeIds.ConsentToggles, t.ToggleProtoId)),
            elementSelector: t => t.ToggleProtoState
        ));
    }

    public static ConsentSettings ToDbObject(this PlayerConsentSettings playerConsentSettings)
    {
        return new()
        {
            ConsentFreetext = playerConsentSettings.Freetext,
            ConsentToggles = playerConsentSettings.Toggles
                .Select(x => new ConsentToggle {
                    ToggleProtoId = x.Key,
                    ToggleProtoState = x.Value,
                })
                .ToList()
        };
    }
}
