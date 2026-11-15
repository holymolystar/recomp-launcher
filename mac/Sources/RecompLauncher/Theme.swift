// The three looks: Midnight and Ember (dark) and Daylight (light), as on Windows.
import SwiftUI

struct Theme {
    var name: String
    var top: Color
    var bottom: Color
    var panel: Color
    var text: Color
    var muted: Color
    var ring: Color
    var dark: Bool

    static let all: [Theme] = [
        Theme(name: "midnight", top: Color(hex: "#16202A"), bottom: Color(hex: "#0B1117"), panel: Color.white.opacity(0.06),
              text: Color(hex: "#E8E6DF"), muted: Color(hex: "#8A9AA5"), ring: Color(hex: "#3FB8A0"), dark: true),
        Theme(name: "ember", top: Color(hex: "#2A1A16"), bottom: Color(hex: "#120B09"), panel: Color.white.opacity(0.06),
              text: Color(hex: "#F1E6DC"), muted: Color(hex: "#A8948A"), ring: Color(hex: "#E8873C"), dark: true),
        Theme(name: "daylight", top: Color(hex: "#F4F2EC"), bottom: Color(hex: "#DCDDD8"), panel: Color.black.opacity(0.05),
              text: Color(hex: "#1D252B"), muted: Color(hex: "#5F6B73"), ring: Color(hex: "#1F8A74"), dark: false),
    ]
    static func named(_ n: String) -> Theme { all.first { $0.name == n } ?? all[0] }
    var title: String { name.prefix(1).uppercased() + name.dropFirst() }
}

extension Color {
    init(hex: String?) { self.init(nsColor: NSColor(hex: hex)) }
}
