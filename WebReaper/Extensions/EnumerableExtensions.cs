using System;

namespace WebReaper.Extensions;

internal static class EnumerableExtensions
{
    public static IEnumerable<U> SelectTruthy<T, U>(this IEnumerable<T> enumerable, Func<T, U?> predicate)
        where U : class
    {
        foreach (var item in enumerable)
        {
            if (predicate(item) is { } result)
            {
                yield return result;
            }
        }
    }

    public static IEnumerable<T> SelectTruthy<T>(this IEnumerable<T?> enumerable)
    {
        foreach (var item in enumerable)
        {
            if (item is { } result)
            {
                yield return result;
            }
        }
    }

    public static IEnumerable<U> SelectTruthy<T, U>(this IEnumerable<T> enumerable, Func<T, U?> predicate) where U : struct
    {
        foreach (var item in enumerable)
        {
            if (predicate(item) is { } result)
            {
                yield return result;
            }
        }
    }

    public static IEnumerable<T> SelectTruthy<T>(this IEnumerable<T?> enumerable) where T : struct
    {
        foreach (var item in enumerable)
        {
            if (item is { } result)
            {
                yield return result;
            }
        }
    }

    public static T ChooseRandom<T>(this IEnumerable<T> enumerable, Random? random = null)
    {
        random ??= Random.Shared;
        if (enumerable.TryGetNonEnumeratedCount(out var count))
        {
            var index = random.Next(count);
            return enumerable.ElementAt(index);
        }
        else
        {
            var list = enumerable.ToList();
            return list[random.Next(list.Count)];
        }
    }
}
