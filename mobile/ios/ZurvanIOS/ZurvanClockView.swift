import SwiftUI
import Foundation

/// Native live Zurvan clock. Historical numeral variants use explicitly decorative
/// markers until their glyphs have been checked against authoritative sources.
struct ZurvanClockViewIOS: View {
    @AppStorage("zurvan.clock.mode") private var mode = "analog"
    @AppStorage("zurvan.clock.digitalStyle") private var digitalStyle = "classic"
    @AppStorage("zurvan.clock.analogNumerals") private var numeralStyle = "zurvan"
    @AppStorage("zurvan.clock.twentyFourHours") private var twentyFourHours = true
    @AppStorage("zurvan.clock.showSeconds") private var showSeconds = true

    private let roman = ["XII","I","II","III","IV","V","VI","VII","VIII","IX","X","XI"]
    private let persian = ["۱۲","۱","۲","۳","۴","۵","۶","۷","۸","۹","۱۰","۱۱"]

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { timeline in
            Group {
                if mode == "digital" {
                    digitalClock(timeline.date)
                } else {
                    analogClock(timeline.date)
                }
            }
        }
        .frame(maxWidth: .infinity)
        .accessibilityLabel("ساعت زروان")
    }

    private func digitalClock(_ date: Date) -> some View {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = twentyFourHours
            ? (showSeconds ? "HH:mm:ss" : "HH:mm")
            : (showSeconds ? "hh:mm:ss a" : "hh:mm a")
        let accent: Color = switch digitalStyle {
        case "modern": Color(red: 0.39, green: 0.9, blue: 0.95)
        case "dots": Color(red: 0.87, green: 0.76, blue: 0.99)
        case "flip": .white
        default: Color(red: 0.96, green: 0.81, blue: 0.49)
        }
        let background: Color = switch digitalStyle {
        case "modern": Color(red: 0.04, green: 0.16, blue: 0.22)
        case "dots": Color(red: 0.15, green: 0.11, blue: 0.23)
        case "flip": Color(red: 0.13, green: 0.15, blue: 0.20)
        default: Color(red: 0.11, green: 0.09, blue: 0.18)
        }
        return Text(formatter.string(from: date))
            .font(.system(size: showSeconds ? 32 : 42, weight: .medium, design: digitalStyle == "dots" ? .monospaced : .rounded))
            .monospacedDigit()
            .foregroundStyle(accent)
            .minimumScaleFactor(0.6)
            .lineLimit(1)
            .frame(maxWidth: 310, minHeight: 105)
            .padding(.horizontal, 12)
            .background(background, in: RoundedRectangle(cornerRadius: 18))
            .overlay(RoundedRectangle(cornerRadius: 18).stroke(.yellow.opacity(0.7), lineWidth: 2))
            .accessibilityLabel(formatter.string(from: date))
    }

    private func analogClock(_ date: Date) -> some View {
        let parts = Calendar.current.dateComponents([.hour, .minute, .second], from: date)
        let hour = Double(parts.hour ?? 0)
        let minute = Double(parts.minute ?? 0)
        let second = Double(parts.second ?? 0)
        return Canvas { context, size in
            let side = min(size.width, size.height)
            let center = CGPoint(x: size.width / 2, y: size.height / 2)
            let outerRadius = side * 0.46
            var star = Path()
            for index in 0..<24 {
                let angle = Double(index) * .pi / 12 - .pi / 2
                let radius = outerRadius * (index.isMultiple(of: 2) ? 1.0 : 0.79)
                let point = CGPoint(x: center.x + radius * CGFloat(cos(angle)),
                                    y: center.y + radius * CGFloat(sin(angle)))
                if index == 0 { star.move(to: point) } else { star.addLine(to: point) }
            }
            star.closeSubpath()
            context.fill(star, with: .color(Color(red: 0.99, green: 0.97, blue: 0.91)))
            context.stroke(star, with: .color(.black), lineWidth: 5)

            var inner = Path()
            for index in 0..<24 {
                let angle = Double(index) * .pi / 12 - .pi / 2
                let radius = outerRadius * (index.isMultiple(of: 2) ? 0.57 : 0.45)
                let point = CGPoint(x: center.x + radius * CGFloat(cos(angle)),
                                    y: center.y + radius * CGFloat(sin(angle)))
                if index == 0 { inner.move(to: point) } else { inner.addLine(to: point) }
            }
            inner.closeSubpath()
            context.stroke(inner, with: .color(.black), lineWidth: 4)

            let labels = numeralStyle == "roman" ? roman : persian
            for index in 0..<12 {
                let angle = Double(index) * .pi / 6 - .pi / 2
                let radius = outerRadius * 0.72
                let point = CGPoint(x: center.x + radius * CGFloat(cos(angle)),
                                    y: center.y + radius * CGFloat(sin(angle)))
                let text: String = switch numeralStyle {
                case "khwarazmi": "◇"
                case "pahlavi": "✣"
                case "cuneiform": "◆"
                default: labels[index]
                }
                context.draw(Text(text).font(.system(size: 10, weight: .bold)).foregroundColor(.black), at: point)
            }

            func hand(_ angleDegrees: Double, length: Double, width: CGFloat, color: Color, tail: Double = 0) {
                let angle = angleDegrees * .pi / 180 - .pi / 2
                var path = Path()
                path.move(to: CGPoint(x: center.x - outerRadius * CGFloat(tail * cos(angle)),
                                      y: center.y - outerRadius * CGFloat(tail * sin(angle))))
                path.addLine(to: CGPoint(x: center.x + outerRadius * CGFloat(length * cos(angle)),
                                         y: center.y + outerRadius * CGFloat(length * sin(angle))))
                context.stroke(path, with: .color(color), style: StrokeStyle(lineWidth: width, lineCap: .round))
            }
            hand((hour.truncatingRemainder(dividingBy: 12)) * 30 + minute / 2, length: 0.34, width: 7, color: .black)
            hand(minute * 6 + second / 10, length: 0.47, width: 5, color: Color(red: 0.83, green: 0.62, blue: 0.25))
            if showSeconds {
                hand(second * 6, length: 0.61, width: 2, color: .red, tail: 0.12)
            }
            context.fill(Path(ellipseIn: CGRect(x: center.x-6, y: center.y-6, width: 12, height: 12)),
                         with: .color(Color(red: 0.83, green: 0.62, blue: 0.25)))
        }
        .frame(width: 220, height: 220)
        .accessibilityLabel("ساعت آنالوگ زروان")
    }
}
