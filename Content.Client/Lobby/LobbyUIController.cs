using System.Linq;
using Content.Client._Mono.Company; // Mono
using Content.Client._Mono.MonoCoins;
using Content.Client._WF.InRoundCreator; // WOLFGATE(InRoundCreator)
using Content.Client.Gameplay; // WOLFGATE(InRoundCreator)
using Content.Client.Guidebook;
using Content.Client.Humanoid;
using Content.Client.Inventory;
using Content.Client.Lobby.UI;
using Content.Client.Players.PlayTimeTracking;
using Content.Client.Station;
using Content.Shared._Mono.Company;
using Content.Shared.CCVar;
using Content.Shared.Clothing;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Lobby;

// WOLFGATE(InRoundCreator): IOnStateExited<GameplayState> added
public sealed partial class LobbyUIController : UIController, IOnStateEntered<LobbyState>, IOnStateExited<LobbyState>, IOnStateExited<GameplayState>
{
    [Dependency] private IClientPreferencesManager _preferencesManager = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IFileDialogManager _dialogManager = default!;
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private IStateManager _stateManager = default!;
    [Dependency] private JobRequirementsManager _requirements = default!;
    [Dependency] private MarkingManager _markings = default!;
    [Dependency] private CompanyManager _companyManager = default!; // Mono
    [Dependency] private MonoCoinsManager _monoCoins = default!; // Mono
    [UISystemDependency] private readonly HumanoidAppearanceSystem _humanoid = default!;
    [UISystemDependency] private readonly ClientInventorySystem _inventory = default!;
    [UISystemDependency] private readonly StationSpawningSystem _spawn = default!;
    [UISystemDependency] private readonly GuidebookSystem _guide = default!;

    private CharacterSetupGui? _characterSetup;
    private HumanoidProfileEditor? _profileEditor;
    private CharacterSetupGuiSavePanel? _savePanel;

    /// <summary>
    /// This is the characher preview panel in the chat. This should only update if their character updates.
    /// </summary>
    private LobbyCharacterPreviewPanel? PreviewPanel => GetLobbyPreview();

    /// <summary>
    /// This is the modified profile currently being edited.
    /// </summary>
    private HumanoidCharacterProfile? EditedProfile => _profileEditor?.Profile;

    private int? EditedSlot => _profileEditor?.CharacterSlot;

    // WOLFGATE(InRoundCreator): slot picked in the in-round creator; picking there must not select the character
    private int? _inRoundSlot;

    // WOLFGATE(InRoundCreator): holds the creator during a round
    private InRoundCreatorWindow? _inRoundWindow;

    public override void Initialize()
    {
        base.Initialize();
        _prototypeManager.PrototypesReloaded += OnProtoReload;
        _preferencesManager.OnServerDataLoaded += PreferencesDataLoaded;
        _requirements.Updated += OnRequirementsUpdated;

        _configurationManager.OnValueChanged(CCVars.FlavorText, args =>
        {
            _profileEditor?.RefreshFlavorText();
        });

        _configurationManager.OnValueChanged(CCVars.GameRoleTimers, _ => RefreshProfileEditor());

        _configurationManager.OnValueChanged(CCVars.GameRoleWhitelist, _ => RefreshProfileEditor());
    }

    private bool _monoCoinsSubscribed = false;

    /// <summary>
    /// Safely subscribes to MonoCoins balance updates if not already subscribed.
    /// </summary>
    private void EnsureMonoCoinsSubscription()
    {
        if (!_monoCoinsSubscribed && _monoCoins != null)
        {
            _monoCoins.BalanceUpdated += OnMonoCoinsBalanceUpdated;
            _monoCoinsSubscribed = true;
        }
    }

    /// <summary>
    /// Called when MonoCoins balance is updated from the server.
    /// </summary>
    private void OnMonoCoinsBalanceUpdated(long balance)
    {
        UpdateMonoCoinsDisplay();
    }

    /// <summary>
    /// Updates the MonoCoins display in the lobby preview panel.
    /// </summary>
    private void UpdateMonoCoinsDisplay()
    {
        if (PreviewPanel == null)
            return;

        var balance = _monoCoins?.GetLastKnownBalance() ?? -1;
        PreviewPanel.SetMonoCoinsText(Loc.GetString("server-currency-text", ("balance", balance)));
    }

