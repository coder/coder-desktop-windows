using System;
using System.Collections.Generic;
using System.Linq;

namespace Coder.Desktop.App.Utils;

public interface IModelMergeable<in T>
{
    /// <summary>
    ///     Applies a merge of obj to `this`.
    /// </summary>
    /// <returns>
    ///     True if the two objects represent the same item (e.g. the ID/name
    ///     matches) and the merge was performed.
    /// </returns>
    public bool ApplyMerge(T obj);
}

/// <summary>
///     A static utility class providing methods for merging model updates with
///     as little UI updates as possible.
///     The main goal of the utilities in this class is to prevent redraws in
///     ItemsRepeater items when nothing has changed.
/// </summary>
public static class ModelMerge
{
    /// <summary>
    ///     Merges two observable lists with as little operations as possible
    ///     to avoid excessive/unncessary UI updates.
    ///     It's assumed that the target list is already sorted.
    /// </summary>
    public static void MergeLists<T>(IList<T> target, IEnumerable<T> update, Comparison<T> sorter)
        where T : IModelMergeable<T>
    {
        var newItems = update.ToList();

        // Update and remove existing items. We use index-based for loops here
        // because we remove items, and removing items while using the list as
        // an IEnumerable will throw an exception.
        for (var i = 0; i < target.Count; i++)
        {
            // Even though we're removing items before a "break", we still use
            // index-based for loops here to avoid exceptions.
            for (var j = 0; j < newItems.Count; j++)
            {
                if (!target[i].ApplyMerge(newItems[j])) continue;

                // Prevent it from being added below, or checked again. We
                // don't need to decrement `j` here because we're breaking
                // out of this inner loop.
                newItems.RemoveAt(j);
                goto OuterLoopEnd; // continue outer loop
            }

            // A merge couldn't occur, so we need to remove the old item and
            // decrement `i` for the next iteration.
            target.RemoveAt(i);
            i--;

            OuterLoopEnd: ;
        }

        // Add any items that were missing into their correct sorted place.
        // It's assumed the list is already sorted.
        foreach (var item in newItems)
        {
            // Perform a binary search. List<T> has BinarySearch(), but
            // IList<T> does not.
            //
            // Inserts after existing equal elements.
            var low = 0;
            var high = target.Count;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (sorter(item, target[mid]) < 0)
                    high = mid;
                else
                    low = mid + 1;
            }

            target.Insert(low, item);
        }
    }
}
