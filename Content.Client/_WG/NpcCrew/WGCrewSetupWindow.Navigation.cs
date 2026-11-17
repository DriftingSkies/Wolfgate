using System.Globalization;
using System.Linq;
using Content.Shared._WG.NpcCrew;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.NpcCrew;

public sealed partial class WGCrewSetupWindow
{
    private bool ValidateNavigation(WGCrewMission mission)
    {
        if (mission.Navigation.IsValid())
            return true;
        var invalid = WGCrewNavigationSettings.Fields.FirstOrDefault(field =>
            !float.IsFinite(field.Get(mission.Navigation)) || field.Get(mission.Navigation) < field.Min
            || field.Get(mission.Navigation) > field.Max);
        Plain(_status, invalid == null ? Text("navigation-invalid-hold")
            : Text("navigation-invalid-value", ("setting", Text($"navigation-{invalid.Id}")), ("min", invalid.Min), ("max", invalid.Max)));
        return false;
    }

    /// <summary>A collapsed, categorized editor with independent copies of reusable navigation profiles.</summary>
    private sealed class NavigationForm
    {
        public readonly Collapsible Body;
        private readonly OptionButton _profile = new() { Filterable = true };
        private readonly Dictionary<string, LineEdit> _inputs = new();
        private readonly List<WGCrewNavigationSettings> _profiles = new();
        private bool _loading;

        public NavigationForm(IPrototypeManager prototypes)
        {
            var contents = Column(10);
            contents.AddChild(Help("navigation-help"));
            _profile.AddItem(Text("navigation-custom"), -1);
            _profile.AddItem(Text("navigation-defaults"), 0);
            _profiles.Add(new WGCrewNavigationSettings());
            foreach (var profile in prototypes.EnumeratePrototypes<WGCrewNavigationProfilePrototype>().OrderBy(profile => profile.ID))
            {
                _profile.AddItem(Loc.GetString(profile.Name), _profiles.Count);
                _profiles.Add(profile.Settings.Clone());
            }
            _profile.OnItemSelected += args =>
            {
                _profile.SelectId(args.Id);
                if (args.Id >= 0 && args.Id < _profiles.Count)
                    Load(_profiles[args.Id]);
            };
            contents.AddChild(Line("navigation-profile", _profile));
            contents.AddChild(Button("navigation-reset", () => Load(new WGCrewNavigationSettings())));
            foreach (var category in WGCrewNavigationSettings.Fields.GroupBy(field => field.Category))
            {
                var fields = Column();
                foreach (var field in category)
                {
                    var input = new LineEdit { ToolTip = Text("navigation-bounds", ("min", field.Min), ("max", field.Max)) };
                    input.OnTextChanged += _ =>
                    {
                        if (!_loading)
                            _profile.SelectId(-1);
                    };
                    _inputs.Add(field.Id, input);
                    fields.AddChild(Line($"navigation-{field.Id}", input));
                }
                contents.AddChild(new Collapsible(Text($"navigation-category-{category.Key}"),
                    new CollapsibleBody { Children = { fields } }));
            }
            Body = new Collapsible(Text("navigation-tuning"), new CollapsibleBody { Children = { contents } });
            Load(new WGCrewNavigationSettings());
        }

        /// <summary>Reads a new settings instance; unfinished numbers remain invalid until corrected.</summary>
        public WGCrewNavigationSettings Read()
        {
            var settings = new WGCrewNavigationSettings();
            foreach (var field in WGCrewNavigationSettings.Fields)
                field.Set(settings, Number(_inputs[field.Id]));
            return settings;
        }

        /// <summary>Copies a profile into editable values without changing its source or another crew's draft.</summary>
        public void Load(WGCrewNavigationSettings source)
        {
            var settings = source.Clone();
            _loading = true;
            foreach (var field in WGCrewNavigationSettings.Fields)
                _inputs[field.Id].Text = field.Get(settings).ToString("R", CultureInfo.InvariantCulture);
            _loading = false;
            _profile.SelectId(_profiles.FindIndex(profile => WGCrewNavigationSettings.Fields.All(field => field.Get(profile) == field.Get(settings))));
        }
    }
}
