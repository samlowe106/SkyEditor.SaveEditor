using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Tests.MysteryDungeon.Rescue
{
    [TestClass]
    public class RBRescueTeamRankTests
    {
        private const string Category = "RB Rescue Team Rank Tests";

        [DataTestMethod]
        [TestCategory(Category)]
        [DataRow(0, RBRescueTeamRank.Normal)]
        [DataRow(49, RBRescueTeamRank.Normal)]
        [DataRow(50, RBRescueTeamRank.Bronze)]
        [DataRow(499, RBRescueTeamRank.Bronze)]
        [DataRow(500, RBRescueTeamRank.Silver)]
        [DataRow(1499, RBRescueTeamRank.Silver)]
        [DataRow(1500, RBRescueTeamRank.Gold)]
        [DataRow(2999, RBRescueTeamRank.Gold)]
        [DataRow(3000, RBRescueTeamRank.Platinum)]
        [DataRow(7499, RBRescueTeamRank.Platinum)]
        [DataRow(7500, RBRescueTeamRank.Diamond)]
        [DataRow(14999, RBRescueTeamRank.Diamond)]
        [DataRow(15000, RBRescueTeamRank.Lucario)]
        [DataRow(99999999, RBRescueTeamRank.Lucario)]
        public void RankForPoints_MatchesDecompThresholds(int points, RBRescueTeamRank expected)
        {
            Assert.AreEqual(expected, RBRescueTeamRanks.RankForPoints(points));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void PointsToNextRank_AtLucario_IsNull()
        {
            Assert.IsNull(RBRescueTeamRanks.PointsToNextRank(15000));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void PointsToNextRank_BelowThreshold_IsExactRemainder()
        {
            Assert.AreEqual(1, RBRescueTeamRanks.PointsToNextRank(49));
            Assert.AreEqual(50, RBRescueTeamRanks.PointsToNextRank(0));
        }
    }
}
