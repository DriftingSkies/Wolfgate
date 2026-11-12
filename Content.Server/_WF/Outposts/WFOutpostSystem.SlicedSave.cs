using System.Diagnostics;
using Content.Server._WF.Outposts.Components;
using Content.Server.Database;
using Content.Shared.Database;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Events;
using Robust.Shared.Network;
using Robust.Shared.Serialization.Markdown.Mapping;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Autosaves a few milliseconds per tick. Serialising an outpost at once costs about a fifth of a millisecond an object
/// on the main thread, most of a second for a large one; the same work is spread over as many ticks as it needs
/// instead, the way <c>WFHullRetireSystem</c> copies a sold hull.
///
/// The outpost is live while this runs, so the save is taken in an order that keeps it whole. The first tick takes
/// the outpost itself and everything loose on it (items, crates, people's things: what moves), so those are as they
/// stood that tick. The anchored structures, which are most of it and hardly ever move, follow a few a tick, each with
/// what it holds. If a structure is gone before its turn, or something already saved has since moved into one, the
/// save is torn: it is dropped and tried again shortly, and after two torn tries the outpost is saved at once.
/// </summary>
public sealed partial class WFOutpostSystem
{
    [Dependency] private IDependencyCollection _dependencies = default!;

    /// <summary>Main-thread time a sliced save may take each tick, in milliseconds.</summary>
    private const double SliceBudgetMs = 3;

    /// <summary>How soon a torn save is tried again.</summary>
    private static readonly TimeSpan TornRetry = TimeSpan.FromSeconds(30);

    /// <summary>Torn tries after which an outpost is saved at once.</summary>
    private const int TornLimit = 2;

    private sealed class SlicedSave
    {
        public EntityUid Outpost;
        public NetUserId Owner;
        public int Slot;
        public EntitySerializer Serializer = default!;
        public HashSet<EntityUid> Roots = new();

        /// <summary>The anchored structures still to take, each the root of everything it holds.</summary>
        public List<EntityUid> Structures = new();
        public int Next;

        /// <summary>Everything taken so far.</summary>
        public HashSet<EntityUid> Done = new();
        public bool Torn;

        public string Name = string.Empty;
        public int Tiles;
        public int Entities;
        public int Appraisal;
        public int Fingerprint;
        public Guid Lineage;

        public double Ms;
        public int Ticks;
        public double Worst;
    }

    /// <summary>The save under way; one outpost at a time.</summary>
    private SlicedSave? _sliced;

    /// <summary>How many sliced saves of an outpost in a row were torn.</summary>
    private readonly Dictionary<EntityUid, int> _torn = new();

    private readonly Stopwatch _sliceTimer = new();

    /// <summary>The last sliced save finished, for logs and tests: objects, time spent, ticks taken, the longest tick, and whether it was torn.</summary>
    public (int Entities, double Ms, int Ticks, double WorstMs, bool Torn)? LastSlicedSave { get; private set; }

    /// <summary>Whether a sliced save is under way.</summary>
    public bool IsSlicing => _sliced != null;

    private static SerializationOptions SliceOptions()
    {
        // No orphan error: the outpost's ground is left out on purpose.
        return SerializationOptions.Default with
        {
            MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
            LogAutoInclude = null,
            Category = FileCategory.Grid,
            ErrorOnOrphan = false,
        };
    }

