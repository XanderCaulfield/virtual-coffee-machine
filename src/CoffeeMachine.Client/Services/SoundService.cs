using Microsoft.JSInterop;

namespace CoffeeMachine.Client.Services;

/// <summary>
/// Blazor wrapper around <c>window.coffeeSounds</c> (wwwroot/js/sound.js).
/// All effects are synthesized with the WebAudio API — no audio files.
/// The mute flag lives in localStorage and is mirrored here so the UI can
/// render the correct button state synchronously.
/// </summary>
public sealed class SoundService
{
    private readonly IJSRuntime _js;
    private bool _muted;
    private Task<bool>? _initialised;

    public SoundService(IJSRuntime js) => _js = js;

    /// <summary>Current mute state (false until <see cref="InitialiseAsync"/> completes).</summary>
    public bool Muted => _muted;

    public async ValueTask InitialiseAsync()
    {
        _initialised ??= LoadMutedAsync();
        _muted = await _initialised.ConfigureAwait(false);
    }

    public async ValueTask ToggleMutedAsync()
    {
        _muted = await _js.InvokeAsync<bool>("coffeeSounds.toggleMuted").ConfigureAwait(false);
    }

    public async ValueTask SetMutedAsync(bool muted)
    {
        _muted = await _js.InvokeAsync<bool>("coffeeSounds.setMuted", muted).ConfigureAwait(false);
    }

    /// <summary>Plays a named effect unless muted. Never throws (sound is non-critical).</summary>
    public async ValueTask PlayAsync(string name, double? arg = null)
    {
        if (_muted)
        {
            return;
        }

        try
        {
            await _js.InvokeVoidAsync("coffeeSounds.play", name, arg).ConfigureAwait(false);
        }
        catch (JSException)
        {
            // Audio unavailable (no WebAudio, script blocked) — UI must keep working.
        }
    }

    private async Task<bool> LoadMutedAsync()
    {
        try
        {
            return await _js.InvokeAsync<bool>("coffeeSounds.muted").ConfigureAwait(false);
        }
        catch (JSException)
        {
            return false;
        }
    }
}
