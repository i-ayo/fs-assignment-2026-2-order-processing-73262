using AdminDashboard.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId());

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient<AdminApiService>(c =>
    c.BaseAddress = new Uri(
        builder.Configuration["OrderApi:BaseUrl"] ?? "http://localhost:5100/"));

var app = builder.Build();

Log.Information("AdminDashboard starting in {Environment}", app.Environment.EnvironmentName);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<AdminDashboard.Components.App>()
    .AddInteractiveServerRenderMode();

app.Lifetime.ApplicationStarted.Register(() =>
{
    foreach (var url in app.Urls)
        Log.Information("AdminDashboard listening on {Url}", url);
});

Log.Information("AdminDashboard is ready");
app.Run();
