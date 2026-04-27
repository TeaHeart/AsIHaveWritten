namespace AsIHaveWritten.Helpers;

using System.Text;

internal static class ConsoleHelper
{
    internal static int Left => Console.CursorLeft;
    internal static int Top => Console.CursorTop;
    internal static int Width => Console.WindowWidth;
    internal static int Height => Console.WindowHeight;
    internal static bool CursorVisible { get => Console.CursorVisible; set => Console.CursorVisible = value; }

    private static readonly object _syncRoot = new();

    internal static void SetEncoding(Encoding? encoding = null)
    {
        Console.InputEncoding = Console.OutputEncoding = encoding ?? Encoding.UTF8;
    }

    internal static void Clear(int? top = null)
    {
        lock (_syncRoot)
        {
            if (top is int v)
            {
                var (l, t) = Console.GetCursorPosition();
                Console.SetCursorPosition(0, v);
                Console.Write(new string(' ', Console.WindowWidth));
                Console.SetCursorPosition(l, t);
            }
            else
            {
                Console.Clear();
            }
        }
    }

    internal static void Write(object? value, int? top = null)
    {
        lock (_syncRoot)
        {
            Console.SetCursorPosition(0, top ?? Top);
            Console.Write(value);
        }
    }

    internal static void WriteLine(object? value = null, int? top = null)
    {
        lock (_syncRoot)
        {
            Console.SetCursorPosition(0, top ?? Top);
            Console.WriteLine(value);
        }
    }

    internal static void WriteProgress(int current, int total, int? top = null)
    {
        lock (_syncRoot)
        {
            top ??= Top;

            var sb = new StringBuilder();
            var currStr = current.ToString();
            var totalStr = total.ToString();

            sb.Append('0', totalStr.Length - currStr.Length)
              .Append(currStr)
              .Append('/')
              .Append(totalStr)
              .Append(' ');

            var totalWidth = Math.Min(Console.WindowWidth - sb.Length, 50);
            var progressWidth = totalWidth * current / total;

            sb.Append('*', progressWidth)
              .Append('-', totalWidth - progressWidth);

            Clear(top);
            Write(sb, top);

            if (current == total)
            {
                Console.WriteLine();
            }
        }
    }

    internal static string Read(ReadOnlySpan<char> whitelist)
    {
        lock (_syncRoot)
        {
            var top = Top;
            var sb = new StringBuilder();
            while (true)
            {
                Clear(top);
                Write(sb, top);

                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return sb.ToString();
                }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (key.Modifiers == ConsoleModifiers.Control)
                    {
                        sb.Clear();
                    }
                    else if (sb.Length != 0)
                    {
                        sb.Remove(sb.Length - 1, 1);
                    }
                }
                else if (whitelist.Contains(key.KeyChar))
                {
                    sb.Append(key.KeyChar);
                }
            }
        }
    }

    internal static T ReadOption<T>(IReadOnlyList<T> options)
    {
        lock (_syncRoot)
        {
            var top = Top;
            var index = 0;
            var count = options.Count;
            var maxShow = Math.Min(Height - top, count);
            var visable = CursorVisible;
            CursorVisible = false;
            while (true)
            {
                for (int i = 0; i < maxShow; i++)
                {
                    Clear(top + i);
                    Write($"{(i == 0 ? "> " : "  ")}{options[(index + i) % count]}", top + i);
                }

                var key = Console.ReadKey(true);
                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        Console.WriteLine();
                        CursorVisible = visable;
                        return options[index];
                    case ConsoleKey.UpArrow:
                        index = (index + count - 1) % count;
                        break;
                    case ConsoleKey.DownArrow:
                        index = (index + 1) % count;
                        break;
                }
            }
        }
    }
}
