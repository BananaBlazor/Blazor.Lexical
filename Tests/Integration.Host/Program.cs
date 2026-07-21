using Blazor.Lexical;
using Tests.Integration.Host.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddLexicalBlazor();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Exposed so the integration test project can host this app in-process via
// WebApplicationFactory<Program>.
public partial class Program { }
