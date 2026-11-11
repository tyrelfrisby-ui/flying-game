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
