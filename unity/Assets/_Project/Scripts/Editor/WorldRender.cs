using System.IO;
using FlyingGame.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>Headless aerial renders of the generated world (terrain steps, water, airports) to build/world/*.png.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.WorldRender.Render  (no -nographics)</summary>
    public static class WorldRender
    {
        [MenuItem("FlyingGame/Render World")]
        public static void Render()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/world"));
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(0.4f, 0.8f, -0.4f).normalized);
            FlyingGame.Core.WorldTerrain.Active = new FlyingGame.Core.WorldTerrain();
            WorldBuilder.BuildAll();

            var camGo = new GameObject("RenderCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.45f, 0.66f, 0.95f);
            cam.fieldOfView = 50f; cam.nearClipPlane = 1f; cam.farClipPlane = 60000f;
            var rt = new RenderTexture(1600, 1000, 24); cam.targetTexture = rt;

            (string name, Vector3 pos, Vector3 look)[] views =
            {
                ("overview", new Vector3(9000f, 9000f, -9000f), new Vector3(-3500f, 800f, 1500f)),
                ("valley-city", new Vector3(2400f, 750f, 200f), new Vector3(900f, 350f, -1500f)),
                ("valley-pad", new Vector3(1700f, 650f, -3300f), new Vector3(900f, 500f, -2850f)),
                ("topdown", new Vector3(-1500f, 17000f, 900f), new Vector3(-1500f, 0f, 910f)),
                ("topdown-valley", new Vector3(900f, 7500f, 600f), new Vector3(900f, 0f, 610f)),
                ("gorge-flight", new Vector3(-400f, 420f, 1650f), new Vector3(900f, 200f, 1700f)),
                ("hills", new Vector3(2900f, 500f, -1200f), new Vector3(2000f, 300f, -1700f)),
                ("valley-city-street", new Vector3(1900f, 280f, -1200f), new Vector3(1000f, 200f, -2200f)),
                ("valley-airport", new Vector3(1200f, 700f, -1600f), new Vector3(0f, 0f, 400f)),
                ("first-wall", new Vector3(2500f, 1500f, -2500f), new Vector3(-1800f, 450f, 1000f)),
                ("summit-airport", new Vector3(-7000f, 3500f, -1800f), new Vector3(-8900f, 2700f, 400f)),
                ("stol-strip", new Vector3(600f, 120f, -50f), new Vector3(420f, 0f, 250f)),
                ("river-lake", new Vector3(3500f, 1400f, -3200f), new Vector3(2400f, 850f, 1500f)),
                ("race-course", new Vector3(1950f, 520f, 250f), new Vector3(1950f, 40f, 1200f)),
                ("race-gate", new Vector3(1800f, 45f, 650f), new Vector3(1800f, 40f, 860f)),
                // The stretched course (2026-10-06): from the south, and a gate number up close (sim gate 1 at (45, 1800)).
                ("race-course2", new Vector3(1925f, 750f, -500f), new Vector3(1925f, 300f, 650f)),
                ("race-number", new Vector3(1850f, 400f, -20f), new Vector3(1800f, 396f, 45f)),
                ("race-number2", new Vector3(1760f, 395f, 100f), new Vector3(1800f, 396f, 45f)),
                ("aero-box", new Vector3(1000f, 700f, -900f), new Vector3(1000f, 300f, 400f)),
                ("bridge", new Vector3(-150f, 120f, 2200f), new Vector3(-150f, 0f, 1900f)),
                ("crop-field", new Vector3(2050f, 90f, -300f), new Vector3(1750f, 25f, 400f)),
                ("crop-wires", new Vector3(1450f, 25f, 100f), new Vector3(1750f, 30f, 191f)),
                ("grass-strip", new Vector3(-435f, 5f, -180f), new Vector3(-420f, 1f, 350f)),
                ("arch", new Vector3(-1250f, 120f, 1300f), new Vector3(-900f, 120f, 1740f)),
                ("eiffel", new Vector3(-600f, 120f, -2500f), new Vector3(-1000f, 120f, -1750f)),
                ("town", new Vector3(1160f, 400f, -3700f), new Vector3(1160f, 60f, -1750f)),
                ("town-street", new Vector3(1170f, 40f, -3000f), new Vector3(1170f, 60f, -1500f)),
                // Plunge waterfall at the first wall (sim x 1849, lip y −2002): front, from inside the slot, and the lip.
                ("waterfall-front", new Vector3(-1350f, 520f, 2350f), new Vector3(-1985f, 380f, 1849f)),
                ("waterfall-slot", new Vector3(-2030f, 350f, 1480f), new Vector3(-2030f, 330f, 2200f)),
                // The carrier, posed at t = 20 s (northbound, sim (−2415, 3700)): the approach from astern, its port side, from the shore.
                ("carrier-approach", new Vector3(3712f, 75f, -3150f), new Vector3(3695f, 18f, -2440f)),
                ("carrier-side", new Vector3(3330f, 70f, -2330f), new Vector3(3705f, 25f, -2415f)),
                ("carrier-deck", new Vector3(3712f, 40f, -2560f), new Vector3(3690f, 18f, -2350f)),
                ("carrier-shore", new Vector3(2700f, 450f, -3600f), new Vector3(3700f, 0f, -2415f)),
                ("waterfall-lip", new Vector3(-1900f, 880f, 1700f), new Vector3(-2010f, 780f, 1849f)),
                // The Valley lake's seaplane lane and base (owner 2026-10-07): Unity (x = sim y, z = sim x), lake surface 302.8 m.
                ("runway-south-final", new Vector3(0f, 420f, -3400f), new Vector3(0f, 305f, -600f)),
                ("seaplane-base", new Vector3(-980f, 345f, -2700f), new Vector3(-720f, 303f, -2600f)),
                ("seaplane-ramp", new Vector3(-685f, 309f, -2545f), new Vector3(-770f, 305f, -2600f)),
                ("seaplane-lane", new Vector3(-450f, 318f, -3120f), new Vector3(-450f, 302f, -2300f)),
                // The Reno high desert on the Mesa plateau (owner 2026-10-16). Unity x = sim y, z = sim x, so
                // the block (sim x -6900..-3600, y -7450..-4550) lies along Unity z at Unity x around -6000.
                ("desert-range", new Vector3(-6000f, 2200f, -1000f), new Vector3(-6000f, 1500f, -5000f)),
                ("desert-redrock", new Vector3(-4600f, 2050f, -4700f), new Vector3(-5280f, 1646f, -5600f)),
                ("desert-redrock-final", new Vector3(-5280f, 1760f, -4700f), new Vector3(-5280f, 1646f, -5600f)),
                ("desert-sandpass", new Vector3(-6000f, 1700f, -3700f), new Vector3(-6600f, 1480f, -4700f)),
                ("desert-hungryridge", new Vector3(-4810f, 1420f, -3350f), new Vector3(-4810f, 1340f, -4150f)),
                ("desert-playa", new Vector3(-7250f, 1420f, -4900f), new Vector3(-7250f, 1222f, -6100f)),
                ("desert-ruts", new Vector3(-7250f, 1232f, -6530f), new Vector3(-7250f, 1226f, -5900f)),
                ("desert-topdown", new Vector3(-6000f, 6000f, -5250f), new Vector3(-6000f, 1400f, -5240f)),
                // The desert's north edge, where its floor used to hang 610 m over the Bench and stop dead.
                ("desert-northedge", new Vector3(-3000f, 1500f, -4500f), new Vector3(-4700f, 1250f, -4500f)),
                // The mountain fronts (owner 2026-10-16: risers as mountain fronts, not flat shelves). The
                // fronts run along sim x, so in Unity they run along z and you look at them down Unity x.
                // First front: a summit to the west, the widest spur to the east, a saddle, and the bay the
                // Valley aerodrome sits in — where the front is still the old cliff.
                ("front-first-summit", new Vector3(1200f, 1150f, -4000f), new Vector3(-1500f, 800f, -4000f)),
                ("front-first-spur", new Vector3(1500f, 1250f, 6500f), new Vector3(-1400f, 800f, 6500f)),
                ("front-first-pass", new Vector3(-300f, 800f, -3000f), new Vector3(-2400f, 920f, -3000f)),
                ("front-first-bay", new Vector3(1100f, 1250f, 400f), new Vector3(-1400f, 800f, 400f)),
                // Along the first front from the south-east, high: spurs, bays and saddles all at once.
                ("front-first-oblique", new Vector3(2600f, 2700f, 2200f), new Vector3(-1300f, 800f, -4000f)),
                ("front-first-topdown", new Vector3(-1100f, 5200f, 1500f), new Vector3(-1150f, 500f, 1500f)),
                // Second front (Bench to Mesa, 3,000 to 5,000 ft) and third (Mesa to Summit, to 7,000 ft).
                ("front-second", new Vector3(-1600f, 2100f, 6000f), new Vector3(-5200f, 1250f, 6000f)),
                ("front-third", new Vector3(-6300f, 2600f, -4000f), new Vector3(-8100f, 2000f, -4000f)),
                ("front-third-east", new Vector3(-5000f, 3400f, 6500f), new Vector3(-7900f, 2300f, 6500f)),
                // The Alaska range on the Mesa plateau (owner 2026-10-16: "I didn't see any screenshots of
                // white snow covered mountains"). Nobody had ever rendered it: there were no views for it.
                // The High One is at sim (4200, −5900), 5,405 m — 17,700 ft — and the snow line is at 2,500 m.
                ("alaska-massif", new Vector3(-1600f, 4300f, 4200f), new Vector3(-5900f, 3000f, 4200f)),
                ("alaska-denali", new Vector3(-3400f, 3800f, 4300f), new Vector3(-5900f, 4300f, 4200f)),
                ("alaska-ridge", new Vector3(-2600f, 4800f, 900f), new Vector3(-6100f, 3400f, 4400f)),
                ("alaska-glacier", new Vector3(-4600f, 2050f, 5400f), new Vector3(-6900f, 2100f, 5050f)),
                ("alaska-lake", new Vector3(-4850f, 1620f, 5395f), new Vector3(-5700f, 1560f, 5350f)),
                ("alaska-braided", new Vector3(-1200f, 1700f, 5800f), new Vector3(-4100f, 950f, 5800f)),
                // The alpine range and its stair of lakes, on the Summit plateau. Cathedral is 3,357 m.
                ("alpine-range", new Vector3(-6200f, 5000f, 5900f), new Vector3(-10600f, 2700f, 5900f)),
                ("alpine-lakes", new Vector3(-7600f, 3000f, 3200f), new Vector3(-8900f, 2430f, 3000f)),
                ("alpine-outlet", new Vector3(-7400f, 3050f, 2200f), new Vector3(-8900f, 2440f, 3000f)),
                ("alpine-larch", new Vector3(-11900f, 3250f, 5500f), new Vector3(-13600f, 2500f, 6600f)),
                ("alpine-cirque", new Vector3(-10100f, 3400f, 4700f), new Vector3(-11300f, 2520f, 4500f)),
                // The Frank Church canyon and its strips, which had no views at all until the timber arrived
                // and made it worth looking at the approaches. (Sim (x, y, up) → Unity (y, up, x).)
                ("church-canyon", new Vector3(-12800f, 2000f, -5000f), new Vector3(-12600f, 1500f, 1000f)),
                ("church-johnson", new Vector3(-12891f, 2108f, -2769f), new Vector3(-12600f, 1958f, -1400f)),
                ("church-soldier", new Vector3(-13054f, 1948f, -1592f), new Vector3(-13900f, 1788f, -1900f)),
                ("church-indian", new Vector3(-13501f, 1576f, -1295f), new Vector3(-12830f, 1476f, -300f)),
                // The timber itself: the line across the third front, and a low run over the valley stands.
                ("forest-line", new Vector3(-7800f, 2600f, 1500f), new Vector3(-7800f, 2200f, -3000f)),
                ("forest-low", new Vector3(-6000f, 1660f, 0f), new Vector3(-5200f, 1555f, 2500f)),
            };
            // Moving scenery doesn't Update in an editor render: pose the carrier for its views.
            var carrier = Object.FindFirstObjectByType<CarrierRuntime>();
            if (carrier != null)
            {
                var (cx, cy, ch, _) = FlyingGame.Core.Carrier.PoseAt(20);
                carrier.transform.SetPositionAndRotation(WorldBuilder.U(cx, cy, 0), Quaternion.Euler(0f, (float)(ch * Mathf.Rad2Deg), 0f));
            }
            string onlyViews = System.Environment.GetEnvironmentVariable("WORLD_VIEWS");
            foreach ((string name, Vector3 pos, Vector3 look) in views)
            {
                if (!string.IsNullOrEmpty(onlyViews) && !onlyViews.Contains(name)) continue;
                cam.transform.position = pos; cam.transform.LookAt(look, Vector3.up);
                // The forest is streamed round the camera in the game, and nothing streams in a batch render,
                // so build the timber round whatever this view is LOOKING AT and throw it away afterwards.
                // (Unity x is the sim's y and Unity z is the sim's x.)
                var forest = new GameObject("ForestView");
                ForestBuilder.BuildAround(FlyingGame.Core.WorldTerrain.Active, forest.transform, look.z, look.x, 2200, 700);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(forest);
            }
            RenderTexture.active = null;
            Debug.Log($"WorldRender: wrote {views.Length} images to {outDir}");
        }
    }
}
