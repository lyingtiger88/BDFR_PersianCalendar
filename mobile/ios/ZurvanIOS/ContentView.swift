import SwiftUI

private enum ZurvanTab: Hashable {
    case calendar
    case plans
    case reminders
    case companion
    case settings
}

struct ContentView: View {
    @State private var selection: ZurvanTab = .calendar

    var body: some View {
        TabView(selection: $selection) {
            NavigationStack {
                CalendarShellView()
                    .navigationTitle("Zurvan | زروان")
            }
            .tabItem {
                Label("تقویم", systemImage: "calendar")
            }
            .tag(ZurvanTab.calendar)

            NavigationStack {
                PlaceholderView(title: "برنامه‌ها")
            }
            .tabItem {
                Label("برنامه‌ها", systemImage: "checklist")
            }
            .tag(ZurvanTab.plans)

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
                PlaceholderView(title: "تنظیمات")
            }
            .tabItem {
                Label("تنظیمات", systemImage: "gearshape")
            }
            .tag(ZurvanTab.settings)
        }
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
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                VStack(alignment: .leading, spacing: 10) {
                    Text("جفت‌سازی با رایانه")
                        .font(.headline)
                    Text("QR یا کد ۶ رقمی برای Pairing و سپس همگام‌سازی دوطرفه Local-first.")
                        .foregroundStyle(.secondary)
                    Button("جفت‌سازی دستگاه") {}
                        .buttonStyle(.borderedProminent)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding()
                .background(.thinMaterial, in: RoundedRectangle(cornerRadius: 18))
            }
            .padding()
        }
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
