// ============================================================================
// AppFormat.cs — форматирование количеств и времени, разбор чисел
// (порт _format_qty из planner.py + Format из calculator.Requirement).
// ============================================================================
using System.Globalization;

namespace Recipes.Services;

public static class AppFormat
{
    /// <summary>8 → "8", 1.5 → "1.5" (без хвостовых нулей).</summary>
    public static string FormatQty(double qty)
    {
        if (qty == Math.Truncate(qty) && Math.Abs(qty) < 1e15)
            return ((long)qty).ToString(CultureInfo.InvariantCulture);
        return qty.ToString("0.######", CultureInfo.InvariantCulture);
    }

    /// <summary>"(8)[шт]" или "(1.5)[м]" — формат исходного проекта prodplan.</summary>
    public static string FormatQtyUnit(double qty, string unit)
        => $"({FormatQty(qty)})[{unit}]";

    /// <summary>
    /// Длительность в человекочитаемом виде:
    /// 45 → "45 мин", 170.5 ч → "170 ч 30 мин", больше 48 ч → "3 дн 2 ч 5 мин".
    /// </summary>
    public static string FormatMinutes(long totalMinutes)
    {
        if (totalMinutes < 60)
            return $"{totalMinutes} мин";

        var hours = totalMinutes / 60;
        var mins = totalMinutes % 60;

        if (totalMinutes < 48 * 60)
            return mins == 0 ? $"{hours} ч" : $"{hours} ч {mins} мин";

        var days = hours / 24;
        var restHours = hours % 24;
        var result = $"{days} дн {restHours} ч";
        if (mins > 0)
            result += $" {mins} мин";
        return result;
    }

    /// <summary>
    /// Разбор числа: принимает и запятую, и точку ("1,5" и "1.5").
    /// </summary>
    public static bool TryParseDouble(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float,
            CultureInfo.InvariantCulture, out value);
    }
}
