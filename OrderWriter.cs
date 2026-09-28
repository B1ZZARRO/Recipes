// ============================================================================
// OrderWriter.cs — запись папки заказа: сводка материалов 00_материалы.txt,
// наряды .docx по шаблону, журнал непорядка (порт planner.write_task_folder).
// ============================================================================
using System.Text;
using Recipes.Models;

namespace Recipes.Services;

public static class OrderWriter
{
    /// <summary>Журнал непорядка: создаётся, если в нарядах есть операции без нормы.</summary>
    public const string DisorderLogName = "!_НЕПОРЯДОК_ОТНОРМИРУЙТЕ.txt";

    /// <summary>
    /// Создать папку заказа и записать туда все файлы:
    ///   00_материалы.txt                          — сводка сырья на весь заказ
    ///   01_<деталь>_партия_001.docx …             — наряды (post-order)
    ///   !_НЕПОРЯДОК_ОТНОРМИРУЙТЕ.txt              — только если есть ненормированные операции
    /// Возвращает путь к созданной папке.
    /// </summary>
    public static string WriteOrderFolder(
        string outputDir,
        string deviceName,
        double totalQty,
        CalculationResult materials,
        Dictionary<string, Recipe> recipes,
        int batchSize,
        string templatePath)
    {
        Directory.CreateDirectory(outputDir);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        var safeName = ProductionPlanner.SafeFileName(deviceName);
        var baseName = $"{safeName}_{AppFormat.FormatQty(totalQty)}шт_заказ";

        // Ищем свободный порядковый номер заказа: 001, 002, …
        var orderIndex = 1;
        while (Directory.Exists(Path.Combine(
                   outputDir, $"{baseName}_{orderIndex:000}_{timestamp}")))
            orderIndex++;

        var orderDir = Path.Combine(outputDir, $"{baseName}_{orderIndex:000}_{timestamp}");
        Directory.CreateDirectory(orderDir);

        // 1. Сводка материалов на весь заказ.
        File.WriteAllText(
            Path.Combine(orderDir, "00_материалы.txt"),
            FormatMaterialsFile(deviceName, totalQty, materials),
            Encoding.UTF8);

        // 2. Наряды: post-order — сначала глубокие детали, потом финальная сборка.
        var tasks = ProductionPlanner.BuildTaskList(deviceName, totalQty, recipes, batchSize);
        foreach (var task in tasks)
        {
            var fileName =
                $"{task.Number:00}_{ProductionPlanner.SafeFileName(task.DetailName)}_партия_{task.BatchIndex:000}.docx";
            DocxExporter.FillTaskDocx(task, Path.Combine(orderDir, fileName), templatePath, deviceName);
        }

        // 3. Журнал непорядка — если есть наряды с операциями без нормы.
        WriteDisorderLogIfNeeded(tasks, recipes, orderDir);

        return orderDir;
    }

    /// <summary>
    /// Создать папку заказа из СОХРАНЁННОГО заказа (снимка из orders.xml) —
    /// без обращения к текущим рецептам: наряды берутся из order.Tasks.
    /// Имя папки содержит номер заказа: «…_заказ_003_дата_время».
    /// Возвращает путь к созданной папке.
    /// </summary>
    public static string WriteOrderFolderFromOrder(
        string outputDir, Order order, string templatePath)
    {
        Directory.CreateDirectory(outputDir);

        var timestamp = order.CreatedAt.ToString("yyyy-MM-dd_HHmm");
        var safeName = ProductionPlanner.SafeFileName(order.Device);
        var baseName =
            $"{safeName}_{AppFormat.FormatQty(order.Quantity)}шт_заказ_{order.Number:000}";

        // Ищем свободное имя: …_2026-02-27_1435, …_2026-02-27_1435_2, …
        var orderDir = Path.Combine(outputDir, $"{baseName}_{timestamp}");
        var index = 1;
        while (Directory.Exists(orderDir))
            orderDir = Path.Combine(outputDir, $"{baseName}_{timestamp}_{++index}");
        Directory.CreateDirectory(orderDir);

        // 1. Сводка материалов на весь заказ — из снимка позиций.
        File.WriteAllText(
            Path.Combine(orderDir, "00_материалы.txt"),
            FormatMaterialsFile(order),
            Encoding.UTF8);

        // 2. Наряды .docx из снимка (post-order сохранён при формировании).
        foreach (var task in order.Tasks)
        {
            var fileName =
                $"{task.Number:00}_{ProductionPlanner.SafeFileName(task.DetailName)}_партия_{task.BatchIndex:000}.docx";
            DocxExporter.FillTaskDocx(
                task, Path.Combine(orderDir, fileName), templatePath, order.Device);
        }

        // 3. Журнал непорядка — из снимка нарядов.
        WriteDisorderLogFromTasks(order.Tasks, orderDir);

        return orderDir;
    }

    /// <summary>Сводный файл материалов из сохранённого заказа.</summary>
    private static string FormatMaterialsFile(Order order)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Материалы на заказ ===");
        sb.AppendLine($"Заказ №{order.Number} от {order.CreatedAt:dd.MM.yyyy HH:mm}");
        sb.AppendLine($"Прибор: {order.Device}");
        sb.AppendLine($"Количество: {AppFormat.FormatQty(order.Quantity)} шт");
        sb.AppendLine();

