using Content.Shared._WF.CombatMusic;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.CombatMusic;

/// <summary>Gives a spawned player's body the combat playlist from their character.</summary>
public sealed partial class WFCombatMusicSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent args)
    {
        var tracks = WFCombatMusicRules.Validate(args.Profile.CombatMusic, _proto);
        if (tracks.Count == 0)
        {
            RemComp<WFCombatMusicComponent>(args.Mob);
            return;
        }

        var music = EnsureComp<WFCombatMusicComponent>(args.Mob);
        music.Tracks = tracks;
        Dirty(args.Mob, music);
    }
}
