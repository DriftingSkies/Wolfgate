#nullable enable
using System.Linq;
using System.Reflection;
using System.Text;
using Content.IntegrationTests.Pair;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.Ci;

/// <summary>
/// Names the sprite layers behind the client's bounds assert, which fails without saying whose sprite it was: a layer
/// whose cached bounds are marked clean but no longer match what it draws.
/// </summary>
public static class WGStaleSpriteBounds
{
    private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo LayerDirty = typeof(SpriteComponent.Layer).GetField("BoundsDirty", Internal)!;
    private static readonly FieldInfo LayerBounds = typeof(SpriteComponent.Layer).GetField("Bounds", Internal)!;
    private static readonly MethodInfo Calculate = typeof(SpriteSystem).GetMethod("CalculateLocalBounds", Internal)!;

    /// <summary>Runs a step of a test, and if the bounds assert stops the client, fails naming the stale layers.</summary>
    public static async Task Guard(TestPair pair, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (DebugAssertException e) when (e.StackTrace?.Contains("SpriteSystem.GetLocalBounds") == true)
        {
            // The assert ended the client's thread, so its entities can be read from here.
            throw new AssertionException($"A client sprite layer's bounds went stale:\n{Describe(pair.Client.EntMan) ?? "none found."}", e);
        }
    }

    /// <summary>Describes every stale layer on the client, or null if there are none.</summary>
    public static string? Describe(IEntityManager entities)
    {
        var sprites = entities.System<SpriteSystem>();
        var report = new StringBuilder();
        var query = entities.AllEntityQueryEnumerator<SpriteComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var sprite, out var meta))
        {
            var index = 0;
            foreach (var layer in sprite.AllLayers.OfType<SpriteComponent.Layer>())
            {
                index++;
                if ((bool) LayerDirty.GetValue(layer)!)
                    continue;

                var cached = (Box2) LayerBounds.GetValue(layer)!;
                var actual = (Box2) Calculate.Invoke(sprites, new object[] { layer })!;
                if (cached.EqualsApprox(actual))
                    continue;

                report.AppendLine($"{entities.ToPrettyString(uid)} ({meta.EntityPrototype?.ID}) layer {index - 1}: "
                    + $"{layer.ActualRsi?.Path}/{layer.State}, texture {layer.Texture != null}, cached {cached}, actual {actual}, "
                    + $"rotation {layer.Rotation}, strategy {layer.RenderingStrategy}, snap {sprite.SnapCardinals}, granular {sprite.GranularLayersRendering}");
            }
        }

        return report.Length == 0 ? null : report.ToString();
    }
}
