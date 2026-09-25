using Culprit.Core;
using Culprit.Core.Alerting;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Culprit;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public double StallMs { get; set; } = 40;

    public double SustainedMs { get; set; } = 3;

    public double SustainedWindowSeconds { get; set; } = 5;

    public int ErrorCount { get; set; } = 5;

    public double ErrorWindowSeconds { get; set; } = 60;

    public double UnattributedMs { get; set; } = 100;

    public double CooldownMinutes { get; set; } = 5;

    public bool NotifyUnattributed { get; set; } = false;

    public bool WarnOnFirstIncident { get; set; } = false;

    public double RepeatWindowMinutes { get; set; } = 10;

    public bool Muted { get; set; } = false;

    public bool DebugLog { get; set; } = false;

    public bool ShowAllPlugins { get; set; } = false;

    public List<string> IgnoredPlugins { get; set; } = [];

    public static Configuration Load(IDalamudPluginInterface pluginInterface)
    {
        Configuration config;
        try
        {
            config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        }
        catch (Exception e)
        {
            Services.Log.Warning(e, "Culprit configuration could not be read, using defaults");
            config = new Configuration();
        }

        config.IgnoredPlugins ??= [];

        if (config.Version < 2 && config.StallMs == 20)
        {
            config.StallMs = 40;
        }

        config.Version = 2;

        config.StallMs = Math.Clamp(config.StallMs, 5, 500);
        config.SustainedMs = Math.Clamp(config.SustainedMs, 0.5, 50);
        config.SustainedWindowSeconds = Math.Clamp(config.SustainedWindowSeconds, 1, 60);
        config.ErrorCount = Math.Clamp(config.ErrorCount, 1, 100);
        config.ErrorWindowSeconds = Math.Clamp(config.ErrorWindowSeconds, 5, 600);
        config.UnattributedMs = Math.Clamp(config.UnattributedMs, 20, 1000);
        config.CooldownMinutes = Math.Clamp(config.CooldownMinutes, 0.1, 60);
        config.RepeatWindowMinutes = Math.Clamp(config.RepeatWindowMinutes, 1, 120);

        return config;
    }

    public Thresholds ToThresholds() => new(
        this.StallMs,
        this.SustainedMs,
        this.SustainedWindowSeconds,
        this.ErrorCount,
        this.ErrorWindowSeconds,
        this.UnattributedMs);

    public AlertSettings ToAlertSettings() => new(
        TimeSpan.FromMinutes(this.CooldownMinutes),
        this.Muted,
        this.NotifyUnattributed,
        this.WarnOnFirstIncident,
        TimeSpan.FromMinutes(this.RepeatWindowMinutes));

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
