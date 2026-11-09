import Foundation

struct ZurvanCollabGroup: Decodable, Identifiable {
    let id: String
    let name: String
    let agenda_enabled: Bool
    let kanban_enabled: Bool
    let school_enabled: Bool
}

struct ZurvanCollabSubject: Decodable, Identifiable {
    let id: String
    let name: String
}

struct ZurvanCollabHomework: Decodable, Identifiable {
    let id: String
    let title: String
    let instructions: String
    let due_at: String?
}

struct ZurvanCollabColumn: Decodable, Identifiable {
    let id: String
    let name: String
    let position: Int
    let color: String
}

struct ZurvanCollabChangeFeed: Decodable {
    let events: [ZurvanCollabEvent]
    let nextCursor: Int64
}

struct ZurvanCollabEvent: Decodable, Identifiable {
    var id: Int64 { seq }
    let seq: Int64
    let kind: String
    let resource_id: String?
}

enum ZurvanCollabAPIError: LocalizedError {
    case invalidConfiguration, invalidResponse, http(Int, String)
    var errorDescription: String? {
        switch self {
        case .invalidConfiguration: return "نشانی سرویس همکاری پیکربندی نشده است."
        case .invalidResponse: return "پاسخ سرویس همکاری معتبر نیست."
        case .http(let code, let message): return "خطای سرویس (\(code)): \(message)"
        }
    }
}

struct ZurvanCollaborationAPI {
    private let baseURL = URL(string: "https://yztithydgdvydaktuxlw.supabase.co/functions/v1/zurvan-collaboration")!
    private let publishableKey = "sb_publishable_UAKFYbxgWOCVRDDdv_0JLg_oIIiubmT"

    private func request<T: Decodable>(_ method: String, _ path: String, token: String,
                                       body: [String: Any]? = nil, as type: T.Type) async throws -> T {
        let parts = path.split(separator: "?", maxSplits: 1, omittingEmptySubsequences: false)
        var components = URLComponents(url: baseURL.appending(path: String(parts[0])), resolvingAgainstBaseURL: false)
        if parts.count > 1 { components?.percentEncodedQuery = String(parts[1]) }
        guard let targetURL = components?.url else { throw ZurvanCollabAPIError.invalidConfiguration }
        var request = URLRequest(url: targetURL)
        request.httpMethod = method
        request.timeoutInterval = 35
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        request.setValue(publishableKey, forHTTPHeaderField: "apikey")
        if let body {
            request.httpBody = try JSONSerialization.data(withJSONObject: body)
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }
        let (data, response) = try await URLSession.shared.data(for: request)
        guard let response = response as? HTTPURLResponse else { throw ZurvanCollabAPIError.invalidResponse }
        guard (200..<300).contains(response.statusCode) else {
            let message = (try? JSONSerialization.jsonObject(with: data) as? [String: Any])?["error"] as? String ?? "درخواست ناموفق بود."
            throw ZurvanCollabAPIError.http(response.statusCode, message)
        }
        return try JSONDecoder().decode(T.self, from: data)
    }

