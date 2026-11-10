// COMPANION LINK (docs/TV-COMPANION-PLAN.md §5; Ty 2026-11-04: Q9 "QR and PIN, QR recommended", Q10 "TLS-PSK in v1").
//
// The transport only: Bonjour (_aeroplayground._tcp), TCP + TLS 1.2 with pre-shared keys, newline-delimited lines, and
// a queue of events C# polls. Every key, identity and proof is computed in tested C# (Core CompanionPairing /
// CompanionAuth) and handed in as hex, so this file never decides anything about trust except "the TLS handshake
// succeeded with one of the keys I was given". The same source builds into UnityFramework on iOS and into
// AeroNative.bundle on the Mac (tools/native-mac/build.sh), like JoyLink.mm.
//
// Why Network.framework: .NET sockets in Unity can do neither Bonjour nor peer-to-peer Wi-Fi, and Network.framework's
// TLS takes a PSK directly (sec_protocol_options_add_pre_shared_key), so there is no certificate to make or pin.
// TLS_PSK_WITH_AES_128_GCM_SHA256 (0x00A8) is a TLS 1.2 suite, so both ends pin 1.2.
//
// Threading: Network.framework calls back on `queue`; C# calls in on Unity's main thread. Every piece of state is
// touched only on `queue` (calls in hop onto it with sync), and events leave through a locked array.

import Foundation
import Network
import Security
import CoreImage

private let serviceType = "_aeroplayground._tcp"
private let pskSuite: UInt16 = 0x00A8   // TLS_PSK_WITH_AES_128_GCM_SHA256

private func hexData(_ s: String) -> Data? {
    guard s.count % 2 == 0 else { return nil }
    var d = Data(capacity: s.count / 2)
    var i = s.startIndex
    while i < s.endIndex {
        let j = s.index(i, offsetBy: 2)
        guard let b = UInt8(s[i..<j], radix: 16) else { return nil }
        d.append(b)
        i = j
    }
    return d
}

private func dispatchData(_ d: Data) -> DispatchData { d.withUnsafeBytes { DispatchData(bytes: $0) } }

/// TLS 1.2 with these pre-shared keys and nothing else: no certificates, no other suites.
private func tlsParameters(_ psks: [(identity: String, key: Data)]) -> NWParameters {
    let tls = NWProtocolTLS.Options()
    let sec = tls.securityProtocolOptions
    for p in psks {
        sec_protocol_options_add_pre_shared_key(sec, dispatchData(p.key) as __DispatchData,
                                                dispatchData(Data(p.identity.utf8)) as __DispatchData)
    }
    if let suite = tls_ciphersuite_t(rawValue: pskSuite) { sec_protocol_options_append_tls_ciphersuite(sec, suite) }
    sec_protocol_options_set_min_tls_protocol_version(sec, .TLSv12)
    sec_protocol_options_set_max_tls_protocol_version(sec, .TLSv12)
    let tcp = NWProtocolTCP.Options()
    tcp.noDelay = true
    tcp.enableKeepalive = true          // Glass's numbers: a dead link is noticed in about 15 s
    tcp.keepaliveIdle = 5
    tcp.keepaliveInterval = 5
    tcp.keepaliveCount = 2
    let params = NWParameters(tls: tls, tcp: tcp)
    params.includePeerToPeer = true
    return params
}

private final class Link {
    static let shared = Link()
    let queue = DispatchQueue(label: "aero.link")

    private let eventLock = NSLock()
    private var events: [String] = []

    private var listener: NWListener?
    private var listenName = "", listenId = ""
    private var serverKeys: [(identity: String, key: Data)] = []
    private var browser: NWBrowser?
    private var found: [[String: String]] = []
    private var conns: [Int: NWConnection] = [:]
    private var buffers: [Int: Data] = [:]
    private var ready: Set<Int> = []
    private var nextId = 1

    // MARK: events

    func emit(_ e: [String: Any]) {
        guard let d = try? JSONSerialization.data(withJSONObject: e), let s = String(data: d, encoding: .utf8) else { return }
        eventLock.lock(); events.append(s); if events.count > 512 { events.removeFirst(events.count - 512) }; eventLock.unlock()
    }

    /// The next event into dst (NUL-terminated); 0 if there is none, −(bytes needed) if dst is too small (kept).
    func poll(_ dst: UnsafeMutablePointer<CChar>, _ max: Int32) -> Int32 {
        eventLock.lock(); defer { eventLock.unlock() }
        guard let first = events.first else { return 0 }
        let u = Array(first.utf8)
        if u.count + 1 > Int(max) { return -Int32(u.count + 1) }
        u.withUnsafeBufferPointer { b in b.baseAddress!.withMemoryRebound(to: CChar.self, capacity: u.count) { dst.update(from: $0, count: u.count) } }
        dst[u.count] = 0
        events.removeFirst()
        return Int32(u.count)
    }

