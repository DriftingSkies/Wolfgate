using Content.Server.Polymorph.Components;
using Content.Server.Speech.EntitySystems;
using Content.Shared._Shitmed.Humanoid.Events;
using Content.Shared._WF.EmoteVoices;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Cloning;
using Content.Shared.Humanoid;
using Content.Shared.Polymorph;
using Content.Shared.Speech.Components;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Server._WF.EmoteVoices;

/// <summary>Gives characters the scream and laugh chosen in the creator in place of their species' own.</summary>
/// <remarks>
/// The voices go into the entity's loaded emote sounds, so every emote sound path (scream action, muffled emotes)
/// plays them, while zombies and cluwnes still override them with their own sets.
/// </remarks>
public sealed partial class EmoteVoiceSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private VocalSystem _vocal = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VocalComponent, ProfileLoadFinishedEvent>(OnProfileLoadFinished);
        // VocalSystem reloads the species' sounds on both, so the voices go back on after it.
        SubscribeLocalEvent<EmoteVoiceComponent, MapInitEvent>(OnMapInit, after: [typeof(VocalSystem)]);
        SubscribeLocalEvent<EmoteVoiceComponent, SexChangedEvent>(OnSexChanged, after: [typeof(VocalSystem)]);
        SubscribeLocalEvent<EmoteVoiceComponent, CloningEvent>(OnCloning);
        SubscribeLocalEvent<EmoteVoiceComponent, PolymorphedEvent>(OnPolymorphed);
    }

    /// <summary>Every profile load sets the voices, so a profile without any gives back the species' own.</summary>
    private void OnProfileLoadFinished(Entity<VocalComponent> ent, ref ProfileLoadFinishedEvent args)
    {
        if (args.Profile is { } profile)
            SetVoices(ent, EmoteVoiceRules.Chosen(profile));
    }

    private void OnMapInit(Entity<EmoteVoiceComponent> ent, ref MapInitEvent args)
    {
        Refresh(ent);
    }

    private void OnSexChanged(Entity<EmoteVoiceComponent> ent, ref SexChangedEvent args)
    {
        Refresh(ent);
    }

    /// <summary>A clone grows from the same template, so it keeps the voices.</summary>
    private void OnCloning(Entity<EmoteVoiceComponent> ent, ref CloningEvent args)
    {
        SetVoices(args.Target, ent.Comp.Voices);
    }

    /// <summary>A transformation that keeps the person's appearance keeps their voices too.</summary>
    private void OnPolymorphed(Entity<EmoteVoiceComponent> ent, ref PolymorphedEvent args)
    {
        if (args.IsRevert
            || !TryComp<PolymorphedEntityComponent>(args.NewEntity, out var polymorphed)
            || !polymorphed.Configuration.TransferHumanoidAppearance)
            return;

        SetVoices(args.NewEntity, ent.Comp.Voices);
    }

    /// <summary>Replaces the entity's chosen voices; none gives back the species' own sounds.</summary>
    public void SetVoices(EntityUid uid, IEnumerable<ProtoId<EmoteVoicePrototype>> voices)
    {
        var list = new List<ProtoId<EmoteVoicePrototype>>(voices);
        if (list.Count == 0)
        {
            if (!RemComp<EmoteVoiceComponent>(uid))
                return;
        }
        else
        {
            EnsureComp<EmoteVoiceComponent>(uid).Voices = list;
        }

        Refresh(uid);
    }

    /// <summary>Reloads the species' emote sounds, then lays the chosen voices over them.</summary>
    private void Refresh(EntityUid uid)
    {
        // Before map init VocalSystem has loaded nothing yet; OnMapInit runs once it has.
        if (LifeStage(uid) < EntityLifeStage.MapInitialized || !TryComp<VocalComponent>(uid, out var vocal))
            return;

        _vocal.ReloadSounds((uid, vocal));

        if (TryComp<EmoteVoiceComponent>(uid, out var voices) && vocal.EmoteSounds is { } species)
        {
            var sex = CompOrNull<HumanoidAppearanceComponent>(uid)?.Sex ?? Sex.Unsexed;
            vocal.EmoteSounds = WithVoices(species, voices.Voices, sex);
        }

        Dirty(uid, vocal);
    }

    /// <summary>A copy of a species' emote sounds with each voice in place of its emote's sound.</summary>
    private EmoteSoundsPrototype WithVoices(EmoteSoundsPrototype species,
        List<ProtoId<EmoteVoicePrototype>> voices,
        Sex sex)
    {
        // Every sound is copied, never shared with the prototype. The species' general params move onto its own
        // sounds, so they never reach a chosen voice.
        var general = species.GeneralParams;
        var voiced = _serialization.CreateCopy(species, notNullableOverride: true);
        voiced.GeneralParams = null;
        voiced.FallbackSound = species.FallbackSound is { } fallback ? CopySound(fallback, general) : null;
        voiced.Sounds = new Dictionary<string, SoundSpecifier>();
        foreach (var (emote, sound) in species.Sounds)
        {
            voiced.Sounds[emote] = CopySound(sound, general);
        }

        foreach (var id in voices)
        {
            if (!_proto.TryIndex(id, out var voice))
                continue;

            if (voice.GetSound(sex) is { } sound)
                voiced.Sounds[voice.Emote] = CopySound(sound, voice.Params);
            else
                voiced.Sounds.Remove(voice.Emote);
        }

        return voiced;
    }

    /// <summary>A new specifier for the same sound; SoundSpecifier is copied by reference, so CreateCopy would share it.</summary>
    private static SoundSpecifier CopySound(SoundSpecifier sound, AudioParams? audioParams)
    {
        var set = audioParams ?? sound.Params;
        return sound switch
        {
            SoundPathSpecifier path => new SoundPathSpecifier(path.Path, set),
            SoundCollectionSpecifier { Collection: { } collection } => new SoundCollectionSpecifier(collection, set),
            _ => sound,
        };
    }
}
