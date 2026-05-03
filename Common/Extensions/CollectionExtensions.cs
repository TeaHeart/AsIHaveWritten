namespace Common.Extensions;

public static class CollectionExtensions
{
    public static void RemoveIf<T>(this ICollection<T> source, Func<T, bool> predicate)
    {
        foreach (var item in source.Where(predicate).ToList())
        {
            source.Remove(item);
        }
    }
}
