using BDFR.PersianCalendar.Core;

namespace BDFR.PersianCalendar.Infrastructure;

public sealed class ReminderPollingService(
    ICalendarRepository repository,
    INotificationSink notifications,
    TimeSpan? interval = null) : IAsyncDisposable
{
    private readonly PeriodicTimer _timer = new(interval ?? TimeSpan.FromSeconds(20));
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await PollOnceAsync(cancellationToken);
        while (await _timer.WaitForNextTickAsync(cancellationToken))
            await PollOnceAsync(cancellationToken);
    }

    public async Task PollOnceAsync(CancellationToken cancellationToken = default)
    {
        var due = await repository.GetDueRemindersAsync(DateTimeOffset.UtcNow, cancellationToken);
        foreach (var reminder in due)
        {
            try
            {
                await notifications.ShowAsync(reminder, cancellationToken);
                await repository.SetReminderStateAsync(reminder.Id, ReminderState.Fired, cancellationToken: cancellationToken);
                await repository.AddActivityAsync(new ActivityLogEntry(
                    Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, "notification-fired",
                    reminder.ItemKind, reminder.ItemId, reminder.Title), cancellationToken);
            }
            catch
            {
                // Keep Pending so a transient notification failure is retried.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            if (_loop is not null)
            {
                try { await _loop; } catch (OperationCanceledException) { }
            }
            _cts.Dispose();
        }
        _timer.Dispose();
    }
}