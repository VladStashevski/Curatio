using System.ComponentModel;

namespace Curatio.Desktop;

public sealed class TableColumnPreference(
    string key,
    string title,
    bool isVisible,
    Action<TableColumnPreference> changed) : INotifyPropertyChanged
{
    private bool _isVisible = isVisible;

    public string Key { get; } = key;
    public string Title { get; } = title;

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
                return;

            _isVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
            changed(this);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetWithoutNotification(bool value)
    {
        if (_isVisible == value)
            return;

        _isVisible = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
    }
}
