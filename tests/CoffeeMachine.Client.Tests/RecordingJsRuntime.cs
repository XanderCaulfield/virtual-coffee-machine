using Microsoft.JSInterop;

namespace CoffeeMachine.Client.Tests;

/// <summary>Records JS interop calls and returns canned results.</summary>
internal sealed class RecordingJsRuntime : IJSRuntime
{
    public List<(string Identifier, object?[]? Args)> Calls { get; } = [];

    public Dictionary<string, object?> Results { get; } = new();

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        Calls.Add((identifier, args));
        return ValueTask.FromResult(Results.TryGetValue(identifier, out var value) ? (TValue)value! : default!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeAsync<TValue>(identifier, args);
}
