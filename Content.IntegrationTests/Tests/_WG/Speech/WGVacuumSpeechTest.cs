using Content.Server.Atmos.EntitySystems;
using Content.Server.Chat.Systems;
using Content.Shared._EinsteinEngines.Language;
using Content.Shared._WG.CCVar;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Speech;

/// <summary>Speech only carries as a whisper where the air is too thin, whatever the speaker wears; sign is never muffled.</summary>
[TestFixture]
public sealed class WGVacuumSpeechTest
{
    [Test]
    public async Task ThinAirMuffles()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        // The test pool turns the rule off, since test maps have no air.
        await server.WaitPost(() => server.CfgMan.SetCVar(SpeechCVars.VacuumWhisper, true));
        await server.WaitAssertion(() =>
        {
            var chat = server.System<ChatSystem>();
            var atmos = server.System<AtmosphereSystem>();
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var spoken = prototypes.Index<LanguagePrototype>("TauCetiBasic");
            var sign = prototypes.Index<LanguagePrototype>("Sign");
            var mob = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(chat.WgSpeechMuffledByVacuum(mob, spoken), Is.True, "With no air a voice only reaches whisper range.");
            Assert.That(chat.WgSpeechMuffledByVacuum(mob, sign), Is.False, "Sign needs no air.");

            atmos.SetMapAtmosphere(map.MapUid, false, Air(Atmospherics.OneAtmosphere));
            Assert.That(chat.WgSpeechMuffledByVacuum(mob, spoken), Is.False, "Normal air carries a normal voice.");

            atmos.SetMapAtmosphere(map.MapUid, false, Air(Atmospherics.HazardLowPressure / 2f));
            Assert.That(chat.WgSpeechMuffledByVacuum(mob, spoken), Is.True, "Air below hazard pressure only carries a whisper.");

            server.CfgMan.SetCVar(SpeechCVars.VacuumWhisper, false);
            Assert.That(chat.WgSpeechMuffledByVacuum(mob, spoken), Is.False, "With the rule off nothing is muffled.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Room-temperature air at about <paramref name="kilopascals"/>.</summary>
    private static GasMixture Air(float kilopascals)
    {
        var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
        var share = kilopascals / Atmospherics.OneAtmosphere;
        air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard * share);
        air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard * share);
        return air;
    }
}
