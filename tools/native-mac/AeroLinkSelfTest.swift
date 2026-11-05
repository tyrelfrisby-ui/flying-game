// Loopback self-test of Plugins/iOS/AeroLink.swift on the Mac (tools/native-mac/aerolink-selftest.sh). It runs the
// real listener and real client in one process over 127.0.0.1 and checks what Q10 promises: a connection with a key the
// listener holds opens and carries lines both ways, under its own identity, alongside other keys; one with the right
// identity and the WRONG key never opens; and the QR generator returns a square of modules. Exit 0 = all passed.
import Foundation

func poll() -> [String: Any]? {
    var buf = [CChar](repeating: 0, count: 65536)
    let n = AeroLink_Poll(&buf, Int32(buf.count))
    guard n > 0 else { return nil }
    let s = String(cString: buf)
    return (try? JSONSerialization.jsonObject(with: Data(s.utf8))) as? [String: Any]
}

var seen: [[String: Any]] = []   // events not yet matched: both ends' events arrive interleaved

func wait(_ what: String, _ timeout: Double = 8, _ match: ([String: Any]) -> Bool) -> [String: Any]? {
    let end = Date().addingTimeInterval(timeout)
    while Date() < end {
        if let i = seen.firstIndex(where: match) { return seen.remove(at: i) }
        if let e = poll() { print("  event", e); seen.append(e) } else { Thread.sleep(forTimeInterval: 0.01) }
    }
    print("TIMEOUT waiting for \(what)")
    return nil
}

var failures = 0
func check(_ ok: Bool, _ what: String) { print(ok ? "PASS" : "FAIL", what); if !ok { failures += 1 } }

let kA = String(repeating: "11", count: 32), kPin = String(repeating: "22", count: 32), kWrong = String(repeating: "33", count: 32)
let idA = "aero-link/1/fedcba9876543210fedcba9876543210", idPin = "aero-pin/1"
check(AeroLink_SetServerKeys("\(idA)\t\(kA)\n\(idPin)\t\(kPin)") == 2, "two server keys accepted")
check(AeroLink_SetServerKeys("bad") == -1, "a malformed key table is refused")
_ = AeroLink_SetServerKeys("\(idA)\t\(kA)\n\(idPin)\t\(kPin)")
AeroLink_Listen("Aero Playground selftest", "0123456789abcdef0123456789abcdef")
guard let l = wait("listening", 8, { $0["ev"] as? String == "listening" && ($0["port"] as? Int ?? 0) > 0 }), let port = l["port"] as? Int else {
    print("FAIL listener did not start"); exit(1)
}

func tryConnect(_ identity: String, _ key: String, expectOpen: Bool, _ label: String) {
    let c = AeroLink_ConnectHost("127.0.0.1", Int32(port), identity, key)
    check(c > 0, "\(label): connect started")
    let e = wait(label, 8) { e in (e["c"] as? Int == Int(c)) && (e["ev"] as? String == "open" || e["ev"] as? String == "closed") }
    let opened = e?["ev"] as? String == "open"
    check(opened == expectOpen, "\(label): \(expectOpen ? "opens" : "is refused")")
    if !expectOpen { check(e?["handshake"] as? Bool == true, "\(label): refused in the handshake"); return }
    check(e?["suite"] as? Int == 0x00A8, "\(label): TLS_PSK_WITH_AES_128_GCM_SHA256 negotiated (\(e?["suite"] ?? "none"))")
    // the server side opens too; send a line client → server and back
    guard let s = wait("\(label) server side", 8, { $0["ev"] as? String == "open" && ($0["out"] as? Bool) == false }), let sc = s["c"] as? Int else {
        check(false, "\(label): server side opened"); return
    }
    check(AeroLink_Send(c, "{\"kind\":\"hello\"}") == 1, "\(label): send")
    let got = wait("\(label) line", 8) { $0["ev"] as? String == "line" && $0["c"] as? Int == sc }
    check(got?["line"] as? String == "{\"kind\":\"hello\"}", "\(label): the server read the line")
    check(AeroLink_Send(Int32(sc), "pong") == 1, "\(label): reply")
    let back = wait("\(label) reply", 8) { $0["ev"] as? String == "line" && $0["c"] as? Int == Int(c) }
    check(back?["line"] as? String == "pong", "\(label): the client read the reply")
    AeroLink_Close(c)
}

tryConnect(idPin, kPin, expectOpen: true, "PIN key")
tryConnect(idA, kA, expectOpen: true, "link key (second key in the table)")
tryConnect(idPin, kWrong, expectOpen: false, "PIN identity, wrong key")
tryConnect(idA, kPin, expectOpen: false, "link identity, another identity's key")

var side: Int32 = 0
var mods = [UInt8](repeating: 0, count: 177 * 177)
let n = AeroLink_QrModules("aeroplay://pair?v=1&id=0123456789abcdef0123456789abcdef&name=Mac&k=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8", &mods, Int32(mods.count), &side)
check(n > 0 && Int(n) == Int(side) * Int(side) && side >= 21, "QR code: \(side) × \(side) modules")
// A QR finder pattern: the top-left 7×7 has a dark border (after CoreImage's quiet zone, if any).
let q = (0..<Int(side)).first { r in mods[r * Int(side) + r] == 1 } ?? -1
check(q >= 0 && mods[q * Int(side) + q + 6] == 1 && mods[(q + 6) * Int(side) + q] == 1, "QR code: finder pattern at the top left (quiet zone \(q))")

AeroLink_StopAll()
print(failures == 0 ? "AEROLINK SELFTEST PASSED" : "AEROLINK SELFTEST FAILED (\(failures))")
exit(failures == 0 ? 0 : 1)
