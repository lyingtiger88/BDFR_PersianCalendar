import SwiftUI
import PhotosUI

private enum ZurvanTab: Hashable {
    case calendar
    case desk
    case plans
    case occasions
    case reminders
    case companion
    case settings
}

struct ContentView: View {
    @AppStorage("zurvan.appearance.theme") private var themeID = "windows-light"
    @State private var selection: ZurvanTab = .calendar

    var body: some View {
        TabView(selection: $selection) {
            NavigationStack {
                ZurvanCalendarView()
                    .navigationTitle("Zurvan | زروان")
            }
            .tabItem {
                Label("تقویم", systemImage: "calendar")
            }
            .tag(ZurvanTab.calendar)

            NavigationStack {
                ZurvanCloudShellView()
                    .navigationTitle("Zurvan Desk")
            }
            .tabItem {
                Label("زروان دسک", systemImage: "externaldrive.connected.to.line.below")
            }
            .tag(ZurvanTab.desk)

            NavigationStack {
                PlaceholderView(title: "برنامه‌ها")
            }
            .tabItem {
                Label("برنامه‌ها", systemImage: "checklist")
            }
            .tag(ZurvanTab.plans)

            NavigationStack {
                ZurvanOccasionsView()
            }
            .tabItem {
                Label("مناسبت‌ها", systemImage: "sparkles")
            }
            .tag(ZurvanTab.occasions)

            NavigationStack {
                PlaceholderView(title: "یادآورها")
            }
            .tabItem {
                Label("یادآورها", systemImage: "bell")
            }
            .tag(ZurvanTab.reminders)

            NavigationStack {
                CompanionShellView()
                    .navigationTitle("Companion")
            }
            .tabItem {
                Label("همراه", systemImage: "laptopcomputer.and.iphone")
            }
            .tag(ZurvanTab.companion)

            NavigationStack {
                MobileSettingsView()
            }
            .tabItem {
                Label("تنظیمات", systemImage: "gearshape")
            }
            .tag(ZurvanTab.settings)
        }
        .tint(ZurvanIOSAppearance.accent(themeID))
        .preferredColorScheme(themeID == "graphite-night" ? .dark : .light)
    }
}

