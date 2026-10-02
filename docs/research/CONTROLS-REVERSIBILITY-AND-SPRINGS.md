# Primary flight controls: reversibility, springs, balance and free-float behavior

Research date: 2026-10-02. Prepared for the Aero Playground flight model (control-loading / free-surface / hinge-moment work).

## How to read this

- **Reversible (R)**: mechanical cables, pushrods or torque tubes connect the stick/yoke/pedals to the surface. Air loads back-drive the controls. Let go and the surface trails to its float angle, and the stick follows.
- **Irreversible (I)**: a hydraulic or electric actuator holds the surface. Air loads cannot reach the cockpit. Any feel comes from springs, bobweights or a q-feel unit. Let go and the surface holds wherever the feel spring's neutral (the trim point) puts it.
- **Springs**: centering, bungee, down/up-spring, spring trim (adjustable-neutral spring) and interconnect springs.
- **Confidence**: H = primary or regulatory source (POH, TCDS, flight manual, AD). M = several secondary sources agree, or the source is an engineering/owner reference. L = inferred from the design class or from forums. These are flagged in the text.
- **Sources** use the numbers in the source list at the bottom.

## Summary table

| Aircraft | Aileron | Elevator / stabilator | Rudder | Springs (axis, character) | Balance / tabs | Conf. | Sources |
|---|---|---|---|---|---|---|---|
| PA-18 Super Cub (land/bush) | R, cables | R, cables | R, cables | **Pitch:** none in the elevator circuit. Trim is an **adjustable stabilizer** on a jackscrew, so the elevator's free-float angle is set by stab incidence, not by a spring. **Yaw:** tailwheel steering springs/chains from the rudder horn to the tailwheel arms give weak centering, mostly on the ground. | Horn-balanced rudder and elevator tips. No tabs. | M | 3, 4, 5 |
| PA-18 on floats | R | R | R | As above. The water rudders are cable- and spring-linked to the air rudder, which adds a little spring load on the water. Retracted in flight, so no effect in the air. | As above | M/L | 3, 4 |
| PA-25 Pawnee | R, cables | R, cables | R, cables | Pitch trim very likely a PA-18-style adjustable-stabilizer jackscrew (**unverified**: found Piper stab-jackscrew parts and service discussions but not a PA-25 POH statement). No centering springs known. Tailwheel steering springs on the rudder. | Horn-balanced surfaces (Cub lineage) | L/M | 6, 7 |
| PA-28-181 Archer (new) | R, cables | R, cables, **all-moving stabilator** | R, cables | **Yaw:** a "spring device in the rudder pedal torque tube" gives rudder trim (a spring-trim knob on the pedestal), and the pedals are rigidly linked to nosewheel steering (20° each way on later serials). Expect a noticeable spring-centered pedal. **Pitch:** none; the anti-servo tab provides the feel. **Roll:** none. | Stabilator **anti-servo tab** (TCDS: stab 14° up / 2° down, tab 3° up / 12° down). It moves with the trailing edge, adds stick force and drives the free stabilator toward the trim angle. | H | 8, 9 |
| Cirrus SR22 (new) | R, cables/pushrods, side yoke | R, cables/pushrods | R, cables | **Every axis is spring-loaded.** Pitch and roll trim are electric motors that move the neutral of **spring cartridges**; there are **no trim tabs**. Yaw has a spring "yaw trim cartridge" (ground-adjustable on most serials). A **rudder-aileron interconnect bungee/spring** links aileron and rudder cables. Expect strong centering: the yoke is always pulled toward the spring neutral. | No trim tabs. Elevator and rudder carry mass balance. | H/M | 10, 11, 12, 13 |
| Cessna 172 | R, cables | R, cables | R, cables | **Yaw:** spring **steering bungees** between the rudder bars and the nose gear. In the air the nose oleo extends onto a centering cam, so the bungees act as weak rudder centering springs. **No rudder-aileron interconnect** on the 172; the task's premise was wrong. **Pitch:** no spring. Trim is a cable-driven tab. | Frise-type differential ailerons. Horn-balanced elevator tips and rudder top. Elevator trim tab. | H/M | 14, 15 |
| PA-44 Seminole | R, cables | R, cables, T-tail **stabilator** | R, cables | **Yaw:** rudder trim (Piper practice is a rudder tab on the PA-44; **verify**: checklists confirm "rudder trim" but not whether it is a tab or a spring) plus nosewheel steering linked to the pedals through a bungee. **Pitch:** anti-servo tab, which also trims. | Stabilator anti-servo tab | M | 16, 17 |
| ACA 8KCAB Decathlon | R, cables | R, cables | R, cables | No centering springs known. Tailwheel steering springs. | **Elevator trim tab** (pilot-adjustable). **Fixed, ground-adjustable rudder tab.** Optional **aileron spades**: stock ailerons are heavy, and spades lighten them a lot. | M | 18, 19 |
| Extra 300 | R, pushrods/torque tubes | R, pushrods | R, cables | No springs. | Big **aileron spades** give very light, near-neutral roll force. **Cable-driven trim tab on the right elevator.** Horn-balanced elevator and rudder. | M | 20, 21 |
| Pitts S-2B | R, pushrods; ailerons on both wings joined by an interplane strut | R, pushrod/cable | R, cables | No centering springs known. Pitch trim: **unverified** whether it is a trim tab or a spring bias. Some Pitts (especially homebuilt S-1s) use spring/bungee bias trim. | **Aileron spades on the S-2B** because the ailerons are not finely balanced, so forces are unequal by direction. The S-2C has re-balanced ailerons and no spades. | M/L | 22, 23 |
| Boeing-Stearman PT-17 | R, cables; ailerons on the **lower wing only** | R, cables | R, cables | No centering springs. Trim: in-flight **adjustable horizontal stabilizer** (crank). Moderately confident; one web hit describing an elevator tab with a hand wheel looked like a different, British type. | Horn-balanced rudder and elevator. No servo tabs. | M/L | 24, 25 |
| Gee Bee R-2 (1932) | R, cables/rods | R | R | None known. Assume no springs. | Unknown. Assume an adjustable stabilizer or a ground-bent tab. Airfoil NACA M-6 (8%). | L | 26 |
| Cassutt F1 racer | R, pushrod/cable | R, pushrod | R, cables | None, or a simple spring bias trim. | Small surfaces, likely a ground-adjustable tab | L | 26 |
| Glasair III | R, pushrods | R, pushrods | R, cables | Pitch trim is an **electric servo-driven trim tab** inside the elevator (factory kit). Aileron trim, if fitted, is ground-adjustable or a spring bias. | Elevator trim tab | M/L | 27 |
| Schweizer SGS 2-33 | R, cables | R, cables | R, cables | **Pitch: spring trim on the stick.** S/N < 500 used a bungee trim; S/N ≥ 500 use a ratchet-lock lever acting on a **spring cartridge**. There is no trim tab. The free elevator floats to the hinge-moment balance; the stick sits where the spring and hinge moment balance. | None | H | 28 |
| Swift S-1 | R, pushrods | R, pushrods | R, cables | **Pitch: spring trim.** The parts book shows one spring and some ships have two in the elevator trim, so it is a spring-bias trim, not a tab. | Frise-type ailerons | M | 29, 30 |
| EB29R (Open Class) | R, CFRP pushrods | R, pushrods (automatic hookup) | R, cables | **Pitch: electrically adjusted trim**, switch on the stick. Typical for Binder/Schempp-Hirth: a motor moves a **spring** neutral, not a tab. Treat as spring trim (**inferred**). | Flaperons (6-part wing); wing profile DFVLR HQ17 → DU84-132/V3 | M/L | 31, 26 |
| North American P-51D | R, cables (no boost) | R, cables | R, cables | **Pitch: elevator bobweight** (added for dive stick-force reversal) adds about +1 g's worth of stick force per g. Owner reports put it near **20 lb/g**; some owners cut the bobweight in half. No centering springs. | **Trim tabs** on the elevators, the rudder and the left aileron. Takeoff trim: rudder 5° R, elevator 2–3° NU. Aileron boost (servo) tabs exist only as a non-stock modification. Measured stick force about **48 lb in a 3 g pull, 86 lb at 5 g**, so it is a "two-hander". | M | 32, 33, 34 |
| Douglas DC-3 / C-47 | R, cables | R, cables | R, cables | No feel springs. **Gust locks** on the ground. | **Frise ailerons. Overhang-balanced elevator and rudder. Trim tabs on the rudder, both elevators and the right aileron.** Heavy forces at speed. | H | 35 |
| North American F-86F | **I**, hydraulic | **I**, hydraulic all-flying tail | **R**, cables, electric rudder trim tab | **Pitch and roll: artificial-feel spring bungees.** Trim moves the bungee neutral (no aerodynamic trim tab on the tail or ailerons). Pitch also has a **bobweight** plus leaf springs at the tail valve lever for stick force per g. Normal plus alternate hydraulic systems; **no manual reversion** for pitch/roll on the F-86E/F. (The F-86A had boosted, reversible controls.) | Rudder trim tab | H | 36, 37 |
| Boeing 737 (Classic/NG) | **I**, hydraulic; manual reversion available | **I**, hydraulic; manual reversion available | **I**, hydraulic (A, B, standby), **no manual reversion** | **Aileron feel and centering unit** (springs); aileron trim shifts its neutral. **Elevator feel computer** (q-feel from pitot and stab position) plus a feel and centering unit. **Rudder feel and centering unit** (springs); rudder trim shifts its neutral. | Ailerons and elevators have **balance tabs** for manual reversion. The control tabs are locked out when hydraulics are on. Manual-reversion forces are high. | H/M | 38, 39 |
| Small jet target drone | I (servo) | I (servo) | I (servo) or none | None. The servos hold the commanded position. | n/a | Assumption | — |

