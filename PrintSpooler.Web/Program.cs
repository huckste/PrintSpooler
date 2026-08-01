using PrintSpooler.Web.Components;
using PrintSpooler.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var apiBaseAddress = builder.Configuration["ApiBaseAddress"]
    ?? throw new InvalidOperationException("ApiBaseAddress is not configured.");

builder.Services.AddHttpClient(
    "PrintSpoolerApi",
    client =>
    {
        client.BaseAddress = new Uri(apiBaseAddress);
    }
);

builder.Services.AddScoped<ApiClient>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
