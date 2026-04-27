namespace AsIHaveWritten.Extensions;

internal static class TimerExtensions
{
    internal static void Enable(this Timer timer, int period, int dueTime = 0)
    {
        timer.Change(dueTime, period);
    }

    internal static void Disable(this Timer timer)
    {
        timer.Change(Timeout.Infinite, Timeout.Infinite);
    }
}
