package com.bdfr.zurvan

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import kotlinx.coroutines.flow.StateFlow

class ZurvanViewModel(
    application: Application
) : AndroidViewModel(application) {
    private val repository =
        AndroidLocalRepository(application)

    private val companionManager =
        AndroidCompanionManager(
            application,
            repository
        )

    val companionState:
        StateFlow<CompanionUiState> =
        companionManager.state

    val plans: StateFlow<List<LocalPlan>> =
        repository.plans

    val occasions: StateFlow<List<LocalOccasion>> =
        repository.occasions

    val reminders: StateFlow<List<LocalReminder>> =
        repository.reminders

    val notes: StateFlow<List<LocalDayNote>> =
        repository.notes

    val uiPreferences:
        StateFlow<AndroidUiPreferences> =
        repository.uiPreferences

    fun addPlan(
        title: String,
        dateKey: String,
        hour: Int,
        minute: Int,
        kind: String,
        isPrivate: Boolean,
        icon: String = "📌"
    ) = repository.addPlan(
        title,
        dateKey,
        hour,
        minute,
        kind,
        isPrivate,
        icon
    )

    fun togglePlan(id: String) =
        repository.togglePlan(id)

    fun deletePlan(id: String) =
        repository.deletePlan(id)

    fun addOccasion(
        title: String,
        calendar: CalendarPriority,
        month: Int,
        day: Int,
        hour: Int,
        minute: Int
    ) = repository.addOccasion(
        title,
        calendar,
        month,
        day,
        hour,
        minute
    )

    fun deleteOccasion(id: String) =
        repository.deleteOccasion(id)

    fun saveNote(
        dateKey: String,
        text: String,
        isPrivate: Boolean
    ) = repository.saveNote(
        dateKey,
        text,
        isPrivate
    )

    fun addReminder(
        title: String,
        dateKey: String,
        hour: Int,
        minute: Int,
        isPrivate: Boolean,
        repeatCount: Int,
        repeatIntervalMinutes: Int
    ) {
        val reminder = repository.addReminder(
            title,
            dateKey,
            hour,
            minute,
            isPrivate,
            repeatCount,
            repeatIntervalMinutes
        )

        AndroidReminderScheduler.schedule(
            getApplication(),
            reminder
        )
    }

    fun acknowledgeReminder(id: String) {
        AndroidReminderScheduler.cancel(
            getApplication(),
            id
        )
        repository.acknowledgeReminder(id)
    }

    fun deleteReminder(id: String) {
        AndroidReminderScheduler.cancel(
            getApplication(),
            id
        )
        repository.deleteReminder(id)
    }

    fun setCalendarOrder(
        value: List<CalendarPriority>
    ) = repository.setCalendarOrder(value)

    fun setCornerRadius(value: Float) =
        repository.setCornerRadius(value)

    fun setRailSide(value: String) =
        repository.setRailSide(value)

    fun startCompanionMaster() =
        companionManager.startMaster()

    fun joinCompanionFromQr(
        rawQr: String
    ) = companionManager.joinFromQr(
        rawQr
    )

    fun stopCompanionSession() =
        companionManager.stop()

    override fun onCleared() {
        companionManager.close()
        super.onCleared()
    }
}
