using System.Collections.Generic;

namespace SkyEditor.SaveEditor.MysteryDungeon.Rescue
{
    /// <summary>
    /// One named story-progression cutscene flag with display metadata: which phase of the
    /// game it belongs to and what event sets it.
    /// </summary>
    public sealed class RBStoryFlagInfo
    {
        public RBStoryFlagInfo(RBCutsceneFlag flag, RBStoryPhase phase, string description)
        {
            Flag = flag;
            Phase = phase;
            Description = description;
        }

        public RBCutsceneFlag Flag { get; }
        public RBStoryPhase Phase { get; }
        public string Description { get; }
    }

    public enum RBStoryPhase
    {
        MainStory,
        Postgame,
        /// <summary>
        /// Not story progress at all: scratch state the game recomputes every time it's relevant
        /// (see the Regi notes in RBBossEncounters/RECRUIT_MECHANICS.md). Shown for completeness.
        /// </summary>
        TransientScratch,
    }

    /// <summary>
    /// Every named <see cref="RBCutsceneFlag"/>, in story order, grouped into main story /
    /// postgame / transient scratch. Complements <see cref="RBBossEncounters"/> (which is a
    /// per-boss-species view of a subset of these): this is the per-event view of story
    /// progression itself.
    /// </summary>
    /// <remarks>
    /// Ordering and chapter numbers follow GAME_STORY.md (COMMUNITY, Bulbapedia walkthrough
    /// index). Event descriptions for the non-obvious flags are decomp-sourced:
    /// FrostyForestIntruded is set on reaching Frosty Forest 6F during the story visit
    /// (run_dungeon.c:581); MtFreezePeakComplete is the Ninetales scene, no fight
    /// (rescue_scenario.c "Meet Ninetales."); MagmaCavernMidReached is the Tyranitar/Alakazam
    /// mid-cavern scene (dungeon_cutscene.c:81); the two Regi flags are rewritten on every Regi
    /// room entry (dungeon_cutscene_regis.c).
    /// </remarks>
    public static class RBStoryFlags
    {
        public static IReadOnlyList<RBStoryFlagInfo> All { get; } = new[]
        {
            // Main story, in chapter order
            new RBStoryFlagInfo(RBCutsceneFlag.MtSteelReached, RBStoryPhase.MainStory, "Ch.1: Mt. Steel summit reached (Skarmory cutscene seen)"),
            new RBStoryFlagInfo(RBCutsceneFlag.MtSteelComplete, RBStoryPhase.MainStory, "Ch.1: Skarmory defeated at Mt. Steel"),
            new RBStoryFlagInfo(RBCutsceneFlag.SinisterWoodsReached, RBStoryPhase.MainStory, "Ch.2: Sinister Woods depths reached (Team Meanies cutscene seen)"),
            new RBStoryFlagInfo(RBCutsceneFlag.SinisterWoodsComplete, RBStoryPhase.MainStory, "Ch.2: Team Meanies defeated in Sinister Woods"),
            new RBStoryFlagInfo(RBCutsceneFlag.MtThunderPeakReached, RBStoryPhase.MainStory, "Ch.2: Mt. Thunder Peak reached (Zapdos cutscene seen)"),
            new RBStoryFlagInfo(RBCutsceneFlag.MtThunderPeakComplete, RBStoryPhase.MainStory, "Ch.2: Zapdos defeated at Mt. Thunder Peak"),
            new RBStoryFlagInfo(RBCutsceneFlag.MtBlazePeakReached, RBStoryPhase.MainStory, "Ch.3: Mt. Blaze Peak reached (Moltres cutscene seen)"),
            new RBStoryFlagInfo(RBCutsceneFlag.MtBlazePeakComplete, RBStoryPhase.MainStory, "Ch.3: Moltres defeated at Mt. Blaze Peak"),
            new RBStoryFlagInfo(RBCutsceneFlag.FrostyForestIntruded, RBStoryPhase.MainStory, "Ch.4: reached Frosty Forest 6F during the story (fugitive arc) visit"),
            new RBStoryFlagInfo(RBCutsceneFlag.FrostyGrottoReached, RBStoryPhase.MainStory, "Ch.4: Frosty Grotto reached (Articuno cutscene seen)"),
            new RBStoryFlagInfo(RBCutsceneFlag.FrostyGrottoComplete, RBStoryPhase.MainStory, "Ch.4: Articuno defeated at Frosty Grotto"),
            new RBStoryFlagInfo(RBCutsceneFlag.MtFreezePeakComplete, RBStoryPhase.MainStory, "Ch.4: Ninetales scene at Mt. Freeze Peak (story scene, no fight)"),
            new RBStoryFlagInfo(RBCutsceneFlag.UproarForestReached, RBStoryPhase.MainStory, "Ch.4: Uproar Forest reached (Mankey trio cutscene seen)"),
            new RBStoryFlagInfo(RBCutsceneFlag.UproarForestComplete, RBStoryPhase.MainStory, "Ch.4: Mankey trio defeated at Uproar Forest"),
            new RBStoryFlagInfo(RBCutsceneFlag.MagmaCavernMidReached, RBStoryPhase.MainStory, "Ch.5: Tyranitar/Alakazam scene mid-Magma Cavern"),
            new RBStoryFlagInfo(RBCutsceneFlag.MagmaCavernPitReached, RBStoryPhase.MainStory, "Ch.5: Magma Cavern Pit reached (Groudon cutscene seen)"),
            new RBStoryFlagInfo(RBCutsceneFlag.MagmaCavernPitComplete, RBStoryPhase.MainStory, "Ch.5: Groudon defeated at Magma Cavern Pit"),
            new RBStoryFlagInfo(RBCutsceneFlag.SkyTowerSummitReached, RBStoryPhase.MainStory, "Ch.5: Sky Tower Summit reached (Rayquaza cutscene seen)"),
            new RBStoryFlagInfo(RBCutsceneFlag.SkyTowerSummitComplete, RBStoryPhase.MainStory, "Ch.5: Rayquaza defeated at Sky Tower Summit (main story complete)"),

            // Postgame, in chapter order
            new RBStoryFlagInfo(RBCutsceneFlag.NorthernRangeReached, RBStoryPhase.Postgame, "Ch.7: Northern Range reached (Latios cutscene seen)"),
            new RBStoryFlagInfo(RBCutsceneFlag.NorthernRangeComplete, RBStoryPhase.Postgame, "Ch.7: Latios defeated at Northern Range"),
            new RBStoryFlagInfo(RBCutsceneFlag.FieryFieldReached, RBStoryPhase.Postgame, "Ch.9: Fiery Field reached (Entei cutscene seen)"),
            new RBStoryFlagInfo(RBCutsceneFlag.FieryFieldComplete, RBStoryPhase.Postgame, "Ch.9: Entei defeated at Fiery Field (starts the Wing chain)"),
            new RBStoryFlagInfo(RBCutsceneFlag.LightningFieldReached, RBStoryPhase.Postgame, "Ch.9: Lightning Field reached (Raikou cutscene seen)"),
            new RBStoryFlagInfo(RBCutsceneFlag.LightningFieldComplete, RBStoryPhase.Postgame, "Ch.9: Raikou defeated at Lightning Field"),
            new RBStoryFlagInfo(RBCutsceneFlag.NorthwindFieldReached, RBStoryPhase.Postgame, "Ch.9: Northwind Field reached (Suicune cutscene seen)"),
            new RBStoryFlagInfo(RBCutsceneFlag.NorthwindFieldComplete, RBStoryPhase.Postgame, "Ch.9: Suicune defeated at Northwind Field"),
            new RBStoryFlagInfo(RBCutsceneFlag.MtFarawayComplete, RBStoryPhase.Postgame, "Ch.10: Ho-Oh encounter at Mt. Faraway complete (no Reached flag exists)"),
            new RBStoryFlagInfo(RBCutsceneFlag.WesternCaveReached, RBStoryPhase.Postgame, "Ch.10: Western Cave reached (Mewtwo cutscene seen)"),
            new RBStoryFlagInfo(RBCutsceneFlag.WesternCaveComplete, RBStoryPhase.Postgame, "Ch.10: Mewtwo defeated at Western Cave"),
            new RBStoryFlagInfo(RBCutsceneFlag.MedichamComplete, RBStoryPhase.Postgame, "Ch.11: Medicham rescued in Wish Cave"),
            new RBStoryFlagInfo(RBCutsceneFlag.JirachiComplete, RBStoryPhase.Postgame, "Ch.11: Jirachi encounter at Wish Cave complete"),
            new RBStoryFlagInfo(RBCutsceneFlag.HowlingForestComplete, RBStoryPhase.Postgame, "Side content: Howling Forest complete (Smeargle join event)"),

            // Transient scratch, not story progress
            new RBStoryFlagInfo(RBCutsceneFlag.RegiItemObtained, RBStoryPhase.TransientScratch, "Rewritten on every Regi room entry: a Regi Part/Music Box is held, or that Regi was already recruited"),
            new RBStoryFlagInfo(RBCutsceneFlag.RegiRecruited, RBStoryPhase.TransientScratch, "Rewritten on every Regi room entry: that Regi is already in the roster"),
        };
    }
}
