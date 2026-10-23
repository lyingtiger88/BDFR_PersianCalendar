import SwiftUI
import Foundation

private struct ZurvanOccasion: Identifiable {
    let id: String
    let title: String
    let calendar: Calendar.Identifier
    let month: Int
    let day: Int
    let holiday: Bool
    let mourning: Bool
}

private enum ZurvanOccasions {
    static let records: [ZurvanOccasion] = [
        .init(id:"nowruz1",title:"نوروز",calendar:.persian,month:1,day:1,holiday:true,mourning:false),
        .init(id:"nowruz2",title:"تعطیلات نوروز",calendar:.persian,month:1,day:2,holiday:true,mourning:false),
        .init(id:"nowruz3",title:"تعطیلات نوروز",calendar:.persian,month:1,day:3,holiday:true,mourning:false),
        .init(id:"nowruz4",title:"تعطیلات نوروز",calendar:.persian,month:1,day:4,holiday:true,mourning:false),
        .init(id:"republic",title:"روز جمهوری اسلامی",calendar:.persian,month:1,day:12,holiday:true,mourning:false),
        .init(id:"nature",title:"روز طبیعت",calendar:.persian,month:1,day:13,holiday:true,mourning:false),
        .init(id:"khordad14",title:"رحلت امام خمینی",calendar:.persian,month:3,day:14,holiday:true,mourning:true),
        .init(id:"khordad15",title:"قیام ۱۵ خرداد",calendar:.persian,month:3,day:15,holiday:true,mourning:false),
        .init(id:"bahman22",title:"سالگرد پیروزی انقلاب اسلامی",calendar:.persian,month:11,day:22,holiday:true,mourning:false),
        .init(id:"esfand29",title:"روز ملی شدن صنعت نفت",calendar:.persian,month:12,day:29,holiday:true,mourning:false),
        .init(id:"tasua",title:"تاسوعای حسینی",calendar:.islamicCivil,month:1,day:9,holiday:true,mourning:true),
        .init(id:"ashura",title:"عاشورای حسینی",calendar:.islamicCivil,month:1,day:10,holiday:true,mourning:true),
        .init(id:"arbaeen",title:"اربعین حسینی",calendar:.islamicCivil,month:2,day:20,holiday:true,mourning:true),
        .init(id:"prophet",title:"رحلت پیامبر اسلام",calendar:.islamicCivil,month:2,day:28,holiday:true,mourning:true),
        .init(id:"askari",title:"شهادت امام حسن عسکری",calendar:.islamicCivil,month:3,day:8,holiday:true,mourning:true),
        .init(id:"birth",title:"میلاد پیامبر اسلام",calendar:.islamicCivil,month:3,day:17,holiday:true,mourning:false),
        .init(id:"fatemeh",title:"شهادت حضرت فاطمه",calendar:.islamicCivil,month:6,day:3,holiday:true,mourning:true),
        .init(id:"ali",title:"ولادت امام علی",calendar:.islamicCivil,month:7,day:13,holiday:true,mourning:false),
        .init(id:"mabath",title:"مبعث پیامبر",calendar:.islamicCivil,month:7,day:27,holiday:true,mourning:false),
        .init(id:"shaban",title:"نیمه شعبان",calendar:.islamicCivil,month:8,day:15,holiday:true,mourning:false),
        .init(id:"ali-martyr",title:"شهادت امام علی",calendar:.islamicCivil,month:9,day:21,holiday:true,mourning:true),
        .init(id:"fitr1",title:"عید فطر",calendar:.islamicCivil,month:10,day:1,holiday:true,mourning:false),
        .init(id:"fitr2",title:"تعطیل عید فطر",calendar:.islamicCivil,month:10,day:2,holiday:true,mourning:false),
        .init(id:"sadegh",title:"شهادت امام صادق",calendar:.islamicCivil,month:10,day:25,holiday:true,mourning:true),
        .init(id:"reza",title:"ولادت امام رضا",calendar:.islamicCivil,month:11,day:11,holiday:true,mourning:false),
        .init(id:"bagher",title:"شهادت امام باقر",calendar:.islamicCivil,month:12,day:7,holiday:true,mourning:true),
        .init(id:"qorban",title:"عید قربان",calendar:.islamicCivil,month:12,day:10,holiday:true,mourning:false),
        .init(id:"ghadir",title:"عید غدیر",calendar:.islamicCivil,month:12,day:18,holiday:true,mourning:false)
    ]

    static func forDate(_ date: Date) -> [ZurvanOccasion] {
        records.filter { item in
            let components = Calendar(identifier: item.calendar)
                .dateComponents([.month, .day], from: date)
            return components.month == item.month && components.day == item.day
        }
    }

    static func holiday(_ date: Date) -> Bool {
        let weekday = Calendar(identifier:.gregorian).component(.weekday, from:date)
        return weekday == 6 || forDate(date).contains(where: \.holiday)
    }
}

