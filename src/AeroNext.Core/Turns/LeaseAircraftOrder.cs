using AeroNext.Core.Airlines;

namespace AeroNext.Core.Turns;

/// <summary>
/// The expensive instant-metal escape hatch (CLAUDE.md): bypasses the
/// production queue entirely, delivering the same quarter it's ordered.
/// Costs 1 action like AircraftOrder — it's not contested (nobody else's
/// lease competes with yours), but it's still a real commitment of
/// attention, not a free market posture.
/// </summary>
public sealed record LeaseAircraftOrder(AircraftType Type, int Quantity) : QuarterOrder;
