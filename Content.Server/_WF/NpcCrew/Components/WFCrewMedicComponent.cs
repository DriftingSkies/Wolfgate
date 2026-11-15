using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew.Components;

/// <summary>On an autodoc a surgeon has stocked: the bottle last put in each reservoir, so one taken away isn't replaced for free.</summary>
[RegisterComponent]
public sealed partial class WFCrewPodStockComponent : Component
{
    /// <summary>The bottle he last put in each reservoir slot, by slot index.</summary>
    [ViewVariables]
    public Dictionary<int, EntityUid> Bottles = new();
}

/// <summary>
/// What the NPC medics have done for one patient lately, whichever of them did it, so letting the patient go and
/// taking them again restarts no dose's or shock's clock. Kept by the medic system a while after the last entry.
/// </summary>
public sealed class WFCrewPatientCare
{
    /// <summary>Shocks given, counted against a paramedic's <see cref="WFCrewMedicComponent.ShocksOnSite"/>; a revival clears it.</summary>
    public int Shocks;

    /// <summary>When the next shock may be given.</summary>
    public TimeSpan NextShock;

    /// <summary>When the next stimulant dose may be given.</summary>
    public TimeSpan NextStimulant;

    /// <summary>When the next antitoxin dose may be given.</summary>
    public TimeSpan NextAntitoxin;

    /// <summary>Until when a paramedic leaves the patient where they lie, there being no free bed or drawer to take them to.</summary>
    public TimeSpan NoRoomUntil;

    /// <summary>When the record may be forgotten.</summary>
    public TimeSpan Until;
}

/// <summary>Which care an NPC medic gives.</summary>
public enum WFCrewMedicRank : byte
{
    /// <summary>Answers the downed and the bleeding wherever they lie aboard or on a docked ship, stabilises them,
    /// patches up minor injuries and brings anyone worse to Medical.</summary>
    Paramedic,

    /// <summary>Works the beds in Medical: the serious cases a paramedic brings in, without his limits. He runs the
    /// autodoc for anyone who needs surgery and clones the dead on the ship's genetic records.</summary>
    Surgeon,
}

/// <summary>One thing an NPC medic does for a patient, each its own do-after.</summary>
public enum WFCrewMedicAction : byte
{
    None,
    Shock,
    Cpr,
    Tourniquet,
    Dress,
    Stimulant,
    Antitoxin,
    Brute,
    Burn,

    /// <summary>Closed cuts and punctures with a suture.</summary>
    Suture,

    /// <summary>A surgeon put the patient in the autodoc and started a run.</summary>
    Surgery,

    /// <summary>A surgeon took a living player's genetic record for cloning, at their asking.</summary>
    Register,

    /// <summary>A surgeon started the cloner for a player on file who died.</summary>
    Clone,

    /// <summary>A surgeon operated by hand at a table, the pod being busy or missing.</summary>
    TableSurgery,
}

/// <summary>One step of a hand operation: the part, the surgery that owns the step, the step and its base seconds.</summary>
public readonly record struct WFCrewTableStep(EntityUid Part, EntProtoId Surgery, EntProtoId Step, float Seconds);

/// <summary>
/// An NPC medic: finds patients on his home grid and the ships docked to it, treats them a step at a time, and
/// (as a paramedic) drags anyone who needs more to a free bed in Medical, or the unrevivable dead to the morgue. A
/// surgeon takes bed patients who need surgery to a free autodoc, keeps it stocked, and clones players on the ship's
/// genetic records. His supplies never run out. Added by the medic crew roles.
/// </summary>
[RegisterComponent]
public sealed partial class WFCrewMedicComponent : Component
{
    /// <summary>Which care he gives.</summary>
    [DataField]
    public WFCrewMedicRank Rank = WFCrewMedicRank.Paramedic;

    /// <summary>A paramedic leaves a patient with more damage than this to Medical rather than treating it where they lie.</summary>
    [DataField]
    public float SeriousDamage = 100f;

    /// <summary>The item whose effect dresses a bleeding part.</summary>
    [DataField]
    public EntProtoId Gauze = "Gauze";

    /// <summary>The item whose effect patches brute damage.</summary>
    [DataField]
    public EntProtoId Brutepack = "Brutepack";

    /// <summary>The item whose effect patches burns.</summary>
    [DataField]
    public EntProtoId Ointment = "RegenerativeMesh";

    /// <summary>The item whose effect closes cuts and punctures, which a bruise pack leaves open.</summary>
    [DataField]
    public EntProtoId Suture = "MedicatedSuture";

    /// <summary>Given to the unconscious, at most once a minute.</summary>
    [DataField]
    public string Stimulant = "Epinephrine";

    /// <summary>Given when the patient's toxin load is above <see cref="ToxinLoad"/>, at most once a minute.</summary>
    [DataField]
    public string Antitoxin = "Dylovene";

    /// <summary>Units of stimulant or antitoxin in one dose.</summary>
    [DataField]
    public float Dose = 10f;

