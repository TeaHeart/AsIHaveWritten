namespace Common.Extensions;

public static class ListExtensions
{
    public static int FindIndex<T>(this IReadOnlyList<T> source, Func<T, bool> predicate)
    {
        for (int i = 0; i < source.Count; i++)
        {
            if (predicate(source[i]))
            {
                return i;
            }
        }
        return -1;
    }

    public static int FindLastIndex<T>(this IReadOnlyList<T> source, Func<T, bool> predicate)
    {
        for (int i = source.Count - 1; i >= 0; i--)
        {
            if (predicate(source[i]))
            {
                return i;
            }
        }
        return -1;
    }

    public static int FindIndex<T>(this IList<T> source, Func<T, bool> predicate)
    {
        for (int i = 0; i < source.Count; i++)
        {
            if (predicate(source[i]))
            {
                return i;
            }
        }
        return -1;
    }

    public static int FindLastIndex<T>(this IList<T> source, Func<T, bool> predicate)
    {
        for (int i = source.Count - 1; i >= 0; i--)
        {
            if (predicate(source[i]))
            {
                return i;
            }
        }
        return -1;
    }
}
