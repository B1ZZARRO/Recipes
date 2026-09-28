// ============================================================================
// Models.cs — модели данных (порт prodplan/recipe.py + calculator.py).
//   Recipe      — рецепт одной детали/прибора (состав + операции).
//   Component   — строка состава: метка, имя, количество, единица.
//   Operation   — строка операций: метка, описание, норма времени (мин/шт).
//   Requirement — потребность в одном компоненте (результат расчёта).
// ============================================================================
using System.Xml.Serialization;

namespace Recipes.Models;

#region Component

/// <summary>Один компонент в составе рецепта.</summary>
public class Component
{
    /// <summary>Метка для подстановки в описания операций: s1, s2, …</summary>
    [XmlAttribute("Label")]
    public string Label { get; set; } = "";

    /// <summary>Имя детали/материала. Совпадает с именем другого рецепта → деталь изготавливается у нас.</summary>
    [XmlAttribute("Name")]
    public string Name { get; set; } = "";

    /// <summary>Количество на 1 шт. родительской детали.</summary>
    [XmlAttribute("Qty")]
    public double Qty { get; set; } = 1;

    /// <summary>Единица измерения: шт, м, г, мл, компл…</summary>
    [XmlAttribute("Unit")]
    public string Unit { get; set; } = "шт";

    public Component() { }

    public Component(string label, string name, double qty, string unit = "шт")
    {
        Label = label;
        Name = name;
        Qty = qty;
        Unit = unit;
    }
}

#endregion

#region Operation

/// <summary>Одна операция рецепта.</summary>
public class Operation
{
    private double _timeMinutes;
    private double _nonHumanMinutes;

    /// <summary>Метка операции: o1, o2, …</summary>
    [XmlAttribute("Label")]
    public string Label { get; set; } = "";

    /// <summary>Описание. Может содержать s-метки, которые заменяются именами компонентов.</summary>
    [XmlAttribute("Description")]
    public string Description { get; set; } = "";

    /// <summary>
    /// Норма времени исполнителя, минут на 1 шт. детали.
    /// 0 (или пусто в редакторе) = «нормировка не выполнена».
    /// </summary>
    [XmlAttribute("TimeMinutes")]
    public double TimeMinutes
    {
        get => _timeMinutes;
        set => _timeMinutes = value > 0 ? value : 0;
    }

    /// <summary>Не сериализовать атрибут, если норма не задана.</summary>
    public bool ShouldSerializeTimeMinutes() => _timeMinutes > 0;

    [XmlIgnore]
    public bool HasTime => _timeMinutes > 0;

    /// <summary>
    /// Время задержки на процессы, не требующие человека (застывание,
    /// сушка, полимеризация…), минут — на ОДНО выполнение операции
    /// (на партию), независимо от количества штук в партии. Складывается
    /// в общее время наравне с временем исполнителя.
    /// </summary>
    [XmlAttribute("NonHumanMinutes")]
    public double NonHumanMinutes
    {
        get => _nonHumanMinutes;
        set => _nonHumanMinutes = value > 0 ? value : 0;
    }

    /// <summary>Не сериализовать атрибут, если время без человека не задано.</summary>
    public bool ShouldSerializeNonHumanMinutes() => _nonHumanMinutes > 0;

    [XmlIgnore]
    public bool HasNonHumanTime => _nonHumanMinutes > 0;

    public Operation() { }

    public Operation(string label, string description,
        double? timeMinutes = null, double? nonHumanMinutes = null)
    {
        Label = label;
        Description = description;
        _timeMinutes = timeMinutes ?? 0;
        _nonHumanMinutes = nonHumanMinutes ?? 0;
    }
}

#endregion

#region Recipe

/// <summary>Рецепт одной детали/прибора: состав + операции.</summary>
public class Recipe
{
    /// <summary>Имя детали/прибора (уникальный ключ в XML-файле).</summary>
    [XmlAttribute("Name")]
    public string Name { get; set; } = "";

    private int _runQty = 1;

