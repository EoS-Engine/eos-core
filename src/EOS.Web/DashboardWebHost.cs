using EOS.Contracts;
using EOS.Dashboard;
using EOS.SharedKernel.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EOS.Web;

/// <summary>
/// WP-030: the EOS.Web presentation surface (Constitution §0.11: Dashboards "presented via
/// EOS.Web"). Per ADR-015-001's Composition Root Adapter Pattern (reapplied here exactly as its
/// own "Consequences" section anticipates), EOS.Web never constructs infrastructure itself —
/// <see cref="DashboardQueryService"/> and <see cref="DashboardOptions"/> are supplied by
/// EOS.Runner, the sole composition root (Constitution Part 1 §1.3). EOS.Web's dependency
/// surface stays exactly EOS.Dashboard + EOS.Contracts (Part 1 §1.2), never EOS.Infrastructure.
/// </summary>
public static class DashboardWebHost
{
    public static async Task RunAsync(DashboardQueryService dashboardQueryService, DashboardOptions dashboardOptions, string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        MapRoutes(app, dashboardQueryService, dashboardOptions);

        await app.RunAsync();
    }

    public static void MapRoutes(IEndpointRouteBuilder app, DashboardQueryService dashboardQueryService, DashboardOptions dashboardOptions)
    {
        app.MapGet("/", () => Results.Content(BuildHtml(dashboardOptions.Title), "text/html"));

        app.MapGet("/api/loop-status", async (CancellationToken cancellationToken) =>
            await dashboardQueryService.GetLoopStatusAsync(cancellationToken));

        app.MapGet("/api/tasks", async (TaskLifecycleState state, CancellationToken cancellationToken) =>
            await dashboardQueryService.GetTasksByStateAsync(state, cancellationToken));

        app.MapGet("/api/recent-events", async (int count = 50, CancellationToken cancellationToken = default) =>
            await dashboardQueryService.GetRecentEventsAsync(count, cancellationToken));
    }

    // WP-030: pull-only live view (§5 of the approved Implementation Plan) — the page fetches
    // each endpoint exactly once on load; no auto-refresh, no SignalR/WebSockets/polling
    // infrastructure of any kind.
    private static string BuildHtml(string title) => $$"""
        <!DOCTYPE html>
        <html>
        <head><title>{{title}}</title></head>
        <body>
        <h1>{{title}}</h1>
        <h2>Loop Status</h2>
        <pre id="loop-status"></pre>
        <h2>Tasks (Ready)</h2>
        <pre id="tasks"></pre>
        <h2>Recent Events</h2>
        <pre id="recent-events"></pre>
        <script>
        fetch('/api/loop-status').then(r => r.json()).then(d => document.getElementById('loop-status').textContent = JSON.stringify(d, null, 2));
        fetch('/api/tasks?state=Ready').then(r => r.json()).then(d => document.getElementById('tasks').textContent = JSON.stringify(d, null, 2));
        fetch('/api/recent-events?count=50').then(r => r.json()).then(d => document.getElementById('recent-events').textContent = JSON.stringify(d, null, 2));
        </script>
        </body>
        </html>
        """;
}
