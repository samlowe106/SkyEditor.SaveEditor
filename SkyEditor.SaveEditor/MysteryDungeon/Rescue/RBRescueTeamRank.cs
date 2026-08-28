namespace SkyEditor.SaveEditor.MysteryDungeon.Rescue
{
    /// <summary>
    /// Mirrors <c>enum TeamRanks</c> (<c>include/rescue_team_info.h</c>) in the pret/pmd-red decomp.
    /// </summary>
    public enum RBRescueTeamRank
    {
        Normal = 0,
        Bronze = 1,
        Silver = 2,
        Gold = 3,
        Platinum = 4,
        Diamond = 5,
        Lucario = 6,
    }

    /// <summary>
    /// Computes a Rescue Team's rank from its point total, matching
    /// <c>GetRescueTeamRank</c>/<c>sRescueRankMaxPoints</c> (<c>src/rescue_team_info.c</c>) exactly:
    /// a team is the lowest rank whose threshold its points haven't reached yet.
    /// </summary>
    public static class RBRescueTeamRanks
    {
        private static readonly (RBRescueTeamRank Rank, string Name, int MaxPoints)[] Thresholds =
        {
            (RBRescueTeamRank.Normal, "Normal Rank", 50),
            (RBRescueTeamRank.Bronze, "Bronze Rank", 500),
            (RBRescueTeamRank.Silver, "Silver Rank", 1500),
            (RBRescueTeamRank.Gold, "Gold Rank", 3000),
            (RBRescueTeamRank.Platinum, "Platinum Rank", 7500),
            (RBRescueTeamRank.Diamond, "Diamond Rank", 15000),
            // The decomp's sRescueRankMaxPoints entry for LUCARIO_RANK (100000000) is never
            // actually compared against -- GetRescueTeamRank falls through to Lucario once points
            // meet or exceed the Diamond threshold, rather than reading this one. Reproduced here
            // anyway for fidelity/documentation.
            (RBRescueTeamRank.Lucario, "Lucario Rank", 100000000),
        };

        /// <summary>The rank a team with <paramref name="points"/> Rescue Team points holds.</summary>
        public static RBRescueTeamRank RankForPoints(int points)
        {
            foreach (var (rank, _, maxPoints) in Thresholds)
            {
                if (points < maxPoints)
                {
                    return rank;
                }
            }
            return RBRescueTeamRank.Lucario;
        }

        /// <summary>
        /// The fewest points that put a team at <paramref name="rank"/>: 0 for Normal, otherwise
        /// the previous rank's threshold (the inverse of <see cref="RankForPoints"/>, so
        /// <c>RankForPoints(MinPointsFor(r)) == r</c> for every rank).
        /// </summary>
        public static int MinPointsFor(RBRescueTeamRank rank) =>
            rank == RBRescueTeamRank.Normal ? 0 : Thresholds[(int)rank - 1].MaxPoints;

        /// <summary>Display name for a rank, matching <c>gRescueTeamRanks</c> (<c>src/strings.c</c>).</summary>
        public static string NameOf(RBRescueTeamRank rank) => Thresholds[(int)rank].Name;

        /// <summary>
        /// How many more points are needed to reach the next rank, or null if already at the
        /// highest rank (Lucario). Matches <c>GetPtsToNextRank</c>.
        /// </summary>
        public static int? PointsToNextRank(int points)
        {
            var rank = RankForPoints(points);
            if (rank == RBRescueTeamRank.Lucario)
            {
                return null;
            }
            return Thresholds[(int)rank].MaxPoints - points;
        }
    }
}
