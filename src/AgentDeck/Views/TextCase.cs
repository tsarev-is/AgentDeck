using System.Globalization;
using Avalonia.Data.Converters;

namespace AgentDeck.Views;

/// <summary>
/// Приведение текста к верхнему регистру: заголовки Industry всегда uppercase,
/// а модель хранит исходное имя.
/// </summary>
public sealed class TextCase : IValueConverter
{
    /// <summary>
    /// Конвертер в верхний регистр.
    /// </summary>
    public static readonly TextCase Upper = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string text ? text.ToUpper(culture) : value;

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
