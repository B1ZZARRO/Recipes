// ============================================================================
// ProductionPlanner.cs — порт planner.py: разбиение заказа на партии,
// post-order обход дерева состава, формирование заданий (нарядов).
// Наряды детали считаются партиями её изготовления (Recipe.RunQty —
// сколько штук делается за раз, например заливка 20 пневмосистем),
// а если партия изготовления не указана — по размеру партии заказа.
// ============================================================================
using System.Text.RegularExpressions;
using Recipes.Models;

namespace Recipes.Services;

/// <summary>Материал в задании: покупное сырьё или наша промежуточная деталь.</summary>
public class MaterialLine
{
    public const string PurchasedMarker = "{покупное}";
    public const string ManufacturedMarker = "{изготовленное}";

    public string Name { get; set; } = "";
    public double Qty { get; set; }
    public string Unit { get; set; } = "шт";
    public bool IsPurchased { get; set; }
    public bool IsManufactured { get; set; }

    /// <summary>"• винт М4 — (8)[шт]  {покупное}" (порт _format_material_line).</summary>
    public string Format()
    {
        var marker = IsPurchased ? $"  {PurchasedMarker}"
                   : IsManufactured ? $"  {ManufacturedMarker}"
                   : "";
        return $"• {Name} — {AppFormat.FormatQtyUnit(Qty, Unit)}{marker}";
    }
}

/// <summary>Операция в задании: описание с подставленными метками, время на партию.</summary>
public class OperationLine
{
    public string Description { get; set; } = "";
    public double? TimePerUnit { get; set; }   // минут на 1 шт; null — нормировка не выполнена
    public double BatchQty { get; set; }
    public double? TotalTime { get; set; }     // TimePerUnit × BatchQty (или null)

    /// <summary>
    /// Задержка без участия человека (застывание и т.п.), мин — один раз на
    /// выполнение операции (на партию), независимо от количества штук.
    /// null — у операции нет такого времени.
    /// </summary>
    public double? NonHumanTime { get; set; }

    public bool IsUntimed => TimePerUnit is null || TimePerUnit <= 0;

    /// <summary>"• Установить… — время: 10 мин × 2 шт = 20 мин" (порт _format_operation_line).</summary>
    public string Format()
    {
        var text = IsUntimed
            ? $"• {Description} — время: [нормировка не выполнена]"
            : $"• {Description} — время: {AppFormat.FormatQty(TimePerUnit!.Value)} мин × " +
              $"{AppFormat.FormatQty(BatchQty)} шт = {AppFormat.FormatQty(TotalTime!.Value)} мин";

        if (NonHumanTime is { } delay)
            text += $"; без участия человека: {AppFormat.FormatQty(delay)} мин";

        return text;
    }
}

/// <summary>Одно задание: одна партия одной детали (порт planner.Task).</summary>
public class PlannerTask
{
    /// <summary>Надбавка на перекуры/подготовку: +10% к времени исполнителя.</summary>
    public const double IdleFactor = 0.10;

    public int Number { get; set; }          // сквозной номер 01, 02, …
    public string DetailName { get; set; } = "";
    public int BatchIndex { get; set; }      // 1-based
    public int BatchCount { get; set; }
    public double BatchQty { get; set; }

    // Сеттеры нужны XmlSerializer при сохранении снимка заказа в orders.xml.
    public List<MaterialLine> Materials { get; set; } = new();
    public List<OperationLine> Operations { get; set; } = new();

    public bool HasUntimedOperations => Operations.Any(o => o.IsUntimed);

    /// <summary>Чистое время исполнителя, мин (сумма ceil по операциям партии).</summary>
    public long ExecutorPureMinutes =>
        Operations.Where(o => !o.IsUntimed && o.TotalTime is not null)
            .Sum(o => (long)Math.Ceiling(o.TotalTime!.Value));

    /// <summary>Надбавка на перекуры, мин (ceil(чистое время × 10%)).</summary>
    public long IdleMinutes => (long)Math.Ceiling(ExecutorPureMinutes * IdleFactor);

    /// <summary>Время задержек без участия человека, мин (одна задержка на операцию).</summary>
    public long NonHumanMinutes =>
        Operations.Where(o => o.NonHumanTime is not null)
            .Sum(o => (long)Math.Ceiling(o.NonHumanTime!.Value));

