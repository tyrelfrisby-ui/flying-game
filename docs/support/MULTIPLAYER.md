# Flying together, and what goes wrong

There are **three** ways to fly with somebody, and they fail for completely different reasons. Establish which
one they are using before troubleshooting anything. The choice is on the landing page, under the aircraft and
start settings.

| | What it uses | Needs the internet | Needs the Pass |
|---|---|---|---|
| **Online** (public or private room) | our relay, `wss://mp.aeroplayground.app` | Yes | Yes (see PURCHASES.md) |
| **Same network** | plain Wi-Fi, device to device | No | **No** |
| **Same network → "No wifi router"** | Apple peer-to-peer, no access point at all | No | **No** |

Two things are worth saying out loud to a customer, because they are unusual and they are both in our favour:
flying together on your own Wi-Fi **costs nothing and needs no subscription**, because nothing of ours is in
the middle; and **nothing about any of it is stored anywhere but on the device**.

## Online rooms

- **A private room is a 6-letter/digit code.** One person taps **Create** and reads the code out; everybody
  else types it. Codes are case-insensitive as typed and the field only accepts letters and digits.
- **A challenge, race or lesson always flies solo.** This is by design, not a fault: picking a challenge puts
  the session into single player. If somebody says "we joined the same room but can't see each other", ask
  whether either of them has a challenge or lesson selected. This is the most common online report.
- **"It says I need the pass."** Check PURCHASES.md. Note that the gate is currently **off** in shipped builds
  (`gateMultiplayer: false`), so a refusal over the pass should not be happening at all today — if somebody is
  seeing one, that is a real bug and worth a ticket.
- A corporate, school or hotel network that blocks WebSockets will fail here. Same network (below) is the
  answer, or a phone hotspot.

## Same network

Both devices on the same Wi-Fi. No code to type, usually nothing to configure — the devices find each other by
asking each host on the subnet directly, and once **one** pilot is found, their announcement carries up to 24
other pilots' addresses, so joining a big group costs one question and one answer.

In order, the things that actually go wrong:

1. **The local-network permission.** iOS asks for it once, on the first join. If it was refused, discovery
   silently finds nobody. Fix: iOS Settings → Aero Playground → Local Network, on. There is no way for the app
   to ask again.
2. **Client isolation on the access point.** Common on hotel, café, campus and some mesh Wi-Fi: devices can
   reach the internet but not each other. If one device can be reached but not the other (isolation is often
   one-way), the **typed hint** fixes it: in the Same-network panel, type the other device's address, or just
   its last number — four digits one pilot can read across a room. Otherwise: a phone hotspot, or "No wifi
   router" below.
3. **Different subnets.** Two bands of the same mesh sometimes are. The typed hint handles this too, with the
   full address rather than the last number.
4. **A very large network.** A subnet wider than 1,024 hosts is only searched around the device's own
   neighbourhood, because asking 65,000 hosts is not practical. On a big campus network, use the typed hint.

## No wifi router (a field, a ridge, an aeroplane)

The switch **"No wifi router"** in the Same-network panel. The devices build the link themselves with Apple's
peer-to-peer Wi-Fi: no access point, no addresses, nothing to type, and finding each other is part of it. Tens
of metres. It appears only on iPhone, iPad and Mac — nowhere else has it.

> **This has never been flown between two real devices.** It compiles and the transport underneath is tested,
> but no two phones have been in a room together yet. Treat any report about it as new information and worth a
> ticket, not as the customer having done something wrong. Ask: did the permission prompt appear on the first
> join, and did each device see the other's name at all?

## The radio and the intercom

- **Intercom** is voice-activated and you hear yourself — headphones, or it will howl.
- **Radio** is hold-to-talk, and it is **per frequency**: you only hear pilots tuned to the same one. If two
  pilots in the same room cannot hear each other, the frequency is the first thing to check
  (OPTIONS → INTERCOM & RADIO).
- The frequency was not being sent over a shared-network room in builds before **2026.1017**, so on an older
  build everybody is effectively on 122.80. If they are on an older build, the answer is to update.
