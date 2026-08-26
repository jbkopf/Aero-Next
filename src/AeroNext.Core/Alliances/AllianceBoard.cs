namespace AeroNext.Core.Alliances;

/// <summary>Which regional carriers have already joined an alliance — permanent, once decided.</summary>
public sealed record AllianceBoard(IReadOnlyDictionary<string, string> JoinedCarrierToAirlineName)
{
    public static AllianceBoard Empty { get; } = new(new Dictionary<string, string>());

    public bool IsAvailable(RegionalCarrier carrier) => !JoinedCarrierToAirlineName.ContainsKey(carrier.Id);
}
