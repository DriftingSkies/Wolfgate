using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Client.Guidebook.Richtext;
using Content.Shared._WG.SafetyDepositBox.Components;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Whitelist;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.SafetyDepositBox;

/// <summary>Guidebook list of what a safety deposit box refuses, or with <c>List="Allowed"</c> what it lets in anyway.</summary>
/// <remarks>
/// Read from the box prototype's storage blacklist and allow list. A component or tag with no
/// <c>safety-deposit-refused-component-*</c> or <c>safety-deposit-refused-tag-*</c> string is listed under its raw
/// name, so a new entry shows up without touching the guide.
/// </remarks>
[UsedImplicitly]
public sealed partial class SafetyDepositRefusedEmbed : BoxContainer, IDocumentTag
{
    private const string BoxProto = "SafetyDepositBoxSmall";

    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IComponentFactory _factory = default!;

    public SafetyDepositRefusedEmbed()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
    }

    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        control = null;

        if (!_prototype.TryIndex<EntityPrototype>(BoxProto, out var proto) ||
            !proto.TryComp<StorageComponent>(out var storage, _factory) ||
            storage.Blacklist is not { } blacklist)
        {
            IoCManager.Resolve<ILogManager>().GetSawmill("wg.safetydeposit").Error($"Safety deposit refused list: {BoxProto} has no storage blacklist");
            return false;
        }

        proto.TryComp<SafetyDepositAllowListComponent>(out var allowList, _factory);

        var lines = args.TryGetValue("List", out var list) && list == "Allowed"
            ? AllowedLines(allowList)
            : RefusedLines(blacklist, allowList?.Restricted);

        foreach (var line in lines.Where(l => !string.IsNullOrEmpty(l)).Distinct().Order())
        {
            AddChild(new Label { Text = $"  - {line}" });
        }

        control = this;
        return true;
    }

    private static List<string> RefusedLines(EntityWhitelist blacklist, EntityWhitelist? restricted)
    {
        var lines = new List<string>();
        foreach (var list in new[] { blacklist, restricted })
        {
            if (list == null)
                continue;

            foreach (var name in list.Components ?? Array.Empty<string>())
            {
                lines.Add(LocName("safety-deposit-refused-component-", name));
            }

            foreach (var tag in list.Tags ?? new List<ProtoId<Shared.Tag.TagPrototype>>())
            {
                lines.Add(LocName("safety-deposit-refused-tag-", tag.Id));
            }
        }

        return lines;
    }

    private List<string> AllowedLines(SafetyDepositAllowListComponent? allowList)
    {
        var lines = new List<string>();
        if (allowList == null)
            return lines;

        foreach (var stack in allowList.Stacks)
        {
            if (_prototype.TryIndex(stack, out var stackProto) && _prototype.TryIndex(stackProto.Spawn, out var cash))
                lines.Add(cash.Name);
        }

        foreach (var id in allowList.Prototypes)
        {
            if (_prototype.TryIndex(id, out var entity))
                lines.Add(entity.Name);
        }

        return lines;
    }

    private static string LocName(string prefix, string id)
    {
        return IoCManager.Resolve<ILocalizationManager>().TryGetString(prefix + id.ToLowerInvariant(), out var text) ? text : id;
    }
}