    /// <summary>
    /// Starts a sliced save of an outpost into a slot: checks and stamps it as a save at once does, takes the outpost and
    /// everything loose on it now, and leaves its structures to the ticks that follow.
    /// </summary>
    private bool TryBeginSlicedSave(Entity<WFOutpostComponent> outpost, int slot)
    {
        var owner = outpost.Comp.Owner;
        if (_sliced != null || slot < 0 || slot >= Content.Shared._WF.Outposts.WFOutpostLimits.SaveSlots || IsBusy(owner)
            || HasComp<Content.Shared._WF.Outposts.WFOutpostPendingLoadComponent>(outpost) || !TryComp<MapGridComponent>(outpost, out var grid))
            return false;

        var entities = CountAllEntities(outpost);
        if (entities > EntityCap(outpost))
            return false;

        Stamp(outpost);
        CaptureAccess(outpost);
        _roundSetup.SyncOutpostCrew(outpost);
        var save = new SlicedSave
        {
            Outpost = outpost,
            Owner = owner,
            Slot = slot,
            Name = Name(outpost),
            Tiles = CountAllTiles((outpost, outpost.Comp, grid)),
            Entities = entities,
            Appraisal = LoadAppraisal((outpost, grid)),
            Fingerprint = Fingerprint((outpost, grid)),
            Lineage = GetLineage(outpost.Comp),
        };

        var saving = new WFOutpostSavingEvent();
        RaiseLocalEvent(outpost, ref saving);
        _sliceTimer.Restart();
        try
        {
            // The event can add entities to save, as a map save's does.
            save.Roots.Add(outpost);
            RaiseLocalEvent(new BeforeSerializationEvent(save.Roots, new HashSet<MapId> { Transform(outpost).MapID }, FileCategory.Grid));
            save.Serializer = new EntitySerializer(_dependencies, SliceOptions());

            // The outpost itself and what is loose on it now; its anchored structures after.
            var now = new HashSet<EntityUid>();
            var later = new HashSet<EntityUid>();
            foreach (var root in save.Roots)
            {
                if (!save.Serializer.IsSerializable(root))
                    continue;

                now.Add(root);
                var children = Transform(root).ChildEnumerator;
                while (children.MoveNext(out var child))
                {
                    if (!save.Serializer.IsSerializable(child))
                        continue;

                    if (Transform(child).Anchored)
                    {
                        save.Structures.Add(child);
                        IncludeForSave(save.Serializer, child, later);
                    }
                    else
                    {
                        IncludeForSave(save.Serializer, child, now);
                    }
                }
            }

            var all = new HashSet<EntityUid>(now);
            all.UnionWith(later);
            save.Serializer.ReserveYamlIds(all);
            save.Serializer.SerializeEntities(now);
            save.Done.UnionWith(now);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to start saving outpost {ToPrettyString(outpost)}: {e}");
            return false;
        }
        finally
        {
            // What the event put on the outpost for the save is in it now.
            var saved = new WFOutpostSavedEvent();
            RaiseLocalEvent(outpost, ref saved);
        }

        save.Ms = save.Worst = _sliceTimer.Elapsed.TotalMilliseconds;
        save.Ticks = 1;
        _busy.Add(owner);
        _sliced = save;
        return true;
    }

    private void IncludeForSave(EntitySerializer serializer, EntityUid uid, HashSet<EntityUid> entities)
    {
        if (!serializer.IsSerializable(uid))
            return;

        entities.Add(uid);
        var children = Transform(uid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            IncludeForSave(serializer, child, entities);
        }
    }

