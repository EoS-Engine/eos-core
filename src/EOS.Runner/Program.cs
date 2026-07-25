using EOS.Runner.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = Host.CreateApplicationBuilder(args).Build();
var logger = host.Services.GetRequiredService<ILogger<BootstrapRunner>>();

var loader = JsonConfigurationLoader.Discover();
var runner = BootstrapRunner.CreateEosBootstrap(loader, logger);
var results = await runner.RunAsync();

return results.Count > 0 && results.All(r => r.Status) ? 0 : 1;
