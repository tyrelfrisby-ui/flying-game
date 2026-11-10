using System.Runtime.CompilerServices;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// WHICH AEROPLANE IS THE FLIGHT MODEL FLYING, AND IS IT THE ONE ON SCREEN.
    ///
    /// Ty, 2026-11-09, across three messages as he worked it out, after switching from the Cub to the Pitts:
    ///   "there is a ton of roll with yaw (dihedral effect) on the pitts in this last build it is significantly
    ///    different and not stick free this is with both sticks held"
    ///   "weird that behaviour is now gone and it is back to normal… some sort of transient bug… it was flying way
    ///    different from what it should have almost like it was the extra flying but the pitts livery"
    ///   "i switched from the cub to the pitts so maybe the flight model was stuck on cub and the pitts livery was
    ///    present"
    /// His reading fits the symptom exactly: a J-3 has a high wing with real dihedral and a strong roll due to
    /// sideslip; a Pitts has very little. "A ton of roll with yaw" is what a Cub flight model feels like wearing a
    /// Pitts livery, and it is not what either aeroplane does on its own.
    ///
    /// Offered the choice between hunting the race and shipping a diagnostic he chose the diagnostic: "make the build
    /// log or display which config the flight model is actually using, so the next time it happens we know for certain
    /// rather than inferring". So this does not try to fix anything. It makes the next occurrence unambiguous.
    ///
    /// WHAT IT READS, AND WHY IT CANNOT BE FOOLED BY THE BUG IT IS LOOKING FOR
    ///
    /// The flight model is not an object you can ask for its name: AeroModel is entirely static, so THE CONFIG OBJECT
    /// IS THE FLIGHT MODEL. Everything the physics does each step comes out of <see cref="Aircraft.Config"/> and the
    /// per-config geometry tables keyed on that object. So the identity of the aeroplane being flown is the identity of
    /// that object, and this reads it from <see cref="FlightSimDriver.Sim"/>.Aircraft.Config — the instance the step is
    /// actually handed — and never from <see cref="FlightSimDriver.AircraftId"/>, <see cref="SessionSettings.AircraftId"/>
    /// or anything else that only records what was ASKED for. Those are shown too, beside it, precisely because they
    /// are the things that may be lying.
    ///
    /// The drawn aeroplane is read from <see cref="AirframeVisual.DrawnFromConfig"/>, recorded inside Rebuild() at the
    /// one moment the picture and the physics are known to agree, plus <see cref="AirframeVisual.DrawnModelPath"/>,
    /// the GLB actually instantiated. The two sides are compared BY OBJECT REFERENCE, not by id: two aeroplanes of the
    /// same type carry the same string and different objects, so a stale picture or a stale flight model that a string
    /// comparison would call healthy shows here as two different tags.
    ///
    /// THE MECHANISM CHECKS, not just the symptom. Three things would produce Ty's report and none of them is visible
    /// in an id:
    ///  • A REUSED CONFIG OBJECT. TailWake, PropJet, BiplaneCellule and six tables in AeroModel key per-config geometry
    ///    on a ConditionalWeakTable over the AircraftConfig object, assuming one object is one aeroplane for life.
    ///    Reuse one and those tables serve the FIRST aeroplane's geometry under the second's name — exactly "the extra
    ///    flying but the pitts livery". So the wing span is read TWICE: once through AeroModel.WingSpan (the cache) and
    ///    once through AeroModel.WingSpanUncached (the strips as authored). They agree on every healthy aeroplane.
    ///  • THE PUBLIC STATICS outliving the probe that set them: <see cref="Aircraft.SwirlFactor"/> (0.2),
    ///    <see cref="PropJet.AttachedBias"/> (0.5) and <see cref="PropJet.SpreadOverride"/> (-1, off). Nothing in the
    ///    Bridge writes any of them — only the headless probes under tools/FlightTests do — so in a phone build they
    ///    should be at their shipped defaults, and a reading that says so rules them out in one glance instead of a
    ///    session. A future writer that forgets to put one back shows up here the moment it happens.
    ///  • THE MASS AND INERTIA the integrator is using (<see cref="Aircraft.MassProperties"/>), which is per-instance
    ///    and not derived from the id at all. A Cub flying under a Pitts livery reads the Cub's 550 kg and the Cub's
    ///    roll inertia whatever any string says.
    ///
    /// Rejected: measuring the dihedral effect itself (the actual symptom) by perturbing beta and differencing the
    /// roll moment. It would be the most direct answer and it is the wrong tool — it costs a full aero evaluation per
    /// sample, on a build that is already the subject of a separate performance investigation, to tell us something
    /// the span and the inertia already tell us for free.
    ///
    /// Also rejected: keying anything on <see cref="SessionSettings.AircraftId"/>. The driver picks that up only at
    /// ApplySession, so it legitimately leads the driver between choosing an aeroplane on the landing page and flying
    /// it. It is reported as information and never counted as a disagreement.
    /// </summary>
    public readonly struct ModelIdentityReading
    {
        /// <summary>There is an aeroplane to report on (a sim with an Aircraft). False before the first spawn.</summary>
        public readonly bool Valid;

        /// <summary>The config the PHYSICS is stepping: its id, its display name, and the object's identity hash.</summary>
        public readonly string FlyingId, FlyingName;
        public readonly int FlyingTag;

        /// <summary>The config the PICTURE was lofted from at the last Rebuild, and that object's identity hash
        /// (0 when nothing has been built yet). <see cref="DrawnModelPath"/> is the GLB instantiated, null for the
        /// procedural shell.</summary>
        public readonly string DrawnId, DrawnTypeId, DrawnModelPath, DrawnLivery;
        public readonly int DrawnTag;

        /// <summary>What was ASKED for: the driver's type id, and the landing page's. Information, not a verdict.</summary>
        public readonly string DriverId, SessionId;

        /// <summary>Wing span through the per-config cache, and measured fresh from the strips. Equal when healthy.</summary>
        public readonly double SpanCachedM, SpanAuthoredM;

        /// <summary>What the integrator is actually using this step.</summary>
        public readonly double MassKg, Ixx, Iyy, Izz;

        /// <summary>The three public statics that would survive an aircraft change (shipped: 0.2, 0.5, -1).</summary>
        public readonly double Swirl, AttachedBias, SpreadOverride;

        public const double SwirlDefault = 0.2, AttachedBiasDefault = 0.5, SpreadOverrideDefault = -1.0;

        public ModelIdentityReading(bool valid, string flyingId, string flyingName, int flyingTag,
                                    string drawnId, int drawnTag, string drawnTypeId, string drawnModelPath, string drawnLivery,
                                    string driverId, string sessionId,
                                    double spanCachedM, double spanAuthoredM,
                                    double massKg, double ixx, double iyy, double izz,
                                    double swirl, double attachedBias, double spreadOverride)
        {
            Valid = valid; FlyingId = flyingId; FlyingName = flyingName; FlyingTag = flyingTag;
            DrawnId = drawnId; DrawnTag = drawnTag; DrawnTypeId = drawnTypeId; DrawnModelPath = drawnModelPath; DrawnLivery = drawnLivery;
            DriverId = driverId; SessionId = sessionId;
            SpanCachedM = spanCachedM; SpanAuthoredM = spanAuthoredM;
            MassKg = massKg; Ixx = ixx; Iyy = iyy; Izz = izz;
            Swirl = swirl; AttachedBias = attachedBias; SpreadOverride = spreadOverride;
        }

        /// <summary>THE BUG: the aeroplane drawn and the aeroplane flown are different config objects. Compared by
        /// object and not by id, so two Pittses are still a mismatch.</summary>
        public bool PictureDisagrees => Valid && DrawnTag != 0 && DrawnTag != FlyingTag;

        /// <summary>The driver believes it spawned a type other than the one its own Aircraft is carrying — the
        /// spawn did not take, or something replaced the sim behind it.</summary>
        public bool DriverDisagrees => Valid && !string.IsNullOrEmpty(DriverId) && DriverId != FlyingId;

        /// <summary>The per-config geometry caches are holding another aeroplane's geometry (see the class remarks).
        /// A metre is far beyond any rounding: the fleet's spans differ by metres.</summary>
        public bool GeometryCacheDisagrees => Valid && System.Math.Abs(SpanCachedM - SpanAuthoredM) > 0.01;

        /// <summary>One of the three global statics is not at its shipped value, so some aeroplane's physics is being
        /// flown with a probe's setting.</summary>
        public bool StaticsDisturbed =>
            System.Math.Abs(Swirl - SwirlDefault) > 1e-9
            || System.Math.Abs(AttachedBias - AttachedBiasDefault) > 1e-9
            || System.Math.Abs(SpreadOverride - SpreadOverrideDefault) > 1e-9;

        public bool Agrees => Valid && !PictureDisagrees && !DriverDisagrees && !GeometryCacheDisagrees && !StaticsDisturbed;

        /// <summary>
        /// THE ONE LINE THAT IS ALWAYS ON SCREEN. In the healthy case it is four words and names the aeroplane the
        /// PHYSICS is flying, so a glance confirms it. In the failure case it is the whole report: it names both
        /// aeroplanes, which is the entire content of Ty's question, with no tap and no menu. Each disagreement says
        /// which of the four it is, because they want different answers.
        /// </summary>
        public string Line
        {
            get
            {
                if (!Valid) return "MODEL · no aeroplane";
                if (PictureDisagrees) return $"MODEL MISMATCH · FLYING {FlyingId} #{FlyingTag:x} · DRAWN {DrawnId} #{DrawnTag:x}";
                if (DriverDisagrees) return $"MODEL MISMATCH · FLYING {FlyingId} · the driver spawned {DriverId}";
                if (GeometryCacheDisagrees) return $"MODEL MISMATCH · {FlyingId} · cached span {SpanCachedM:F2} m, authored {SpanAuthoredM:F2} m";
                if (StaticsDisturbed) return $"MODEL · {FlyingId} · STATICS swirl {Swirl:F2} bias {AttachedBias:F2} spread {SpreadOverride:F2}";
                return $"MODEL · {FlyingId} · {MassKg:F0} kg · {SpanCachedM:F1} m span";
            }
        }

        /// <summary>
        /// THE FULL RECORD: what the one-liner leaves out, for a tap on the line and for the log. Written whole on
        /// every airframe change so a session can be read back afterwards without anyone having been looking at the
        /// screen when it happened.
        /// </summary>
        public string Detail
        {
            get
            {
                if (!Valid) return "FLIGHT MODEL: no aeroplane (no sim yet)";
                return
                    $"FLYING   {FlyingId} \"{FlyingName}\"  cfg#{FlyingTag:x}\n"
                  + $"DRAWN    {(DrawnTag == 0 ? "(not built)" : DrawnId)}  cfg#{DrawnTag:x}  type {DrawnTypeId}  livery {DrawnLivery}\n"
                  + $"MODEL    {DrawnModelPath ?? "procedural airframe (no GLB)"}\n"
                  + $"ASKED    driver {DriverId}  ·  launch page {SessionId}\n"
                  + $"GEOM     span {SpanCachedM:F3} m cached / {SpanAuthoredM:F3} m authored  ·  {MassKg:F1} kg  "
                  + $"Ixx {Ixx:F0} Iyy {Iyy:F0} Izz {Izz:F0}\n"
                  + $"STATICS  swirl {Swirl:F3} (0.2)  attachedBias {AttachedBias:F3} (0.5)  spreadOverride {SpreadOverride:F3} (-1)\n"
                  + $"VERDICT  {(Agrees ? "the picture and the physics are the same aeroplane" : "DISAGREE — " + Line)}";
            }
        }
    }

    /// <summary>Reads <see cref="ModelIdentityReading"/> off the live objects. Separate from the badge that draws it
    /// so a test, a clip or a log line can take a reading without a camera.</summary>
    public static class FlightModelIdentity
    {
        /// <summary>
        /// Take a reading. <paramref name="visual"/> may be null (no picture to compare against, which is itself
        /// reported rather than hidden). Allocates a string or two and walks the wing strips once, so it belongs on an
        /// aircraft change or a one-per-second refresh and never in the step.
        /// </summary>
        public static ModelIdentityReading Read(FlightSimDriver driver, AirframeVisual visual)
        {
            Aircraft ac = driver != null && driver.Sim != null ? driver.Sim.Aircraft : null;
            AircraftConfig flying = ac?.Config;
            if (flying == null)
                return new ModelIdentityReading(false, null, null, 0, null, 0, null, null, null,
                                                driver != null ? driver.AircraftId : null, SessionSettings.AircraftId,
                                                0, 0, 0, 0, 0, 0,
                                                Aircraft.SwirlFactor, PropJet.AttachedBias, PropJet.SpreadOverride);

            AircraftConfig drawn = visual != null ? visual.DrawnFromConfig : null;
            var mp = ac.MassProperties;
            return new ModelIdentityReading(
                true,
                flying.Id, flying.DisplayName, RuntimeHelpers.GetHashCode(flying),
                drawn?.Id, drawn != null ? RuntimeHelpers.GetHashCode(drawn) : 0,
                visual != null ? visual.DrawnTypeId : null,
                visual != null ? visual.DrawnModelPath : null,
                visual != null ? visual.DrawnLivery : null,
                driver != null ? driver.AircraftId : null, SessionSettings.AircraftId,
                AeroModel.WingSpan(flying), AeroModel.WingSpanUncached(flying),
                mp.MassKg, mp.Ixx, mp.Iyy, mp.Izz,
                Aircraft.SwirlFactor, PropJet.AttachedBias, PropJet.SpreadOverride);
        }
    }

    /// <summary>
    /// Draws the reading, and logs it.
    ///
    /// WHERE IT IS, AND WHY THERE. Always visible and small, on the shared text stack (UiLayout.NextLine), rather than
    /// correct-but-buried on the OPTIONS page. Three reasons, in order:
    ///  • The bug clears itself. Ty's did, inside a minute — "that behaviour is now gone and it is back to normal" —
    ///    so a diagnostic you have to go and open is a diagnostic that is shut when the thing happens.
    ///  • He has a standing complaint about exactly that: "we need a shortcut button… so I don't have to several menus
    ///    deep to switch".
    ///  • The failure line is self-sufficient. It names both aeroplanes, which is the whole of his question, so in the
    ///    case that matters nothing has to be opened at all.
    /// A TAP ON THE LINE opens the full record under it and a second tap closes it — one tap, no menus — for the
    /// object tags, the cached-against-authored span and the statics. OPTIONS › Aids has a switch to turn the line off
    /// for anyone who does not want it; it is on by default until the bug is caught.
    ///
    /// The stack is the no-overlap rule's own mechanism (owner 2026-10-03: no text or UI may ever overlap), so this
    /// cannot land on the challenge line, the dials or a button whatever the screen or the orientation. It is drawn
    /// first in the stack (DefaultExecutionOrder below FlightHud's) so its position does not move as other lines come
    /// and go.
    ///
    /// COST. One string comparison and a float compare per frame while nothing changes; the reading itself is retaken
    /// only when the sim's Aircraft or the drawn config changes identity, or once a second. Nothing is drawn in 3-D,
    /// no particle, no per-frame allocation in the common path — the J-3 is already the subject of a separate
    /// performance investigation and this must not appear in it.
    ///
    /// NOT NEAR A MAP. This says nothing about where the aeroplane is, so it cannot breach the standing rule that a
    /// chart never shows own-ship position.
    /// </summary>
    [DefaultExecutionOrder(-500)]   // first in the shared text stack, so the line does not move about
    public sealed class ModelIdentityBadge : MonoBehaviour
    {
        public FlightSimDriver Driver;

        /// <summary>How often the reading is retaken when nothing has changed identity. A swap is caught immediately
        /// by the reference checks; this only covers the statics, which nothing in the Bridge writes.</summary>
        public const float RefreshSec = 1f;

        private AirframeVisual _visual;
        private FlightSimDriver _visualFor;
        private ModelIdentityReading _reading;
        private object _readFor;        // the Aircraft the reading was taken for
        private object _readDrawnFor;   // and the drawn config at that moment
        private float _nextRefresh;
        private bool _detail;
        private bool _wasDisagreeing;
        private GUIStyle _line, _block;

        /// <summary>Colours: the quiet grey of a line you are meant to stop noticing, and the amber of one you are not.</summary>
        private static readonly Color Quiet = new(0.70f, 0.74f, 0.78f, 0.75f);
        private static readonly Color Loud = new(1f, 0.62f, 0.15f, 0.95f);

        private AirframeVisual Visual
        {
            get
            {
                if (Driver == null) return null;
                // Keyed on the Driver, not fetched once: in split screen this component is rebound to the other
                // player's aeroplane, and AirframeVisual is [RequireComponent(typeof(FlightSimDriver))] so it is
                // always on the Driver's own GameObject.
                if (!ReferenceEquals(_visualFor, Driver)) { _visualFor = Driver; _visual = Driver.GetComponent<AirframeVisual>(); }
                return _visual;
            }
        }

        private void Start()
        {
            if (Driver == null) return;
            // The log side. Every path that installs a new aeroplane goes through FlightSimDriver.Sim's setter, which
            // raises AirframeReplaced before the new aeroplane takes a step, so nothing can switch types without
            // leaving a line behind. AircraftChanged covers a rebuild that keeps the same Aircraft.
            Driver.AirframeReplaced += LogNow;
            Driver.AircraftChanged += LogNow;
        }

        private void OnDestroy()
        {
            if (Driver == null) return;
            Driver.AirframeReplaced -= LogNow;
            Driver.AircraftChanged -= LogNow;
        }

        /// <summary>
        /// One block in the device log per aircraft change, and one more the moment a disagreement appears. A replay
        /// or a session read back afterwards then carries the answer even if nobody was watching the line
        /// (Ty: "make the build log or display which config the flight model is actually using").
        /// QuestRig.Note as well, which is how the headset's own trace is read.
        /// </summary>
        private void LogNow()
        {
            // Taken fresh: an AirframeReplaced arrives before the visual has rebuilt, which is exactly the instant
            // worth recording — it says which aeroplane the picture still belongs to.
            Refresh(force: true);
            Debug.Log($"[ModelIdentity]\n{_reading.Detail}");
            QuestRig.Note($"model identity: {_reading.Line}");
        }

        private void Refresh(bool force)
        {
            Aircraft ac = Driver != null && Driver.Sim != null ? Driver.Sim.Aircraft : null;
            AircraftConfig drawn = Visual != null ? Visual.DrawnFromConfig : null;
            if (!force && ReferenceEquals(ac, _readFor) && ReferenceEquals(drawn, _readDrawnFor) && Time.unscaledTime < _nextRefresh) return;
            _readFor = ac; _readDrawnFor = drawn;
            _nextRefresh = Time.unscaledTime + RefreshSec;
            _reading = FlightModelIdentity.Read(Driver, Visual);
            // A disagreement that appears mid-flight, with no aircraft change to hang a log line on — which is the
            // shape of the one Ty saw, because it cleared itself without another swap. Edge-triggered: one block when
            // it starts and one when it ends, never a line a frame.
            bool disagreeing = _reading.Valid && !_reading.Agrees;
            if (disagreeing != _wasDisagreeing)
            {
                _wasDisagreeing = disagreeing;
                Debug.Log($"[ModelIdentity] {(disagreeing ? "DISAGREEMENT APPEARED" : "agreement restored")}\n{_reading.Detail}");
                QuestRig.Note($"model identity {(disagreeing ? "DISAGREES" : "agrees")}: {_reading.Line}");
            }
        }

        /// <summary>
        /// The reading is taken here and not in OnGUI, so the LOG half keeps working when the line is switched off,
        /// when a menu is open and in a headset — all of which stop the drawing below. Ty asked for "the build log OR
        /// display"; they are not the same answer and the log is the one that survives a session nobody was watching.
        /// Once a frame, and almost always a handful of reference compares (see <see cref="Refresh"/>).
        /// </summary>
        private void Update() => Refresh(force: false);

        private void OnGUI()
        {
            if (!SessionSettings.ShowModelIdentity) return;
            if (Driver == null || SessionSettings.MenuOpen || UiLayout.Modal) return;
            if (LocalPlayerHud.CompactActive) return;
            // In a headset a screen-space overlay smears across both eyes at the wrong depth (the same rule every
            // flat overlay follows); the Quest reads this out of QuestRig.Note instead.
            if (VrMode.SuppressFlatOverlays) return;

            int fs = UiLayout.MinText;
            _line ??= new GUIStyle { font = UiFont.Get(), alignment = TextAnchor.MiddleLeft };
            _block ??= new GUIStyle { font = UiFont.Get(), alignment = TextAnchor.UpperLeft, wordWrap = false };
            _line.fontSize = fs;
            _block.fontSize = fs;
            bool loud = _reading.Valid && !_reading.Agrees;
            _line.normal.textColor = loud ? Loud : Quiet;
            _block.normal.textColor = loud ? Loud : Quiet;

            float lh = fs * 1.5f;
            Rect row = UiLayout.NextLine(lh);
            UiLayout.Label(row, _reading.Line, _line);

            // ONE TAP opens the full record and one closes it. Hit-tested by hand rather than drawn as a GUI.Button:
            // the built-in skin is stripped on iOS (see FlightHud) and a button box here would read as a control.
            // Use() keeps the tap off whatever is behind it.
            Event e = Event.current;
            if (e != null && e.type == EventType.MouseDown && row.Contains(e.mousePosition))
            {
                _detail = !_detail;
                e.Use();
            }

            if (!_detail) return;
            string detail = _reading.Detail;
            int lines = 1; foreach (char c in detail) if (c == '\n') lines++;
            UiLayout.LabelWrapped(UiLayout.NextBlock(lh * lines), detail, _block);
        }
    }
}
