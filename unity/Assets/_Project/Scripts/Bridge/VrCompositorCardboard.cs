using FlyingGame.Core.Vr;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Inverse lens mesh, with independent radial UV scales for red and blue.</summary>
    public sealed class VrCompositorCardboard : MonoBehaviour, IVrCompositor
    {
        private VrLensConfig _lens;
        private RenderTexture _left, _right;
        private Material _material;
        private Mesh _leftMesh, _rightMesh;
        private Vector2 _physicalOverride, _bakedSize;
        private VrWorldHud _hud;

        public void Configure(VrLensConfig lens, RenderTexture left, RenderTexture right)
        {
            _lens = lens; _left = left; _right = right;
            ClearMeshes();
            // Not `??`: in the editor a missing component comes back as Unity's fake null, which `??` keeps.
            if (_hud == null) _hud = gameObject.GetComponent<VrWorldHud>();
            if (_hud == null) _hud = gameObject.AddComponent<VrWorldHud>();
            _hud.Refresh();
        }

        /// <summary>Edit-mode device geometry and HUD refresh, before either eye is rendered.</summary>
        public void PrepareForTesting(float widthM, float heightM)
        {
            _physicalOverride = new Vector2(widthM, heightM);
            _hud.Refresh();
        }

        public void Composite(RenderTexture destination)
        {
            if (_left == null || _right == null || _lens == null) return;
            int w = destination != null ? destination.width : Screen.width;
            int h = destination != null ? destination.height : Screen.height;
            float dpi = Screen.dpi > 1 ? Screen.dpi : 326;
            Vector2 size = _physicalOverride != Vector2.zero ? _physicalOverride : new Vector2(w / dpi * .0254f, h / dpi * .0254f);
            if (_material == null)
            {
                Shader shader = Resources.Load<Shader>("VrCardboard");
                if (shader == null) { Debug.LogError("[VR] missing Resources/VrCardboard shader"); return; }
                _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (_leftMesh == null || size != _bakedSize)
            {
                ClearMeshes(); _bakedSize = size;
                _leftMesh = Bake(false, size); _rightMesh = Bake(true, size);
            }
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = destination;
            GL.PushMatrix();
            try
            {
                GL.LoadOrtho();
                Draw(_leftMesh, _left); Draw(_rightMesh, _right);
            }
            finally { GL.PopMatrix(); RenderTexture.active = previous; }
        }

        private void Draw(Mesh mesh, RenderTexture eye)
        {
            _material.mainTexture = eye;
            _material.SetPass(0);
            Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
        }

        private Mesh Bake(bool right, Vector2 size)
        {
            const int n = 64;
            var vertices = new Vector3[(n + 1) * (n + 1)];
            var red = new Vector2[vertices.Length]; var green = new Vector2[vertices.Length]; var blue = new Vector2[vertices.Length];
            var triangles = new int[n * n * 6];
            for (int y = 0; y <= n; y++) for (int x = 0; x <= n; x++)
            {
                int i = y * (n + 1) + x;
                double u = (double)x / n, v = (double)y / n;
                vertices[i] = new Vector3((float)(u * .5 + (right ? .5 : 0)), (float)v, 0);
                Vector2 Sample(double scale)
                {
                    var uv = VrDistortion.Sample(_lens, right, size.x, size.y, u, v, scale);
                    return new Vector2((float)uv.U, (float)uv.V);
                }
                green[i] = Sample(1);
                red[i] = Sample(_lens.ChromaticAberration ? _lens.CaRedScale : 1);
                blue[i] = Sample(_lens.ChromaticAberration ? _lens.CaBlueScale : 1);
                if (x == n || y == n) continue;
                int k = (y * n + x) * 6;
                triangles[k] = i; triangles[k+1] = i+1; triangles[k+2] = i+n+1;
                triangles[k+3] = i+1; triangles[k+4] = i+n+2; triangles[k+5] = i+n+1;
            }
            var mesh = new Mesh { name = "VR inverse lens", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices; mesh.uv = red; mesh.uv2 = green; mesh.uv3 = blue; mesh.triangles = triangles;
            return mesh;
        }

        private void OnGUI()
        {
            if (!VrMode.On || Event.current.type != EventType.Repaint) return;
            GUI.depth = 1000;
            Composite(null);
        }
        private void ClearMeshes() { Gone(_leftMesh); Gone(_rightMesh); _leftMesh = _rightMesh = null; }
        private void OnDestroy() { ClearMeshes(); Gone(_material); if (_hud != null) Gone(_hud); }
        private static void Gone(Object value) { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    }
}
