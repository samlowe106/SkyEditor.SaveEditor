using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SkyEditor.SaveEditor.Gui.ViewModels;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Gui;

/// <summary>
/// Thin View-layer glue: StorageProvider file dialogs (need the TopLevel/Window, so can't live in
/// the ViewModel) delegate straight into <see cref="MainWindowViewModel"/>, which owns everything
/// else. The Story Flags tab and the Items list on the General tab haven't been converted to the
/// MVVM pattern yet (tracked as a follow-up), so their lists/handlers still live here against the
/// VM's exposed <see cref="Save"/> pass-through -- General, Friend Areas and Roster, and the
/// Roster detail/edit pane are fully VM-owned and bound directly in MainWindow.axaml.
/// </summary>
public partial class MainWindow : Window
{
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;
    private RBSave? Save => Vm.Save;

    private static readonly FilePickerFileType SaveFileType = new("Rescue Team save files")
    {
        Patterns = ["*.sav", "*.srm"],
    };

    public MainWindow()
    {
        DataContext = new MainWindowViewModel();
        InitializeComponent();
        RefreshAllItemsList();
        RosterMetAtInput.ItemsSource = Lists.RBLocations.Values.OrderBy(v => v).ToList();
        RosterHeldItemInput.ItemsSource = new[] { "(none)" }.Concat(Lists.RBItems.Where(kv => kv.Key > 0).Select(kv => kv.Value).OrderBy(v => v)).ToList();

        WelcomePanel.AddHandler(DragDrop.DragOverEvent, OnWelcomePanelDragOver);
        WelcomePanel.AddHandler(DragDrop.DropEvent, OnWelcomePanelDrop);

        // Inventory drag and drop. The DragOver/Drop routed events only reach elements with
        // AllowDrop set; the panels/list set it in XAML, the TabItems here (they're drop-over
        // switch targets, not real drop targets).
        StoragePanel.AddHandler(DragDrop.DragOverEvent, OnInventoryTargetDragOver);
        StoragePanel.AddHandler(DragDrop.DropEvent, OnStorageDrop);
        ToolboxPanel.AddHandler(DragDrop.DragOverEvent, OnInventoryTargetDragOver);
        ToolboxPanel.AddHandler(DragDrop.DropEvent, OnToolboxDrop);
        HeldList.AddHandler(DragDrop.DragOverEvent, OnInventoryTargetDragOver);
        HeldList.AddHandler(DragDrop.DropEvent, OnHeldListDrop);
        foreach (var tab in new[] { StorageTab, ToolboxTab, HeldTab })
        {
            DragDrop.SetAllowDrop(tab, true);
            tab.AddHandler(DragDrop.DragOverEvent, OnInventoryTabDragOver);
        }
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this)!;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Rescue Team save",
            AllowMultiple = false,
            FileTypeFilter = [SaveFileType, FilePickerFileTypes.All],
        });

        if (files.Count == 0) return;

        await OpenSaveAndRefreshUnconvertedTabs(files[0].Path.LocalPath);
    }

    private void OnWelcomePanelDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnWelcomePanelDrop(object? sender, DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
        if (file == null) return;

        await OpenSaveAndRefreshUnconvertedTabs(file.Path.LocalPath);
    }

    private async Task OpenSaveAndRefreshUnconvertedTabs(string path)
    {
        await Vm.OpenSaveFromPathAsync(path);
        RefreshBosses();
        RefreshItems();
        RefreshWonderMail();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        await Vm.SaveAsync();
        RefreshBosses();
        RefreshItems();
        RefreshWonderMail();
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        if (Save == null) return;

        var topLevel = TopLevel.GetTopLevel(this)!;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Rescue Team save as",
            FileTypeChoices = [SaveFileType, FilePickerFileTypes.All],
            SuggestedFileName = Vm.SuggestedFileName,
        });

        if (file == null) return;

        await Vm.SaveAsAsync(file.Path.LocalPath);
        RefreshBosses();
        RefreshItems();
        RefreshWonderMail();
    }

    private void OnMoveNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is AutoCompleteBox { DataContext: MoveSlotViewModel { Owner.IsPreview: false } move } box)
        {
            move.CommitMoveName(box.Text ?? "");
        }
    }

    /// <summary>
    /// The "-" button on a resident row (grouped or flat list). Shows the confirm dialog here in
    /// code-behind (it needs a TopLevel, which the ViewModel doesn't have) and only calls into the
    /// ViewModel once the user has actually confirmed -- <see cref="MainWindowViewModel.DeleteResident"/>
    /// itself contains no confirmation logic.
    /// </summary>
    private async void OnDeleteResidentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RosterEntryViewModel { CanDelete: true } entry })
        {
            await ConfirmAndDelete(entry);
        }
    }

    private async void OnResidentListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && sender is ListBox { SelectedItem: RosterEntryViewModel { CanDelete: true } entry })
        {
            e.Handled = true;
            await ConfirmAndDelete(entry);
        }
    }

    private async Task ConfirmAndDelete(RosterEntryViewModel entry)
    {
        // A staged-but-unsaved addition is just being cancelled, not released: the file never saw
        // it, so none of the in-game Farewell caveats apply.
        var message = entry.IsPending
            ? $"Remove {entry.Species} \"{entry.Name}\"? It was added this session and hasn't been " +
              "saved yet, so this just cancels the addition (along with any cutscene flag or " +
              "area unlock that addition made)."
            : $"Say farewell to {entry.Species} \"{entry.Name}\"? This removes it from your roster " +
              "entirely, the same as choosing Farewell in-game, and can't be undone once you save. " +
              "(This tool can't confirm whether it's currently on your active field team of 4 -- " +
              "releasing an active member could leave stale team-slot data.)";
        var dialog = new ConfirmDeleteDialog { Message = message };
        var confirmed = await dialog.ShowDialog<bool>(this);
        if (confirmed)
        {
            Vm.DeleteResident(entry);
            // Deleting a staged boss add may have reverted its cutscene flag; the Story Flags
            // tab's lists are still code-behind-built, so rebuild them.
            RefreshBosses();
        }
    }

    /// <summary>
    /// Warns before recruiting a species <see cref="RBSave.CanCurrentlyRecruit"/> says isn't
    /// currently recruitable -- a species in <see cref="RBBossEncounters.NeverCombatRecruitable"/>
    /// (permanently, not "not yet"), a species in <see cref="RBBossEncounters.FirstEncounterFlagsByBoss"/>
    /// whose flag isn't set (the real game hard-blocks recruiting until its mandatory first story
    /// encounter, a fight that can never end in a recruit, has happened), or a Regi whose Part/Music
    /// Box isn't currently held or in storage. Adding one now would create a roster entry the game
    /// itself could never have produced at this point in the save. Returns true if the caller should
    /// proceed anyway (either the check didn't apply, or the user confirmed past the warning).
    /// </summary>
    private async Task<bool> ConfirmFirstEncounterIfNeeded(int speciesId, string speciesName)
    {
        if (Save == null || Save.CanCurrentlyRecruit(speciesId)) return true;

        var message = RBBossEncounters.NeverCombatRecruitable.Contains(speciesId)
            ? $"{speciesName} can never legitimately be recruited through combat, at any point in the game -- " +
              "this isn't a \"not yet,\" the real game's recruit check unconditionally excludes it. It's obtained " +
              "only through a separate scripted story event this tool doesn't model. Adding it now would create " +
              "a roster entry that could never exist through legitimate play, ever. Add it anyway?"
            : RBBossEncounters.RegiItems.PartIdsBySpecies.ContainsKey(speciesId)
                ? $"{speciesName} can't legitimately be recruited yet -- you don't currently have its Part (or the " +
                  "assembled Music Box) held or in storage. The real game re-checks this every time you enter its " +
                  "room, so without one of those items it can only be fought, never recruited. Adding it now creates " +
                  "a roster entry the game itself would never produce right now. Add it anyway?"
                : $"{speciesName} can't legitimately be recruited yet. The real game blocks recruiting it " +
                  "until its mandatory first story encounter has happened -- that first fight can only end " +
                  "in it fainting or fleeing, never a recruit. Adding it now creates a roster entry the game " +
                  "itself would never produce at this point in your save. Add it anyway?";
        var dialog = new ConfirmDeleteDialog { Message = message, ConfirmText = "Add Anyway" };
        return await dialog.ShowDialog<bool>(this);
    }

    private async void OnRecruitCandidateClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RecruitCandidateViewModel candidate }) return;

        if (!await ConfirmFirstEncounterIfNeeded(candidate.Entry.SpeciesId, candidate.Entry.SpeciesName)) return;

        candidate.RecruitCommand.Execute(null);
    }

    private async void OnMarkBossClick(object? sender, RoutedEventArgs e)
    {
        if (Save == null) return;
        if (BossList.SelectedItem is not BossRow selected)
        {
            Vm.StatusText = "Select a boss first.";
            return;
        }

        if (!await ConfirmFirstEncounterIfNeeded(selected.SpeciesId, selected.Name)) return;

        Vm.MarkBossRecruited(selected.SpeciesId, (int)(BossLevelInput.Value ?? 30));
        RefreshBosses();
    }

    private void RefreshBosses()
    {
        if (Save == null) return;
        var bossNames = new Dictionary<string, int>();
        foreach (var field in typeof(RBBossEncounters).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType == typeof(int))
            {
                bossNames[field.Name] = (int)field.GetValue(null)!;
            }
        }

        var rows = bossNames.OrderBy(kv => kv.Value).Select(kv =>
        {
            var recruitedPkm = Save.StoredPokemon.Find(p => p.ID == kv.Value);
            var recruited = recruitedPkm != null;

            var recruitableCell = recruited
                ? new BoolCell { Value = true, NoAssociatedFlag = true, Tooltip = "No associated flag, already recruited so this is moot -- shown green rather than as a warning state." }
                : RBBossEncounters.NeverCombatRecruitable.Contains(kv.Value)
                    ? new BoolCell { Value = null, NoAssociatedFlag = true, Tooltip = "No associated flag, this Pokemon is recruited through a story event instead of combat." }
                    : RBBossEncounters.RegiItems.PartIdsBySpecies.ContainsKey(kv.Value)
                        ? new BoolCell
                        {
                            Value = Save.CanCurrentlyRecruit(kv.Value),
                            NoAssociatedFlag = true,
                            Tooltip = $"No associated flag, checks whether you currently hold or have in storage this Regi's own Part (item ID {RBBossEncounters.RegiItems.PartIdsBySpecies[kv.Value]}) or the assembled Music Box (item ID {RBBossEncounters.RegiItems.MusicBoxItemId}).",
                        }
                        : RBBossEncounters.FirstEncounterFlagsByBoss.ContainsKey(kv.Value)
                            ? new BoolCell
                            {
                                Value = Save.CanCurrentlyRecruit(kv.Value),
                                Tooltip = $"`RBCutsceneFlag.{RBBossEncounters.FirstEncounterFlagsByBoss[kv.Value]}`: must already be set -- the mandatory first story fight against this boss can only end in it fainting or fleeing, never a recruit.",
                            }
                            : new BoolCell { Value = null, NoAssociatedFlag = true, Tooltip = "No associated flag, nothing restricts this species -- it's recruitable (by the normal chance roll) as soon as you find it." };

            var hasFlag = RBBossEncounters.CompleteFlagsByBoss.TryGetValue(kv.Value, out var flag);
            var cutsceneFlagCell = hasFlag
                ? new BoolCell
                {
                    Value = Save.ExclusivePokemonData.GetCutsceneFlag(flag),
                    Tooltip = $"`RBCutsceneFlag.{flag}`: once set, revisiting won't replay the first-encounter cutscene. \"Mark Selected Boss Recruited\" and recruiting via Friend Areas and Roster both set this automatically.",
                }
                : RBBossEncounters.RegiItems.PartIdsBySpecies.ContainsKey(kv.Value)
                    ? new BoolCell { Value = null, NoAssociatedFlag = true, Tooltip = "No associated flag, the Regis don't use a story cutscene-complete flag -- see the Recruitable column instead." }
                    : new BoolCell { Value = null, NoAssociatedFlag = true, Tooltip = "No associated flag, this boss has no story cutscene-complete flag." };

            return new BossRow
            {
                Name = kv.Key,
                SpeciesId = kv.Value,
                Recruited = recruited,
                IsPending = recruited && Save.IsSlotPending(recruitedPkm!.SlotIndex),
                RecruitedCell = new BoolCell { Value = recruited, Tooltip = "Whether this species currently has a roster entry." },
                RecruitableCell = recruitableCell,
                CutsceneFlagCell = cutsceneFlagCell,
            };
        });
        BossList.ItemsSource = new ObservableCollection<BossRow>(rows);

        StoryFlagList.ItemsSource = new ObservableCollection<StoryFlagRow>(RBStoryFlags.All.Select(info => new StoryFlagRow
        {
            Phase = info.Phase switch
            {
                RBStoryPhase.MainStory => "Main story",
                RBStoryPhase.Postgame => "Postgame",
                _ => "Scratch",
            },
            Name = info.Flag.ToString(),
            Description = info.Description,
            SetCell = new BoolCell
            {
                Value = Save.ExclusivePokemonData.GetCutsceneFlag(info.Flag),
                Tooltip = $"`RBCutsceneFlag.{info.Flag}` (bit {(int)info.Flag})",
            },
        }));
    }

    private void RefreshWonderMail()
    {
        if (Save == null) return;

        List<WonderMailRow> BuildRows(List<RBWonderMail> slots, string section) => slots
            .Select((mail, i) => (mail, i))
            .Where(x => !x.mail.IsEmpty)
            .Select(x => new WonderMailRow
            {
                Section = section,
                Index = x.i,
                Summary = x.mail.GetMissionSummary(),
                Reward = x.mail.GetRewardSummary(),
                Password = x.mail.IsWonderMail ? RBWonderMailPassword.FormatForDisplay(RBWonderMailPassword.Encode(x.mail)) : "",
                Tooltip = $"slot {x.i}: mailType={x.mail.MailType} missionType={x.mail.MissionType} client=#{x.mail.ClientSpecies} target=#{x.mail.TargetSpecies} "
                        + $"dungeon=#{x.mail.DungeonId} floor={x.mail.Floor} seed={x.mail.Seed:x6} rewardType={x.mail.RewardType} rewardItem=#{x.mail.RewardItem}",
            })
            .ToList();

        var jobs = BuildRows(Save.MailData.JobSlots, "job");
        var board = BuildRows(Save.MailData.PelipperBoardJobs, "board");
        var mailbox = BuildRows(Save.MailData.MailboxSlots, "mailbox");
        var used = Save.MailData.UsedMailHistory
            .Select((record, i) => (record, i))
            .Where(x => !x.record.IsEmpty)
            .Select(x => new UsedMailRow
            {
                Index = x.i,
                Summary = x.record.GetSummary(),
                Tooltip = $"entry {x.i}: dungeon=#{x.record.DungeonId} floor={x.record.Floor} seed={x.record.Seed:x6} mail checksum={x.record.Checksum:x8}",
            })
            .ToList();

        JobList.ItemsSource = jobs;
        BoardList.ItemsSource = board;
        MailboxList.ItemsSource = mailbox;
        UsedMailList.ItemsSource = used;
        JobListEmpty.IsVisible = jobs.Count == 0;
        BoardListEmpty.IsVisible = board.Count == 0;
        MailboxListEmpty.IsVisible = mailbox.Count == 0;
        UsedMailListEmpty.IsVisible = used.Count == 0;
    }

    private void OnRemoveMailClick(object? sender, RoutedEventArgs e)
    {
        if (Save == null) return;
        if (sender is not Button { DataContext: WonderMailRow row }) return;

        switch (row.Section)
        {
            case "job": Save.MailData.RemoveJob(row.Index); break;
            case "board": Save.MailData.RemovePelipperBoardJob(row.Index); break;
            case "mailbox": Save.MailData.RemoveMailboxSlot(row.Index); break;
            default: return;
        }

        Vm.MarkDirty();
        RefreshWonderMail();
        Vm.StatusText = $"Removed: {row.Summary}";
    }

    private void OnRemoveUsedMailClick(object? sender, RoutedEventArgs e)
    {
        if (Save == null) return;
        if (sender is not Button { DataContext: UsedMailRow row }) return;

        Save.MailData.RemoveUsedMailRecord(row.Index);
        Vm.MarkDirty();
        RefreshWonderMail();
        Vm.StatusText = $"Removed used-password entry: {row.Summary}. Its Wonder Mail password can be entered again.";
    }

    /// <summary>
    /// The Add button targets whichever inventory tab is open: Storage gets the spinner quantity,
    /// the Toolbox gets one stack-0 slot (the game's own convention for carried gear), and Held
    /// by Pokemon gives the item to the Pokemon selected in that tab's list.
    /// </summary>
    private void OnAddItemClick(object? sender, RoutedEventArgs e)
    {
        if (Save == null) return;
        if (AllItemsList.SelectedItem is not AllItemRow selected)
        {
            Vm.StatusText = "Select an item from the list on the left first.";
            return;
        }

        if (InventoryTabs.SelectedItem == ToolboxTab)
        {
            AddToToolbox(selected.ItemId, parameter: 0);
        }
        else if (InventoryTabs.SelectedItem == HeldTab)
        {
            if (HeldList.SelectedItem is not RosterEntryViewModel holder)
            {
                Vm.StatusText = "Select a Pokemon in the Held by Pokemon list first (or drop the item onto one).";
                return;
            }
            GiveHeldItem(holder, selected.ItemId, quantity: 0);
        }
        else
        {
            var quantity = (int)(ItemQuantityInput.Value ?? 1);
            AddToStorage(selected.ItemId, quantity);
            Vm.MarkDirty();
            RefreshItems();
            Vm.StatusText = $"Added {quantity}x {selected.Name} to storage.";
        }
    }

    private void OnItemFilterChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshAllItemsList();
    }

    /// <summary>
    /// Populates the browsable "Add Items" list on the left, filtered by
    /// <see cref="ItemFilterInput"/>. Static reference data (<see cref="Lists.RBItems"/>), not
    /// dependent on which save is open, so this can run before any file is loaded.
    /// </summary>
    private void RefreshAllItemsList()
    {
        var filter = ItemFilterInput.Text?.Trim() ?? "";
        var rows = Lists.RBItems
            .Where(kv => filter.Length == 0 || kv.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => kv.Value)
            .Select(kv => new AllItemRow { ItemId = kv.Key, Name = kv.Value });
        AllItemsList.ItemsSource = new ObservableCollection<AllItemRow>(rows);
    }

    private void RefreshItems()
    {
        if (Save == null) return;
        var rows = Save.StoredItems
            .GroupBy(i => i.ItemID)
            .Select(g => new StoredItemRow
            {
                Name = Lists.RBItems.TryGetValue(g.Key, out var n) ? n : $"#{g.Key}",
                ItemId = g.Key,
                Quantity = g.Sum(i => i.Quantity),
                PendingDelta = Save.PendingItemDelta(g.Key),
            })
            .OrderBy(r => r.Name);
        StoredItemsList.ItemsSource = new ObservableCollection<StoredItemRow>(rows);

        ToolboxHeader.Text = $"Toolbox ({Save.HeldItems.Count}/20)";
        ToolboxList.ItemsSource = new ObservableCollection<ToolboxRow>(Save.HeldItems.Select((item, i) => new ToolboxRow
        {
            Index = i,
            ItemId = item.ID,
            Name = Lists.RBItems.TryGetValue(item.ID, out var name) ? name : $"#{item.ID}",
            Parameter = item.Parameter,
        }));

        RefreshHeldList();
    }

    /// <summary>
    /// (Re)builds the Held by Pokemon tab's rows from the current roster. The rows are the same
    /// canonical <see cref="RosterEntryViewModel"/> instances the Friend Areas tab uses, so held
    /// item changes made anywhere show up everywhere without a rebuild; this only re-runs to pick
    /// up roster membership changes (a new recruit, a farewell).
    /// </summary>
    private void RefreshHeldList()
    {
        // The rows are canonical shared instances, so the old selection can simply be re-pointed
        // at after a rebuild instead of getting lost.
        var selected = HeldList.SelectedItem as RosterEntryViewModel;
        var rows = Vm.RosterEntriesBySlot;
        HeldList.ItemsSource = new ObservableCollection<RosterEntryViewModel>(rows);
        if (selected != null && rows.Contains(selected))
        {
            HeldList.SelectedItem = selected;
        }
    }

    private void OnInventoryTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged is a routed event, so selection changes inside the tabs' own ListBoxes
        // bubble up here too -- only act on the TabControl's own tab switch.
        if (!ReferenceEquals(e.Source, InventoryTabs)) return;

        // Roster membership may have changed since the rows were last built (recruits and
        // farewells happen on another tab), so refresh when this tab comes into view.
        if (InventoryTabs?.SelectedItem == HeldTab && Save != null)
        {
            RefreshHeldList();
        }
    }

    private void AddToStorage(int itemId, int quantity)
    {
        // Top up an existing stack first (999 cap per stack, matching the game), then open new
        // stacks for any remainder.
        while (quantity > 0)
        {
            var stack = Save!.StoredItems.FirstOrDefault(i => i.ItemID == itemId && i.Quantity < 999);
            if (stack == null)
            {
                var take = Math.Min(999, quantity);
                Save.StoredItems.Add(new RBStoredItem(itemId, take));
                quantity -= take;
            }
            else
            {
                var take = Math.Min(999 - stack.Quantity, quantity);
                stack.Quantity += take;
                quantity -= take;
            }
        }
    }

    private bool TakeOneFromStorage(int itemId)
    {
        var stack = Save!.StoredItems.LastOrDefault(i => i.ItemID == itemId && i.Quantity > 0);
        if (stack == null) return false;
        stack.Quantity--;
        if (stack.Quantity == 0) Save.StoredItems.Remove(stack);
        return true;
    }

    private void AddToToolbox(int itemId, int parameter)
    {
        if (Save == null) return;
        if (Save.HeldItems.Count >= Save.Offsets.HeldItemCount)
        {
            Vm.StatusText = "The Toolbox is full (20 slots).";
            return;
        }

        // Stack 0 matches what the game writes for ordinary gear; thrown-item stacks keep the
        // count they arrived with.
        Save.HeldItems.Add(new RBHeldItem { ID = itemId, Parameter = parameter });
        Vm.MarkDirty();
        RefreshItems();
        Vm.StatusText = $"Added {ItemName(itemId)} to the Toolbox.";
    }

    /// <summary>Gives an item to a Pokemon; whatever it was already holding goes to Storage.</summary>
    private void GiveHeldItem(RosterEntryViewModel holder, int itemId, int quantity)
    {
        var displaced = "";
        if (holder.HasHeldItem)
        {
            var oldId = holder.Pkm.HeldItemId;
            AddToStorage(oldId, Math.Max(1, holder.Pkm.HeldItemQuantity));
            displaced = $" Its {ItemName(oldId)} went to storage.";
        }

        holder.SetHeldItem(itemId, quantity);
        Vm.MarkDirty();
        RefreshItems();
        Vm.StatusText = $"Gave {ItemName(itemId)} to {holder.Species} \"{holder.Name}\".{displaced}";
    }

    private static string ItemName(int itemId) => Lists.RBItems.TryGetValue(itemId, out var n) ? n : $"#{itemId}";

    private void OnRemoveToolboxItemClick(object? sender, RoutedEventArgs e)
    {
        if (Save == null) return;
        if (sender is not Button { DataContext: ToolboxRow row }) return;

        Save.HeldItems.RemoveAt(row.Index);
        Vm.MarkDirty();
        RefreshItems();
        Vm.StatusText = $"Removed {row.Name} from the Toolbox.";
    }

    private void OnDecrementStoredItemClick(object? sender, RoutedEventArgs e)
    {
        if (Save == null) return;
        if (sender is not Button { DataContext: StoredItemRow row }) return;

        if (TakeOneFromStorage(row.ItemId))
        {
            Vm.MarkDirty();
            RefreshItems();
            Vm.StatusText = $"Removed one {row.Name} from storage.";
        }
    }

    private void OnRemoveStoredItemClick(object? sender, RoutedEventArgs e)
    {
        if (Save == null) return;
        if (sender is not Button { DataContext: StoredItemRow row }) return;

        Save.StoredItems.RemoveAll(i => i.ItemID == row.ItemId);
        Vm.MarkDirty();
        RefreshItems();
        Vm.StatusText = $"Removed all {row.Quantity}x {row.Name} from storage.";
    }

    private void OnTakeHeldItemClick(object? sender, RoutedEventArgs e)
    {
        if (Save == null) return;
        if (sender is not Button { DataContext: RosterEntryViewModel holder } || !holder.HasHeldItem) return;

        var itemId = holder.Pkm.HeldItemId;
        AddToStorage(itemId, Math.Max(1, holder.Pkm.HeldItemQuantity));
        holder.SetHeldItem(0, 0);
        Vm.MarkDirty();
        RefreshItems();
        Vm.StatusText = $"Took {ItemName(itemId)} from {holder.Species} \"{holder.Name}\" into storage.";
    }

    #region Item drag and drop

    /// <summary>
    /// What's being dragged and where it came from. The payload rides in this field rather than
    /// in the platform drag data: every drag here is within this one window, and an in-process
    /// reference is both simpler and lossless. The platform DataTransfer carries only a marker
    /// format so our drop targets can tell our drags from anything foreign.
    /// </summary>
    private sealed record ItemDrag(string Source, int ItemId, int Quantity, int ToolboxIndex, RosterEntryViewModel? Holder);

    private static readonly DataFormat<string> ItemDragFormat = DataFormat.CreateStringApplicationFormat("sky-editor-item-drag");

    private ItemDrag? _dragPayload;
    private (Point Position, ItemDrag Payload)? _pressedDrag;

    /// <summary>Walks up from an event source to the row view-model that spawned it.</summary>
    private static T? RowContext<T>(object? source) where T : class
    {
        for (var el = source as StyledElement; el != null; el = el.Parent)
        {
            if (el.DataContext is T match) return match;
        }
        return null;
    }

    private void OnItemRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedDrag = null;
        if (Save == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        ItemDrag? payload = sender switch
        {
            _ when RowContext<AllItemRow>(e.Source) is { } all => new ItemDrag("all", all.ItemId, 0, -1, null),
            _ when RowContext<StoredItemRow>(e.Source) is { } stored => new ItemDrag("storage", stored.ItemId, stored.Quantity, -1, null),
            _ when RowContext<ToolboxRow>(e.Source) is { } box => new ItemDrag("toolbox", box.ItemId, box.Parameter, box.Index, null),
            _ when RowContext<RosterEntryViewModel>(e.Source) is { HasHeldItem: true } holder =>
                new ItemDrag("held", holder.Pkm.HeldItemId, holder.Pkm.HeldItemQuantity, -1, holder),
            _ => null,
        };

        if (payload != null)
        {
            _pressedDrag = (e.GetPosition(this), payload);
        }
    }

    private async void OnItemRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedDrag is not { } pressed) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pressedDrag = null;
            return;
        }

        // Only promote to a drag after a real movement, so plain clicks still select rows.
        var delta = e.GetPosition(this) - pressed.Position;
        if (Math.Abs(delta.X) < 5 && Math.Abs(delta.Y) < 5) return;

        _pressedDrag = null;
        _dragPayload = pressed.Payload;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(ItemDragFormat, pressed.Payload.Source));
        try
        {
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        finally
        {
            _dragPayload = null;
        }
    }

    private void OnItemRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressedDrag = null;
    }

    /// <summary>Hovering a drag over any inventory tab (header or content) switches to it, so a
    /// row can be dragged from one container's list to another's even though only one list is
    /// visible at a time.</summary>
    private void OnInventoryTabDragOver(object? sender, DragEventArgs e)
    {
        if (_dragPayload != null && sender is TabItem tab && InventoryTabs.SelectedItem != tab)
        {
            InventoryTabs.SelectedItem = tab;
        }
    }

    private void OnInventoryTargetDragOver(object? sender, DragEventArgs e)
    {
        var accepts = _dragPayload != null && sender switch
        {
            _ when ReferenceEquals(sender, StoragePanel) => _dragPayload.Source != "storage",
            _ when ReferenceEquals(sender, ToolboxPanel) => _dragPayload.Source != "toolbox",
            _ when ReferenceEquals(sender, HeldList) => RowContext<RosterEntryViewModel>(e.Source) != null,
            _ => false,
        };
        e.DragEffects = accepts ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnStorageDrop(object? sender, DragEventArgs e)
    {
        var payload = _dragPayload;
        if (Save == null || payload == null || payload.Source == "storage") return;
        e.Handled = true;

        switch (payload.Source)
        {
            case "all":
                var quantity = (int)(ItemQuantityInput.Value ?? 1);
                AddToStorage(payload.ItemId, quantity);
                Vm.StatusText = $"Added {quantity}x {ItemName(payload.ItemId)} to storage.";
                break;
            case "toolbox":
                Save.HeldItems.RemoveAt(payload.ToolboxIndex);
                AddToStorage(payload.ItemId, Math.Max(1, payload.Quantity));
                Vm.StatusText = $"Moved {ItemName(payload.ItemId)} from the Toolbox to storage.";
                break;
            case "held":
                payload.Holder!.SetHeldItem(0, 0);
                AddToStorage(payload.ItemId, Math.Max(1, payload.Quantity));
                Vm.StatusText = $"Took {ItemName(payload.ItemId)} from {payload.Holder.Species} into storage.";
                break;
            default:
                return;
        }

        Vm.MarkDirty();
        RefreshItems();
    }

    private void OnToolboxDrop(object? sender, DragEventArgs e)
    {
        var payload = _dragPayload;
        if (Save == null || payload == null || payload.Source == "toolbox") return;
        e.Handled = true;

        if (Save.HeldItems.Count >= Save.Offsets.HeldItemCount)
        {
            Vm.StatusText = "The Toolbox is full (20 slots).";
            return;
        }

        switch (payload.Source)
        {
            case "all":
                AddToToolbox(payload.ItemId, parameter: 0);
                return; // AddToToolbox already marked dirty and refreshed
            case "storage":
                if (!TakeOneFromStorage(payload.ItemId)) return;
                Save.HeldItems.Add(new RBHeldItem { ID = payload.ItemId, Parameter = 0 });
                Vm.StatusText = $"Moved one {ItemName(payload.ItemId)} from storage to the Toolbox.";
                break;
            case "held":
                payload.Holder!.SetHeldItem(0, 0);
                Save.HeldItems.Add(new RBHeldItem { ID = payload.ItemId, Parameter = Math.Clamp(payload.Quantity, 0, 127) });
                Vm.StatusText = $"Took {ItemName(payload.ItemId)} from {payload.Holder.Species} into the Toolbox.";
                break;
            default:
                return;
        }

        Vm.MarkDirty();
        RefreshItems();
    }

    private void OnHeldListDrop(object? sender, DragEventArgs e)
    {
        var payload = _dragPayload;
        if (Save == null || payload == null) return;
        if (RowContext<RosterEntryViewModel>(e.Source) is not { } target) return;
        if (payload.Source == "held" && ReferenceEquals(payload.Holder, target)) return;
        e.Handled = true;

        // Take the item out of wherever it came from first; then GiveHeldItem parks whatever the
        // target was already holding in storage and hands over the new item. Thrown stacks keep
        // their count (clamped to the held field's 7 bits); everything else is held as stack 0,
        // the game's own convention.
        switch (payload.Source)
        {
            case "all":
                break;
            case "storage":
                if (!TakeOneFromStorage(payload.ItemId)) return;
                break;
            case "toolbox":
                Save.HeldItems.RemoveAt(payload.ToolboxIndex);
                break;
            case "held":
                payload.Holder!.SetHeldItem(0, 0);
                break;
            default:
                return;
        }

        var carriedStack = payload.Source is "toolbox" or "held" ? Math.Clamp(payload.Quantity, 0, 127) : 0;
        GiveHeldItem(target, payload.ItemId, carriedStack);
    }

    #endregion
}
