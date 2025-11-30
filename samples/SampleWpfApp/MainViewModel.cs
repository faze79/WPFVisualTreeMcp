using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SampleWpfApp;

/// <summary>
/// Main view model for the sample application.
/// Demonstrates various binding scenarios for testing the Visual Tree Inspector.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private string _userName = string.Empty;
    private string _email = string.Empty;
    private int _age;
    private string _status = "Active";
    private bool _isActive = true;
    private string _newItemText = string.Empty;
    private ItemModel? _selectedItem;

    public MainViewModel()
    {
        Items = new ObservableCollection<ItemModel>
        {
            new ItemModel { Name = "Item 1", Description = "First sample item" },
            new ItemModel { Name = "Item 2", Description = "Second sample item" },
            new ItemModel { Name = "Item 3", Description = "Third sample item" }
        };

        SubmitCommand = new RelayCommand(Submit, CanSubmit);
        ClearCommand = new RelayCommand(Clear);
        AddItemCommand = new RelayCommand(AddItem, CanAddItem);
        RemoveItemCommand = new RelayCommand(RemoveItem, () => HasSelectedItem);
    }

    public string UserName
    {
        get => _userName;
        set
        {
            if (SetProperty(ref _userName, value))
            {
                ((RelayCommand)SubmitCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    public int Age
    {
        get => _age;
        set => SetProperty(ref _age, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public string NewItemText
    {
        get => _newItemText;
        set
        {
            if (SetProperty(ref _newItemText, value))
            {
                ((RelayCommand)AddItemCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public ItemModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(HasSelectedItem));
                ((RelayCommand)RemoveItemCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedItem => SelectedItem != null;

    public ObservableCollection<ItemModel> Items { get; }

    public ICommand SubmitCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand AddItemCommand { get; }
    public ICommand RemoveItemCommand { get; }

    private void Submit()
    {
        // Simulate form submission
        Status = $"Submitted at {DateTime.Now:HH:mm:ss}";
    }

    private bool CanSubmit() => !string.IsNullOrWhiteSpace(UserName);

    private void Clear()
    {
        UserName = string.Empty;
        Email = string.Empty;
        Age = 0;
        Status = "Active";
        IsActive = true;
    }

    private void AddItem()
    {
        Items.Add(new ItemModel
        {
            Name = $"Item {Items.Count + 1}",
            Description = NewItemText
        });
        NewItemText = string.Empty;
    }

    private bool CanAddItem() => !string.IsNullOrWhiteSpace(NewItemText);

    private void RemoveItem()
    {
        if (SelectedItem != null)
        {
            Items.Remove(SelectedItem);
            SelectedItem = null;
        }
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}

/// <summary>
/// Simple model for list items.
/// </summary>
public class ItemModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _description = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            if (_description != value)
            {
                _description = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Simple ICommand implementation.
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