    // MARK: game role (the Mac): a listener that accepts any of the keys it was given

    func listen(name: String, id: String) {
        listenName = name; listenId = id
        restartListener()
    }

    func setServerKeys(_ keys: [(identity: String, key: Data)]) {
        serverKeys = keys
        if listener != nil { restartListener() }   // accepted connections are not affected
    }

    func stopListening() { listener?.cancel(); listener = nil }

    private func restartListener() {
        listener?.stateUpdateHandler = nil
        listener?.cancel(); listener = nil
        guard !serverKeys.isEmpty else { emit(["ev": "listening", "port": 0, "keys": 0]); return }
        do {
            let l = try NWListener(using: tlsParameters(serverKeys))
            l.service = NWListener.Service(name: listenName, type: serviceType, domain: nil,
                                           txtRecord: NWTXTRecord(["role": "game", "v": "1", "id": listenId]))
            l.stateUpdateHandler = { [weak self, weak l] st in
                guard let self, let l, l === self.listener else { return }
                switch st {
                case .ready: self.emit(["ev": "listening", "port": Int(l.port?.rawValue ?? 0), "keys": self.serverKeys.count])
                case .failed(let e): self.emit(["ev": "listen-failed", "err": "\(e)"]); l.cancel(); self.listener = nil
                default: break
                }
            }
            l.newConnectionHandler = { [weak self] c in self?.adopt(c, outgoing: false) }
            listener = l
            l.start(queue: queue)
        } catch {
            emit(["ev": "listen-failed", "err": "\(error)"])
        }
    }

    // MARK: companion role (the iPad / iPhone): find games, connect to one

    func browse(_ on: Bool) {
        browser?.cancel(); browser = nil; found = []
        guard on else { return }
        let p = NWParameters(); p.includePeerToPeer = true
        let b = NWBrowser(for: .bonjourWithTXTRecord(type: serviceType, domain: nil), using: p)
        b.browseResultsChangedHandler = { [weak self] results, _ in
            guard let self else { return }
            var list: [[String: String]] = []
            for r in results {
                guard case let .service(name, _, _, _) = r.endpoint else { continue }
                var row = ["name": name]
                if case let .bonjour(txt) = r.metadata { row["role"] = txt["role"] ?? ""; row["id"] = txt["id"] ?? ""; row["v"] = txt["v"] ?? "" }
                list.append(row)
            }
            self.found = list
            self.emit(["ev": "browse", "found": list])
        }
        b.stateUpdateHandler = { [weak self] st in if case .failed(let e) = st { self?.emit(["ev": "browse-failed", "err": "\(e)"]) } }
        browser = b
        b.start(queue: queue)
    }

    func connect(_ endpoint: NWEndpoint, identity: String, key: Data) -> Int {
        let c = NWConnection(to: endpoint, using: tlsParameters([(identity, key)]))
        return adopt(c, outgoing: true)
    }

    // MARK: connections (both roles)

    @discardableResult
    private func adopt(_ c: NWConnection, outgoing: Bool) -> Int {
        let id = nextId; nextId += 1
        conns[id] = c; buffers[id] = Data()
        c.stateUpdateHandler = { [weak self, weak c] st in
            guard let self, let c, self.conns[id] === c else { return }
            switch st {
            case .ready:
                self.ready.insert(id)
                // Say what TLS actually negotiated, so the log (and the self-test) can see it is the PSK suite.
                var suite = 0
                if let m = c.metadata(definition: NWProtocolTLS.definition) as? NWProtocolTLS.Metadata {
                    suite = Int(sec_protocol_metadata_get_negotiated_tls_ciphersuite(m.securityProtocolMetadata).rawValue)
                }
                self.emit(["ev": "open", "c": id, "out": outgoing, "suite": suite])
                self.receive(id, c)
            case .failed(let e): self.drop(id, "\(e)")
            case .waiting(let e): if !outgoing || !self.ready.contains(id) { self.drop(id, "\(e)") }
            case .cancelled: self.drop(id, "closed")
            default: break
            }
        }
        c.start(queue: queue)
        return id
    }

