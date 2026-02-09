using System;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Media;
using Avalonia.Metadata;

namespace Coder.Desktop.App.Converters;

/// <summary>
/// An item in a <see cref="DependencyObjectSelector{TK,TV}"/>.
/// </summary>
/// <typeparam name="TK">Key type.</typeparam>
/// <typeparam name="TV">Value type.</typeparam>
public class DependencyObjectSelectorItem<TK, TV> : AvaloniaObject
    where TK : IEquatable<TK>
    where TV : class
{
    public static readonly StyledProperty<TK?> KeyProperty =
        AvaloniaProperty.Register<DependencyObjectSelectorItem<TK, TV>, TK?>(nameof(Key));

    public static readonly StyledProperty<TV?> ValueProperty =
        AvaloniaProperty.Register<DependencyObjectSelectorItem<TK, TV>, TV?>(nameof(Value));

    public TK? Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    public TV? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
}

/// <summary>
/// Avalonia port of the WinUI DependencyObjectSelector.
///
/// This is a simplified implementation intended for XAML resources where a view-model key
/// selects a value from a list.
/// </summary>
/// <typeparam name="TK">Key type.</typeparam>
/// <typeparam name="TV">Value type.</typeparam>
public class DependencyObjectSelector<TK, TV> : AvaloniaObject
    where TK : IEquatable<TK>
    where TV : class
{
    public static readonly StyledProperty<TK?> SelectedKeyProperty =
        AvaloniaProperty.Register<DependencyObjectSelector<TK, TV>, TK?>(nameof(SelectedKey));

    public static readonly DirectProperty<DependencyObjectSelector<TK, TV>, TV?> SelectedObjectProperty =
        AvaloniaProperty.RegisterDirect<DependencyObjectSelector<TK, TV>, TV?>(nameof(SelectedObject), o => o.SelectedObject);

    private TV? _selectedObject;
    private DependencyObjectSelectorItem<TK, TV>? _selectedItem;

    public DependencyObjectSelector()
    {
        References = new AvaloniaList<DependencyObjectSelectorItem<TK, TV>>();
        References.CollectionChanged += (_, __) => UpdateSelectedObject();

        UpdateSelectedObject();
    }

    /// <summary>
    /// The list of possible references.
    /// </summary>
    [Content]
    public AvaloniaList<DependencyObjectSelectorItem<TK, TV>> References { get; }

    /// <summary>
    /// The key to select.
    /// </summary>
    public TK? SelectedKey
    {
        get => GetValue(SelectedKeyProperty);
        set => SetValue(SelectedKeyProperty, value);
    }

    /// <summary>
    /// The selected value.
    /// </summary>
    public TV? SelectedObject
    {
        get => _selectedObject;
        private set => SetAndRaise(SelectedObjectProperty, ref _selectedObject, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedKeyProperty)
        {
            UpdateSelectedObject();
        }
    }

    private void UpdateSelectedObject()
    {
        VerifyReferencesUniqueKeys();

        var item = References.FirstOrDefault(i =>
                       (i.Key == null && SelectedKey == null) ||
                       (i.Key != null && SelectedKey != null && i.Key.Equals(SelectedKey)))
                   ?? References.FirstOrDefault(i => i.Key == null);

        if (!ReferenceEquals(item, _selectedItem))
        {
            if (_selectedItem != null)
            {
                _selectedItem.PropertyChanged -= SelectedItem_OnPropertyChanged;
            }

            _selectedItem = item;

            if (_selectedItem != null)
            {
                _selectedItem.PropertyChanged += SelectedItem_OnPropertyChanged;
            }
        }

        SelectedObject = item?.Value;
    }

    private void SelectedItem_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == DependencyObjectSelectorItem<TK, TV>.ValueProperty)
        {
            SelectedObject = _selectedItem?.Value;
        }
    }

    private void VerifyReferencesUniqueKeys()
    {
        var keys = References.Select(i => i.Key).Distinct().ToArray();
        if (keys.Length != References.Count)
        {
            throw new ArgumentException("DependencyObjectSelector keys must be unique.");
        }
    }
}

public sealed class StringToBrushSelectorItem : DependencyObjectSelectorItem<string, Brush>;

public sealed class StringToBrushSelector : DependencyObjectSelector<string, Brush>;

public sealed class StringToStringSelectorItem : DependencyObjectSelectorItem<string, string>;

public sealed class StringToStringSelector : DependencyObjectSelector<string, string>;
