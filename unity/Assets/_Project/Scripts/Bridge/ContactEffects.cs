using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// PLACEHOLDER scrape sound and dust (Ty 2026-11-03: "a contact patch that scrapes the surface with sound and dust").
    /// Every patch the sim says is sliding (<see cref="Aircraft.Scrapes"/>) gets a looping filtered-noise voice and a dust
    /// emitter at its contact point, both scaled by the patch's friction power μ·N·v (100 W silent … 1 MW full). The real
    /// visuals and audio are Codex's (AGENT-LOG NEEDS-Codex); the hooks they need are <see cref="Aircraft.Scrapes"/> per
    /// frame and <see cref="Aircraft.Contact"/> for the start/stop/bent/broken/prop-strike events.
    /// Installed on StructuralDamage's object (StructuralDamage.Repair).
    /// </summary>
    public sealed class ContactEffects : MonoBehaviour
    {
        private const int Voices = 4;
        private FlightSimDriver _driver;
        private readonly AudioSource[] _audio = new AudioSource[Voices];
        private readonly ParticleSystem[] _dust = new ParticleSystem[Voices];
        private readonly string[] _owner = new string[Voices];
        private AudioClip _noise;
        private Material _dustMat;
        private bool _built;

        private void Awake() { _driver = GetComponent<FlightSimDriver>(); }

        private void OnDestroy()
        {
            for (int i = 0; i < Voices; i++) { if (_audio[i] != null) Destroy(_audio[i].gameObject); if (_dust[i] != null) Destroy(_dust[i].gameObject); }
        }

        private void Build()
        {
            _built = true;
            // One second of brown-ish noise (a leaky integral of white noise), looped: the scrape's grit.
            const int rate = 22050;
            var data = new float[rate];
            var rng = new System.Random(20261103);
            float y = 0f;
            for (int i = 0; i < data.Length; i++) { y = 0.97f * y + 0.25f * (float)(rng.NextDouble() * 2 - 1); data[i] = Mathf.Clamp(y, -1f, 1f); }
            _noise = AudioClip.Create("ScrapeNoise", rate, 1, rate, false);
            _noise.SetData(data, 0);
            Shader sh = Shader.Find("FlyingGame/UnlitTransparent");
            _dustMat = sh != null ? new Material(sh) { color = new Color(0.55f, 0.47f, 0.36f, 0.45f) } : null;
            for (int i = 0; i < Voices; i++)
            {
                var a = new GameObject("ScrapeVoice" + i).AddComponent<AudioSource>();
                a.clip = _noise; a.loop = true; a.playOnAwake = false; a.spatialBlend = 1f; a.minDistance = 3f; a.maxDistance = 300f; a.volume = 0f;
                _audio[i] = a;
                var go = new GameObject("ScrapeDust" + i);
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.loop = true; main.playOnAwake = false; main.maxParticles = 400;
                main.startLifetime = 1.6f; main.startSpeed = 1.5f; main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 1.1f);
                main.gravityModifier = -0.02f;
                var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = 0.3f;
                var em = ps.emission; em.rateOverTime = 0f;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (_dustMat != null) r.sharedMaterial = _dustMat;
                ps.Play();
                _dust[i] = ps;
            }
        }

        private static float Level(double powerW) => Mathf.Clamp01((float)((System.Math.Log10(System.Math.Max(1.0, powerW)) - 2.0) / 4.0));

        /// <summary>Grit pitch and dust colour by what is being scraped.</summary>
        private static (float pitch, Color dust) Look(int surface) => surface switch
        {
            (int)WorldTerrain.Surface.Paved => (1.15f, new Color(0.62f, 0.60f, 0.58f, 0.35f)),
            (int)WorldTerrain.Surface.Gravel or (int)WorldTerrain.Surface.Dirt or (int)WorldTerrain.Surface.Sand => (0.9f, new Color(0.58f, 0.53f, 0.46f, 0.45f)),
            (int)WorldTerrain.Surface.Grass => (0.65f, new Color(0.40f, 0.42f, 0.25f, 0.40f)),
            (int)WorldTerrain.Surface.Snow or (int)WorldTerrain.Surface.PackedGlacierSnow => (0.5f, new Color(0.95f, 0.97f, 1f, 0.55f)),
            FlyingGame.Core.ContactPoint.SurfaceSolid => (1.35f, new Color(0.6f, 0.6f, 0.6f, 0.3f)),
            _ => (0.8f, new Color(0.50f, 0.42f, 0.30f, 0.5f)),
        };

        private void LateUpdate()
        {
            Aircraft ac = _driver != null && _driver.Sim != null ? _driver.Sim.Aircraft : null;
            IReadOnlyList<ScrapeState> scrapes = ac != null ? ac.Scrapes : null;
            if ((scrapes == null || scrapes.Count == 0) && !_built) return;
            if (!_built) Build();
            // Keep each voice on the patch it already had; give free voices to the loudest newcomers.
            for (int i = 0; i < Voices; i++)
                if (_owner[i] != null && (scrapes == null || !Contains(scrapes, _owner[i]))) _owner[i] = null;
            if (scrapes != null)
                foreach (ScrapeState s in scrapes)
                {
                    if (System.Array.IndexOf(_owner, s.Part) >= 0) continue;
                    int free = System.Array.IndexOf(_owner, null);
                    if (free < 0) break;
                    _owner[free] = s.Part;
                }
            for (int i = 0; i < Voices; i++)
            {
                ScrapeState s = _owner[i] != null ? Find(scrapes, _owner[i]) : null;
                var em = _dust[i].emission;
                if (s == null)
                {
                    if (_audio[i].isPlaying) _audio[i].Stop();
                    em.rateOverTime = 0f;
                    continue;
                }
                // The patch's world point, put next to the aeroplane the way the sim has it.
                Vector3 at = transform.position + CoordinateMap.ToUnity(s.PositionWorld - ac.State.Position);
                float level = Level(s.PowerW);
                (float pitch, Color dust) = Look(s.SurfaceCode);
                _audio[i].transform.position = at;
                _audio[i].volume = level * FlightAudio.MasterVolume;
                _audio[i].pitch = pitch * (0.85f + 0.3f * Mathf.Clamp01((float)s.SlipMs / 40f));
                if (!_audio[i].isPlaying) _audio[i].Play();
                _dust[i].transform.position = at;
                var main = _dust[i].main; main.startColor = dust;
                em.rateOverTime = 250f * level;
            }
        }

        private static bool Contains(IReadOnlyList<ScrapeState> list, string part) => Find(list, part) != null;
        private static ScrapeState Find(IReadOnlyList<ScrapeState> list, string part)
        {
            if (list == null) return null;
            foreach (ScrapeState s in list) if (s.Part == part) return s;
            return null;
        }
    }
}
