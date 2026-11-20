#nullable enable
using Content.Server._WG.AdminObserver;
using Content.Shared._WG.AdminObserver;
using Content.Shared.Chat;
using Content.Shared.Examine;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.AdminObserver;

/// <summary>The part of the staff marker nobody can switch off: the examine line, the name tag and one visual.</summary>
[TestFixture]
[TestOf(typeof(WGStaffMarkerSystem))]
public sealed class StaffMarkerFloorTest
{
    [Test]
    public async Task MarkedEntitySaysSoEverywhere()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var marker = server.System<WGStaffMarkerSystem>();
        var examine = server.System<ExamineSystemShared>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var marked = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var onlooker = entMan.SpawnEntity("MobHuman", map.GridCoords);

            // Wording: the rank by default, "Staff" without one, the account name only when asked for.
            Assert.Multiple(() =>
            {
                Assert.That(marker.Label(WGMarkerWording.Rank, "Head Admin", "moon"), Is.EqualTo("Head Admin"));
                Assert.That(marker.Label(WGMarkerWording.Rank, null, "moon"), Is.EqualTo("Staff"));
                Assert.That(marker.Label(WGMarkerWording.Staff, "Head Admin", "moon"), Is.EqualTo("Staff"));
                Assert.That(marker.Label(WGMarkerWording.Name, "Head Admin", "moon"), Is.EqualTo("moon"));
                Assert.That(marker.Label(WGMarkerWording.RankAndName, "Head Admin", "moon"), Is.EqualTo("Head Admin moon"));
            });

            // A marker asked for with no visual still gets one, and the colour follows the Host flag, not the request.
            var settings = new WGStaffMarkerSettings { Visuals = WGMarkerVisuals.None, Color = "#00ff00" };
            var comp = marker.Mark(marked, settings, "Head Admin", "moon", host: false);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Visuals, Is.Not.EqualTo(WGMarkerVisuals.None));
                Assert.That(comp.Label, Is.EqualTo("Head Admin"));
                Assert.That(comp.Color, Is.EqualTo(WGStaffMarkerComponent.StaffColor));
                Assert.That(marker.Mark(marked, settings, "Host", "moon", host: true).Color, Is.EqualTo(Color.FromHex("#00ff00")));
                Assert.That(WGStaffMarkerSystem.MarkerColor(null, true), Is.EqualTo(WGStaffMarkerComponent.HostColor));
                Assert.That(WGStaffMarkerSystem.MarkerColor("#00ff0000", true), Is.EqualTo(WGStaffMarkerComponent.HostColor),
                    "A fully transparent colour would hide the marker.");
                Assert.That(WGStaffMarkerSystem.VisualsFor(WGAvatarKind.Character, WGMarkerVisuals.Sigil),
                    Is.EqualTo(WGMarkerVisuals.Sigil | WGStaffMarkerSystem.ObjectDefault),
                    "A character with only a sigil beside it could pass for a player.");
            });

            marker.Mark(marked, settings, "Head Admin", "moon", host: false);
            var text = examine.GetExamineText(marked, onlooker).ToString();
            Assert.That(text, Does.Contain("a member of staff: Head Admin"), "The examine line names the staff wording.");

            var name = new TransformSpeakerNameEvent(marked, "Inspector");
            entMan.EventBus.RaiseLocalEvent(marked, name);
            Assert.That(name.VoiceName, Is.EqualTo("[Head Admin] Inspector"));
        });

        await pair.CleanReturnAsync();
    }
}
