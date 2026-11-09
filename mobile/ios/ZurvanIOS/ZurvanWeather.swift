import SwiftUI
import Foundation

struct ZurvanWeatherHourIOS: Identifiable {
    let time: String
    let temperature: Double
    let code: Int
    var id: String { time }
}
struct ZurvanWeatherDayIOS: Identifiable {
    let id: String
    let high: Double
    let low: Double
    let code: Int
    let precipitation: Int
}
struct ZurvanWeatherSnapshotIOS {
    let temperature: Double
    let code: Int
    let city: String
    let hours: [ZurvanWeatherHourIOS]
    let days: [String: ZurvanWeatherDayIOS]
}

@MainActor
final class ZurvanWeatherModelIOS: ObservableObject {
    @Published private(set) var current: ZurvanWeatherSnapshotIOS?
    @Published private(set) var error: String?
    private var fetchedAt: Date?
    private var lastCoordinates: String?

    func load(latitude: Double, longitude: Double, city: String, force: Bool = false) async {
        let key = "\(latitude),\(longitude)"
        if !force, let fetchedAt, Date().timeIntervalSince(fetchedAt) < 1800,
           lastCoordinates == key, current != nil { return }
        guard latitude.isFinite, longitude.isFinite, abs(latitude) <= 90, abs(longitude) <= 180 else {
            error = "مختصات آب‌وهوا معتبر نیست."
            return
        }
        var components = URLComponents(string: "https://api.open-meteo.com/v1/forecast")!
        components.queryItems = [
            .init(name: "latitude", value: String(latitude)),
            .init(name: "longitude", value: String(longitude)),
            .init(name: "current", value: "temperature_2m,weather_code"),
            .init(name: "hourly", value: "temperature_2m,weather_code"),
            .init(name: "daily", value: "weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max"),
            .init(name: "forecast_days", value: "7"),
            .init(name: "timezone", value: "auto")
        ]
        guard let url = components.url else { error = "آدرس سرویس آب‌وهوا نامعتبر است."; return }
        do {
            var request = URLRequest(url: url)
            request.timeoutInterval = 15
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let response = response as? HTTPURLResponse, (200..<300).contains(response.statusCode)
            else { throw URLError(.badServerResponse) }
            let root = try JSONDecoder().decode(ForecastResponse.self, from: data)
            let formatter = DateFormatter()
            formatter.locale = Locale(identifier: "en_US_POSIX")
            formatter.dateFormat = "yyyy-MM-dd'T'HH:mm"
            formatter.timeZone = TimeZone(secondsFromGMT: 0)
            let localNow = Date().addingTimeInterval(TimeInterval(root.utc_offset_seconds))
            // Compare city wall-clock strings, not device-local time.
            let cityClock = formatter.string(from: localNow)
            let hourly = (0..<min(root.hourly.time.count, root.hourly.temperature_2m.count,
                                  root.hourly.weather_code.count)).compactMap { index -> ZurvanWeatherHourIOS? in
                guard root.hourly.time[index] >= cityClock else { return nil }
                return ZurvanWeatherHourIOS(time: root.hourly.time[index],
                    temperature: root.hourly.temperature_2m[index], code: root.hourly.weather_code[index])
            }.prefix(24)
            var days: [String: ZurvanWeatherDayIOS] = [:]
            let count = min(root.daily.time.count, root.daily.temperature_2m_max.count,
                            root.daily.temperature_2m_min.count, root.daily.weather_code.count,
                            root.daily.precipitation_probability_max.count)
            for index in 0..<count {
                let date = root.daily.time[index]
                days[date] = ZurvanWeatherDayIOS(id: date, high: root.daily.temperature_2m_max[index],
                    low: root.daily.temperature_2m_min[index], code: root.daily.weather_code[index],
                    precipitation: root.daily.precipitation_probability_max[index])
            }
            current = ZurvanWeatherSnapshotIOS(temperature: root.current.temperature_2m,
                code: root.current.weather_code, city: city, hours: Array(hourly), days: days)
            error = nil
            fetchedAt = Date()
            lastCoordinates = key
        } catch {
            self.error = current == nil ? "اتصال به سرویس آب‌وهوا برقرار نشد." :
                "اطلاعات قبلی نمایش داده می‌شود."
        }
    }

    static func icon(_ code: Int) -> String {
        switch code {
        case 0: return "☀️"
        case 1, 2: return "🌤️"
        case 3: return "☁️"
        case 45, 48: return "🌫️"
        case 51...67, 80...82: return "🌧️"
        case 71...77, 85, 86: return "❄️"
        case 95...99: return "⛈️"
        default: return "🌡️"
        }
    }

    private struct ForecastResponse: Decodable {
        struct Current: Decodable { let temperature_2m: Double; let weather_code: Int }
        struct Hourly: Decodable { let time: [String]; let temperature_2m: [Double]; let weather_code: [Int] }
        struct Daily: Decodable {
            let time: [String]
            let temperature_2m_max: [Double]
            let temperature_2m_min: [Double]
            let weather_code: [Int]
            let precipitation_probability_max: [Int]
        }
        let current: Current
        let hourly: Hourly
        let daily: Daily
        let utc_offset_seconds: Int
    }
}

struct ZurvanTodayWeatherIOS: View {
    let snapshot: ZurvanWeatherSnapshotIOS?
    let error: String?
    var body: some View {
        VStack(alignment: .leading, spacing: 9) {
            Text("🌤️ آب‌وهوای امروز · \(snapshot?.city ?? "تهران")").font(.headline)
            if let snapshot {
                Text("\(ZurvanWeatherModelIOS.icon(snapshot.code)) \(Int(snapshot.temperature.rounded()))°C")
                    .font(.title2.bold())
                Text("تغییرات دما در ۲۴ ساعت آینده").font(.caption)
                    .foregroundStyle(.secondary)
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 6) {
                        ForEach(snapshot.hours) { hour in
                            VStack(spacing: 5) {
                                Text(String(hour.time.suffix(5))).font(.caption2)
                                Text(ZurvanWeatherModelIOS.icon(hour.code)).font(.headline)
                                Text("\(Int(hour.temperature.rounded()))°").font(.caption.bold())
                            }
                            .frame(width: 48)
                            .padding(.vertical, 7)
                            .background(.white.opacity(0.09), in: RoundedRectangle(cornerRadius: 7))
                        }
                    }
                }
            } else {
                Text(error ?? "در حال دریافت اطلاعات آب‌وهوا...")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Text("منبع: Open-Meteo").font(.caption2).foregroundStyle(.secondary)
        }
        .foregroundStyle(.white)
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(LinearGradient(colors: [Color(red:0.04,green:0.12,blue:0.25),
            Color(red:0.06,green:0.20,blue:0.36)], startPoint: .topLeading, endPoint: .bottomTrailing),
            in: RoundedRectangle(cornerRadius: 14))
    }
}
