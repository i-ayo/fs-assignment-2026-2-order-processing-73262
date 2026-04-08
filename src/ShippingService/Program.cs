using ShippingService.Workers;
using Serilog;

// Bootstrap logger catches any startup failures before host is built
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("ShippingService starting up");

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
            services.AddHostedService<ShippingWorker>();
        })
        .Build();

    Log.Information("ShippingService host built — waiting for messages");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "ShippingService terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
