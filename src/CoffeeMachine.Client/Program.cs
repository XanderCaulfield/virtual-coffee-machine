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

// All API traffic goes to /api/v1 on the same origin the app is served
// from (the hosted Api project).
builder.Services.AddScoped(sp =>
{
    var origin = new Uri(builder.HostEnvironment.BaseAddress);
    var apiBase = new Uri(origin, "api/v1/");
    return new HttpClient { BaseAddress = apiBase };
});

builder.Services.AddScoped(sp => new MachineApiClient(
    sp.GetRequiredService<HttpClient>(),
    sp.GetRequiredService<IJSRuntime>()));

await builder.Build().RunAsync();
