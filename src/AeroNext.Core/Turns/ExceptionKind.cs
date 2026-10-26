namespace AeroNext.Core.Turns;

public enum ExceptionKind
{
    RouteEntered,
    LoadFactorDropped,
    LoadFactorSurged,
    OrderRejectedOverBudget,
    OrderRejectedOutOfRange,
    OrderRejectedInvalidStops,
    OrderRejectedAircraftNotYetAvailable,
    ActionCapIncreased,
    RouteAircraftTypeChanged,
}
