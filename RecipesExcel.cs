// ============================================================================
// RecipesExcel.cs — экспорт и импорт рецептов в книгу Excel (.xlsx).
// Каждый рецепт — отдельный лист, имя листа = название детали/прибора.
// Структура листа (первая строка — заголовки, данные со второй):
//   • столбцы A–D — таблица «Состав»: метка, наименование, кол-во, ед. изм.;
//   • со столбца F — таблица «Операции»: метка, описание, время (мин на 1 шт),
//     время без участия человека (мин на одно выполнение операции);
//   • K1/K2 — «Изготавливать за раз, шт» (партия изготовления рецепта).
// Числа принимаются и с запятой, и с точкой (как во всём приложении).
// ============================================================================
using ClosedXML.Excel;
using Recipes.Models;

namespace Recipes.Services;

public static class RecipesExcel
{
    // Колонки листа (1-based).
    private const int CompLabelCol = 1;      // A
    private const int CompNameCol = 2;       // B
    private const int CompQtyCol = 3;        // C
    private const int CompUnitCol = 4;       // D
    private const int OpLabelCol = 6;        // F
    private const int OpDescCol = 7;         // G
    private const int OpTimeCol = 8;         // H
    private const int OpNonHumanCol = 9;     // I
    private const int RunQtyCol = 11;        // K

    private const int HeaderRow = 1;
    private const int FirstDataRow = 2;
    private const int MaxSheetNameLength = 31;   // ограничение Excel

    // ── Экспорт ────────────────────────────────────────────────────────

    /// <summary>Сохранить все рецепты в книгу .xlsx (лист на каждый рецепт).</summary>
    public static void ExportToExcel(string path, IEnumerable<Recipe> recipes)
    {
        using var workbook = new XLWorkbook();

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var recipe in recipes)
        {
            var ws = workbook.Worksheets.Add(SanitizeSheetName(recipe.Name, usedNames));

            // Заголовки.
            ws.Cell(HeaderRow, CompLabelCol).Value = "Метка";
            ws.Cell(HeaderRow, CompNameCol).Value = "Наименование";
            ws.Cell(HeaderRow, CompQtyCol).Value = "Кол-во";
            ws.Cell(HeaderRow, CompUnitCol).Value = "Ед. изм.";
            ws.Cell(HeaderRow, OpLabelCol).Value = "Метка";
            ws.Cell(HeaderRow, OpDescCol).Value = "Описание операции";
            ws.Cell(HeaderRow, OpTimeCol).Value = "Время, мин";
            ws.Cell(HeaderRow, OpNonHumanCol).Value = "Без человека, мин";
            ws.Cell(HeaderRow, RunQtyCol).Value = "Изготавливать за раз, шт";

            // Состав — столбцы A–D.
            var row = FirstDataRow;
            foreach (var c in recipe.Components)
            {
                ws.Cell(row, CompLabelCol).Value = c.Label;
                ws.Cell(row, CompNameCol).Value = c.Name;
                ws.Cell(row, CompQtyCol).Value = c.Qty;
                ws.Cell(row, CompUnitCol).Value = c.Unit;
                row++;
            }

            // Операции — со столбца F. Пустая ячейка времени = нормировка
            // не выполнена (так же, как в редакторе).
            row = FirstDataRow;
            foreach (var o in recipe.Operations)
            {
                ws.Cell(row, OpLabelCol).Value = o.Label;
                ws.Cell(row, OpDescCol).Value = o.Description;
                if (o.HasTime)
                    ws.Cell(row, OpTimeCol).Value = o.TimeMinutes;
                if (o.HasNonHumanTime)
                    ws.Cell(row, OpNonHumanCol).Value = o.NonHumanMinutes;
                row++;
            }

            // Партия изготовления.
            ws.Cell(FirstDataRow, RunQtyCol).Value = recipe.RunQty;

            // Фиксированные ширины — описания операций бывают длинными.
            ws.Column(1).Width = 10;
            ws.Column(2).Width = 42;
            ws.Column(3).Width = 10;
            ws.Column(4).Width = 10;
            ws.Column(5).Width = 3;   // разделитель между таблицами
            ws.Column(6).Width = 10;
            ws.Column(7).Width = 62;
            ws.Column(8).Width = 12;
            ws.Column(9).Width = 18;
            ws.Column(11).Width = 26;

            ws.Range(HeaderRow, 1, HeaderRow, RunQtyCol).Style.Font.Bold = true;
        }

