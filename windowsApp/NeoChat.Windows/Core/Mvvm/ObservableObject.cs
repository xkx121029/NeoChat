using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NeoChat.Windows.Core.Mvvm;

/// <summary>
/// Minimal INotifyPropertyChanged base. The task sheet forbids new package dependencies, so this
/// replaces the ObservableObject that CommunityToolkit.Mvvm would otherwise supply; the surface
/// (SetProperty / OnPropertyChanged) is intentionally the same so a swap stays mechanical.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
