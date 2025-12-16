namespace AsIHaveWritten.Extensions;

public static class TimerExtensions
{
    public static void Enable(this Timer timer, int period, int dueTime = 0)
    {
        timer.Change(dueTime, period);
    }

    public static void Disable(this Timer timer)
    {
        timer.Change(Timeout.Infinite, Timeout.Infinite);
    }
}
