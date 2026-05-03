namespace Common.Extensions;

using System.Collections;

public static class EnumerableExtensions
{
    public static void ForEach<T>(this IEnumerable<T> items, Action<T> action)
    {
        foreach (var item in items)
        {
            action(item);
        }
    }

    public static void ForEach<T>(this IEnumerable<T> items, Action<T, int> action)
    {
        var index = 0;
        foreach (var item in items)
        {
            action(item, index++);
        }
    }

    public static void ForEach(this IEnumerable items, Action<object> action)
    {
        foreach (var item in items)
        {
            action(item);
        }
    }

    public static void ForEach(this IEnumerable items, Action<object, int> action)
    {
        var index = 0;
        foreach (var item in items)
        {
            action(item, index++);
        }
    }
}
