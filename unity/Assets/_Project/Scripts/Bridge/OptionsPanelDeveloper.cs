using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Tuning;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE DEVELOPER PAGE: every tuning constant in the sim, on a slider, for the aeroplane being flown.
    ///
    /// Ty, 2026-11-11: "Create a test mode page in options with all of the k factors so I can tune them and dial in
    /// each airplane including things like toe in/out idle rpm adjustments vertical tail offsets etc" — "Clicking on
    /// the title for each provides a description of what it does this is a developer only feature/page so call it
    /// developer and we'll close off access prior to shipping" — "But make it available during beta because I have
    /// some test pilots who are doing beta testing."
    ///
    /// It is a RENDERER. It knows nothing about any particular knob: it draws whatever is in
    /// <see cref="TunableRegistry"/>, in that registry's groups, so a new knob is one Register call beside the field
    /// it moves and this file never changes. That is the only way a page like this survives contact with a year of
    /// physics work.
    ///
    /// WHY IT IS BUILT FOR A THUMB IN SUNLIGHT. He flies with the phone in portrait, outdoors, and a soft keyboard
    /// over a numeric field would make the page useless where it is used. So every knob is a slider for the coarse
    /// move and four step buttons for the approach, the readout carries enough digits to see the fine step move it,
    /// and the shipped default is printed beside the current value so he can always see how far he has gone.
    ///
    /// THE GATE IS THE TAB, NOT THIS FILE. See <see cref="DeveloperAccess"/>: the page is always compiled, and the
    /// switch removes the route. Which is why nothing here runs unless the page is being drawn — no Update, no
    /// coroutine, no state written at startup. The page is free when it is closed.
    /// </summary>
    public sealed partial class OptionsPanel
    {
        /// <summary>Which knob's description is open; "" for none. One at a time — the page is long enough.</summary>
        private string _devOpenDesc = "";

        /// <summary>The COPY FROM picker is showing its list.</summary>
        private bool _devCopyPicking;

        /// <summary>What the aeroplane's knobs were before the last COPY FROM, so it can be taken back. He will copy
        /// onto a tuned aeroplane by accident, and the tuning that would lose was an evening's flying.</summary>
        private Dictionary<string, double> _devUndo;
        private string _devUndoNote = "";

        private void DeveloperPage(ref float y)
        {
            AircraftConfig cfg = Driver == null || Driver.Sim == null || Driver.Sim.Aircraft == null
                               ? null : Driver.Sim.Aircraft.Config;
            if (cfg == null)
            {
                UiLayout.Label(new Rect(_x, y, _cw, _lh), "DEVELOPER", _head); y += _lh;
                Note("No aeroplane is loaded, so there is nothing to tune. Start a flight and come back.", ref y);
                return;
            }

            UiLayout.Label(new Rect(_x, y, _cw, _lh), "DEVELOPER", _head); y += _lh;
            Note(TuningSession.LabelOf(cfg) + " — these numbers apply to this aeroplane in this configuration, and to "
                 + "nothing else. They are kept when you leave and put back when you fly it again.", ref y);

            // THE STOCK / MODIFIED INDICATOR. A tunable page makes every flight report suspect unless you can see
            // whether the aeroplane was the shipped one, and Ty has beta test pilots sending reports back.
            int mod = TuningSession.ModifiedCount(cfg);
            {
                string banner = mod == 0
                    ? "STOCK — every knob at its shipped value"
                    : "MODIFIED — " + mod + (mod == 1 ? " knob is" : " knobs are") + " off the shipped value";
                float bh = RowH(banner, _cw);
                UiLayout.LabelWrapped(new Rect(_x, y, _cw, bh), banner, mod == 0 ? _note : _head);
                y += bh + _gap * 0.3f;
            }

            // The three actions, on one row: put it all back, take another aeroplane's tuning, hand the deltas to a
            // report. COPY REPORT is the one that makes a tester's bug report worth reading.
            {
                float bw = (_cw - _gap) / 3f;
                if (UiLayout.Button(new Rect(_x, y, bw, _lh), "RESET ALL", _btn))
                {
                    _devUndo = TuningSession.Snapshot(cfg);
                    _devUndoNote = "reset all";
                    TuningSession.ResetAll(cfg);
                }
                if (UiLayout.Button(new Rect(_x + bw + _gap * 0.5f, y, bw, _lh), _devCopyPicking ? "CANCEL" : "COPY FROM…", _btn))
                    _devCopyPicking = !_devCopyPicking;
                if (UiLayout.Button(new Rect(_x + 2 * (bw + _gap * 0.5f), y, bw, _lh), "COPY REPORT", _btn))
                    GUIUtility.systemCopyBuffer = TuningSession.DeltaReport(cfg);
                y += _lh + _gap * 0.4f;
            }

            if (_devUndo != null)
            {
                if (UiLayout.Button(new Rect(_x, y, _cw * 0.55f, _lh), "UNDO (" + _devUndoNote + ")", _btnOn))
                {
                    TuningSession.Restore(cfg, _devUndo);
                    _devUndo = null; _devUndoNote = "";
                }
                y += _lh + _gap * 0.4f;
            }

            if (_devCopyPicking) { CopyFromPicker(cfg, ref y); y += _gap * 0.5f; }

            RecorderSection(cfg, ref y);

            Note("Tap a knob's TITLE to read what it does. ◂◂ ◂ ▸ ▸▸ are the coarse and fine steps; the slider is for "
                 + "getting near. A row marked NEXT FLY is built into the aeroplane when it is created, so it takes "
                 + "effect the next time you fly rather than straight away.", ref y);
            y += _gap * 0.4f;

            List<KeyValuePair<string, List<Tunable>>> groups = TunableRegistry.ForAircraft(cfg);
            for (int gi = 0; gi < groups.Count; gi++)
            {
                y += _gap * 0.5f;
                UiLayout.Label(new Rect(_x, y, _cw, _lh), groups[gi].Key.ToUpperInvariant(), _head);
                y += _lh;
                List<Tunable> knobs = groups[gi].Value;
                for (int ki = 0; ki < knobs.Count; ki++) KnobRow(cfg, knobs[ki], ref y);
            }

            GlobalSection(ref y);
        }

        /// <summary>
        /// THE GLOBAL KNOBS, drawn apart from everything above them and labelled so nobody can mistake one for the
        /// other. Every row above belongs to the aeroplane being flown and to nothing else; every row down here
        /// belongs to the whole fleet, and a pilot who moves one and then reports how the Cub flew has changed the
        /// Sabre too.
        ///
        /// It is drawn LAST on purpose — the per-aircraft knobs are what he is normally here for — and separated by
        /// a rule, its own banner and its own reset, so the boundary is visible on a phone in sunlight without
        /// reading anything. The rows themselves use the same KnobRow idiom, because a knob that behaved
        /// differently under the thumb would be a second thing to learn for no reason.
        /// </summary>
        private void GlobalSection(ref float y)
        {
            List<KeyValuePair<string, List<GlobalTunable>>> groups = GlobalTunableRegistry.Groups();
            if (groups.Count == 0) return;

            y += _gap * 1.2f;
            // A rule across the page: the one piece of pure decoration here, and it earns its place by marking the
            // boundary at a glance.
            UiLayout.Label(new Rect(_x, y, _cw, _lh), "──────────────────────────────", _note);
            y += _lh * 0.8f;
            UiLayout.Label(new Rect(_x, y, _cw, _lh), "GLOBAL — THESE APPLY TO EVERY AEROPLANE", _head);
            y += _lh;

            int gmod = GlobalTuningSession.ModifiedCount();
            {
                string banner = gmod == 0
                    ? "STOCK — every global at its shipped value"
                    : "MODIFIED — " + gmod + (gmod == 1 ? " global is" : " globals are") + " off the shipped value, on EVERY aeroplane";
                float bh = RowH(banner, _cw);
                UiLayout.LabelWrapped(new Rect(_x, y, _cw, bh), banner, gmod == 0 ? _note : _head);
                y += bh + _gap * 0.3f;
            }

            Note("These are not this aeroplane's numbers. They are the model's — one value for the whole fleet — so "
                 + "moving one changes how every type behaves, including the ones you are not flying. They are kept "
                 + "separately from the per-aeroplane tuning above, they are not copied by COPY FROM, and RESET ALL "
                 + "up there does not touch them.", ref y);

            if (UiLayout.Button(new Rect(_x, y, _cw * 0.55f, _lh), "RESET GLOBALS", gmod == 0 ? _btn : _btnOn))
                GlobalTuningSession.ResetAll();
            y += _lh + _gap * 0.4f;

            for (int gi = 0; gi < groups.Count; gi++)
            {
                y += _gap * 0.5f;
                UiLayout.Label(new Rect(_x, y, _cw, _lh), groups[gi].Key.ToUpperInvariant(), _head);
                y += _lh;
                List<GlobalTunable> knobs = groups[gi].Value;
                for (int ki = 0; ki < knobs.Count; ki++) GlobalKnobRow(knobs[ki], ref y);
            }
        }

        /// <summary>One global knob — the same row as <see cref="KnobRow"/>, reading and writing a static instead of
        /// a field on the aeroplane's config. Every global is live by construction: a static is read by the physics
        /// on the next step, so there is no NEXT FLY case to draw.</summary>
        private void GlobalKnobRow(GlobalTunable t, ref float y)
        {
            double nowD;
            try { nowD = t.Get(); } catch (System.Exception) { return; }
            float now = (float)nowD;
            bool modified = GlobalTuningSession.IsModified(t);
            string fmt = "F" + t.Decimals;

            {
                float lw = _cw * 0.54f;
                float h = RowH(t.Label, lw);
                if (UiLayout.Button(new Rect(_x, y, lw, h), t.Label, modified ? _btnOn : _label))
                    _devOpenDesc = _devOpenDesc == t.Id ? "" : t.Id;
                string reading = now.ToString(fmt) + t.Units;
                if (!double.IsNaN(t.Shipped)) reading += "   (was " + t.Shipped.ToString(fmt) + t.Units + ")";
                UiLayout.Label(new Rect(_x + lw + _gap * 0.5f, y, _cw - lw - _gap * 0.5f, h), reading, modified ? _head : _note);
                y += h;
            }

            {
                float v = GUI.HorizontalSlider(new Rect(_x, y + _lh * 0.25f, _cw, _lh * 0.4f),
                                               now, (float)t.Min, (float)t.Max);
                if (Mathf.Abs(v - now) > (float)t.FineStep * 0.1f) GlobalTuningSession.Set(t, v);
                y += _lh * 0.8f;
            }

            {
                float bw = (_cw * 0.62f - _gap * 1.5f) / 4f;
                if (UiLayout.Button(new Rect(_x, y, bw, _lh), "◂◂", _btn)) GlobalTuningSession.Set(t, nowD - t.Step);
                if (UiLayout.Button(new Rect(_x + bw + _gap * 0.5f, y, bw, _lh), "◂", _btn)) GlobalTuningSession.Set(t, nowD - t.FineStep);
                if (UiLayout.Button(new Rect(_x + 2 * (bw + _gap * 0.5f), y, bw, _lh), "▸", _btn)) GlobalTuningSession.Set(t, nowD + t.FineStep);
                if (UiLayout.Button(new Rect(_x + 3 * (bw + _gap * 0.5f), y, bw, _lh), "▸▸", _btn)) GlobalTuningSession.Set(t, nowD + t.Step);
                float rx = _x + _cw * 0.64f, rw = _cw * 0.36f;
                if (modified && UiLayout.Button(new Rect(rx, y, rw * 0.5f, _lh), "RESET", _btnOn)) GlobalTuningSession.Reset(t);
                y += _lh + _gap * 0.3f;
            }

            if (_devOpenDesc == t.Id) { Note(t.Description, ref y); y += _gap * 0.3f; }
            y += _gap * 0.2f;
        }

        /// <summary>
        /// THE FLIGHT TEST RECORDER. It is ALWAYS RUNNING — this page does not switch it on, it decides what to do
        /// with what has already been recorded. Ty's test pilots fly cards rather than menus, so the recording
        /// cannot depend on anyone remembering to arm it; what a pilot does here is KEEP the last few minutes after
        /// something interesting has happened.
        /// </summary>
        private void RecorderSection(AircraftConfig cfg, ref float y)
        {
            y += _gap * 0.5f;
            UiLayout.Label(new Rect(_x, y, _cw, _lh), "FLIGHT TEST RECORDER", _head); y += _lh;

            double minutes = FlightTestRecording.MinutesHeld;
            string state = FlightTestRecording.Armed
                ? "Recording — " + minutes.ToString("F1") + " min in the buffer"
                : "NOT recording — " + FlightTestRecording.Status;
            float h = RowH(state, _cw);
            UiLayout.LabelWrapped(new Rect(_x, y, _cw, h), state, FlightTestRecording.Armed ? _note : _head);
            y += h + _gap * 0.3f;

            float bw = (_cw - _gap) / 3f;
            if (UiLayout.Button(new Rect(_x, y, bw, _lh), "KEEP FLIGHT", _btnOn))
                FlightTestRecording.Save(cfg, DrawnModelId());
            if (UiLayout.Button(new Rect(_x + bw + _gap * 0.5f, y, bw, _lh), "MARK", _btn))
                FlightTestRecording.Mark();
            if (UiLayout.Button(new Rect(_x + 2 * (bw + _gap * 0.5f), y, bw, _lh), "COPY PATH", _btn))
                GUIUtility.systemCopyBuffer = FlightTestRecording.LastSavedPath;
            y += _lh + _gap * 0.4f;

            if (FlightTestRecording.Status.Length > 0) Note(FlightTestRecording.Status, ref y);
            if (FlightTestRecording.LastSavedPath.Length > 0) Note("Saved to " + FlightTestRecording.LastSavedPath, ref y);
            Note("MARK drops a flag in the recording so you can find this moment again. KEEP FLIGHT writes the last "
                 + "few minutes to a file you can pull off the phone from the Files app. The recorder also marks "
                 + "stalls, spins, touchdowns, impacts and anything breaking off by itself, and records four times "
                 + "as fast for two seconds around each of them.", ref y);
            y += _gap * 0.5f;
        }

        /// <summary>Which model is actually being DRAWN, which is not always the one being flown — the MODEL
        /// MISMATCH banner exists because they have disagreed. Both go in the recording header.</summary>
        private string DrawnModelId()
        {
            try { return SessionSettings.AircraftId; }
            catch (System.Exception) { return ""; }
        }

        /// <summary>
        /// COPY ANOTHER AEROPLANE'S TUNING ONTO THIS ONE. Ty, 2026-11-11: "there should be a carry forward button so
        /// I could tune the j3 on wheels then carry forward to start the j3 on floats for example so I don't have to
        /// start from scratch. More like copy x airplane".
        ///
        /// It copies the DISTANCE FROM DEFAULT, not the number — see TuningSession.CopyFrom for why that is what he
        /// means — and the page says so in as many words, because the two are the same thing only when the two
        /// aeroplanes shipped with the same value and he should not have to work out which case he is in.
        /// </summary>
        private void CopyFromPicker(AircraftConfig cfg, ref float y)
        {
            Note("Copies how far each knob has been moved FROM ITS OWN SHIPPED VALUE — so \"two degrees more toe-out\" "
                 + "carries across, rather than an absolute that would mean something different on another aeroplane. "
                 + "Knobs this aeroplane has not got are skipped. You can undo it.", ref y);
            List<TunedEntry> tuned = TuningSession.TunedAircraft(cfg);
            if (tuned.Count == 0)
            {
                Note("No other aeroplane has been tuned yet, so there is nothing to copy.", ref y);
                return;
            }
            for (int i = 0; i < tuned.Count; i++)
            {
                string label = tuned[i].Label + "  (" + tuned[i].Count + (tuned[i].Count == 1 ? " knob)" : " knobs)");
                float h = RowH(label, _cw * 0.8f);
                if (UiLayout.Button(new Rect(_x, y, _cw * 0.8f, h), label, _btn))
                {
                    _devUndo = TuningSession.Snapshot(cfg);
                    int skipped;
                    int moved = TuningSession.CopyFrom(cfg, tuned[i].Key, out skipped);
                    _devUndoNote = "copied " + moved + (skipped > 0 ? ", " + skipped + " skipped" : "");
                    _devCopyPicking = false;
                }
                y += h + _gap * 0.3f;
            }
        }

        /// <summary>
        /// ONE KNOB. Title and readout, then the slider, then the steps — and the description underneath when the
        /// title has been tapped.
        ///
        /// The title is a BUTTON drawn in the label style, which is the panel's existing idiom for a tappable
        /// sentence (see Check), so it looks like a title and behaves like a control.
        /// </summary>
        private void KnobRow(AircraftConfig cfg, Tunable t, ref float y)
        {
            double nowD;
            try { nowD = t.Get(cfg); } catch (System.Exception) { return; }
            float now = (float)nowD;
            double def = TuningSession.DefaultOf(TuningSession.ConfigKeyOf(cfg), t.Id);
            bool modified = TuningSession.IsModified(cfg, t);
            string fmt = "F" + t.Decimals;

            // Title (tappable) on the left, the reading on the right. The reading carries the shipped value beside it
            // so he can always see how far he has moved, which is the thing a bare number cannot tell him.
            {
                float lw = _cw * 0.54f;
                float h = RowH(t.Label, lw);
                if (UiLayout.Button(new Rect(_x, y, lw, h), t.Label, modified ? _btnOn : _label))
                    _devOpenDesc = _devOpenDesc == t.Id ? "" : t.Id;
                string reading = now.ToString(fmt) + t.Units;
                if (!double.IsNaN(def)) reading += "   (was " + def.ToString(fmt) + t.Units + ")";
                UiLayout.Label(new Rect(_x + lw + _gap * 0.5f, y, _cw - lw - _gap * 0.5f, h), reading, modified ? _head : _note);
                y += h;
            }

            // The slider: full width, because getting near a value is the thing a thumb is good at.
            {
                float v = GUI.HorizontalSlider(new Rect(_x, y + _lh * 0.25f, _cw, _lh * 0.4f),
                                               now, (float)t.Min, (float)t.Max);
                // A dead band an order below the fine step: IMGUI sliders jitter by a pixel, and without this the
                // page would write a new value every frame a thumb rested on it.
                if (Mathf.Abs(v - now) > (float)t.FineStep * 0.1f) TuningSession.Set(cfg, t, v);
                y += _lh * 0.8f;
            }

            // Steps and reset. Coarse and fine both ways, because the slider cannot be read to a hundredth outdoors.
            {
                float bw = (_cw * 0.62f - _gap * 1.5f) / 4f;
                if (UiLayout.Button(new Rect(_x, y, bw, _lh), "◂◂", _btn)) TuningSession.Set(cfg, t, nowD - t.Step);
                if (UiLayout.Button(new Rect(_x + bw + _gap * 0.5f, y, bw, _lh), "◂", _btn)) TuningSession.Set(cfg, t, nowD - t.FineStep);
                if (UiLayout.Button(new Rect(_x + 2 * (bw + _gap * 0.5f), y, bw, _lh), "▸", _btn)) TuningSession.Set(cfg, t, nowD + t.FineStep);
                if (UiLayout.Button(new Rect(_x + 3 * (bw + _gap * 0.5f), y, bw, _lh), "▸▸", _btn)) TuningSession.Set(cfg, t, nowD + t.Step);
                float rx = _x + _cw * 0.64f, rw = _cw * 0.36f;
                if (modified && UiLayout.Button(new Rect(rx, y, rw * 0.5f, _lh), "RESET", _btnOn)) TuningSession.Reset(cfg, t);
                if (!t.Live) UiLayout.Label(new Rect(rx + rw * 0.52f, y, rw * 0.48f, _lh), "NEXT FLY", _note);
                y += _lh + _gap * 0.3f;
            }

            if (t.Derived) Note("DERIVED — " + t.Rule, ref y);
            if (!t.Live && t.LiveNote.Length > 0) Note(t.LiveNote, ref y);
            if (_devOpenDesc == t.Id) { Note(t.Description, ref y); y += _gap * 0.3f; }
            y += _gap * 0.2f;
        }
    }
}
