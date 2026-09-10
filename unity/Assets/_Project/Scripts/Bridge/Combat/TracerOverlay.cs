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
            if (bullets.Count == 0) return;
            _mat.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);
            var c = new Color(1f, 0.85f, 0.35f, 0.95f);
            foreach (var b in bullets)
            {
                Vector3 p = CoordinateMap.ToUnity(b.Pos);
                Vector3 v = CoordinateMap.ToUnity(b.Vel);
                Vector3 tail = p - v * 0.025f;
                GL.Color(c); GL.Vertex(tail); GL.Vertex(p);
            }
            GL.End();
            GL.PopMatrix();
        }
    }
}
