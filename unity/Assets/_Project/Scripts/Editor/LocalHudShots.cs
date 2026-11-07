using System;
using System.IO;
using FlyingGame.Bridge;
using UnityEditor;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    // IMGUI is rendered by an actual EditorWindow, using LocalPlayerHud.Draw verbatim.
    // Fixture readings deliberately differ between seats so swapped ownership is visible.
    public sealed class LocalHudShots : EditorWindow
    {
        private LocalPlayerHud _hud;
        private int _frame, _index;
        private static readonly Vector2Int[] Sizes = { new(860,390), new(1133,744), new(390,860) };
        private static readonly string[] Names = { "iphone-landscape", "ipad-landscape", "iphone-portrait" };
        public static void Run()
        {
            var w = CreateInstance<LocalHudShots>();
            w._hud = new GameObject("HUD review fixture").AddComponent<LocalPlayerHud>();
            w.ShowPopup();
            w.SetSize();
            EditorApplication.update += w.Tick;
        }
        private void SetSize()
        {
            var size = Sizes[_index];
            minSize = maxSize = size;
            position = new Rect(50, 80, size.x, size.y);
            _frame = 0;
            Focus();
        }
        private void Tick()
        {
            Repaint();
            if (++_frame < 25) return;
            var size = Sizes[_index];
            // Grab this GUIView's render target directly. Screen reads include desktop chrome and
            // Retina offsets, and truncate a review window larger than the physical desktop.
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;
            object host = typeof(EditorWindow).GetField("m_Parent", flags).GetValue(this);
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView");
            int pw = Mathf.RoundToInt(size.x * EditorGUIUtility.pixelsPerPoint);
            int ph = Mathf.RoundToInt(size.y * EditorGUIUtility.pixelsPerPoint);
            var rt = new RenderTexture(pw, ph, 0, RenderTextureFormat.ARGB32);
            rt.Create();
            type.GetMethod("GrabPixels", flags).Invoke(host, new object[] { rt, new Rect(0,0,pw,ph) });
            RenderTexture.active = rt;
            var tex = new Texture2D(pw, ph, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0,0,pw,ph),0,0); tex.Apply();
            var original = tex.GetPixels();
            var flipped = new Color[original.Length];
            for (int y=0; y<ph; y++) Array.Copy(original,y*pw,flipped,(ph-1-y)*pw,pw);
            tex.SetPixels(flipped); tex.Apply();
            RenderTexture.active = null;
            rt.Release(); DestroyImmediate(rt);
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath,"../../renders/local-mp-hud"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, Names[_index]+".png"),tex.EncodeToPNG());
            DestroyImmediate(tex);
            Debug.Log("[LocalHudShots] captured " + Names[_index]);
            if (++_index < Sizes.Length) { SetSize(); return; }
            EditorApplication.update -= Tick;
            DestroyImmediate(_hud.gameObject);
            Close();
            EditorApplication.Exit(0);
        }
        private void OnGUI()
        {
            if (_hud == null) return;
            var size = Sizes[_index];
            GUI.color = new Color(.24f,.43f,.57f);
            GUI.DrawTexture(new Rect(0,0,size.x,size.y),Texture2D.whiteTexture);
            GUI.color = Color.black;
            if (size.x >= size.y) GUI.DrawTexture(new Rect(size.x*.498f,0,size.x*.004f,size.y),Texture2D.whiteTexture);
            else GUI.DrawTexture(new Rect(0,size.y*.498f,size.x,size.y*.004f),Texture2D.whiteTexture);
            GUI.color = Color.white;
            // Insets are representative points, explicitly supplied: batch Screen.safeArea is not a phone.
            Rect safe = _index == 0 ? new Rect(44,0,772,369) :
                _index == 1 ? new Rect(0,0,1133,724) : new Rect(0,47,390,779);
            for (int i=0;i<2;i++)
            {
                var v = FlyingGame.Core.LocalMp.SplitLayout.Viewport(i,2,size.x,size.y);
                Rect viewport = new Rect((float)v.X*size.x,(1f-(float)v.Y-(float)v.H)*size.y,
                    (float)v.W*size.x,(float)v.H*size.y);
                Rect r = LocalPlayerHud.SafeRect(viewport,safe);
                if (r.width <= 0 || r.height <= 0) throw new Exception("Empty safe viewport");
                _hud.Draw(r,i,true,i==0?112:98,i==0?2450:2380,i==0?274:269,i==0?1.2:0.9,
                    viewport.center-r.position,i==0,true);
            }
        }
    }
}
