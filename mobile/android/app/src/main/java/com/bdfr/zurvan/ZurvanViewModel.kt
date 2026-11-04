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

    val mediaTags: StateFlow<List<LocalMediaTag>> = repository.mediaTags

    val uiPreferences:
        StateFlow<AndroidUiPreferences> =
        repository.uiPreferences

    fun exportManualBackup(): String = ZurvanLocalBackup.export(getApplication())

    fun restoreManualBackup(raw: String, merge: Boolean): Int {
        val previous = repository.reminders.value
        // Validate and commit before cancelling any alarms.
        val count = ZurvanLocalBackup.import(getApplication(), raw, merge)
        repository.reloadAfterRestore()
        previous.forEach { AndroidReminderScheduler.cancel(getApplication(), it.id) }
        repository.reminders.value.filterNot { it.acknowledged }.forEach {
            AndroidReminderScheduler.schedule(getApplication(), it)
        }
        return count
    }

    fun addPlan(
        title: String,
        dateKey: String,
        hour: Int,
        minute: Int,
        kind: String,
        isPrivate: Boolean,
        icon: String = "📌",
        deleteAfterDays: Int? = null
    ) = repository.addPlan(
        title,
        dateKey,
        hour,
        minute,
        kind,
        isPrivate,
        icon,
        deleteAfterDays
    )

    fun purgeExpiredPlans() = repository.purgeExpiredPlans()

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

    fun addMediaTag(dateKey: String, uri: String, displayName: String, mimeType: String, tags: List<String>) =
        repository.addMediaTag(dateKey, uri, displayName, mimeType, tags)

    fun updateMediaTags(id: String, tags: List<String>) = repository.updateMediaTags(id, tags)

    fun deleteMediaTag(id: String) = repository.deleteMediaTag(id)

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

    fun setDayMediaEnabled(enabled: Boolean) = repository.setDayMediaEnabled(enabled)

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