struct ZurvanCalendarView: View {
    @State private var monthOffset = 0
    @State private var selectedDate: Date?
    private let persian = Calendar(identifier: .persian)
    private let columns = Array(repeating: GridItem(.flexible(), spacing: 4), count: 7)
    private let weekdays = ["ش", "ی", "د", "س", "چ", "پ", "ج"]

    private var monthStart: Date {
        let today = persian.date(from: persian.dateComponents([.year, .month], from: Date())) ?? Date()
        return persian.date(byAdding: .month, value: monthOffset, to: today) ?? today
    }
    private var days: [Date] {
        guard let range = persian.range(of: .day, in: .month, for: monthStart) else { return [] }
        return range.compactMap { day in
            persian.date(byAdding: .day, value: day - 1, to: monthStart)
        }
    }
    private var leading: Int {
        (Calendar(identifier:.gregorian).component(.weekday,from: monthStart) + 0) % 7
    }
    private var monthTitle: String {
        let formatter = DateFormatter()
        formatter.calendar = persian
        formatter.locale = Locale(identifier:"fa_IR")
        formatter.dateFormat = "LLLL yyyy"
        return formatter.string(from: monthStart)
    }
    var body: some View {
        ScrollView {
            VStack(spacing: 16) {
                HStack {
                    Button { monthOffset -= 1 } label: { Image(systemName: "chevron.right") }
                    Spacer()
                    Text(monthTitle).font(.title2.bold())
                    Spacer()
                    Button { monthOffset += 1 } label: { Image(systemName: "chevron.left") }
                }
                HStack {
                    ForEach(weekdays, id: \.self) { name in
                        Text(name).frame(maxWidth:.infinity).foregroundColor(name == "ج" ? Color.red : Color.primary)
                    }
                }
                LazyVGrid(columns: columns, spacing: 5) {
                    ForEach(0..<leading, id: \.self) { _ in Color.clear.frame(height: 65) }
                    ForEach(days, id: \.self) { date in
                        let items = ZurvanOccasions.forDate(date)
                        let mourning = items.contains(where: \.mourning)
                        let holiday = ZurvanOccasions.holiday(date)
                        Button { selectedDate = date } label: {
                            ZStack(alignment:.topLeading) {
                                RoundedRectangle(cornerRadius: 10)
                                    .fill(persian.isDateInToday(date) ? Color.blue.opacity(0.25) : Color.secondary.opacity(0.09))
                                VStack(spacing: 2) {
                                    Text(String(persian.component(.day, from:date)))
                                        .font(.headline)
                                        .foregroundColor(holiday ? Color.red : Color.primary)
                                    Text(String(Calendar(identifier:.islamicCivil).component(.day, from:date)))
                                        .font(.caption2)
                                    Text(String(Calendar(identifier:.gregorian).component(.day, from:date)))
                                        .font(.caption2)
                                }
                                .frame(maxWidth:.infinity,maxHeight:.infinity)
                                if mourning {
                                    Text("◢").foregroundStyle(.black).font(.title3).padding(1)
                                }
                            }
                            .frame(height:65)
                        }.buttonStyle(.plain)
                    }
                }
                Text("مناسبت‌های رسمی و علامت‌گذاری ایام سوگواری")
                    .font(.footnote).foregroundStyle(.secondary)
                Text("تاریخ مناسبت‌های قمری با تقویم محاسباتی است و ممکن است با اعلام رسمی رؤیت هلال یک روز تفاوت داشته باشد.")
                    .font(.caption).foregroundStyle(.secondary)
            }.padding()
        }
        .sheet(item: Binding(
            get: { selectedDate.map { SelectedZurvanDate(date:$0) } },
            set: { selectedDate = $0?.date }
        )) { selection in
            NavigationStack {
                List {
                    ForEach(ZurvanOccasions.forDate(selection.date)) { occasion in
                        HStack {
                            if occasion.mourning { Image(systemName:"ribbon.fill").foregroundStyle(.black) }
                            Text(occasion.title)
                            Spacer()
                            if occasion.holiday { Text("تعطیل").foregroundStyle(.red) }
                        }
                    }
                }
                .navigationTitle("مناسبت‌های روز")
                .toolbar { Button("بستن") { selectedDate = nil } }
            }
            .presentationDetents([.medium])
        }
    }
}

private struct SelectedZurvanDate: Identifiable {
    let date: Date
    var id: Date { date }
}

struct ZurvanOccasionsView: View {
    var body: some View {
        List {
            Section("مناسبت‌های رسمی ثابت") {
                ForEach(ZurvanOccasions.records) { item in
                    HStack {
                        VStack(alignment:.leading) {
                            Text(item.title)
                            Text("\(item.day) / \(item.month) · \(item.calendar == .persian ? "شمسی" : "قمری")")
                                .font(.caption).foregroundStyle(.secondary)
                        }
                        Spacer()
                        if item.mourning { Image(systemName:"ribbon.fill").foregroundStyle(.black) }
                        if item.holiday { Text("تعطیل").foregroundStyle(.red).font(.caption) }
                    }
                }
            }
        }
        .navigationTitle("مناسبت‌ها")
    }
}