## Per-aircraft notes and free-float behavior

### General free-float physics (all reversible types)

When a reversible control is released, the surface rotates until its aerodynamic hinge moment, plus any tab moment, plus any spring/bungee/bobweight moment about the hinge, sums to zero. The stick or yoke moves with it. Rules a flight model can encode:

- **Elevator (conventional tail)**: hinge moment coefficient `Ch = Ch0 + Ch_alpha·alpha_t + Ch_delta·delta_e + Ch_tab·delta_tab`. Typical light aircraft have Ch_alpha < 0 and Ch_delta < 0, so a free elevator **trails with the local tail flow**. At positive tail angle of attack it floats trailing-edge **up**, which reduces stick-free stability (the "stick-free neutral point" lies ahead of the stick-fixed one). The trim tab sets the angle at which the elevator floats, and **"trimmed" means "free-float angle = required elevator angle"**.
- **Ailerons**: a pair of free ailerons on a lifting wing both **float trailing-edge up** under the upper-surface suction (an equal Ch_alpha on each side). This is a symmetric "reflex" that slightly reduces lift. The cable loop prevents opposite floating, so in practice the pair floats up to the limit of cable stretch and slop. In sideslip or roll the asymmetric alpha produces a net wheel/stick torque, which is the classic "stick moves to the low wing" feel. Frise ailerons and spades push the float toward neutral and lighten forces. Spades can over-balance at large deflection.
- **Rudder**: in sideslip, a free rudder **weathervanes with the local flow**, trailing downwind and reducing directional stability. Horn balances reduce the float tendency. Centering springs (Cessna steering bungees, SR22 yaw cartridge, PA-28 spring trim) pull it back toward neutral.
- **Stabilator with anti-servo tab (PA-28, PA-44)**: a bare stabilator hinged near its quarter-chord would be close to neutral or even unstable in hinge moment. The anti-servo tab deflects **the same direction** as the stabilator trailing edge (TCDS lists stabilator 14° up / 2° down and tab 3° up / 12° down. The tab range combines anti-servo gearing and trim travel, so the gearing ratio is not given directly; about 0.5–1 tab-deg per stab-deg is a reasonable model value). This produces a restoring hinge moment proportional to deflection from the trimmed position. Released, the stabilator **returns to the trim angle** with a firm, roughly linear force. Model it as a stiff aerodynamic spring whose stiffness scales with dynamic pressure q.
- **Spring-trim aircraft (SR22, 2-33, Swift, EB29R, F-86 bungees)**: the spring's neutral is the trim point. Released, the stick goes to the point where spring torque balances the aerodynamic hinge moment, which is **not** zero hinge moment. On reversible spring-trim gliders the stick drifts slightly with speed (spring trim is speed-stable only near the trim speed). On the irreversible F-86 the stick goes exactly to the spring neutral and the surface follows it.
- **Bobweight (P-51D, F-86)**: adds a stick force proportional to n (load factor) and, for elevator-free motion, a nose-down moment under positive g. It shifts the free-float elevator angle with g.

