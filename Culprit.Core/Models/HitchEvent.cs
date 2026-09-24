namespace Culprit.Core.Models;

/// <summary>
/// One of Dalamud's own "[HITCH]" warnings, already resolved to a plugin where possible.
/// Only fed to the engine when Culprit's own timing probe for that source is unavailable.
/// </summary>
public sealed record HitchEvent(DateTime Timestamp, string? InternalName, double Ms, double LimitMs, string Source);
