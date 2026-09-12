using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Bullets drawn as short bright streaks (GL lines) after the scene renders — every bullet in the air, from
    /// anyone's guns. Lives on the camera; reads the CombatController's gunnery.</summary>
    public sealed class TracerOverlay : MonoBehaviour
    {
        public CombatController Combat;
        private Material _mat;

        private void Start()
        {
            Shader sh = Shader.Find("FlyingGame/HudLine") ?? Shader.Find("Unlit/Color");
            if (sh != null) _mat = new Material(sh) { color = Color.white };
        }

        private void OnPostRender()
        {
            if (Combat == null || _mat == null || SessionSettings.MenuOpen) return;
            var bullets = Combat.Gunnery.Bullets;
            _mat.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);
            var c = new Color(1f, 0.85f, 0.35f, 0.95f);
            foreach (var b in bullets)
            {
                Vector3 p = CoordinateMap.ToUnity(b.Pos);
                Vector3 o = CoordinateMap.ToUnity(b.Origin);
                Vector3 v = CoordinateMap.ToUnity(b.Vel);
                // The streak never reaches back past the muzzle it left (owner: tracers began behind the aircraft).
                Vector3 tail = p - v * 0.025f;
                float flown = (p - o).magnitude;
                if ((p - tail).magnitude > flown) tail = o;
                GL.Color(c); GL.Vertex(tail); GL.Vertex(p);
            }
            // Drone markers: an orange diamond around each live drone (so they can be found), grey once dead.
            Camera cam = GetComponent<Camera>();
            if (cam != null)
            {
                foreach ((Vector3 pos, string kind, bool dead) in Combat.DroneMarkers())
                {
                    Vector3 sp = cam.WorldToScreenPoint(pos);
                    if (sp.z <= 0f) continue;
                    float r = Mathf.Clamp(2200f / Mathf.Max(20f, sp.z), 8f, 40f) * (Screen.height / 1000f);
                    Vector3 a = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y + r, sp.z)), bq = cam.ScreenToWorldPoint(new Vector3(sp.x + r, sp.y, sp.z));
                    Vector3 cc = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y - r, sp.z)), d = cam.ScreenToWorldPoint(new Vector3(sp.x - r, sp.y, sp.z));
                    GL.Color(dead ? new Color(0.5f, 0.5f, 0.5f, 0.6f) : new Color(1f, 0.55f, 0.1f, 0.9f));
                    GL.Vertex(a); GL.Vertex(bq); GL.Vertex(bq); GL.Vertex(cc); GL.Vertex(cc); GL.Vertex(d); GL.Vertex(d); GL.Vertex(a);
                }
            }
            GL.End();
            GL.PopMatrix();
        }
    }
}
