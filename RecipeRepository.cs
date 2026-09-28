// ============================================================================
// RecipeRepository.cs — загрузка/сохранение рецептов в XML
// (порт prodplan/parser.py: load_recipes — теперь один XML вместо папки .txt).
// ============================================================================
using System.Text;
using System.Xml.Serialization;
using Recipes.Models;

namespace Recipes.Services;

public class RecipeRepository
{
    private readonly string _filePath;

    public RecipeRepository(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>Путь к XML-файлу рецептов.</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Загрузить все рецепты из XML. Файл не существует → пустой словарь.
    /// Битый файл → исключение (вызывающий код предложит восстановить пример).
    /// </summary>
    public Dictionary<string, Recipe> LoadAll() => LoadFrom(_filePath);

    /// <summary>Загрузить рецепты из произвольного XML-файла (для импорта).</summary>
    public static Dictionary<string, Recipe> LoadFrom(string filePath)
    {
        var result = new Dictionary<string, Recipe>();

        if (!File.Exists(filePath))
            return result;

        var serializer = new XmlSerializer(typeof(RecipeCollection));
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var collection = (RecipeCollection?)serializer.Deserialize(stream);
        if (collection is null)
            return result;

        foreach (var recipe in collection.Recipes)
        {
            if (!string.IsNullOrWhiteSpace(recipe.Name))
                result[recipe.Name] = recipe;
        }

        return result;
    }

    /// <summary>Сохранить все рецепты в рабочий XML (упорядоченно по имени).</summary>
    public void SaveAll(Dictionary<string, Recipe> recipes) => SaveTo(_filePath, recipes);

    /// <summary>Сохранить рецепты в произвольный XML-файл (для экспорта).</summary>
    public static void SaveTo(string filePath, Dictionary<string, Recipe> recipes)
    {
        var collection = new RecipeCollection
        {
            Recipes = recipes.Values
                .OrderBy(r => r.Name, StringComparer.CurrentCulture)
                .ToList()
        };

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var serializer = new XmlSerializer(typeof(RecipeCollection));
        var ns = new XmlSerializerNamespaces();
        ns.Add("", ""); // без xmlns-заголовков

        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        serializer.Serialize(writer, collection, ns);
    }

    /// <summary>
    /// Корневые продукты — рецепты, на которые НИКТО не ссылается как на
    /// компонент (порт calculator.find_root_products). Это и есть «приборы».
    /// </summary>
    public static List<string> GetRootProducts(Dictionary<string, Recipe> recipes)
    {
        var referenced = CollectReferencedNames(recipes);
        return recipes.Keys
            .Where(name => !referenced.Contains(name))
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// Промежуточные детали — рецепты, на которые ссылается хотя бы один
    /// другой рецепт (порт calculator.find_intermediate_details).
    /// </summary>
    public static List<string> GetIntermediateDetails(Dictionary<string, Recipe> recipes)
    {
        var referenced = CollectReferencedNames(recipes);
        return recipes.Keys
            .Where(referenced.Contains)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();
    }

    private static HashSet<string> CollectReferencedNames(Dictionary<string, Recipe> recipes)
    {
        var referenced = new HashSet<string>();
        foreach (var recipe in recipes.Values)
            foreach (var comp in recipe.Components)
                if (!string.IsNullOrWhiteSpace(comp.Name))
                    referenced.Add(comp.Name);
        return referenced;
    }

    /// <summary>
    /// Имена рецептов, у которых в составе есть компонент с именем
    /// componentName (кто ссылается на рецепт; используется при удалении
    /// и переименовании, чтобы напомнить об обновлении ссылок).
    /// </summary>
    public static List<string> FindReferencing(
        Dictionary<string, Recipe> recipes, string componentName)
        => recipes.Values
            .Where(r => r.Components.Any(c =>
                string.Equals(c.Name, componentName, StringComparison.Ordinal)))
            .Select(r => r.Name)
            .OrderBy(n => n, StringComparer.CurrentCulture)
            .ToList();

    /// <summary>
    /// Достижимо ли имя target из имени from по ссылкам состава
    /// (from → компонент → компонент…). Используется для предупреждения
    /// о циклических ссылках во вложенных рецептах: если целевой рецепт
    /// достижим из того, на который ссылаемся, — состав зациклен.
    /// from == target → true (рецепт ссылается сам на себя).
    /// </summary>
    public static bool Reaches(
        Dictionary<string, Recipe> recipes, string from, string target)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(from);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
                continue;

            if (string.Equals(current, target, StringComparison.Ordinal))
                return true;

            if (recipes.TryGetValue(current, out var recipe))
                foreach (var comp in recipe.Components)
                    if (!string.IsNullOrWhiteSpace(comp.Name))
                        stack.Push(comp.Name);
        }

        return false;
    }
}
