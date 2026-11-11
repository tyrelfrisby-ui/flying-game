using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;   // AircraftConfig: it is NOT in FlyingGame.Core
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Recording;
using FlyingGame.Core.Tuning;
using FlyingGame.Sim;
using FlyingGame.Sim.Recording;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE FLIGHT TEST RECORDER, WIRED INTO THE GAME. Always armed, keeping the last N minutes; a pilot's only job
    /// is to decide to KEEP what has already been recorded.
    ///
    /// WHY ALWAYS ARMED AND WHY NOT GATED. Ty's beta pilots fly cards, not menus. A recorder you must remember to
    /// switch on is off for the flight that mattered, so the recording runs whether or not the Developer page is
    /// reachable — the PAGE is gated, the DATA is not. Gating the recording would make the data unavailable exactly
    /// when a tester hits something worth reporting.
    ///
    /// NOTHING TOUCHES THE DISK IN FLIGHT. The samples go into a preallocated ring in memory; saving copies the
    /// ring and hands the bytes to a background thread. So the physics step pays for one block copy and no I/O.
    /// </summary>
    public static class FlightTestRecording
    {
        /// <summary>About 110 minutes at the 50 Hz baseline. The ring wraps, so a long flight keeps its END.</summary>
        public const int RingBytes = 200 * 1024 * 1024;

        private static FlightTestRecorder _rec;
        private static FlightTestEvents _events;
        private static SimLoop _hooked;
        private static string _docs;
        private static string _lastSaved = "";
        private static string _status = "";
        private static int _savesInFlight;

        public static FlightTestRecorder Recorder => _rec;
        public static bool Armed => _rec != null && _rec.Enabled && !_rec.AutoStopped;
        public static string LastSavedPath => _lastSaved;
        public static string Status => _rec == null ? "not started"
                                     : _rec.AutoStopped ? _rec.AutoStopReason
                                     : _status;

        /// <summary>Minutes of flight currently in the ring.</summary>
        public static double MinutesHeld => _rec == null ? 0.0
            : _rec.Ring.Count / (double)FlightRecording.BaselineHz / 60.0;

        /// <summary>
        /// Attach to a newly spawned aeroplane. Called from FlightSimDriver whenever the SimLoop is replaced, so
        /// the recorder follows the aeroplane the pilot is actually flying.
        /// </summary>
        public static void Attach(SimLoop sim, Aircraft aircraft)
        {
            if (sim == null || aircraft == null) return;
            if (_rec == null)
            {
                try { _rec = new FlightTestRecorder(RingBytes); }
                catch (Exception e) { _status = "recorder not started: " + e.Message; return; }
                _events = new FlightTestEvents();
                _docs = ResolveDocumentsFolder();
                Debug.Log("[FlightTest] recording to " + _docs + " (" + _rec.ChannelCount + " channels, "
                          + FlightRecording.BaselineHz + " Hz, bursting to " + FlightRecording.BurstHz + ")");
            }

            if (_hooked != null) _hooked.Stepped -= OnStep;
            sim.Stepped += OnStep;
            _hooked = sim;
            _events.Reset();

            // The events the sim already raises are wired straight through; the rest (stall, spin, touchdown) are
            // watched for per step, because the sim has no event for them.
            aircraft.ComponentLost += _ => _rec?.Mark(FlightRecording.Event.ComponentLoss);
            aircraft.HardImpact += (speed, where) => _rec?.Mark(FlightRecording.Event.Damage);
            _status = "armed";
        }

        private static void OnStep(SimLoop sim, ControlInputs inputs)
        {
            if (_rec == null || sim == null) return;
            Aircraft a = sim.Aircraft;
            if (a == null) return;

            FlightRecording.Event fired = _events.Step(a, sim.FixedDtSec);
            if (fired != FlightRecording.Event.None) _rec.Mark(fired);

            _rec.Step(a, inputs, sim.FixedDtSec, WindAt(a), OilTempF(), FlightSimDriver.CatchUpCappedFrames);
        }

        /// <summary>The wind the aeroplane is actually in. Sampled once per step rather than once per channel.</summary>
        private static Vec3 WindAt(Aircraft a)
        {
            try { return Atmosphere.WindAtPosition(a.State.Position); }
            catch (Exception) { return Vec3.Zero; }
        }

        /// <summary>Oil TEMPERATURE is owned by the cockpit bridge, not the sim, so it is pushed in rather than
        /// read. 0 on a build with no cockpit state, which is honest: the channel is empty, not wrong.</summary>
        private static double OilTempF()
        {
            try { return CockpitRigState.LastOilTempF; }
            catch (Exception) { return 0.0; }
        }

        /// <summary>A pilot marking something he wants to find again in the file.</summary>
        public static void Mark() => _rec?.Mark(FlightRecording.Event.PilotMark);

        /// <summary>
        /// KEEP WHAT IS IN THE RING. Copies it on this thread (fast, no I/O) and writes it on a background one, so
        /// a 40 MB save never stutters the flight.
        /// </summary>
        public static void Save(AircraftConfig config, string drawnModelId)
        {
            if (_rec == null) { _status = "nothing recorded yet"; return; }
            if (_rec.Ring.Count == 0) { _status = "nothing in the buffer yet"; return; }

            byte[] bytes;
            try { bytes = _rec.BuildFile(Header(config, drawnModelId)); }
            catch (Exception e) { _status = "could not build the recording: " + e.Message; return; }

            string folder = _docs;
            string name = "flight-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".aerorec";
            string path = Path.Combine(folder, name);
            Interlocked.Increment(ref _savesInFlight);
            _status = "saving " + (bytes.Length / (1024 * 1024)) + " MB...";

            // Application.persistentDataPath is a Unity API and must not be touched off the main thread — the path
            // was resolved on the main thread and only the string crosses over.
            var t = new Thread(() =>
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    string tmp = path + ".tmp";
                    File.WriteAllBytes(tmp, bytes);
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmp, path);                      // never leave a half-written file under the real name
                    _lastSaved = path;
                    _status = "saved " + name + " (" + (bytes.Length / (1024 * 1024)) + " MB)";
                }
                catch (Exception e) { _status = "save failed: " + e.Message; }
                finally { Interlocked.Decrement(ref _savesInFlight); }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>
        /// WHAT WAS FLOWN. The two identities are SEPARATE fields on purpose: the flight model and the drawn model
        /// can disagree in the running game — that is what the MODEL MISMATCH banner exists to catch — and a
        /// recording that carried one "aircraft" field could not be trusted to say which aeroplane it was.
        ///
        /// The tuning block is the same DeltaReport the Developer page's COPY REPORT produces. One function, two
        /// callers, so a recording and a bug report can never disagree about what the knobs were.
        /// </summary>
        private static List<KeyValuePair<string, string>> Header(AircraftConfig config, string drawnModelId)
        {
            var h = new List<KeyValuePair<string, string>>();
            void Add(string k, string v) => h.Add(new KeyValuePair<string, string>(k, v ?? ""));

            Add("recorded", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Add("aircraftFlown", config == null ? "" : config.Id);
            Add("aircraftName", config == null ? "" : config.DisplayName);
            Add("modelDrawn", drawnModelId ?? "");
            Add("configuration", config == null ? "" : TuningSession.ConfigKeyOf(config));
            Add("build", Application.version);
            Add("platform", Application.platform.ToString());
            Add("baselineHz", FlightRecording.BaselineHz.ToString());
            Add("burstHz", FlightRecording.BurstHz.ToString());
            Add("recorderCostUs", _rec == null ? "" : _rec.MeanSampleMicros.ToString("F1"));
            if (config != null)
            {
                Add("massKg", config.Mass.MassKg.ToString("F1"));
                Add("cg", config.Mass.CgVec().X.ToString("F4") + " " + config.Mass.CgVec().Y.ToString("F4") + " " + config.Mass.CgVec().Z.ToString("F4"));
            }
            try { Add("airport", SessionSettings.Airport.ToString()); } catch (Exception) { }
            Add("tuning", config == null ? "" : TuningSession.DeltaReport(config));
            return h;
        }

        /// <summary>
        /// WHERE A PILOT CAN ACTUALLY GET AT THE FILE. On iOS only the app's Documents directory is exposed to the
        /// Files app (through UIFileSharingEnabled, set in BuildScript's post-process step), and Unity's
        /// persistentDataPath has been Documents in some versions and Library/Application Support in others — so
        /// this RESOLVES it rather than assuming, and logs what it found. Everywhere else persistentDataPath is
        /// the right answer and this returns it unchanged.
        /// </summary>
        private static string ResolveDocumentsFolder()
        {
            string p = Application.persistentDataPath;
            try
            {
#if UNITY_IOS && !UNITY_EDITOR
                const string docs = "/Documents";
                int at = p.LastIndexOf(docs, StringComparison.Ordinal);
                if (at >= 0) return Path.Combine(p.Substring(0, at + docs.Length), "FlightTest");

                // .../Library/Application Support/<company>/<product> — climb to the container and go to Documents.
                var d = new DirectoryInfo(p);
                while (d != null && d.Parent != null && d.Name != "Library") d = d.Parent;
                if (d != null && d.Parent != null) return Path.Combine(d.Parent.FullName, "Documents", "FlightTest");
#endif
                return Path.Combine(p, "FlightTest");
            }
            catch (Exception) { return p; }
        }
    }
}
