# Grob G 103 C Twin III Acro - Blanc conversion

Original aircraft mesh: **Florian Blanc**, **G103c Twin III Acro**.
Source: https://www.printables.com/model/852637-g103c-twin-iii-acro
Mirrors recorded with the supplied download: https://www.thingiverse.com/thing:6568049
and https://makerworld.com/en/models/463059 .

The supplied source and download README identify the mesh as **Creative Commons
Attribution-ShareAlike (CC BY-SA)**. This adapted mesh is distributed under
**CC BY-SA 4.0**, https://creativecommons.org/licenses/by-sa/4.0/ .
Retain Florian Blanc's credit, this source link and the share-alike terms when
redistributing the adapted mesh. No endorsement by Florian Blanc is implied.
The upstream page was unavailable to the conversion browser; the source licence
record is the user-approved download README, not a newly verified page snapshot.

Changes by Talk Flying / Ty Frisby's project, 2026-11-03: unpacked and assembled
print-plate fuselage halves, inner and outer wings and T-tail; removed internal
print caps, joined the centre seam; scaled to 18.00 m span, 8.18 m length and
1.71 m level belly-to-fin crown; extracted canopy glazing and white frame;
extracted independently named ailerons, elevator halves, rudder and spoilers;
added UVs, gelcoat, red trim and synthetic D-103C registration-style lettering;
retained and fitted the project's original tandem cockpit, instruments, control
maps and dimensioned rounded fixed gear with skin fillets; decimated for mobile.
Both `*-own.glb` files in this folder now contain this derived CC BY-SA mesh.
The historical `own` filename does not mean this replacement is clean-room art.

The original project cockpit and dial artwork are retained from
`tools/own_models/build_grob103.py` and `grob103_cockpit.py`. Their inclusion in
these combined mesh assets is covered by the same CC BY-SA 4.0 adaptation terms.
Raw STL and POH pixels are not embedded in the GLBs. FlightGear-derived Grob
reference inspected for general scale/proportions only; its older Twin Astir
variant is not the dimensional authority for the Twin III. No FlightGear
vertices, materials or textures were copied into this conversion.

Source STL SHA-256:
`4cb05ac635e412a63f8758d4704199eb6283bcc4645e46339cd46cff743916e0`.
The 5.1 MB raw print plate remains outside git in the approved transfer folder.

Reproduce using existing Blender and Python with NumPy/Pillow (no install):

```sh
blender -b --factory-startup --python-exit-code 1 --python tools/own_models/build_grob103_blanc.py -- --python /path/to/python
python tools/own_models/check_grob103.py
blender -b --factory-startup --python-exit-code 1 --python tools/own_models/render_grob103_blanc.py
python tools/own_models/overlay_grob103_blanc.py /path/to/TwinIII-POH.pdf
```

Pass `--source /path/to/Twin3_plate.stl` after Blender's `--` if the external
source is elsewhere. The builder regenerates the old analytic asset in a
private temporary directory solely to retain its original cockpit/rig/gear.
It never reads a previously converted asset as its input.
Visual review and Unity/device validation remain pending Ty's approval.
