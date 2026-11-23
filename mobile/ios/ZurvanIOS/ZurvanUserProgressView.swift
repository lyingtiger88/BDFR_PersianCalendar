import SwiftUI

/// Read-only profile plus explicit daily award and private leaderboard choice.
/// Permanent special ranks are issued by admin, never by XP thresholds.
struct ZurvanUserProgressView: View {
    @State private var snapshot: [String: Any]?
    @State private var busy = false
    @State private var message = ""

    private var profile: [String: Any] {
        snapshot?["profile"] as? [String: Any] ?? [:]
    }
    private var badges: [[String: Any]] {
        snapshot?["badges"] as? [[String: Any]] ?? []
    }
    private var history: [[String: Any]] {
        snapshot?["history"] as? [[String: Any]] ?? []
    }
    private var leaders: [[String: Any]] {
        snapshot?["leaderboard"] as? [[String: Any]] ?? []
    }
    private func count(_ data: [String:Any], _ key: String) -> Int {
        data[key] as? Int ?? 0
    }
    private func caption(_ data: [String:Any], _ key: String) -> String {
        data[key] as? String ?? ""
    }
    private func enabled(_ data: [String:Any], _ key: String) -> Bool {
        data[key] as? Bool == true
    }
    private func reload(_ action: String = "status", visible: Bool? = nil) async {
        busy = true
        defer { busy = false }
        do {
            let result = try await ZurvanIOSLicense.userProgress(action:action,visible:visible)
            snapshot = result
            if action == "claim_daily" {
                let claim = result["claim"] as? [String:Any] ?? [:]
                message = enabled(claim,"claimed")
                    ? "امتیاز امروز با موفقیت ثبت شد."
                    : "امتیاز امروز را قبلاً دریافت کرده‌ای."
            } else if action == "set_visibility" {
                message = "تنظیم حریم خصوصی رتبه‌بندی ذخیره شد."
            } else if action == "claim_silver" {
                let claim = result["claim"] as? [String:Any] ?? [:]
                message = enabled(claim,"claimed")
                    ? "لایسنس یک‌ساله Premium Silver صادر شد."
                    : "هنوز دعوت موفق کافی برای دریافت جایزه ثبت نشده است."
            } else {
                message = ""
            }
        } catch {
            message = error.localizedDescription
        }
    }

