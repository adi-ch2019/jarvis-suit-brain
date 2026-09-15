using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Jarvis.Worker;

var builder = Host.CreateApplicationBuilder(args);

// ---------------------------------------------------------------------------
// Worker subscribes to the "alerts-topic" subscription and processes
// AlertMessage events (paging Tony, warming up the suit, etc.).
// Same passwordless pattern as the API.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton(sp =>
{
    var ns = builder.Configuration["ServiceBus:Namespace"];
    var conn = builder.Configuration["ServiceBus:Connection"];

    return string.IsNullOrWhiteSpace(conn)
        ? new ServiceBusClient(ns!, new DefaultAzureCredential())
        : new ServiceBusClient(conn);
});

builder.Services.AddSingleton(sp =>
{
    var client = sp.GetRequiredService<ServiceBusClient>();
    return client.CreateProcessor(
        topicName: builder.Configuration["ServiceBus:TopicName"]!,
        subscriptionName: builder.Configuration["ServiceBus:SubscriptionName"]!,
        new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 4
        });
});

builder.Services.AddHostedService<AlertPublisher>();

var host = builder.Build();
await host.RunAsync();