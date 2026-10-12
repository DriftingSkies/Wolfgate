#nullable enable
using System.Collections;
using System.Numerics;
using System.Reflection;
using Content.Client._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.BlackHole;

/// <summary>Checks the system replacement and the shader interface used by the star overlay.</summary>
[TestFixture]
public sealed class BlackHoleStarTest
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>The visual replacement preserves Kyphrus's name, illumination model and planetary layout.</summary>
    [Test]
    public async Task KyphrusRetainsSystemLayout()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var definition = prototypes.Index<StarSystemPrototype>("SystemKyphrus");
            var system = pair.Server.System<Content.Server._FarHorizons.StarSystem.StarSystemMapSystem>()
                .BuildPlanetarySystem("SystemKyphrus");
            Assert.That(system, Is.Not.Null);
            var ordinary = new Star(prototypes.Index<StarTypePrototype>("TypeKStar"), prototypes);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(definition.Star.Id, Is.EqualTo("StarKyphrus"));
                Assert.That(system!.Star.Shader, Is.EqualTo("WFBlackHole"));
                Assert.That(system.Star.Name, Is.EqualTo("Kyphrus"));
                Assert.That(system.Star.SolarMass, Is.EqualTo(0.62f));
                Assert.That(system.Star.Radius, Is.EqualTo(ordinary.Radius));
                Assert.That(system.Star.Luminocity, Is.EqualTo(ordinary.Luminocity));
                Assert.That(system.Planets, Has.Count.EqualTo(definition.Planets.Count));
                Assert.That(system.Planets, Is.Not.Empty);
            }

            for (var i = 0; i < definition.Planets.Count; i++)
            {
                var entry = definition.Planets[i];
                var planet = prototypes.Index(entry.Planet);
                var expected = new Vector2(MathF.Cos(entry.Angle), MathF.Sin(entry.Angle)) * entry.Distance;
                Assert.That(system!.Planets[i].Name, Is.EqualTo(planet.Name));
                Assert.That(Vector2.Distance(system.Planets[i].Position, expected), Is.LessThan(0.001f));
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Both renderers accept the overlay's common bindings; only black holes request the background.</summary>
    [TestCase("StarKyphrus", "WFBlackHole", true)]
    [TestCase("TypeKStar", "MainSequenceStar", false)]
    public async Task StarShadersSupportOverlayBindings(string starId, string shaderId, bool lensing)
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Client.WaitAssertion(() =>
        {
            var prototypes = pair.Client.ResolveDependency<IPrototypeManager>();
            var star = new Star(prototypes.Index<StarTypePrototype>(starId), prototypes);
            Assert.That(star.Shader, Is.EqualTo(shaderId));
            var prototype = prototypes.Index<ShaderPrototype>(star.Shader);
            AssertUniforms(prototype,
                ("starWorldPos", "Vec2"), ("starRadius", "Float"), ("starColor", "Vec3"),
                ("starLuminosity", "Float"), ("rotationAngle", "Float"),
                ("viewportMin", "Vec2"), ("viewportSize", "Vec2"), ("parallaxCenter", "Vec2"),
                ("hasRings", "Bool"), ("ringsRadiusInner", "Float"), ("ringsRadiusOuter", "Float"),
                ("ringsBandFrequency", "Float"), ("ringsColor1", "Vec3"),
                ("ringsColor2", "Vec3"), ("ringsColor3", "Vec3"));
            if (lensing)
            {
                AssertUniforms(prototype,
                    ("SCREEN_TEXTURE", "Sampler2D"), ("hasBackground", "Bool"),
                    ("worldToScreenX", "Vec2"), ("worldToScreenY", "Vec2"),
                    ("parallaxFactor", "Float"), ("solarRadiusFactor", "Float"),
                    ("diskTilt", "Float"), ("rotationSpeed", "Float"), ("lensStrength", "Float"), ("effectRadius", "Float"));
            }

            var overlay = new StarOverlay(pair.Client.EntMan, prototypes);
            using var shader = (ShaderInstance?) typeof(StarOverlay)
                .GetMethod("SetupStarShader", PrivateInstance)!.Invoke(overlay, new object[] { star });
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader!.Mutable, Is.True);
            typeof(StarOverlay).GetField("_star", PrivateInstance)!.SetValue(overlay, star);
            Assert.That(overlay.RequestScreenTexture, Is.EqualTo(lensing));
            overlay.ResetShader();
            Assert.That(overlay.RequestScreenTexture, Is.False);
            overlay.Dispose();
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Checks parsed declarations because headless shader instances do not validate SetParameter calls.</summary>
    private static void AssertUniforms(ShaderPrototype prototype, params (string Name, string Type)[] expected)
    {
        var parsed = typeof(ShaderPrototype).GetProperty("_parsed", PrivateInstance)!.GetValue(prototype);
        Assert.That(parsed, Is.Not.Null, $"{prototype.ID} did not load its shader source.");
        var uniforms = (IDictionary) parsed!.GetType().GetProperty("Uniforms")!.GetValue(parsed)!;
        foreach (var (name, type) in expected)
        {
            Assert.That(uniforms.Contains(name), Is.True, $"{prototype.ID} is missing uniform {name}.");
            var uniform = uniforms[name]!;
            var fullType = uniform.GetType().GetProperty("Type")!.GetValue(uniform)!;
            var actual = fullType.GetType().GetProperty("Type")!.GetValue(fullType)!.ToString();
            Assert.That(actual, Is.EqualTo(type), $"{prototype.ID}.{name} has the wrong parameter type.");
        }
    }
}
