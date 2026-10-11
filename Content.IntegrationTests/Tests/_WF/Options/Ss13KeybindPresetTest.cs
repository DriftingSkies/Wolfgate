#nullable enable
using System.Linq;
using Content.Client._WF.Options;
using Robust.Client.Input;
using Robust.Shared.Input;

namespace Content.IntegrationTests.Tests._WF.Options;

/// <summary>The SS13 keybind preset applies cleanly and leaves no two of its own actions on one key.</summary>
[TestFixture]
[TestOf(typeof(Ss13KeybindPreset))]
public sealed class Ss13KeybindPresetTest
{
    [Test]
    public async Task PresetApplies()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var input = client.ResolveDependency<IInputManager>();

        await client.WaitAssertion(() =>
        {
            Ss13KeybindPreset.Apply(input, _ => KeyBindingType.State);

            try
            {
                Assert.Multiple(() =>
                {
                    foreach (var group in Ss13KeybindPreset.Binds.GroupBy(b => b.Function))
                    {
                        var expected = group.Select(b => (b.Key, b.Mod));
                        var actual = input.GetKeyBindings(group.Key).Select(b => (b.BaseKey, b.Mod1));
                        Assert.That(actual, Is.EquivalentTo(expected), group.Key.FunctionName);
                    }

                    foreach (var function in Ss13KeybindPreset.Unbound)
                    {
                        Assert.That(input.GetKeyBindings(function), Is.Empty, function.FunctionName);
                    }

                    foreach (var combo in Ss13KeybindPreset.Binds.GroupBy(b => (b.Key, b.Mod)))
                    {
                        Assert.That(combo.Count(), Is.EqualTo(1), $"{combo.Key} is bound twice");
                    }

                    Assert.That(input.IsKeyFunctionModified(EngineKeyFunctions.Use), Is.False);
                });
            }
            finally
            {
                // The client is pooled.
                input.ResetAllBindings();
            }
        });

        await pair.CleanReturnAsync();
    }
}
