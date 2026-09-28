// ============================================================================
// Labels.cs — подстановка s-меток в описания операций (порт labels.py).
//   "Установить s3 в s1" → "Установить печатную плату КНЯ85 в корпус А"
// ============================================================================
using System.Text.RegularExpressions;

namespace Recipes.Services;

public static class Labels
{
    /// <summary>
    /// Заменяет метки (s1, s2, …) в описании операции на имена компонентов.
    /// Более длинные метки заменяются первыми, чтобы s10 не портился при
    /// замене s1; границы слов \b не дают заменить метку внутри другого слова.
    /// </summary>
    public static string Substitute(string description, Dictionary<string, string> labelMap)
    {
        if (string.IsNullOrEmpty(description) || labelMap.Count == 0)
            return description;

        foreach (var pair in labelMap.OrderByDescending(p => p.Key.Length))
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            var pattern = @"\b" + Regex.Escape(pair.Key) + @"\b";
            description = Regex.Replace(description, pattern, pair.Value);
        }

        return description;
    }
}
