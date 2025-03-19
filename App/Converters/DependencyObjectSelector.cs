using System;
using System.Linq;
using Windows.Foundation.Collections;
using Windows.UI.Xaml.Markup;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Coder.Desktop.App.Converters;

// This file uses manual DependencyProperty properties rather than
// DependencyPropertyGenerator since it doesn't seem to work properly with
// generics.

public class DependencyObjectSelectorItem<TK, TV> : DependencyObject
    where TK : IEquatable<TK>
{
    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.Register(nameof(Key),
            typeof(TK?),
            typeof(DependencyObjectSelectorItem<TK, TV>),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value),
            typeof(TV?),
            typeof(DependencyObjectSelectorItem<TK, TV>),
            new PropertyMetadata(null));

    public TK? Key
    {
        get => (TK?)GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    public TV? Value
    {
        get => (TV?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
}

[ContentProperty(Name = nameof(References))]
public class DependencyObjectSelector<TK, TV> : DependencyObject
    where TK : IEquatable<TK>
{
    public static readonly DependencyProperty ReferencesProperty =
        DependencyProperty.Register(nameof(References),
            typeof(DependencyObjectCollection),
            typeof(DependencyObjectSelector<TK, TV>),
            new PropertyMetadata(null, ReferencesPropertyChanged));

    public static readonly DependencyProperty SelectedKeyProperty =
        DependencyProperty.Register(nameof(SelectedKey),
            typeof(TK?),
            typeof(DependencyObjectSelector<TK, TV>),
            new PropertyMetadata(null, SelectedPropertyChanged));

    public static readonly DependencyProperty SelectedObjectProperty =
        DependencyProperty.Register(nameof(SelectedObject),
            typeof(TV?),
            typeof(DependencyObjectSelector<TK, TV>),
            new PropertyMetadata(null));

    public DependencyObjectCollection? References
    {
        get => (DependencyObjectCollection?)GetValue(ReferencesProperty);
        set
        {
            // Ensure unique keys and that the values are DependencyObjectSelectorItem<K, V>.
            if (value != null)
            {
                var items = value.OfType<DependencyObjectSelectorItem<TK, TV>>().ToArray();
                var keys = items.Select(i => i.Key).Distinct().ToArray();
                if (keys.Length != value.Count)
                    throw new ArgumentException("ObservableCollection Keys must be unique.");
            }

            SetValue(ReferencesProperty, value);
        }
    }

    public TK? SelectedKey
    {
        get => (TK?)GetValue(SelectedKeyProperty);
        set => SetValue(SelectedKeyProperty, value);
    }

    public TV? SelectedObject
    {
        get => (TV?)GetValue(SelectedObjectProperty);
        set => SetValue(SelectedObjectProperty, value);
    }

    public DependencyObjectSelector()
    {
        References = [];
    }

    private void OnVectorChangedReferences(IObservableVector<DependencyObject> sender, IVectorChangedEventArgs args)
    {
        UpdateSelectedObject();
    }

    private void UpdateSelectedObject()
    {
        if (References != null)
        {
            var references = References.OfType<DependencyObjectSelectorItem<TK, TV>>().ToArray();
            var item = references
                           .FirstOrDefault(i =>
                               (i.Key == null && SelectedKey == null) ||
                               (i.Key != null && SelectedKey != null && i.Key!.Equals(SelectedKey!)))
                       ?? references.FirstOrDefault(i => i.Key == null);
            if (item is not null)
            {
                BindingOperations.SetBinding
                (
                    this,
                    SelectedObjectProperty,
                    new Binding
                    {
                        Source = item,
                        Path = new PropertyPath(nameof(DependencyObjectSelectorItem<TK, TV>.Value)),
                    }
                );
                return;
            }
        }

        ClearValue(SelectedObjectProperty);
    }

    private static void ReferencesPropertyChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        var self = obj as DependencyObjectSelector<TK, TV>;
        if (self == null) return;
        var oldValue = args.OldValue as DependencyObjectCollection;
        if (oldValue != null)
            oldValue.VectorChanged -= self.OnVectorChangedReferences;
        var newValue = args.NewValue as DependencyObjectCollection;
        if (newValue != null)
            newValue.VectorChanged += self.OnVectorChangedReferences;
    }

    private static void SelectedPropertyChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        var self = obj as DependencyObjectSelector<TK, TV>;
        self?.UpdateSelectedObject();
    }
}

public sealed class StringToBrushSelectorItem : DependencyObjectSelectorItem<string, Brush>;

public sealed class StringToBrushSelector : DependencyObjectSelector<string, Brush>;
