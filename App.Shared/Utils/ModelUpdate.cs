using System;
using System.Collections.Generic;
using System.Linq;

namespace Coder.Desktop.App.Utils;

public interface IModelUpdateable<in T>
{
    /// <summary>
    ///     Applies changes from obj to `this` if they represent the same
    ///     object based on some identifier like an ID or fixed name.
    /// </summary>
    /// <returns>
    ///     True if the two objects represent the same item and the changes
    ///     were applied.
    /// </returns>
    public bool TryApplyChanges(T obj);
}

/// <summary>
///     A static utility class providing methods for applying model updates
///     with as little UI updates as possible.
///     The main goal of the utilities in this class is to prevent redraws in
///     ItemsRepeater items when nothing has changed.
/// </summary>
public static class ModelUpdate
{
    /// <summary>
    ///     Takes all items in `update` and either applies them to existing
    ///     items in `target`, or adds them to `target` if there are no
    ///     matching items.
    ///     Any items in `target` that don't have a corresponding item in
    ///     `update` will be removed from `target`.
    ///     Items are inserted in their correct sort position according to
    ///     `sorter`. It's assumed that the target list is already sorted by
    ///     `sorter`.
    /// </summary>
    /// <param name="target">Target list to be updated</param>
    /// <param name="update">Incoming list to apply to `target`</param>
    /// <param name="sorter">
    ///     Comparison to use for sorting. Note that the sort order does not
    ///     need to be the ID/name field used in the <c>IModelUpdateable</c>
    ///     implementation, and can be by any order.
    ///     New items will be sorted after existing items.
    /// </param>
    public static void ApplyLists<T>(IList<T> target, IEnumerable<T> update, Comparison<T> sorter)
        where T : IModelUpdateable<T>
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
                if (!target[i].TryApplyChanges(newItems[j])) continue;

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

            // Rider fights `dotnet format` about whether there should be a
            // space before the semicolon or not.
#pragma warning disable format
        OuterLoopEnd: ;
#pragma warning restore format
        }

        // Add any items that were missing into their correct sorted place.
        // It's assumed the list is already sorted.
        foreach (var newItem in newItems)
        {
            for (var i = 0; i < target.Count; i++)
                // If the new item sorts before the current item, insert it
                // after.
                if (sorter(newItem, target[i]) < 0)
                {
                    target.Insert(i, newItem);
                    goto OuterLoopEnd;
                }

            // Handle the case where target is empty or the new item is
            // equal to or after every other item.
            target.Add(newItem);

            // Rider fights `dotnet format` about whether there should be a
            // space before the semicolon or not.
#pragma warning disable format
        OuterLoopEnd: ;
#pragma warning restore format
        }
    }
}
