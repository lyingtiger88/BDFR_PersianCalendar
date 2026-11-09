import SwiftUI
import CryptoKit
import Foundation

private struct ZurvanQueuedCollabCommand: Codable, Identifiable {
    let id: String
    let groupID: String
    let kind: String
    let payload: Data
    let createdAt: Date
    var blockedReason: String? = nil
}

struct ZurvanCollaborationView: View {
    @State private var token = ""
    @State private var groups: [ZurvanCollabGroup] = []
    @State private var members: [ZurvanCollabMember] = []
    @State private var groupID = ""
    @State private var groupName = ""
    @State private var newGroupName = ""
    @State private var subjectName = ""
    @State private var subjects: [ZurvanCollabSubject] = []
    @State private var homework: [ZurvanCollabHomework] = []
    @State private var homeworkTitle = ""
    @State private var homeworkInstructions = ""
    @State private var submissionText = ""
    @State private var gradeText = "0"
    @State private var gradeFeedback = ""
    @State private var timetableCount = 0
    @State private var kanbanColumns: [ZurvanCollabColumn] = []
    @State private var kanbanCards: [ZurvanCollabCard] = []
    @State private var kanbanColumnName = ""
    @State private var kanbanTaskTitle = ""
    @State private var agendaTitle = ""
    @State private var messageText = ""
    @State private var sharedItems: [String] = []
    @State private var sharedMessages: [String] = []
    @State private var recurrenceRule = "none"
    @State private var status = "برای استفاده از همکاری آنلاین وارد حساب Zurvan Desk شوید."
    @State private var isBusy = false
    @State private var cursor: Int64 = 0
    @State private var queue: [ZurvanQueuedCollabCommand] = []
    private let api = ZurvanCollaborationAPI()
    private let secureStore = ZurvanCloudSecureStoreIOS()
    private let queueKey = "zurvan.collaboration.offline.queue.v1"

