namespace Culprit.Core.Models;

public enum IncidentRule
{
    Stall,
    Sustained,
    Errors,
    UnattributedStall,
}

/// <summary>
/// One detected hiccup. InternalName is null for unattributed stalls. Detail is short text
/// naming the source: "Draw", "Update", the window length, or the first error line.
/// </summary>
public sealed record Incident(
    DateTime Timestamp,
    string? InternalName,
    IncidentRule Rule,
    double Value,
    double Limit,
    string Detail)
{
    /// <summary>Set by the caller once the alert policy decided to show a toast.</summary>
    public bool Notified { get; set; }
}
