// Keyboard and game controller: arrows / d-pad / left stick move, Return / A confirms, Esc / B goes back,
// Y opens All games, Menu opens Settings. Input only counts while the launcher is the app in front.
import AppKit
import GameController

@MainActor
final class InputRouter {
    private weak var store: Store?
    private var monitor: Any?
    private var stick = (x: 0, y: 0)

    func start(_ s: Store) {
        guard monitor == nil else { return }
        store = s
        let keys: [UInt16: Store.Input] = [123: .left, 124: .right, 125: .down, 126: .up, 36: .confirm, 76: .confirm, 53: .back]
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] e in
            guard let store = self?.store else { return e }
            // typing in a field, a sheet or an alert: the keys are theirs
            if NSApp.keyWindow?.firstResponder is NSTextView { return e }
            if store.showSettings || store.confirmUninstall != nil || store.message != nil { return e }
            if !e.modifierFlags.intersection([.command, .control, .option]).isEmpty { return e }
            guard let input = keys[e.keyCode] else { return e }
            store.handle(input)
            return nil
        }
        NotificationCenter.default.addObserver(forName: .GCControllerDidConnect, object: nil, queue: .main) { [weak self] n in
            guard let c = n.object as? GCController else { return }
            Task { @MainActor in self?.attach(c) }
        }
        for c in GCController.controllers() { attach(c) }
    }

    private func send(_ i: Store.Input) {
        guard NSApp.isActive else { return }
        store?.handle(i)
    }

    private func attach(_ c: GCController) {
        guard let pad = c.extendedGamepad else { return }
        let buttons: [(GCControllerButtonInput, Store.Input)] = [
            (pad.dpad.left, .left), (pad.dpad.right, .right), (pad.dpad.up, .up), (pad.dpad.down, .down),
            (pad.buttonA, .confirm), (pad.buttonB, .back), (pad.buttonY, .allGames), (pad.buttonMenu, .settings),
        ]
        for (button, input) in buttons {
            button.pressedChangedHandler = { [weak self] _, _, pressed in
                guard pressed else { return }
                Task { @MainActor in self?.send(input) }
            }
        }
        pad.leftThumbstick.valueChangedHandler = { [weak self] _, x, y in
            let dx = x > 0.6 ? 1 : (x < -0.6 ? -1 : 0)
            let dy = y > 0.6 ? 1 : (y < -0.6 ? -1 : 0)
            Task { @MainActor in self?.moved(dx, dy) }
        }
    }

    /// the stick counts once each time it is pushed past the edge
    private func moved(_ dx: Int, _ dy: Int) {
        if dx != stick.x, dx != 0 { send(dx > 0 ? .right : .left) }
        if dy != stick.y, dy != 0 { send(dy > 0 ? .up : .down) }
        stick = (dx, dy)
    }
}
