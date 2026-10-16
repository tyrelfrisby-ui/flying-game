// STOREKIT 2 (App Store auto-renewing subscription + the multiplayer-data purchases).
//
// Why Swift and not another .mm like the rest of Plugins/iOS: StoreKit 2 is a Swift-only API (async/await,
// `Transaction.currentEntitlements`), with no Objective-C surface. StoreKit 1 has one, but it hands back
// receipts that have to be validated against a server to be worth anything, whereas StoreKit 2 verifies
// locally and gives a signed, already-checked transaction. For a one-developer app that is the whole
// difference between needing a receipt-validation service and not needing one.
//
// Shape of the bridge: StoreKit 2 is asynchronous and Unity's interop is not, so nothing here calls back into
// C#. Every async result lands in a cached JSON snapshot and C# reads the latest one when it wants it
// (StoreKitBridge.cs). That also means a dropped frame or a scene load can never lose a transaction — the
// snapshot is still there next time anyone asks.
//
// Prices and product identifiers are NOT in this file. The identifiers come from C# (loaded from config) and
// the prices come from the App Store at runtime, localised, which is the only correct place to read them.

import Foundation
import StoreKit

@available(iOS 15.0, *)
private final class AeroStore {
    static let shared = AeroStore()

    private var products: [String: Product] = [:]
    private var updatesTask: Task<Void, Never>?
    private var purchasing: String?
    private var lastError: String?
    private var ready = false

    private let lock = NSLock()
    private var cached: String = #"{"ready":false}"#

    // MARK: lifecycle

    func start(productIds: [String]) {
        if updatesTask == nil {
            // Transactions can arrive at any time: an Ask-to-Buy approval, a renewal, a purchase made on
            // another device, or one interrupted by a crash. Listening for the life of the process is the
            // documented requirement, and it is what makes "restore" a rare manual fallback rather than a
            // thing people have to know about.
            updatesTask = Task.detached { [weak self] in
                for await update in Transaction.updates {
                    guard let self else { return }
                    if case .verified(let t) = update {
                        await t.finish()
                    }
                    await self.refresh()
                }
            }
        }
        Task { await self.load(productIds: productIds) }
    }

    private func load(productIds: [String]) async {
        do {
            let found = try await Product.products(for: productIds)
            for p in found { products[p.id] = p }
            ready = true
            lastError = nil
        } catch {
            // No network, or the identifiers are not configured in App Store Connect yet. Either way the
            // paywall shows its offline state rather than an empty list of nothing.
            lastError = error.localizedDescription
        }
        await refresh()
    }

    // MARK: actions

    func purchase(_ id: String) {
        guard let product = products[id] else {
            lastError = "That purchase is not available right now."
            publish()
            return
        }
        purchasing = id
        publish()
        Task {
            do {
                let result = try await product.purchase()
                switch result {
                case .success(let verification):
                    if case .verified(let t) = verification {
                        await t.finish()
                        lastError = nil
                    } else {
                        // Signature check failed: treat as not purchased. Never grant on an unverified one.
                        lastError = "That purchase could not be verified."
                    }
                case .userCancelled:
                    lastError = nil
                case .pending:
                    // Ask to Buy, or Strong Customer Authentication. It may complete hours later, and
                    // Transaction.updates is what will tell us.
                    lastError = nil
                @unknown default:
                    lastError = nil
                }
            } catch {
                lastError = error.localizedDescription
            }
            purchasing = nil
            await refresh()
        }
    }

    func restore() {
        Task {
            do {
                try await AppStore.sync()
                lastError = nil
            } catch {
                lastError = error.localizedDescription
            }
            await refresh()
        }
    }

    // MARK: state