    private LobbyCharacterPreviewPanel? GetLobbyPreview()
    {
        if (_stateManager.CurrentState is LobbyState lobby)
        {
            return lobby.Lobby?.CharacterPreview;
        }

        return null;
    }

    private void OnRequirementsUpdated()
    {
        if (_profileEditor != null)
        {
            _profileEditor.RefreshAntags();
            _profileEditor.RefreshJobs();
        }
    }

    private void OnProtoReload(PrototypesReloadedEventArgs obj)
    {
        if (_profileEditor != null)
        {
            if (obj.WasModified<AntagPrototype>())
            {
                _profileEditor.RefreshAntags();
            }

            if (obj.WasModified<JobPrototype>() ||
                obj.WasModified<DepartmentPrototype>())
            {
                _profileEditor.RefreshJobs();
            }

            if (obj.WasModified<LoadoutPrototype>() ||
                obj.WasModified<LoadoutGroupPrototype>() ||
                obj.WasModified<RoleLoadoutPrototype>())
            {
                _profileEditor.RefreshLoadouts();
            }

            if (obj.WasModified<SpeciesPrototype>())
            {
                _profileEditor.RefreshSpecies();
            }

            if (obj.WasModified<TraitPrototype>())
            {
                _profileEditor.RefreshTraits();
            }
        }
    }

    private void PreferencesDataLoaded()
    {
        PreviewPanel?.SetLoaded(true);

        if (_stateManager.CurrentState is not LobbyState)
            return;

        ReloadCharacterSetup();
    }

    public void OnStateEntered(LobbyState state)
    {
        PreviewPanel?.SetLoaded(_preferencesManager.ServerDataLoaded);
        ReloadCharacterSetup();

        // Ensure MonoCoins subscription and request balance when entering lobby
        EnsureMonoCoinsSubscription();
        _monoCoins?.RequestBalance();
    }

    public void OnStateExited(LobbyState state)
    {
        PreviewPanel?.SetLoaded(false);
        _profileEditor?.Dispose();
        _characterSetup?.Dispose();

        _characterSetup = null;
        _profileEditor = null;
    }

    // WOLFGATE(InRoundCreator) START: the creator opened from the game menu, over the round
    /// <summary>
    /// Opens the creator in a window during a round, so chat stays usable. Any character can be edited, but the
    /// selected one stays selected; edits apply on the next spawn.
    /// </summary>
    public void OpenInRoundCharacterSetup()
    {
        if (_stateManager.CurrentState is not GameplayState || !_preferencesManager.ServerDataLoaded)
            return;

        if (_inRoundWindow is { IsOpen: true })
        {
            _inRoundWindow.MoveToFront();
            return;
        }

        var (characterGui, _) = EnsureGui();

        if (_inRoundWindow == null)
        {
            characterGui.SetInRound();

            // Sized to the screen with a margin; the creator was laid out for a full screen.
            var screen = UIManager.RootControl.Size;
            _inRoundWindow = new InRoundCreatorWindow
            {
                Title = Loc.GetString("character-setup-gui-character-setup-label"),
                SetSize = new System.Numerics.Vector2(Math.Min(1500f, screen.X - 80f), Math.Min(900f, screen.Y - 80f)),
                CloseRequested = RequestCloseProfileEditor,
            };
            _inRoundWindow.Contents.AddChild(characterGui);
        }

        _inRoundSlot = null;
        _inRoundWindow.OpenCentered();
        ReloadCharacterSetup();
    }

    /// <summary>
    /// Closes the creator, asking first if there are unsaved edits.
    /// </summary>
    private void RequestCloseProfileEditor()
    {
        if (_profileEditor is { Profile: not null, IsDirty: true })
            OpenSavePanel(CloseProfileEditor);
        else
            CloseProfileEditor();
    }

