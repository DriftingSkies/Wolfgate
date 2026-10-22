using Content.Client.UserInterface.Systems.Guidebook;
using Content.Shared._WF.MappingMode;
using Content.Shared.Guidebook;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.MappingMode;

/// <summary>Opens the guidebook on the mapping server guide, showing only its pages.</summary>
public static class WFMappingGuideLauncher
{
    public static void Open()
    {
        IoCManager.Resolve<IUserInterfaceManager>().GetUIController<GuidebookUIController>()
            .OpenGuidebook(new List<ProtoId<GuideEntryPrototype>> { WFMappingGuide.Root }, selected: WFMappingGuide.Root);
    }
}
