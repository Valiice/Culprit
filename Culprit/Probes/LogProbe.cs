using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Serilog.Events;

namespace Culprit.Probes;

/// <summary>An error-level log event. PluginName comes from Dalamud's log property; Frames are the exception's stack assemblies for fallback attribution.</summary>
public sealed record RawError(DateTime Timestamp, string? PluginName, IReadOnlyList<Assembly> Frames, string Message);

/// <summary>One of Dalamud's "[HITCH]" warnings, with its template properties.</summary>
public sealed record RawHitch(DateTime Timestamp, string Name, double Ms, double LimitMs);

/// <summary>
/// Listens to Dalamud's internal Serilog sink so Culprit sees every plugin's log lines. Runs on
/// whatever thread logs; only enqueues. The sampler drains the queues on the framework thread.
/// </summary>
internal sealed class LogProbe : IDisposable
{
    private const string Name = "Log stream";
    private const string PluginNameProperty = "Dalamud.PluginName";

    private readonly ConcurrentQueue<RawError> errors = new();
    private readonly ConcurrentQueue<RawHitch> hitches = new();
    private readonly object? sink;
    private readonly EventInfo? logLine;
    private readonly Delegate? handler;

    public LogProbe(DalamudInternals internals)
    {
        try
        {
            var sinkType = internals.GetType("Dalamud.Logging.Internal.SerilogEventSink");
            this.sink = DalamudInternals.Property(sinkType, "Instance").GetValue(null)
                        ?? throw new InvalidOperationException("SerilogEventSink.Instance is null");
            this.logLine = DalamudInternals.Event(sinkType, "LogLine");
            this.handler = new EventHandler<(string Line, LogEvent LogEvent)>(this.OnLogLine);
            this.logLine.AddEventHandler(this.sink, this.handler);
            this.Status = ProbeStatus.Ok(Name);
        }
        catch (Exception e)
        {
            this.handler = null;
            this.Status = ProbeStatus.Failed(Name, e);
        }
    }

    public ProbeStatus Status { get; }

    public bool TryDequeueError([MaybeNullWhen(false)] out RawError error) => this.errors.TryDequeue(out error);

    public bool TryDequeueHitch([MaybeNullWhen(false)] out RawHitch hitch) => this.hitches.TryDequeue(out hitch);

    public void Dispose()
    {
        try
        {
            if (this.handler is not null)
            {
                this.logLine?.RemoveEventHandler(this.sink, this.handler);
            }
        }
        catch (Exception e)
        {
            Services.Log.Warning(e, "Culprit could not detach from the log sink");
        }
    }

    private static string? Scalar(LogEvent e, string property) =>
        e.Properties.TryGetValue(property, out var value) && value is ScalarValue { Value: { } v } ? v.ToString() : null;

    private static double ScalarDouble(LogEvent e, string property) =>
        e.Properties.TryGetValue(property, out var value) && value is ScalarValue { Value: IConvertible c }
            ? c.ToDouble(System.Globalization.CultureInfo.InvariantCulture)
            : 0;

    private static IReadOnlyList<Assembly> FrameAssemblies(Exception? exception)
    {
        if (exception is null)
        {
            return [];
        }

        var result = new List<Assembly>();
        foreach (var frame in new StackTrace(exception, false).GetFrames())
        {
            if (frame.GetMethod()?.DeclaringType?.Assembly is { } assembly && !result.Contains(assembly))
            {
                result.Add(assembly);
            }
        }

        return result;
    }

    private void OnLogLine(object? sender, (string Line, LogEvent LogEvent) e)
    {
        try
        {
            var logEvent = e.LogEvent;
            var now = DateTime.UtcNow;

            if (logEvent.Level == LogEventLevel.Warning && logEvent.MessageTemplate.Text.StartsWith("[HITCH]", StringComparison.Ordinal))
            {
                this.hitches.Enqueue(new RawHitch(now, Scalar(logEvent, "Name") ?? string.Empty, ScalarDouble(logEvent, "Total"), ScalarDouble(logEvent, "Max")));
                return;
            }

            if (logEvent.Level < LogEventLevel.Error)
            {
                return;
            }

            var firstLine = e.Line.Split('\n', 2)[0].TrimEnd('\r');
            this.errors.Enqueue(new RawError(now, Scalar(logEvent, PluginNameProperty), FrameAssemblies(logEvent.Exception), firstLine));
        }
        catch
        {
            // Never let a probe failure reach Dalamud's logging pipeline.
        }
    }
}
