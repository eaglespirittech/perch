// perch-ble: the CoreBluetooth half of Perch on macOS.
//
// Perch is .NET, which has no CoreBluetooth binding, so it starts this and talks to it
// over stdin/stdout, one JSON object per line. It deliberately knows nothing about
// desks: it scans, connects, and reads, writes and subscribes to characteristics. The
// Linak protocol lives in the shared C# code (Perch.Core/Desk).
//
// Requests carry an id and get exactly one reply with the same id:
//   {"id":1,"op":"scan","seconds":4}             -> {"id":1,"ok":true,"devices":[{"id":"<uuid>","name":"Desk 1234"}]}
//   {"id":2,"op":"connect","device":"<uuid>","timeout":15}  -> {"id":2,"ok":true,"name":"Desk 1234"}
//   {"id":3,"op":"discover","service":"<uuid>","char":"<uuid>"}
//   {"id":4,"op":"read","service":"<uuid>","char":"<uuid>"}  -> {"id":4,"ok":true,"value":"<hex>"}
//   {"id":5,"op":"write","service":"<uuid>","char":"<uuid>","value":"<hex>","withoutResponse":true}
//   {"id":6,"op":"subscribe","service":"<uuid>","char":"<uuid>"}
//   {"id":7,"op":"disconnect"}
// Failures:  {"id":n,"ok":false,"error":"<message for the user>"}
// Events, which have no id:
//   {"event":"notify","char":"<uuid>","value":"<hex>"}
//   {"event":"disconnected"}
//
// Closing stdin disconnects and exits, so the helper never outlives Perch.
//
// Build: native/macos/perch-ble/build.sh

import CoreBluetooth
import Foundation

let queue = DispatchQueue(label: "com.eaglespirit.perch.ble")

let deskService = CBUUID(string: "99FA0001-338A-1024-8A49-009C0215F78A")

let busyMessage =
    "Could not reach the desk. Check it is in range and awake (press one of its buttons), " +
    "and that nothing else is connected to it, such as the IKEA app on your phone."

func emit(_ object: [String: Any]) {
    guard let data = try? JSONSerialization.data(withJSONObject: object, options: []) else { return }
    FileHandle.standardOutput.write(data)
    FileHandle.standardOutput.write(Data([0x0A]))
}

func hex(_ data: Data) -> String {
    data.map { String(format: "%02x", $0) }.joined()
}

func data(fromHex text: String) -> Data? {
    guard text.count % 2 == 0 else { return nil }
    var result = Data()
    var index = text.startIndex
    while index < text.endIndex {
        let next = text.index(index, offsetBy: 2)
        guard let byte = UInt8(text[index..<next], radix: 16) else { return nil }
        result.append(byte)
        index = next
    }
    return result
}

func uuid(_ value: Any?) -> CBUUID? {
    guard let text = value as? String, let parsed = UUID(uuidString: text) else { return nil }
    return CBUUID(nsuuid: parsed)
}

func looksLikeDesk(_ name: String) -> Bool {
    let lower = name.lowercased()
    return ["desk", "lift", "linak", "idasen"].contains { lower.contains($0) }
}

final class Bridge: NSObject, CBCentralManagerDelegate, CBPeripheralDelegate {
    private var central: CBCentralManager!

    /// Requests waiting for Bluetooth to finish starting up.
    private var stateWaiters: [(id: Int, body: () -> Void)] = []

    /// Everything heard from, held strongly: CoreBluetooth forgets peripherals nobody holds.
    private var seen: [UUID: CBPeripheral] = [:]
    private var names: [UUID: String] = [:]
    private var advertisesDesk: Set<UUID> = []
    private var scanUsers = 0

    /// A connect waiting for a scan to turn up its peripheral.
    private var wanted: (uuid: UUID, id: Int, found: (CBPeripheral) -> Void)?

    private var peripheral: CBPeripheral?
    private var connectReply: Int?
    private var pendingDiscover: [(id: Int, service: CBUUID, char: CBUUID)] = []
    private var pendingReads: [CBUUID: [Int]] = [:]
    private var pendingWrites: [CBUUID: [Int]] = [:]
    private var pendingNotify: [CBUUID: [Int]] = [:]

    override init() {
        super.init()
        central = CBCentralManager(delegate: self, queue: queue)
    }

    // MARK: - requests

