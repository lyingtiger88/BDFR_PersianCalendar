package com.bdfr.zurvan

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import kotlinx.coroutines.flow.StateFlow

class ZurvanViewModel(
    application: Application
) : AndroidViewModel(application) {
    private val repository =
        AndroidLocalRepository(application)

    private fun writable(): Boolean = ZurvanTrialPolicy.canWrite(getApplication())

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

    val mediaTags: StateFlow<List<LocalMediaTag>> = repository.mediaTags

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
        icon: String = "📌",
        deleteAfterDays: Int? = null
    ) {
        if (!writable()) return
        repository.addPlan(
        title,
        dateKey,
        hour,
        minute,
        kind,
        isPrivate,
        icon,
        deleteAfterDays
        )
        if (title.isNotBlank()) ZurvanAchievementTracker.record(getApplication(), "content_saved")
    }

    fun purgeExpiredPlans() = repository.purgeExpiredPlans()

    fun togglePlan(id: String) { if (!writable()) return; repository.togglePlan(id) }

    fun deletePlan(id: String) { if (!writable()) return; repository.deletePlan(id) }

    fun addOccasion(
        title: String,
        calendar: CalendarPriority,
        month: Int,
        day: Int,
        hour: Int,
        minute: Int
    ) {
        if (!writable()) return
        repository.addOccasion(
        title,
        calendar,
        month,
        day,
        hour,
        minute
        )
        if (title.isNotBlank()) ZurvanAchievementTracker.record(getApplication(), "content_saved")
    }

    fun deleteOccasion(id: String) { if (!writable()) return; repository.deleteOccasion(id) }

    fun addMediaTag(dateKey: String, uri: String, displayName: String, mimeType: String, tags: List<String>) { if (!writable()) return; repository.addMediaTag(dateKey, uri, displayName, mimeType, tags) }

    fun updateMediaTags(id: String, tags: List<String>) { if (!writable()) return; repository.updateMediaTags(id, tags) }

    fun deleteMediaTag(id: String) { if (!writable()) return; repository.deleteMediaTag(id) }

    fun saveNote(
        dateKey: String,
        text: String,
        isPrivate: Boolean
    ) {
        if (!writable()) return
        repository.saveNote(
        dateKey,
        text,
        isPrivate
        )
        if (text.isNotBlank()) ZurvanAchievementTracker.record(getApplication(), "content_saved")
    }

    fun addReminder(
        title: String,
        dateKey: String,
        hour: Int,
        minute: Int,
        isPrivate: Boolean,
        repeatCount: Int,
        repeatIntervalMinutes: Int
    ) {
        if (!writable()) return
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
        if (!writable()) return
        AndroidReminderScheduler.cancel(
            getApplication(),
            id
        )
        repository.acknowledgeReminder(id)
    }

    fun deleteReminder(id: String) {
        if (!writable()) return
        AndroidReminderScheduler.cancel(
            getApplication(),
            id
        )
        repository.deleteReminder(id)
    }

    fun setCalendarOrder(
        value: List<CalendarPriority>
    ) { if (!writable()) return; repository.setCalendarOrder(value) }

    fun setCornerRadius(value: Float) { if (!writable()) return; repository.setCornerRadius(value) }

    fun setRailSide(value: String) { if (!writable()) return; repository.setRailSide(value) }

    fun setDayMediaEnabled(enabled: Boolean) { if (!writable()) return; repository.setDayMediaEnabled(enabled) }

    fun startCompanionMaster() { if (!writable()) return; companionManager.startMaster() }

    fun joinCompanionFromQr(
        rawQr: String
    ) { if (!writable()) return; companionManager.joinFromQr(
        rawQr
        )
    }

    fun stopCompanionSession() { if (!writable()) return; companionManager.stop() }

    override fun onCleared() {
        companionManager.close()
        super.onCleared()
    }
}
