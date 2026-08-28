using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyEditor.SaveEditor.Gui;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Gui.ViewModels;

/// <summary>
/// Root view-model for the whole window. Owns the loaded <see cref="RBSave"/>, every persistent
/// collection (Friend Areas and their residents), and the "which detail pane is showing" selection
/// state. Collections here are built once per file load (<see cref="BuildViewModel"/>) and only
/// ever Add/Remove afterward for genuine membership changes (a new recruit, a newly-populated
/// synthetic bucket) -- never wholesale-replaced -- which is what actually fixes the freeze/
/// chugging/hover-flicker bugs the old code-behind rebuild-everything pattern caused.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    public RBSave? Save { get; private set; }
    private string? _currentPath;

    /// <summary>Reference-identity keyed so brand-new entries (SlotIndex not yet meaningful) are
    /// still safe to key by, and so the same instance is reused everywhere a given Pokemon shows
    /// up (flat roster, its Friend Area's residents, the detail pane).</summary>
    private readonly Dictionary<RBStoredPokemon, RosterEntryViewModel> _rosterByPokemon = new();

    [ObservableProperty] private bool _isSaveOpen;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _pathText = "No save open";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _pendingSummaryText = "";

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(WindowTitle));
    public string WindowTitle => IsDirty ? "Sky Editor - Red/Blue Rescue Team *" : "Sky Editor - Red/Blue Rescue Team";

    // General tab
    [ObservableProperty] private string _teamNameText = "";
    [ObservableProperty] private string _heldMoneyText = "";
    [ObservableProperty] private string _storedMoneyText = "";
    [ObservableProperty] private string _checksumText = "";
    [ObservableProperty] private string _rescuePointsText = "";
    [ObservableProperty] private string _rescueRankText = "";
    [ObservableProperty] private string _rescueRankProgressText = "";
    [ObservableProperty] private int _moneyAmount = 100;
    [ObservableProperty] private bool _targetIsHeldMoney = true;
    [ObservableProperty] private int _rescuePointsAmount = 50;

    public IReadOnlyList<RBRescueTeamRank> Ranks { get; } = Enum.GetValues<RBRescueTeamRank>();

    /// <summary>
    /// Rank and points are coupled both ways, like Level and Exp on a Pokemon: the rank shown is
    /// always derived from the points (the game's own GetRescueTeamRank), and picking a rank
    /// sets the points to the fewest that earn it. Points edited via Apply refresh this through
    /// RefreshGeneral.
    /// </summary>
    public RBRescueTeamRank SelectedRank
    {
        get => Save == null ? RBRescueTeamRank.Normal : RBRescueTeamRanks.RankForPoints(Save.RescueTeamPoints);
        set
        {
            if (Save == null || value == SelectedRank) return;
            Save.RescueTeamPoints = RBRescueTeamRanks.MinPointsFor(value);
            MarkDirty();
            RefreshGeneral();
            StatusText = $"Set Rescue Team points to {Save.RescueTeamPoints}, the minimum for {RBRescueTeamRanks.NameOf(value)}.";
        }
    }

    public ObservableCollection<FriendAreaViewModel> FriendAreas { get; } = new();

    [ObservableProperty] private RosterEntryViewModel? _selectedRosterEntry;
    [ObservableProperty] private FriendAreaViewModel? _selectedFriendArea;

    /// <summary>
    /// The flat sorted list's own ListBox selection -- kept separate from
    /// <see cref="SelectedRosterEntry"/> rather than binding that ListBox's SelectedItem straight to
    /// it. A candidate preview (see <see cref="PreviewCandidate"/>) is a scratch entry that's never
    /// actually a member of <see cref="FlatSortedResidents"/>; when SelectedRosterEntry pointed a
    /// shared TwoWay-bound ListBox.SelectedItem at such an item, the ListBox couldn't find it in its
    /// own items, snapped its selection back to null, and (being TwoWay) wrote that null straight
    /// back into SelectedRosterEntry a moment later -- silently cancelling the preview. This
    /// property only ever flows one way, into SelectedRosterEntry, so previewing something not in
    /// this list can no longer be reverted by it.
    /// </summary>
    [ObservableProperty] private RosterEntryViewModel? _selectedFlatRosterEntry;

    partial void OnSelectedFlatRosterEntryChanged(RosterEntryViewModel? value)
    {
        if (value != null) SelectedRosterEntry = value;
    }

    [ObservableProperty] private SortMode _sortMode = SortMode.FriendArea;

    partial void OnSortModeChanged(SortMode value)
    {
        OnPropertyChanged(nameof(ShowGroupedView));
        RefreshFlatSortedResidents();
    }

    public bool ShowGroupedView => SortMode == SortMode.FriendArea;

    public IReadOnlyList<SortMode> SortModes { get; } = Enum.GetValues<SortMode>();

    /// <summary>Every resident across every Friend Area (including the synthetic "Other Pokemon"
    /// bucket), flattened and sorted per <see cref="SortMode"/> -- shown instead of the grouped
    /// <see cref="FriendAreas"/> list whenever <see cref="ShowGroupedView"/> is false. Re-sorts
    /// references to the same persistent <see cref="RosterEntryViewModel"/> instances each time
    /// (recomputed here rather than incrementally), which is cheap at roster-sized lists and keeps
    /// every existing binding/selection intact -- nothing new is ever constructed by this.</summary>
    public ObservableCollection<RosterEntryViewModel> FlatSortedResidents { get; } = new();

    private void RefreshFlatSortedResidents()
    {
        if (ShowGroupedView) return;

        IEnumerable<RosterEntryViewModel> all = FriendAreas.SelectMany(a => a.Residents);
        IEnumerable<RosterEntryViewModel> sorted = SortMode switch
        {
            SortMode.Name => all.OrderBy(r => r.Species),
            SortMode.Number => all.OrderBy(r => r.SpeciesId),
            SortMode.Level => all.OrderBy(r => r.Level),
            SortMode.IQ => all.OrderBy(r => r.IQ),
            _ => all,
        };

        FlatSortedResidents.Clear();
        foreach (var entry in sorted) FlatSortedResidents.Add(entry);
    }

    /// <summary>
    /// The two selections used to force-clear each other, back when they shared one overlaid
    /// detail region. That's what caused an occasional visible overlap of both panes' content
    /// during the frame the two IsVisible bindings disagreed -- now that the Friend Areas tab
    /// gives each its own always-present area (list+candidates on the left, area art + Pokemon
    /// dossier on the right), they're fully independent: selecting a Pokemon no longer disturbs
    /// which Friend Area is showing, and vice versa. Row highlighting itself is now the native
    /// ListBoxItem `:selected` style rather than a hand-rolled IsSelected flag.
    /// </summary>
    partial void OnSelectedRosterEntryChanged(RosterEntryViewModel? oldValue, RosterEntryViewModel? newValue)
    {
        OnPropertyChanged(nameof(ShowRosterDetail));
        OnPropertyChanged(nameof(ShowRightPanePlaceholder));
    }

    partial void OnSelectedFriendAreaChanged(FriendAreaViewModel? oldValue, FriendAreaViewModel? newValue)
    {
        OnPropertyChanged(nameof(ShowFriendAreaInfo));
        OnPropertyChanged(nameof(ShowRightPanePlaceholder));
    }

    public bool ShowRosterDetail => SelectedRosterEntry != null;
    public bool ShowFriendAreaInfo => SelectedFriendArea != null;

    /// <summary>The right pane always shows Friend Area art above a Pokemon dossier, but needs its
    /// own placeholder for the moment neither is selected yet.</summary>
    public bool ShowRightPanePlaceholder => SelectedFriendArea == null && SelectedRosterEntry == null;

    /// <summary>
    /// Shows a candidate species (clicked from a Friend Area's "who can live here" list) in the
    /// right-hand dossier pane as a read-only preview -- built the same way
    /// <see cref="RecruitCandidate"/> would for real, but never added to the roster or save.
    /// </summary>
    public void PreviewCandidate(RecruitGuideEntry entry)
    {
        SelectedRosterEntry = new RosterEntryViewModel(entry.ToStoredPokemon(), this, isPreview: true);
    }

    public async Task OpenSaveFromPathAsync(string path)
    {
        byte[] bytes;
        RBSave save;
        try
        {
            bytes = await File.ReadAllBytesAsync(path);
            save = new RBSave(bytes);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to open '{path}': {ex.Message}";
            return;
        }

        Save = save;
        _currentPath = path;
        PathText = path;
        IsSaveOpen = true;

        BuildViewModel();
        ClearDirty();
        StatusText = "Opened.";
    }

    public async Task SaveAsync()
    {
        if (Save == null || _currentPath == null) return;

        try
        {
            File.Copy(_currentPath, _currentPath + ".bak", overwrite: true);
            await File.WriteAllBytesAsync(_currentPath, Save.ToByteArray());
            ClearDirty();
            StatusText = $"Saved to {_currentPath} (previous contents backed up to {_currentPath}.bak)";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
    }

    public async Task SaveAsAsync(string newPath)
    {
        if (Save == null) return;

        try
        {
            await File.WriteAllBytesAsync(newPath, Save.ToByteArray());
            _currentPath = newPath;
            PathText = newPath;
            ClearDirty();
            StatusText = $"Saved to {_currentPath}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
    }

    public string? SuggestedFileName => _currentPath != null ? Path.GetFileName(_currentPath) : "save.sav";

    /// <summary>
    /// One-time construction of every persistent collection for a freshly-loaded save. Replaces
    /// the old code-behind's RefreshFriendAreas/RefreshLegacyRoster, which rebuilt these from
    /// scratch on every single interaction instead of just once here.
    /// </summary>
    private void BuildViewModel()
    {
        if (Save == null) return;

        _rosterByPokemon.Clear();
        FriendAreas.Clear();

        RosterEntryViewModel EntryFor(RBStoredPokemon p, bool canDelete)
        {
            if (!_rosterByPokemon.TryGetValue(p, out var entry))
            {
                entry = new RosterEntryViewModel(p, this, canDelete: canDelete) { IsPending = Save.IsSlotPending(p.SlotIndex) };
                _rosterByPokemon[p] = entry;
            }
            return entry;
        }

        // Not deletable: lives in the synthetic "Other Pokemon" bucket, where the
        // protagonist/partner are indistinguishable from any other unmapped recruit.
        var unmapped = Save.StoredPokemon
            .Where(p => RBRecruitGuide.HomeAreaOf(p.ID) == null)
            .OrderBy(p => p.SlotIndex)
            .Select(p => EntryFor(p, canDelete: false))
            .ToList();

        if (unmapped.Count > 0)
        {
            var bucket = new FriendAreaViewModel(this, "Other Pokemon (not tied to a Friend Area)", -1, unmapped.Count, unlocked: true);
            foreach (var r in unmapped) bucket.Residents.Add(r);
            FriendAreas.Add(bucket);
        }

        foreach (var area in Enum.GetValues<RBFriendArea>())
        {
            if (area == RBFriendArea.None) continue;
            var index = (int)area;
            var vm = new FriendAreaViewModel(this, FriendAreaDisplayNames.NameOf(area), index, RBFriendAreaCapacity.Capacity(area), Save.FriendAreasUnlocked[index])
            {
                RawEnumName = area.ToString(),
                IsPending = Save.IsFriendAreaPending(area),
            };
            vm.Candidates = RBRecruitGuide.GetCandidates(area).Select(e => new RecruitCandidateViewModel(e, vm)).ToList();
            foreach (var p in Save.StoredPokemon.Where(p => RBRecruitGuide.HomeAreaOf(p.ID) == area).OrderBy(p => p.SlotIndex))
            {
                vm.Residents.Add(EntryFor(p, canDelete: !BossSpecies.Ids.Contains(p.ID)));
            }
            FriendAreas.Add(vm);
        }

        RefreshGeneral();
        RefreshFlatSortedResidents();
    }

    /// <summary>
    /// Registers a newly-recruited Pokemon into every persistent collection it belongs in -- the
    /// one place actual collection membership changes (Add), as opposed to editing an existing
    /// entry's properties in place.
    /// </summary>
    public RosterEntryViewModel AddRosterEntry(RBStoredPokemon pkm)
    {
        var homeArea = RBRecruitGuide.HomeAreaOf(pkm.ID);
        var canDelete = homeArea.HasValue && !BossSpecies.Ids.Contains(pkm.ID);
        var entry = new RosterEntryViewModel(pkm, this, canDelete: canDelete) { IsPending = Save!.IsSlotPending(pkm.SlotIndex) };
        _rosterByPokemon[pkm] = entry;

        if (homeArea.HasValue)
        {
            FriendAreas.FirstOrDefault(a => a.Index == (int)homeArea.Value)?.Residents.Add(entry);
        }
        else
        {
            var bucket = FriendAreas.FirstOrDefault(a => a.Index == -1);
            if (bucket == null)
            {
                bucket = new FriendAreaViewModel(this, "Other Pokemon (not tied to a Friend Area)", -1, 0, unlocked: true);
                FriendAreas.Insert(0, bucket);
            }
            bucket.Residents.Add(entry);
            bucket.Capacity = bucket.Residents.Count;
        }

        RefreshFlatSortedResidents();
        return entry;
    }

    /// <summary>
    /// Removes a recruited Pokemon from the roster entirely -- the in-tool equivalent of choosing
    /// Farewell in-game. Only ever called for an entry with <see cref="RosterEntryViewModel.CanDelete"/>
    /// true (the "-" button and Delete key are both hidden/no-op otherwise); the confirmation
    /// dialog itself lives in code-behind (needs a TopLevel), so by the time this runs the user has
    /// already confirmed. <see cref="RBSave.StoredPokemon"/>'s own doc comment sanctions
    /// <c>.Remove(...)</c> as the way to release a Pokemon before saving.
    /// </summary>
    public void DeleteResident(RosterEntryViewModel entry)
    {
        if (Save == null || !entry.CanDelete) return;

        var species = entry.Species;
        var wasStaged = entry.IsPending;
        Save.StoredPokemon.Remove(entry.Pkm);
        _rosterByPokemon.Remove(entry.Pkm);
        FriendAreaViewModel? home = null;
        foreach (var area in FriendAreas)
        {
            if (area.Residents.Remove(entry))
            {
                home = area;
                break;
            }
        }

        if (SelectedRosterEntry == entry) SelectedRosterEntry = null;

        // Deleting a staged-but-unsaved addition also reverts the side effects that specific add
        // performed (and only those -- see the two FromAdd properties), so an edit in flight is
        // fully reversible: add + delete before saving nets out to nothing.
        var reverts = "";
        if (wasStaged)
        {
            if (entry.CutsceneFlagFromAdd is { } flag && Save.ExclusivePokemonData.GetCutsceneFlag(flag))
            {
                Save.ExclusivePokemonData.SetCutsceneFlag(flag, false);
                reverts += $" Cleared cutscene flag {flag} it had set.";
            }
            if (entry.AreaUnlockedByAdd is { } unlockedArea
                && home is { IsRealArea: true } homeArea
                && homeArea.Index == (int)unlockedArea
                && homeArea.Residents.Count == 0
                && homeArea.Unlocked)
            {
                homeArea.Unlocked = false;
                reverts += $" Locked {homeArea.Name} again.";
            }
        }

        MarkDirty();
        RefreshFlatSortedResidents();
        StatusText = wasStaged
            ? $"Removed staged {species}; nothing was ever saved.{reverts}"
            : $"Said farewell to {species}.";
    }

    public void MarkBossRecruited(int speciesId, int level)
    {
        if (Save == null) return;

        var name = Lists.RBPokemon.TryGetValue(speciesId, out var speciesName) ? speciesName : $"#{speciesId}";
        var pokemon = new RBStoredPokemon
        {
            ID = speciesId,
            Name = name,
            Level = Math.Clamp(level, 1, 100),
            IQ = 1,
            HP = Math.Clamp(level * 10, 1, 999),
            Attack = Math.Clamp(level * 2, 1, 255),
            SpAttack = Math.Clamp(level * 2, 1, 255),
            Defense = Math.Clamp(level * 2, 1, 255),
            SpDefense = Math.Clamp(level * 2, 1, 255),
            Exp = 0,
            Attack1 = new RBAttack(),
            Attack2 = new RBAttack(),
            Attack3 = new RBAttack(),
            Attack4 = new RBAttack(),
        };
        RBLearnsets.ApplyWildMoveset(pokemon);

        var flagFromAdd = FlagAnAddWouldSet(speciesId);
        var homeArea = RBRecruitGuide.HomeAreaOf(speciesId);
        var areaWasLocked = homeArea.HasValue && !Save.FriendAreasUnlocked[(int)homeArea.Value];

        bool added;
        try
        {
            added = Save.MarkBossRecruited(speciesId, pokemon);
        }
        catch (InvalidOperationException ex)
        {
            StatusText = ex.Message;
            return;
        }

        var hasFlag = RBBossEncounters.CompleteFlagsByBoss.TryGetValue(speciesId, out var flag);

        MarkDirty();
        if (added)
        {
            var rosterEntry = AddRosterEntry(pokemon);
            rosterEntry.CutsceneFlagFromAdd = flagFromAdd;
            rosterEntry.AreaUnlockedByAdd = areaWasLocked ? homeArea : null;
            SelectedRosterEntry = rosterEntry;
        }

        if (homeArea.HasValue)
        {
            var area = FriendAreas.FirstOrDefault(a => a.Index == (int)homeArea.Value);
            if (area != null)
            {
                // MarkBossRecruited auto-unlocked the area in the save when it added the entry;
                // route the same change through the view-model so its state agrees.
                if (added && !area.Unlocked) area.Unlocked = true;
                SelectedFriendArea = area;
            }
        }

        StatusText = added
            ? $"Added {name} (Lv.{pokemon.Level}) to the roster.{(hasFlag ? $" Set cutscene flag {flag}." : "")}"
            : $"{name} was already recruited; roster unchanged.";
    }

    public void RecruitCandidate(RecruitGuideEntry entry)
    {
        if (Save == null) return;

        // Captured before the add so the delete path can revert exactly what this add changed
        // (and nothing that was already true) -- see DeleteResident.
        var flagFromAdd = FlagAnAddWouldSet(entry.SpeciesId);
        var areaWasLocked = !Save.FriendAreasUnlocked[(int)entry.FriendArea];

        RBStoredPokemon pokemon;
        try
        {
            pokemon = Save.RecruitFromGuide(entry);
        }
        catch (InvalidOperationException ex)
        {
            StatusText = ex.Message;
            return;
        }

        MarkDirty();
        var rosterEntry = AddRosterEntry(pokemon);
        rosterEntry.CutsceneFlagFromAdd = flagFromAdd;
        rosterEntry.AreaUnlockedByAdd = areaWasLocked ? entry.FriendArea : null;
        SelectedRosterEntry = rosterEntry;

        var area = FriendAreas.FirstOrDefault(a => a.Index == (int)entry.FriendArea);
        if (area != null)
        {
            // RecruitFromGuide auto-unlocked the area in the save; route the same change through
            // the view-model so its checkbox/header/pending state agree with the save data.
            if (!area.Unlocked) area.Unlocked = true;
            SelectedFriendArea = area;
        }

        StatusText = $"Added {pokemon.Name} (Lv.{pokemon.Level}, met at {entry.DungeonName} {entry.Floor}F) to the recruited roster.";
    }

    /// <summary>
    /// The cutscene "complete" flag an add of <paramref name="speciesId"/> would newly set
    /// (via <see cref="RBExclusivePokemonData.MarkBossDefeated"/>), or null if the species has
    /// no such flag or it's already set.
    /// </summary>
    private RBCutsceneFlag? FlagAnAddWouldSet(int speciesId)
    {
        if (Save != null
            && RBBossEncounters.CompleteFlagsByBoss.TryGetValue(speciesId, out var flag)
            && !Save.ExclusivePokemonData.GetCutsceneFlag(flag))
        {
            return flag;
        }
        return null;
    }

    /// <summary>Every real roster entry (no previews), slot order -- the Held by Pokemon
    /// inventory tab's row source. Recomputed on demand; the entries themselves are the same
    /// canonical instances every other list shares.</summary>
    public IReadOnlyList<RosterEntryViewModel> RosterEntriesBySlot =>
        _rosterByPokemon.Values.OrderBy(e => e.SlotIndex).ToList();

    public void MarkDirty()
    {
        IsDirty = true;
        RefreshPendingSummaryText();
    }

    public void ClearDirty()
    {
        IsDirty = false;
        RefreshGeneral();
        SyncPendingFlags();
        RefreshPendingSummaryText();
    }

    /// <summary>
    /// Resets every persistent row's pending-highlight flag from the save's snapshot API. Cheap
    /// now -- just property sets on already-built view-models, not a container rebuild -- but
    /// still only needed for the broad case (a fresh load, or right after a save recaptures the
    /// snapshot); a single edit updates its own row directly (see
    /// <see cref="RosterEntryViewModel.Edited"/> / <see cref="FriendAreaViewModel.Unlocked"/>).
    /// </summary>
    private void SyncPendingFlags()
    {
        if (Save == null) return;
        foreach (var entry in _rosterByPokemon.Values)
        {
            entry.IsPending = Save.IsSlotPending(entry.SlotIndex);
        }
        foreach (var area in FriendAreas.Where(a => a.IsRealArea))
        {
            area.IsPending = Save.IsFriendAreaPending((RBFriendArea)area.Index);
        }
    }

    /// <summary>
    /// Counts every staged-but-unsaved edit (new roster slots, newly-ticked friend areas, money
    /// deltas, item quantity deltas) and shows the total next to the save path, so it's obvious
    /// there's unsaved work even when the orange row highlights are scrolled out of view.
    /// </summary>
    private void RefreshPendingSummaryText()
    {
        if (Save == null)
        {
            PendingSummaryText = "";
            return;
        }

        var pendingCount = Save.StoredPokemon.Count(p => Save.IsSlotPending(p.SlotIndex))
            + Enum.GetValues<RBFriendArea>().Count(a => a != RBFriendArea.None && Save.IsFriendAreaPending(a))
            + (Save.HeldMoneyDelta != 0 ? 1 : 0)
            + (Save.StoredMoneyDelta != 0 ? 1 : 0)
            + (Save.RescueTeamPointsDelta != 0 ? 1 : 0)
            + Save.StoredItems.Select(i => i.ItemID).Distinct().Count(id => Save.PendingItemDelta(id) != 0);

        PendingSummaryText = pendingCount > 0
            ? $"{pendingCount} unsaved change{(pendingCount == 1 ? "" : "s")}"
            : "";
    }

    private void RefreshGeneral()
    {
        if (Save == null) return;
        TeamNameText = Save.TeamName;
        HeldMoneyText = Save.HeldMoneyDelta != 0
            ? $"Held money: {Save.HeldMoney} ({Save.HeldMoneyDelta:+#;-#;0} unsaved)"
            : $"Held money: {Save.HeldMoney}";
        StoredMoneyText = Save.StoredMoneyDelta != 0
            ? $"Stored money: {Save.StoredMoney} ({Save.StoredMoneyDelta:+#;-#;0} unsaved)"
            : $"Stored money: {Save.StoredMoney}";
        RescuePointsText = Save.RescueTeamPointsDelta != 0
            ? $"Rescue team points: {Save.RescueTeamPoints} ({Save.RescueTeamPointsDelta:+#;-#;0} unsaved)"
            : $"Rescue team points: {Save.RescueTeamPoints}";
        ChecksumText = $"Checksums valid: primary={Save.IsPrimaryChecksumValid()}, secondary={Save.IsSecondaryChecksumValid()}";

        var rank = RBRescueTeamRanks.RankForPoints(Save.RescueTeamPoints);
        RescueRankText = $"Rank: {RBRescueTeamRanks.NameOf(rank)}";
        var toNext = RBRescueTeamRanks.PointsToNextRank(Save.RescueTeamPoints);
        RescueRankProgressText = toNext.HasValue
            ? $"{toNext.Value} points to {RBRescueTeamRanks.NameOf((RBRescueTeamRank)((int)rank + 1))}"
            : "Highest rank reached.";
        OnPropertyChanged(nameof(SelectedRank));
    }

    [RelayCommand]
    private void ApplyMoney()
    {
        if (Save == null) return;
        if (TargetIsHeldMoney) Save.HeldMoney += MoneyAmount; else Save.StoredMoney += MoneyAmount;

        MarkDirty();
        RefreshGeneral();
        StatusText = $"Applied {MoneyAmount:+#;-#;0} to {(TargetIsHeldMoney ? "held" : "stored")} money.";
    }

    [RelayCommand]
    private void ApplyRescuePoints()
    {
        if (Save == null) return;
        Save.RescueTeamPoints += RescuePointsAmount;

        MarkDirty();
        RefreshGeneral();
        StatusText = $"Applied {RescuePointsAmount:+#;-#;0} to Rescue Team points.";
    }
}
