using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.EngineCompat;

#pragma warning disable CS0618 // Test shorthand for two engine APIs with no drop-in replacement yet.
/// <summary>The obsolete engine calls tests still need, kept in one place.</summary>
public static class WGObsoleteTestApis
{
    /// <summary>Runs a control's full teardown, which <see cref="Control.Orphan"/> alone skips.</summary>
    public static void Teardown(this Control control)
    {
        control.Dispose();
    }

    /// <summary>The entity that owns a component fetched without its uid.</summary>
    public static EntityUid Uid(this IComponent component)
    {
        return component.Owner;
    }
}