    func handle(_ line: String) {
        guard let bytes = line.data(using: .utf8),
              let message = (try? JSONSerialization.jsonObject(with: bytes)) as? [String: Any],
              let id = message["id"] as? Int,
              let op = message["op"] as? String
        else {
            return
        }

        switch op {
        case "scan":
            scan(id, seconds: message["seconds"] as? Double ?? 4)
        case "connect":
            connect(id, device: message["device"] as? String ?? "", timeout: message["timeout"] as? Double ?? 15)
        case "disconnect":
            disconnect()
            reply(id)
        case "discover", "read", "write", "subscribe":
            guard let service = uuid(message["service"]), let char = uuid(message["char"]) else {
                reply(id, error: "Bad service or characteristic id.")
                return
            }
            guard let peripheral, peripheral.state == .connected else {
                reply(id, error: "Not connected to the desk.")
                return
            }
            if op == "discover" {
                discover(id, peripheral, service, char)
                return
            }
            guard let characteristic = find(peripheral, service, char) else {
                reply(id, error: "That characteristic has not been discovered.")
                return
            }
            switch op {
            case "read":
                pendingReads[char, default: []].append(id)
                peripheral.readValue(for: characteristic)
            case "write":
                guard let value = data(fromHex: message["value"] as? String ?? "") else {
                    reply(id, error: "Bad value.")
                    return
                }
                write(id, peripheral, characteristic, value, withoutResponse: message["withoutResponse"] as? Bool ?? false)
            default:
                pendingNotify[char, default: []].append(id)
                peripheral.setNotifyValue(true, for: characteristic)
            }
        default:
            reply(id, error: "Unknown request \"\(op)\".")
        }
    }

    func shutdown() {
        disconnect()
    }

    private func reply(_ id: Int, _ fields: [String: Any] = [:]) {
        var object = fields
        object["id"] = id
        object["ok"] = true
        emit(object)
    }

    private func reply(_ id: Int, error: String) {
        emit(["id": id, "ok": false, "error": error])
    }

    // MARK: - Bluetooth state

    private var settled: Bool {
        central.state != .unknown && central.state != .resetting
    }

    private var stateProblem: String? {
        switch central.state {
        case .poweredOn:
            return nil
        case .unauthorized:
            return "Perch is not allowed to use Bluetooth. Allow it in System Settings > Privacy & Security > Bluetooth."
        case .poweredOff:
            return "Bluetooth is turned off."
        case .unsupported:
            return "This Mac does not support Bluetooth LE."
        default:
            return "Bluetooth is not ready yet. Try again in a moment."
        }
    }

    /// Runs `body` once Bluetooth is on, or replies with why it cannot be.
    private func whenReady(_ id: Int, _ body: @escaping () -> Void) {
        if settled {
            if let problem = stateProblem { reply(id, error: problem) } else { body() }
            return
        }

        stateWaiters.append((id, body))
        queue.asyncAfter(deadline: .now() + 5) { [weak self] in self?.releaseWaiters() }
    }

    private func releaseWaiters() {
        let waiters = stateWaiters
        stateWaiters = []
        for waiter in waiters {
            if let problem = stateProblem { reply(waiter.id, error: problem) } else { waiter.body() }
        }
    }

    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        if settled { releaseWaiters() }

