using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.Nav;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The fire pots, standing in the world. Owner, 2026-10-18: *"create a row of fire pots between two
    /// airports like the old mail pilots followed."*
    ///
    /// Before the radio there was a line of fires. The transcontinental airmail route was lit with flashing
    /// beacons and a pilot flew the mail at night by going from one to the next — the first system in the
    /// world that let you fly when you could not see the ground. For that to be a thing you can actually
    /// DO rather than a line on a chart, the lights have to exist out there, so each pot is a small emissive
    /// tower with a point light on top that flashes once every three seconds, slightly out of step with its
    /// neighbours so the chain shimmers rather than blinking as one.
    ///
    /// Where they are, and the three dark stretches at the escarpments, is <see cref="FirePots"/>'s
    /// decision and is measured off the terrain — a beacon has to stand on ground somebody could get a
    /// truck to.
    /// </summary>
    public sealed class FirePotLights : MonoBehaviour
    {
        /// <summary>How tall the little tower is, metres. Enough to clear the grass and be seen a way off.</summary>
        public const float TowerM = 9f;

        /// <summary>One flash every this many seconds.</summary>
        public const float PeriodS = 3f;

        private readonly List<Light> _lights = new();
        private readonly List<Material> _mats = new();
        private readonly List<float> _phase = new();

        private void Start()
        {
            var t = WorldTerrain.Active;
            if (t == null) { enabled = false; return; }

            var towerMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            towerMat.color = new Color(0.22f, 0.2f, 0.18f);

            int i = 0;
            foreach (FirePot p in FirePots.All(t))
            {
                var root = new GameObject($"FirePot{p.Number}");
                root.transform.SetParent(transform, false);
                root.transform.position = CoordinateMap.ToUnity(new FlyingGame.Core.MathTypes.Vec3(p.X, p.Y, -p.GroundM));

                // A skeleton tower: cheap, and reads as a mast rather than a floating light.
                var mast = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                mast.name = "mast";
                mast.transform.SetParent(root.transform, false);
                mast.transform.localScale = new Vector3(0.5f, TowerM * 0.5f, 0.5f);
                mast.transform.localPosition = new Vector3(0, TowerM * 0.5f, 0);
                Object.Destroy(mast.GetComponent<Collider>());      // nobody should hit a lamp post
                mast.GetComponent<MeshRenderer>().sharedMaterial = towerMat;

                // The lamp itself, emissive so it is visible before the light reaches you.
                var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lamp.name = "lamp";
                lamp.transform.SetParent(root.transform, false);
                lamp.transform.localScale = Vector3.one * 1.6f;
                lamp.transform.localPosition = new Vector3(0, TowerM, 0);
                Object.Destroy(lamp.GetComponent<Collider>());
                var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
                lamp.GetComponent<MeshRenderer>().material = mat;
                _mats.Add(mat);

                var go = new GameObject("beam");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(0, TowerM, 0);
                Light l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 450f;
                l.color = new Color(1f, 0.62f, 0.22f);
                l.intensity = 0f;
                _lights.Add(l);

                // Out of step with its neighbours, so the chain shimmers down the valley.
                _phase.Add(i * 0.37f % PeriodS);
                i++;
            }
        }

        private void Update()
        {
            for (int i = 0; i < _lights.Count; i++)
            {
                float t = (Time.time + _phase[i]) % PeriodS;
                // A short bright flash with a long dark gap, which is what a rotating beacon looks like.
                float f = Mathf.Clamp01(1f - t / 0.45f);
                f *= f;
                if (_lights[i] != null) _lights[i].intensity = 2.5f + 9f * f;
                if (_mats[i] != null) _mats[i].color = Color.Lerp(new Color(0.55f, 0.28f, 0.08f),
                                                                  new Color(1f, 0.86f, 0.55f), f);
            }
        }
    }
}
