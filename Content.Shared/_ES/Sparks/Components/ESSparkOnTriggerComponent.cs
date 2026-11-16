using Robust.Shared.GameStates;

// WOLFGATE(InteractionParticles): sparks when triggered, ported from Starlight's Visual Overdose
namespace Content.Shared._ES.Sparks.Components;

/// <summary>
/// An entity that sparks when triggered.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(ESSparksSystem))]
public sealed partial class ESSparkOnTriggerComponent : ESBaseSparkConfigurationComponent;
