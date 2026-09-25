# Culprit

Finds the plugin behind your stutter.

Culprit watches every other Dalamud plugin's draw time, update time and error output. When one of them stalls a frame, eats frame time steadily, or throws repeatedly, Culprit shows a Dalamud notification naming that plugin.

## Installation

This is a private plugin. Build it and load it as a dev plugin:

1. Build with `dotnet build Culprit/Culprit.csproj -c Release`.
2. In the game, open `/xlsettings`, go to Experimental, and add the folder `Culprit/bin/Release/Culprit` as a dev plugin location.
3. Open `/xlplugins`, find Culprit under Dev Tools, and enable it.

## Usage

- `/culprit` opens the window.
- `/culprit pause` and `/culprit resume` stop and restart detection.
- `/culprit reset` clears counters and the incident log.
- `/culprit report` saves a text report you can share.

The Live tab shows each plugin's latest and longest draw and update times, its 5 second average, and how many errors and incidents it has caused. It hides quiet plugins by default; tick "Show all plugins" to see every loaded plugin instead of just the offenders. The "Copy report" and "Save report" buttons on the Live tab give you the same plain text report as `/culprit report`, saved to `%AppData%\XIVLauncher\pluginConfigs\Culprit\culprit-report.txt`. The Incidents tab lists everything Culprit caught, with a Copy button for bug reports. Settings holds the thresholds, the cooldown, and the ignore list.

## What counts as a hiccup

- Stall: a plugin's draw or update took longer than the stall limit in one frame (default 40 ms).
- Sustained: a plugin averaged more than the sustained limit per frame over a window (default 3 ms over 5 s).
- Errors: a plugin logged at least the error count within the error window (default 5 in 60 s).
- Unattributed: the whole frame was long but no plugin crossed the stall limit. Logged, not shown, unless you enable it.

By default a plugin must stall or drain twice within 10 minutes before Culprit shows a toast, so a one-off cost like a plugin opening a window for the first time stays quiet; the Incidents tab still records every occurrence.

Culprit stays quiet while zoning, in cutscenes, and for 5 seconds after any plugin loads or unloads, and for 5 seconds after a zone change finishes.

## How it works

Dalamud already times every plugin's draw and update callbacks for its own Plugin Statistics window (`/xlstats`), but that tracking is off by default and has no alerts or history. Culprit turns it on, reads those numbers each frame, and applies the rules above. It also listens to Dalamud's log stream to count errors per plugin.

Those counters are Dalamud internals, not public API. If a Dalamud update changes them, the Live tab shows which signal became unavailable and Culprit keeps running with what is left.

Update handlers are matched to plugins through Dalamud's own subscription lists, which also covers static handlers and shared libraries, and Dalamud's internal handlers appear as "Dalamud (internal)". That row sums Dalamud's own per-frame work, which includes callbacks plugins register through Dalamud events, so a toast on it can mean a plugin callback that Dalamud invoked directly.

Turning on Dalamud's update-time tracking has one side effect on other plugins: while it is on, Dalamud logs their update-handler exceptions as a generic framework error without the plugin name, and the dev-plugin error toast does not appear. Culprit still attributes those errors through the exception's stack trace. Unloading Culprit restores the previous setting.

## Limitations

- Time spent inside a plugin's game hooks is not measured by Dalamud and so not by Culprit. It can only show up as an unattributed stall.
- Memory use cannot be attributed to a plugin and is not tracked.
- Incidents are kept in memory only and are gone after a restart.
- Because it reads Dalamud internals, Culprit is not eligible for the official plugin repository.
- If you untick the tracking boxes in /xlstats, Culprit stops receiving draw or update times and says so on the Live tab.

## Privacy

Nothing leaves your machine. Culprit reads other plugins' timing numbers and error messages, never their data.