    private func receive(_ id: Int, _ c: NWConnection) {
        c.receive(minimumIncompleteLength: 1, maximumLength: 65536) { [weak self, weak c] data, _, done, err in
            guard let self, let c, self.conns[id] === c else { return }
            if let data, !data.isEmpty {
                var buf = (self.buffers[id] ?? Data()) + data
                while let nl = buf.firstIndex(of: 0x0A) {
                    let line = String(decoding: buf[buf.startIndex..<nl], as: UTF8.self)
                    buf = Data(buf[buf.index(after: nl)...])
                    if !line.isEmpty { self.emit(["ev": "line", "c": id, "line": line]) }
                }
                if buf.count > 1 << 20 { self.close(id); self.drop(id, "line too long"); return }   // not ours
                self.buffers[id] = buf
            }
            if done || err != nil { self.drop(id, err.map { "\($0)" } ?? "closed"); return }
            self.receive(id, c)
        }
    }

    func send(_ id: Int, _ line: String) -> Bool {
        guard let c = conns[id], ready.contains(id) else { return false }
        c.send(content: Data((line + "\n").utf8), completion: .contentProcessed { _ in })
        return true
    }

    func close(_ id: Int) {
        guard let c = conns[id] else { return }
        c.stateUpdateHandler = nil   // a late callback from this connection must not tear down a new one
        c.cancel()
        drop(id, "closed")
    }

    private func drop(_ id: Int, _ why: String) {
        guard let c = conns.removeValue(forKey: id) else { return }
        c.stateUpdateHandler = nil
        c.cancel()
        let wasReady = ready.remove(id) != nil
        buffers[id] = nil
        // handshake = true: it never got through TLS (a wrong key, or the other end is gone). The Mac counts those
        // against an open pairing window.
        emit(["ev": "closed", "c": id, "err": why, "handshake": !wasReady])
    }

    func stopAll() {
        stopListening(); browse(false)
        for id in Array(conns.keys) { close(id) }
        serverKeys = []
    }
}

// MARK: Keychain (the paired keys)

private let keychainService = "com.tyrelfrisby.aeroplayground.companion"

private func secretSet(_ account: String, _ value: String) -> Bool {
    let base: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: keychainService,
                               kSecAttrAccount as String: account]
    SecItemDelete(base as CFDictionary)
    var add = base
    add[kSecValueData as String] = Data(value.utf8)
    add[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
    return SecItemAdd(add as CFDictionary, nil) == errSecSuccess
}

private func secretGet(_ account: String) -> String? {
    let q: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: keychainService,
                            kSecAttrAccount as String: account, kSecReturnData as String: true, kSecMatchLimit as String: kSecMatchLimitOne]
    var out: CFTypeRef?
    guard SecItemCopyMatching(q as CFDictionary, &out) == errSecSuccess, let d = out as? Data else { return nil }
    return String(data: d, encoding: .utf8)
}

// MARK: C surface (C#: Bridge/Companion/AeroLinkNative.cs)

private func str(_ p: UnsafePointer<CChar>?) -> String { p.map { String(cString: $0) } ?? "" }

@_cdecl("AeroLink_Listen")
public func AeroLink_Listen(_ name: UnsafePointer<CChar>?, _ id: UnsafePointer<CChar>?) {
    let n = str(name), i = str(id)
    Link.shared.queue.async { Link.shared.listen(name: n, id: i) }
}

/// keys: lines of "identity<TAB>hex key". Replaces the whole table; the listener restarts with it.
@_cdecl("AeroLink_SetServerKeys")
public func AeroLink_SetServerKeys(_ keys: UnsafePointer<CChar>?) -> Int32 {
    var table: [(identity: String, key: Data)] = []
    for row in str(keys).split(separator: "\n") {
        let parts = row.split(separator: "\t")
        guard parts.count == 2, let k = hexData(String(parts[1])), k.count >= 16 else { return -1 }
        table.append((String(parts[0]), k))
    }
    Link.shared.queue.async { Link.shared.setServerKeys(table) }
    return Int32(table.count)
}

@_cdecl("AeroLink_StopListening")
public func AeroLink_StopListening() { Link.shared.queue.async { Link.shared.stopListening() } }

@_cdecl("AeroLink_Browse")
public func AeroLink_Browse(_ on: Int32) { Link.shared.queue.async { Link.shared.browse(on != 0) } }

/// Connect to a game found by browsing (its Bonjour service name). Returns the connection id, or −1.
@_cdecl("AeroLink_Connect")
public func AeroLink_Connect(_ service: UnsafePointer<CChar>?, _ identity: UnsafePointer<CChar>?, _ keyHex: UnsafePointer<CChar>?) -> Int32 {
    guard let k = hexData(str(keyHex)), k.count >= 16 else { return -1 }
    let ep = NWEndpoint.service(name: str(service), type: serviceType, domain: "local.", interface: nil)
    let ident = str(identity)
    return Int32(Link.shared.queue.sync { Link.shared.connect(ep, identity: ident, key: k) })
}

