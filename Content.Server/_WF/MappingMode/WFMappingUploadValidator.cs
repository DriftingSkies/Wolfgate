using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Content.Server._WF.MappingMode;

/// <summary>
/// Checks an uploaded map file before the map loader sees it: UTF-8 text, one YAML document without anchors or
/// aliases and not nested too deep, a map file's root and an entity count within the cap. Unknown prototypes are
/// reported rather than refused; <see cref="StripMissing"/> takes their entities out.
/// </summary>
public static class WFMappingUploadValidator
{
    /// <summary>Deepest YAML nesting accepted; real map files stay far below it and the parser recurses per level.</summary>
    public const int MaxDepth = 64;

    /// <summary>The top-level lists of yaml uids a map file of format 7 or later keeps.</summary>
    private static readonly string[] RootLists = { "maps", "grids", "orphans", "nullspace" };

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Validates a file and parses it. <paramref name="renamed"/> and <paramref name="deleted"/> are the map
    /// migrations the loader applies, so a renamed prototype passes as its new id. <paramref name="missing"/> gets
    /// the file's prototype ids this server lacks or holds only as abstract, sorted. False with a Fluent id and its
    /// arguments when it is refused.
    /// </summary>
    public static bool TryValidate(byte[] bytes, IPrototypeManager prototypes,
        IReadOnlyDictionary<string, string> renamed, IReadOnlySet<string> deleted, int maxEntities,
        [NotNullWhen(true)] out MappingDataNode? root, out List<string> missing, out string error,
        out (string, object)[] args)
    {
        root = null;
        missing = new List<string>();
        args = Array.Empty<(string, object)>();

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (ArgumentException)
        {
            error = "wf-mapping-mode-upload-not-text";
            return false;
        }

        if (text.Length > 0 && text[0] == '﻿')
            text = text[1..];

        try
        {
            if (!TryScan(text, out error))
                return false;

            var documents = DataNodeParser.ParseYamlStream(new StringReader(text)).ToArray();
            if (documents.Length != 1 ||
                documents[0].Root is not MappingDataNode mapping ||
                !mapping.TryGet<MappingDataNode>("meta", out var meta) ||
                !meta.TryGet<ValueDataNode>("format", out var format) ||
                !int.TryParse(format.Value, out var version) ||
                !mapping.TryGet<SequenceDataNode>("entities", out var entities))
            {
                error = "wf-mapping-mode-upload-not-map";
                return false;
            }

            if (!CheckEntities(entities, version, prototypes, renamed, deleted, maxEntities, missing, out error,
                    out args))
            {
                return false;
            }

            root = mapping;
            return true;
        }
        catch (Exception e) when (e is YamlException or InvalidCastException or NotSupportedException
                                      or InvalidOperationException or ArgumentException)
        {
            error = "wf-mapping-mode-upload-bad-yaml";
            return false;
        }
    }

