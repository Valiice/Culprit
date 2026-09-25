using System.Globalization;
using Culprit.Core.Detection;
using Culprit.Core.Models;

namespace Culprit.Core.Alerting;

public static class ToastFormatter
{
    public static string Title(Incident incident, string? displayName) =>
        displayName ?? incident.InternalName ?? "No plugin";

    public static string Body(Incident incident, Thresholds thresholds, int suppressedSinceLast)
    {
        var c = CultureInfo.InvariantCulture;
        var body = incident.Rule switch
        {
            IncidentRule.Stall => string.Format(c, "{0} took {1:0} ms (limit {2:0} ms)", incident.Detail, incident.Value, incident.Limit),
            IncidentRule.Sustained => string.Format(c, "Averaged {0:0.0} ms per frame over the last {1:0} s (limit {2:0} ms)", incident.Value, thresholds.SustainedWindowSeconds, incident.Limit),
            IncidentRule.Errors => string.Format(c, "{0:0} errors in the last {1:0} s", incident.Value, thresholds.ErrorWindowSeconds),
            IncidentRule.UnattributedStall => incident.Detail.StartsWith(Engine.GcPauseDetailPrefix, StringComparison.Ordinal)
                ? string.Format(c, "Frame took {0:0} ms, mostly a .NET garbage collection ({1})", incident.Value, incident.Detail)
                : string.Format(c, "Frame took {0:0} ms with no plugin over {1:0} ms", incident.Value, thresholds.StallMs),
            _ => incident.Detail,
        };

        return suppressedSinceLast > 0
            ? string.Format(c, "{0}, {1} more since the last warning", body, suppressedSinceLast)
            : body;
    }
}
