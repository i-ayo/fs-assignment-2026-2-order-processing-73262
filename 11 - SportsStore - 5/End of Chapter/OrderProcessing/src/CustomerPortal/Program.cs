using CustomerPortal.Services;
using Serilog;

// ── Bootstrap logger: shows startup output immediately before config loads ────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ───────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId());

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── Cart (scoped = one cart per Blazor Server circuit / browser session) ─────
// Scoped persists across client-side navigations within the same circuit while
// giving each browser tab/user their own independent cart instance.
// Singleton caused all tabs and users to share one cart (and fired StateChanged
// across every connected circuit on every cart mutation).
builder.Services.AddScoped<CartService>();

// ── Order API HTTP client ──────────────────────────────────────────────────────
// JsonStringEnumConverter is applied here so OrderStatus is read as a string
// ("Completed") and mapped to the enum – this fixes the My Orders bug (Task A).
builder.Services.AddHttpClient<OrderApiService>(c =>
    c.BaseAddress = new Uri(
        builder.Configuration["OrderApi:BaseUrl"] ?? "http://localhost:5100/"));

var app = builder.Build();

Log.Information("CustomerPortal starting in {Environment}", app.Environment.EnvironmentName);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<CustomerPortal.Components.App>()
    .AddInteractiveServerRenderMode();

app.Lifetime.ApplicationStarted.Register(() =>
{
    foreach (var url in app.Urls)
        Log.Information("CustomerPortal listening on {Url}", url);
});

Log.Information("CustomerPortal is ready");
app.Run();

} // end try
catch (Exception ex)
{
    Log.Fatal(ex, "CustomerPortal terminated unexpectedly during startup");
}
finally
{
    Log.CloseAndFlush();
}
