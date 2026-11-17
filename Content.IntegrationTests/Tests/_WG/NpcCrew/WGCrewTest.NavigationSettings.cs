#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._WG.NpcCrew;
using Content.Shared._WG.NpcCrew;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>Every editable flight limit is copied independently and rejects invalid numeric values.</summary>
    [Test]
    public void CrewNavigationSettingsValidateAndClone()
    {
        var settings = new WGCrewNavigationSettings();
        Assert.That(settings.IsValid(), Is.True);
        var fields = WGCrewNavigationSettings.Fields;
        Assert.That(fields.Select(field => field.Id).Distinct().Count(), Is.EqualTo(fields.Count));
        Assert.That(typeof(WGCrewNavigationSettings).GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Count(field => field.FieldType == typeof(float)), Is.EqualTo(fields.Count));
        foreach (var field in fields)
        {
            var copy = settings.Clone();
            Assert.That(field.Get(copy), Is.EqualTo(field.Get(settings)));
            field.Set(copy, field.Get(settings) + 1);
            Assert.That(field.Get(copy), Is.Not.EqualTo(field.Get(settings)), field.Id);
            foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, field.Min - 1, field.Max + 1 })
            {
                copy = settings.Clone();
                field.Set(copy, invalid);
                Assert.That(copy.IsValid(), Is.False, $"{field.Id} accepted {invalid}.");
            }
        }
        settings.HoldReturnRange = settings.HoldRange + 1;
        Assert.That(settings.IsValid(), Is.False);
    }

    /// <summary>All configured flight limits survive the actual client-to-server setup message unchanged.</summary>
    [Test]
    public async Task CrewNavigationSettingsRoundTripInSetupRequest()
    {
        var observer = Server.System<WGCrewSetupRequestObserver>();
        observer.Requests.Clear();
        var expected = new WGCrewNavigationSettings();
        foreach (var field in WGCrewNavigationSettings.Fields)
            field.Set(expected, field.Min + (field.Max - field.Min) * 0.25f);
        Assert.That(expected.IsValid(), Is.True);
        await WithCrewWindow(window =>
        {
            var ship = new WGCrewSetupCrew { Grid = new NetEntity(101), Group = "existing" };
            UiCall(window, "Receive", UiSnapshot(ship));
            typeof(WGCrewSetupWindow).GetField("_creationGrid", UiPrivate)!.SetValue(window, ship.Grid);
            UiCall(window, "RefreshCreationGrid");
            UiField<LineEdit>(window, "_group").Text = "navigation-payload";
            UiCall(window, "AddRow", new WGCrewSetupPost { Position = new Vector2(2.5f) });
            var editor = UiField<object>(UiField<object>(window, "_createSettings"), "_navigation");
            var source = expected.Clone();
            editor.GetType().GetMethod("Load")!.Invoke(editor, new object[] { source });
            source.CruiseSpeed = 400;
            Assert.That(((Collapsible) editor.GetType().GetField("Body")!.GetValue(editor)!).BodyVisible,
                Is.False, "Advanced settings start collapsed.");
            UiCall(window, "SendCreation", WGCrewSetupAction.Preview, null);
        });
        await RunTicks(15);
        var request = observer.Requests.Single(item => item.Action == WGCrewSetupAction.Preview && item.Mission.Group == "navigation-payload");
        Assert.That(request.Mission.Navigation.IsValid(), Is.True);
        foreach (var field in WGCrewNavigationSettings.Fields)
            Assert.That(field.Get(request.Mission.Navigation), Is.EqualTo(field.Get(expected)), field.Id);
    }

    /// <summary>Invalid flight edits are caught before creation or live-settings requests leave the window.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CrewNavigationEditorRejectsInvalidSettings(bool existingCrew)
    {
        await WithCrewWindow(window =>
        {
            var crew = new WGCrewSetupCrew { Grid = new NetEntity(101), Group = "navigation" };
            UiCall(window, "Receive", UiSnapshot(crew));
            object form;
            if (existingCrew)
            {
                window.SelectCrew(crew.Grid, crew.Group);
                form = UiField<object>(window, "_crewSettings");
            }
            else
            {
                typeof(WGCrewSetupWindow).GetField("_creationGrid", UiPrivate)!.SetValue(window, crew.Grid);
                UiCall(window, "RefreshCreationGrid");
                UiCall(window, "AddRow", new WGCrewSetupPost { Position = new Vector2(2.5f) });
                form = UiField<object>(window, "_createSettings");
            }
            var editor = UiField<object>(form, "_navigation");
            UiField<Dictionary<string, LineEdit>>(editor, "_inputs")["cruise-speed"].Text = "unfinished";
            if (existingCrew)
                UiCall(window, "SendCrew", WGCrewSetupAction.Rules);
            else
                UiCall(window, "SendCreation", WGCrewSetupAction.Preview, null);
            Assert.That(UiField<IDictionary>(window, "_requests"), Is.Empty);
        });
    }
}
