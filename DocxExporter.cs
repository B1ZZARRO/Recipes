// ============================================================================
// DocxExporter.cs — генерация заказ-нарядов .docx по шаблону
// (порт docx_tasks.py). Плейсхолдеры шаблона:
//   [qwerty]  → «Операция: Изготовление <деталь> (партия …)»
//   [asdfgh]  → «Использовать материалы:» + список
//   [zxcvbn]  → «Произвести операции:» + список
//   [poiuyt]  → ожидаемое время выполнения исполнителем (ceil по операциям,
//               +10% сверху)
//   [lkjhgf]  → ожидаемое время задержки на независимые от человека
//               процессы (застывание и т.п. — по одному разу на операцию)
//   [mnbvcx]  → общий срок: Tисп + Tнез, через это время от старта работ
//               заказ-наряд должен быть выполнен
// Плейсхолдеры в шаблоне разорваны по run'ам — перед заменой текст
// абзаца склеивается (как это делал python-docx).
// ============================================================================
using System.Reflection;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Recipes.Models;

namespace Recipes.Services;

public class DocxExportException : Exception
{
    public DocxExportException(string message) : base(message) { }
}

public static class DocxExporter
{
    public const string TemplateFileName = "ЗАКАЗ_НАРЯД_ФОРМА.docx";

    private const string PlaceholderOperation = "[qwerty]";
    private const string PlaceholderMaterials = "[asdfgh]";
    private const string PlaceholderOperations = "[zxcvbn]";
    private const string PlaceholderExecutorTime = "[poiuyt]";   // Tисп
    private const string PlaceholderDelayTime = "[lkjhgf]";      // Tнез
    private const string PlaceholderDeadlineTime = "[mnbvcx]";   // Tобщ

    /// <summary>Надбавка на перекуры/подготовку: +10% к чистому времени операций.</summary>
    public const double IdleFactor = 0.10;

    private static readonly string[] MonthsRu =
    {
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря",
    };

