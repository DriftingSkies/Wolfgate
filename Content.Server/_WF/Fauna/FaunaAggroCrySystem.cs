using Content.Server.NPC.HTN;
using Content.Server.Popups;
using Content.Shared._WF.Fauna;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Plays the cry of <see cref="FaunaAggroCryComponent"/> mobs when their NPC picks a new target, and sets
/// <see cref="FaunaAggroVisuals.Aggro"/> while they have one.
/// </summary>
public sealed partial class FaunaAggroCrySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaAggroCryComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var cry, out var htn))
        {
            if (!_mobState.IsAlive(uid) ||
                !htn.Blackboard.TryGetValue<EntityUid>(FaunaHelperSystem.TargetKey, out var target, EntityManager) ||
                !_mobState.IsAlive(target))
            {
                if (cry.LastTarget != null)
                    _appearance.SetData(uid, FaunaAggroVisuals.Aggro, false);

                cry.LastTarget = null;
                continue;
            }

            if (cry.LastTarget == target)
                continue;

            if (cry.LastTarget == null)
                _appearance.SetData(uid, FaunaAggroVisuals.Aggro, true);

            cry.LastTarget = target;
            if (now < cry.NextCry)
                continue;

            cry.NextCry = now + cry.Cooldown;
            _audio.PlayPvs(cry.Sound, uid);
            if (cry.Popup is { } popup)
                _popup.PopupEntity(Loc.GetString(popup, ("user", uid), ("target", target)), uid);
        }
    }
}
