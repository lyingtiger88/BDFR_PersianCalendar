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
    static func image() -> UIImage? { UIImage(contentsOfFile: location.path) }
}

struct ZurvanBackgroundView: View {
    var body: some View {
        if let image = ZurvanBackgroundStore.image() {
            Image(uiImage: image)
                .resizable()
                .scaledToFill()
                .opacity(0.23)
                .allowsHitTesting(false)
        }
    }
}
