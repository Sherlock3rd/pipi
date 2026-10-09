import AppKit
import CoreGraphics

let app = NSApplication.shared
app.setActivationPolicy(.accessory)
let report = CommandLine.arguments[1]
var downs = 0
var ups = 0
var drags = 0
func save() {
    let state: [String: Any] = ["downs": downs, "ups": ups, "drags": drags,
        "frontPid": NSWorkspace.shared.frontmostApplication?.processIdentifier ?? -1,
        "postAccess": CGPreflightPostEventAccess()]
    let data = try! JSONSerialization.data(withJSONObject: state)
    try! data.write(to: URL(fileURLWithPath: report), options: .atomic)
}
final class Desktop: NSView {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func mouseDown(with event: NSEvent) { downs += 1; save() }
    override func mouseUp(with event: NSEvent) { ups += 1; save() }
    override func mouseDragged(with event: NSEvent) { drags += 1; save() }
    override func rightMouseDown(with event: NSEvent) { downs += 1; save() }
    override func rightMouseUp(with event: NSEvent) { ups += 1; save() }
}
let window = NSWindow(contentRect: NSScreen.screens[0].visibleFrame, styleMask: .borderless, backing: .buffered, defer: false)
window.title = "Chenpi desktop input receiver"
window.backgroundColor = NSColor(calibratedWhite: 0.2, alpha: 0.04)
window.isOpaque = false
window.hasShadow = false
window.level = NSWindow.Level(rawValue: Int(CGWindowLevelForKey(.desktopIconWindow)))
window.contentView = Desktop(frame: NSRect(origin: .zero, size: window.frame.size))
window.orderFrontRegardless()
save()
DispatchQueue.global().async {
    while let line = readLine() {
        let data = line.data(using: .utf8)!
        let command = try! JSONSerialization.jsonObject(with: data) as! [String: Any]
        DispatchQueue.main.sync {
            if let kind = command["event"] as? String {
                let types: [String: CGEventType] = ["move": .mouseMoved, "down": .leftMouseDown,
                    "up": .leftMouseUp, "drag": .leftMouseDragged, "rightDown": .rightMouseDown, "rightUp": .rightMouseUp]
                let point = CGPoint(x: command["x"] as! Double, y: command["y"] as! Double)
                let event = CGEvent(mouseEventSource: CGEventSource(stateID: .hidSystemState), mouseType: types[kind]!, mouseCursorPosition: point, mouseButton: kind.hasPrefix("right") ? .right : .left)!
                event.post(tap: .cghidEventTap)
            }
            if let level = command["level"] as? String {
                window.level = NSWindow.Level(rawValue: Int(CGWindowLevelForKey(level == "normal" ? .normalWindow : .desktopIconWindow)))
                window.orderFrontRegardless()
            }
            save()
            print("ok")
            fflush(stdout)
        }
    }
    DispatchQueue.main.async { app.terminate(nil) }
}
app.run()
