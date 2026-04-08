using CustomerPortal.Services;
using Serilog;
using System.Text.Json.Serialization;
using System.Text.Json;

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

// ── Cart (singleton so it persists across page navigations) ──────────────────
builder.Services.AddSingleton<CartService>();

// ── Order API HTTP client ──────────────────────────────────────────────────────
// JsonStringEnumConverter is applied here so OrderStatus is read as a string
// ("Completed") and mapped to the enum – this fixes the My Orders bug (Task A).
builder.Services.AddHttpClient<OrderApiService>(c =>
    c.BaseAddress = new Uri(
        builder.Configuration["OrderApi:BaseUrl"] ?? "https://localhost:7050/"));

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

Log.Information("CustomerPortal is ready");
app.Run();
