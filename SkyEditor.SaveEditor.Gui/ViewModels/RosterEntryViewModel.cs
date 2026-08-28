using System.Linq;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Gui.ViewModels;

/// <summary>
/// One canonical view-model per recruited <see cref="RBStoredPokemon"/>. The same instance is
/// referenced from <see cref="MainWindowViewModel.FlatSortedResidents"/>, from whichever
/// <see cref="FriendAreaViewModel.Residents"/> it belongs to (or the synthetic "not tied to a
/// Friend Area" bucket), and from <see cref="MainWindowViewModel.SelectedRosterEntry"/> -- so
/// editing a stat anywhere updates every place it's displayed for free via normal
/// property-changed binding, with no list rebuild anywhere.
/// </summary>
public sealed partial class RosterEntryViewModel : ObservableObject
{
    private readonly RBStoredPokemon _pkm;

    /// <summary>
    /// True for a scratch entry built from a <see cref="RecruitCandidateViewModel"/> to preview in
    /// the right-hand dossier pane -- not part of any roster or Friend Area, and not persisted.
    /// Every editable control in the dossier is wrapped in an IsEnabled="{Binding !IsPreview}" in
    /// XAML, so a disabled control can't raise LostFocus/edit events in the first place; the
    /// checks in <see cref="Edited"/> and the LostFocus handlers are defense in depth on top of that.
    /// </summary>
    public bool IsPreview { get; }

    private readonly bool _canDeleteWhenSaved;

    /// <summary>
    /// The baseline (<see cref="_canDeleteWhenSaved"/>) is false for story bosses/legendaries
    /// (<see cref="BossSpecies"/> -- their cutscene scripts recheck HasRecruitedMon() on revisit,
    /// so removing a saved one would make the game think it was never recruited) and for anything
    /// in the synthetic "Other Pokemon (not tied to a Friend Area)" bucket (where the
    /// protagonist/partner live, indistinguishably from any other unmapped recruit -- see
    /// <see cref="MainWindowViewModel.BuildViewModel"/>). But a staged-but-unsaved addition
    /// (<see cref="IsPending"/>) is always deletable regardless: an edit in flight must always be
    /// reversible, and removing it before saving just cancels the addition -- the file never sees
    /// it. Once saved, <see cref="MainWindowViewModel.ClearDirty"/> refreshes IsPending and this
    /// drops back to the baseline rule. Always false for a preview entry, which was never really
    /// added to the roster in the first place.
    /// </summary>
    public bool CanDelete => !IsPreview && (_canDeleteWhenSaved || IsPending);

    /// <summary>
    /// The cutscene "complete" flag that adding this (still-unsaved) entry set, if the add
    /// actually flipped it -- so deleting the entry before saving can flip it back, keeping
    /// in-flight edits fully reversible. Null once saved (cleared alongside the pending flags)
    /// or when the flag was already set before the add.
    /// </summary>
    internal RBCutsceneFlag? CutsceneFlagFromAdd { get; set; }

    /// <summary>
    /// The friend area that adding this (still-unsaved) entry auto-unlocked, if the add actually
    /// flipped it from locked -- so deleting the entry before saving can lock it back (when no
    /// other resident has since moved in). Same lifecycle as <see cref="CutsceneFlagFromAdd"/>.
    /// </summary>
    internal RBFriendArea? AreaUnlockedByAdd { get; set; }

    /// <summary>Internal-only escape hatch so <see cref="MainWindowViewModel.DeleteResident"/> can
    /// remove the underlying save-file entry without every other property needing to expose it.</summary>
    internal RBStoredPokemon Pkm => _pkm;

    public RosterEntryViewModel(RBStoredPokemon pkm, MainWindowViewModel owner, bool isPreview = false, bool canDelete = true)
    {
        _pkm = pkm;
        Owner = owner;
        IsPreview = isPreview;
        _canDeleteWhenSaved = canDelete && !isPreview;
        Moves =
        [
            new MoveSlotViewModel(pkm.Attack1, 1, this),
            new MoveSlotViewModel(pkm.Attack2, 2, this),
            new MoveSlotViewModel(pkm.Attack3, 3, this),
            new MoveSlotViewModel(pkm.Attack4, 4, this),
        ];
        RefreshExpInfo();
    }

