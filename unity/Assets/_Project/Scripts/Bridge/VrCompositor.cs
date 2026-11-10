using FlyingGame.Core.Vr;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE SEAM BETWEEN THE RIG AND THE LENS WORK. <see cref="VrRig"/> renders two undistorted eye textures and
    /// hands them here; whatever this puts on the screen is what the pilot sees through the headset.
    ///
    /// Codex owns the implementation: barrel-distortion correction (the inverse of
    /// <see cref="VrOptics.Distort"/>, whose coefficients arrive on the <see cref="VrLensConfig"/>), the
    /// chromatic-aberration pass, and the VR HUD. Nothing in the rig needs to change to swap this out — put a
    /// component implementing this interface on the main camera BEFORE VR is switched on and the rig uses it
    /// instead of the plain one.
    /// </summary>
    public interface IVrCompositor
    {
        /// <summary>Called once when the rig builds its eyes, and again if the profile or the screen changes.
        /// The textures stay the same objects for the life of the session.</summary>
        void Configure(VrLensConfig lens, RenderTexture left, RenderTexture right);

        /// <summary>Draw the finished frame into <paramref name="destination"/> (null = the backbuffer). The
        /// rig calls this once per frame after both eyes have rendered.</summary>
        void Composite(RenderTexture destination);
    }

    /// <summary>
    /// The plain one: the two eyes side by side, undistorted, left on the left. It exists so the rig works end
    /// to end on this branch — through a headset it will look pincushioned, because nothing has undone the
    /// lens yet. That is Codex's pass.
    ///
    /// The layout it writes is the contract: LEFT eye into the left half of the destination, RIGHT into the
    /// right half, both full height, no gap, no overscan.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class VrCompositorSideBySide : MonoBehaviour, IVrCompositor
    {
        private RenderTexture _left, _right;
        private VrLensConfig _lens;

        public void Configure(VrLensConfig lens, RenderTexture left, RenderTexture right)
        {
            _lens = lens; _left = left; _right = right;
        }

        public void Composite(RenderTexture destination)
        {
            if (_left == null || _right == null) return;
            // The destination's own size, not Screen's: an editor tool renders a device's resolution into a
            // RenderTexture while the batch window is 640x480, and sizing from Screen there puts the seam in
            // the wrong place. Screen is only the fallback for the backbuffer, which has no size of its own.
            int w = destination != null ? destination.width : Screen.width;
            int h = destination != null ? destination.height : Screen.height;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = destination;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, w, h, 0);
            int half = w / 2;
            Graphics.DrawTexture(new Rect(0, 0, half, h), _left);
            Graphics.DrawTexture(new Rect(half, 0, half, h), _right);
            GL.PopMatrix();
            RenderTexture.active = previous;
        }

        /// <summary>The rig disables the main camera, so the composite is driven from here: this component sits
        /// on the main camera and paints the backbuffer after everything else has drawn.</summary>
        private void OnGUI()
        {
            if (!VrMode.On || Event.current.type != EventType.Repaint) return;
            // Furthest back of anything on glass: in Unity's GUI a HIGHER depth draws first. The eyes are the
            // world, so everything still allowed to draw in VR — the G veil, the menus — goes over the top.
            GUI.depth = 1000;
            Composite(null);
        }
    }
}
