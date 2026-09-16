using System.ComponentModel;

namespace MikroTikManager;

public sealed class SortableBindingList<T>(List<T> items) : BindingList<T>(items)
{
    private bool _isSorted;
    private PropertyDescriptor? _sortProperty;
    private ListSortDirection _sortDirection;

    protected override bool SupportsSortingCore => true;
    protected override bool IsSortedCore => _isSorted;
    protected override PropertyDescriptor? SortPropertyCore => _sortProperty;
    protected override ListSortDirection SortDirectionCore => _sortDirection;

    protected override void ApplySortCore(PropertyDescriptor property, ListSortDirection direction)
    {
        if (Items is not List<T> list) return;
        list.Sort((left, right) => Compare(property.GetValue(left), property.GetValue(right), direction));
        _sortProperty = property;
        _sortDirection = direction;
        _isSorted = true;
        OnListChanged(new ListChangedEventArgs(ListChangedType.Reset, -1));
    }

    protected override void RemoveSortCore()
    {
        _isSorted = false;
        _sortProperty = null;
    }

    public void RefreshSort()
    {
        if (_isSorted && _sortProperty is not null) ApplySortCore(_sortProperty, _sortDirection);
        else ResetBindings();
    }

    private static int Compare(object? left, object? right, ListSortDirection direction)
    {
        int result;
        if (ReferenceEquals(left, right)) result = 0;
        else if (left is null) result = -1;
        else if (right is null) result = 1;
        else if (left is string a && right is string b) result = StringComparer.CurrentCultureIgnoreCase.Compare(a, b);
        else if (left is IComparable comparable) result = comparable.CompareTo(right);
        else result = StringComparer.CurrentCultureIgnoreCase.Compare(left.ToString(), right.ToString());
        return direction == ListSortDirection.Ascending ? result : -result;
    }
}
