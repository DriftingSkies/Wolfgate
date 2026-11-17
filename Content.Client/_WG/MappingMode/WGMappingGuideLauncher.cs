using Content.Client.UserInterface.Systems.Guidebook;
using Content.Shared._WG.MappingMode;
using Content.Shared.Guidebook;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.MappingMode;

/// <summary>Opens the guidebook on the mapping server guide, showing only its pages.</summary>
public static class WGMappingGuideLauncher
{
    public static void Open()
    {
        IoCManager.Resolve<IUserInterfaceManager>().GetUIController<GuidebookUIController>()
            .OpenGuidebook(new List<ProtoId<GuideEntryPrototype>> { WGMappingGuide.Root }, selected: WGMappingGuide.Root);
    }
}
