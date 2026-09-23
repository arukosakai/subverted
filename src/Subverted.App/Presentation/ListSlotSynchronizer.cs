namespace Subverted.App.Presentation;

/// <summary>
/// <see cref="ChangeListSynchronizer"/>'s fewest-edits merge for a list of slots: a key that stays
/// keeps its slot and has its content updated, so a changed line is never a replaced container —
/// which would take the list's selection and keyboard focus with it.
/// </summary>
public static class ListSlotSynchronizer
{
    /// <param name="shown">Mutated in place; afterwards its contents equal <paramref name="fresh"/>, in order.</param>
    /// <param name="fresh">The new layout, unique by <paramref name="keyOf"/>.</param>
    /// <param name="create">Makes the slot for a key that was not shown.</param>
    public static void Apply<TSlot, T>(
        IList<TSlot> shown,
        IReadOnlyList<T> fresh,
        Func<T, string> keyOf,
        Func<T, TSlot> create
    )
        where TSlot : IListSlot<T>
    {
        var wanted = fresh.Select(keyOf).ToHashSet(StringComparer.Ordinal);
        for (var index = shown.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(keyOf(shown[index].Content)))
            {
                shown.RemoveAt(index);
            }
        }

        for (var index = 0; index < fresh.Count; index++)
        {
            var item = fresh[index];
            var key = keyOf(item);
            var existing = IndexOf(shown, key, keyOf, from: index);
            if (existing < 0)
            {
                shown.Insert(index, create(item));
                continue;
            }

            if (existing != index)
            {
                var slot = shown[existing];
                shown.RemoveAt(existing);
                shown.Insert(index, slot);
            }

            // Equal content is left alone, so a line that did not change raises nothing at all.
            if (!EqualityComparer<T>.Default.Equals(shown[index].Content, item))
            {
                shown[index].Content = item;
            }
        }
    }

    private static int IndexOf<TSlot, T>(
        IList<TSlot> slots,
        string key,
        Func<T, string> keyOf,
        int from
    )
        where TSlot : IListSlot<T>
    {
        for (var index = from; index < slots.Count; index++)
        {
            if (keyOf(slots[index].Content) == key)
            {
                return index;
            }
        }

        return -1;
    }
}
