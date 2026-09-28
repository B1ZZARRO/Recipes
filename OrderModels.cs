// ============================================================================
// OrderModels.cs — модель заказа. При каждом нажатии «Сформировать» заказ
// сохраняется в отдельный XML-файл (orders.xml) со всеми заказами.
// Заказ — снимок расчёта на момент формирования: прибор, количество,
// позиции (сырьё/детали), наряды по партиям, время, ненормированные операции.
// Накладные ведутся по позициям заказа (в карточке заказа, в верхней
// таблице): PDF — пока заглушка, плюс сколько единиц позиции покрывает.
// ============================================================================
using System.Xml.Serialization;
using Recipes.Services;

namespace Recipes.Models;

/// <summary>Одна позиция заказа: покупное сырьё или промежуточная деталь.</summary>
public class OrderItem
{
    public const string PurchaseKind = "Purchase";
    public const string ManufacturedKind = "Manufactured";

    /// <summary>Purchase — покупное, Manufactured — изготавливается у нас.</summary>
    [XmlAttribute("Kind")]
    public string Kind { get; set; } = PurchaseKind;

    [XmlAttribute("Name")]
    public string Name { get; set; } = "";

    [XmlAttribute("Qty")]
    public double Qty { get; set; }

    [XmlAttribute("Unit")]
    public string Unit { get; set; } = "шт";

    /// <summary>«Покупное» / «Изготавливается» — для таблиц в UI.</summary>
    [XmlIgnore]
    public string KindText => Kind == ManufacturedKind ? "Изготавливается" : "Покупное";

    /// <summary>
    /// Накладные по позиции: PDF (пока заглушка) + сколько единиц покрывает
    /// каждая. Первая накладная редактируется прямо в строке позиции,
    /// остальные — в строках ниже, которые появляются, пока суммарное
    /// покрытие меньше количества позиции.
    /// </summary>
    [XmlArray("Invoices")]
    [XmlArrayItem("Invoice")]
    public List<OrderInvoice> Invoices { get; set; } = new();

    /// <summary>Сумма единиц по всем накладным позиции.</summary>
    [XmlIgnore]
    public double InvoicedUnits => Invoices.Sum(i => i.Units);

    /// <summary>Накладные покрывают позицию (сумма единиц &gt;= количества).</summary>
    [XmlIgnore]
    public bool IsFullyInvoiced =>
        Invoices.Count > 0 && Qty > 0 && InvoicedUnits >= Qty - 1e-9;

    public OrderItem() { }

    public OrderItem(string kind, string name, double qty, string unit)
    {
        Kind = kind;
        Name = name;
        Qty = qty;
        Unit = unit;
    }
}

/// <summary>
/// Накладная, прикреплённая к позиции заказа. PDF пока заглушка (файл не
/// сохраняется — при переходе с XML на отдельную БД вложения будут
/// храниться в базе).
/// </summary>
public class OrderInvoice
{
    [XmlAttribute("Id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Имя PDF-файла накладной. Пусто — файл ещё не прикреплён.</summary>
    [XmlAttribute("File")]
    public string FileName { get; set; } = "";

    /// <summary>Сколько единиц продукции покрывает накладная (0 — не указано).</summary>
    [XmlAttribute("Units")]
    public double Units { get; set; }
}

/// <summary>
/// Заказ — снимок расчёта «Сформировать». Хранится в orders.xml рядом
/// с recipes.xml; все заказы накапливаются в одном файле.
/// </summary>
public class Order
{
    /// <summary>Неизменный идентификатор (стабилен при удалении других заказов).</summary>
    [XmlAttribute("Id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Сквозной номер заказа: 1, 2, 3… (не переиспользуется).</summary>
    [XmlAttribute("Number")]
    public int Number { get; set; }

    /// <summary>Момент формирования заказа.</summary>
    [XmlAttribute("CreatedAt")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Имя прибора (корневого рецепта).</summary>
    [XmlAttribute("Device")]
    public string Device { get; set; } = "";

    /// <summary>Количество приборов в заказе.</summary>
    [XmlAttribute("Qty")]
    public double Quantity { get; set; }

    /// <summary>Размер партии нарядов (сколько изделий в одном наряде).</summary>
    [XmlAttribute("BatchSize")]
    public int BatchSize { get; set; } = 1;

    /// <summary>Чистое время операций, мин (ceil по каждой операции дерева).</summary>
    [XmlAttribute("PureMinutes")]
    public long PureMinutes { get; set; }

    /// <summary>Надбавка, мин (молча прибавляется к общему времени).</summary>
    [XmlAttribute("IdleMinutes")]
    public long IdleMinutes { get; set; }

    /// <summary>
    /// Время задержек на процессы без участия человека (застывание и т.п.),
    /// мин — уже входит в общее время.
    /// </summary>
    [XmlAttribute("NonHumanMinutes")]
    public long NonHumanMinutes { get; set; }

    /// <summary>Общее время производства, мин (исполнитель + надбавка + независимые процессы).</summary>
    [XmlIgnore]
    public long TotalMinutes => PureMinutes + IdleMinutes + NonHumanMinutes;

    /// <summary>Позиции заказа: покупное сырьё + промежуточные детали.</summary>
    [XmlArray("Items")]
    [XmlArrayItem("Item")]
    public List<OrderItem> Items { get; set; } = new();

    /// <summary>Снимок нарядов (партии деталей) на момент формирования.</summary>
    [XmlArray("Tasks")]
    [XmlArrayItem("Task")]
    public List<PlannerTask> Tasks { get; set; } = new();

    /// <summary>
    /// Ненормированные операции (текстовые строки «Рецепт: o2. Описание»)
    /// для предупреждения в окне заказа.
    /// </summary>
    [XmlArray("Unnormed")]
    [XmlArrayItem("Op")]
    public List<string> UnnormedOperations { get; set; } = new();

    /// <summary>Есть ли в заказе операции без нормы времени.</summary>
    [XmlIgnore]
    public bool HasUnnormedOperations =>
        UnnormedOperations.Count > 0 || Tasks.Any(t => t.HasUntimedOperations);
}