    public MainWindowViewModel Owner { get; }

    public int SlotIndex => _pkm.SlotIndex;
    public int SpeciesId => _pkm.ID;
    public string Species => Lists.RBPokemon.TryGetValue(_pkm.ID, out var n) ? n : $"#{_pkm.ID}";
    public string HeaderText => IsPreview ? $"{Species} (#{_pkm.ID}) -- preview" : $"{Species} (#{_pkm.ID}) -- slot {SlotIndex}";
    public string SlotText => $"[slot {SlotIndex,3}] {Species,-16} Lv.{Level,-3} \"{Name}\"";
    public string ResidentDisplayText => $"#{SpeciesId,3} {Species,-16} Lv.{Level,-3} \"{Name}\"";
    public IReadOnlyList<MoveSlotViewModel> Moves { get; }

    public string Name
    {
        get => _pkm.Name;
        set
        {
            if (_pkm.Name == value) return;
            _pkm.Name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SlotText));
            OnPropertyChanged(nameof(ResidentDisplayText));
            OnPropertyChanged(nameof(HeaderText));
            Edited();
        }
    }

    public bool FloorEditable => NoFloorLocations.AppliesTo(_pkm.MetAt);

    /// <summary>
    /// A ComboBox over <see cref="Lists.RBLocations"/>'s values, not free text -- MetAt is really a
    /// closed enum of dungeon IDs, and only ever letting the user pick a name that's actually in
    /// that list means there's no "unknown dungeon name" case to validate or reject any more.
    /// </summary>
    public string MetAtName
    {
        get => Lists.RBLocations.TryGetValue(_pkm.MetAt, out var d) ? d : "";
        set
        {
            if (IsPreview) return;

            var match = Lists.RBLocations.FirstOrDefault(kv => kv.Value == value);
            if (match.Value == null || _pkm.MetAt == match.Key) return;

            _pkm.MetAt = match.Key;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FloorEditable));
            Edited();
        }
    }

    public int Floor
    {
        get => _pkm.Floor;
        set
        {
            if (_pkm.Floor == value) return;
            _pkm.Floor = value;
            OnPropertyChanged();
            Edited();
        }
    }

    public int Level
    {
        get => _pkm.Level;
        set
        {
            if (_pkm.Level == value) return;

            // Level, stats, and Exp move together the way an in-game level-up moves them
            // (LevelUp in src/dungeon_leveling.c adds that level's growth-table row, capped at
            // 999/255; level-down subtracts it, floored at 1; currExp snaps to the new level's
            // requirement). Growth is a fixed per-species table, no RNG, so this yields exactly
            // the stats a legitimately leveled Pokemon would have. Any stat can still be edited
            // afterward.
            if (!RBGrowthTables.SetLevel(_pkm, value))
            {
                _pkm.Level = value;
            }
            NotifyLevelDependentsChanged();
            Edited();
        }
    }

    private void NotifyLevelDependentsChanged()
    {
        OnPropertyChanged(nameof(Level));
        OnPropertyChanged(nameof(SlotText));
        OnPropertyChanged(nameof(ResidentDisplayText));
        OnPropertyChanged(nameof(Exp));
        OnPropertyChanged(nameof(HP));
        OnPropertyChanged(nameof(Attack));
        OnPropertyChanged(nameof(SpAttack));
        OnPropertyChanged(nameof(Defense));
        OnPropertyChanged(nameof(SpDefense));
        RefreshExpInfo();
    }

    /// <summary>Level at first evolution, 0 = never evolved while recruited (a wild-recruited
    /// evolved form legitimately has 0). Only affects which pre-evolution moves Gulpin offers.</summary>
    public int FirstEvolutionLevel
    {
        get => _pkm.FirstEvolutionLevel;
        set { if (_pkm.FirstEvolutionLevel == value) return; _pkm.FirstEvolutionLevel = value; OnPropertyChanged(); Edited(); }
    }

    public int SecondEvolutionLevel
    {
        get => _pkm.SecondEvolutionLevel;
        set { if (_pkm.SecondEvolutionLevel == value) return; _pkm.SecondEvolutionLevel = value; OnPropertyChanged(); Edited(); }
    }

    public int IQ
    {
        get => _pkm.IQ;
        set { if (_pkm.IQ == value) return; _pkm.IQ = value; OnPropertyChanged(); Edited(); }
    }

    public string HeldItemName
    {
        get => _pkm.HeldItemId == 0
            ? "(none)"
            : Lists.RBItems.TryGetValue(_pkm.HeldItemId, out var name) ? name : $"#{_pkm.HeldItemId}";
        set
        {
            if (value == null) return;
            if (value == "(none)")
            {
                if (_pkm.HeldItemId == 0) return;
                _pkm.HeldItemId = 0;
                _pkm.HeldItemQuantity = 0;
            }
            else
            {
                var match = Lists.RBItems.FirstOrDefault(kv => kv.Value == value);
                if (match.Value == null || _pkm.HeldItemId == match.Key) return;
                _pkm.HeldItemId = match.Key;
                // Itemless Pokemon carry stale garbage in the quantity bits (the game never
                // clears them); start a newly assigned item at a clean 0 stack.
                _pkm.HeldItemQuantity = 0;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(HeldItemQuantity));
            NotifyHeldItemDisplayChanged();
            Edited();
        }
    }

    /// <summary>Stack count; the game only uses this for thrown items (Gravelerock etc.) and keeps it 0 for gear.</summary>
    public int HeldItemQuantity
    {
        get => _pkm.HeldItemQuantity;
        set
        {
            if (_pkm.HeldItemQuantity == value) return;
            _pkm.HeldItemQuantity = value;
            OnPropertyChanged();
            NotifyHeldItemDisplayChanged();
            Edited();
        }
    }

    // Held-item projections for the Held by Pokemon inventory tab.
    public bool HasHeldItem => _pkm.HeldItemId != 0;
    public Avalonia.Media.Imaging.Bitmap? HeldItemIcon => HasHeldItem ? ItemIconLoader.GetIcon(_pkm.HeldItemId) : null;
    public string HeldItemDisplayText => !HasHeldItem
        ? "(nothing held)"
        : _pkm.HeldItemQuantity > 0 ? $"{HeldItemName} x{_pkm.HeldItemQuantity}" : HeldItemName;

    private void NotifyHeldItemDisplayChanged()
    {
        OnPropertyChanged(nameof(HasHeldItem));
        OnPropertyChanged(nameof(HeldItemIcon));
        OnPropertyChanged(nameof(HeldItemDisplayText));
    }

    /// <summary>
    /// Sets the held item by id/stack directly (the inventory tab's drag-drop and Take/give
    /// operations), keeping every binding in sync -- unlike writing <c>Pkm.HeldItemId</c>
    /// directly, which the roster detail pane would never hear about.
    /// </summary>
    internal void SetHeldItem(int itemId, int quantity)
    {
        if (_pkm.HeldItemId == itemId && _pkm.HeldItemQuantity == quantity) return;
        _pkm.HeldItemId = itemId;
        _pkm.HeldItemQuantity = quantity;
        OnPropertyChanged(nameof(HeldItemName));
        OnPropertyChanged(nameof(HeldItemQuantity));
        NotifyHeldItemDisplayChanged();
        Edited();
    }

    public int HP
    {
        get => _pkm.HP;
        set { if (_pkm.HP == value) return; _pkm.HP = value; OnPropertyChanged(); Edited(); }
    }

    public int Attack
    {
        get => _pkm.Attack;
        set { if (_pkm.Attack == value) return; _pkm.Attack = value; OnPropertyChanged(); Edited(); }
    }

    public int SpAttack
    {
        get => _pkm.SpAttack;
        set { if (_pkm.SpAttack == value) return; _pkm.SpAttack = value; OnPropertyChanged(); Edited(); }
    }

    public int Defense
    {
        get => _pkm.Defense;
        set { if (_pkm.Defense == value) return; _pkm.Defense = value; OnPropertyChanged(); Edited(); }
    }

    public int SpDefense
    {
        get => _pkm.SpDefense;
        set { if (_pkm.SpDefense == value) return; _pkm.SpDefense = value; OnPropertyChanged(); Edited(); }
    }

    public int Exp
    {
        get => _pkm.Exp;
        set
        {
            if (_pkm.Exp == value) return;
            _pkm.Exp = value;

            // And level follows Exp: the game levels up whenever currExp reaches the next
            // level's cumulative requirement, so the level implied by an Exp value is the
            // highest one whose requirement it meets; stats follow the level change as above.
            // Exp itself is left exactly as typed (partial progress toward the next level is
            // legitimate state).
            var impliedLevel = RBGrowthTables.LevelForExp(SpeciesId, value);
            if (impliedLevel.HasValue && impliedLevel.Value != _pkm.Level)
            {
                RBGrowthTables.SetLevel(_pkm, impliedLevel.Value, keepExp: true);
            }
            NotifyLevelDependentsChanged();
            Edited();
        }
    }

    /// <summary>
    /// How much EXP is needed for the next level and for level 100, using the game's real
    /// per-species growth curve (<see cref="RBGrowthTables"/>) rather than a generic formula -- this
    /// game's EXP tables are fixed per species, not derived from a shared growth rate.
    /// </summary>
    public string ExpInfoText { get; private set; } = "";

    private void RefreshExpInfo()
    {
        string text;
        if (Level >= 100)
        {
            text = "Already at max level (100).";
        }
        else
        {
            var nextLevel = Level + 1;
            var expForNext = RBGrowthTables.ExpRequiredForLevel(SpeciesId, nextLevel);
            if (expForNext == null)
            {
                text = "";
            }
            else
            {
                var toNext = (long)expForNext.Value - Exp;
                text = $"{toNext:N0} Exp to level {nextLevel} ({expForNext.Value:N0} total).";

                var expFor100 = RBGrowthTables.ExpRequiredForLevel(SpeciesId, 100);
                if (expFor100.HasValue)
                {
                    var to100 = (long)expFor100.Value - Exp;
                    text += $" {to100:N0} Exp to level 100 ({expFor100.Value:N0} total).";
                }
            }
        }

        ExpInfoText = text;
        OnPropertyChanged(nameof(ExpInfoText));
    }

    /// <summary>
    /// Pokemon portrait/dialog sprites aren't bundled yet, so this looks for an optional embedded
    /// asset at Assets/PokemonPortraits/{species id}.png and falls back to a placeholder when it
    /// isn't there -- same convention as <see cref="FriendAreaViewModel.Graphic"/>.
    /// </summary>
    public Bitmap? Portrait
    {
        get
        {
            var uri = new Uri($"avares://SkyEditor.SaveEditor.Gui/Assets/PokemonPortraits/{SpeciesId}.png");
            if (!AssetLoader.Exists(uri)) return null;
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
    }

    public bool HasPortrait => Portrait != null;

    [ObservableProperty]
    private bool _isPending;

    partial void OnIsPendingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDelete));
        if (!value)
        {
            // No longer a staged addition (it was just saved, or the whole view was rebuilt):
            // its add-side-effects are now part of the file, so there's nothing left to revert.
            CutsceneFlagFromAdd = null;
            AreaUnlockedByAdd = null;
        }
    }

    /// <summary>
    /// Marks the file dirty and refreshes this Pokemon's own pending-highlight state -- called by
    /// every property setter above and by <see cref="MoveSlotViewModel"/>'s. No other row's
    /// pending state is affected by editing one Pokemon's stats, so this only needs to touch this
    /// instance rather than looping every roster/friend-area row (see
    /// <see cref="MainWindowViewModel.SyncPendingFlags"/> for the broader case, used after saving).
    /// </summary>
    public void Edited()
    {
        if (IsPreview) return;

        Owner.MarkDirty();
        if (Owner.Save != null)
        {
            IsPending = Owner.Save.IsSlotPending(SlotIndex);
        }
    }
}