private struct CalendarShellView: View {
    var body: some View {
        ScrollView {
            VStack(spacing: 16) {
                HStack {
                    Button(action: {}) {
                        Image(systemName: "chevron.right")
                    }

                    Spacer()

                    VStack(spacing: 3) {
                        Text("مهر ۱۴۰۵")
                            .font(.title2.bold())
                        Text("Persian · Hijri · Gregorian")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }

                    Spacer()

                    Button(action: {}) {
                        Image(systemName: "chevron.left")
                    }
                }

                VStack(alignment: .leading, spacing: 10) {
                    Text("نمای تقویم ماهانه")
                        .font(.headline)

                    Text("تقویم اول بزرگ، تقویم دوم بالا-راست و تقویم سوم پایین-چپ نمایش داده می‌شود.")

                    Text("قابلیت‌های ثبت‌شده در Feature Parity Contract به همین رابط منتقل می‌شوند.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding()
                .background(.thinMaterial, in: RoundedRectangle(cornerRadius: 18))
            }
            .padding()
        }
    }
}

private struct CompanionShellView: View {
    @StateObject private var model =
        CompanionPairingViewModel()

    var body: some View {
        ScrollView {
            VStack(
                alignment: .leading,
                spacing: 16
            ) {
                VStack(
                    alignment: .leading,
                    spacing: 10
                ) {
                    Text(
                        "جفت‌سازی امن شبیه SHAREit"
                    )
                    .font(.headline)

                    Text(
                        "در رایانه یا دستگاه Master یک QR یک‌بارمصرف ساخته می‌شود. این iPhone/iPad با اسکن QR مستقیماً روی شبکه محلی وصل می‌شود و Sync Frameها با ECDH P-256 و AES-GCM رمز می‌شوند."
                    )
                    .foregroundStyle(
                        .secondary
                    )

                    Button {
                        model.scan()
                    } label: {
                        Label(
                            "اسکن QR دستگاه Master",
                            systemImage:
                                "qrcode.viewfinder"
                        )
                    }
                    .buttonStyle(
                        .borderedProminent
                    )
                    .disabled(
                        model.isBusy
                    )

                    if model.isBusy {
                        HStack {
                            ProgressView()
                            Text(model.status)
                        }
                    } else {
                        Text(model.status)
                            .font(.subheadline)
                    }

                    if let peer =
                        model.peerName {
                        Text(
                            "دستگاه مقابل: \(peer)"
                        )
                    }

                    if let sas =
                        model.sas {
                        Text(
                            "کد امنیتی مشترک: \(sas)"
                        )
                        .font(
                            .title3.bold()
                        )
                    }

                    if model
                        .receivedItemCount >
                        0 {
                        Text(
                            "آیتم‌های دریافت‌شده: \(model.receivedItemCount)"
                        )
                    }

                    if let error =
                        model.errorMessage {
                        Text(error)
                            .foregroundStyle(
                                .red
                            )
                            .font(.footnote)
                    }
                }
                .frame(
                    maxWidth: .infinity,
                    alignment: .leading
                )
                .padding()
                .background(
                    .thinMaterial,
                    in:
                        RoundedRectangle(
                            cornerRadius: 18
                        )
                )

                VStack(
                    alignment: .leading,
                    spacing: 6
                ) {
                    Text("امنیت Pairing")
                        .font(.headline)
                    Text(
                        "QR فقط اطلاعات جلسه کوتاه‌عمر، آدرس LAN و کلید عمومی موقت را حمل می‌کند؛ اطلاعات تقویم داخل QR نیست. پس از Handshake، Snapshot رمز‌شده دریافت و در فضای Application Support زروان ذخیره می‌شود."
                    )
                    .font(.footnote)
                    .foregroundStyle(
                        .secondary
                    )
                }
                .frame(
                    maxWidth: .infinity,
                    alignment: .leading
                )
                .padding()
                .background(
                    .thinMaterial,
                    in:
                        RoundedRectangle(
                            cornerRadius: 18
                        )
                )
            }
            .padding()
        }
        .sheet(
            isPresented:
                $model
                    .isScannerPresented
        ) {
            CompanionQRScannerView {
                payload in
                model.handleScannedPayload(
                    payload
                )
            } onCancel: {
                model.cancelScanner()
            }
            .ignoresSafeArea()
        }
    }
}

private struct ZurvanCloudShellView: View {
    @State private var identifier = ""
    @State private var password = ""
    @State private var displayName = ""
    @State private var creatingAccount = false
    @State private var session: DeskSessionIOS?
    @State private var files: [DeskFileIOS] = []
    @State private var isBusy = false
    @State private var errorMessage: String?
    private let api = ZurvanDeskAccountAPI()

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("زروان دسک")
                    .font(.title2.bold())
                Text("فضای شخصی فایل‌ها و نسخه‌های پشتیبان شما")
                    .foregroundStyle(.secondary)

                if let session {
                    VStack(alignment: .leading, spacing: 12) {
                        Text(session.displayName ?? session.identifier)
                            .font(.headline)
                        Button("خروج از حساب") {
                            run {
                                try await api.logout(token: session.token)
                                self.session = nil
                                files = []
                                password = ""
                            }
                        }
                        .disabled(isBusy)
                        Divider()
                        Text("فایل‌های من").font(.headline)
                        Button("به‌روزرسانی فایل‌ها") { refreshFiles() }
                            .disabled(isBusy)
                        if files.isEmpty {
                            Text("فایلی برای نمایش وجود ندارد.")
                                .foregroundStyle(.secondary)
                        }
                        ForEach(files) { file in
                            HStack {
                                Image(systemName: "doc")
                                VStack(alignment: .leading) {
                                    Text("فایل رمزگذاری‌شده")
                                    Text("\(file.size) بایت")
                                        .font(.caption)
                                        .foregroundStyle(.secondary)
                                }
                                Spacer()
                                Button(role: .destructive) {
                                    run {
                                        try await api.deleteFile(token: session.token, fileID: file.id)
                                        files = try await api.listFiles(token: session.token)
                                    }
                                } label: {
                                    Image(systemName: "trash")
                                }
                                .disabled(isBusy)
                                .accessibilityLabel("حذف فایل")
                            }
                        }
                        Text("نام فایل‌ها تا زمان آماده‌شدن رمزگشایی امن در iOS نمایش داده نمی‌شود.")
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                    }
                } else {
                    Picker("حساب", selection: $creatingAccount) {
                        Text("ورود").tag(false)
                        Text("ساخت حساب").tag(true)
                    }
                    .pickerStyle(.segmented)
                    TextField("ایمیل یا شناسه حساب", text: $identifier)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .textContentType(.username)
                    SecureField("رمز عبور", text: $password)
                        .textContentType(creatingAccount ? .newPassword : .password)
                    if creatingAccount {
                        TextField("نام نمایشی (اختیاری)", text: $displayName)
                    }
                    Button(creatingAccount ? "ساخت حساب" : "ورود") {
                        run {
                            let result = try await (creatingAccount
                                ? api.register(identifier: identifier, password: password, displayName: displayName)
                                : api.login(identifier: identifier, password: password))
                            session = result
                            password = ""
                            files = try await api.listFiles(token: result.token)
                        }
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(isBusy || identifier.isEmpty || password.isEmpty || !api.isConfigured)
                    Text("بازیابی اطلاعات رمزگذاری‌شده فقط با عبارت بازیابی ۲۴ کلمه‌ای یا دستگاه مورد اعتماد امکان‌پذیر است.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                    if !api.isConfigured {
                        Text("سرویس حساب زروان دسک هنوز برای این نسخه پیکربندی نشده است.")
                            .foregroundStyle(.secondary)
                    }
                }
                if isBusy { ProgressView() }
                if let errorMessage {
                    Text(errorMessage)
                        .foregroundStyle(.red)
                        .font(.footnote)
                }
            }
            .textFieldStyle(.roundedBorder)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding()
        }
    }