    /// <summary>Takes structures into the save under way until the tick's budget is gone, and stores it when none are left.</summary>
    private void UpdateSlicedSave()
    {
        if (_sliced is not { } save)
            return;

        if (TerminatingOrDeleted(save.Outpost) || !TryComp<WFOutpostComponent>(save.Outpost, out var outpost))
        {
            EndSlicedSave(save, stored: false);
            return;
        }

        _sliceTimer.Restart();
        var unit = new HashSet<EntityUid>();
        try
        {
            while (save.Next < save.Structures.Count && _sliceTimer.Elapsed.TotalMilliseconds < SliceBudgetMs)
            {
                var structure = save.Structures[save.Next++];

                // Gone, or no longer standing on the outpost: what points at it would point at nothing.
                if (TerminatingOrDeleted(structure) || Transform(structure).ParentUid != save.Outpost)
                {
                    save.Torn = true;
                    continue;
                }

                unit.Clear();
                IncludeForSave(save.Serializer, structure, unit);

                // Something saved where it stood a moment ago has since gone into this.
                if (unit.Overlaps(save.Done))
                {
                    save.Torn = true;
                    unit.ExceptWith(save.Done);
                }

                save.Serializer.SerializeEntities(unit);
                save.Done.UnionWith(unit);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save outpost {ToPrettyString(save.Outpost)}: {e}");
            EndSlicedSave(save, stored: false);
            return;
        }

        var spent = _sliceTimer.Elapsed.TotalMilliseconds;
        save.Ms += spent;
        save.Ticks++;
        save.Worst = Math.Max(save.Worst, spent);
        if (save.Next < save.Structures.Count)
            return;

        FinishSlicedSave(save, (save.Outpost, outpost));
    }

    private void FinishSlicedSave(SlicedSave save, Entity<WFOutpostComponent> outpost)
    {
        if (save.Torn)
        {
            _torn[save.Outpost] = _torn.GetValueOrDefault(save.Outpost) + 1;
            outpost.Comp.NextAutosave = _timing.CurTime + TornRetry;
            EndSlicedSave(save, stored: false);
            return;
        }

        MappingDataNode node;
        try
        {
            node = save.Serializer.Write();
            var category = save.Serializer.GetCategory();
            RaiseLocalEvent(new AfterSerializationEvent(save.Roots, node, category));
            if (category != FileCategory.Grid)
                throw new InvalidOperationException($"Saved as {category}, not as a grid.");
            DropRecordKeys(node);

            // What lies below is small beside the outpost, and is taken whole.
            SerializeDepths(outpost, node, SliceOptions());
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save outpost {ToPrettyString(outpost)}: {e}");
            EndSlicedSave(save, stored: false);
            return;
        }

        _torn.Remove(save.Outpost);
        var stored = new WFStoredOutpostSave(save.Slot, save.Name, save.Tiles, save.Entities, save.Appraisal, _ticker.RoundId, 0,
            DateTime.UtcNow, save.Lineage);
        outpost.Comp.Slot = save.Slot;
        outpost.Comp.SavedFingerprint = save.Fingerprint;
        outpost.Comp.NextAutosave = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(Content.Shared._WF.Outposts.OutpostCVars.AutosaveInterval));
        EndSlicedSave(save, stored: true);

        // Store takes the owner's busy mark off again once the save is written.
        _busy.Add(save.Owner);
        _ = Store(null, save.Owner, stored, node, save.Name);
        _adminLog.Add(LogType.WFOutpost, LogImpact.Low,
            $"Outpost {ToPrettyString(outpost):grid} was autosaved into slot {save.Slot} over {save.Ticks} ticks, appraised at {save.Appraisal}");
    }

    private void EndSlicedSave(SlicedSave save, bool stored)
    {
        _busy.Remove(save.Owner);
        _sliced = null;
        LastSlicedSave = (save.Done.Count, save.Ms, save.Ticks, save.Worst, !stored && save.Torn);
        Log.Info($"Sliced save of {save.Name}: {save.Done.Count} objects, {save.Ms:0} ms over {save.Ticks} ticks, longest {save.Worst:0.0} ms"
                 + (stored ? "." : save.Torn ? "; torn, so dropped." : "; dropped."));
    }

    /// <summary>Drops the sliced save under way, for a save at once to take its place: the round is ending.</summary>
    private void AbortSlicedSave()
    {
        if (_sliced is { } save)
            EndSlicedSave(save, stored: false);
    }

    /// <summary>
    /// The owner's free save slot an outpost that has none is given, so it is kept without its owner filing it by hand;
    /// false while the owner's slots are still being read, where every slot holds another outpost, or where nothing asks
    /// for outposts to keep themselves.
    /// </summary>
    private bool EnsureSlot(EntityUid uid, WFOutpostComponent outpost)
    {
        if (outpost.Slot != null)
            return true;

        var wants = new WFOutpostWantsSlotEvent();
        RaiseLocalEvent(uid, ref wants);
        if (!wants.Wanted || GetSlots(outpost.Owner) is not { } slots)
            return false;

        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
                continue;

            outpost.Slot = i;
            return true;
        }

        return false;
    }
}
