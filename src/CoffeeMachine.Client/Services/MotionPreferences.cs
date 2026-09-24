using Microsoft.JSInterop;

namespace CoffeeMachine.Client.Services;

/// <summary>
/// Reads the user's <c>prefers-reduced-motion</c> preference once, so the brew
/// sequence can compress its animations to near-instant for vestibular comfort.
/// </summary>
public sealed class MotionPreferences
{
    private readonly IJSRuntime _js;
    private Task<bool>? _loaded;

    public MotionPreferences(IJSRuntime js) => _js = js;

    public Task<bool> ReducedAsync => _loaded ??= LoadAsync();

    private async Task<bool> LoadAsync()
    {
        try
        {
            return await _js.InvokeAsync<bool>("coffeeMotion.reduced").ConfigureAwait(false);
        }
        catch (JSException)
        {
            return false;
        }
    }
}
