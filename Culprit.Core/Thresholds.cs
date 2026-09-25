namespace Culprit.Core;

/// <summary>Detection limits. All times in milliseconds unless the name says seconds.</summary>
public sealed record Thresholds(
    double StallMs = 40,
    double SustainedMs = 3,
    double SustainedWindowSeconds = 5,
    int ErrorCount = 5,
    double ErrorWindowSeconds = 60,
    double UnattributedMs = 100);