### Piper PA-18 Super Cub (incl. floats, bush)
- Pitch trim: **adjustable horizontal stabilizer** driven by a jackscrew and yoke from an overhead crank (Univair parts catalog, owner discussions). **There is no elevator trim tab and no trim spring.** Trimming changes stabilizer incidence. The free elevator then floats in line with the tail flow at the new incidence.
- Surfaces are fabric-covered with horn-balanced tips (elevator and rudder). Forces are light at low speed. The Cub is known for heavy, low-authority ailerons and significant adverse yaw.
- Rudder: cables to the rudder horn. Tailwheel steering springs (leaf/coil plus chain) connect the horn to the tailwheel steering arms, so there is light centering. On floats the water rudders are cable-linked to the rudder via springs and retracted in flight.
- Bush mods (VGs, big tires, extended baggage) do not change the control system.

### Piper PA-25 Pawnee
- Cub-family structure and controls (cables, horn balances). Piper's PA-25 elevator cable links are subject to AD PA-25/20 (elevator cable link assembly), which confirms a cable elevator. The trim type was **not confirmed from a primary source**. The best evidence is Cub-lineage parts that point to an adjustable stabilizer. **Action: verify against a PA-25 POH/parts catalog before modeling trim as stab-incidence.**

### Piper PA-28-181 Archer
- POH §7.9: "Dual controls... cable system between controls and surfaces. Horizontal tail (stabilator) is of the all-movable slab type with a trim tab mounted on the trailing edge to reduce control forces... actuated by a control wheel on the floor between the front seats." "A rudder trim adjustment is mounted on the right side of the pedestal." §7.7: "A spring device is incorporated in the rudder pedal torque tube assembly to provide rudder trim. By using the rudder pedals and brakes, the nose gear is steerable through a 20° arc." [8]
- TCDS 2A13 control travels (Archer III): stabilator 14° up / 2° down (±1°); stab tab 3° up / 12° down; ailerons 25° up / 12.5° down; rudder 28° L/R; flaps 10/25/40°. [9]
- Feel: the anti-servo tab gives a firm, linear, q-scaled pitch force with a **strong return to the trimmed stabilator angle** when released. Pedals are spring-centered by the rudder trim spring and stiffly linked to the nosewheel. No rudder gust lock is needed on PA-28s for that reason (pilot-forum observation [17]).

