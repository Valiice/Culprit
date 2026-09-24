using Culprit.Core.Models;
using Dalamud.Interface.ImGuiNotification;

namespace Culprit;

/// <summary>Turns an alert decision into a Dalamud notification toast.</summary>
internal sealed class Notifier
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(8);

    public void Show(Incident incident, string title, string body)
    {
        Services.NotificationManager.AddNotification(new Notification
        {
            Title = title,
            Content = body,
            Type = incident.Rule == IncidentRule.Sustained ? NotificationType.Info : NotificationType.Warning,
            InitialDuration = Duration,
            Minimized = false,
        });
    }

    public void ShowReducedMode(IEnumerable<string> failedProbeNames)
    {
        Services.NotificationManager.AddNotification(new Notification
        {
            Title = "Culprit",
            Content = $"Running in reduced mode. Unavailable: {string.Join(", ", failedProbeNames)}. See the Live tab.",
            Type = NotificationType.Warning,
            InitialDuration = TimeSpan.FromSeconds(12),
            Minimized = false,
        });
    }
}