    var body: some View {
        Form {
            if busy { ProgressView("در حال دریافت از سرور...") }
            if !message.isEmpty {
                Section { Text(message).font(.footnote).foregroundStyle(.secondary) }
            }
            if let snapshot {
                Section("امتیاز و سطح") {
                    Text("\(count(profile,"total_xp")) XP")
                        .font(.title.bold())
                    Text("سطح \(count(profile,"level")) · \(caption(profile,"level_title"))")
                    ProgressView(value:Double(count(profile,"progress_percent")),total:100)
                    if profile["next_level_xp"] is NSNull {
                        Text("به بالاترین سطح فعلی رسیده‌ای.")
                    } else {
                        Text("\(count(profile,"xp_to_next")) امتیاز تا سطح بعدی")
                    }
                    Text("حضور متوالی: \(count(profile,"daily_streak")) روز · بهترین رکورد: \(count(profile,"best_streak")) روز")
                    Button("ثبت حضور و دریافت امتیاز امروز") {
                        Task { await reload("claim_daily") }
                    }
                    .disabled(busy || enabled(profile,"checked_in_today"))
                }
                if let referral = snapshot["referral"] as? [String:Any] {
                    Section("دعوت دوستان با شناسه معرف") {
                        Text("شناسه معرف شما: \(caption(referral,"identifier"))")
                            .font(.headline.monospaced())
                            .textSelection(.enabled)
                        Button("کپی شناسه معرف", systemImage:"doc.on.doc") {
                            UIPasteboard.general.string=caption(referral,"identifier")
                            message="شناسه معرف کپی شد."
                        }
                        Text("شناسه را به دوستان بده تا هنگام ثبت‌نام وارد کنند؛ لینک لازم نیست.")
                            .font(.footnote).foregroundStyle(.secondary)
                        Text("کل دعوت‌ها: \(count(referral,"total_invited")) · موفق: \(count(referral,"successful"))")
                        Text("در انتظار تأیید: \(count(referral,"pending")) · جوایز Silver: \(count(referral,"earned_rewards"))")
                        Text("تا پاداش بعدی: \(count(referral,"remaining_to_next")) نفر")
                        Text("هر دعوت موفق ۱۰۰ XP؛ هر ۱۵ دعوت موفق یک‌سال Premium Silver")
                            .font(.footnote).foregroundStyle(.secondary)
                        if enabled(referral,"reward_available") {
                            Button("دریافت لایسنس یک‌ساله Premium Silver") {
                                Task { await reload("claim_silver") }
                            }.disabled(busy)
                        }
                        if let silver = referral["current_silver"] as? [String:Any] {
                            Text("Premium Silver فعال تا: \(caption(silver,"expires_at"))")
                                .font(.footnote)
                        }
                    }
                }
                if let special = snapshot["special_rank"] as? [String:Any] {
                    Section("نشان ویژه مدیر") {
                        Label(caption(special,"display_name_fa"),systemImage:"crown.fill")
                        Text("این نشان مستقل از XP است و فقط توسط مدیریت صادر می‌شود.")
                            .font(.footnote).foregroundStyle(.secondary)
                    }
                }
                Section("نشان‌های کسب‌کردنی") {
                    ForEach(Array(badges.enumerated()),id:\.offset) { _, badge in
                        HStack(spacing:10) {
                            Text(enabled(badge,"earned") ? "✅" : "🔒")
                            VStack(alignment:.leading,spacing:3) {
                                Text(caption(badge,"icon")+" "+caption(badge,"name_fa"))
                                    .font(.subheadline.bold())
                                Text(caption(badge,"description_fa"))
                                    .font(.caption).foregroundStyle(.secondary)
                            }
                        }
                    }
                }
                Section("تاریخچه امتیازها") {
                    if history.isEmpty { Text("هنوز امتیازی ثبت نشده است.") }
                    ForEach(Array(history.prefix(10).enumerated()),id:\.offset) { _, item in
                        HStack {
                            Text(caption(item,"title"))
                            Spacer()
                            Text("+\(count(item,"points")) XP").foregroundStyle(.green)
                        }
                    }
                }
                Section("رتبه‌بندی اختیاری") {
                    Toggle("نمایش حساب در رتبه‌بندی",isOn:Binding(
                        get: { enabled(profile,"leaderboard_opt_in") },
                        set: { newValue in
                            Task { await reload("set_visibility",visible:newValue) }
                        }
                    )).disabled(busy)
                    Text("فقط نام کاربری و XP نمایش داده می‌شود؛ ایمیل خصوصی می‌ماند.")
                        .font(.footnote).foregroundStyle(.secondary)
                    if leaders.isEmpty { Text("هنوز کسی وارد رتبه‌بندی نشده است.") }
                    ForEach(Array(leaders.enumerated()),id:\.offset) { _, user in
                        HStack {
                            Text("\(count(user,"position")). \(caption(user,"display_name"))")
                            Spacer()
                            Text("\(count(user,"total_xp")) XP")
                            if enabled(user,"is_self") { Text("شما").foregroundStyle(.tint) }
                        }
                    }
                }
                Section {
                    Button("به‌روزرسانی امتیاز و نشان‌ها") {
                        Task { await reload() }
                    }.disabled(busy)
                }
            } else {
                Section {
                    Text("برای مشاهده امتیازها و نشان‌ها، با حساب تأییدشده زروان وارد شوید.")
                    Button("دریافت اطلاعات حساب") { Task { await reload() } }
                        .disabled(busy)
                }
            }
        }
        .navigationTitle("امتیاز و نشان‌های من")
        .task { await reload() }
    }
}