    private func refreshFiles() {
        guard let session else { return }
        run { files = try await api.listFiles(token: session.token) }
    }

    private func run(_ action: @escaping () async throws -> Void) {
        isBusy = true
        errorMessage = nil
        Task {
            do { try await action() }
            catch { errorMessage = error.localizedDescription }
            isBusy = false
        }
    }
}

private struct MobileSettingsView: View {
    @AppStorage("zurvan.appearance.theme") private var themeID = "windows-light"
    @State private var selectedBackground: PhotosPickerItem?
    @State private var backgroundError: String?
    @State private var backgroundAvailable = ZurvanBackgroundStore.exists
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("تنظیمات")
                    .font(.title2.bold())

                VStack(alignment: .leading, spacing: 8) {
                    Text("تم ظاهری").font(.headline)
                    Picker("تم", selection: $themeID) {
                        ForEach(ZurvanIOSAppearance.presets, id: \.0) { preset in
                            Text(preset.1).tag(preset.0)
                        }
                    }
                    .pickerStyle(.menu)
                    Text("تم‌های متناظر با ویندوز و اندروید؛ انتخاب در دستگاه ذخیره می‌شود.")
                        .font(.footnote).foregroundStyle(.secondary)
                }
                .padding()
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(.thinMaterial, in: RoundedRectangle(cornerRadius: 18))

                VStack(alignment: .leading, spacing: 8) {
                    Text("پس‌زمینه سفارشی").font(.headline)
                    PhotosPicker(selection: $selectedBackground, matching: .images) {
                        Label("انتخاب تصویر از گالری", systemImage: "photo")
                    }
                    if backgroundAvailable {
                        Button("حذف پس‌زمینه") {
                            ZurvanBackgroundStore.remove()
                            backgroundAvailable = false
                        }
                    }
                    if let backgroundError {
                        Text(backgroundError).foregroundStyle(.red).font(.footnote)
                    }
                }
                .onChange(of: selectedBackground) { _, newValue in
                    guard let newValue else { return }
                    Task {
                        do {
                            guard let data = try await newValue.loadTransferable(type: Data.self),
                                  data.count < 20_000_000 else {
                                throw ZurvanBackgroundError.invalidImage
                            }
                            try ZurvanBackgroundStore.save(data)
                            backgroundAvailable = true
                            backgroundError = nil
                        } catch {
                            backgroundError = error.localizedDescription
                        }
                    }
                }
                .padding()
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(.thinMaterial, in: RoundedRectangle(cornerRadius: 18))

                VStack(alignment: .leading, spacing: 8) {
                    Text("چیدمان ناوبری")
                        .font(.headline)

                    Text("در iPhone سربرگ‌ها در پایین صفحه قرار می‌گیرند تا فضای تقویم حفظ شود؛ ترتیب با نسخه Windows یکسان است و تنظیمات همیشه آخر قرار دارد.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)

                    Text("در iPad، NavigationSplitView/Sidebar کناری در مسیر UI تطبیقی زروان فعال خواهد شد.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding()
                .background(
                    .thinMaterial,
                    in: RoundedRectangle(cornerRadius: 18)
                )
            }
            .padding()
        }
        .navigationTitle("تنظیمات")
    }
}

private struct PlaceholderView: View {
    let title: String

    var body: some View {
        ContentUnavailableView(
            title,
            systemImage: "hammer",
            description: Text("در حال توسعه")
        )
        .navigationTitle(title)
    }
}
