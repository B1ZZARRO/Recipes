// ============================================================================
// OrdersRepository.cs — хранилище заказов: отдельный XML-файл (orders.xml),
// куда при каждом нажатии «Сформировать» дописывается новый заказ.
// Формат и принцип — те же, что у RecipeRepository (XmlSerializer, UTF-8).
// ============================================================================
using System.Text;
using System.Xml.Serialization;
using Recipes.Models;

namespace Recipes.Services;

public class OrdersRepository
{
    private readonly string _filePath;

    public OrdersRepository(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>Путь к XML-файлу заказов.</summary>
    public string FilePath => _filePath;

    /// <summary>Корневой элемент XML-файла заказов.</summary>
    [XmlRoot("Orders")]
    public class OrderCollection
    {
        [XmlElement("Order")]
        public List<Order> Orders { get; set; } = new();
    }

    /// <summary>
    /// Загрузить все заказы (по возрастанию номеров).
    /// Файл не существует → пустой список.
    /// </summary>
    public List<Order> LoadAll()
    {
        if (!File.Exists(_filePath))
            return new List<Order>();

        var serializer = new XmlSerializer(typeof(OrderCollection));
        using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read);
        var collection = (OrderCollection?)serializer.Deserialize(stream);

        return collection?.Orders
                   .OrderBy(o => o.Number)
                   .ToList()
               ?? new List<Order>();
    }

    /// <summary>Сохранить все заказы в XML (упорядоченно по номеру).</summary>
    public void SaveAll(IEnumerable<Order> orders)
    {
        var collection = new OrderCollection
        {
            Orders = orders.OrderBy(o => o.Number).ToList()
        };

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var serializer = new XmlSerializer(typeof(OrderCollection));
        var ns = new XmlSerializerNamespaces();
        ns.Add("", ""); // без xmlns-заголовков

        using var writer = new StreamWriter(_filePath, false, Encoding.UTF8);
        serializer.Serialize(writer, collection, ns);
    }

    /// <summary>
    /// Добавить заказ в файл: назначить очередной сквозной номер
    /// (максимум по файлу + 1 — номера не переиспользуются) и сохранить.
    /// Возвращает тот же заказ с заполненными Id/Number.
    /// </summary>
    public Order Add(Order order)
    {
        var all = LoadAll();

        if (order.Number <= 0)
            order.Number = all.Count == 0 ? 1 : all.Max(o => o.Number) + 1;

        if (string.IsNullOrEmpty(order.Id))
            order.Id = Guid.NewGuid().ToString("N");

        if (order.CreatedAt == default)
            order.CreatedAt = DateTime.Now;

        all.Add(order);
        SaveAll(all);
        return order;
    }

    /// <summary>Удалить заказ по идентификатору. True — если удалён.</summary>
    public bool Delete(string id)
    {
        var all = LoadAll();
        var removed = all.RemoveAll(o => o.Id == id);
        if (removed > 0)
            SaveAll(all);
        return removed > 0;
    }
}

/// <summary>
/// Фабрика заказа: превращает результат расчёта «Сформировать» в снимок
/// заказа (позиции + наряды по партиям + ненормированные операции).
/// </summary>
public static class OrderFactory
{
    public static Order FromCalculation(
        string device,
        double quantity,
        int batchSize,
        CalculationResult result,
        Dictionary<string, Recipe> recipes)
    {
        var order = new Order
        {
            Device = device,
            Quantity = quantity,
            BatchSize = Math.Max(1, batchSize),
            PureMinutes = result.PureMinutes,
            IdleMinutes = result.IdleMinutes,
            NonHumanMinutes = result.NonHumanMinutes,
        };

        foreach (var r in result.PurchasedMaterials)
            order.Items.Add(new OrderItem(OrderItem.PurchaseKind, r.Name, r.Qty, r.Unit));

        foreach (var r in result.ManufacturedParts)
            order.Items.Add(new OrderItem(OrderItem.ManufacturedKind, r.Name, r.Qty, r.Unit));

        // Снимок нарядов: те же задания, из которых потом печатаются .docx.
        order.Tasks = ProductionPlanner.BuildTaskList(
            device, quantity, recipes, order.BatchSize);

        foreach (var u in result.Unnormed)
        {
            var op = string.IsNullOrEmpty(u.OpLabel)
                ? u.OpDescription
                : $"{u.OpLabel}. {u.OpDescription}";
            order.UnnormedOperations.Add($"{u.RecipeName}: {op}");
        }

        return order;
    }
}