    /// <summary>
    /// Достать шаблон наряда. Если файл «ЗАКАЗ_НАРЯД_ФОРМА.docx» лежит рядом
    /// с exe — используется он (можно подменить своим шаблоном с теми же
    /// плейсхолдерами); иначе встроенный ресурс извлекается на диск.
    /// </summary>
    public static string EnsureTemplate(string baseDir)
    {
        var path = Path.Combine(baseDir, TemplateFileName);
        if (File.Exists(path))
            return path;

        var asm = Assembly.GetExecutingAssembly();
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(TemplateFileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new DocxExportException(
                $"Встроенный шаблон «{TemplateFileName}» не найден в сборке.");

        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var file = File.Create(path);
        stream.CopyTo(file);
        return path;
    }

    /// <summary>
    /// Ожидаемое время выполнения ИСПОЛНИТЕЛЕМ — строка для [poiuyt]
    /// (порт _compute_expected_total_time / _format_expected_total_time):
    /// по каждой операции ceil(норма × количество), сумма, +10% сверху.
    /// Все операции без нормы → «нормировка не выполнена».
    /// </summary>
    public static string FormatExpectedExecutorTime(PlannerTask task, out bool hasUntimed)
    {
        hasUntimed = task.HasUntimedOperations;

        var pure = task.ExecutorPureMinutes;
        if (pure == 0)
            return "нормировка не выполнена";

        var text = AppFormat.FormatMinutes(pure + task.IdleMinutes);
        if (hasUntimed)
            text += " (нормировка не выполнена)";
        return text;
    }

    /// <summary>
    /// Ожидаемое время задержки на независимые от человека процессы
    /// (застывание, сушка…), суммарно по операциям партии — строка для [lkjhgf].
    /// </summary>
    public static string FormatExpectedNonHumanTime(PlannerTask task)
        => AppFormat.FormatMinutes(task.NonHumanMinutes);

    /// <summary>
    /// Общий срок выполнения партии: Tисп + Tнез — строка для [mnbvcx];
    /// через это время от момента старта работ заказ-наряд должен быть выполнен.
    /// </summary>
    public static string FormatExpectedDeadlineTime(PlannerTask task)
    {
        if (task.ExecutorPureMinutes == 0 && task.NonHumanMinutes == 0)
            return "нормировка не выполнена";
        return AppFormat.FormatMinutes(task.TotalMinutes);
    }

    /// <summary>Создать .docx наряда из шаблона (порт fill_task_docx).</summary>
    public static void FillTaskDocx(
        PlannerTask task, string outputPath, string templatePath, string? deviceName)
    {
        if (!File.Exists(templatePath))
            throw new DocxExportException($"Шаблон не найден: {templatePath}");

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.Copy(templatePath, outputPath, overwrite: true);

        using var doc = WordprocessingDocument.Open(outputPath, true);
        var main = doc.MainDocumentPart
            ?? throw new DocxExportException("В шаблоне нет основной части документа.");
        var body = main.Document.Body
            ?? throw new DocxExportException("В шаблоне нет тела документа.");

        // ── Шапка: «ЗАКАЗ-НАРЯД № ________» → «ЗАКАЗ-НАРЯД № 001» ──
        var titleParagraph = body.Elements<Paragraph>()
            .FirstOrDefault(p => p.InnerText.Contains("ЗАКАЗ-НАРЯД"));
        titleParagraph?.Also(p => SetParagraphText(p, $"ЗАКАЗ-НАРЯД № {task.Number:000}", bold: true));

        // ── Таблица шапки ──
        var table = body.Elements<Table>().FirstOrDefault()
            ?? throw new DocxExportException("В шаблоне нет таблиц.");
        var rows = table.Elements<TableRow>().ToList();
        if (rows.Count < 8)
            throw new DocxExportException("В таблице шапки мало строк (ожидается 8).");

        // R0 — дата выдачи (текущая дата).
        FirstParagraph(rows[0])?.Also(p =>
        {
            var today = DateTime.Now;
            SetParagraphText(p,
                $"Дата выдачи: «{today.Day:00}» {MonthsRu[today.Month - 1]} {today.Year} г.",
                bold: false);
        });

        // R2 — «Операция: [qwerty][asdfgh][zxcvbn]».
        var opCell = FirstCell(rows[2]);
        var target = opCell?.Elements<Paragraph>().FirstOrDefault(p =>
                p.InnerText.Contains(PlaceholderOperation) ||
                p.InnerText.Contains(PlaceholderMaterials) ||
                p.InnerText.Contains(PlaceholderOperations))
            ?? throw new DocxExportException(
                $"В шаблоне не найдены плейсхолдеры {PlaceholderOperation}/{PlaceholderMaterials}/{PlaceholderOperations}.");

        var qtyStr = AppFormat.FormatQty(task.BatchQty);
        var operationText =
            $"Изготовление «{task.DetailName}» (партия {task.BatchIndex} из {task.BatchCount}, {qtyStr} шт)";

        // «Операция: …» — заголовок, жирный.
        var cleaned = target.InnerText
            .Replace(PlaceholderOperation, "")
            .Replace(PlaceholderMaterials, "")
            .Replace(PlaceholderOperations, "")
            .TrimEnd();
        if (!cleaned.EndsWith(':'))
            cleaned += ":";
        SetParagraphText(target, $"{cleaned} {operationText}", bold: true);

        // Материалы (порт _replace_placeholders).
        var anchor = target;
        anchor = InsertParagraphAfter(anchor, "Использовать материалы:", bold: true);
        if (task.Materials.Count > 0)
        {
            foreach (var m in task.Materials)
                anchor = InsertParagraphAfter(anchor, "  " + m.Format(), bold: false);
        }
        else
        {
            anchor = InsertParagraphAfter(anchor, "  (материалы не требуются)", bold: false);
        }

        // Операции.
        anchor = InsertParagraphAfter(anchor, "Произвести операции:", bold: true);
        if (task.Operations.Count > 0)
        {
            foreach (var o in task.Operations)
                anchor = InsertParagraphAfter(anchor, "  " + o.Format(), bold: false);
        }
        else
        {
            InsertParagraphAfter(anchor, "  (операций нет — это покупное сырьё)", bold: false);
        }

        // R4 — перечень номеров изделий.
        FirstParagraph(rows[4])?.Also(p =>
        {
            var line = "Перечень номеров изделий (плат): ";
            if (!string.IsNullOrWhiteSpace(deviceName))
                line += $"{deviceName} партия {task.BatchIndex} из {task.BatchCount} ({qtyStr} шт)";
            SetParagraphText(p, line, bold: false);
        });

        // R6 — количество изделий в партии.
        FirstParagraph(rows[6])?.Also(p =>
            SetParagraphText(p, $"Количество изделий в партии: {qtyStr} шт.", bold: false));

        // R7 — срок выполнения: Tисп [poiuyt], задержка Tнез [lkjhgf],
        // общий срок Tобщ [mnbvcx] — заменяем только плейсхолдеры, текст строки сохраняем.
        FirstParagraph(rows[7])?.Also(p =>
        {
            if (p.InnerText.Contains(PlaceholderExecutorTime) ||
                p.InnerText.Contains(PlaceholderDelayTime) ||
                p.InnerText.Contains(PlaceholderDeadlineTime))
            {
                ReplaceInParagraph(p, PlaceholderExecutorTime,
                    FormatExpectedExecutorTime(task, out _));
                ReplaceInParagraph(p, PlaceholderDelayTime,
                    FormatExpectedNonHumanTime(task));
                ReplaceInParagraph(p, PlaceholderDeadlineTime,
                    FormatExpectedDeadlineTime(task));
            }
        });

        main.Document.Save();
    }

    // ── Низкоуровневые операции с абзацами ─────────────────────────────

    private static Paragraph? FirstParagraph(TableRow row)
        => row.Elements<TableCell>().FirstOrDefault()?.Elements<Paragraph>().FirstOrDefault();

    private static TableCell? FirstCell(TableRow row)
        => row.Elements<TableCell>().FirstOrDefault();

    /// <summary>Заменить весь текст абзаца, сохранив форматирование первого run'а
    /// (порт _replace_paragraph_text).</summary>
    private static void SetParagraphText(Paragraph paragraph, string text, bool bold)
    {
        var runs = paragraph.Elements<Run>().ToList();

        if (runs.Count == 0)
        {
            var run = new Run();
            if (bold)
                run.Append(new RunProperties(new Bold()));
            run.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            paragraph.Append(run);
            return;
        }

        // Оставляем только первый run, остальные удаляем.
        foreach (var run in runs.Skip(1))
            run.Remove();

        var first = runs[0];
        foreach (var t in first.Elements<Text>().ToList())
            t.Remove();
        foreach (var b in first.Elements<Break>().ToList())
            b.Remove();
        foreach (var tab in first.Elements<TabChar>().ToList())
            tab.Remove();
        first.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });

