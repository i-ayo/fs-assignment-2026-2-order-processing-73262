using InventoryService.Workers;
using Serilog;

// Bootstrap logger catches any startup failures before host is built
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("InventoryService starting up");

    var host = Host.CreateDefaultBuilder(args)
        .UseEnvironment("Production")
        // ── Serilog: read full config (levels, sinks, enrichers) from appsettings.json ──
        .UseSerilog((ctx, services, lc) => lc
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId())
        .ConfigureServices((ctx, services) =>
        {
            services.AddHostedService<InventoryWorker>();
        })
        .Build();

    Log.Information("InventoryService host built — waiting for messages");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "InventoryService terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
