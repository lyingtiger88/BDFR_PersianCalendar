import SwiftUI
import UIKit

enum ZurvanIOSAppearance {
    static let presets: [(String, String)] = [
        ("windows-light", "Windows Light"),
        ("azure-glass", "Azure Glass"),
        ("graphite-night", "Graphite Night"),
        ("warm-sand", "Warm Sand"),
        ("zara-pastel", "Zara Pastel"),
        ("season-vibe", "Season Vibe"),
        ("elena-mode", "Elena Mode")
    ]

    static func accent(_ id: String) -> Color {
        switch id {
        case "azure-glass": return Color(red: 0.36, green: 0.66, blue: 0.91)
        case "graphite-night": return Color(red: 0.57, green: 0.68, blue: 0.82)
        case "warm-sand": return Color(red: 0.72, green: 0.48, blue: 0.25)
        case "season-vibe": return Color(red: 0.34, green: 0.61, blue: 0.85)
        case "elena-mode": return Color(red: 0.75, green: 0.54, blue: 0.38)
        case "zara-pastel": return Color(red: 0.49, green: 0.67, blue: 0.86)
        default: return Color(red: 0.28, green: 0.52, blue: 0.85)
        }
    }

    static let colorKeys: [(String, String)] = [
        ("background", "پس‌زمینه"),
        ("surface", "پنل‌ها"),
        ("card", "کارت‌ها"),
        ("text", "متن‌ها"),
        ("accent", "رنگ تأکیدی و دکمه‌ها"),
        ("holiday", "تعطیلی و هشدار")
    ]

    static func color(_ theme: String, key: String) -> Color {
        let storageKey = "zurvan.color.\(theme).\(key)"
        if let hex = UserDefaults.standard.string(forKey: storageKey),
           hex.count == 7, hex.hasPrefix("#"),
           let number = UInt64(hex.dropFirst(), radix: 16) {
            return Color(red: Double((number >> 16) & 255) / 255,
                         green: Double((number >> 8) & 255) / 255,
                         blue: Double(number & 255) / 255)
        }
        if key == "accent" { return accent(theme) }
        switch key {
        case "holiday": return Color.red
        case "text": return theme == "graphite-night" ? .white : .primary
        case "background": return theme == "graphite-night" ? Color(red: 0.09, green: 0.10, blue: 0.12) : .white
        case "card": return theme == "graphite-night" ? Color(red: 0.16, green: 0.18, blue: 0.21) : Color(white: 0.96)
        default: return theme == "graphite-night" ? Color(red: 0.13, green: 0.15, blue: 0.18) : Color(white: 0.98)
        }
    }

    static func setColor(_ color: Color, themeID: String, key: String) {
        let ui = UIColor(color)
        var r: CGFloat = 0, g: CGFloat = 0, b: CGFloat = 0, a: CGFloat = 0
        guard ui.getRed(&r, green: &g, blue: &b, alpha: &a) else { return }
        let hex = String(format: "#%02X%02X%02X", Int(r*255), Int(g*255), Int(b*255))
        UserDefaults.standard.set(hex, forKey: "zurvan.color.\(themeID).\(key)")
    }

    static func resetColor(themeID: String, key: String) {
        UserDefaults.standard.removeObject(forKey: "zurvan.color.\(themeID).\(key)")
    }
}
