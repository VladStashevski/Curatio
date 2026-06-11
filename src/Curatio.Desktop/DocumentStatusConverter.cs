using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Curatio.Core;

namespace Curatio.Desktop;

public sealed class DocumentStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            DocumentStatus.Unprocessed => "Не обработан",
            DocumentStatus.Processed => "Обработан",
            DocumentStatus.NeedsReview => "Требует проверки",
            DocumentStatus.Error => "Ошибка",
            _ => ""
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Возвращает мягкую заливку или цвет текста статус-бейджа в стиле shadcn.
/// ConverterParameter: "bg" — фон, иначе — цвет текста.
/// </summary>
public sealed class DocumentStatusColorConverter : IValueConverter
{
    private static readonly IBrush ProcessedBg = Brush("#DCFCE7");
    private static readonly IBrush ProcessedFg = Brush("#15803D");
    private static readonly IBrush ReviewBg = Brush("#FEF3C7");
    private static readonly IBrush ReviewFg = Brush("#B45309");
    private static readonly IBrush ErrorBg = Brush("#FEE2E2");
    private static readonly IBrush ErrorFg = Brush("#B91C1C");
    private static readonly IBrush NeutralBg = Brush("#F4F4F5");
    private static readonly IBrush NeutralFg = Brush("#52525B");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var background = parameter as string == "bg";
        return value switch
        {
            DocumentStatus.Processed => background ? ProcessedBg : ProcessedFg,
            DocumentStatus.NeedsReview => background ? ReviewBg : ReviewFg,
            DocumentStatus.Error => background ? ErrorBg : ErrorFg,
            _ => background ? NeutralBg : NeutralFg
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}
