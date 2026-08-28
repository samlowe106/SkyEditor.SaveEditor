using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Gui;

/// <summary>
/// Converts a <see cref="SkyEditor.SaveEditor.MysteryDungeon.Rescue.RBFriendArea"/> enum
/// identifier (concatenated PascalCase, e.g. "BountifulSea" -- matching the asset filenames under
/// Assets/FriendAreas/, which must keep using the raw enum name) into a readable display name
/// ("Bountiful Sea") by splitting on capital letters. This is a mechanical transform, not a
/// curated list of official names, so a handful of oddly-named entries (e.g.
/// AgedChamberOExclaim) won't come out perfectly -- but it beats showing the raw identifier
/// everywhere, without hand-verifying all 57 official names.
/// </summary>
internal static class FriendAreaDisplayNames
{
    public static string NameOf(SkyEditor.SaveEditor.MysteryDungeon.Rescue.RBFriendArea area)
    {
        var spaced = Regex.Replace(area.ToString(), "(?<=[a-z0-9])(?=[A-Z])", " ");
        return Regex.Replace(spaced, @"^Mt(?=\s)", "Mt.");
    }
}

/// <summary>
/// Shared highlight brushes for staged-but-unsaved edits. A light orange at fairly high opacity,
/// not a fully-saturated orange at low opacity -- the latter reads as a muddy brown once blended
/// against a dark theme background instead of the intended light-orange tint.
/// </summary>
internal static class PendingHighlight
{
    public static readonly IBrush Pending = new SolidColorBrush(Color.FromArgb(160, 255, 183, 94));
    public static readonly IBrush None = Brushes.Transparent;

    public static IBrush For(bool isPending) => isPending ? Pending : None;
}

/// <summary>
/// Every RB monster ID <see cref="RBBossEncounters"/> defines a constant for, gathered by
/// reflection (same technique <see cref="MainWindow"/>'s boss list uses) rather than duplicating
/// the list by hand. Used to keep story bosses/legendaries out of the roster delete feature: the
/// decomp-derived remarks on <see cref="RBBossEncounters"/> note every boss's cutscene script
/// rechecks <c>HasRecruitedMon()</c> on revisit, so removing one from the roster would make the
/// game think it was never recruited and could replay that encounter.
/// </summary>
internal static class BossSpecies
{
    public static readonly HashSet<int> Ids = typeof(RBBossEncounters)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(int))
        .Select(f => (int)f.GetValue(null)!)
        .ToHashSet();
}

/// <summary>
/// Loads and caches item icon bitmaps from <see cref="ItemIcons.IconFileByItemId"/>. Many items
/// share one icon file (all Orbs, all TMs, etc. -- see ItemIcons.generated.cs), so this caches by
/// filename, not by item ID, to avoid decoding the same bitmap repeatedly.
/// </summary>
internal static class ItemIconLoader
{
    private static readonly Dictionary<string, Bitmap?> Cache = new();

    public static Bitmap? GetIcon(int itemId)
    {
        if (!ItemIcons.IconFileByItemId.TryGetValue(itemId, out var fileName))
        {
            return null;
        }

        if (Cache.TryGetValue(fileName, out var cached))
        {
            return cached;
        }

        Bitmap? bitmap = null;
        var uri = new System.Uri($"avares://SkyEditor.SaveEditor.Gui/Assets/Items/{fileName}");
        if (AssetLoader.Exists(uri))
        {
            using var stream = AssetLoader.Open(uri);
            bitmap = new Bitmap(stream);
        }

        Cache[fileName] = bitmap;
        return bitmap;
    }
}

/// <summary>
/// One yes/no/not-applicable fact about a boss row, rendered as a colored glyph with a tooltip
/// explaining what it means -- see the Story Flags tab. <see cref="Value"/> is null for "this
/// doesn't apply to this boss," distinct from false ("applies, and it's not true yet"), so a boss
/// with no story-flag requirement at all reads as a neutral dash rather than a misleading red x.
/// <see cref="NoAssociatedFlag"/> marks a cell whose value isn't backed by a specific
/// <c>RBCutsceneFlag</c> at all (checked some other way, or simply doesn't apply) -- it appends a
/// "*" to the glyph so that's visible at a glance, not just in the tooltip.
/// </summary>
public sealed class BoolCell
{
    public bool? Value { get; init; }
    public string Tooltip { get; init; } = "";
    public bool NoAssociatedFlag { get; init; }

    public string Glyph => (Value switch { true => "✓", false => "✗", null => "–" }) + (NoAssociatedFlag ? "*" : "");

    public IBrush Tint => Value switch
    {
        true => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
        false => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
        null => new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
    };
}

public sealed class BossRow
{
    public string Name { get; init; } = "";
    public int SpeciesId { get; init; }
    public bool Recruited { get; init; }
    public bool IsPending { get; init; }
    public required BoolCell RecruitedCell { get; init; }
    public required BoolCell RecruitableCell { get; init; }
    public required BoolCell CutsceneFlagCell { get; init; }