    /// <summary>The toxin load above which he gives antitoxin.</summary>
    [DataField]
    public float ToxinLoad = 10f;

    /// <summary>How many shocks a paramedic gives where the patient lies before taking them in.</summary>
    [DataField]
    public int ShocksOnSite = 3;

    /// <summary>
    /// What a surgeon puts in an empty autodoc reservoir slot, in slot order: the anaesthetic first, then the
    /// antibiotic. Whatever is in the reservoirs he tops back up to full before each run.
    /// </summary>
    [DataField]
    public List<EntProtoId> Reservoirs = new() { "WFWolfmedOpiateChemistryBottle", "WFSpaceacillinChemistryBottle" };

    /// <summary>Autodoc runs a surgeon starts for one patient before he takes them back to a bed.</summary>
    [DataField]
    public int OperationsPerPatient = 4;

    /// <summary>
    /// The off-map pod a surgeon works from at a table: its library and triage are what he knows, its tools and tray
    /// are his hands' tools, its reservoirs his anaesthetic.
    /// </summary>
    [DataField]
    public EntProtoId Instruments = "WFCrewSurgeonInstruments";

    /// <summary>Procedures a surgeon does by hand for one patient before he takes them back to a bed.</summary>
    [DataField]
    public int TableProcedures = 12;

    /// <summary>Multiplies a hand step's time.</summary>
    [DataField]
    public float TableSpeed = 1f;

    /// <summary>
    /// How long a surgeon waits before cloning a player on file whose body could still be brought back, so the
    /// medics get their chance first. The unrevivable and those with no body left are cloned at once.
    /// </summary>
    [DataField]
    public TimeSpan CloneAfter = TimeSpan.FromMinutes(2);

    /// <summary>The patient he has taken on, if any.</summary>
    [ViewVariables]
    public EntityUid? Patient;

    /// <summary>The bed, morgue or autodoc a medic is taking his patient to.</summary>
    [ViewVariables]
    public EntityUid? Destination;

    /// <summary>The autodoc a surgeon's patient is in, while he attends it.</summary>
    [ViewVariables]
    public EntityUid? Pod;

    /// <summary>The operating table a surgeon's patient is on, while he works there by hand.</summary>
    [ViewVariables]
    public EntityUid? Table;

    /// <summary>His instruments, once he has needed them. Held off the map.</summary>
    [ViewVariables]
    public EntityUid? Kit;

    /// <summary>The procedure he is doing by hand.</summary>
    [ViewVariables]
    public AutodocProcedureEntry? Procedure;

    /// <summary>The step of <see cref="Procedure"/> under way.</summary>
    [ViewVariables]
    public WFCrewTableStep? Step;

    /// <summary>Passes of a step in a row that changed nothing on the part.</summary>
    [ViewVariables]
    public int Stalls;

    /// <summary>When the last step's wounds have landed, so the next plan doesn't read them as new work.</summary>
    [ViewVariables]
    public TimeSpan TableSettle;

    /// <summary>Autodoc runs started for the current patient, or procedures begun at the table.</summary>
    [ViewVariables]
    public int Operations;

    /// <summary>Tries at taking hold of the patient he is taking somewhere, when the pull doesn't take.</summary>
    [ViewVariables]
    public int PullTries;

    /// <summary>The treatment under way.</summary>
    [ViewVariables]
    public WFCrewMedicAction Action;

    /// <summary>The body part <see cref="Action"/> is for, if any.</summary>
    [ViewVariables]
    public EntityUid? Part;

    /// <summary>Whether a treatment's do-after is under way.</summary>
    [ViewVariables]
    public bool Busy;

    /// <summary>The do-after of the treatment under way; any other that finishes is one he dropped.</summary>
    [ViewVariables]
    public DoAfterId? Treatment;

    /// <summary>The last treatment finished, for tests and the admin's eye.</summary>
    [ViewVariables]
    public WFCrewMedicAction Last;

    /// <summary>When he may next say a line aloud.</summary>
    [ViewVariables]
    public TimeSpan NextLine;

    /// <summary>Where he was last headed: his patient, or the bed or drawer he drags them to.</summary>
    [ViewVariables]
    public EntityUid? Goal;

    /// <summary>How far he stood from <see cref="Goal"/> when he last got nearer to it.</summary>
    [ViewVariables]
    public float GoalDistance;

    /// <summary>When he last got nearer to <see cref="Goal"/>, or was working.</summary>
    [ViewVariables]
    public TimeSpan GoalProgress;

    /// <summary>Patients he gave up on for want of a way to them, and until when he leaves them be.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Passed = new();

    /// <summary>When a surgeon next looks through the dead on file for someone to clone.</summary>
    [ViewVariables]
    public TimeSpan NextCloneCheck;

    /// <summary>The healing items he works with, held off the map: his kit never runs out.</summary>
    [ViewVariables]
    public Dictionary<EntProtoId, EntityUid> Supplies = new();
}
