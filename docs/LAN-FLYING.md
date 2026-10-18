# Flying together with no internet (2026-10-17)

Owner: *"could two ios devices in close proximity do multi player without the network… using the wifi at
their home but not over the internet"*, then *"do that now i want to build the unicast first then the two
ipads in a field then the 3rd party bluetooth i want to be sure the ios devices and mac app can all join in
the same local network with no external internet. we used to play games like this at the dorms at my college
and there were a ton of people on them with low latency so it was very popular and i'm guessing it still
is."*

This is step one of three: **unicast on a shared network**. The two iPads in a field (Apple's peer-to-peer
wifi) and the third-party Bluetooth box are not built yet, and the design below is shaped so they are
transports underneath the same thing rather than rewrites.

## There is no server and no host

`FlyingGame.Core.Lan.LanMesh`. Every aeroplane sends its own state straight to every other aeroplane over
UDP. That is exactly what the relay was doing for us, minus the relay — one wifi hop instead of a round trip
to a datacentre, and nothing of ours is involved, so a room on your own network costs nobody anything.

Nothing above the socket changed. `LanLink` turns what the mesh sees into the same `hello` / `peer` / `bye` /
`s` messages the relay sends, so `NetSession`, `RemoteAircraft`, the name tags and the radio are the code they
already were. The relay and the LAN are two implementations of `INetLink` and the session cannot tell which
one it has, beyond the three rules that only exist because a relay costs money (see below).

The Mac app is in this the same way the phones are: `src/` is a Unity package, so the mesh is one source file
compiled into the iOS build, the macOS build and the test suite alike.

## Finding each other without an entitlement

Since iOS 14, **broadcast and multicast both need `com.apple.developer.networking.multicast`** — a form to
Apple and a provisioning-profile change. So this never broadcasts:

* **the sweep** — one unicast probe to each host of our own subnet, 48 a frame so nothing stalls, and only
  while the room is empty. Unicast needs no entitlement. A subnet wider than 1,024 hosts is swept as the /24
  around us, because a /16 is 65,000 probes and the other pilot is on our own /24;
* **gossip** — an announcement carries the addresses of up to 24 pilots we know, so **one pilot found is the
  whole room found.** This is what makes a dorm-sized room practical: the twentieth pilot to arrive sends one
  probe, gets one answer, and has nineteen addresses;
* **a typed hint** — a full address or just the last octet ("42"), four digits one pilot can read out across a
  room. For an access point that does client isolation one way, or two devices on different subnets.

The local-network permission (`NSLocalNetworkUsageDescription`) was already in the Info.plist for the joystick
link, which is the only permission any of this needs.

## What the tests prove

`tools/FlightTests/LanMeshTests.cs`, eight of them, on **real UDP sockets on loopback** — the thing that
ships, not a mock of it: two pilots find each other with no server at all and pass state through byte for
byte; a third finds the room through one address; a room only hears itself; a pilot who vanishes is noticed
and one who leaves goes at once; a far pilot gets a fifth of the rate; the sweep stops when the room has
somebody in it. Every mesh in the suite is `LoopbackOnly`, because a test that swept the subnet would put a
port scan across the owner's house on every run.

Two bugs they caught, both of which would have been miserable to find on a phone:

* **two clocks.** `Tick(nowS)` took the caller's clock while `LastSeen` was stamped from a static stopwatch
  inside the mesh. The bases do not have to agree: with the caller's ahead, every peer timed out the instant
  it was added, came back as "new" on its next packet, and the event queue never emptied. A class that owns a
  timeout owns the clock it is measured on, so `Tick()` now takes nothing.
* **disposing a socket under a blocking receive.** On Unix, `Socket.Dispose()` waits for a thread sitting in
  `recvfrom()`, and a receive waiting for a datagram that never comes never returns — so leaving a room hung
  the app. The listening thread now has a 250 ms receive timeout and is joined BEFORE the socket is closed.

## The three rules that do not apply here

They are in `NetSession` and they all exist because the relay is billed by the hour:

