namespace Culprit.Core.Models;

/// <summary>An error-level log line attributed (or not) to a plugin. Message is its first line.</summary>
public sealed record ErrorEvent(DateTime Timestamp, string? InternalName, string Message);
