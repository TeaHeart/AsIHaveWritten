namespace AsIHaveWritten.Helpers;

public class SimpleConsoleView
{
    private readonly object _syncRoot = new object();

    public object? this[int top, int left = 0]
    {
        set
        {
            lock (_syncRoot)
            {
                Console.SetCursorPosition(left, top);
                Console.Write($"{value}".PadRight(Console.WindowWidth));
            }
        }
    }
}