| rule | on a LAN |
|---|---|
| the online pass gate | not applied — nothing of ours is used. One condition, `IsLanRoom`, and it is the owner's call to change |
| airtime counting | never begins |
| idle eviction at 15 minutes | off: nobody is paying for an aeroplane parked on the ramp |

## Scale

A mesh is N² datagrams a tick: ten pilots is about 25 kB/s each at 10 Hz, twenty-five is 60 kB/s. Nothing for
wifi, not nothing for a phone radio — so a pilot more than 12 km away, which is further than you can see one,
gets state every fifth tick and stays in the roster. They go back to full rate when they come back into
sight.

## No router at all: the field case (2026-10-17, same day)

`unity/Assets/Plugins/iOS/AeroPeer.mm` + `PeerLink.cs`. **MultipeerConnectivity**: the devices build their own
link out of nothing — no access point, no DHCP, no addresses to type, and no sweep, because finding each other
is part of the protocol. Tens of metres, one hop.

It sits behind the same `ILocalLink` as the wifi mesh and synthesises the same `hello` / `peer` / `bye` / `s`
messages, so the only difference above the socket is which object `NetSession` built. The switch is **"No wifi
router"** in the Same-network panel, and it only appears where there is such a thing — the editor and anything
not Apple get a stub that says no.

Details worth keeping:

* **Only one side invites.** Both devices see each other, so the one whose peer name sorts first sends the
  invitation and the other waits. Two crossing invitations make `MCSession` drop one of the connections.
* **The room travels in the discovery info**, and an invitation carries it as its context, so two groups in one
  field do not merge. The service type (`aeroplay`, ≤ 15 characters by Apple's rule) is shared by everyone.
* **State goes unreliable, the radio goes reliable.** A late position is worth less than the next one, and
  head-of-line blocking on a weak link is exactly what makes an aeroplane stutter. A word of radio is not
  replaceable.
* **The pilot's name and aeroplane ride in the peer's display name** (`id|name|aircraft`, 63 bytes), which is
  the one field the framework carries for us — so there is no round of introductions after a connection.
* **`NSBonjourServices` must list `_aeroplay._tcp` and `_aeroplay._udp`** or discovery silently finds nobody on
  iOS 14 and later. Added in `BuildScript.OnPostProcessBuild` alongside the framework.
* The **Mac gets it too**: `tools/native-mac/build.sh` builds the same source into `AeroNative.bundle`, so a Mac
  can be in a field group as well as on the wifi.

Compiled against both the macOS and iOS SDKs. **Not run between two devices yet** — there is no way to test
peer-to-peer wifi from a Mac on its own.

## Still open

* **Not yet flown between two devices.** The transport is tested on sockets and the glue compiles, but no two
  phones have been in a room together yet. First thing to check on a device: the local-network permission
  prompt on first join, and that the sweep finds anybody at all on a real home access point.
* **The two iPads in a field** is built (above) but unflown. The things to watch on a device: that the
  invitation tie-break really does leave one connection rather than none, and what the range is in practice.
* **The third-party box**: an M5Stack ESP32 in SoftAP mode is a pocket router — both devices join its wifi and
  this code works unchanged, with the ESP answering a "who's here" probe. About an hour of Arduino, and it is
  the same box as the RC link, so one unit could carry the radio and host the match.
* ~~Voice over a LAN is sent to every pilot in the room rather than only those on the frequency; it is
  filtered on arrival.~~ It was not filtered at all: the filter in `VoiceComms.OnVoice` compares the packet's
  frequency against ours and passes a packet that has none, and only the RELAY ever stamped one — so in a LAN
  room every pilot heard every other pilot whatever any of them had the box tuned to. The radio did nothing.
  Fixed 2026-10-17 by stamping the frequency on the packet at the transmitter, where it is known.
* Voice is still SENT to every pilot on the network and discarded by the ones not on the frequency, which the
  relay does not do — it sends a call only to the pilots it belongs to. At a dorm's scale that is the
  bandwidth worth saving: twelve pilots on three frequencies move four times the voice traffic they need to.
  Narrowing it needs the mesh to carry each peer's frequency and a send that takes a list of peers, which the
  unicast transport can do (`LanMesh` already addresses each peer individually) and the field link can too
  (`sendData:toPeers:`). Not done.