        workbook.SaveAs(path);
    }

    // ── Импорт ─────────────────────────────────────────────────────────

    /// <summary>
    /// Прочитать рецепты из книги .xlsx: каждый лист — рецепт
    /// (имя листа = название детали/прибора).
    /// </summary>
    public static Dictionary<string, Recipe> ImportFromExcel(string path)
    {
        using var workbook = new XLWorkbook(path);
        var result = new Dictionary<string, Recipe>(StringComparer.CurrentCulture);

        foreach (var ws in workbook.Worksheets)
        {
            var recipe = new Recipe(ws.Name.Trim());
            var lastUsedRow = ws.LastRowUsed();
            var lastRow = lastUsedRow is not null ? lastUsedRow.RowNumber() : HeaderRow;

            // Партия изготовления (K2); пусто или не число → 1.
            recipe.RunQty =
                TryReadNumber(ws.Cell(FirstDataRow, RunQtyCol), out var runQty) &&
                runQty >= 1
                    ? (int)Math.Round(runQty)
                    : 1;

            // Состав — столбцы A–D.
            for (var row = FirstDataRow; row <= lastRow; row++)
            {
                var label = Text(ws.Cell(row, CompLabelCol));
                var name = Text(ws.Cell(row, CompNameCol));
                var unit = Text(ws.Cell(row, CompUnitCol));
                var hasQty = TryReadNumber(ws.Cell(row, CompQtyCol), out var qty);

                if (label.Length == 0 && name.Length == 0 && !hasQty)
                    continue;   // пустая строка

                if (name.Length == 0)
                    throw new InvalidOperationException(
                        $"Лист «{ws.Name}», строка {row}: у компонента не указано наименование.");

                if (!hasQty || qty <= 0)
                    throw new InvalidOperationException(
                        $"Лист «{ws.Name}», строка {row}: количество компонента " +
                        "должно быть числом больше нуля (например 8 или 1,5).");

                recipe.Components.Add(new Component(
                    label, name, qty, unit.Length == 0 ? "шт" : unit));
            }

            // Операции — со столбца F.
            for (var row = FirstDataRow; row <= lastRow; row++)
            {
                var label = Text(ws.Cell(row, OpLabelCol));
                var description = Text(ws.Cell(row, OpDescCol));
                var hasTime = TryReadNumber(ws.Cell(row, OpTimeCol), out var time);
                var hasNonHuman =
                    TryReadNumber(ws.Cell(row, OpNonHumanCol), out var nonHuman);

                if (label.Length == 0 && description.Length == 0 && !hasTime && !hasNonHuman)
                    continue;   // пустая строка

                if (description.Length == 0)
                    throw new InvalidOperationException(
                        $"Лист «{ws.Name}», строка {row}: пустое описание операции.");

                if (hasTime && time < 0)
                    throw new InvalidOperationException(
                        $"Лист «{ws.Name}», строка {row}: время операции — число минут (0 или пусто = нормировка не выполнена).");

                if (hasNonHuman && nonHuman < 0)
                    throw new InvalidOperationException(
                        $"Лист «{ws.Name}», строка {row}: время без участия человека — число минут (0 или пусто = нет).");

                recipe.Operations.Add(new Operation(
                    label, description,
                    hasTime && time > 0 ? time : null,
                    hasNonHuman && nonHuman > 0 ? nonHuman : null));
            }

            result[recipe.Name] = recipe;
        }

        return result;
    }

    // ── Вспомогательные методы ─────────────────────────────────────────

    private static string Text(IXLCell cell)
        => cell.IsEmpty() ? "" : cell.GetString().Trim();

    /// <summary>Число из ячейки: числовой формат или текст с запятой/точкой.</summary>
    private static bool TryReadNumber(IXLCell? cell, out double value)
    {
        value = 0;
        if (cell is null || cell.IsEmpty())
            return false;

        if (cell.DataType == XLDataType.Number)
        {
            value = cell.GetDouble();
            return true;
        }

        return AppFormat.TryParseDouble(cell.GetString(), out value);
    }

    /// <summary>
    /// Имя листа: запрещённые символы → пробел, максимум 31 символ,
    /// уникальность в книге («Имя 2», «Имя 3»… при повторах).
    /// </summary>
    private static string SanitizeSheetName(string name, HashSet<string> used)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? ' ' : ch).ToArray())
            .Trim();

        if (cleaned.Length > MaxSheetNameLength)
            cleaned = cleaned[..MaxSheetNameLength].Trim();
        if (cleaned.Length == 0)
            cleaned = "Рецепт";

        var unique = cleaned;
        var n = 2;
        while (!used.Add(unique))
        {
            var suffix = " " + n++;
            var cut = Math.Min(cleaned.Length, MaxSheetNameLength - suffix.Length);
            unique = cleaned[..cut].TrimEnd() + suffix;
        }

        return unique;
    }
}
