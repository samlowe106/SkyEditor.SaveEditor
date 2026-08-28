using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Gui.ViewModels;

/// <summary>
/// One row in the Friend Areas and Roster tab's left-hand list. Doubles as the browsable roster
/// for that area (<see cref="Residents"/>), which is a persistent, in-place-mutated collection
/// (Add/Remove only, never wholesale-replaced) shared with
/// <see cref="MainWindowViewModel.FlatSortedResidents"/> and the roster detail pane -- see
/// <see cref="RosterEntryViewModel"/>. <see cref="Index"/> is -1 for
/// the synthetic "not tied to a Friend Area" bucket (the protagonist/partner and any other
/// recruited Pokemon whose species has no <see cref="RBRecruitGuide.HomeAreaOf"/> mapping), which
/// has no lock checkbox and no real-area detail pane of its own.
/// </summary>
public sealed partial class FriendAreaViewModel : ObservableObject
{
    private readonly MainWindowViewModel _owner;

    public FriendAreaViewModel(MainWindowViewModel owner, string name, int index, int capacity, bool unlocked)
    {
        _owner = owner;
        Name = name;
        Index = index;
        _capacity = capacity;
        _unlocked = unlocked;
        Residents.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HeaderText));
            OnPropertyChanged(nameof(OpenSlots));
            OnPropertyChanged(nameof(HasOpenSlots));
        };
    }

    public string Name { get; }
    public int Index { get; }

    [ObservableProperty]
    private int _capacity;

    partial void OnCapacityChanged(int value) => OnPropertyChanged(nameof(HeaderText));

    public ObservableCollection<RosterEntryViewModel> Residents { get; } = new();

    // Not `init`: each RecruitCandidateViewModel needs a reference back to this area (so its "+"
    // button can call RecruitCandidateEntry), which means building the list has to happen after
    // this instance exists, not inside its own object initializer -- see MainWindowViewModel.
    public IReadOnlyList<RecruitCandidateViewModel> Candidates { get; set; } = Array.Empty<RecruitCandidateViewModel>();

    [ObservableProperty]
    private RecruitCandidateViewModel? _selectedCandidate;

    /// <summary>Clicking a candidate previews it (read-only) in the right-hand dossier pane, same
    /// as clicking a real resident does -- separate from actually recruiting it.</summary>
    partial void OnSelectedCandidateChanged(RecruitCandidateViewModel? value)
    {
        if (value != null) _owner.PreviewCandidate(value.Entry);
    }

    /// <summary>This area's own residents ListBox selection -- kept separate from
    /// <see cref="MainWindowViewModel.SelectedRosterEntry"/> and one-way forwarded into it, same
    /// reasoning as <see cref="MainWindowViewModel.SelectedFlatRosterEntry"/>: several of these
    /// lists (one per expanded area, plus the flat sorted list) exist at once, and a shared TwoWay
    /// binding across more than one of them is what caused last session's silent-selection-reversion
    /// bug.</summary>
    [ObservableProperty]
    private RosterEntryViewModel? _selectedResident;

    partial void OnSelectedResidentChanged(RosterEntryViewModel? value)
    {
        if (value != null) _owner.SelectedRosterEntry = value;
    }

    // A locked area still has an info pane worth seeing (artwork, who could move in) -- you just
    // can't do anything about it until it's unlocked.
    public bool IsRealArea => Index >= 0;

    private bool _unlocked;
    public bool Unlocked
    {
        get => _unlocked;
        set
        {
            if (value == _unlocked) return;

            if (!value && Residents.Count > 0)
            {
                _owner.StatusText = $"Can't lock {Name} -- it still has recruited Pokemon living there.";
                OnPropertyChanged(); // snaps the bound CheckBox back to checked
                return;
            }

            _unlocked = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HeaderText));
            OnPropertyChanged(nameof(HasOpenSlots));
            OnPropertyChanged(nameof(CandidatesHeader));

            if (IsRealArea && _owner.Save != null)
            {
                _owner.Save.FriendAreasUnlocked[Index] = value;
                _owner.MarkDirty();
                IsPending = _owner.Save.IsFriendAreaPending((RBFriendArea)Index);
            }

            if (value)
            {
                _owner.SelectedFriendArea = this;
            }
            else if (_owner.SelectedFriendArea == this)
            {
                // A locked area has no detail pane to show (there's nothing left you can do to it
                // besides re-ticking the box), so drop the selection instead of displaying it.
                _owner.SelectedFriendArea = null;
            }
        }
    }

    [ObservableProperty]
    private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowExpandedContent));
        OnPropertyChanged(nameof(ExpandGlyph));
    }

    public bool ShowExpandedContent => Unlocked && IsExpanded;

    // Bigger, bolder triangles than the small "▸"/"▾" this used to use -- those read as nearly
    // identical at this font size, so toggling didn't look like it had visibly done anything,
    // especially for an area with no residents where the only other change is one text line.
    public string ExpandGlyph => IsExpanded ? "▼" : "▶";

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    /// <summary>
    /// Called by each candidate row's own "+" button (see <see cref="RecruitCandidateViewModel.RecruitCommand"/>)
    /// rather than a single shared "recruit whichever is selected" button -- recruiting auto-unlocks
    /// the area if needed (see <see cref="MainWindowViewModel.RecruitCandidate"/>/`RBSave.RecruitFromGuide`),
    /// so there's no lock-state gating to do here either.
    /// </summary>
    public void RecruitCandidateEntry(RecruitGuideEntry entry) => _owner.RecruitCandidate(entry);

    // Locked areas can't hold anyone yet, so the "X/Y" count would just be visual noise -- shown
    // only once the area is actually ownable (unlocked real area, or the synthetic bucket).
    public string HeaderText => IsRealArea && !Unlocked ? Name : $"{Name} ({Residents.Count}/{Capacity})";
    public int OpenSlots => Math.Max(0, Capacity - Residents.Count);
    public bool HasOpenSlots => IsRealArea && OpenSlots > 0;
    public string OpenSlotsText => $"{OpenSlots} open slot{(OpenSlots == 1 ? "" : "s")}";

    public string CandidatesHeader => !Unlocked
        ? $"Who can live in {Name} once it's unlocked"
        : Candidates.Count > 0
            ? $"Who can live in {Name}"
            : $"No easily-recruitable species call {Name} home";

    /// <summary>
    /// Friend area artwork isn't bundled yet (see TODO.md), so this looks for an optional
    /// embedded asset at Assets/FriendAreas/{enum name}.png and falls back to a plain
    /// placeholder when it isn't there -- dropping matching PNGs into that folder later is
    /// all that's needed to light this up, no further code changes. Uses the raw enum name
    /// (via <see cref="RawEnumName"/>), not the display name, since that's what the asset
    /// filenames are keyed by.
    /// </summary>
    public Bitmap? Graphic
    {
        get
        {
            if (!IsRealArea) return null;
            var uri = new Uri($"avares://SkyEditor.SaveEditor.Gui/Assets/FriendAreas/{RawEnumName}.png");
            if (!AssetLoader.Exists(uri)) return null;
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
    }

    public bool HasGraphic => Graphic != null;
    public string RawEnumName { get; init; } = "";

    [ObservableProperty]
    private bool _isPending;
}