### Cirrus SR22
- No trim tabs. **Pitch** trim: the elevator pushrod has two springs attached to a center piece that an electric motor drives fore and aft. The motor "only sets the zero position of the elevators" (owner-forum description [10]). **Roll** trim: an electric spring cartridge on the aileron circuit, the same principle. **Yaw**: a "yaw trim cartridge" (spring cartridge, with a self-locking nut subject to a CASA AD and SB [12]). **Rudder-aileron interconnect**: a bungee between the rudder and aileron cables, accessed under the rear floor. It exists because the low dihedral did not meet the FAA's control-with-rudder-alone requirement (Cirrus forum [11]). It was subject to a fleet-wide mandatory SB and AD for possible jamming [13, 40].
- Feel: light at low q but always spring-centered. Released, the yoke returns to the spring neutral, and the surfaces settle where spring torque equals the hinge moment. Aileron input also produces a small rudder deflection through the interconnect, and pedal input produces a small aileron deflection.

### Cessna 172
- All cable. Elevator trim tab on the right elevator, cable-driven from the trim wheel. Nosewheel steering uses spring **steering bungees** between the rudder bars and the nose strut (about ±10° by pedal, more with differential braking). In the air the extended oleo locks the nosewheel on a centering cam, so the bungees become mild **rudder centering springs** [14, 15]. Frise differential ailerons. Horn balances on the elevator tips and the rudder top.
- **Correction to the task premise:** the 172 has **no** rudder-aileron interconnect spring. (Interconnects are found on the Ercoupe, Tri-Pacer, SR20/22 and others, not on the 172.)

### Piper PA-44 Seminole
- T-tail stabilator with an anti-servo/trim tab, mechanically similar to the PA-28 (Wikipedia and type references note "mechanical flight controls"; checklists set "stabilator and rudder trim neutral") [16, 17]. Rudder trim implementation (tab vs. spring) not confirmed from a primary source here. Nosewheel steering linked to the pedals.

