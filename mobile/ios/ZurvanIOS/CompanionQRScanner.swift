import AVFoundation
import SwiftUI
import UIKit

struct CompanionQRScannerView: UIViewControllerRepresentable {
    let onScanned: (String) -> Void
    let onCancel: () -> Void

    func makeCoordinator() -> Coordinator {
        Coordinator(
            onScanned: onScanned,
            onCancel: onCancel
        )
    }

    func makeUIViewController(
        context: Context
    ) -> QRScannerViewController {
        let controller = QRScannerViewController()
        controller.coordinator = context.coordinator
        return controller
    }

    func updateUIViewController(
        _ uiViewController: QRScannerViewController,
        context: Context
    ) {
    }

    final class Coordinator: NSObject,
        AVCaptureMetadataOutputObjectsDelegate {
        private let onScanned: (String) -> Void
        private let onCancel: () -> Void
        private var completed = false

        init(
            onScanned: @escaping (String) -> Void,
            onCancel: @escaping () -> Void
        ) {
            self.onScanned = onScanned
            self.onCancel = onCancel
        }

        func metadataOutput(
            _ output: AVCaptureMetadataOutput,
            didOutput metadataObjects: [AVMetadataObject],
            from connection: AVCaptureConnection
        ) {
            guard !completed,
                  let object =
                    metadataObjects.first
                        as? AVMetadataMachineReadableCodeObject,
                  object.type == .qr,
                  let value = object.stringValue else {
                return
            }

            completed = true
            onScanned(value)
        }

        func cancel() {
            guard !completed else { return }
            completed = true
            onCancel()
        }
    }
}

final class QRScannerViewController: UIViewController {
    weak var coordinator:
        CompanionQRScannerView.Coordinator?

    private let session = AVCaptureSession()
    private let previewLayer =
        AVCaptureVideoPreviewLayer()

    override func viewDidLoad() {
        super.viewDidLoad()

        view.backgroundColor = .black

        let cancel = UIButton(type: .system)
        cancel.setTitle("لغو", for: .normal)
        cancel.setTitleColor(.white, for: .normal)
        cancel.backgroundColor =
            UIColor.black.withAlphaComponent(0.55)
        cancel.layer.cornerRadius = 12
        cancel.addTarget(
            self,
            action: #selector(cancelTapped),
            for: .touchUpInside
        )
        cancel.translatesAutoresizingMaskIntoConstraints =
            false
        view.addSubview(cancel)

        NSLayoutConstraint.activate([
            cancel.topAnchor.constraint(
                equalTo:
                    view.safeAreaLayoutGuide.topAnchor,
                constant: 12
            ),
            cancel.trailingAnchor.constraint(
                equalTo: view.trailingAnchor,
                constant: -16
            ),
            cancel.widthAnchor.constraint(
                equalToConstant: 72
            ),
            cancel.heightAnchor.constraint(
                equalToConstant: 42
            )
        ])

        configureCapture()
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        previewLayer.frame = view.bounds
    }

    override func viewWillAppear(_ animated: Bool) {
        super.viewWillAppear(animated)
        if !session.isRunning {
            DispatchQueue.global(
                qos: .userInitiated
            ).async { [weak self] in
                self?.session.startRunning()
            }
        }
    }

    override func viewWillDisappear(_ animated: Bool) {
        super.viewWillDisappear(animated)
        if session.isRunning {
            session.stopRunning()
        }
    }

    private func configureCapture() {
        guard let camera =
                AVCaptureDevice.default(
                    for: .video
                ),
              let input =
                try? AVCaptureDeviceInput(
                    device: camera
                ),
              session.canAddInput(input) else {
            return
        }

        session.addInput(input)

        let output = AVCaptureMetadataOutput()
        guard session.canAddOutput(output) else {
            return
        }

        session.addOutput(output)
        output.setMetadataObjectsDelegate(
            coordinator,
            queue: .main
        )
        output.metadataObjectTypes = [.qr]

        previewLayer.session = session
        previewLayer.videoGravity =
            .resizeAspectFill
        view.layer.insertSublayer(
            previewLayer,
            at: 0
        )
    }

    @objc
    private func cancelTapped() {
        coordinator?.cancel()
    }
}