    public void OnStateExited(GameplayState state)
    {
        _savePanel?.Close();
        _profileEditor?.Dispose();
        _characterSetup?.Dispose();

        if (_inRoundWindow != null)
        {
            _inRoundWindow.CloseRequested = null;
            _inRoundWindow.Dispose();
        }

        _characterSetup = null;
        _profileEditor = null;
        _inRoundWindow = null;
        _inRoundSlot = null;
    }

    /// <summary>
    /// The slot the creator shows: the selected character, or in a round the one picked from the slot list.
    /// </summary>
    private int? ShownSlot()
    {
        var prefs = _preferencesManager.Preferences;

        if (_inRoundSlot is { } slot && _stateManager.CurrentState is GameplayState && prefs?.Characters.ContainsKey(slot) == true)
            return slot;

        return prefs?.SelectedCharacterIndex;
    }
    // WOLFGATE END

    /// <summary>
    /// Reloads every single character setup control.
    /// </summary>
    public void ReloadCharacterSetup()
    {
        RefreshLobbyPreview();
        var (characterGui, profileEditor) = EnsureGui();
        // WOLFGATE(InRoundCreator) START: shows the picked slot, which in a round need not be the selected one
        // characterGui.ReloadCharacterPickers();
        // profileEditor.SetProfile(
        //     (HumanoidCharacterProfile?) _preferencesManager.Preferences?.SelectedCharacter,
        //     _preferencesManager.Preferences?.SelectedCharacterIndex);
        var slot = ShownSlot();
        characterGui.EditedSlot = slot;
        characterGui.ReloadCharacterPickers();
        profileEditor.SetProfile(
            slot == null ? null : _preferencesManager.Preferences?.Characters.GetValueOrDefault(slot.Value) as HumanoidCharacterProfile,
            slot);
        // WOLFGATE END
    }

    /// <summary>
    /// Refreshes the character preview in the lobby chat.
    /// </summary>
    private void RefreshLobbyPreview()
    {
        if (PreviewPanel == null)
            return;

        // Get selected character, load it, then set it
        var character = _preferencesManager.Preferences?.SelectedCharacter;

        if (character is not HumanoidCharacterProfile humanoid)
        {
            PreviewPanel.SetSprite(EntityUid.Invalid);
            PreviewPanel.SetSummaryText(string.Empty);
            PreviewPanel.SetBankBalanceText(string.Empty); // Frontier
            PreviewPanel.SetCompanyText(string.Empty); // Company Display
            PreviewPanel.SetMonoCoinsText("server-currency-loading"); // MonoCoins Display
            return;
        }

        // Verify company exists, if not set it to "None"
        if (!string.IsNullOrEmpty(humanoid.Company) &&
            humanoid.Company != "None" &&
            !_prototypeManager.HasIndex<CompanyPrototype>(humanoid.Company))
        {
            // Create a new profile with the company set to "None"
            humanoid = humanoid.WithCompany("None");

            // Update the character in preferences
            if (_preferencesManager.Preferences != null)
            {
                _preferencesManager.UpdateCharacter(humanoid, _preferencesManager.Preferences.SelectedCharacterIndex);
            }
        }

        var dummy = LoadProfileEntity(humanoid, null, true);
        PreviewPanel.SetSprite(dummy);
        PreviewPanel.SetSummaryText(humanoid.Summary);
        PreviewPanel.SetBankBalanceText(humanoid.BankBalanceText); // Frontier

        // Company Display
        var companyId = humanoid.Company;
        if (_prototypeManager.TryIndex<CompanyPrototype>(companyId, out var company))
        {
            PreviewPanel.SetCompanyText($"[color=white]Company:[/color] [color={company.Color.ToHex()}]{company.Name}[/color]");
        }
        else
        {
            PreviewPanel.SetCompanyText($"[color=white]Company:[/color] [color=yellow]{companyId}[/color]");
        }

        // MonoCoins Display - Request balance from server and update display
        EnsureMonoCoinsSubscription();
        _monoCoins?.RequestBalance();
        UpdateMonoCoinsDisplay();
    }

    private void RefreshProfileEditor()
    {
        _profileEditor?.RefreshAntags();
        _profileEditor?.RefreshJobs();
        _profileEditor?.RefreshLoadouts();
    }