### ACA 8KCAB Decathlon
- Cables throughout. Pilot elevator trim tab and a fixed ground-adjustable rudder tab [18]. Stock ailerons are heavy and less effective. Factory **aileron spades** "correct this" and are "worth the investment" (AvWeb used-aircraft guide [19]). Wing NACA 1412 (Lednicer [26]).

### Extra 300
- Elevator trim tab on the right elevator's inboard trailing edge, actuated by two cables (UK AAIB report [20]). Rudder by cables. Ailerons and elevator by pushrods. Large aileron spades give near-zero breakout and very light roll forces (14.5–20 lb at full rate is typical in owner reports; **forum-grade**). Wing MA 15S root / MA 12S tip (symmetric).

### Pitts S-2B
- Four ailerons on two wings, joined by an interplane strut. The S-2B needs **spades** because the ailerons are not finely balanced: stick force differs left versus right. The S-2C's ailerons are balanced and need no spades (AOPA fact sheet [22]). Elevator trim mechanism not confirmed.

### Boeing-Stearman PT-17
- Ailerons on the lower wing only. Cables throughout. Pitch trim assumed to be an adjustable stabilizer via crank (**medium-low confidence**). Wing NACA 2213.

### Gee Bee R-2, Cassutt F1
- No reliable controls documentation found. Model them as plain reversible cable/pushrod controls with no springs. Gee Bee: tiny, highly sensitive surfaces (wing NACA M-6 8%). Cassutt: Cassutt 1107 section.

### Glasair III
- Pushrod ailerons and elevator, cable rudder. Factory electric trim is a servo inside the elevator driving the trim tab through a rigid pushrod (Glasair owners documentation [27]). Wing NASA GA(W)-2 mod.

### Gliders (2-33, Swift S-1, EB29R)
- **2-33**: the manual says S/N ≥ 500 have a **ratchet-lock trim (P/N 33140G)** with a spring cartridge, superseding the earlier **bungee-type** trim. Pilot squeezes the lever, moves the stick to the desired speed, and releases [28]. The trim acts on the stick through springs; **there is no tab**. Wing NACA 43012A.
- **Swift S-1**: pushrods. Elevator trim uses one or two springs at the bellcrank, so it is spring trim [29, 30]. Frise-type ailerons. ±10 g / −7.5 g (cert +10/−7.5). Wing NACA 64-412.
- **EB29R**: electrically adjustable trim with the switch on the stick, CFRP control rods, automatic elevator hookup [31]. Wing 28 m, 14.9 m², AR 52.6, L/D about 66.

### North American P-51D
- Fully manual, reversible cable system. Trim tabs on the elevators, rudder and left aileron, set from a trim-wheel console by the left knee [32, 34]. Takeoff trim: rudder 5° R; elevator 2–3° NU (≤25 gal fuselage tank) or 1–3° ND (full); aileron 0°. An **elevator bobweight** was added to cure stick-force reversal and resulting wing failures in high-speed dives, and the aircraft is only marginally stable with more than about half the fuselage tank [33]. Stick force about 48 lb at 3 g and 86 lb at 5 g at corner speed (C.C. Jordan via yarchive [33]); aileron about 20–25 lb at 200–250 kt. Aileron "boost" servo tabs are a non-stock modification [32]. Wing NAA/NACA 45-100.

### Douglas DC-3 / C-47
- Cables throughout. NASA flight test of the C-47B: overhanging aerodynamic balance on the elevator and rudder, Frise ailerons, and trim tabs on the rudder, both elevators and the right aileron [35]. Ground gust locks. Free surfaces trail with the flow; the big fabric rudder slams to the stop in a ground gust without the lock.

### North American F-86F
- From the DCS F-86F manual, derived from T.O. 1F-86F-1 [36]. Ailerons and the all-flying tail are **irreversible hydraulic**: "aerodynamic loads of any kind cannot reach the pilot through the stick." Feel comes from **spring bungees** proportional to stick deflection from the trim position. The pitch channel adds a **bobweight** with leaf springs for g-feel. Trim **repositions the bungee neutral**. The rudder is **mechanical (cables/struts)** with an electric trim tab. *Caveat:* the DCS manual also claims the feel-spring stiffness varies with airspeed. That is not in the period documentation I could check, so treat it as a DCS modeling choice. History: the F-86A used hydraulically boosted (still reversible) controls; the F-86E introduced the irreversible all-flying tail [37].

