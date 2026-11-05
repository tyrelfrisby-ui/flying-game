"""The shared cockpit instrument library (Ty 2026-11-05: "we don't need 6 different coordinators ... we just need one
and it gets used 6 times").

One generator per instrument, one GLB per instrument, used by every aircraft. An aircraft does not build an instrument:
it says WHERE one goes (FlyingGame.Core.Instruments.InstrumentLibrary, the mount table) and, for an instrument whose
markings are the aeroplane's own, what the markings are (configs/aircraft/<id>.json "instruments"). The game places
the library instrument over the panel at runtime (Bridge/SharedInstruments.cs) and stands down whatever the old model
drew there, so every GLB built before the library still gets the shared instrument.

  turn_coordinator  generic TC: thin tapered wings to the width of the standard-rate index (design: lane/codex-turn-coord
                    f2e544ee, Ty 2026-11-05 05:55), slip ball in a curved tube
  airspeed          generic ASI: one law (30 deg at the scale floor, 330 deg at its top), the aeroplane's own arcs/units
  altimeter         generic three-pointer altimeter (100 / 1,000 / 10,000 ft); the Kollsman window is the panel's
                    (CockpitTaps draws its digits)
  attitude          generic AI: roll card with a pitch card that slides on it, fixed bank scale and miniature aeroplane
  vertical_speed    generic VSI, +/-2,000 ft/min, zero at 9 o'clock
  heading           generic DG: the card turns, lubber line and aeroplane stay

Frame (every GLB): metres are FACE RADII (the face is a disc of radius 1 at z = 0), glTF axes, +z toward the pilot,
+y up, +x to the pilot's right. Every moving part's node origin is ON its pivot (the dial centre) with its vertices
relative to it (CLAUDE.md "a rig row turns its node about the node's origin"). The runtime scales the instrument by the
mount's face radius.

Period-correct variants are the exception and are named as such (the J-3's own instruments stay the J-3's until the
library has a period face; see PERIOD_VARIANTS in check_no_private_instruments and InstrumentLibraryTests).

Run with the system Python that has NumPy and Pillow:
    /usr/bin/python3 tools/own_models/instruments/build.py           # every GLB + every per-aircraft face
    /usr/bin/python3 tools/own_models/instruments/build.py --review  # plus CPU close-ups into renders/shared-instruments/
"""
