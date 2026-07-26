using System.Text.Json;
using EOS.KnowledgeGraph;

namespace EOS.Knowledge.Tests;

public class KnowledgeNodeTests
{
    [Theory]
    [InlineData(KnowledgeNodeType.Fact)]
    [InlineData(KnowledgeNodeType.Lesson)]
    [InlineData(KnowledgeNodeType.Pattern)]
    [InlineData(KnowledgeNodeType.Decision)]
    [InlineData(KnowledgeNodeType.Risk)]
    public void KnowledgeNodeType_HasExactlyTheFiveConstitutionNodeTypes(KnowledgeNodeType nodeType)
    {
        Assert.True(Enum.IsDefined(nodeType));
    }

    [Fact]
    public void KnowledgeNodeType_DefinesExactlyFiveValues()
    {
        var values = Enum.GetValues<KnowledgeNodeType>();

        Assert.Equal(5, values.Length);
    }

    [Fact]
    public void DomainTagsAndEvidenceRefs_RoundTripThroughJson()
    {
        var domainTags = new[] { "backend", "mobile" };
        var evidenceRefs = new[] { "artifact://evidence/1", "artifact://evidence/2" };

        var domainTagsJson = JsonSerializer.Serialize(domainTags);
        var evidenceRefsJson = JsonSerializer.Serialize(evidenceRefs);

        var roundTrippedDomainTags = JsonSerializer.Deserialize<string[]>(domainTagsJson);
        var roundTrippedEvidenceRefs = JsonSerializer.Deserialize<string[]>(evidenceRefsJson);

        Assert.Equal(domainTags, roundTrippedDomainTags);
        Assert.Equal(evidenceRefs, roundTrippedEvidenceRefs);
    }

    [Fact]
    public void KnowledgeNode_ConstructsWithAllApprovedFields()
    {
        var nodeId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var node = new KnowledgeNode(
            NodeId: nodeId,
            NodeType: KnowledgeNodeType.Fact,
            Content: "content",
            DomainTags: ["backend"],
            EvidenceRefs: ["artifact://evidence/1"],
            CreatedAt: createdAt);

        Assert.Equal(nodeId, node.NodeId);
        Assert.Equal(KnowledgeNodeType.Fact, node.NodeType);
        Assert.Equal("content", node.Content);
        Assert.Equal(["backend"], node.DomainTags);
        Assert.Equal(["artifact://evidence/1"], node.EvidenceRefs);
        Assert.Equal(createdAt, node.CreatedAt);
    }
}