/// Connect to host:port directly (tests, and a Mac named by address).
@_cdecl("AeroLink_ConnectHost")
public func AeroLink_ConnectHost(_ host: UnsafePointer<CChar>?, _ port: Int32, _ identity: UnsafePointer<CChar>?, _ keyHex: UnsafePointer<CChar>?) -> Int32 {
    guard let k = hexData(str(keyHex)), k.count >= 16, let p = NWEndpoint.Port(rawValue: UInt16(clamping: port)) else { return -1 }
    let ep = NWEndpoint.hostPort(host: NWEndpoint.Host(str(host)), port: p)
    let ident = str(identity)
    return Int32(Link.shared.queue.sync { Link.shared.connect(ep, identity: ident, key: k) })
}

@_cdecl("AeroLink_Send")
public func AeroLink_Send(_ conn: Int32, _ line: UnsafePointer<CChar>?) -> Int32 {
    let l = str(line)
    return Link.shared.queue.sync { Link.shared.send(Int(conn), l) } ? 1 : 0
}

@_cdecl("AeroLink_Close")
public func AeroLink_Close(_ conn: Int32) { Link.shared.queue.async { Link.shared.close(Int(conn)) } }

@_cdecl("AeroLink_Poll")
public func AeroLink_Poll(_ dst: UnsafeMutablePointer<CChar>?, _ max: Int32) -> Int32 {
    guard let dst, max > 0 else { return 0 }
    return Link.shared.poll(dst, max)
}

@_cdecl("AeroLink_StopAll")
public func AeroLink_StopAll() { Link.shared.queue.sync { Link.shared.stopAll() } }

@_cdecl("AeroLink_SecretSet")
public func AeroLink_SecretSet(_ account: UnsafePointer<CChar>?, _ value: UnsafePointer<CChar>?) -> Int32 {
    secretSet(str(account), str(value)) ? 1 : 0
}

/// The stored value into dst; its length, 0 if there is none, −(bytes needed) if dst is too small.
@_cdecl("AeroLink_SecretGet")
public func AeroLink_SecretGet(_ account: UnsafePointer<CChar>?, _ dst: UnsafeMutablePointer<CChar>?, _ max: Int32) -> Int32 {
    guard let v = secretGet(str(account)), let dst else { return 0 }
    let u = Array(v.utf8)
    if u.count + 1 > Int(max) { return -Int32(u.count + 1) }
    for (i, b) in u.enumerated() { dst[i] = CChar(bitPattern: b) }
    dst[u.count] = 0
    return Int32(u.count)
}

@_cdecl("AeroLink_SecretDelete")
public func AeroLink_SecretDelete(_ account: UnsafePointer<CChar>?) {
    let q: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: keychainService,
                            kSecAttrAccount as String: str(account)]
    SecItemDelete(q as CFDictionary)
}

/// The QR code for text as modules, one byte each (1 = dark), row-major, top row first; *side = modules per side.
/// Returns side × side, or −(bytes needed) if dst is too small. Medium error correction.
@_cdecl("AeroLink_QrModules")
public func AeroLink_QrModules(_ text: UnsafePointer<CChar>?, _ dst: UnsafeMutablePointer<UInt8>?, _ max: Int32, _ side: UnsafeMutablePointer<Int32>?) -> Int32 {
    guard let f = CIFilter(name: "CIQRCodeGenerator") else { return 0 }
    f.setValue(Data(str(text).utf8), forKey: "inputMessage")
    f.setValue("M", forKey: "inputCorrectionLevel")
    guard let img = f.outputImage else { return 0 }
    let w = Int(img.extent.width), h = Int(img.extent.height)
    guard w > 0, w == h else { return 0 }
    side?.pointee = Int32(w)
    if w * h > Int(max) { return -Int32(w * h) }
    guard let dst else { return 0 }
    var rgba = [UInt8](repeating: 0, count: w * h * 4)
    let ctx = CIContext(options: [.useSoftwareRenderer: true])
    ctx.render(img, toBitmap: &rgba, rowBytes: w * 4, bounds: img.extent, format: .RGBA8, colorSpace: CGColorSpaceCreateDeviceRGB())
    // CoreImage's origin is bottom-left and render() writes the first row at the top of the bitmap: row 0 is the top.
    for i in 0..<(w * h) { dst[i] = rgba[i * 4] < 128 ? 1 : 0 }
    return Int32(w * h)
}