    var body: some View {
        Form {
            Section("گروه آنلاین") {
                if token.isEmpty {
                    Text("ابتدا در زروان دسک وارد شوید؛ نشست ورود به‌صورت امن در Keychain نگهداری می‌شود.")
                        .foregroundStyle(.secondary)
                } else {
                    TextField("نام گروه جدید", text: $newGroupName)
                    Button("ساخت گروه") { run { 
                        let group = try await api.createGroup(token: token, name: newGroupName,
                            agenda: true, kanban: true, school: true)
                        groups.append(group); groupID = group.id; newGroupName = ""
                        status = "گروه ساخته شد."
                    }}
                    Picker("گروه فعال", selection: $groupID) {
                        Text("انتخاب گروه").tag("")
                        ForEach(groups) { group in Text(group.name).tag(group.id) }
                    }
                    Button("بارگذاری گروه‌ها") { run { try await loadGroups() } }
                }
            }
            if !groupID.isEmpty {
                Section("اعضای گروه و مجوزها") {
                    ForEach($members) { member in
                        DisclosureGroup(member.wrappedValue.account?.identifier ?? member.wrappedValue.account_id) {
                            Text("نقش: \(member.wrappedValue.role)").font(.caption).foregroundStyle(.secondary)
                            Toggle("اجازه گفت‌وگو", isOn: member.can_chat)
                            Toggle("اجازه ثبت گزارش", isOn: member.can_report)
                            Toggle("اجازه ویرایش", isOn: member.can_edit)
                            Button("ذخیره مجوزها") {
                                let row = member.wrappedValue
                                run { try await api.setPermissions(token: token, groupID: groupID,
                                    memberID: row.account_id, canChat: row.can_chat,
                                    canReport: row.can_report, canEdit: row.can_edit) }
                            }
                            if member.wrappedValue.role != "owner" {
                                Button("حذف عضو", role: .destructive) {
                                    let row = member.wrappedValue
                                    run { try await api.removeMember(token: token, groupID: groupID, memberID: row.account_id)
                                        members.removeAll { $0.account_id == row.account_id } }
                                }
                            }
                        }
                    }
                    Button("بارگذاری اعضا") { run { try await loadMembers() } }
                }
                Section("Agenda") {
                    TextField("عنوان برنامه", text: $agendaTitle)
                    Picker("تکرار", selection: $recurrenceRule) {
                        Text("بدون تکرار").tag("none")
                        Text("روزانه").tag("daily")
                        Text("روزهای کاری").tag("weekdays")
                        Text("هفتگی").tag("weekly")
                        Text("ماهانه").tag("monthly")
                    }
                    Button("ثبت برنامه مشترک") {
                        let title = agendaTitle.trimmingCharacters(in: .whitespacesAndNewlines)
                        guard !title.isEmpty else { status = "عنوان برنامه را وارد کنید."; return }
                        var payload: [String: Any] = ["title": title, "mode": "agenda",
                            "state": "todo", "timezone": TimeZone.current.identifier]
                        if recurrenceRule != "none" { payload["recurrenceRule"] = recurrenceRule }
                        enqueueOrSend("create_item", payload: payload)
                        agendaTitle = ""
                    }
                }
                Section("Kanban") {
                    TextField("نام ستون", text: $kanbanColumnName)
                    Button("افزودن ستون") {
                        run { try await api.createKanbanColumn(token: token, groupID: groupID, name: kanbanColumnName); kanbanColumnName = ""; try await loadKanban() }
                    }
                    Button("بارگذاری ستون‌ها") { run { try await loadKanban() } }
                    ForEach(kanbanCards) { card in
                        let index = kanbanColumns.firstIndex(where: { $0.id == card.column_id }) ?? -1
                        HStack {
                            VStack(alignment: .leading) {
                                Text(card.title)
                                Text(card.state).font(.caption).foregroundStyle(.secondary)
                            }
                            Spacer()
                            if index >= 0 && index < kanbanColumns.count - 1 {
                                Button("انتقال ←") {
                                    enqueueOrSend("update_item", payload: [
                                        "itemId": card.id, "expectedVersion": card.version,
                                        "title": card.title, "details": card.details,
                                        "state": card.state, "columnId": kanbanColumns[index + 1].id,
                                        "timezone": TimeZone.current.identifier
                                    ])
                                }
                            }
                        }
                    }
                    ForEach(kanbanColumns) { column in
                        let index = kanbanColumns.firstIndex(where: { $0.id == column.id }) ?? 0
                        HStack {
                            Text(column.name)
                            Spacer()
                            if index > 0 {
                                Button { run { var ids = kanbanColumns.map(\.id); ids.swapAt(index, index - 1); try await api.reorderKanban(token: token, groupID: groupID, ids: ids); try await loadKanban() } } label: { Image(systemName: "arrow.up") }
                            }
                            if index < kanbanColumns.count - 1 {
                                Button { run { var ids = kanbanColumns.map(\.id); ids.swapAt(index, index + 1); try await api.reorderKanban(token: token, groupID: groupID, ids: ids); try await loadKanban() } } label: { Image(systemName: "arrow.down") }
                            }
                        }
                    }
                    TextField("عنوان کارت جدید", text: $kanbanTaskTitle)
                    Button("ثبت کارت در ستون اول") {
                        guard let column = kanbanColumns.first else { status = "ابتدا ستون بسازید."; return }
                        let title = kanbanTaskTitle.trimmingCharacters(in: .whitespacesAndNewlines)
                        guard !title.isEmpty else { status = "عنوان کارت را وارد کنید."; return }
                        enqueueOrSend("create_item", payload: ["title": title, "mode": "kanban",
                            "state": "todo", "columnId": column.id, "timezone": TimeZone.current.identifier])
                        kanbanTaskTitle = ""
                    }
                }
                Section("مدرسه") {
                    TextField("نام درس", text: $subjectName)
                    Button("ثبت درس") {
                        run { try await api.createSubject(token: token, groupID: groupID, name: subjectName); subjectName = ""; try await loadSchool() }
                    }
                    Button("بارگذاری دروس و تکالیف") { run { try await loadSchool() } }
                    Text("جلسه‌های ثبت‌شده در برنامه هفتگی: \(timetableCount)").font(.caption).foregroundStyle(.secondary)
                    ForEach(subjects) { subject in
                        VStack(alignment: .leading) {
                            Text(subject.name).font(.headline)
                            Button("ثبت جلسه هفتگی دوشنبه ۸ تا ۹") {
                                run { try await api.createTimetableSlot(token: token, groupID: groupID,
                                    subjectID: subject.id, weekday: 0, startTime: "08:00", endTime: "09:00") }
                            }
                        }
                    }
                    Divider()
                    TextField("عنوان تکلیف", text: $homeworkTitle)
                    TextField("شرح تکلیف", text: $homeworkInstructions, axis: .vertical)
                    Button("ثبت تکلیف برای اولین درس") {
                        guard let subject = subjects.first else { status = "ابتدا یک درس ثبت کنید."; return }
                        run { try await api.createHomework(token: token, groupID: groupID,
                            subjectID: subject.id, title: homeworkTitle, instructions: homeworkInstructions)
                            homeworkTitle = ""; homeworkInstructions = ""; try await loadSchool() }
                    }
                    ForEach(homework) { item in
                        VStack(alignment: .leading, spacing: 8) {
                            Text(item.title).font(.headline)
                            Text(item.instructions).font(.caption).foregroundStyle(.secondary)
                            TextField("پاسخ تکلیف", text: $submissionText, axis: .vertical)
                            Button("ارسال پاسخ") {
                                guard !submissionText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { status = "پاسخ را وارد کنید."; return }
                                run { try await api.submitHomework(token: token, homeworkID: item.id, body: submissionText); submissionText = ""; status = "پاسخ ثبت شد." }
                            }
                        }
                    }
                    TextField("نمره ۰ تا ۱۰۰", text: $gradeText)
                        .keyboardType(.decimalPad)
                    TextField("بازخورد معلم", text: $gradeFeedback, axis: .vertical)
                    Button("تصحیح اولین پاسخ تکلیف") {
                        guard let item = homework.first, let grade = Double(gradeText), (0...100).contains(grade) else {
                            status = "تکلیف و نمره معتبر لازم است."; return
                        }
                        run {
                            let rows = try await api.submissions(token: token, homeworkID: item.id)
                            guard let submission = rows.first else { status = "هنوز پاسخی ثبت نشده است."; return }
                            try await api.gradeSubmission(token: token, homeworkID: item.id,
                                submissionID: submission.id, grade: grade, feedback: gradeFeedback)
                            status = "نمره و بازخورد ثبت شد."
                        }
                    }
                }
            }
            Section("پیام‌های گروه") {
                TextField("پیام جدید", text: $messageText, axis: .vertical)
                Button("ارسال پیام") {
                    let body = messageText.trimmingCharacters(in: .whitespacesAndNewlines)
                    guard !body.isEmpty else { status = "متن پیام را وارد کنید."; return }
                    enqueueOrSend("send_message", payload: ["body": body])
                    messageText = ""
                }
                ForEach(sharedMessages.suffix(12), id: \.self) { Text($0).font(.footnote) }
                ForEach(sharedItems.suffix(12), id: \.self) { Text("• " + $0).font(.caption).foregroundStyle(.secondary) }
            }
            Section("همگام‌سازی و صف آفلاین") {
                Text(status).font(.footnote)
                Text("فرمان‌های ذخیره‌شده روی دستگاه: \(queue.count)")
                    .font(.caption).foregroundStyle(.secondary)
                Button("تلاش مجدد برای ارسال صف") { run { try await flushQueue() } }
            }
        }
        .navigationTitle("برنامه‌ها و همکاری")
        .onChange(of: groupID) { _, _ in cursor = 0 }
        .task {
            token = (try? secureStore.get(ZurvanCloudSecureStoreIOS.deskSessionToken)) ?? ""
            loadQueue()
            if !token.isEmpty { await loadGroups() }
        }
        .task(id: groupID) {
            guard !token.isEmpty, !groupID.isEmpty else { return }
            while !Task.isCancelled {
                do {
                    try await flushQueue()
                    let feed = try await api.changes(token: token, groupID: groupID, after: cursor)
                    cursor = max(cursor, feed.nextCursor)
                    if !feed.events.isEmpty {
                        try await loadSchool()
                        try await loadKanban()
                        try await loadMembers()
                        let items = try await api.items(token: token, groupID: groupID)
                        sharedItems = items.map { item in
                            let next = item.occurrences?.first?.at
                            return item.title + " • " + item.state +
                                (item.recurrence_rule.map { " • تکرار: " + $0 + (next.map { " • " + $0 } ?? "") } ?? "")
                        }
                        let messages = try await api.messages(token: token, groupID: groupID)
                        sharedMessages = messages.compactMap { $0["body"] as? String }
                        status = "همگام‌سازی آنلاین: \(feed.events.count) تغییر جدید"
                    }
                } catch {
                    status = error.localizedDescription.contains("version_conflict")
                        ? "تعارض نسخه در صف آفلاین؛ تغییر نیازمند بازبینی است."
                        : "اتصال موقتاً قطع است؛ صف آفلاین حفظ می‌شود."
                    try? await Task.sleep(for: .seconds(2))
                }
            }
        }
    }