    /// <summary>
    /// Сколько штук изготавливается за один производственный цикл
    /// (например, пневмосистемы заливаются сразу по 20 шт, хотя на прибор
    /// нужна одна). Наряды этой детали считаются партиями такого размера,
    /// а время без участия человека (застывание и т.п.) — один раз на
    /// партию. 1 — обычная работа по мере надобности.
    /// </summary>
    [XmlAttribute("RunQty")]
    public int RunQty
    {
        get => _runQty;
        set => _runQty = value >= 1 ? value : 1;
    }

    /// <summary>Не писать атрибут, когда партия изготовления обычная (1 шт).</summary>
    public bool ShouldSerializeRunQty() => _runQty > 1;

    [XmlArray("Components")]
    [XmlArrayItem("Component")]
    public List<Component> Components { get; set; } = new();

    [XmlArray("Operations")]
    [XmlArrayItem("Operation")]
    public List<Operation> Operations { get; set; } = new();

    /// <summary>Словарь «метка → имя компонента» (порт recipe.labels из labels.py).</summary>
    [XmlIgnore]
    public Dictionary<string, string> Labels =>
        Components
            .Where(c => !string.IsNullOrWhiteSpace(c.Label))
            .GroupBy(c => c.Label)
            .ToDictionary(g => g.Key, g => g.First().Name);

    public Recipe() { }

    public Recipe(string name)
    {
        Name = name;
    }
}

#endregion

#region RecipeCollection

/// <summary>Корневой элемент XML-файла рецептов.</summary>
[XmlRoot("Recipes")]
public class RecipeCollection
{
    [XmlElement("Recipe")]
    public List<Recipe> Recipes { get; set; } = new();
}

#endregion

#region Requirement

/// <summary>Потребность в одном компоненте (порт calculator.Requirement).</summary>
public class Requirement
{
    public string Name { get; set; } = "";
    public double Qty { get; set; }
    public string Unit { get; set; } = "шт";

    public Requirement() { }

    public Requirement(string name, double qty, string unit)
    {
        Name = name;
        Qty = qty;
        Unit = unit;
    }
}

#endregion

#region UnnormedOperation / CalculationResult

/// <summary>Операция без нормы времени (для «журнала непорядка» в UI).</summary>
public class UnnormedOperation
{
    public string RecipeName { get; set; } = "";
    public string OpLabel { get; set; } = "";
    public string OpDescription { get; set; } = "";
}

/// <summary>Результат расчёта по прибору и количеству.</summary>
public class CalculationResult
{
    /// <summary>Покупное сырьё (компоненты без своего рецепта).</summary>
    public List<Requirement> PurchasedMaterials { get; } = new();

    /// <summary>Промежуточные детали (есть свой рецепт — изготавливаются у нас).</summary>
    public List<Requirement> ManufacturedParts { get; } = new();

    /// <summary>Чистое время операций исполнителя, мин (сумма ceil(норма × количество) по всему дереву).</summary>
    public long PureMinutes { get; set; }

    /// <summary>Надбавка на перекуры/подготовку, мин (ceil(чистое время × 10%)).</summary>
    public long IdleMinutes { get; set; }

    /// <summary>
    /// Время задержек на процессы без участия человека (застывание и т.п.),
    /// мин — суммарно по всем партиям изготовления. Прибавляется к общему
    /// времени наравне с временем исполнителя.
    /// </summary>
    public long NonHumanMinutes { get; set; }

    /// <summary>Общее время производства, мин (исполнитель + перекуры + независимые процессы).</summary>
    public long TotalMinutes => PureMinutes + IdleMinutes + NonHumanMinutes;

    /// <summary>True, если в дереве есть операции без нормы времени.</summary>
    public bool HasUnnormedOperations { get; set; }

    /// <summary>Список ненормированных операций (по одному рецепту — один раз).</summary>
    public List<UnnormedOperation> Unnormed { get; } = new();

    /// <summary>Текст ошибки (циклическая ссылка, несовпадение единиц и т.п.) или null.</summary>
    public string? Error { get; set; }
}

#endregion
