#nullable enable
using System.Linq;
using Content.Client._WG.Options;
using Robust.Client.Input;
using Robust.Shared.Input;

namespace Content.IntegrationTests.Tests._WG.Options;

/// <summary>The SS13 keybind preset applies cleanly and none of its keys land on another on-foot action.</summary>
[TestFixture]
[TestOf(typeof(Ss13KeybindPreset))]
public sealed class Ss13KeybindPresetTest
{
    /// <summary>Text entry, arcade and piloting functions only act in their own mode, so they may share a key.</summary>
    private static readonly string[] ExemptPrefixes = { "Text", "Arcade", "Shuttle" };

    [Test]
    public async Task PresetApplies([Values] bool toggleWalk)
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var input = client.ResolveDependency<IInputManager>();

        await client.WaitAssertion(() =>
        {
            Ss13KeybindPreset.Apply(input, toggleWalk);

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

                    AssertType(EngineKeyFunctions.HideUI, KeyBindingType.Toggle);
                    AssertType(EngineKeyFunctions.ShowDebugMonitors, KeyBindingType.Toggle);
                    AssertType(EngineKeyFunctions.Walk, toggleWalk ? KeyBindingType.Toggle : KeyBindingType.State);
                    AssertType(EngineKeyFunctions.MoveUp, KeyBindingType.State);

                    var human = input.Contexts.GetContext("human");
                    var preset = Ss13KeybindPreset.Binds.Select(b => b.Function).ToHashSet();
                    var onFoot = input.AllBindings
                        .Where(b => human.FunctionExistsHierarchy(b.Function) && !ExemptPrefixes.Any(b.Function.FunctionName.StartsWith))
                        .GroupBy(b => (b.BaseKey, b.Mod1, b.Mod2, b.Mod3));

                    foreach (var combo in onFoot)
                    {
                        var functions = combo.Select(b => b.Function).Distinct().ToArray();
                        if (functions.Length > 1 && functions.Any(preset.Contains) && !IsSharedOnPurpose(functions))
                            Assert.Fail($"{combo.Key} is shared by {string.Join(", ", functions.Select(f => f.FunctionName))}");
                    }

                    foreach (var (first, then) in Ss13KeybindPreset.SharedKeys)
                    {
                        var chords = input.GetKeyBindings(first)
                            .SelectMany(a => input.GetKeyBindings(then)
                                .Where(b => (a.BaseKey, a.Mod1, a.Mod2, a.Mod3) == (b.BaseKey, b.Mod1, b.Mod2, b.Mod3))
                                .Select(b => (First: a, Then: b)))
                            .ToArray();
                        Assert.That(chords, Is.Not.Empty, $"{first.FunctionName} and {then.FunctionName} don't share a key");

                        foreach (var chord in chords)
                        {
                            Assert.That(chord.First.Priority, Is.GreaterThan(chord.Then.Priority),
                                $"{first.FunctionName} must run before {then.FunctionName} on {chord.First.BaseKey}");
                        }
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

        bool IsSharedOnPurpose(BoundKeyFunction[] functions)
        {
            return functions.Length == 2 && Ss13KeybindPreset.SharedKeys.Any(s =>
                functions.Contains(s.First) && functions.Contains(s.Then));
        }

        void AssertType(BoundKeyFunction function, KeyBindingType type)
        {
            Assert.That(input.GetKeyBindings(function).Select(b => b.BindingType), Is.All.EqualTo(type), function.FunctionName);
        }
    }
}
