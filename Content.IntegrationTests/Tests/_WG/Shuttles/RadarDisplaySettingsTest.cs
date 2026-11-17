#nullable enable annotations

using Content.IntegrationTests.Tests._WG.EngineCompat;
using System.Reflection;
using Content.Client._Mono.FireControl.UI;
using Content.Client.Shuttles.UI;
using Content.Shared._WG.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._WG.Shuttles;

/// <summary>Checks a radar display choice made on one radar screen is what the next radar screen opens with.</summary>
[TestFixture]
public sealed class RadarDisplaySettingsTest
{
    private static readonly FieldInfo Azimuth =
        typeof(ShuttleNavControl).GetField("_azimuthMode", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Test]
    public async Task AChoiceMadeOnOneRadarIsKeptForTheNext()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var cfg = client.ResolveDependency<IConfigurationManager>();
        var ui = client.ResolveDependency<IUserInterfaceManager>();
        var original = (cfg.GetCVar(WolfgateCVars.RadarShowIff), cfg.GetCVar(WolfgateCVars.RadarShowIffDetailed),
            cfg.GetCVar(WolfgateCVars.RadarAzimuth));
        RadarConsoleWindow scanner = default!;
        try
        {
            await client.WaitAssertion(() =>
            {
                cfg.SetCVar(WolfgateCVars.RadarShowIff, true);
                cfg.SetCVar(WolfgateCVars.RadarShowIffDetailed, true);
                cfg.SetCVar(WolfgateCVars.RadarAzimuth, 0);

                scanner = new RadarConsoleWindow();
                scanner.OpenCentered();
                var radar = scanner.FindControl<ShuttleNavControl>("RadarScreen");
                var detailed = scanner.FindControl<Button>("IFFDetailedToggle");
                // Every radar marks docks.
                Assert.That((radar.ShowIFF, radar.ShowIFFDetailed, radar.ShowDocks), Is.EqualTo((true, true, true)));
                Assert.That(detailed.Pressed, Is.True);

                // The mass scanner's own handler, as a click on the toggle runs it.
                radar.ShowIFFDetailed = false;
                Azimuth.SetValue(radar, Enum.ToObject(Azimuth.FieldType, 2));
            });
            await pair.RunTicksSync(5);
            await client.WaitAssertion(() =>
            {
                Assert.That(cfg.GetCVar(WolfgateCVars.RadarShowIffDetailed), Is.False, "Turning detailed IFF off must be saved.");
                Assert.That(cfg.GetCVar(WolfgateCVars.RadarShowIff), Is.True);
                Assert.That(cfg.GetCVar(WolfgateCVars.RadarAzimuth), Is.EqualTo(2), "The bearing marks' mode must be saved.");
                scanner.Close();

                using var reopened = new RadarConsoleWindow();
                var radar = reopened.FindControl<ShuttleNavControl>("RadarScreen");
                Assert.That(radar.ShowIFFDetailed, Is.False, "A reopened mass scanner must keep detailed IFF off.");
                Assert.That(reopened.FindControl<Button>("IFFDetailedToggle").Pressed, Is.False, "Its toggle must show that.");
                Assert.That(reopened.FindControl<Button>("IFFToggle").Pressed, Is.True);
                Assert.That(Convert.ToInt32(Azimuth.GetValue(radar)), Is.EqualTo(2));

                using var helm = new NavScreen();
                Assert.That(helm.FindControl<ShuttleNavControl>("NavRadar").ShowIFFDetailed, Is.False, "The helm shares the choice.");
                Assert.That(helm.FindControl<Button>("IFFDetailedToggle").Pressed, Is.False);
                Assert.That(helm.FindControl<Button>("IFFToggle").Pressed, Is.True);

                using var gunnery = new FireControlWindow();
                Assert.That(gunnery.FindControl<ShuttleNavControl>("NavRadar").ShowIFFDetailed, Is.False,
                    "The gunnery console shares the choice.");
                Assert.That(gunnery.FindControl<Button>("IFFDetailedToggle").Pressed, Is.False);
                Assert.That(helm.FindControl<ShuttleNavControl>("NavRadar").ShowDocks, Is.True, "The helm marks docks.");
                Assert.That(gunnery.FindControl<ShuttleNavControl>("NavRadar").ShowDocks, Is.True, "So does gunnery.");
            });
        }
        finally
        {
            await client.WaitPost(() =>
            {
                scanner?.Teardown();
                cfg.SetCVar(WolfgateCVars.RadarShowIff, original.Item1);
                cfg.SetCVar(WolfgateCVars.RadarShowIffDetailed, original.Item2);
                cfg.SetCVar(WolfgateCVars.RadarAzimuth, original.Item3);
            });
        }

        await pair.CleanReturnAsync();
    }
}
