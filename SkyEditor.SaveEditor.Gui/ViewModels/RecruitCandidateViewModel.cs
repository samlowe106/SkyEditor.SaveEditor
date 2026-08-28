using CommunityToolkit.Mvvm.Input;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Gui.ViewModels;

/// <summary>
/// Static reference data (a species recruitable in a given Friend Area, at the level a legitimate
/// recruit would have) -- no mutable displayed state, so unlike most other view-models here this
/// isn't an <c>ObservableObject</c>; it's still partial only because <c>[RelayCommand]</c>'s source
/// generator needs that.
/// </summary>
public sealed partial class RecruitCandidateViewModel(RecruitGuideEntry entry, FriendAreaViewModel owner)
{
    public RecruitGuideEntry Entry { get; } = entry;

    public string DisplayText => $"{Entry.SpeciesName,-16} Lv.{Entry.Level,-3} ({Entry.DungeonName} {Entry.Floor}F)";

    [RelayCommand]
    private void Recruit() => owner.RecruitCandidateEntry(Entry);
}
