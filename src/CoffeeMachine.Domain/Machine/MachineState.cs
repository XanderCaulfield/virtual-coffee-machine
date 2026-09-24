namespace CoffeeMachine.Domain.Machine;

/// <summary>
/// The states of the vending machine state machine.
/// </summary>
/// <remarks>
/// The server drives <see cref="Idle"/> → <see cref="AwaitingSelection"/> →
/// <see cref="Dispensing"/> → <see cref="Idle"/> synchronously.
/// <see cref="Brewing"/> is part of the observable surface (the client plays
/// a 2–3 second brew animation locally) but is never entered by the domain
/// core itself; it exists so rehydrated and future hardware states can be
/// represented without contract changes.
/// </remarks>
public enum MachineState
{
    /// <summary>No credit, nothing pending. The resting state.</summary>
    Idle,

    /// <summary>Credit has been inserted; waiting for a selection or cancel.</summary>
    AwaitingSelection,

    /// <summary>A drink is being brewed (used by the client animation only).</summary>
    Brewing,

    /// <summary>A drink and change have been dispensed; waiting for pickup.</summary>
    Dispensing,
}