        if (bold)
        {
            var rp = first.GetFirstChild<RunProperties>();
            if (rp == null)
            {
                rp = new RunProperties();
                first.PrependChild(rp);
            }
            if (rp.Bold == null)
                rp.Bold = new Bold();
        }
    }

    /// <summary>Заменить плейсхолдер в абзаце, слив run'ы в один
    /// (порт _replace_in_paragraph).</summary>
    private static void ReplaceInParagraph(Paragraph paragraph, string placeholder, string replacement)
    {
        var full = paragraph.InnerText;
        if (!full.Contains(placeholder))
            return;
        SetParagraphText(paragraph, full.Replace(placeholder, replacement), bold: false);
    }

    /// <summary>Вставить новый абзац ПОСЛЕ указанного, скопировав стиль
    /// (порт _add_paragraph_after). Возвращает вставленный абзац.</summary>
    private static Paragraph InsertParagraphAfter(Paragraph anchor, string text, bool bold)
    {
        var p = new Paragraph();
        if (anchor.ParagraphProperties != null)
            p.Append((ParagraphProperties)anchor.ParagraphProperties.CloneNode(true));

        var run = new Run();
        var srcRun = anchor.Elements<Run>().FirstOrDefault();
        if (srcRun?.RunProperties != null)
            run.Append((RunProperties)srcRun.RunProperties.CloneNode(true));

        if (bold)
        {
            var rp = run.GetFirstChild<RunProperties>();
            if (rp == null)
            {
                rp = new RunProperties();
                run.PrependChild(rp);
            }
            if (rp.Bold == null)
                rp.Bold = new Bold();
        }

        run.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        p.Append(run);

        anchor.InsertAfterSelf(p);
        return p;
    }
}

/// <summary>Мини-хелпер: выполнить действие, если объект не null.</summary>
internal static class ObjectExtensions
{
    public static void Also<T>(this T? obj, Action<T> action) where T : class
    {
        if (obj is not null)
            action(obj);
    }
}
