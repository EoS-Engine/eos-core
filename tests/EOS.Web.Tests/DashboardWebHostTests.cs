using EOS.Contracts;
using EOS.Dashboard;
using EOS.SharedKernel.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace EOS.Web.Tests;

// WP-030: real Kestrel WebApplication bound to an ephemeral port, exercised with plain
// HttpClient — no Microsoft.AspNetCore.Mvc.Testing / WebApplicationFactory needed, since
// EOS.Web deliberately has no entry-point Program class for one to target (EOS.Runner hosts
// the app; EOS.Web only supplies route mapping, per the approved WP030-05 plan). Hand-rolled
// Fixed*Client stubs match this repository's established no-mocking-framework convention
// (see EOS.Dashboard.Tests.DashboardQueryServiceTests).
public sealed class DashboardWebHostTests : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;
    private FixedRecentEventsQueryClient? _recentEventsQueryClient;

    public async Task InitializeAsync()
    {
        var loopStatus = new LoopStatus(Guid.NewGuid(), OperationalMode.Assisted, 0.5);
        var tasks = new DispatchedTask[]
        {
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "WP030-05 web test task", [], [], 1,
                TaskLifecycleState.Ready, SchedulingMode.Immediate, null, null, false, 0, null),
        };
        var events = new RecentEventSummary[]
        {
            new(Guid.NewGuid(), "SampleEvent", "EOS.Web.Tests", DateTimeOffset.UtcNow, """{"marker":"web-test"}"""),
        };

        _recentEventsQueryClient = new FixedRecentEventsQueryClient(events);
        var dashboardQueryService = new DashboardQueryService(
            new FixedLoopStatusQueryClient(loopStatus),
            new FixedTaskStatusQueryClient(tasks),
            _recentEventsQueryClient);
        var dashboardOptions = new DashboardOptions { Title = "EOS Dashboard Test" };

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();
        DashboardWebHost.MapRoutes(_app, dashboardQueryService, dashboardOptions);
        await _app.StartAsync();

        var address = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Root_ReturnsOkAndHtmlContainingTheConfiguredTitle()
    {
        var response = await _client!.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("EOS Dashboard Test", html);
    }

    [Fact]
    public async Task LoopStatus_ReturnsOkAndTheServiceResult()
    {
        var response = await _client!.GetAsync("/api/loop-status");
        var loopStatus = await response.Content.ReadFromJsonAsync<LoopStatus>();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(loopStatus);
        Assert.Equal(OperationalMode.Assisted, loopStatus.CurrentMode);
        Assert.Equal(0.5, loopStatus.LoopHealthScore);
    }

    [Fact]
    public async Task Tasks_WithStateQueryParameter_ReturnsOkAndTheServiceResult()
    {
        var response = await _client!.GetAsync("/api/tasks?state=Ready");
        var tasks = await response.Content.ReadFromJsonAsync<List<DispatchedTask>>();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(tasks);
        Assert.Single(tasks);
        Assert.Equal("WP030-05 web test task", tasks[0].Description);
    }

    [Fact]
    public async Task RecentEvents_WithoutCountQueryParameter_DefaultsToFifty()
    {
        var response = await _client!.GetAsync("/api/recent-events");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(50, _recentEventsQueryClient!.LastRequestedCount);
    }

    [Fact]
    public async Task RecentEvents_WithCountQueryParameter_PassesItThroughAndReturnsTheServiceResult()
    {
        var response = await _client!.GetAsync("/api/recent-events?count=5");
        var events = await response.Content.ReadFromJsonAsync<List<RecentEventSummary>>();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(5, _recentEventsQueryClient!.LastRequestedCount);
        Assert.NotNull(events);
        Assert.Single(events);
        Assert.Equal("SampleEvent", events[0].EventType);
    }

    private sealed class FixedLoopStatusQueryClient(LoopStatus status) : ILoopStatusQueryClient
    {
        public Task<LoopStatus> GetCurrentStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status);
    }

    private sealed class FixedTaskStatusQueryClient(IReadOnlyList<DispatchedTask> tasks) : ITaskStatusQueryClient
    {
        public Task<IReadOnlyList<DispatchedTask>> GetByStateAsync(TaskLifecycleState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(tasks);

        public Task<int> CountByStateAsync(TaskLifecycleState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(tasks.Count);
    }

    private sealed class FixedRecentEventsQueryClient(IReadOnlyList<RecentEventSummary> events) : IRecentEventsQueryClient
    {
        public int? LastRequestedCount { get; private set; }

        public Task<IReadOnlyList<RecentEventSummary>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
        {
            LastRequestedCount = count;
            return Task.FromResult(events);
        }
    }
}
