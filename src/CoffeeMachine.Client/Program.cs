using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using CoffeeMachine.Client;
using CoffeeMachine.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<SoundService>();
builder.Services.AddScoped<MotionPreferences>();

// ------------------------------------------------------------------------
// HTTP pipeline (demo seam)
//
// DEMO MODE — when the page URL contains ?demo=1 the app talks to an
// in-process fake backend (FakeBackendHandler) that serves
// contract-conforming JSON for every /api/v1 endpoint, so the UI runs
// standalone without the real Api project. To exercise the REAL API,
// simply load the app without ?demo=1 (the machine talks to the same
// origin the app is served from, i.e. the hosted Api).
// ------------------------------------------------------------------------
builder.Services.AddScoped(sp =>
{
    var navigation = sp.GetRequiredService<NavigationManager>();
    var origin = new Uri(builder.HostEnvironment.BaseAddress);
    var apiBase = new Uri(origin, "api/v1/");

    if (HasDemoFlag(navigation.Uri))
    {
        return new HttpClient(new FakeBackendHandler()) { BaseAddress = apiBase };
    }

    return new HttpClient { BaseAddress = apiBase };
});

builder.Services.AddScoped(sp => new MachineApiClient(
    sp.GetRequiredService<HttpClient>(),
    sp.GetRequiredService<IJSRuntime>()));

await builder.Build().RunAsync();

static bool HasDemoFlag(string uri)
{
    var query = new Uri(uri, UriKind.RelativeOrAbsolute).Query;
    foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var separator = pair.IndexOf('=');
        var key = separator < 0 ? pair : pair[..separator];
        if (string.Equals(key, "demo", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}
