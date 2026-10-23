import SwiftUI

enum ZurvanIOSAppearance {
    static let presets: [(String, String)] = [
        ("windows-light", "Windows Light"),
        ("azure-glass", "Azure Glass"),
        ("graphite-night", "Graphite Night"),
        ("warm-sand", "Warm Sand"),
        ("zara-pastel", "Zara Pastel")
    ]

    static func accent(_ id: String) -> Color {
        switch id {
        case "azure-glass": return Color(red: 0.36, green: 0.66, blue: 0.91)
        case "graphite-night": return Color(red: 0.57, green: 0.68, blue: 0.82)
        case "warm-sand": return Color(red: 0.72, green: 0.48, blue: 0.25)
        case "zara-pastel": return Color(red: 0.49, green: 0.67, blue: 0.86)
        default: return Color(red: 0.28, green: 0.52, blue: 0.85)
        }
    }
}