    /// Walks the current entitlements and writes the snapshot C# polls. Consumables are deliberately absent:
    /// a consumable is credited once, when its transaction arrives, and is not an entitlement afterwards.
    private func refresh() async {
        var subs: [[String: Any]] = []
        var consumed: [[String: Any]] = []

        for await result in Transaction.currentEntitlements {
            guard case .verified(let t) = result else { continue }   // unverified grants nothing
            switch t.productType {
            case .autoRenewable:
                subs.append([
                    "id": t.productID,
                    "expiresUtc": AeroStore.iso(t.expirationDate),
                    "revoked": t.revocationDate != nil,
                    "upgraded": t.isUpgraded,
                ])
            case .nonConsumable:
                subs.append([
                    "id": t.productID,
                    "expiresUtc": "",                                 // never expires
                    "revoked": t.revocationDate != nil,
                    "upgraded": false,
                ])
            default:
                break
            }
        }

        // Unfinished consumables: data purchases that have not been credited yet. C# credits the meter and
        // calls AeroStoreFinish, so a purchase survives being killed mid-credit.
        for await result in Transaction.unfinished {
            guard case .verified(let t) = result, t.productType == .consumable else { continue }
            consumed.append(["id": t.productID, "txn": String(t.id)])
        }

        var offers: [[String: Any]] = []
        for (_, p) in products {
            offers.append([
                "id": p.id,
                "title": p.displayName,
                "detail": p.description,
                "price": p.displayPrice,                               // localised, from the store
            ])
        }

        let snapshot: [String: Any] = [
            "ready": ready,
            "purchasing": purchasing ?? "",
            "error": lastError ?? "",
            "nowUtc": AeroStore.iso(Date()),
            "entitlements": subs,
            "pendingConsumables": consumed,
            "offers": offers,
        ]
        publish(snapshot)
    }

    func finish(txnId: String) {
        Task {
            for await result in Transaction.unfinished {
                if case .verified(let t) = result, String(t.id) == txnId {
                    await t.finish()
                }
            }
            await refresh()
        }
    }

    private func publish(_ dict: [String: Any]? = nil) {
        guard let dict else { return }
        guard let data = try? JSONSerialization.data(withJSONObject: dict),
              let json = String(data: data, encoding: .utf8) else { return }
        lock.lock(); cached = json; lock.unlock()
    }

    private func publish() { Task { await refresh() } }

    func snapshot() -> String {
        lock.lock(); defer { lock.unlock() }
        return cached
    }

    private static func iso(_ d: Date?) -> String {
        guard let d else { return "" }
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f.string(from: d)
    }
}

// MARK: - C entry points (DllImport "__Internal" from StoreKitBridge.cs)

@_cdecl("AeroStoreStart")
public func AeroStoreStart(_ idsCsv: UnsafePointer<CChar>?) {
    guard #available(iOS 15.0, *), let idsCsv else { return }
    let ids = String(cString: idsCsv).split(separator: ",").map { String($0).trimmingCharacters(in: .whitespaces) }
    AeroStore.shared.start(productIds: ids.filter { !$0.isEmpty })
}

@_cdecl("AeroStorePurchase")
public func AeroStorePurchase(_ productId: UnsafePointer<CChar>?) {
    guard #available(iOS 15.0, *), let productId else { return }
    AeroStore.shared.purchase(String(cString: productId))
}

@_cdecl("AeroStoreRestore")
public func AeroStoreRestore() {
    guard #available(iOS 15.0, *) else { return }
    AeroStore.shared.restore()
}

@_cdecl("AeroStoreFinish")
public func AeroStoreFinish(_ txnId: UnsafePointer<CChar>?) {
    guard #available(iOS 15.0, *), let txnId else { return }
    AeroStore.shared.finish(txnId: String(cString: txnId))
}

/// Copies the JSON snapshot into a caller-owned buffer and returns its byte length — or, when the buffer is
/// too small, the length needed and nothing written. No allocation crosses the boundary, so there is no
/// question of who frees what.
@_cdecl("AeroStoreState")
public func AeroStoreState(_ dst: UnsafeMutablePointer<CChar>?, _ max: Int32) -> Int32 {
    guard #available(iOS 15.0, *) else { return 0 }
    let json = AeroStore.shared.snapshot()
    let bytes = Array(json.utf8)
    if dst == nil || Int(max) < bytes.count { return Int32(bytes.count) }
    bytes.withUnsafeBufferPointer { src in
        dst!.withMemoryRebound(to: UInt8.self, capacity: bytes.count) { d in
            d.update(from: src.baseAddress!, count: bytes.count)
        }
    }
    return Int32(bytes.count)
}