### Boeing 737 (Classic/NG)
- Ailerons: hydraulic A and B. The **aileron feel and centering unit** provides spring feel, and aileron trim electrically repositions it. Manual reversion is available: the wheel drives **balance tabs** that fly the ailerons, with high forces.
- Elevator: hydraulic. The **elevator feel computer** (q from elevator pitots plus stab position) drives the feel and centering unit. Manual reversion is available through the elevator balance tabs.
- Rudder: hydraulic (A, B, standby PCU). The **rudder feel and centering unit** is a spring with inner and outer springs; a broken inner spring causes low pedal force (CASA AD). Rudder trim repositions the centering unit. **No manual reversion.**
- Sources [38, 39] (FAA/EASA ADs, training references).

### Target drone
- Assumption: electromechanical servos, irreversible, no feel. Model surfaces as rate-limited actuators holding commanded positions, with optional blow-back (reduced deflection at high q from limited servo torque).

## Suggested spring/feel parameters for the game (estimates, not sourced data)

| Type | Breakout | Gradient at full deflection | Notes |
|---|---|---|---|
| Cub/Decathlon/Stearman (aerodynamic only) | ~0.5–1 lb | q-dependent, about 5–15 lb pitch at cruise | No spring; free float = trail |
| PA-28/PA-44 stabilator | ~1 lb | about 20–35 lb pull at full aft, landing speed | Anti-servo tab gives a q-scaled centering "spring" toward trim |
| C172 rudder | ~3–5 lb | ~30–50 lb pedal at full throw (bungee + air) | Weak spring plus air load |
| SR22 pitch/roll cartridges | ~2–3 lb | spring ~10–15 lb at full yoke plus q load | Strong return to the spring neutral at low speed |
| Glider spring trim | ~0 | spring of a few lb | Stick drifts with speed |
| F-86 bungee | small | constant spring (not q) plus bobweight about 3–5 lb/g | Surface holds when released |
| 737 q-feel | detent | q-scheduled | Surface holds; force grows with speed |

These are game-design starting points inferred from the mechanisms above and pilot-reported impressions. They are **not** measured data.