    /// <summary>Walks the YAML events: one document, no anchor or alias, nesting at most <see cref="MaxDepth"/>.</summary>
    private static bool TryScan(string text, out string error)
    {
        var parser = new Parser(new StringReader(text));
        var documents = 0;
        var depth = 0;
        while (parser.MoveNext())
        {
            switch (parser.Current)
            {
                case AnchorAlias:
                    error = "wf-mapping-mode-upload-anchors";
                    return false;
                case NodeEvent node when !node.Anchor.IsEmpty:
                    error = "wf-mapping-mode-upload-anchors";
                    return false;
                case DocumentStart:
                    if (++documents > 1)
                    {
                        error = "wf-mapping-mode-upload-not-map";
                        return false;
                    }

                    break;
                case MappingStart:
                case SequenceStart:
                    if (++depth > MaxDepth)
                    {
                        error = "wf-mapping-mode-upload-too-deep";
                        return false;
                    }

                    break;
                case MappingEnd:
                case SequenceEnd:
                    depth--;
                    break;
            }
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Counts the entities against the cap and collects unknown or abstract prototypes into <paramref name="missing"/>.</summary>
    private static bool CheckEntities(SequenceDataNode entities, int version, IPrototypeManager prototypes,
        IReadOnlyDictionary<string, string> renamed, IReadOnlySet<string> deleted, int maxEntities,
        List<string> missing, out string error, out (string, object)[] args)
    {
        args = Array.Empty<(string, object)>();
        var key = ProtoKey(version);
        var count = 0;
        var unknown = new HashSet<string>();
        foreach (var node in entities)
        {
            if (node is not MappingDataNode group)
            {
                error = "wf-mapping-mode-upload-not-map";
                return false;
            }

            count += group.TryGet<SequenceDataNode>("entities", out var members) ? members.Count : 1;
            if (!group.TryGet<ValueDataNode>(key, out var protoNode) || string.IsNullOrWhiteSpace(protoNode.Value))
                continue;

            var id = renamed.TryGetValue(protoNode.Value, out var newId) ? newId : protoNode.Value;
            if (deleted.Contains(id))
                continue;

            if (!prototypes.TryIndex<EntityPrototype>(id, out var prototype) || prototype.Abstract)
                unknown.Add(protoNode.Value);
        }

        if (count > maxEntities)
        {
            error = "wf-mapping-mode-limit-entities";
            args = new (string, object)[] { ("max", maxEntities) };
            return false;
        }

        missing.AddRange(unknown.Order(StringComparer.Ordinal));
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Takes the entities of the <paramref name="missing"/> prototypes out of a parsed map file, then every entity
    /// parented to a removed one, round after round until none is left. Their entries in the root uid lists and in
    /// containers go too, and <c>meta.entityCount</c> is set to what remains. <paramref name="dropped"/> is the
    /// number of entities removed.
    /// </summary>
    public static void StripMissing(MappingDataNode root, ISet<string> missing, out int dropped)
    {
        dropped = 0;
        if (!root.TryGet<SequenceDataNode>("entities", out var groups))
            return;

        var version = root.TryGet<MappingDataNode>("meta", out var meta) &&
                      meta.TryGet<ValueDataNode>("format", out var format) &&
                      int.TryParse(format.Value, out var parsed)
            ? parsed
            : 0;
        var key = ProtoKey(version);

        var removedUids = new HashSet<int>();
        var removedNodes = new HashSet<DataNode>(ReferenceEqualityComparer.Instance);
        var survivors = new List<(SequenceDataNode Owner, MappingDataNode Node, int? Uid, int? Parent)>();
        var total = 0;

        void Drop(MappingDataNode node, int? uid)
        {
            removedNodes.Add(node);
            if (uid is { } id)
                removedUids.Add(id);
        }

        foreach (var node in groups)
        {
            if (node is not MappingDataNode group)
                continue;

            var isMissing = group.TryGet<ValueDataNode>(key, out var protoNode) && missing.Contains(protoNode.Value);

            // From format 4 entities sit in groups by prototype; before it each entry is one entity.
            var members = group.TryGet<SequenceDataNode>("entities", out var list)
                ? list.OfType<MappingDataNode>().Select(m => (Owner: list, Node: m))
                : new[] { (Owner: groups, Node: group) };

            foreach (var (owner, member) in members)
            {
                total++;
                var uid = ReadInt(member, "uid");
                if (isMissing)
                    Drop(member, uid);
                else
                    survivors.Add((owner, member, uid, ParentOf(member)));
            }

            if (isMissing)
                removedNodes.Add(group);
        }

        // Children of removed entities go with them, however deep.
        bool more;
        do
        {
            more = false;
            for (var i = survivors.Count - 1; i >= 0; i--)
            {
                var survivor = survivors[i];
                if (survivor.Parent is not { } parent || !removedUids.Contains(parent))
                    continue;

                Drop(survivor.Node, survivor.Uid);
                survivors.RemoveAt(i);
                more = true;
            }
        } while (more);

        dropped = total - survivors.Count;

        RemoveNodes(groups, removedNodes);
        foreach (var node in groups)
        {
            if (node is MappingDataNode group && group.TryGet<SequenceDataNode>("entities", out var list))
                RemoveNodes(list, removedNodes);
        }

        for (var i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i] is MappingDataNode group &&
                group.TryGet<SequenceDataNode>("entities", out var list) &&
                list.Count == 0)
            {
                groups.RemoveAt(i);
            }
        }

        foreach (var name in RootLists)
        {
            if (root.TryGet<SequenceDataNode>(name, out var ids))
                RemoveUids(ids, removedUids);
        }

        foreach (var survivor in survivors)
        {
            ClearContainers(survivor.Node, removedUids);
        }

        if (meta != null && meta.Has("entityCount"))
            meta["entityCount"] = new ValueDataNode(survivors.Count.ToString(CultureInfo.InvariantCulture));
    }

    private static string ProtoKey(int version)
    {
        return version >= 4 ? "proto" : "type";
    }

    private static int? ReadInt(MappingDataNode node, string key)
    {
        return node.TryGet<ValueDataNode>(key, out var value) &&
               int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    /// <summary>The yaml uid in an entity's Transform <c>parent</c>, if it has one.</summary>
    private static int? ParentOf(MappingDataNode entity)
    {
        var transform = Component(entity, "Transform");
        return transform == null ? null : ReadInt(transform, "parent");
    }

    private static MappingDataNode? Component(MappingDataNode entity, string type)
    {
        if (!entity.TryGet<SequenceDataNode>("components", out var components))
            return null;

        foreach (var node in components)
        {
            if (node is MappingDataNode component &&
                component.TryGet<ValueDataNode>("type", out var name) &&
                name.Value == type)
            {
                return component;
            }
        }

        return null;
    }

    /// <summary>Drops removed entities from an entity's containers: <c>ents</c> lists and <c>ent</c> slots.</summary>
    private static void ClearContainers(MappingDataNode entity, HashSet<int> removed)
    {
        if (Component(entity, "ContainerContainer") is not { } component ||
            !component.TryGet<MappingDataNode>("containers", out var containers))
        {
            return;
        }

        foreach (var (_, node) in containers)
        {
            if (node is not MappingDataNode container)
                continue;

            if (container.TryGet<SequenceDataNode>("ents", out var ents))
                RemoveUids(ents, removed);

            if (ReadInt(container, "ent") is { } slot && removed.Contains(slot))
                container["ent"] = ValueDataNode.Null();
        }
    }

    private static void RemoveUids(SequenceDataNode ids, HashSet<int> removed)
    {
        for (var i = ids.Count - 1; i >= 0; i--)
        {
            if (ids[i] is ValueDataNode value &&
                int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                removed.Contains(id))
            {
                ids.RemoveAt(i);
            }
        }
    }

    private static void RemoveNodes(SequenceDataNode sequence, HashSet<DataNode> removed)
    {
        for (var i = sequence.Count - 1; i >= 0; i--)
        {
            if (removed.Contains(sequence[i]))
                sequence.RemoveAt(i);
        }
    }
}
