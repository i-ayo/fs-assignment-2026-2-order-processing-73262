using PaymentService.Workers;
using Serilog;

// Bootstrap logger catches any startup failures before host is built
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("PaymentService starting up");

    var host = Host.CreateDefaultBuilder(args)
        .UseEnvironment("Production")
        // ── appsettings.Local.json — gitignored, holds the real RabbitMQ password ──
        .ConfigureAppConfiguration((_, cfg) =>
            cfg.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false))
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
            services.AddHostedService<PaymentWorker>();
        })
        .Build();

    Log.Information("PaymentService host built — waiting for messages");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "PaymentService terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