## Sources
3. Univair PA-18 Parts Catalog, stabilizer adjustment pages: https://www.univair.com/content/partcatalog/18PM-flipbook/files/basic-html/page158.html, https://www.univair.com/content/partcatalog/18PM-flipbook/files/basic-html/page161.html
4. SuperCub.org trim threads (owner forum): https://www.supercub.org/forum/threads/j-3-trim-problems.58115/post-782134
5. Short Wing Pipers, stab trim jackscrew (owner forum): https://www.shortwingpipers.org/forum/threads/stab-trim-jackscrew-assembly-play.15715/latest
6. CASA AD/PA-25/20 Elevator Cable Link Assembly: https://services.casa.gov.au/airworth/airwd/ADfiles/UNDER/PA-25/PA-25-020.pdf
7. NZ CAA PA-25 ADs: https://www.aviation.govt.nz/assets/aircraft/airworthiness-directives/aeroplanes/pa25.pdf
8. Piper PA-28-181 Archer III POH VB-2266 (2013), §1, 2, 4, 7: https://www.andrews.edu/cp/aviation/resources/archeriii-poh.pdf
9. FAA TCDS 2A13 Rev 48 (PA-28 series): https://touringmachine.com/Cherokee/PDFs/Cherokee%20Type%20Certificate%20Data%20Sheet%202A13.pdf
10. EuroGA forum, Cirrus pitch trim (owner forum): https://www.euroga.org/forums/flying/10810-cirrus-no-pitch-trim-wheel?page=3
11. COPA forum, aileron-rudder interconnect (owner forum): https://forum.cirruspilots.org/t/aileron-and-rudder-interconnect/280504
12. CASA AD, Roll and Yaw Trim Cartridges self-locking nuts: https://www.casa.gov.au/index.php/node/182762
13. FlightGlobal, interconnect jam recall: https://www.flightglobal.com/control-jam-spurs-another-cirrus-full-fleet-recall/73627.article
14. Transport Canada Service Difficulty Advisory, Cessna 150/172/175 steering tube: https://tc.canada.ca/en/aviation/reference-centre/civil-aviation-safety-alerts/service-difficulty-advisories/cessna-150-172-175-steering-tube-pn-0543022-xx-service-difficulty-advisory
15. SimTuts C172S landing gear (training reference): https://simtuts.com/type-rating/c172/landing-gear-and-brakes
16. Wikipedia, Piper PA-44 Seminole: https://en.wikipedia.org/wiki/Piper_PA-44_Seminole
17. MGA Seminole and Archer checklists: https://ce.mga.edu/aviation/knight-flight/aircraft-information-procedures/docs/Seminole_Checklist.pdf; PoA "Archer vs C172 controls" (forum): https://pilotsofamerica.com/community/threads/archer-vs-c172-flight-controls.95131
18. Bellanca-Champion Club, 8KCAB elevator trim: https://bellanca-championclub.com/forum/threads/8kcab-elevator-trim-question-problem.1845
19. AvWeb Used Aircraft Guide, Decathlon & Citabria: https://avweb.com/ownership/used-aircraft-guide-decathlon-and-citabria/
20. UK AAIB, Extra EA-300/L G-ZXEL: https://assets.publishing.service.gov.uk/media/6409cf258fa8f55609b1413f/Extra_EA_300-L_G-ZXEL_04-23.pdf
21. Wikipedia, Extra EA-300: https://en.wikipedia.org/wiki/Extra_EA-300
22. AOPA, Aviat Pitts S-2C fact sheet: https://AOPA.org/go-fly/aircraft-and-ownership/aircraft-fact-sheets/aviat-pitts-s2c
23. KitPlanes, "Design Process: Pitch Force Trim" (spring trim background): https://www.kitplanes.com/design-process-pitch-force-trim/
24. Air Corps Aviation, Stearman Airframe Parts Catalog: https://www.aircorpsaviation.com/wp-content/uploads/StearmanAirframePartsCatalogDigital.pdf
25. (Stearman trim: no primary source found; flagged.)
26. Lednicer, *The Incomplete Guide to Airfoil Usage*: https://m-selig.ae.illinois.edu/ads/aircraft.html
27. Glasair owners, electric trim instructions: https://glasair-owners.com/download/063-09001-01-glastar-electric-trim-instructions/
28. Schweizer SGS 2-33A Flight-Erection-Maintenance Manual: https://www.faasafety.gov/files/events/GL/GL09/2018/GL0983174/schweizer_2_33_manual.pdf
29. EASA TCDS Swift S-1: https://www.easa.europa.eu/en/downloads/7448/en
30. BGA Swift S-1 info: https://www.gliding.co.uk/wp-content/uploads/sites/3/2015/04/1430312150_swifts1.pdf
31. Binder Flugmotorenbau, EB29R: https://binder-flugmotorenbau.de/eb290.html?&L=1
32. Smithsonian Air & Space, "How Things Work: Trim Tabs"; WIX forum (boost tabs): https://www.smithsonianmag.com/air-space-magazine/how-things-work-trim-tabs-129376547/, https://warbirdinformationexchange.org/phpBB3/viewtopic.php?p=630952
33. yarchive, P-51 posts (C.C. Jordan et al.): https://yarchive.net/mil/p51.html
34. A2A forum quoting the P-51 manual (takeoff trim): https://a2asimulations.com/forum/viewtopic.php?p=233308
35. NASA NTRS, C-47B flight-test report: https://ntrs.nasa.gov/api/citations/19930083829/downloads/19930083829.pdf
36. DCS F-86F Flight Manual (Eagle Dynamics), §Flight Controls / Artificial-feel system: https://Digitalcombatsimulator.com/upload/iblock/5c6/DCS%20F-86F%20Flight%20Manual%20EN.pdf
37. GlobalSecurity F-86; AirVectors F-86: https://www.globalsecurity.org/military/systems/aircraft/f-86.htm, https://www.airvectors.net/avf86_2.html
38. CASA AD, 737 Rudder Feel and Centering Unit: https://www.casa.gov.au/index.php/node/169371; FR 2009-09-09 737 AD: https://www.govinfo.gov/content/pkg/FR-2009-09-09/html/E9-21412.htm
39. 737NG flight controls (training): https://www.slideshare.net/slideshow/b737-ng-flight-controls/26849036
40. FAA AD, Cirrus interconnect (Federal Register): https://www.govinfo.gov/content/pkg/FR-2007-06-21/pdf/E7-12006.pdf
