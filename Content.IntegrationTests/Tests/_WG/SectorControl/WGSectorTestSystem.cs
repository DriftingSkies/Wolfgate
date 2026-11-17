#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server._WG.SectorControl;
using Content.Shared._WG.SectorControl;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.SectorControl;

/// <summary>Records the sector events a test cares about and vetoes the claims it is told to.</summary>
public sealed class WGSectorTestSystem : EntitySystem
{
    public readonly List<WGSectorCellChangedEvent> Changed = new();
    public readonly List<WGSectorCellContestedEvent> Contested = new();
    public readonly List<WGSectorContestResolvedEvent> Resolved = new();
    public readonly List<WGSectorClaimAttemptEvent> Attempts = new();
    public readonly List<WGSectorCellDisputedEvent> Disputed = new();

    public readonly List<WGSectorRetakeAttemptEvent> RetakeAttempts = new();
    public readonly List<WGSectorCellRetakingEvent> Retaking = new();
    public readonly List<WGSectorCellRetakenEvent> Retaken = new();

    public readonly List<WGSectorCellClearingEvent> Clearing = new();
    public readonly List<WGSectorCellClearedEvent> Cleared = new();

    /// <summary>Cells a retake is vetoed for.</summary>
    public readonly HashSet<WGSectorCell> RetakeVetoed = new();

    /// <summary>Cells every claim and contest on is vetoed for.</summary>
    public readonly HashSet<WGSectorCell> Vetoed = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGSectorClaimAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WGSectorCellChangedEvent>(OnChanged);
        SubscribeLocalEvent<WGSectorCellContestedEvent>(OnContested);
        SubscribeLocalEvent<WGSectorContestResolvedEvent>(OnResolved);
        SubscribeLocalEvent<WGSectorCellDisputedEvent>(OnDisputed);
        SubscribeLocalEvent<WGSectorRetakeAttemptEvent>(OnRetakeAttempt);
        SubscribeLocalEvent<WGSectorCellRetakingEvent>(OnRetaking);
        SubscribeLocalEvent<WGSectorCellRetakenEvent>(OnRetaken);
        SubscribeLocalEvent<WGSectorCellClearingEvent>(OnClearing);
        SubscribeLocalEvent<WGSectorCellClearedEvent>(OnCleared);
    }

    private void OnClearing(ref WGSectorCellClearingEvent args)
    {
        Clearing.Add(args);
    }

    private void OnCleared(ref WGSectorCellClearedEvent args)
    {
        Cleared.Add(args);
    }

    /// <summary>Forgets what was recorded and lifts every veto.</summary>
    public void Reset()
    {
        Changed.Clear();
        Contested.Clear();
        Resolved.Clear();
        Attempts.Clear();
        Disputed.Clear();
        Vetoed.Clear();
        RetakeAttempts.Clear();
        Retaking.Clear();
        Retaken.Clear();
        RetakeVetoed.Clear();
        Clearing.Clear();
        Cleared.Clear();
    }

    private void OnRetakeAttempt(ref WGSectorRetakeAttemptEvent args)
    {
        RetakeAttempts.Add(args);
        if (RetakeVetoed.Contains(args.Cell))
            args.Cancelled = true;
    }

    private void OnRetaking(ref WGSectorCellRetakingEvent args)
    {
        Retaking.Add(args);
    }

    private void OnRetaken(ref WGSectorCellRetakenEvent args)
    {
        Retaken.Add(args);
    }

    private void OnAttempt(ref WGSectorClaimAttemptEvent args)
    {
        Attempts.Add(args);
        if (Vetoed.Contains(args.Cell))
            args.Cancelled = true;
    }

    private void OnChanged(ref WGSectorCellChangedEvent args)
    {
        Changed.Add(args);
    }

    private void OnContested(ref WGSectorCellContestedEvent args)
    {
        Contested.Add(args);
    }

    private void OnDisputed(ref WGSectorCellDisputedEvent args)
    {
        Disputed.Add(args);
    }

    private void OnResolved(ref WGSectorContestResolvedEvent args)
    {
        Resolved.Add(args);
    }
}
