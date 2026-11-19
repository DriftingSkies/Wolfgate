using Content.Shared._Onyx.Body.Systems;
using Content.Shared._WG.Cooking;
using Content.Shared._WG.Wolfmed.Body;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;

namespace Content.Server._WG.Cooking;

/// <summary>Applies <see cref="WGMendOrgans"/> to Wolfmed organ health.</summary>
public sealed class WGOrganMendSystem : EntitySystem
{
    [Dependency] private OrganHealthSystem _organs = default!;
    [Dependency] private SharedBodySystem _body = default!;

    private static readonly string[] InnerOrgans = { "heart", "lungs", "liver", "kidneys", "stomach" };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGMendOrgansEvent>(OnMend);
    }

    private void OnMend(ref WGMendOrgansEvent args)
    {
        if (args.Amount <= 0f)
            return;

        foreach (var (organ, comp) in _body.GetBodyOrgans(args.Target))
        {
            var slot = comp.SlotId;
            if (args.Organs.Count > 0 ? !args.Organs.Contains(slot) : Array.IndexOf(InnerOrgans, slot) < 0)
                continue;
            if (!TryComp<WolfmedOrganComponent>(organ, out var health) || health.Health <= FixedPoint2.Zero)
                continue;

            var cap = health.MaxHealth * args.Cap;
            if (health.Health >= cap)
                continue;

            var amount = args.Favours.Contains(slot) ? args.Amount * args.FavourFactor : args.Amount;
            _organs.ChangeHealth((organ, health), FixedPoint2.Min(FixedPoint2.New(amount), cap - health.Health));
        }
    }
}
