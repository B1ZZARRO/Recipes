// ============================================================================
// BomCalculator.cs — расчёт потребностей и времени по дереву рецептов.
//   Порт:
//     calculator.py  → calc_requirements, find_root_products
//     planner.py     → _collect_all_subcomponents
//     docx_tasks.py  → _compute_expected_total_time (+10% на перекуры, ceil)
//   Время складывается из:
//     • времени исполнителя — ceil(норма × количество) по каждой операции;
//       у рецептов с партией изготовления (RunQty > 1) операции выполняются
//       партиями — на каждую партию свой ceil;
//     • времени задержек без участия человека (застывание и т.п.) — один
//       раз на каждую партию изготовления (ceil до целой минуты);
//     • надбавки +10% на перекуры (только к времени исполнителя).
// ============================================================================
using Recipes.Models;

namespace Recipes.Services;

public class BomCalculator
{
    /// <summary>Надбавка на перекуры/туалет/подготовку: +10% к чистому времени операций.</summary>
    public const double IdleFactor = 0.10;

    private const string DefaultUnit = "шт";

    private readonly Dictionary<string, Recipe> _recipes;

    public BomCalculator(Dictionary<string, Recipe> recipes)
    {
        _recipes = recipes ?? new Dictionary<string, Recipe>();
    }

    /// <summary>
    /// Рассчитать все требуемые компоненты и общее время производства.
    /// Ошибка (цикл, несовпадение единиц, отсутствующий рецепт) возвращается
    /// в result.Error — UI показывает её пользователю.
    /// </summary>
    public CalculationResult Calculate(string deviceName, double quantity)
    {
        var result = new CalculationResult();

        try
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                result.Error = "Не выбран прибор.";
                return result;
            }

            if (quantity <= 0)
            {
                result.Error = "Количество должно быть больше нуля.";
                return result;
            }

            if (!_recipes.ContainsKey(deviceName))
            {
                result.Error = $"Рецепт «{deviceName}» не найден.";
                return result;
            }

            // 1. Собрать ВСЕ компоненты дерева: и покупное сырьё, и промежуточные детали.
            var all = new Dictionary<string, Requirement>();
            CollectAllComponents(deviceName, quantity, all, new HashSet<string>());

            foreach (var req in all.Values.OrderBy(r => r.Name, StringComparer.CurrentCulture))
            {
                if (_recipes.ContainsKey(req.Name))
                    result.ManufacturedParts.Add(req);
                else
                    result.PurchasedMaterials.Add(req);
            }

            // 2. Общее время: обход дерева, по каждой операции ceil(норма × количество).
            var recordedUnnormed = new HashSet<string>();
            CollectTotalTime(deviceName, quantity, result, new HashSet<string>(), recordedUnnormed);

            // 3. Надбавка +10% на перекуры (округление вверх).
            result.IdleMinutes = (long)Math.Ceiling(result.PureMinutes * IdleFactor);
            result.HasUnnormedOperations = result.Unnormed.Count > 0;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    // ------------------------------------------------------------------
    // Сбор всех компонентов (порт planner._collect_all_subcomponents).
    // Промежуточные детали НЕ разворачиваются до сырья, а учитываются
    // отдельно (с единицей "шт"); сырьё учитывается с единицей из состава.
    // ------------------------------------------------------------------

    private void CollectAllComponents(
        string itemName,
        double quantity,
        Dictionary<string, Requirement> total,
        HashSet<string> visited)
    {
        if (quantity <= 0)
            return;

        if (visited.Contains(itemName))
            throw new InvalidOperationException(
                "Циклическая ссылка в составе: " + FormatChain(visited, itemName));

        if (!_recipes.TryGetValue(itemName, out var recipe))
        {
            // Базовый компонент (сырьё) — единица по умолчанию.
            AddToTotal(total, itemName, quantity, DefaultUnit);
            return;
        }

        var newPath = new HashSet<string>(visited) { itemName };

        foreach (var comp in recipe.Components)
        {
            var subQty = quantity * comp.Qty;
            var isManufactured = _recipes.ContainsKey(comp.Name);

            // У промежуточной детали единица "шт", у сырья — из состава.
            AddToTotal(total, comp.Name, subQty, isManufactured ? DefaultUnit : comp.Unit);

            if (isManufactured)
                CollectAllComponents(comp.Name, subQty, total, newPath);
        }
    }