    func groups(token: String) async throws -> [ZurvanCollabGroup] {
        try await request("GET", "groups", token: token, as: [ZurvanCollabGroup].self)
    }
    func createGroup(token: String, name: String, agenda: Bool, kanban: Bool, school: Bool) async throws -> ZurvanCollabGroup {
        try await request("POST", "groups", token: token,
            body: ["name": name, "agenda": agenda, "kanban": kanban, "school": school], as: ZurvanCollabGroup.self)
    }
    func subjects(token: String, groupID: String) async throws -> [ZurvanCollabSubject] {
        try await request("GET", "groups/\(groupID)/subjects", token: token, as: [ZurvanCollabSubject].self)
    }
    func createSubject(token: String, groupID: String, name: String) async throws {
        let _: [String: String] = try await request("POST", "groups/\(groupID)/subjects", token: token, body: ["name": name], as: [String: String].self)
    }
    func timetable(token: String, groupID: String) async throws -> [[String: Any]] {
        try await raw("GET", "groups/\(groupID)/timetable", token: token)
    }
    func createTimetableSlot(token: String, groupID: String, subjectID: String, weekday: Int,
                             startTime: String, endTime: String, room: String = "") async throws {
        _ = try await raw("POST", "groups/\(groupID)/timetable", token: token,
            body: ["subjectId": subjectID, "weekday": weekday, "startTime": startTime,
                   "endTime": endTime, "room": room])
    }
    func homework(token: String, groupID: String) async throws -> [ZurvanCollabHomework] {
        try await request("GET", "groups/\(groupID)/homework", token: token, as: [ZurvanCollabHomework].self)
    }
    func createHomework(token: String, groupID: String, subjectID: String, title: String, instructions: String) async throws {
        _ = try await raw("POST", "groups/\(groupID)/homework", token: token,
            body: ["subjectId": subjectID, "title": title, "instructions": instructions])
    }
    func submitHomework(token: String, homeworkID: String, body: String) async throws {
        _ = try await raw("POST", "homework/\(homeworkID)/submissions", token: token, body: ["body": body])
    }
    func gradeSubmission(token: String, homeworkID: String, submissionID: String, grade: Double, feedback: String) async throws {
        _ = try await raw("PUT", "homework/\(homeworkID)/submissions/\(submissionID)/grade", token: token,
            body: ["grade": grade, "feedback": feedback])
    }
    func kanbanColumns(token: String, groupID: String) async throws -> [ZurvanCollabColumn] {
        try await request("GET", "groups/\(groupID)/kanban/columns", token: token, as: [ZurvanCollabColumn].self)
    }
    func createKanbanColumn(token: String, groupID: String, name: String) async throws {
        _ = try await raw("POST", "groups/\(groupID)/kanban/columns", token: token, body: ["name": name])
    }
    func reorderKanban(token: String, groupID: String, ids: [String]) async throws {
        _ = try await raw("PUT", "groups/\(groupID)/kanban/columns/reorder", token: token, body: ["columnIds": ids])
    }
    func createItem(token: String, groupID: String, title: String, mode: String,
                    columnID: String? = nil, recurrenceRule: String? = nil) async throws {
        var body: [String: Any] = ["title": title, "mode": mode, "state": "todo", "timezone": "Asia/Tehran"]
        if let columnID { body["columnId"] = columnID }
        if let recurrenceRule { body["recurrenceRule"] = recurrenceRule }
        _ = try await raw("POST", "groups/\(groupID)/items", token: token, body: body)
    }
    func replay(token: String, groupID: String, commandID: String, kind: String, payload: [String: Any]) async throws {
        _ = try await raw("POST", "groups/\(groupID)/commands", token: token,
            body: ["commandId": commandID, "kind": kind, "payload": payload])
    }
    func changes(token: String, groupID: String, after: Int64) async throws -> ZurvanCollabChangeFeed {
        try await request("GET", "groups/\(groupID)/changes?after=\(after)&wait=20", token: token, as: ZurvanCollabChangeFeed.self)
    }

    private func raw(_ method: String, _ path: String, token: String,
                     body: [String: Any]? = nil) async throws -> Any {
        let parts = path.split(separator: "?", maxSplits: 1, omittingEmptySubsequences: false)
        var components = URLComponents(url: baseURL.appending(path: String(parts[0])), resolvingAgainstBaseURL: false)
        if parts.count > 1 { components?.percentEncodedQuery = String(parts[1]) }
        guard let targetURL = components?.url else { throw ZurvanCollabAPIError.invalidConfiguration }
        var request = URLRequest(url: targetURL)
        request.httpMethod = method; request.timeoutInterval = 35
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        request.setValue(publishableKey, forHTTPHeaderField: "apikey")
        if let body { request.httpBody = try JSONSerialization.data(withJSONObject: body); request.setValue("application/json", forHTTPHeaderField: "Content-Type") }
        let (data,response)=try await URLSession.shared.data(for: request)
        guard let response=response as? HTTPURLResponse else { throw ZurvanCollabAPIError.invalidResponse }
        guard (200..<300).contains(response.statusCode) else {
            let message=(try? JSONSerialization.jsonObject(with:data) as? [String:Any])?["error"] as? String ?? "درخواست ناموفق بود."
            throw ZurvanCollabAPIError.http(response.statusCode,message)
        }
        return try JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed])
    }
}
