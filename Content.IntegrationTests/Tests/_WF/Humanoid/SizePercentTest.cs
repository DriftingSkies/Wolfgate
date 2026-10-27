#nullable enable
using System.Reflection;
using Content.Client._Mono.Company;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Client.Players.PlayTimeTracking;
using Content.Shared.Humanoid.Markings;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Humanoid;

/// <summary>The typed height and width percentages follow their sliders and set them, clamped to range.</summary>
[TestFixture]
public sealed class SizePercentTest
{
    [Test]
    public async Task PercentFollowsAndSetsSliderTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var editor = new HumanoidProfileEditor(
                client.ResolveDependency<IClientPreferencesManager>(),
                client.ResolveDependency<IConfigurationManager>(),
                client.ResolveDependency<IEntityManager>(),
                client.ResolveDependency<IFileDialogManager>(),
                client.ResolveDependency<ILogManager>(),
                client.ResolveDependency<IPlayerManager>(),
                client.ResolveDependency<IPrototypeManager>(),
                client.ResolveDependency<IResourceCache>(),
                client.ResolveDependency<JobRequirementsManager>(),
                client.ResolveDependency<MarkingManager>(),
                client.ResolveDependency<CompanyManager>());

            var slider = editor.FindControl<Slider>("HeightSlider");
            var percent = editor.FindControl<LineEdit>("HeightPercent");

            slider.Value = 1.1f;
            Assert.That(percent.Text, Is.EqualTo("110"), "the box follows the slider");

            Enter(percent, "95%");
            Assert.That(slider.Value, Is.EqualTo(0.95f).Within(0.001f), "typing sets the slider");

            Enter(percent, "500");
            Assert.That(slider.Value, Is.EqualTo(slider.MaxValue).Within(0.001f), "out of range clamps");
            Assert.That(percent.Text, Is.EqualTo(MathF.Round(slider.MaxValue * 100).ToString()));

            Enter(percent, "95.5");
            Assert.That(slider.Value, Is.EqualTo(0.96f).Within(0.001f), "a fraction rounds to a whole percent");
            Assert.That(percent.Text, Is.EqualTo("96"), "so the box shows what was stored");

            Enter(percent, "NaN");
            Assert.That(float.IsFinite(slider.Value), Is.True, "NaN is refused");
            Assert.That(percent.Text, Is.EqualTo("96"));

            Enter(percent, "500");
            Enter(percent, "tall");
            Assert.That(percent.Text, Is.EqualTo(MathF.Round(slider.MaxValue * 100).ToString()), "junk is put back");

            editor.Dispose();
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Types text and raises the entered event the way pressing Enter does.</summary>
    private static void Enter(LineEdit edit, string text)
    {
        edit.Text = text;
        var entered = (Action<LineEdit.LineEditEventArgs>?) typeof(LineEdit)
            .GetField(nameof(LineEdit.OnTextEntered), BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(edit);
        entered!.Invoke(new LineEdit.LineEditEventArgs(edit, text));
    }
}