    /// <summary>Общее время партии: исполнитель + перекуры + независимые процессы.</summary>
    public long TotalMinutes => ExecutorPureMinutes + IdleMinutes + NonHumanMinutes;
}

public static class ProductionPlanner
{
    /// <summary>
    /// Обойти дерево состава post-order и собрать плоский список заданий
    /// (порт planner.build_task_list). Сначала — самые глубокие детали,
    /// потом сборка родителя. Сырьё заданий не порождает.
    /// </summary>
    public static List<PlannerTask> BuildTaskList(
        string itemName, double quantity,
        Dictionary<string, Recipe> recipes, int batchSize)
    {
        var counter = new int[1];
        return BuildTaskList(itemName, quantity, recipes, batchSize,
            counter, new HashSet<string>());
    }

    private static List<PlannerTask> BuildTaskList(
        string itemName, double quantity,
        Dictionary<string, Recipe> recipes, int batchSize,
        int[] counter, HashSet<string> visited)
    {
        if (visited.Contains(itemName))
            throw new InvalidOperationException(
                "Циклическая ссылка при сборе заданий: " +
                string.Join(" → ", visited.Append(itemName)));

        if (!recipes.TryGetValue(itemName, out var recipe))
            return new List<PlannerTask>();   // сырьё — задание не создаётся

        if (batchSize <= 0)
            batchSize = 1;

        // Партия изготовления из рецепта перекрывает размер партии заказа:
        // например, пневмосистемы заливаются сразу по 20 шт — наряды этой
        // детали считаются партиями по 20 шт, независимо от партии заказа.
        var effBatch = recipe.RunQty > 1 ? recipe.RunQty : batchSize;

        var tasks = new List<PlannerTask>();
        var newPath = new HashSet<string>(visited) { itemName };

        // 1. Сначала рекурсивно — все составные части.
        foreach (var comp in recipe.Components)
        {
            if (!recipes.ContainsKey(comp.Name))
                continue;
            tasks.AddRange(BuildTaskList(comp.Name, quantity * comp.Qty,
                recipes, batchSize, counter, newPath));
        }

        // 2. Разбиваем текущую деталь на партии (ceil(quantity / effBatch)).
        var totalBatches = quantity >= 1
            ? Math.Max(1, ((int)Math.Truncate(quantity) + effBatch - 1) / effBatch)
            : 1;

        var labelMap = recipe.Labels;
        var remaining = quantity;
        var batchIndex = 0;

        while (remaining > 0)
        {
            batchIndex++;
            var currentBatchQty = Math.Min((double)effBatch, remaining);
            remaining -= currentBatchQty;
            counter[0]++;

            var task = new PlannerTask
            {
                Number = counter[0],
                DetailName = itemName,
                BatchIndex = batchIndex,
                BatchCount = totalBatches,
                BatchQty = currentBatchQty,
            };

            // Материалы этой партии — прямые компоненты рецепта.
            foreach (var comp in recipe.Components)
            {
                var hasRecipe = recipes.ContainsKey(comp.Name);
                task.Materials.Add(new MaterialLine
                {
                    Name = comp.Name,
                    Qty = currentBatchQty * comp.Qty,
                    Unit = comp.Unit,
                    IsPurchased = !hasRecipe,
                    IsManufactured = hasRecipe,
                });
            }

            // Операции: подстановка s-меток, норма × количество в партии,
            // плюс время задержки без участия человека (один раз на партию).
            foreach (var op in recipe.Operations)
            {
                var tpu = op.HasTime ? op.TimeMinutes : (double?)null;
                task.Operations.Add(new OperationLine
                {
                    Description = Labels.Substitute(op.Description, labelMap),
                    TimePerUnit = tpu,
                    BatchQty = currentBatchQty,
                    TotalTime = tpu * currentBatchQty,
                    NonHumanTime = op.HasNonHumanTime ? op.NonHumanMinutes : (double?)null,
                });
            }

            tasks.Add(task);
        }

        return tasks;
    }

    /// <summary>Безопасное имя файла: пробелы → '_', небезопасные символы удалены, кириллица сохранена.</summary>
    public static string SafeFileName(string name)
    {
        var cleaned = Regex.Replace(name, @"[^\w\s-]", "");
        return cleaned.Trim().Replace(' ', '_');
    }
}
