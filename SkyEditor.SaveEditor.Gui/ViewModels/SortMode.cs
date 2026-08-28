using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SkyEditor.SaveEditor.Gui.ViewModels;

/// <summary>
/// How the Friend Areas tab's left-hand list is ordered. Anything other than
/// <see cref="FriendArea"/> flattens every resident across every area into one sorted list (see
/// <see cref="MainWindowViewModel.FlatSortedResidents"/>) instead of the grouped/disclosure view.
/// </summary>
public enum SortMode
{
    FriendArea,
    Name,
    Number,
    Level,
    IQ,
}

/// <summary>Display text for a <see cref="SortMode"/> value in the sort-by ComboBox.</summary>
public sealed class SortModeDisplayConverter : IValueConverter
{
    public static readonly SortModeDisplayConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        SortMode.FriendArea => "Friend Area",
        SortMode.Name => "Name",
        SortMode.Number => "Number (Dex #)",
        SortMode.Level => "Level",
        SortMode.IQ => "IQ",
        _ => value?.ToString(),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
