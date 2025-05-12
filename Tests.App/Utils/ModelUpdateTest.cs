using System.Collections;
using Coder.Desktop.App.Utils;

namespace Coder.Desktop.Tests.App.Utils;

#region ModelMerge test classes

public class UpdateableItem : IModelUpdateable<UpdateableItem>
{
    public List<int> AttemptedMerges = [];
    public int Id { get; }

    public UpdateableItem(int id)
    {
        Id = id;
    }

    public bool TryApplyChanges(UpdateableItem obj)
    {
        AttemptedMerges.Add(obj.Id);
        return Id == obj.Id;
    }

    public override string ToString()
    {
        return $"MergeableItem {Id}";
    }

    #region MergeableItem equality

    public override bool Equals(object? obj)
    {
        return obj is UpdateableItem other && Equals(other);
    }

    public bool Equals(UpdateableItem? other)
    {
        return other is not null && Id == other.Id;
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }

    public static bool operator ==(UpdateableItem left, UpdateableItem right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(UpdateableItem left, UpdateableItem right)
    {
        return !left.Equals(right);
    }

    #endregion
}

/// <summary>
///     A wrapper around list that tracks Insert and RemoveAt operations.
/// </summary>
public class TrackableList<T> : IList<T>
{
    public List<T> Items = [];
    public List<ListOperation<T>> Operations = [];

    public void Insert(int index, T item)
    {
        Items.Insert(index, item);
        Operations.Add(new ListOperation<T>
        {
            Type = ListOperation<T>.OperationType.Insert,
            Index = index,
            Item = item,
        });
    }

    public void Add(T item)
    {
        Items.Add(item);
        Operations.Add(new ListOperation<T>
        {
            Type = ListOperation<T>.OperationType.Insert,
            Index = Items.Count - 1,
            Item = item,
        });
    }

    public void RemoveAt(int index)
    {
        var item = Items[index];
        Items.RemoveAt(index);
        Operations.Add(new ListOperation<T>
        {
            Type = ListOperation<T>.OperationType.RemoveAt,
            Index = index,
            Item = item,
        });
    }

    public T this[int index]
    {
        get => Items[index];
        // We don't expect this to be called in the test.
        set => throw new NotImplementedException();
    }

    public int IndexOf(T item)
    {
        // We don't expect this to be called in the test.
        throw new NotImplementedException();
    }

    public IEnumerator<T> GetEnumerator()
    {
        // We don't expect this to be called in the test.
        throw new NotImplementedException();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        // We don't expect this to be called in the test.
        throw new NotImplementedException();
    }

    public void Clear()
    {
        // We don't expect this to be called in the test.
        throw new NotImplementedException();
    }

    public bool Contains(T item)
    {
        // We don't expect this to be called in the test.
        throw new NotImplementedException();
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        // We don't expect this to be called in the test.
        throw new NotImplementedException();
    }

    public bool Remove(T item)
    {
        // We don't expect this to be called in the test.
        throw new NotImplementedException();
    }

    public int Count => Items.Count;

    public bool IsReadOnly => false;
}

public class ListOperation<TO>
{
    public enum OperationType
    {
        Insert,
        RemoveAt,
    }

    public required OperationType Type { get; init; }
    public required int Index { get; init; }
    public required TO Item { get; init; }

    public override string ToString()
    {
        return $"ListOperation {Type} {Index} {Item}";
    }

    #region ListOperation equality

    public override bool Equals(object? obj)
    {
        return obj is ListOperation<TO> other && Equals(other);
    }

    public bool Equals(ListOperation<TO>? other)
    {
        return other is not null && Type == other.Type && Index == other.Index && Item!.Equals(other.Item);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Type, Index, Item);
    }

    public static bool operator ==(ListOperation<TO> left, ListOperation<TO> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ListOperation<TO> left, ListOperation<TO> right)
    {
        return !left.Equals(right);
    }

    #endregion
}

#endregion

[TestFixture]
public class ModelUpdateTest
{
    [Test(Description = "Full merge test with merged, removed and added items")]
    public void Full()
    {
        var original1 = new UpdateableItem(1);
        var original3 = new UpdateableItem(3);
        var original4 = new UpdateableItem(4);
        var update2 = new UpdateableItem(2);
        var update1 = new UpdateableItem(1);
        var update4 = new UpdateableItem(4);
        var target = new TrackableList<UpdateableItem>
        {
            Items =
            [
                original1,
                original3,
                original4,
            ],
        };
        var update = new List<UpdateableItem>
        {
            update2,
            update1,
            update4,
        };

        ModelUpdate.ApplyLists(
            target,
            update,
            (a, b) => a.Id - b.Id);

        // Compare directly rather than using `Is.EquivalentTo` because we want
        // to ensure the references are what we expect (rather than just
        // equality).
        Assert.That(target.Items.Count, Is.EqualTo(3));
        Assert.That(target.Items[0], Is.SameAs(original1));
        Assert.That(target.Items[1], Is.SameAs(update2));
        Assert.That(target.Items[2], Is.SameAs(original4));

        // All the original items should have attempted to merge.
        // original1: update2 (X), update1 (O) // update1 will be ignored now
        Assert.That(original1.AttemptedMerges, Is.EquivalentTo([2, 1]));
        // original3: update2 (X), update4 (X)
        Assert.That(original3.AttemptedMerges, Is.EquivalentTo([2, 4]));
        // original4: update2 (X), update4 (O) // update4 will be ignored now
        Assert.That(original4.AttemptedMerges, Is.EquivalentTo([2, 4]));

        // We should've only performed two list writes operations. Removes are
        // processed first, then inserts.
        Assert.That(target.Operations, Is.EquivalentTo(new List<ListOperation<UpdateableItem>>
        {
            // RemoveAt(1) => original3 => [original1, original4]
            new()
            {
                Type = ListOperation<UpdateableItem>.OperationType.RemoveAt,
                Index = 1,
                Item = original3,
            },
            // Insert(1, update2) => [original1, update2, original4]
            new()
            {
                Type = ListOperation<UpdateableItem>.OperationType.Insert,
                Index = 1,
                Item = update2,
            },
        }));
    }

