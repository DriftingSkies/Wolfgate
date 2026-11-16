using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Modsuits.Modules;

public sealed partial class ModEnergyShieldModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModEnergyShieldModuleComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<ModEnergyShieldModuleComponent, ExaminedEvent>(OnExamined);
    }

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ModSuitSystem _modSuit = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ModEnergyShieldModuleComponent>();
        while (query.MoveNext(out var uid, out var shield))
        {
            if (shield.Charges >= shield.MaxCharges || now < shield.NextCharge)
                continue;

            shield.Charges++;
            shield.NextCharge = now + shield.ChargeIncrement;
            Dirty(uid, shield);
        }
    }
    private void OnDamageModify(Entity<ModEnergyShieldModuleComponent> ent, ref DamageModifyEvent args)
    {
        if (ent.Comp.Charges <= 0
            || args.Origin is not { } origin
            || args.Damage.GetTotal() <= 0
            || !_modSuit.TryGetModuleSuit(ent, out var suit)
            || suit.Comp.Wearer is not { } wearer
            || origin == wearer)
            return;

        args.Damage = new DamageSpecifier();
        ent.Comp.Charges--;
        ent.Comp.NextCharge = _timing.CurTime + ent.Comp.RechargeDelay;
        Dirty(ent);

        _modSuit.TryUseCharge(suit, ent.Comp.BlockPower);

        if (_net.IsClient)
            return;

        _audio.PlayPvs(ent.Comp.BlockSound, wearer);
        _popup.PopupEntity(Loc.GetString("modsuit-energy-shield-block"), wearer, PopupType.Medium);
    }
    private void OnExamined(Entity<ModEnergyShieldModuleComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("modsuit-energy-shield-examine",
            ("charges", ent.Comp.Charges),
            ("max", ent.Comp.MaxCharges)));
    }
}
