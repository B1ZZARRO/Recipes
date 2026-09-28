// ============================================================================
// DefaultRecipes.cs — примеры рецептов, автоматически записываются в
// recipes.xml при первом запуске. Включают:
//   • прибор КР-05 (пример из проекта prodplan);
//   • регистратор РП-1 — порядок работ по схема.pdf (SMD/THT монтаж,
//     визуальный контроль, проверка пайки, прошивка CPU1/CPU2, сборка и
//     проверка блока пневматического, сборка/калибровка/проверка
//     регистратора, ПСИ, упаковка).
// Блок пневматический — пример партии изготовления: заливка сразу
// по 20 шт (RunQty), хотя на один регистратор нужен один блок.
// ============================================================================
namespace Recipes;

public static class DefaultRecipes
{
    public const string Xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Recipes>
  <Recipe Name=""прибор КР-05"">
    <Components>
      <Component Label=""s1"" Name=""корпус А"" Qty=""1"" Unit=""шт"" />
      <Component Label=""s2"" Name=""корпус Б"" Qty=""1"" Unit=""шт"" />
      <Component Label=""s3"" Name=""печатная плата КНЯ85 в сборе"" Qty=""1"" Unit=""шт"" />
    </Components>
    <Operations>
      <Operation Label=""o1"" Description=""Установить s3 в s1"" TimeMinutes=""15"" />
      <Operation Label=""o2"" Description=""Соединить s2 с s1"" TimeMinutes=""20"" />
      <Operation Label=""o3"" Description=""Проверить работоспособность"" TimeMinutes=""10"" />
    </Operations>
  </Recipe>

  <Recipe Name=""блок пневматический"" RunQty=""20"">
    <Components>
      <Component Label=""s1"" Name=""датчик давления"" Qty=""2"" Unit=""шт"" />
      <Component Label=""s2"" Name=""пневмотрубка"" Qty=""0.5"" Unit=""м"" />
      <Component Label=""s3"" Name=""фитинг"" Qty=""4"" Unit=""шт"" />
      <Component Label=""s4"" Name=""кронштейн"" Qty=""1"" Unit=""шт"" />
    </Components>
    <Operations>
      <Operation Label=""o1"" Description=""Сборка блока пневматического: закрепить s1, подключить s2 через s3 на s4"" TimeMinutes=""20"" />
      <Operation Label=""o2"" Description=""Проверка блока пневматического: тест пневмосистемы, АЦП датчиков давления"" TimeMinutes=""40"" />
    </Operations>
  </Recipe>

  <Recipe Name=""корпус А"">
    <Components>
      <Component Label=""s1"" Name=""металлическая пластина"" Qty=""2"" Unit=""шт"" />
      <Component Label=""s2"" Name=""винт М4"" Qty=""8"" Unit=""шт"" />
      <Component Label=""s3"" Name=""провод ПВ-3"" Qty=""1.5"" Unit=""м"" />
    </Components>
    <Operations>
      <Operation Label=""o1"" Description=""Сверлить отверстия в s1 под s2"" TimeMinutes=""5"" />
      <Operation Label=""o2"" Description=""Закрепить s2 в s1"" TimeMinutes=""10"" />
    </Operations>
  </Recipe>

  <Recipe Name=""корпус Б"">
    <Components>
      <Component Label=""s1"" Name=""пластиковая панель"" Qty=""1"" Unit=""шт"" />
      <Component Label=""s2"" Name=""клей эпоксидный"" Qty=""50"" Unit=""мл"" />
    </Components>
    <Operations>
      <Operation Label=""o1"" Description=""Склеить s1"" TimeMinutes=""5"" />
    </Operations>
  </Recipe>

  <Recipe Name=""плата регистратора в сборе"">
    <Components>
      <Component Label=""s1"" Name=""печатная плата КНЯ85"" Qty=""1"" Unit=""шт"" />
      <Component Label=""s2"" Name=""комплект SMD компонентов"" Qty=""1"" Unit=""компл"" />
      <Component Label=""s3"" Name=""комплект THT компонентов"" Qty=""1"" Unit=""компл"" />
      <Component Label=""s4"" Name=""микросхема CPU1"" Qty=""1"" Unit=""шт"" />
      <Component Label=""s5"" Name=""микросхема CPU2"" Qty=""1"" Unit=""шт"" />
    </Components>
    <Operations>
      <Operation Label=""o1"" Description=""SMD монтаж s1"" TimeMinutes=""30"" />
      <Operation Label=""o2"" Description=""THT монтаж s1"" TimeMinutes=""25"" />
      <Operation Label=""o3"" Description=""Визуальный контроль платы"" TimeMinutes=""10"" />
      <Operation Label=""o4"" Description=""Проверка пайки"" TimeMinutes=""15"" />
      <Operation Label=""o5"" Description=""Прошивка CPU2 (s5)"" TimeMinutes=""8"" />
      <Operation Label=""o6"" Description=""Прошивка CPU1 (s4)"" TimeMinutes=""8"" />
      <Operation Label=""o7"" Description=""Проверка пайки после прошивки"" TimeMinutes=""10"" />
    </Operations>
  </Recipe>

  <Recipe Name=""печатная плата КНЯ85 в сборе"">
    <Components>
      <Component Label=""s1"" Name=""плата КНЯ85"" Qty=""1"" Unit=""шт"" />
      <Component Label=""s2"" Name=""резистор 10кОм"" Qty=""4"" Unit=""шт"" />
      <Component Label=""s3"" Name=""конденсатор 100нФ"" Qty=""2"" Unit=""шт"" />
      <Component Label=""s4"" Name=""филамент PLA"" Qty=""250"" Unit=""г"" />
    </Components>
    <Operations>
      <Operation Label=""o1"" Description=""Установить s2 на s1"" TimeMinutes=""8"" />
      <Operation Label=""o2"" Description=""Установить s3 на s1"" TimeMinutes=""5"" />
      <Operation Label=""o3"" Description=""Пропаять"" TimeMinutes=""15"" />
    </Operations>
  </Recipe>

  <Recipe Name=""регистратор РП-1"">
    <Components>
      <Component Label=""s1"" Name=""плата регистратора в сборе"" Qty=""1"" Unit=""шт"" />
      <Component Label=""s2"" Name=""блок пневматический"" Qty=""1"" Unit=""шт"" />
      <Component Label=""s3"" Name=""корпус регистратора"" Qty=""1"" Unit=""шт"" />
      <Component Label=""s4"" Name=""динамик"" Qty=""1"" Unit=""шт"" />
      <Component Label=""s5"" Name=""комплект крепежа"" Qty=""1"" Unit=""компл"" />
    </Components>
    <Operations>
      <Operation Label=""o1"" Description=""Сборка регистратора: установить s1 и s2 в s3, подключить s4"" TimeMinutes=""25"" />
      <Operation Label=""o2"" Description=""Калибровка регистратора"" TimeMinutes=""25"" />
      <Operation Label=""o3"" Description=""Проверка регистратора"" TimeMinutes=""15"" />
      <Operation Label=""o4"" Description=""Приёмо-сдаточные испытания (ПСИ)"" TimeMinutes=""30"" />
      <Operation Label=""o5"" Description=""Упаковка готовой продукции"" TimeMinutes=""5"" />
    </Operations>
  </Recipe>
</Recipes>";
}