    private void SaveProfile()
    {
        DebugTools.Assert(EditedProfile != null);

        if (EditedProfile == null || EditedSlot == null)
            return;

        var selected = _preferencesManager.Preferences?.SelectedCharacterIndex;

        if (selected == null)
            return;

        _preferencesManager.UpdateCharacter(EditedProfile, EditedSlot.Value);
        ReloadCharacterSetup();
    }

    private void CloseProfileEditor()
    {
        if (_profileEditor == null)
            return;

        _profileEditor.SetProfile(null, null);
        _profileEditor.Visible = false;

        // WOLFGATE(InRoundCreator): in a round the creator lives in its own window
        if (_stateManager.CurrentState is GameplayState)
        {
            _inRoundWindow?.ForceClose();
            _inRoundSlot = null;
        }

        if (_stateManager.CurrentState is LobbyState lobbyGui)
        {
            lobbyGui.SwitchState(LobbyGui.LobbyGuiState.Default);
        }
    }

    // WOLFGATE(Humanoid) START: onContinue, so switching and creating a character reuse this prompt
    /// <summary>
    /// Asks about unsaved changes, then runs <paramref name="onContinue"/>. Used by closing the editor,
    /// switching character and creating one, so none of them can drop edits silently. Cancel keeps the editor.
    /// </summary>
    private void OpenSavePanel(Action onContinue)
    // WOLFGATE END
    {
        if (_savePanel is { IsOpen: true })
            return;

        _savePanel = new CharacterSetupGuiSavePanel();

        _savePanel.SaveButton.OnPressed += _ =>
        {
            // WOLFGATE(Genitals) START: the editor's anatomy confirmation guards this save too; Cancel returns to the editor unsaved.
            if (_profileEditor != null && _profileEditor.AnatomyClearedOnSave())
            {
                _savePanel.Close();
                _profileEditor.OpenAnatomySaveConfirm(() =>
                {
                    SaveProfile();
                    onContinue();
                });
                return;
            }
            // WOLFGATE END

            SaveProfile();

            _savePanel.Close();

            onContinue(); // WOLFGATE(Humanoid): was CloseProfileEditor()
        };

        _savePanel.NoSaveButton.OnPressed += _ =>
        {
            _savePanel.Close();

            onContinue(); // WOLFGATE(Humanoid): was CloseProfileEditor()
        };

        // WOLFGATE(Humanoid): Cancel goes back to the editor with the edits intact.
        _savePanel.CancelButton.OnPressed += _ => _savePanel.Close();

        _savePanel.OpenCentered();
    }

    private (CharacterSetupGui, HumanoidProfileEditor) EnsureGui()
    {
        if (_characterSetup != null && _profileEditor != null)
        {
            _characterSetup.Visible = true;
            _profileEditor.Visible = true;
            return (_characterSetup, _profileEditor);
        }

        _profileEditor = new HumanoidProfileEditor(
            _preferencesManager,
            _configurationManager,
            EntityManager,
            _dialogManager,
            _logManager,
            _playerManager,
            _prototypeManager,
            _resourceCache,
            _requirements,
            _markings,
            _companyManager); // Mono

        _profileEditor.OnOpenGuidebook += _guide.OpenHelp;

        _characterSetup = new CharacterSetupGui(_profileEditor);

        _characterSetup.CloseButton.OnPressed += _ =>
        {
            // Open the save panel if we have unsaved changes.
            if (_profileEditor.Profile != null && _profileEditor.IsDirty)
            {
                OpenSavePanel(CloseProfileEditor); // WOLFGATE(Humanoid): was OpenSavePanel()

                return;
            }

            // Reset sliders etc.
            CloseProfileEditor();
        };

        _profileEditor.Save += SaveProfile;

        // WOLFGATE(Humanoid) START: switching used to drop unsaved edits without asking.
        _characterSetup.SelectCharacter += args =>
        {
            void Switch()
            {
                // WOLFGATE(InRoundCreator): in a round the slot is only shown, the server refuses to select it
                if (_stateManager.CurrentState is GameplayState)
                    _inRoundSlot = args;
                else
                    _preferencesManager.SelectCharacter(args);
                ReloadCharacterSetup();
            }

            if (_profileEditor.Profile != null && _profileEditor.IsDirty)
                OpenSavePanel(Switch);
            else
                Switch();
        };
        // WOLFGATE END

        // WOLFGATE(Humanoid) START: creating a character used to drop unsaved edits without asking.
        _characterSetup.NewCharacter += () =>
        {
            void Create()
            {
                _preferencesManager.CreateCharacter(HumanoidCharacterProfile.Random());
                _characterSetup?.ReloadCharacterPickers();
            }

            if (_profileEditor.Profile != null && _profileEditor.IsDirty)
                OpenSavePanel(Create);
            else
                Create();
        };
        // WOLFGATE END

        _characterSetup.DeleteCharacter += args =>
        {
            _preferencesManager.DeleteCharacter(args);

            // Reload everything
            if (EditedSlot == args)
            {
                ReloadCharacterSetup();
            }
            else
            {
                // Only need to reload character pickers
                _characterSetup?.ReloadCharacterPickers();
            }
        };

        if (_stateManager.CurrentState is LobbyState lobby)
        {
            lobby.Lobby?.CharacterSetupState.AddChild(_characterSetup);
        }

        return (_characterSetup, _profileEditor);
    }

