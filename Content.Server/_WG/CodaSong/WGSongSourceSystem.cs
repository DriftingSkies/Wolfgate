using Content.Shared._WG.CodaSong;
using Content.Shared.Damage;

namespace Content.Server._WG.CodaSong;

/// <summary>Turns a song source's singers as it is hurt: a fragment's heart sings more wrongly the closer it is to breaking.</summary>
public sealed partial class WGSongSourceSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGSongSourceComponent, DamageChangedEvent>(OnDamaged);
    }

    private void OnDamaged(Entity<WGSongSourceComponent> ent, ref DamageChangedEvent args)
    {
        if (ent.Comp.TurnedAt <= 0f)
            return;

        var turned = Math.Clamp(args.Damageable.TotalDamage.Float() / ent.Comp.TurnedAt, 0f, 1f);
        if (MathF.Abs(turned - ent.Comp.Turned) < 0.02f)
            return;

        ent.Comp.Turned = turned;
        Dirty(ent);
    }
}