    private static void AddToTotal(
        Dictionary<string, Requirement> total,
        string name, double qty, string unit)
    {
        if (total.TryGetValue(name, out var existing))
        {
            if (!string.Equals(existing.Unit, unit, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Несовпадение единиц для «{name}»: {existing.Unit} vs {unit}");
            existing.Qty += qty;
        }
        else
        {
            total[name] = new Requirement(name, qty, unit);
        }
    }

    // ------------------------------------------------------------------
    // Общее время (порт docx_tasks._compute_expected_total_time, но на всё
    // дерево: каждая операция каждого рецепта × количество этой детали,
    // у рецептов с партией изготовления — по партиям; плюс время задержек
    // без участия человека — по одному на каждую партию изготовления).
    // ------------------------------------------------------------------

    private void CollectTotalTime(
        string itemName,
        double quantity,
        CalculationResult result,
        HashSet<string> visited,
        HashSet<string> recordedUnnormed)
    {
        if (quantity <= 0)
            return;

        if (visited.Contains(itemName))
            throw new InvalidOperationException(
                "Циклическая ссылка при расчёте времени: " + FormatChain(visited, itemName));

        if (!_recipes.TryGetValue(itemName, out var recipe))
            return; // сырьё — операций не имеет

        var newPath = new HashSet<string>(visited) { itemName };

        // 1. Сначала — операции всех составных частей (post-order).
        foreach (var comp in recipe.Components)
            if (_recipes.ContainsKey(comp.Name))
                CollectTotalTime(comp.Name, quantity * comp.Qty,
                    result, newPath, recordedUnnormed);

        var labelMap = recipe.Labels;

        // 2. Операции самой детали, округление ВВЕРХ до целой минуты.
        var runQty = Math.Max(1, recipe.RunQty);
        if (runQty > 1)
        {
            // Деталь изготавливается партиями (например, заливка по 20 шт):
            // каждая партия — отдельное выполнение операций, поэтому
            // исполнительное время считается по каждой партии, а задержка
            // без человека возникает один раз на партию (застывают все
            // формы одновременно).
            var remaining = quantity;
            while (remaining > 1e-9)
            {
                var chunk = Math.Min((double)runQty, remaining);
                remaining -= chunk;

                foreach (var op in recipe.Operations)
                {
                    if (op.HasTime)
                        result.PureMinutes += (long)Math.Ceiling(op.TimeMinutes * chunk);
                    if (op.HasNonHumanTime)
                        result.NonHumanMinutes += (long)Math.Ceiling(op.NonHumanMinutes);
                }
            }
        }
        else
        {
            // Обычный рецепт (партия = 1 шт): исполнительное время — на весь
            // объём, как раньше (ceil(норма × количество)); задержка без
            // человека возникает на каждую изготовленную штуку.
            foreach (var op in recipe.Operations)
                if (op.HasTime)
                    result.PureMinutes += (long)Math.Ceiling(op.TimeMinutes * quantity);

            var runCount = (long)Math.Ceiling(quantity - 1e-9);
            foreach (var op in recipe.Operations)
                if (op.HasNonHumanTime)
                    result.NonHumanMinutes += runCount * (long)Math.Ceiling(op.NonHumanMinutes);
        }

        // 3. Операции без нормы — в «журнал непорядка» (рецепт — один раз).
        if (recordedUnnormed.Add(itemName))
        {
            foreach (var op in recipe.Operations.Where(o => !o.HasTime))
            {
                result.Unnormed.Add(new UnnormedOperation
                {
                    RecipeName = itemName,
                    OpLabel = op.Label,
                    OpDescription = Labels.Substitute(op.Description, labelMap),
                });
            }
        }
    }

    private static string FormatChain(IEnumerable<string> visited, string last)
        => string.Join(" → ", visited.Append(last));
}
