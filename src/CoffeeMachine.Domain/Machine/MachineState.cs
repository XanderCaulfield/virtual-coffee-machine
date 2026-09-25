namespace CoffeeMachine.Domain.Machine;

/// <summary>
/// The states of the vending machine state machine.
/// </summary>
/// <remarks>
/// The server drives <see cref="Idle"/> → <see cref="AwaitingSelection"/> →
/// <see cref="Dispensing"/> → <see cref="Idle"/> synchronously. The client
/// plays the 2–3 second brew animation locally, so no Brewing state exists
/// on the wire.
/// </remarks>
public enum MachineState
{
    /// <summary>No credit, nothing pending. The resting state.</summary>
    Idle,

    /// <summary>Credit has been inserted; waiting for a selection or cancel.</summary>
    AwaitingSelection,

    /// <summary>A drink and change have been dispensed; waiting for pickup.</summary>
    Dispensing,
}