    public IBrush Highlight => PendingHighlight.For(IsPending);
}

/// <summary>One mail entry on the Wonder Mail tab (an accepted job, a Pelipper board posting, or a mailbox mail).</summary>
public sealed class WonderMailRow
{
    /// <summary>Which RBMailData list this row lives in: "job", "board", or "mailbox".</summary>
    public string Section { get; init; } = "";
    public int Index { get; init; }
    public string Summary { get; init; } = "";
    public string Reward { get; init; } = "";
    /// <summary>Display-formatted Wonder Mail password, or empty for non-Wonder mail (SOS/A-OK/Thank-You).</summary>
    public string Password { get; init; } = "";
    public string Tooltip { get; init; } = "";
}

/// <summary>One used-Wonder-Mail history entry (a completed job's fingerprint; blocks reusing its password).</summary>
public sealed class UsedMailRow
{
    public int Index { get; init; }
    public string Summary { get; init; } = "";
    public string Tooltip { get; init; } = "";
}

/// <summary>One row of the Story Flags tab's "Story" list: a named story-progression cutscene flag (see RBStoryFlags).</summary>
public sealed class StoryFlagRow
{
    public string Phase { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public required BoolCell SetCell { get; init; }
}

/// <summary>
/// "Met at" location IDs (<see cref="Lists.RBLocations"/> keys) that aren't floored dungeons, so
/// pairing them with a floor number is meaningless -- confirmed for "???" via a real save's
/// protagonist entry, which is stored with MetAt=64 ("???") and Floor=0. The rest are the town
/// hub, the team base, and other menu/status entries rather than places you actually explore
/// floor by floor. Not necessarily exhaustive of every such ID in the list.
/// </summary>
internal static class NoFloorLocations
{
    public static readonly HashSet<int> Ids = new()
    {
        49, // Autopilot
        52, // Dojo Registration
        63, // Out on Rescue
        64, // ???
        69, 70, // Pokémon Square
        71, 72, // Rescue Team Base
        74, // Client Pokémon
    };

    public static bool AppliesTo(int locationId) => !Ids.Contains(locationId);
}

/// <summary>
/// Item IDs confirmed (via community research cross-referenced against the decomp having zero
/// named constants or special-case code for any of them) to be genuine leftovers from
/// development with no real function -- normally unobtainable in legitimate play, only ever seen
/// via a cheat device poking the raw item ID directly. Kept fully accessible here rather than
/// hidden, since browsing/adding exactly this kind of thing is a lot of the point of a save
/// editor -- just annotated so it's clear what they are.
/// </summary>
internal static class DevLeftoverItems
{
    public static readonly HashSet<int> Ids = new() { 50, 51, 52, 237, 238, 239 }; // Ring D/E/F, G Machine 6/7/8

    public static string Annotate(string name, int itemId) =>
        Ids.Contains(itemId) ? $"{name} (unused -- leftover from development)" : name;
}

public sealed class StoredItemRow
{
    public string Name { get; init; } = "";
    public int ItemId { get; init; }
    public int Quantity { get; init; }
    public int PendingDelta { get; init; }

    public IBrush Highlight => PendingHighlight.For(PendingDelta != 0);
    public Bitmap? Icon => ItemIconLoader.GetIcon(ItemId);
    public string DisplayText => PendingDelta != 0
        ? $"{DevLeftoverItems.Annotate(Name, ItemId)} x{Quantity} ({PendingDelta:+#;-#;0} unsaved)"
        : $"{DevLeftoverItems.Annotate(Name, ItemId)} x{Quantity}";
}

public sealed class AllItemRow
{
    public int ItemId { get; init; }
    public string Name { get; init; } = "";

    public Bitmap? Icon => ItemIconLoader.GetIcon(ItemId);
    public string DisplayName => DevLeftoverItems.Annotate(Name, ItemId);
}

/// <summary>One slot of the Toolbox (the 20-slot carried bag, RBSave.HeldItems).</summary>
public sealed class ToolboxRow
{
    public int Index { get; init; }
    public int ItemId { get; init; }
    public string Name { get; init; } = "";
    public int Parameter { get; init; }

    public Bitmap? Icon => ItemIconLoader.GetIcon(ItemId);
    public string DisplayText => Parameter > 0
        ? $"{DevLeftoverItems.Annotate(Name, ItemId)} x{Parameter}"
        : DevLeftoverItems.Annotate(Name, ItemId);
}


/// <summary>Renders an <see cref="RBRescueTeamRank"/> with the game's own rank name ("Gold Rank").</summary>
public sealed class RankNameConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly RankNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is RBRescueTeamRank rank ? RBRescueTeamRanks.NameOf(rank) : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
