using Content.Server._Mono.FireControl;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._WG.GunneryChips;

/// <summary>A co-processor chip that raises a gunnery server's processing power when fitted into it.</summary>
[RegisterComponent]
public sealed partial class WGGunneryChipComponent : Component
{
    /// <summary>Processing power the chip adds.</summary>
    [DataField(required: true)]
    public int Power;
}

/// <summary>The chips fitted to a gunnery server, and what they add.</summary>
[RegisterComponent]
public sealed partial class WGGunneryChipsComponent : Component
{
    [ViewVariables]
    public int Fitted;

    [ViewVariables]
    public int Bonus;
}

/// <summary>Fits gunnery chips into servers: use one on the server, up to <see cref="MaxChips"/> each, for good.</summary>
public sealed class WGGunneryChipSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>The most chips one server takes.</summary>
    public const int MaxChips = 3;

    private static readonly SoundSpecifier FitSound = new SoundPathSpecifier("/Audio/Items/screwdriver.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FireControlServerComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WGGunneryChipsComponent, ExaminedEvent>(OnExamined);
    }

    private void OnInteractUsing(Entity<FireControlServerComponent> server, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp(args.Used, out WGGunneryChipComponent? chip))
            return;
        args.Handled = true;

        var chips = EnsureComp<WGGunneryChipsComponent>(server);
        if (chips.Fitted >= MaxChips)
        {
            _popup.PopupEntity(Loc.GetString("wg-gunnery-chip-full", ("max", MaxChips)), server, args.User);
            return;
        }

        chips.Fitted++;
        chips.Bonus += chip.Power;
        server.Comp.ProcessingPower += chip.Power;
        _audio.PlayPvs(FitSound, server);
        _popup.PopupEntity(Loc.GetString("wg-gunnery-chip-fitted", ("power", chip.Power), ("total", server.Comp.ProcessingPower)),
            server, args.User);
        QueueDel(args.Used);
    }

    private void OnExamined(Entity<WGGunneryChipsComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wg-gunnery-chip-examine", ("fitted", ent.Comp.Fitted), ("max", MaxChips),
            ("bonus", ent.Comp.Bonus)));
    }
}
