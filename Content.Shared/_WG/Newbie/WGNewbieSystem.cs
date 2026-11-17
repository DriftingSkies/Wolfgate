using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Shuttles.Components;

namespace Content.Shared._WG.Newbie;

/// <summary>
/// The newbie examine line, and the tag on a new player's ship name.
/// </summary>
public sealed class WGNewbieSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGNewbieComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<WGNewbieComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wg-newbie-examine", ("ent", Identity.Entity(ent, EntityManager))), -1);
    }

    /// <summary>
    /// Adds the newbie tag to a ship's shown name, unless the ship isn't a newbie's or hides its label.
    /// </summary>
    public string TagShipName(EntityUid ship, string name)
    {
        if (!HasComp<WGNewbieShipComponent>(ship))
            return name;

        if (TryComp<IFFComponent>(ship, out var iff) &&
            (iff.Flags & (IFFFlags.Hide | IFFFlags.HideLabel | IFFFlags.HideLabelAlways)) != 0)
        {
            return name;
        }

        return Loc.GetString("wg-newbie-ship-name", ("name", name));
    }
}
