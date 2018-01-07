using System;
using System.Linq;
using System.Collections.Generic;

namespace Trading.Advisor
{
    static class LinqExtensions
    {
        public static IEnumerable<TResult> Pairwise<T, TResult>(this IEnumerable<T> source, Func<T, T, TResult> selector)
        {
            using (var enumerator = source.GetEnumerator())
            {
                if (!enumerator.MoveNext())
                    yield break;

                var previous = enumerator.Current;

                while (enumerator.MoveNext())
                {
                    var current = enumerator.Current;
                    yield return selector(previous, current);
                    previous = current;
                }
            }
        }

        public static IEnumerable<TSource> DistinctUntilChanged<TSource, TKey>(this IEnumerable<TSource> source,
                                                                         Func<TSource, TKey> keySelector)
        {
            using (var enumerator = source.GetEnumerator())
            {
                if (!enumerator.MoveNext())
                    yield break;

                TSource lastItem = enumerator.Current;
                yield return lastItem;

                while (enumerator.MoveNext())
                {
                    TSource currentItem = enumerator.Current;

                    if (!keySelector(currentItem).Equals(keySelector(lastItem)))
                    {
                        lastItem = currentItem;
                        yield return currentItem;
                    }
                }
            }
        }

        public static IEnumerable<T> Tail<T>(this IList<T> source, int count)
        {
            for (int i = Math.Max(0, source.Count - count); i < source.Count; i++)
            {
                yield return source[i];
            }
        }

        public static IEnumerable<IList<T>> Split<T>(this IEnumerable<T> source, Func<T, T, bool> windowClosingSelector)
        {
            var buffer = new List<T>();
            foreach (var item in source)
            {
                if (buffer.Count > 0
                    && windowClosingSelector(buffer[buffer.Count - 1], item))
                {
                    yield return buffer.ToList();
                    buffer.Clear();
                }
                buffer.Add(item);
            }
            if (buffer.Count > 0)
            {
                yield return buffer.ToList();
            }
        }
    }
}
