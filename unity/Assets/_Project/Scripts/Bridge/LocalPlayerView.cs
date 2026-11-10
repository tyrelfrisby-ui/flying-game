using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// LOCAL 2-PLAYER: one player's view — which camera, which aeroplane, where on the screen. Present on each player's
    /// camera ONLY while split screen is active (<see cref="LocalSplit.Active"/>); with it off there is none, and every
    /// lookup below falls back to exactly what the single-player game used (the static <see cref="ChaseCamera.InCockpit"/>,
    /// the main camera's chase).
    ///
    /// THE PER-PLAYER HUD API (for the split-screen HUD lane). A HUD component that lives on a player's camera asks
    /// <c>LocalPlayerView.Of(this)</c>; null means single-player, draw as always. Otherwise:
    ///   • <see cref="PlayerIndex"/> 0 = P1 (left / top), 1 = P2 (right / bottom);
    ///   • <see cref="Driver"/> that player's aeroplane (IasMs, AltitudeM, Sim.Aircraft …);
    ///   • <see cref="GuiRect"/> its view in OnGUI pixels (top-left origin) — draw inside it, never in Screen.width;
    ///   • <see cref="Camera"/> its camera; GL drawn in OnPostRender is already clipped to the camera's own rect;
    ///   • <see cref="InCockpit"/> that camera's view, not the first player's;
    ///   • <see cref="All"/> every player's view, in player order.
    /// </summary>
    public sealed class LocalPlayerView : MonoBehaviour
    {
        public int PlayerIndex;
        public FlightSimDriver Driver;
        public ChaseCamera Chase;
        public Camera Camera;

        private static readonly List<LocalPlayerView> _all = new(2);
        public static IReadOnlyList<LocalPlayerView> All => _all;

        private void OnEnable()
        {
            if (Camera == null) Camera = GetComponent<Camera>();
            if (Chase == null) Chase = GetComponent<ChaseCamera>();
            if (!_all.Contains(this)) _all.Add(this);
            _all.Sort((a, b) => a.PlayerIndex.CompareTo(b.PlayerIndex));
        }

        private void OnDisable() => _all.Remove(this);

        /// <summary>This view's area in GUI pixels (top-left origin).</summary>
        public Rect GuiRect
        {
            get
            {
                if (Camera == null) return new Rect(0, 0, Screen.width, Screen.height);
                Rect p = Camera.pixelRect;
                return new Rect(p.x, Screen.height - p.y - p.height, p.width, p.height);
            }
        }

        /// <summary>This view is in the cockpit (its own camera, not the first player's).</summary>
        public bool InCockpit => Chase != null ? Chase.IsInCockpit : ChaseCamera.InCockpit;

        /// <summary>The view on the same GameObject as <paramref name="c"/> (a HUD on a player's camera), or null.</summary>
        public static LocalPlayerView Of(Component c)
        {
            if (c == null || _all.Count == 0) return null;
            foreach (var v in _all) if (v != null && v.gameObject == c.gameObject) return v;
            return null;
        }

        public static LocalPlayerView ForDriver(FlightSimDriver d)
        {
            if (d == null) return null;
            foreach (var v in _all) if (v != null && v.Driver == d) return v;
            return null;
        }

        /// <summary>Is <paramref name="d"/>'s pilot looking from the cockpit? Single-player: the static, as always.</summary>
        public static bool InCockpitFor(FlightSimDriver d) => ForDriver(d)?.InCockpit ?? ChaseCamera.InCockpit;

        /// <summary>The chase camera following <paramref name="d"/>. Single-player: the main camera's, as always.</summary>
        public static ChaseCamera ChaseFor(FlightSimDriver d)
        {
            var v = ForDriver(d);
            if (v != null && v.Chase != null) return v.Chase;
            Camera m = Camera.main;
            return m != null && m.TryGetComponent(out ChaseCamera c) ? c : null;
        }
    }
}