    #region Helpers

    /// <summary>
    /// Applies the highest priority job's clothes to the dummy.
    /// </summary>
    public void GiveDummyJobClothesLoadout(EntityUid dummy, JobPrototype? jobProto, HumanoidCharacterProfile profile)
    {
        var job = jobProto ?? GetPreferredJob(profile);
        GiveDummyJobClothes(dummy, profile, job);

        if (_prototypeManager.HasIndex<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(job.ID)))
        {
            var loadout = profile.GetLoadoutOrDefault(LoadoutSystem.GetJobPrototype(job.ID), _playerManager.LocalSession, profile.Species, EntityManager, _prototypeManager);
            GiveDummyLoadout(dummy, loadout);
        }
    }

    /// <summary>
    /// Gets the highest priority job for the profile.
    /// </summary>
    public JobPrototype GetPreferredJob(HumanoidCharacterProfile profile)
    {
        var highPriorityJob = profile.JobPriorities.FirstOrDefault(p => p.Value == JobPriority.High).Key;
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract (what is resharper smoking?)
        return _prototypeManager.Index<JobPrototype>(highPriorityJob.Id ?? SharedGameTicker.FallbackOverflowJob);
    }

    public void GiveDummyLoadout(EntityUid uid, RoleLoadout? roleLoadout)
    {
        if (roleLoadout == null)
            return;

        foreach (var group in roleLoadout.SelectedLoadouts.Values)
        {
            foreach (var loadout in group)
            {
                if (!_prototypeManager.TryIndex(loadout.Prototype, out var loadoutProto))
                    continue;

                _spawn.EquipStartingGear(uid, loadoutProto);
            }
        }
    }

    /// <summary>
    /// Applies the specified job's clothes to the dummy.
    /// </summary>
    public void GiveDummyJobClothes(EntityUid dummy, HumanoidCharacterProfile profile, JobPrototype job)
    {
        if (!_inventory.TryGetSlots(dummy, out var slots))
            return;

        // Apply loadout
        if (profile.Loadouts.TryGetValue(job.ID, out var jobLoadout))
        {
            foreach (var loadouts in jobLoadout.SelectedLoadouts.Values)
            {
                foreach (var loadout in loadouts)
                {
                    if (!_prototypeManager.TryIndex(loadout.Prototype, out var loadoutProto))
                        continue;

                    // TODO: Need some way to apply starting gear to an entity and replace existing stuff coz holy fucking shit dude.
                    foreach (var slot in slots)
                    {
                        // Try startinggear first
                        if (_prototypeManager.TryIndex(loadoutProto.StartingGear, out var loadoutGear))
                        {
                            var itemType = ((IEquipmentLoadout) loadoutGear).GetGear(slot.Name);

                            if (_inventory.TryUnequip(dummy, slot.Name, out var unequippedItem, silent: true, force: true, reparent: false))
                            {
                                EntityManager.DeleteEntity(unequippedItem.Value);
                            }

                            if (itemType != string.Empty)
                            {
                                var item = EntityManager.SpawnEntity(itemType, MapCoordinates.Nullspace);
                                _inventory.TryEquip(dummy, item, slot.Name, true, true);
                            }
                        }
                        else
                        {
                            var itemType = ((IEquipmentLoadout) loadoutProto).GetGear(slot.Name);

                            if (_inventory.TryUnequip(dummy, slot.Name, out var unequippedItem, silent: true, force: true, reparent: false))
                            {
                                EntityManager.DeleteEntity(unequippedItem.Value);
                            }

                            if (itemType != string.Empty)
                            {
                                var item = EntityManager.SpawnEntity(itemType, MapCoordinates.Nullspace);
                                _inventory.TryEquip(dummy, item, slot.Name, true, true);
                            }
                        }
                    }
                }
            }
        }

        if (!_prototypeManager.TryIndex(job.StartingGear, out var gear))
            return;

        foreach (var slot in slots)
        {
            var itemType = ((IEquipmentLoadout) gear).GetGear(slot.Name);

            if (_inventory.TryUnequip(dummy, slot.Name, out var unequippedItem, silent: true, force: true, reparent: false))
            {
                EntityManager.DeleteEntity(unequippedItem.Value);
            }

            if (itemType != string.Empty)
            {
                var item = EntityManager.SpawnEntity(itemType, MapCoordinates.Nullspace);
                _inventory.TryEquip(dummy, item, slot.Name, true, true);
            }
        }
    }

    /// <summary>
    /// Loads the profile onto a dummy entity.
    /// </summary>
    public EntityUid LoadProfileEntity(HumanoidCharacterProfile? humanoid, JobPrototype? job, bool jobClothes)
    {
        EntityUid dummyEnt;

        // WOLFGATE START: unknown species falls back to the default
        // A character saved on a build that has a species this one does not must not throw. The picker list
        // is built in one loop that ends with the create-character button, so one bad profile used to hide every
        // character after it and the button with them.
        if (humanoid != null && !_prototypeManager.HasIndex<SpeciesPrototype>(humanoid.Species))
        {
            _logManager.GetSawmill("lobby").Warning(
                $"Character \"{humanoid.Name}\" uses unknown species {humanoid.Species}; previewing it as {SharedHumanoidAppearanceSystem.DefaultSpecies}.");

            humanoid = humanoid.WithSpecies(SharedHumanoidAppearanceSystem.DefaultSpecies);
        }
        // WOLFGATE END

        EntProtoId? previewEntity = null;
        if (humanoid != null && jobClothes)
        {
            job ??= GetPreferredJob(humanoid);

            previewEntity = job.JobPreviewEntity ?? (EntProtoId?)job?.JobEntity;
        }

        if (previewEntity != null)
        {
            // Special type like borg or AI, do not spawn a human just spawn the entity.
            dummyEnt = EntityManager.SpawnEntity(previewEntity, MapCoordinates.Nullspace);
            return dummyEnt;
        }
        else if (humanoid is not null)
        {
            var dummy = _prototypeManager.Index<SpeciesPrototype>(humanoid.Species).DollPrototype;
            dummyEnt = EntityManager.SpawnEntity(dummy, MapCoordinates.Nullspace);
        }
        else
        {
            dummyEnt = EntityManager.SpawnEntity(_prototypeManager.Index<SpeciesPrototype>(SharedHumanoidAppearanceSystem.DefaultSpecies).DollPrototype, MapCoordinates.Nullspace);
        }

        _humanoid.LoadProfile(dummyEnt, humanoid);

        if (humanoid != null && jobClothes)
        {
            DebugTools.Assert(job != null);

            GiveDummyJobClothes(dummyEnt, humanoid, job);

            if (_prototypeManager.HasIndex<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(job.ID)))
            {
                var loadout = humanoid.GetLoadoutOrDefault(LoadoutSystem.GetJobPrototype(job.ID), _playerManager.LocalSession, humanoid.Species, EntityManager, _prototypeManager);
                GiveDummyLoadout(dummyEnt, loadout);
            }
        }

        return dummyEnt;
    }

    #endregion
}
