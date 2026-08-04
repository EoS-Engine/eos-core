using EOS.Contracts;

namespace EOS.Planner.Tests;

public class GoalManagerTests
{
    private static GoalManager CreateManager(GoalStore store) =>
        new(store, new NoOpGoalCreatedEventPublisher(), new NoOpGoalCancelledEventPublisher());

    private static async Task<GoalStore> CreateStoreAsync()
    {
        var store = new GoalStore(TestConnectionString.SqlServer);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        return store;
    }

    private static Goal SubmittedGoal(string statement, Guid? parentGoalId = null, string[]? domainTags = null) => new(
        GoalId: Guid.NewGuid(),
        Statement: statement,
        ParentGoalId: parentGoalId,
        DomainTags: domainTags ?? [],
        SubmittedByActor: "Product Owner",
        State: GoalLifecycleState.Proposed,
        PlanId: null);

    [Fact]
    public async Task CreateGoalAsync_PersistsANewGoal_InTheProposedState_RespectingTheCallerSuppliedGoalId()
    {
        var store = await CreateStoreAsync();
        var manager = CreateManager(store);
        var submitted = SubmittedGoal("Add a logging statement", domainTags: ["logging"]);

        var goal = await manager.CreateGoalAsync(submitted, CancellationToken.None);

        Assert.Equal(submitted.GoalId, goal.GoalId);
        Assert.Equal(GoalLifecycleState.Proposed, goal.State);
        var persisted = await store.GetByIdAsync(goal.GoalId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(goal.Statement, persisted.Statement);
    }

    [Fact]
    public async Task TransitionStateAsync_UpdatesAndPersistsTheNewState()
    {
        var store = await CreateStoreAsync();
        var manager = CreateManager(store);
        var goal = await manager.CreateGoalAsync(SubmittedGoal("Goal to validate"), CancellationToken.None);

        var validated = await manager.TransitionStateAsync(goal, GoalLifecycleState.Validated, CancellationToken.None);

        Assert.Equal(GoalLifecycleState.Validated, validated.State);
        var persisted = await store.GetByIdAsync(goal.GoalId, CancellationToken.None);
        Assert.Equal(GoalLifecycleState.Validated, persisted!.State);
    }

    [Fact]
    public async Task AttachPlanAsync_TransitionsToPlanned_AndSetsThePlanId()
    {
        var store = await CreateStoreAsync();
        var manager = CreateManager(store);
        var goal = await manager.CreateGoalAsync(SubmittedGoal("Goal to plan"), CancellationToken.None);
        var planId = Guid.NewGuid();

        var planned = await manager.AttachPlanAsync(goal, planId, CancellationToken.None);

        Assert.Equal(GoalLifecycleState.Planned, planned.State);
        Assert.Equal(planId, planned.PlanId);
    }

    // §11.6: "cancelling a Goal cancels every incomplete descendant Task via the existing Task
    // Lifecycle rule" — realized here as cascading Goal-hierarchy cancellation (§11.2).
    [Fact]
    public async Task CancelGoalAsync_CascadesToEveryDescendantGoal()
    {
        var store = await CreateStoreAsync();
        var manager = CreateManager(store);
        var parent = await manager.CreateGoalAsync(SubmittedGoal("Parent goal"), CancellationToken.None);
        var child = await manager.CreateGoalAsync(SubmittedGoal("Child goal", parentGoalId: parent.GoalId), CancellationToken.None);
        var grandchild = await manager.CreateGoalAsync(SubmittedGoal("Grandchild goal", parentGoalId: child.GoalId), CancellationToken.None);

        await manager.CancelGoalAsync(parent.GoalId, "no longer needed", CancellationToken.None);

        Assert.Equal(GoalLifecycleState.Cancelled, (await store.GetByIdAsync(parent.GoalId, CancellationToken.None))!.State);
        Assert.Equal(GoalLifecycleState.Cancelled, (await store.GetByIdAsync(child.GoalId, CancellationToken.None))!.State);
        Assert.Equal(GoalLifecycleState.Cancelled, (await store.GetByIdAsync(grandchild.GoalId, CancellationToken.None))!.State);
    }

    [Fact]
    public async Task CancelGoalAsync_ThrowsForANonExistentGoal()
    {
        var store = await CreateStoreAsync();
        var manager = CreateManager(store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.CancelGoalAsync(Guid.NewGuid(), "reason", CancellationToken.None));
    }

    // CodeRabbit PR #20 round 1: a self-parented Goal would make
    // CancelGoalAndDescendantsAsync's recursive traversal recurse on itself indefinitely.
    [Fact]
    public async Task CreateGoalAsync_ThrowsArgumentException_WhenTheGoalIsItsOwnParent()
    {
        var store = await CreateStoreAsync();
        var manager = CreateManager(store);
        var goalId = Guid.NewGuid();
        var submitted = new Goal(
            GoalId: goalId,
            Statement: "Self-parented goal",
            ParentGoalId: goalId,
            DomainTags: [],
            SubmittedByActor: "Product Owner",
            State: GoalLifecycleState.Proposed,
            PlanId: null);

        await Assert.ThrowsAsync<ArgumentException>(
            () => manager.CreateGoalAsync(submitted, CancellationToken.None));
    }

    // CodeRabbit PR #20 round 1: Constitution Part 6 §6.2's "Any → Cancelled" transition requires
    // a cancellation justification; §11.6 mirrors this rule at the Goal level.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CancelGoalAsync_ThrowsArgumentException_WhenReasonIsBlank(string reason)
    {
        var store = await CreateStoreAsync();
        var manager = CreateManager(store);
        var goal = await manager.CreateGoalAsync(SubmittedGoal("Goal to cancel"), CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(
            () => manager.CancelGoalAsync(goal.GoalId, reason, CancellationToken.None));
    }
}
