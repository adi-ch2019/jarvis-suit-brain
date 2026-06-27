using Jarvis.SuitWorker.Workers;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<JarvisCoordinator>();
builder.Services.AddHealthChecks();

var host = builder.Build();

Console.WriteLine("""
╔═══════════════════════════════════════════════════════════╗
║  🦾 JARVIS Suit Worker v1.0                             ║
║  Status: ONLINE                                         ║
║  Backing Services: Service Bus Consumer                 ║
║  "I'm always watching, sir"                             ║
╚═══════════════════════════════════════════════════════════╝
""");

await host.RunAsync();