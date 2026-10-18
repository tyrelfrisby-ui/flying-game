# Known issues

**Last checked against the build of 2026-10-18.** If a report matches something here, say so plainly, say
whether it is being worked on, and do not promise when. If it does not match anything here, it is new — take
it properly and pass it on rather than making something up.

Keep this file current. It is the one in the pack that goes stale fastest, and a confident wrong answer about
a known bug is worse than no answer.

## Flight model: aeroplanes that do not land well in the lessons

Measured over 5,730 scripted runs per pass (`docs/REGIMEN-2026-10-07.md`). These are real and they are the
owner's open decisions, not mysteries:

- **The 737 and the F-86** break in every idle-power flare lesson. A jet is being flown through a light-aircraft
  flare: 148–185 kt and 6–8 m/s of sink at 50 ft. The 737's approach lesson comes apart at 190 kt. Either the
  jets come out of those lessons or a jet flare gets built; not decided.
- **The Hughes H-4** is a flying boat and reports "No touchdown" in every runway lesson, which is correct
  behaviour in the wrong lesson.
- **The Gee Bee R-2** still breaks in the flare lessons. It is very short-coupled and its stability margin is
  deliberately only just positive — that is the aeroplane, and taming it would be a lie about it.
- The **DC-3, P-51 and Beaver on floats** score poorly in the landing lessons rather than breaking. Improving,
  not finished.

If somebody reports one of these, confirm it, and say the aeroplane itself is right — it is the lesson's
judging that does not fit a jet or a flying boat.

## The ground: a few places where it steps vertically

Twelve of these were found and fixed on 2026-10-17 (`docs/INVISIBLE-WALLS.md`). What is left:

- **Six lakes high in the alpine range** — Hidden, Outlet, Shelf, Larch, Cirque and Crown — have a step at the
  shoreline, worst case the water sitting 602 m above its own bed. Known, measured, and the fix is a design
  decision of the owner's.
- **About thirty smaller steps of 4 to 33 m** are known to exist and are not diagnosed. Two clusters: along
  the Valley plateau's own latitude, and east of x = 6,000 in the high desert.

A pilot who flies into invisible ground anywhere else is reporting something new and it matters. Ask **where**:
the nearest airport and roughly which direction and distance, or a screenshot. A coordinate is better than a
description — CODER.md explains how a report becomes a test.

## Points are not being earned yet

The economy is designed and the table exists; **nothing earns, stores or spends a point yet**. If someone asks
why their points stay at zero, that is why, and it is not a fault in their copy. See PURCHASES.md.

## Multiplayer

- **"No wifi router" (peer-to-peer) has never been flown between two real devices.** It compiles and the
  transport under it is tested on sockets. Any report is new information.
- **The relay has no authentication yet**, so the pass is not enforced online (`gateMultiplayer: false`). A
  customer being refused online over the pass today is a bug worth a ticket.
- The **radio frequency** was not sent in shared-network rooms before build 2026.1017: on older builds everyone
  is effectively on 122.80. The answer is to update.

## iPad and iPhone cannot use a USB joystick or RC dongle

Not a bug and not fixable by us — iOS does not give apps access to generic USB HID. See CONTROLLERS.md for the
two things to offer instead. The Bluetooth box that would solve it has no date.