        if central.state != .poweredOn, peripheral != nil {
            peripheral = nil
            failPending("Bluetooth went away.")
            emit(["event": "disconnected"])
        }
    }

    // MARK: - scanning

    private func beginScan() {
        scanUsers += 1
        if scanUsers == 1 { central.scanForPeripherals(withServices: nil, options: nil) }
    }

    private func endScan() {
        scanUsers = max(0, scanUsers - 1)
        if scanUsers == 0 { central.stopScan() }
    }

    private func remember(_ peripheral: CBPeripheral, name: String?, isDesk: Bool) {
        seen[peripheral.identifier] = peripheral
        if let name, !name.isEmpty { names[peripheral.identifier] = name }
        if isDesk { advertisesDesk.insert(peripheral.identifier) }
    }

    private func name(of peripheral: CBPeripheral) -> String {
        names[peripheral.identifier] ?? peripheral.name ?? "Unknown"
    }

    private func scan(_ id: Int, seconds: Double) {
        whenReady(id) {
            // A desk this Mac is already connected to does not advertise, so ask for those too.
            for connected in self.central.retrieveConnectedPeripherals(withServices: [deskService]) {
                self.remember(connected, name: connected.name, isDesk: true)
            }

            self.beginScan()
            queue.asyncAfter(deadline: .now() + seconds) {
                self.endScan()
                let devices = self.seen.values
                    .filter { self.advertisesDesk.contains($0.identifier) || looksLikeDesk(self.name(of: $0)) }
                    .map { ["id": $0.identifier.uuidString, "name": self.name(of: $0)] }
                self.reply(id, ["devices": devices])
            }
        }
    }

    func centralManager(
        _ central: CBCentralManager, didDiscover peripheral: CBPeripheral,
        advertisementData: [String: Any], rssi RSSI: NSNumber
    ) {
        let services = advertisementData[CBAdvertisementDataServiceUUIDsKey] as? [CBUUID] ?? []
        let name = advertisementData[CBAdvertisementDataLocalNameKey] as? String ?? peripheral.name
        remember(peripheral, name: name, isDesk: services.contains(deskService))

        if let wanted, wanted.uuid == peripheral.identifier {
            self.wanted = nil
            endScan()
            wanted.found(peripheral)
        }
    }

    // MARK: - connecting

    private func connect(_ id: Int, device: String, timeout: Double) {
        guard let identifier = UUID(uuidString: device) else {
            reply(id, error: "\"\(device)\" is not a macOS Bluetooth device id. Run \"perch-cli list\" to see them.")
            return
        }

        whenReady(id) {
            self.disconnect() // one desk at a time

            let start: (CBPeripheral) -> Void = { target in
                self.peripheral = target
                target.delegate = self
                self.connectReply = id
                self.central.connect(target, options: nil)

                // CoreBluetooth never gives up on a connect by itself.
                queue.asyncAfter(deadline: .now() + timeout) {
                    guard self.connectReply == id else { return }
                    self.connectReply = nil
                    self.central.cancelPeripheralConnection(target)
                    self.peripheral = nil
                    self.reply(id, error: busyMessage)
                }
            }

            if let known = self.seen[identifier]
                ?? self.central.retrievePeripherals(withIdentifiers: [identifier]).first {
                self.remember(known, name: known.name, isDesk: false)
                start(known)
                return
            }

            // Not in the system's cache (after a reboot, say): listen for it.
            self.wanted = (identifier, id, start)
            self.beginScan()
            queue.asyncAfter(deadline: .now() + timeout) {
                guard let wanted = self.wanted, wanted.uuid == identifier else { return }
                self.wanted = nil
                self.endScan()
                self.reply(id, error: busyMessage)
            }
        }
    }

    private func disconnect() {
        if let wanted {
            self.wanted = nil
            endScan()
            reply(wanted.id, error: "Disconnected.")
        }
        if let id = connectReply {
            connectReply = nil
            reply(id, error: "Disconnected.")
        }
        guard let current = peripheral else { return }
        peripheral = nil // so the disconnect callback is not reported as a drop
        central.cancelPeripheralConnection(current)
        failPending("Disconnected.")
    }

    func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        guard peripheral == self.peripheral, let id = connectReply else { return }
        connectReply = nil
        reply(id, ["name": name(of: peripheral)])
    }

    func centralManager(_ central: CBCentralManager, didFailToConnect peripheral: CBPeripheral, error: Error?) {
        guard peripheral == self.peripheral, let id = connectReply else { return }
        connectReply = nil
        self.peripheral = nil
        reply(id, error: error.map { "\(busyMessage) (\($0.localizedDescription))" } ?? busyMessage)
    }

    func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: Error?) {
        guard peripheral == self.peripheral else { return }
        self.peripheral = nil

        if let id = connectReply {
            connectReply = nil
            reply(id, error: busyMessage)
            return
        }

        failPending("The desk disconnected.")
        emit(["event": "disconnected"])
    }

    private func failPending(_ message: String) {
        for entry in pendingDiscover { reply(entry.id, error: message) }
        pendingDiscover = []
        for ids in [pendingReads, pendingWrites, pendingNotify].flatMap({ $0.values }) {
            for id in ids { reply(id, error: message) }
        }
        pendingReads = [:]
        pendingWrites = [:]
        pendingNotify = [:]
    }

    // MARK: - discovery

    private func find(_ peripheral: CBPeripheral, _ service: CBUUID, _ char: CBUUID) -> CBCharacteristic? {
        peripheral.services?.first { $0.uuid == service }?.characteristics?.first { $0.uuid == char }
    }

    /// Perch resolves characteristics one at a time, so at most one discovery is in flight.
    private func discover(_ id: Int, _ peripheral: CBPeripheral, _ service: CBUUID, _ char: CBUUID) {
        if find(peripheral, service, char) != nil {
            reply(id)
            return
        }

        pendingDiscover.append((id, service, char))
        if let known = peripheral.services?.first(where: { $0.uuid == service }) {
            peripheral.discoverCharacteristics([char], for: known)
        } else {
            peripheral.discoverServices([service])
        }

        queue.asyncAfter(deadline: .now() + 10) {
            guard let index = self.pendingDiscover.firstIndex(where: { $0.id == id }) else { return }
            self.pendingDiscover.remove(at: index)
            self.reply(id, error: busyMessage)
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        let found = peripheral.services ?? []
        for service in found {
            let chars = pendingDiscover.filter { $0.service == service.uuid }.map { $0.char }
            if !chars.isEmpty { peripheral.discoverCharacteristics(chars, for: service) }
        }

        let missing = pendingDiscover.filter { entry in !found.contains { $0.uuid == entry.service } }
        pendingDiscover.removeAll { entry in !found.contains { $0.uuid == entry.service } }
        for entry in missing {
            reply(entry.id, error: error.map { "\(busyMessage) (\($0.localizedDescription))" } ?? busyMessage)
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: Error?) {
        let mine = pendingDiscover.filter { $0.service == service.uuid }
        pendingDiscover.removeAll { $0.service == service.uuid }
        for entry in mine {
            if find(peripheral, entry.service, entry.char) != nil {
                reply(entry.id)
            } else {
                reply(entry.id, error: error.map { "\(busyMessage) (\($0.localizedDescription))" } ?? busyMessage)
            }
        }
    }

    // MARK: - reading, writing, notifications

    private func write(
        _ id: Int, _ peripheral: CBPeripheral, _ characteristic: CBCharacteristic, _ value: Data, withoutResponse: Bool
    ) {
        let props = characteristic.properties
        let quiet = props.contains(.writeWithoutResponse) &&
            (!props.contains(.write) || (withoutResponse && peripheral.canSendWriteWithoutResponse))

        if quiet {
            peripheral.writeValue(value, for: characteristic, type: .withoutResponse)
            reply(id)
            return
        }

        pendingWrites[characteristic.uuid, default: []].append(id)
        peripheral.writeValue(value, for: characteristic, type: .withResponse)
    }

    private func take(_ pending: inout [CBUUID: [Int]], _ uuid: CBUUID) -> Int? {
        guard var ids = pending[uuid], !ids.isEmpty else { return nil }
        let id = ids.removeFirst()
        pending[uuid] = ids
        return id
    }

    func peripheral(_ peripheral: CBPeripheral, didUpdateValueFor characteristic: CBCharacteristic, error: Error?) {
        // A read and a notification arrive through the same callback; a pending read takes
        // whichever comes first, which is equally current either way.
        if let id = take(&pendingReads, characteristic.uuid) {
            if let error {
                reply(id, error: error.localizedDescription)
            } else {
                reply(id, ["value": hex(characteristic.value ?? Data())])
            }
            return
        }

        if error == nil, let value = characteristic.value {
            emit(["event": "notify", "char": characteristic.uuid.uuidString, "value": hex(value)])
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didWriteValueFor characteristic: CBCharacteristic, error: Error?) {
        guard let id = take(&pendingWrites, characteristic.uuid) else { return }
        if let error { reply(id, error: "Write to the desk failed (\(error.localizedDescription)).") } else { reply(id) }
    }

    func peripheral(
        _ peripheral: CBPeripheral, didUpdateNotificationStateFor characteristic: CBCharacteristic, error: Error?
    ) {
        guard let id = take(&pendingNotify, characteristic.uuid) else { return }
        if let error {
            reply(id, error: "The desk refused height notifications (\(error.localizedDescription)).")
        } else {
            reply(id)
        }
    }
}

let bridge = Bridge()

let reader = Thread {
    while let line = readLine() {
        queue.async { bridge.handle(line) }
    }
    queue.async {
        bridge.shutdown()
        exit(0)
    }
}
reader.start()

dispatchMain()
