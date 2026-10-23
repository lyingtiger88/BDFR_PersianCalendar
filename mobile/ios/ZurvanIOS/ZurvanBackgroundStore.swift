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
        if let image = ZurvanBackgroundStore.image(themeID: themeID, elenaIndex: elenaIndex) {
            Image(uiImage: image)
                .resizable()
                .scaledToFill()
                .opacity(0.23)
                .allowsHitTesting(false)
        }
    }
}
