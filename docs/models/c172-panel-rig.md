# Cessna 172 late-M panel/cabin — binding handoff for Claude

Review state: **v1, awaiting Ty’s render approval.** Local lane `lane/codex-172-cockpit`; no merge or deployment. Glass Overlay untouched.

Original geometry builder: `tools/own_models/c172_cockpit.py`, integrated into `tools/model_c172.py`. Full aircraft asset: `unity/Assets/_Project/Resources/Models/c172-like/c172-like-own.glb`. Isolated cabin GLB and editable Blender build scene: `renders/c172/v1/c172-interior.{glb,blend}`. Reference photos, source URLs and historical interpretation: `ref/172/README.md`.

This is the requested standard six-pack in a late-172M-style panel, with individual conventional navigation indicators. Early L/M instrument arrangements were offset. It is an original visual interpretation, not a serial-specific restoration. Dimension basis: 1.04m panel width, 80mm primary faces, 102mm primary centres; cabin fit and trim are estimates. ASI face is MPH; altimeter feet; VSI hundreds of feet/min; EGT hundreds of degrees F. Radio digits are static example frequencies in physical windows, not active displays.

## Rebuild and review

```sh
C172_RENDER=1 blender -b --factory-startup --python tools/model_c172.py
python3 tools/own_models/check_c172_cockpit.py
```

Without `C172_RENDER=1`, the same builder exports geometry without rendering. Renders use Cycles CPU, 32 samples, 1800×1200; camera metadata is in `renders/c172/v1/render-manifest.json`. All six views show the authored cabin only; the exterior airframe is absent from these review images. Interior furniture is retained. Pilot and CFI eye positions are the existing configuration eyes converted to the authoring frame. The straight-on panel camera is a review camera. Cabin wide looks aft from above the front panel.

## Coordinate and animation contract

Authoring coordinates are Blender +X forward, +Y left, +Z up in metres. Exported glTF coordinates are +X forward, +Y up, +Z right. Accordingly: table +X → glTF +X; table +Z → glTF +Y; table -Y → glTF +Z. Axis directions in this table are **parent-space**, with mesh rest orientation baked. Unity’s loader/placement may introduce a handedness/root rotation: bind below that root and verify movement sign from the pilot eye before flight. Do not animate using world-space axes.

Capture each node’s rest transform and apply deltas to it. Ranges are visual travel envelopes in degrees or metres, not calibrated aircraft system limits. Needles require simulation-to-dial calibration; do not map airspeed or RPM linearly across a full revolution. The heading bug and cards rotate about their gauge centres. Pedal origins are at their lower hinges; toe brake pivots at the base of the upper pads. Trim-wheel origin is its axle. Sunvisor origins are at their top hinges. Push-pull controls translate along the panel normal.

Dual yoke roll is parented to its pitch column so pitch travel carries the complete yoke. The right yoke/pedals follow the same control signals as the pilot controls; both have independent toe-brake mesh nodes. The TC airplane is electrical, while AI and DG are vacuum instruments. **DG is non-slaved:** maintain a drifting heading state; its sync knob manually adjusts it toward the magnetic compass. Heading bug is independent of DG synchronisation. NAV1 has CDI plus GS and separate validity flags; NAV2 has CDI/course only and no GS node. ADF bearing comes from its own receiver, not either NAV head. DME is its own unit; no invented HSI or glass display is present.

The JSON companion enumerates the same bindings, their original authoring pivots and proposed signal names. Every moving mesh also carries a `rig` custom-property JSON string exported in glTF extras. Proposed signal names are a handoff contract, **not implemented runtime avionics**. Digits/readouts, instrument faults, circuit behaviour, knob interaction and state calibration remain for runtime integration. NAV flag text rotates out of its window for validity changes. AI pitch rotates a hemispheric horizon about its lateral axis inside the separate roll assembly; its authored visual envelope is ±25 degrees.

## Binding table

All node names are exact exported names. All axes below refer to the authoring frame described above.