    private func run(_ action: @escaping () async throws -> Void) {
        guard !isBusy else { return }
        isBusy = true
        Task { defer { isBusy = false }; do { try await action() } catch { status = error.localizedDescription } }
    }

    private func loadGroups() async {
        do {
            groups = try await api.groups(token: token)
            if !groups.contains(where: { $0.id == groupID }) { groupID = groups.first?.id ?? "" }
            status = groups.isEmpty ? "هنوز عضو گروهی نیستید." : "گروه‌ها بارگذاری شدند."
            if !groupID.isEmpty { try? await loadMembers() }
        } catch { status = error.localizedDescription }
    }
    private func loadMembers() async throws {
        members = try await api.groupDetail(token: token, groupID: groupID).members
    }
    private func loadSchool() async throws {
        async let s = api.subjects(token: token, groupID: groupID)
        async let h = api.homework(token: token, groupID: groupID)
        async let t = api.timetable(token: token, groupID: groupID)
        subjects = try await s; homework = try await h
        timetableCount = try await t.count
    }
    private func loadKanban() async throws {
        async let c = api.kanbanColumns(token: token, groupID: groupID)
        async let i = api.items(token: token, groupID: groupID)
        kanbanColumns = try await c
        let allItems = try await i
        kanbanCards = allItems.filter { $0.mode == "kanban" }
    }
    private func queueEncryptionKey() throws -> SymmetricKey {
        if let encoded = try secureStore.get(ZurvanCloudSecureStoreIOS.collaborationQueueKey),
           let data = Data(base64Encoded: encoded) { return SymmetricKey(data: data) }
        let key = SymmetricKey(size: .bits256)
        let data = key.withUnsafeBytes { Data($0) }
        try secureStore.set(data.base64EncodedString(), for: ZurvanCloudSecureStoreIOS.collaborationQueueKey)
        return key
    }
    private func loadQueue() {
        guard let sealedData = UserDefaults.standard.data(forKey: queueKey) else { return }
        do {
            let box = try AES.GCM.SealedBox(combined: sealedData)
            let clear = try AES.GCM.open(box, using: queueEncryptionKey())
            queue = try JSONDecoder().decode([ZurvanQueuedCollabCommand].self, from: clear)
        } catch { status = "صف آفلاین رمزگذاری‌شده قابل بازیابی نیست؛ داده محلی حفظ شده است." }
    }
    @discardableResult
    private func saveQueue() -> Bool {
        do {
            let clear = try JSONEncoder().encode(queue)
            let sealed = try AES.GCM.seal(clear, using: queueEncryptionKey())
            guard let combined = sealed.combined else { throw ZurvanCollabAPIError.invalidResponse }
            UserDefaults.standard.set(combined, forKey: queueKey)
            return true
        } catch {
            status = "رمزگذاری صف آفلاین ناموفق بود؛ فرمان‌ها ارسال نشده‌اند."
            return false
        }
    }
    private func enqueueOrSend(_ kind: String, payload: [String: Any]) {
        let id = UUID().uuidString.lowercased()
        Task {
            do {
                try await api.replay(token: token, groupID: groupID, commandID: id, kind: kind, payload: payload)
                status = "فرمان در سرور ثبت شد."
            } catch let error as ZurvanCollabAPIError {
                if case .http(let code, _) = error, code == 400 || code == 403 || code == 409 {
                    status = "فرمان توسط سرور رد شد و به صف آفلاین منتقل نشد: " + error.localizedDescription
                    return
                }
                guard let data = try? JSONSerialization.data(withJSONObject: payload) else { status = error.localizedDescription; return }
                queue.append(ZurvanQueuedCollabCommand(id: id, groupID: groupID, kind: kind, payload: data, createdAt: Date()))
                if saveQueue() {
                    status = "فرمان در صف آفلاین ذخیره شد و بعداً با شناسه ثابت ارسال می‌شود."
                }
            } catch {
                guard let data = try? JSONSerialization.data(withJSONObject: payload) else { status = error.localizedDescription; return }
                queue.append(ZurvanQueuedCollabCommand(id: id, groupID: groupID, kind: kind, payload: data, createdAt: Date()))
                saveQueue()
                status = "فرمان در صف آفلاین ذخیره شد و بعداً با شناسه ثابت ارسال می‌شود."
            }
        }
    }
    private func flushQueue() async throws {
        for command in queue.filter({ $0.groupID == groupID && $0.blockedReason == nil }).prefix(20) {
            let payload = try JSONSerialization.jsonObject(with: command.payload) as? [String: Any] ?? [:]
            do {
                try await api.replay(token: token, groupID: groupID, commandID: command.id, kind: command.kind, payload: payload)
                queue.removeAll { $0.id == command.id }
                saveQueue()
            } catch let error as ZurvanCollabAPIError {
                if case .http(let code, let message) = error, code == 409 || code == 400 || code == 403 {
                    if let index = queue.firstIndex(where: { $0.id == command.id }) {
                        queue[index].blockedReason = message
                        saveQueue()
                    }
                    status = "فرمان آفلاین به علت تعارض یا مجوز متوقف شد و نیازمند بازبینی است."
                    break
                }
                throw error
            }
        }
    }
}
