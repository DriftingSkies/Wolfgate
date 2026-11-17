using System.Collections.Generic;
using Content.Shared._Common.Consent;
using Content.Shared._WG.Genitals.Prototypes;
using Content.Shared._WG.Prototypes;
using Content.Shared._WG.ShipPa;
using Content.Shared.HUD;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Prototypes;

/// <summary>Every legacy id maps to an existing prototype of its kind, and no old id still exists.</summary>
[TestFixture]
[TestOf(typeof(WGLegacyPrototypeIds))]
public sealed class WGLegacyPrototypeIdsTest
{
    [Test]
    public async Task TargetsExistTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();

        Assert.Multiple(() =>
        {
            Check<ConsentTogglePrototype>(proto, WGLegacyPrototypeIds.ConsentToggles);
            Check<GenitalShapePrototype>(proto, WGLegacyPrototypeIds.GenitalShapes);
            Check<HudThemePrototype>(proto, WGLegacyPrototypeIds.HudThemes);
            Check<LoadoutPrototype>(proto, WGLegacyPrototypeIds.Loadouts);
            Check<ShipAlertCodePrototype>(proto, WGLegacyPrototypeIds.ShipAlertCodes);
            Check<SpeciesPrototype>(proto, WGLegacyPrototypeIds.Species);
        });

        await pair.CleanReturnAsync();
    }

    private static void Check<T>(IPrototypeManager proto, IReadOnlyDictionary<string, string> table) where T : class, IPrototype
    {
        foreach (var (old, current) in table)
        {
            Assert.That(proto.HasIndex<T>(current), $"{typeof(T).Name} {old} maps to missing {current}.");
            Assert.That(proto.HasIndex<T>(old), Is.False, $"{typeof(T).Name} {old} exists again, so its remap hides it.");
        }
    }
}
