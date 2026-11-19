using Content.Shared.Preferences.Loadouts;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Cooking;

/// <summary>A loadout group offered to every job that has a loadout, such as the mess kit.</summary>
[Prototype("wgUniversalLoadoutGroup")]
public sealed partial class WGUniversalLoadoutGroupPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<LoadoutGroupPrototype> Group;
}

/// <summary>
/// Adds each <c>wgUniversalLoadoutGroup</c> to every job's loadout, so a group can reach every job without editing
/// each job's loadout prototype. Jobs with no loadout groups at all (borgs, the AI) are left alone.
/// </summary>
public sealed class WGUniversalLoadoutSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();
        Apply();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnReloaded);
    }

    private void OnReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<RoleLoadoutPrototype>() || args.WasModified<WGUniversalLoadoutGroupPrototype>())
            Apply();
    }

    private void Apply()
    {
        foreach (var universal in _proto.EnumeratePrototypes<WGUniversalLoadoutGroupPrototype>())
        {
            foreach (var role in _proto.EnumeratePrototypes<RoleLoadoutPrototype>())
            {
                if (role.Groups.Count > 0 && !role.Groups.Contains(universal.Group))
                    role.Groups.Add(universal.Group);
            }
        }
    }
}
