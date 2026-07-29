using EOS.KnowledgeGraph;

namespace EOS.Knowledge.Tests;

public class RetrievalRankingTests
{
    private static KnowledgeNode CreateNode(DateTimeOffset createdAt, string[] domainTags)
    {
        return new KnowledgeNode(
            NodeId: Guid.NewGuid(),
            NodeType: KnowledgeNodeType.Fact,
            Content: "content",
            DomainTags: domainTags,
            EvidenceRefs: [],
            CreatedAt: createdAt);
    }

    [Fact]
    public void Rank_OrdersMoreRecentNodesFirst_WhenWeightIsOnRecencyOnly()
    {
        var weights = new RankingWeights(VectorSimilarity: 0, Recency: 1, DomainMatch: 0, AccessFrequency: 0);
        var older = CreateNode(DateTimeOffset.UtcNow.AddDays(-30), []);
        var newer = CreateNode(DateTimeOffset.UtcNow, []);

        var ranked = RetrievalRanking.Rank([older, newer], weights, domainScope: null);

        Assert.Equal(newer.NodeId, ranked[0].NodeId);
        Assert.Equal(older.NodeId, ranked[1].NodeId);
    }

    [Fact]
    public void Rank_OrdersDomainMatchingNodesFirst_WhenWeightIsOnDomainMatchOnly()
    {
        var weights = new RankingWeights(VectorSimilarity: 0, Recency: 0, DomainMatch: 1, AccessFrequency: 0);
        var matching = CreateNode(DateTimeOffset.UtcNow.AddDays(-30), ["backend"]);
        var nonMatching = CreateNode(DateTimeOffset.UtcNow, ["mobile"]);

        var ranked = RetrievalRanking.Rank([nonMatching, matching], weights, domainScope: ["backend"]);

        Assert.Equal(matching.NodeId, ranked[0].NodeId);
        Assert.Equal(nonMatching.NodeId, ranked[1].NodeId);
    }

    [Fact]
    public void Rank_ReturnsAllCandidates_WhenAllWeightsAreZero()
    {
        var weights = new RankingWeights(VectorSimilarity: 0, Recency: 0, DomainMatch: 0, AccessFrequency: 0);
        var first = CreateNode(DateTimeOffset.UtcNow, []);
        var second = CreateNode(DateTimeOffset.UtcNow.AddDays(-1), []);

        var ranked = RetrievalRanking.Rank([first, second], weights, domainScope: null);

        Assert.Equal(2, ranked.Count);
    }
}
