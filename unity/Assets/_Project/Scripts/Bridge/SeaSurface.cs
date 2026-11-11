using System;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Hands <see cref="FlyingGame.Core.SeaState"/>'s answers to Water.shader, and nothing else. The physics is in
    /// Core where it can be tested headless (SeaStateTests pins it against JONSWAP, Pierson-Moskowitz, Cox and Munk
    /// and the Beaufort sea criteria); this file is the plumbing, and it contains no constants of its own.
    ///
    /// WHAT IT REPLACES. Water.shader used to read a single BINARY <c>_WaterCalm</c>, set from a <c>calm</c> flag
    /// passed to SunsetLighting.Apply. It had nothing to do with the wind the aeroplane was flying in: the lake
    /// looked identical in a dead calm and in 25 knots, and the only way to change it was to call a lighting
    /// function with a different flag. Ty, 2026-11-11: "water texture should change with wind. No wind glassy wind
    /// [makes] texture first then waves stack up with distance etc use real physics". <c>_WaterCalm</c> survives,
    /// but as a CONTINUOUS number (SeaState.Glassy01) rather than a switch, so the places that read it for a blur
    /// radius or a glint still work and now vary with the real wind.
    ///
    /// THE FETCH FIELD. "Waves stack up with distance" means the sea state has to vary ACROSS the water, not just
    /// be one number for the lake — the water by the upwind shore is smoother than the water a kilometre downwind
    /// of it. A per-pixel shoreline query is out of the question, so the shader is handed the upwind shore as a
    /// PLANE: the distance along the wind at which the water starts. Each fragment's fetch is then
    /// <c>dot(worldXZ, windDirection) - origin</c>, clamped at zero and at the longest water in the world. For a
    /// lake that is a straight shoreline standing in for a curved one, which is wrong by the lake's curvature at
    /// the edges and exactly right down the middle where the aeroplane is; the alternative is a shoreline distance
    /// field, and that is a lot of memory for a gradient Ty will read as "rougher that way".
    /// </summary>
    public static class SeaSurface
    {
        /// <summary>The longest fetch any water in this world can offer: the harbor is a 3.2 km x 0.9 km channel,
        /// and the open sea east of the coast is bounded by the world. Marching further than this costs samples and
        /// can only find more water that is not there.</summary>
        public const double MaxFetchM = 4000.0;

        /// <summary>The upwind march's step, m. 25 m over 4 km is 160 height-field queries once a frame.</summary>
        public const double FetchStepM = 25.0;

        /// <summary>The standard reference height for both wave-growth laws, m above the surface. JONSWAP is written
        /// for U10; sampling the wind down at the water would read the boundary layer the game models for the
        /// aeroplane, not the wind the waves were raised by.</summary>
        public const double WindReferenceHeightM = 10.0;

        private static readonly int WindId = Shader.PropertyToID("_SeaWind");
        private static readonly int GrowthId = Shader.PropertyToID("_SeaGrowth");
        private static readonly int FetchId = Shader.PropertyToID("_SeaFetch");
        private static readonly int CalmId = Shader.PropertyToID("_WaterCalm");

        /// <summary>The last values pushed, for fixtures and diagnostics: wind (m/s), fetch at the reference point
        /// (m), significant height there (m) and peak wavelength there (m).</summary>
        public static (double WindMs, double FetchM, double HeightM, double WavelengthM) Last { get; private set; }

        /// <summary>
        /// Push a sea state built from a wind and an upwind shore. <paramref name="windFromRad"/> is the direction
        /// the wind blows FROM in the sim frame (0 = from the north, +x). <paramref name="shoreSim"/> is a point on
        /// the upwind shoreline, <paramref name="referenceSim"/> the point the diagnostics are reported at.
        /// </summary>
        public static void Apply(double windMs, double windFromRad, Vec3 shoreSim, Vec3 referenceSim)
        {
            // Downwind, in Unity's XZ. Sim x is north, y is east; CoordinateMap puts sim (x, y) at Unity (z, x).
            double downN = -Math.Cos(windFromRad), downE = -Math.Sin(windFromRad);
            var downwind = new Vector2((float)downE, (float)downN);
            if (downwind.sqrMagnitude < 1e-6f) downwind = new Vector2(0, 1);
            downwind.Normalize();

            double ripple = SeaState.Ripple01(windMs);
            double mss = SeaState.MeanSquareSlope(windMs);
            // Hs = kH·√fetch and λ = kL·fetch^0.66, which is JONSWAP with the wind factored out so the shader
            // evaluates one sqrt and one pow per fragment instead of the whole law. The capillary ramp is folded
            // into kH, so below Beaufort 1 the shader's waves are zero without it having to know why.
            double kH = SeaState.JonswapHeightCoeff * windMs / Math.Sqrt(SeaState.G) * ripple;
            // λ = g·T²/2π with T = (U/g)/3.5 · (gX/U²)^0.33, collected into kL·X^0.66.
            double kL = windMs > 0
                ? windMs * windMs / (SeaState.G * SeaState.JonswapPeakCoeff * SeaState.JonswapPeakCoeff * 2.0 * Math.PI)
                  * Math.Pow(SeaState.G / (windMs * windMs), 2.0 * SeaState.JonswapPeakExponent)
                : 0.0;
            double hsMax = SeaState.FullyDevelopedHeightM(windMs) * ripple;
            double lambdaMax = windMs > 0
                ? SeaState.G / (2.0 * Math.PI) * (windMs / (SeaState.FullyDevelopedPeak * SeaState.G))
                  * (windMs / (SeaState.FullyDevelopedPeak * SeaState.G))
                : 1.0;

            // Where the water starts, measured along the wind: the shader's fetch is this subtracted from the
            // fragment's own projection onto the wind direction.
            Vector3 shore = CoordinateMap.ToUnity(shoreSim);
            float origin = shore.x * downwind.x + shore.z * downwind.y;

            Shader.SetGlobalVector(WindId, new Vector4(downwind.x, downwind.y, (float)windMs, (float)ripple));
            Shader.SetGlobalVector(GrowthId, new Vector4((float)kH, (float)kL, (float)hsMax, (float)Math.Max(0.05, lambdaMax)));
            Shader.SetGlobalVector(FetchId, new Vector4(origin, (float)MaxFetchM, (float)mss, 0f));
            Shader.SetGlobalFloat(CalmId, (float)SeaState.Glassy01(windMs));

            Vector3 reference = CoordinateMap.ToUnity(referenceSim);
            double fetch = Math.Clamp(reference.x * downwind.x + reference.z * downwind.y - origin, 0, MaxFetchM);
            Last = (windMs, fetch, SeaState.SignificantHeightM(windMs, fetch), SeaState.PeakWavelengthM(windMs, fetch));
        }

        /// <summary>
        /// The whole thing for a point on the water: read the wind there, march upwind to the shore, push the
        /// result. <paramref name="simPos"/> is in the sim frame. Returns false (and pushes a dead calm) if there
        /// is no water there, so the caller does not have to decide.
        /// </summary>
        public static bool ApplyAt(Vec3 simPos)
        {
            double? surface = FloatHydro.WaterSurfaceAt(simPos.X, simPos.Y);
            if (!surface.HasValue) { Calm(); return false; }

            // The wind 10 m over the water, not at the aeroplane: a wave at the far end of the lake was raised by
            // the wind over the lake, and an aeroplane 2,000 ft up is in different air.
            var at = new Vec3(simPos.X, simPos.Y, -(surface.Value + WindReferenceHeightM));
            Vec3 wind = Atmosphere.MeanWindAtPosition(at);
            double speed = Math.Sqrt(wind.X * wind.X + wind.Y * wind.Y);
            if (!(speed > 0.01)) { Calm(); return true; }
            // The wind VECTOR points where the air is going; the direction it blows FROM is the other way.
            double fromRad = Math.Atan2(-wind.Y, -wind.X);

            double level = surface.Value;
            bool IsWater(double x, double y)
            {
                double? h = FloatHydro.WaterSurfaceAt(x, y);
                // Still the same body of water: a lake 60 m higher up the hillside is not this lake's fetch.
                return h.HasValue && Math.Abs(h.Value - level) < 1.0;
            }
            double fetch = SeaState.UpwindFetchM(IsWater, simPos.X, simPos.Y, fromRad, MaxFetchM, FetchStepM);
            var shore = new Vec3(simPos.X + Math.Cos(fromRad) * fetch, simPos.Y + Math.Sin(fromRad) * fetch, -level);
            Apply(speed, fromRad, shore, new Vec3(simPos.X, simPos.Y, -level));
            return true;
        }

        /// <summary>A dead calm: a mirror, no waves, no fetch gradient. What the shader gets when there is no water
        /// under the aeroplane, and what a fixture asking for "calm" means.</summary>
        public static void Calm() => Apply(0, 0, Vec3.Zero, Vec3.Zero);
    }

    /// <summary>
    /// Keeps the water's sea state in step with the wind the aeroplane is actually flying in, once a frame. Put on
    /// the same object as the aircraft (SceneBootstrap does it beside GroundShadow).
    ///
    /// The fetch march is the only real cost — <see cref="SeaSurface.MaxFetchM"/>/<see cref="SeaSurface.FetchStepM"/>
    /// = 160 water-surface queries — so it is done at <see cref="IntervalSec"/> rather than every frame: a shoreline
    /// 4 km upwind does not move, and at 100 kt the aeroplane covers 10 m between updates, which is under half a
    /// march step.
    /// </summary>
    public sealed class SeaSurfaceDriver : MonoBehaviour
    {
        public float IntervalSec = 0.2f;
        private float _next;

        private void LateUpdate()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Mathf.Max(0.02f, IntervalSec);
            SeaSurface.ApplyAt(CoordinateMap.ToSim(transform.position));
        }
    }
}
