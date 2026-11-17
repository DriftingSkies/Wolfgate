using Content.Shared._WG.CombatMusic;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.CombatMusic;

/// <summary>Gives a spawned player's body the combat playlist from their character.</summary>
public sealed partial class WGCombatMusicSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent args)
    {
        var tracks = WGCombatMusicRules.Validate(args.Profile.CombatMusic, _proto);
        if (tracks.Count == 0)
        {
            RemComp<WGCombatMusicComponent>(args.Mob);
            return;
        }

        var music = EnsureComp<WGCombatMusicComponent>(args.Mob);
        music.Tracks = tracks;
        Dirty(args.Mob, music);
    }
}