    [Test(Description = "Sorts when inserting")]
    public void Sorts()
    {
        var target = new TrackableList<UpdateableItem>();
        var update = new List<UpdateableItem>
        {
            new(3),
            new(2),
            new(5),
            new(0),
            new(4),
            new(1),
            new(6),
            new(8),
            new(7),
            new(9),
        };
        ModelUpdate.ApplyLists(
            target,
            update,
            (a, b) => a.Id - b.Id);

        // Ensure it inserted with correct sorting.
        Assert.That(target.Items.Count, Is.EqualTo(10));
        var ids = target.Items.Select(i => i.Id).ToList();
        Assert.That(ids, Is.EquivalentTo([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]));

        // Ensure it performed the correct operations.
        Assert.That(target.Operations.Count, Is.EqualTo(10));
        Assert.That(target.Operations, Is.EquivalentTo(new List<ListOperation<UpdateableItem>>
        {
            // Insert(0, 3) => [3]
            new()
            {
                Type = ListOperation<UpdateableItem>.OperationType.Insert,
                Index = 0,
                Item = new UpdateableItem(3),
            },
            // Insert(0, 2) => [2, 3]
            new()
            {
                Type = ListOperation<UpdateableItem>.OperationType.Insert,
                Index = 0,
                Item = new UpdateableItem(2),
            },
            // Insert(2, 5) => [2, 3, 5]
            new()
            {
                Type = ListOperation<UpdateableItem>.OperationType.Insert,
                Index = 2,
                Item = new UpdateableItem(5),
            },
            // Insert(0, 0) => [0, 2, 3, 5]
            new()
            {
                Type = ListOperation<UpdateableItem>.OperationType.Insert,
                Index = 0,
                Item = new UpdateableItem(0),
            },
            // Insert(3, 4) => [0, 2, 3, 4, 5]
            new()
            {
                Type = ListOperation<UpdateableItem>.OperationType.Insert,
                Index = 3,
                Item = new UpdateableItem(4),
            },
            // Insert11, 1) => [0, 1, 2, 3, 4, 5]
            new()
            {
                Type = ListOperation<UpdateableItem>.OperationType.Insert,
                Index = 1,
                Item = new UpdateableItem(1),
            },
            // Insert(6, 6) => [0, 1, 2, 3, 4, 5, 6]
            new()
            {
                Type = ListOperation<UpdateableItem>.OperationType.Insert,
                Index = 6,
                Item = new UpdateableItem(6),
            },
            // Insert(7, 8) => [0, 1, 2, 3, 4, 5, 6, 8]
            new()
            {
                Type = ListOperation<UpdateableItem>.OperationType.Insert,
                Index = 7,
                Item = new UpdateableItem(8),
            },
            // Insert(7, 7) => [0, 1, 2, 3, 4, 5, 6, 7, 8]
            new()
            {
                Type = ListOperation<UpdateableItem>.OperationType.Insert,
                Index = 7,
                Item = new UpdateableItem(7),
            },
            // Insert(9, 9) => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
            new()
            {
                Type = ListOperation<UpdateableItem>.OperationType.Insert,
                Index = 9,
                Item = new UpdateableItem(9),
            },
        }));
    }

    [Test(Description = "Sorts AFTER when inserting with matching sort order")]
    public void SortsAfter()
    {
        var target = new List<UpdateableItem>
        {
            new(1),
            new(3),
            new(3),
            new(4),
        };
        var update = new List<UpdateableItem>
        {
            new(4),
            new(2),
            new(3),
            new(3),
            new(1),
        };

        ModelUpdate.ApplyLists(
            target,
            update,
            // Sort 2 and 3 as equal, so that 2 is inserted after both of the
            // 3s.
            (a, b) =>
            {
                if (a.Id is 2 or 3) return 0;
                return a.Id - b.Id;
            });

        // Ensure it inserted with correct sorting.
        Assert.That(target.Count, Is.EqualTo(5));
        var ids = target.Select(i => i.Id).ToList();
        Assert.That(ids, Is.EquivalentTo([1, 3, 3, 2, 4]));
    }
}
