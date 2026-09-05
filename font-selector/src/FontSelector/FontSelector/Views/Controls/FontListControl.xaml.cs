using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FontSelector.ViewModels;

namespace FontSelector.Views.Controls;

public partial class FontListControl : UserControl
{
    public FontListControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        if (e.NewValue is MainViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.FilteredFamilies) or nameof(MainViewModel.SelectedFamily))
        {
            ScrollSelectedIntoView();
        }
    }

    private void FontList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ScrollSelectedIntoView();
    }

    private void ScrollSelectedIntoView()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var item = FontList.SelectedItem;
            if (item is not null)
            {
                FontList.ScrollIntoView(item);
                // 仮想化スタックパネルのレイアウト確定後に再度スクロール補正
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    if (FontList.SelectedItem is not null)
                    {
                        FontList.ScrollIntoView(FontList.SelectedItem);
                    }
                });
            }
        });
    }
}
