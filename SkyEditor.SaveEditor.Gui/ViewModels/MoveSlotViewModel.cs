using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Gui.ViewModels;

/// <summary>
/// Wraps one of a Pokemon's four move slots. Holds a direct reference to the live
/// <see cref="RBAttack"/> object (one of <c>Attack1</c>-<c>Attack4</c> on the owning
/// <see cref="RosterEntryViewModel"/>'s <see cref="RBStoredPokemon"/>), so every property here
/// forwards straight into the save data -- there's no separate commit step for the checkbox/
/// power-boost fields. Built once per <see cref="RosterEntryViewModel"/> and never rebuilt.
/// </summary>
public sealed partial class MoveSlotViewModel : ObservableObject
{
    private readonly RosterEntryViewModel _owner;

    public MoveSlotViewModel(RBAttack attack, int slotNumber, RosterEntryViewModel owner)
    {
        Attack = attack;
        SlotNumber = slotNumber;
        _owner = owner;
    }

    public RosterEntryViewModel Owner => _owner;
    public RBAttack Attack { get; }
    public int SlotNumber { get; }
    public string SlotLabel => $"Move {SlotNumber}";
    public string MoveName => Attack.IsValid && Lists.RBMoves.TryGetValue(Attack.ID, out var n) ? n : "";

    // Indents a linked move under the slot above it so the chain is visible at a glance, not just
    // stated in a checkbox label; the trailing 8px keeps the usual spacing between move rows.
    public Thickness LinkIndent => Attack.IsLinked ? new Thickness(24, 0, 0, 8) : new Thickness(0, 0, 0, 8);

    public bool IsLinked
    {
        get => Attack.IsLinked;
        set
        {
            if (Attack.IsLinked == value) return;
            Attack.IsLinked = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LinkIndent));
            _owner.Edited();
        }
    }

    public bool IsSwitched
    {
        get => Attack.IsSwitched;
        set
        {
            if (Attack.IsSwitched == value) return;
            Attack.IsSwitched = value;
            OnPropertyChanged();
            _owner.Edited();
        }
    }

    public bool IsSet
    {
        get => Attack.IsSet;
        set
        {
            if (Attack.IsSet == value) return;
            Attack.IsSet = value;
            OnPropertyChanged();
            _owner.Edited();
        }
    }

    public int PowerBoost
    {
        get => Attack.PowerBoost;
        set
        {
            if (Attack.PowerBoost == value) return;
            Attack.PowerBoost = value;
            OnPropertyChanged();
            _owner.Edited();
        }
    }

    /// <summary>
    /// Called from the move-name <c>AutoCompleteBox</c>'s <c>LostFocus</c> -- kept as an
    /// explicit commit rather than a two-way <c>Text</c> binding so an unknown move name can be
    /// rejected (with a status message) and the box reverted, instead of re-validating on every
    /// keystroke.
    /// </summary>
    public void CommitMoveName(string text)
    {
        if (_owner.IsPreview) return;

        text = text.Trim();
        if (text.Length == 0)
        {
            Attack.IsValid = false;
            Attack.ID = 0;
        }
        else
        {
            var match = Lists.RBMoves.FirstOrDefault(kv => string.Equals(kv.Value, text, StringComparison.OrdinalIgnoreCase));
            if (match.Value == null)
            {
                _owner.Owner.StatusText = $"Unknown move '{text}' -- {SlotLabel} unchanged.";
                OnPropertyChanged(nameof(MoveName));
                return;
            }
            var wasEmpty = !Attack.IsValid;
            Attack.ID = match.Key;
            Attack.IsValid = true;
            if (wasEmpty)
            {
                // A move filled into an empty slot starts AI-usable, like every organically
                // learned move (InitZeroedPPPokemonMove sets MOVE_FLAG_ENABLED_FOR_AI; verified
                // on the real save). Renaming an existing move keeps its flags as they are.
                Attack.IsSwitched = true;
                OnPropertyChanged(nameof(IsSwitched));
            }
        }

        OnPropertyChanged(nameof(MoveName));
        _owner.Edited();
    }
}