| Node | Motion | Parent-space axis | Visual delta range | Proposed signal |
|---|---|---|---|---|
| `C172_Airspeed_needle` | rotation | +X forward (Blender) | [-180, 180] | `Airspeed_needle` |
| `C172_AI_roll` | rotation | +X | [-180, 180] | `attitude.roll` |
| `C172_AI_pitch` | rotation | -Y | [-25, 25] | `attitude.pitch` |
| `C172_AI_cage` | rotation | +X forward (Blender) | [0, 360] | `AI_cage` |
| `C172_Altimeter_needle` | rotation | +X forward (Blender) | [-180, 180] | `Altimeter_needle` |
| `C172_Altimeter_1000` | rotation | +X forward (Blender) | [-180, 180] | `Altimeter_1000` |
| `C172_Altimeter_10000` | rotation | +X forward (Blender) | [-180, 180] | `Altimeter_10000` |
| `C172_Altimeter_baro` | rotation | +X forward (Blender) | [0, 360] | `Altimeter_baro` |
| `C172_TC_airplane` | rotation | +X | [-30, 30] | `turn.rate` |
| `C172_TC_slip_ball` | translation | -Y | [-0.015, 0.015] | `slip` |
| `C172_DG_card` | rotation | +X | [0, 360] | `heading.nonSlavedDeg` |
| `C172_DG_heading_bug` | rotation | +X | [0, 360] | `headingBugDeg` |
| `C172_DG_sync_adjust` | rotation | +X forward (Blender) | [0, 360] | `DG_sync_adjust` |
| `C172_DG_bug_adjust` | rotation | +X forward (Blender) | [0, 360] | `DG_bug_adjust` |
| `C172_VSI_needle` | rotation | +X forward (Blender) | [-180, 180] | `VSI_needle` |
| `C172_NAV1_OBS_card` | rotation | +X | [0, 360] | `nav1.obsDeg` |
| `C172_NAV1_CDI` | translation | -Y | [-0.018, 0.018] | `nav1.cdiNormalized` |
| `C172_NAV1_GS` | translation | +Z | [-0.018, 0.018] | `nav1.gsNormalized` |
| `C172_NAV1_OBS` | rotation | +X forward (Blender) | [0, 360] | `NAV1_OBS` |
| `C172_NAV1_NAV_flag` | rotation | +X | [0, 90] | `nav1.NAV` |
| `C172_NAV1_TO_FROM_flag` | rotation | +X | [0, 90] | `nav1.TO_FROM` |
| `C172_NAV1_GS_flag` | rotation | +X | [0, 90] | `nav1.GS` |
| `C172_NAV2_OBS_card` | rotation | +X | [0, 360] | `nav2.obsDeg` |
| `C172_NAV2_CDI` | translation | -Y | [-0.018, 0.018] | `nav2.cdiNormalized` |
| `C172_NAV2_OBS` | rotation | +X forward (Blender) | [0, 360] | `NAV2_OBS` |
| `C172_NAV2_NAV_flag` | rotation | +X | [0, 90] | `nav2.NAV` |
| `C172_NAV2_TO_FROM_flag` | rotation | +X | [0, 90] | `nav2.TO_FROM` |
| `C172_ADF_bearing` | rotation | +X forward (Blender) | [-180, 180] | `ADF_bearing` |
| `C172_ADF_card_adjust` | rotation | +X forward (Blender) | [0, 360] | `ADF_card_adjust` |
| `C172_Tach_needle` | rotation | +X forward (Blender) | [-180, 180] | `Tach_needle` |
| `C172_Suction_needle` | rotation | +X forward (Blender) | [-180, 180] | `Suction_needle` |
| `C172_Oil_press_needle` | rotation | +X forward (Blender) | [-180, 180] | `Oil_press_needle` |
| `C172_Oil_temp_needle` | rotation | +X forward (Blender) | [-180, 180] | `Oil_temp_needle` |
| `C172_Fuel_L_needle` | rotation | +X forward (Blender) | [-180, 180] | `Fuel_L_needle` |
| `C172_Fuel_R_needle` | rotation | +X forward (Blender) | [-180, 180] | `Fuel_R_needle` |
| `C172_Ammeter_needle` | rotation | +X forward (Blender) | [-180, 180] | `Ammeter_needle` |
| `C172_EGT_needle` | rotation | +X forward (Blender) | [-180, 180] | `EGT_needle` |
| `C172_Audio_COM1` | rotation | -Y | [0, 25] | `audio.COM1` |
| `C172_Audio_COM2` | rotation | -Y | [0, 25] | `audio.COM2` |
| `C172_Audio_NAV1` | rotation | -Y | [0, 25] | `audio.NAV1` |
| `C172_Audio_NAV2` | rotation | -Y | [0, 25] | `audio.NAV2` |
| `C172_Audio_ADF` | rotation | -Y | [0, 25] | `audio.ADF` |
| `C172_Audio_SPKR` | rotation | -Y | [0, 25] | `audio.SPKR` |
| `C172_KX170B_1_COMM_coarse` | rotation | +X forward (Blender) | [0, 360] | `KX170B_1_COMM_coarse` |
| `C172_KX170B_1_COMM_fine` | rotation | +X forward (Blender) | [0, 360] | `KX170B_1_COMM_fine` |
| `C172_KX170B_1_COMM_volume` | rotation | +X forward (Blender) | [0, 360] | `KX170B_1_COMM_volume` |
| `C172_KX170B_1_NAV_coarse` | rotation | +X forward (Blender) | [0, 360] | `KX170B_1_NAV_coarse` |
| `C172_KX170B_1_NAV_fine` | rotation | +X forward (Blender) | [0, 360] | `KX170B_1_NAV_fine` |
| `C172_KX170B_1_NAV_volume` | rotation | +X forward (Blender) | [0, 360] | `KX170B_1_NAV_volume` |
| `C172_KX170B_2_COMM_coarse` | rotation | +X forward (Blender) | [0, 360] | `KX170B_2_COMM_coarse` |
| `C172_KX170B_2_COMM_fine` | rotation | +X forward (Blender) | [0, 360] | `KX170B_2_COMM_fine` |
| `C172_KX170B_2_COMM_volume` | rotation | +X forward (Blender) | [0, 360] | `KX170B_2_COMM_volume` |
| `C172_KX170B_2_NAV_coarse` | rotation | +X forward (Blender) | [0, 360] | `KX170B_2_NAV_coarse` |
| `C172_KX170B_2_NAV_fine` | rotation | +X forward (Blender) | [0, 360] | `KX170B_2_NAV_fine` |
| `C172_KX170B_2_NAV_volume` | rotation | +X forward (Blender) | [0, 360] | `KX170B_2_NAV_volume` |
| `C172_KR85_ADF_mode` | rotation | +X forward (Blender) | [0, 360] | `KR85_ADF_mode` |
| `C172_KR85_ADF_tune` | rotation | +X forward (Blender) | [0, 360] | `KR85_ADF_tune` |
| `C172_DME_channel` | rotation | +X forward (Blender) | [0, 360] | `DME_channel` |
| `C172_DME_mode` | rotation | +X forward (Blender) | [0, 360] | `DME_mode` |
| `C172_XPDR_digit0` | rotation | +X forward (Blender) | [0, 360] | `XPDR_digit0` |
| `C172_XPDR_digit1` | rotation | +X forward (Blender) | [0, 360] | `XPDR_digit1` |
| `C172_XPDR_digit2` | rotation | +X forward (Blender) | [0, 360] | `XPDR_digit2` |
| `C172_XPDR_digit3` | rotation | +X forward (Blender) | [0, 360] | `XPDR_digit3` |
| `C172_XPDR_mode` | rotation | +X forward (Blender) | [0, 360] | `XPDR_mode` |
| `C172_Master_BAT` | rotation | -Y | [0, 25] | `Master_BAT` |
| `C172_Master_ALT` | rotation | -Y | [0, 25] | `Master_ALT` |
| `C172_Beacon` | rotation | -Y | [0, 25] | `Beacon` |
| `C172_Landing` | rotation | -Y | [0, 25] | `Landing` |
| `C172_Taxi` | rotation | -Y | [0, 25] | `Taxi` |
| `C172_Nav_lights` | rotation | -Y | [0, 25] | `Nav_lights` |
| `C172_Pitot_heat` | rotation | -Y | [0, 25] | `Pitot_heat` |
| `C172_Mags_key` | rotation | +X forward (Blender) | [0, 360] | `Mags_key` |
| `C172_Primer` | translation | +X forward (Blender) | [0, 0.06] | `Primer` |
| `C172_Carb_heat` | translation | +X forward (Blender) | [0, 0.06] | `Carb_heat` |
| `C172_Throttle` | translation | +X forward (Blender) | [0, 0.06] | `Throttle` |
| `C172_Mixture` | translation | +X forward (Blender) | [0, 0.06] | `Mixture` |
| `C172_Cabin_heat` | translation | +X forward (Blender) | [0, 0.06] | `Cabin_heat` |
| `C172_Cabin_air` | translation | +X forward (Blender) | [0, 0.06] | `Cabin_air` |
| `C172_Flap_switch` | rotation | -Y | [0, 40] | `flap.command` |
| `C172_Flap_indicator` | translation | +Z | [0, 0.023] | `flap.position` |
| `C172_Compass_card` | rotation | +Z | [0, 360] | `magneticHeading` |
| `C172_Yoke_L_pitch` | translation | +X | [-0.07, 0.07] | `elevator` |
| `C172_Yoke_L_roll` | rotation | +X | [-90, 90] | `aileron` |
| `C172_Pedal_L_L` | rotation | -Y | [-18, 18] | `rudder` |
| `C172_Toe_brake_L_L` | rotation | -Y | [0, 15] | `brake.L` |
| `C172_Pedal_L_R` | rotation | -Y | [-18, 18] | `rudder` |
| `C172_Toe_brake_L_R` | rotation | -Y | [0, 15] | `brake.R` |
| `C172_Yoke_R_pitch` | translation | +X | [-0.07, 0.07] | `elevator` |
| `C172_Yoke_R_roll` | rotation | +X | [-90, 90] | `aileron` |
| `C172_Pedal_R_L` | rotation | -Y | [-18, 18] | `rudder` |
| `C172_Toe_brake_R_L` | rotation | -Y | [0, 15] | `brake.L` |
| `C172_Pedal_R_R` | rotation | -Y | [-18, 18] | `rudder` |
| `C172_Toe_brake_R_R` | rotation | -Y | [0, 15] | `brake.R` |
| `C172_Trim_wheel` | rotation | -Y | [-720, 720] | `elevatorTrim` |
| `C172_Trim_indicator` | translation | +Z | [-0.04, 0.04] | `elevatorTrim` |
| `C172_Fuel_selector` | rotation | +Z | [-90, 180] | `fuelSelector` |
| `C172_Seat_adjust_L` | translation | +X forward (Blender) | [0, 0.06] | `Seat_adjust_L` |
| `C172_Seat_adjust_R` | translation | +X forward (Blender) | [0, 0.06] | `Seat_adjust_R` |
| `C172_Door_latch_L` | rotation | +Z | [0, 40] | `door.L.latch` |
| `C172_Window_latch_L` | rotation | +Z | [0, 70] | `window.L.latch` |
| `C172_Door_latch_R` | rotation | +Z | [0, 40] | `door.R.latch` |
| `C172_Window_latch_R` | rotation | +Z | [0, 70] | `window.R.latch` |
| `C172_Sunvisor_L` | rotation | -Y | [0, 100] | `visor.L` |
| `C172_Sunvisor_R` | rotation | -Y | [0, 100] | `visor.R` |
