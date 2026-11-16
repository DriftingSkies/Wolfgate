namespace Content.Shared._WF.Modsuits.Modules;

/// <summary>Marks a wearer whose active loudspeaker module widens their speech range.</summary>
[RegisterComponent]
public sealed partial class ModLoudspeakerActiveComponent : Component
{
    [DataField]
    public float Range = 20f;
}

/// <summary>Wolfgate equivalent of Goobstation's loudspeaker holder: widens IC chat for an active module.</summary>
public sealed partial class ModLoudspeakerModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModLoudspeakerModuleComponent, ModModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<ModLoudspeakerModuleComponent, ModModuleDeactivatedEvent>(OnDeactivated);
    }

    private void OnActivated(Entity<ModLoudspeakerModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
        if (args.Wearer is not { } wearer)
            return;

        ent.Comp.User = wearer;
        EnsureComp<ModLoudspeakerActiveComponent>(wearer);
    }

    private void OnDeactivated(Entity<ModLoudspeakerModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        if (ent.Comp.User is not { } wearer)
            return;

        ent.Comp.User = null;
        RemComp<ModLoudspeakerActiveComponent>(wearer);
    }
}
