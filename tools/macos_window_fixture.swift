import AppKit

// Separate-process window fixture. It must not share the pet's PID, because
// the pet correctly excludes its own settings and preview windows.
let app = NSApplication.shared
app.setActivationPolicy(.regular)
let screen = NSScreen.main!
let mode = CommandLine.arguments[1]
let frame = mode == "fullscreen" ? screen.frame : screen.visibleFrame.insetBy(dx: 80, dy: 60)
let window = NSWindow(contentRect: frame, styleMask: .borderless, backing: .buffered, defer: false)
window.title = "Chenpi native window test"
window.backgroundColor = .darkGray
window.makeKeyAndOrderFront(nil)
app.activate(ignoringOtherApps: true)
try! "ready".write(toFile: CommandLine.arguments[2], atomically: true, encoding: .utf8)
DispatchQueue.main.asyncAfter(deadline: .now() + 20) { app.terminate(nil) }
app.run()
