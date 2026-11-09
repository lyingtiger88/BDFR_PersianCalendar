import SwiftUI
import Foundation

/// Astronomical estimates for the selected weather/prayer city; not an official local timetable.
struct ZurvanPrayerTodayIOS: View {
    let city: String
    let latitude: Double
    let longitude: Double
    let timeZoneID: String

    private var times: [(String, String)] {
        let zone = TimeZone(identifier: timeZoneID) ?? TimeZone(secondsFromGMT: 12600)!
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone
        let components = calendar.dateComponents([.year, .month, .day], from: Date())
        guard let y = components.year, let mo = components.month, let d = components.day else { return [] }
        var utcCalendar = Calendar(identifier: .gregorian)
        utcCalendar.timeZone = TimeZone(secondsFromGMT: 0)!
        guard let midnightUTC = utcCalendar.date(from: DateComponents(year: y, month: mo, day: d)) else { return [] }
        let dayNumber = utcCalendar.ordinality(of: .day, in: .year, for: midnightUTC) ?? 1
        let g = 2 * Double.pi / 365 * Double(dayNumber - 1)
        let equation = 229.18 * (0.000075 + 0.001868*cos(g) - 0.032077*sin(g) -
            0.014615*cos(2*g) - 0.040849*sin(2*g))
        let declination = 0.006918 - 0.399912*cos(g) + 0.070257*sin(g) -
            0.006758*cos(2*g) + 0.000907*cos(2*g) -
            0.002697*cos(3*g) + 0.00148*sin(3*g)
        func rad(_ value: Double) -> Double { value * Double.pi / 180 }
        func hour(_ altitude: Double) -> Double? {
            let c = (sin(rad(altitude)) - sin(rad(latitude))*sin(declination)) /
                (cos(rad(latitude))*cos(declination))
            guard c >= -1 && c <= 1 else { return nil }
            return acos(c) * 12 / Double.pi
        }
        guard let fajr = hour(-17.7), let rise = hour(-0.833),
              let maghrib = hour(-4.5), let isha = hour(-14.0) else { return [] }
        let noon = 12 - longitude / 15 - equation / 60
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "fa_IR")
        formatter.timeZone = zone
        formatter.dateFormat = "HH:mm"
        func formatted(_ value: Double) -> String {
            formatter.string(from: midnightUTC.addingTimeInterval(value * 3600))
        }
        return [
            ("اذان صبح", formatted(noon-fajr)),
            ("طلوع", formatted(noon-rise)),
            ("اذان ظهر", formatted(noon)),
            ("مغرب", formatted(noon+maghrib)),
            ("عشا", formatted(noon+isha))
        ]
    }

    var body: some View {
        VStack(spacing: 7) {
            Text("🕌 اوقات شرعی امروز · \(city)").font(.caption.bold())
            if times.isEmpty {
                Text("محاسبه اوقات شرعی برای این موقعیت ممکن نیست.")
                    .font(.caption2).foregroundStyle(.secondary)
            } else {
                HStack(spacing: 3) {
                    ForEach(times, id: \.0) { item in
                        VStack(spacing: 3) {
                            Text(item.0).font(.system(size: 9))
                            Text(item.1).font(.system(size: 10, weight: .semibold))
                        }.frame(maxWidth:.infinity)
                    }
                }
            }
        }
        .padding(10)
        .background(.thinMaterial, in: RoundedRectangle(cornerRadius: 13))
    }
}
