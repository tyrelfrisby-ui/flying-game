namespace FlyingGame.Core;

/// <summary>
/// ============================================================================================================
///  THE DEVELOPER PAGE SWITCH. If you are here to close the Developer page before a public release, change
///  <see cref="ShippedOpen"/> below from true to false. That is the whole job; there is nothing else to find.
/// ============================================================================================================
///
/// Ty, 2026-11-11, asked which shape the gate should take, and chose this one: "It's just another page and the
/// shipped version just doesn't have the ui to get to it."
///
/// SO THE GATE IS NAVIGATION, NOT COMPILATION. The page is always compiled and always present in the binary. What
/// the switch removes is the ROUTE to it — the tab in Options — and nothing else. Three consequences, all of them
/// deliberate:
///
///  * there is no <c>#if</c> and no scripting define, so the page cannot rot behind a flag nobody builds with. It
///    compiles on every platform on every build, and its tests run on every build;
///  * the page must therefore be HARMLESS WHEN UNREACHABLE. It costs nothing while it is not drawn and it changes
///    nothing about the aeroplane by existing: the only thing that touches the flight model is a pilot moving a
///    knob, and a saved override is only ever re-applied for a knob a pilot actually moved. DeveloperAccessTests
///    holds that;
///  * because the code ships either way, a developer can still get in on a shipping build without a new binary —
///    see <see cref="EnvVar"/>.
///
/// PUBLIC RELEASE IS THE STATED DESTINATION for this project, so flipping this is a real release blocker. It is
/// true today because Ty's beta test pilots are flying TestFlight and OTA builds and need the page: "But make it
/// available during beta because I have some test pilots who are doing beta testing."
/// </summary>
public static class DeveloperAccess
{
    /// <summary>
    /// ***** THE SWITCH. true = the Developer tab is offered in Options. false = it is not, and the page is
    /// unreachable through the UI. SET THIS FALSE BEFORE PUBLIC RELEASE. *****
    /// </summary>
    public const bool ShippedOpen = true;

    /// <summary>
    /// THE WAY IN ON A SHIPPING BUILD, once <see cref="ShippedOpen"/> is false — set it to "1" and the tab comes
    /// back for that run. It is an environment variable, which on iOS means an Xcode scheme or a device launched
    /// from a developer's machine, not something a player can reach: a phone has no shell, and nothing in the app
    /// writes it. It is read ONCE, at first use, because reading the environment every frame is a per-frame cost on
    /// a page that is supposed to be free when it is closed.
    ///
    /// It is named here, and in docs/AGENT-LOG.md, precisely so that the next person does not have to invent a
    /// secret gesture — a hidden tap sequence would be undocumented, discoverable by players, and impossible to
    /// audit before release.
    /// </summary>
    public const string EnvVar = "AERO_DEVELOPER";

    private static bool _envChecked;
    private static bool _envOpen;

    /// <summary>Set at runtime to open or close the route for this run, overriding both of the above. The page's own
    /// tests use it; so can an editor menu item. It is deliberately not persisted — a flag that survives a relaunch
    /// is a flag that ships open by accident.</summary>
    public static bool? RuntimeOverride;

    /// <summary>Is the Developer page reachable from the UI right now? The ONLY thing that should ever gate a
    /// Developer entry point, so that there is one answer and one place to change it.</summary>
    public static bool Open
    {
        get
        {
            if (RuntimeOverride.HasValue) return RuntimeOverride.Value;
            if (ShippedOpen) return true;
            if (!_envChecked)
            {
                _envChecked = true;
                try { _envOpen = System.Environment.GetEnvironmentVariable(EnvVar) == "1"; }
                catch (System.Exception) { _envOpen = false; }   // a platform that refuses the lookup is simply closed
            }
            return _envOpen;
        }
    }

    /// <summary>Tests only: forget the cached environment answer and any runtime override.</summary>
    public static void ResetForTest()
    {
        RuntimeOverride = null;
        _envChecked = false;
        _envOpen = false;
    }
}
