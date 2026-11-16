using Content.Shared.Examine;
using Robust.Shared.IoC; // WOLFGATE: step names may be loc keys or plain text
using Robust.Shared.Localization; // WOLFGATE: step names may be loc keys or plain text
using Robust.Shared.Utility;

namespace Content.Shared.Construction.Steps
{
    public abstract partial class ArbitraryInsertConstructionGraphStep : EntityInsertConstructionGraphStep
    {
        [DataField("name")] public string Name { get; private set; } = string.Empty;

        [DataField("icon")] public SpriteSpecifier? Icon { get; private set; }

        public override void DoExamine(ExaminedEvent examinedEvent)
        {
            if (string.IsNullOrEmpty(Name))
                return;

            // WOLFGATE: step names may be loc keys or plain text
            var localization = IoCManager.Resolve<ILocalizationManager>();
            var stepName = localization.TryGetString(Name, out var localized) ? localized : Name;
            examinedEvent.PushMarkup(Loc.GetString("construction-insert-arbitrary-entity", ("stepName", stepName)));
        }

        public override ConstructionGuideEntry GenerateGuideEntry()
        {
            return new ConstructionGuideEntry
            {
                Localization = "construction-presenter-arbitrary-step",
                Arguments = new (string, object)[]{("name", Name)},
                Icon = Icon,
            };
        }
    }
}
