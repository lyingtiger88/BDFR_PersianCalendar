import SwiftUI
import UIKit

enum ZurvanBackgroundError: LocalizedError {
    case invalidImage
    var errorDescription: String? { "تصویر انتخاب‌شده معتبر نیست یا حجم آن بیش از حد مجاز است." }
}

enum ZurvanBackgroundStore {
    private static var location: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("zurvan-custom-background.png")
    }
    static var exists: Bool { FileManager.default.fileExists(atPath: location.path) }
    static func save(_ data: Data) throws {
        guard let image = UIImage(data: data),
              let jpeg = image.jpegData(compressionQuality: 0.82) else {
            throw ZurvanBackgroundError.invalidImage
        }
        try FileManager.default.createDirectory(
            at: location.deletingLastPathComponent(), withIntermediateDirectories: true)
        try jpeg.write(to: location, options: .atomic)
    }
    static func remove() { try? FileManager.default.removeItem(at: location) }
    static func image(themeID: String = "windows-light", elenaIndex: Int = 1) -> UIImage? {
        if let custom = UIImage(contentsOfFile: location.path) { return custom }
        if themeID == "season-vibe" {
            let month = Calendar(identifier: .persian).component(.month, from: Date())
            let name = month <= 3 ? "spring" : month <= 6 ? "summer" : month <= 9 ? "autumn" : "winter"
            if let url = Bundle.main.url(forResource: name, withExtension: "jpg", subdirectory: "themes/season-vibe") {
                return UIImage(contentsOfFile: url.path)
            }
        }
        if themeID == "elena-mode" {
            let name = String(format: "elena-%02d", min(max(elenaIndex, 1), 11))
            if let url = Bundle.main.url(forResource: name, withExtension: "jpg", subdirectory: "themes/elena-mode") {
                return UIImage(contentsOfFile: url.path)
            }
        }
        return nil
    }
}

struct ZurvanBackgroundView: View {
    @AppStorage("zurvan.appearance.theme") private var themeID = "windows-light"
    @AppStorage("zurvan.elena.image") private var elenaIndex = 1
    var body: some View {
        if themeID == "bloom-pastel" {
            BloomPastelWallpaperView().allowsHitTesting(false)
        } else if let image = ZurvanBackgroundStore.image(themeID: themeID, elenaIndex: elenaIndex) {
            Image(uiImage: image)
                .resizable()
                .scaledToFill()
                .opacity(0.23)
                .allowsHitTesting(false)
        }
    }
}

private struct BloomPastelWallpaperView: View {
    private let flowers: [Color] = [
        Color(red: 0.92, green: 0.66, blue: 0.70).opacity(0.48),
        Color(red: 0.91, green: 0.77, blue: 0.61).opacity(0.48),
        Color(red: 0.77, green: 0.72, blue: 0.86).opacity(0.48),
        Color(red: 0.93, green: 0.74, blue: 0.66).opacity(0.48)
    ]
    var body: some View {
        Canvas { context, size in
            let stepX: CGFloat = 92
            let stepY: CGFloat = 88
            var row = 0
            var y: CGFloat = 22
            while y < size.height + stepY {
                var x: CGFloat = row.isMultiple(of: 2) ? 20 : 64
                while x < size.width + stepX {
                    let color = flowers[(row + Int(x / stepX)) % flowers.count]
                    for petal in 0..<5 {
                        let angle = Double(petal) * 2 * Double.pi / 5
                        let px = x + CGFloat(cos(angle) * 6)
                        let py = y + CGFloat(sin(angle) * 6)
                        context.fill(Path(ellipseIn: CGRect(x: px - 4.5, y: py - 4.5, width: 9, height: 9)), with: .color(color))
                    }
                    context.fill(Path(ellipseIn: CGRect(x: x - 2.5, y: y - 2.5, width: 5, height: 5)), with: .color(Color(red: 0.85, green: 0.72, blue: 0.44).opacity(0.65)))
                    var stem = Path()
                    stem.move(to: CGPoint(x: x, y: y + 9))
                    stem.addQuadCurve(to: CGPoint(x: x - 5, y: y + 24), control: CGPoint(x: x - 2, y: y + 17))
                    context.stroke(stem, with: .color(Color(red: 0.61, green: 0.68, blue: 0.55).opacity(0.28)), lineWidth: 1)
                    x += stepX
                }
                y += stepY
                row += 1
            }
        }
        .background(Color(red: 1.0, green: 0.973, blue: 0.961))
    }
}