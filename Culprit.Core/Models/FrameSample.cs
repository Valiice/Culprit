namespace Culprit.Core.Models;

/// <summary>One plugin's cost in one frame. Zero when that source had nothing this frame.</summary>
public sealed record PluginTiming(string InternalName, double DrawMs, double UpdateMs);

/// <summary>Everything the engine needs about one frame.</summary>
public sealed record FrameSample(DateTime Timestamp, double FrameMs, IReadOnlyList<PluginTiming> Plugins);
