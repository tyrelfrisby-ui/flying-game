# flyinggame-relay

Cloudflare Worker + Durable Object that relays pilot state packets between players in a room.
One DO per room (`ffa` = the giant free-for-all, cap 200; private rooms = 6-char codes, cap 16).
No auth, no persistence. Uses the WebSocket Hibernation API so idle rooms cost nothing.

## Endpoints

| Method | Path | Purpose |
| --- | --- | --- |
| `POST` | `/rooms` | `{ code }` — fresh private room code (clients may also make their own) |
| `GET` | `/rooms/{code}` | `{ code, pilots, cap }` — roster size |
| `GET` | `/rooms/{code}/ws?name=Pilot&ac=c172-like` | WebSocket upgrade; `409 room full` when at cap |

## Protocol (JSON text frames, 10 Hz)

Client → server

- `{ "t":"join", "name":"…", "ac":"…" }` — optional; name/aircraft also arrive via the query string.
  Sending it again after connect updates them (aircraft switch) and re-announces you as `peer`.
- `{ "t":"s", "p":[x,y,z], "q":[x,y,z,w], "v":[x,y,z], "d":[ail,ele,rud,spoil,flap], "f":{ "w":0|1, "o":0|1 } }`
  — pose (Unity world axes), velocity, control deflections (rad / fractions), flags (wings gone, pilot out).

Server → client

- `{ "t":"hello", "id", "room", "peers":[{ "id","name","ac","s"? }] }` — your id + current roster; `s` is that peer's last state when known.
- `{ "t":"peer", "id","name","ac" }` — someone joined (or changed name/aircraft).
- `{ "t":"bye", "id" }` — someone left.
- `{ "t":"s", "id", "p","q","v","d","f" }` — a peer's state (fan-out to everyone but the sender).

Per-socket rate limit: ≥ 20 messages in a 1 s window are dropped. Messages > 1 KiB are dropped.

## Develop / test

```sh
cd server/relay
npm install
npm test          # starts `wrangler dev` on :8799, runs test/client.mjs (2 pilots + late joiner), tears down
npm run dev       # wrangler dev on :8787 — the Unity editor build connects to ws://localhost:8787
```

## Deploy (owner)

```sh
cd server/relay
npx wrangler login        # once
npx wrangler deploy       # -> https://flyinggame-relay.tyrel-frisby.workers.dev
```

The Unity client's `SessionSettings.RelayUrl` defaults to `wss://flyinggame-relay.tyrel-frisby.workers.dev`
(device builds) and `ws://localhost:8787` in the editor.
