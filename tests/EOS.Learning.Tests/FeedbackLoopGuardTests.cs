namespace EOS.Learning.Tests;

public class FeedbackLoopGuardTests
{
    [Fact]
    public async Task CheckAsync_FlagsEverySelfReferentialTask()
    {
        var record = TestRecords.Lesson();
        var taskProvenance = new FixedTaskProvenanceQueryClient { SelfReferentialTaskIds = [Guid.NewGuid(), Guid.NewGuid()] };
        var publisher = new RecordingSelfReferentialOutcomeFlaggedEventPublisher();
        var guard = new FeedbackLoopGuard(taskProvenance, publisher);

        var flaggedCount = await guard.CheckAsync(record, CancellationToken.None);

        Assert.Equal(2, flaggedCount);
        Assert.Equal(2, publisher.CallCount);
    }

    [Fact]
    public async Task CheckAsync_FlagsNothing_WhenNoSelfReferentialTasksExist()
    {
        var record = TestRecords.Lesson();
        var taskProvenance = new FixedTaskProvenanceQueryClient { SelfReferentialTaskIds = [] };
        var publisher = new RecordingSelfReferentialOutcomeFlaggedEventPublisher();
        var guard = new FeedbackLoopGuard(taskProvenance, publisher);

        var flaggedCount = await guard.CheckAsync(record, CancellationToken.None);

        Assert.Equal(0, flaggedCount);
        Assert.Equal(0, publisher.CallCount);
    }
}
