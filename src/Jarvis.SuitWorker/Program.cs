using Jarvis.SuitWorker.Workers;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<JarvisCoordinator>();


var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("""
╔═══════════════════════════════════════╗
║  🦾 JARVIS Suit Worker v1.0          ║
║  Status: ONLINE                       ║
║  Monitoring: All active suits         ║
╚═══════════════════════════════════════╝
""");

await host.RunAsync();