        var purchased = order.Items
            .Where(i => i.Kind != OrderItem.ManufacturedKind)
            .ToList();
        var manufactured = order.Items
            .Where(i => i.Kind == OrderItem.ManufacturedKind)
            .ToList();

        if (purchased.Count > 0)
        {
            sb.AppendLine("Покупное сырьё:");
            foreach (var i in purchased)
                sb.AppendLine(
                    $"  • {i.Name} — {AppFormat.FormatQtyUnit(i.Qty, i.Unit)}  {MaterialLine.PurchasedMarker}");
            sb.AppendLine();
        }

        if (manufactured.Count > 0)
        {
            sb.AppendLine("Промежуточные детали (изготавливаются по рецептам):");
            foreach (var i in manufactured)
                sb.AppendLine(
                    $"  • {i.Name} — {AppFormat.FormatQtyUnit(i.Qty, i.Unit)}  {MaterialLine.ManufacturedMarker}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Журнал непорядка из снимка нарядов: детали, у которых в нарядах
    /// есть операции без нормы времени.
    /// </summary>
    private static void WriteDisorderLogFromTasks(
        List<PlannerTask> tasks, string orderDir)
    {
        var badTasks = tasks.Where(t => t.HasUntimedOperations).ToList();
        if (badTasks.Count == 0)
            return; // всё нормировано — файл не создаётся

        var sb = new StringBuilder();
        sb.AppendLine("НЕПОРЯДОК — отнормируйте!");
        sb.AppendLine();
        sb.AppendLine("Следующие детали содержат операции без нормы времени:");
        sb.AppendLine();

        foreach (var group in badTasks.GroupBy(t => t.DetailName))
        {
            sb.AppendLine($"  • Деталь «{group.Key}»");
            foreach (var description in group
                         .SelectMany(t => t.Operations)
                         .Where(o => o.IsUntimed)
                         .Select(o => o.Description)
                         .Distinct())
                sb.AppendLine($"      {description}");
            sb.AppendLine();
        }

        sb.AppendLine("Откройте редактор рецептов и проставьте нормы времени этим операциям,");
        sb.AppendLine("затем сформируйте заказ заново.");
        sb.AppendLine();

        File.WriteAllText(Path.Combine(orderDir, DisorderLogName), sb.ToString(), Encoding.UTF8);
    }

    /// <summary>Сводный файл материалов (порт _format_materials_file).</summary>
    private static string FormatMaterialsFile(
        string deviceName, double totalQty, CalculationResult materials)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Материалы на заказ ===");
        sb.AppendLine($"Прибор: {deviceName}");
        sb.AppendLine($"Количество: {AppFormat.FormatQty(totalQty)} шт");
        sb.AppendLine();

        if (materials.PurchasedMaterials.Count > 0)
        {
            sb.AppendLine("Покупное сырьё:");
            foreach (var r in materials.PurchasedMaterials)
                sb.AppendLine($"  • {r.Name} — {AppFormat.FormatQtyUnit(r.Qty, r.Unit)}  {MaterialLine.PurchasedMarker}");
            sb.AppendLine();
        }

        if (materials.ManufacturedParts.Count > 0)
        {
            sb.AppendLine("Промежуточные детали (изготавливаются по рецептам):");
            foreach (var r in materials.ManufacturedParts)
                sb.AppendLine($"  • {r.Name} — {AppFormat.FormatQtyUnit(r.Qty, r.Unit)}  {MaterialLine.ManufacturedMarker}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Журнал непорядка: список РЕЦЕПТОВ с операциями без нормы
    /// (порт _write_disorder_log_if_needed / _format_disorder_log).</summary>
    private static void WriteDisorderLogIfNeeded(
        List<PlannerTask> tasks,
        Dictionary<string, Recipe> recipes,
        string orderDir)
    {
        var badRecipeNames = tasks
            .Where(t => t.HasUntimedOperations)
            .Select(t => t.DetailName)
            .Distinct()
            .ToList();

        if (badRecipeNames.Count == 0)
            return; // всё нормировано — файл не создаётся

        var sb = new StringBuilder();
        sb.AppendLine("НЕПОРЯДОК — отнормируйте!");
        sb.AppendLine();
        sb.AppendLine("Следующие рецепты содержат операции без нормы времени:");
        sb.AppendLine();

        foreach (var name in badRecipeNames)
        {
            sb.AppendLine($"  • Рецепт «{name}»");
            if (recipes.TryGetValue(name, out var recipe))
            {
                foreach (var op in recipe.Operations.Where(o => !o.HasTime))
                    sb.AppendLine(string.IsNullOrEmpty(op.Label)
                        ? $"      {op.Description}"
                        : $"      {op.Label}. {op.Description}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Откройте редактор рецептов и проставьте нормы времени этим операциям,");
        sb.AppendLine("затем сформируйте заказ-наряды заново.");
        sb.AppendLine();

        File.WriteAllText(Path.Combine(orderDir, DisorderLogName), sb.ToString(), Encoding.UTF8);
    }
}
