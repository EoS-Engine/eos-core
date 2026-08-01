using EOS.KnowledgeGraph;

namespace EOS.Knowledge.Tests;

public class OntologyValidatorTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_SQLSERVER_CONNECTION_STRING is not set.");

    private static async Task<KnowledgeGraphStore> CreateStoreAsync()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        return store;
    }

    private static async Task<Guid> CreateNodeAsync(KnowledgeGraphStore store, KnowledgeNodeType nodeType)
    {
        var nodeId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, nodeType, "content", [], [], DateTimeOffset.UtcNow), CancellationToken.None);
        return nodeId;
    }

    /// <summary>Mirrors config/Knowledge.json's default Ontology configuration.</summary>
    private static OntologyValidator CreateValidator(KnowledgeGraphStore store) => new(
        store,
        dependsOnDisallowedTargetTypes: [KnowledgeNodeType.Lesson],
        governanceApprovalRequiredRelationshipTypes: [RelationshipType.Replaces, RelationshipType.Supersedes]);

    [Fact]
    public async Task ValidateAsync_RejectsDependsOn_WhenTargetIsALesson()
    {
        var store = await CreateStoreAsync();
        var lessonId = await CreateNodeAsync(store, KnowledgeNodeType.Lesson);
        var validator = CreateValidator(store);

        var result = await validator.ValidateAsync(
            new RelationshipEdge { TargetNodeId = lessonId, RelationshipType = RelationshipType.DependsOn });

        Assert.False(result.IsValid);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_AllowsDependsOn_WhenTargetIsNotALesson()
    {
        var store = await CreateStoreAsync();
        var factId = await CreateNodeAsync(store, KnowledgeNodeType.Fact);
        var validator = CreateValidator(store);

        var result = await validator.ValidateAsync(
            new RelationshipEdge { TargetNodeId = factId, RelationshipType = RelationshipType.DependsOn });

        Assert.True(result.IsValid);
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_RejectsReplaces_WhenGovernanceApprovalRefIsMissing()
    {
        var store = await CreateStoreAsync();
        var targetId = await CreateNodeAsync(store, KnowledgeNodeType.Fact);
        var validator = CreateValidator(store);

        var result = await validator.ValidateAsync(
            new RelationshipEdge { TargetNodeId = targetId, RelationshipType = RelationshipType.Replaces });

        Assert.False(result.IsValid);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_AllowsReplaces_WhenGovernanceApprovalRefIsPresent()
    {
        var store = await CreateStoreAsync();
        var targetId = await CreateNodeAsync(store, KnowledgeNodeType.Fact);
        var validator = CreateValidator(store);

        var result = await validator.ValidateAsync(new RelationshipEdge
        {
            TargetNodeId = targetId,
            RelationshipType = RelationshipType.Replaces,
            GovernanceApprovalRef = "adr://123",
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsSupersedes_WhenGovernanceApprovalRefIsMissing()
    {
        var store = await CreateStoreAsync();
        var targetId = await CreateNodeAsync(store, KnowledgeNodeType.Fact);
        var validator = CreateValidator(store);

        var result = await validator.ValidateAsync(
            new RelationshipEdge { TargetNodeId = targetId, RelationshipType = RelationshipType.Supersedes });

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsRequires_WhenTargetDoesNotExist()
    {
        var store = await CreateStoreAsync();
        var validator = CreateValidator(store);

        var result = await validator.ValidateAsync(new RelationshipEdge
        {
            TargetNodeId = Guid.NewGuid(),
            RelationshipType = RelationshipType.Requires,
        });

        Assert.False(result.IsValid);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_AllowsRequires_WhenTargetExists()
    {
        var store = await CreateStoreAsync();
        var targetId = await CreateNodeAsync(store, KnowledgeNodeType.Fact);
        var validator = CreateValidator(store);

        var result = await validator.ValidateAsync(
            new RelationshipEdge { TargetNodeId = targetId, RelationshipType = RelationshipType.Requires });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_AllowsRelatedTo_Unconditionally()
    {
        var store = await CreateStoreAsync();
        var validator = CreateValidator(store);

        var result = await validator.ValidateAsync(new RelationshipEdge
        {
            TargetNodeId = Guid.NewGuid(),
            RelationshipType = RelationshipType.RelatedTo,
        });

        Assert.True(result.IsValid);
    }
}
