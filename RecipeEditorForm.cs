// ============================================================================
// RecipeEditorForm.cs — редактор рецептов (аналог update-time + правка
// файлов рецептов из prodplan, но прямо в программе, с сохранением в XML).
// ============================================================================
using System.Text;
using Recipes.Models;
using Recipes.Services;

namespace Recipes;

public class RecipeEditorForm : Form
{
    private readonly string _xmlPath;
    private readonly RecipeRepository _repository;
    private Dictionary<string, Recipe> _recipes = new();

    private readonly ListBox _recipeList = new();
    private readonly TextBox _nameBox = new();
    private readonly NumericUpDown _runQtyBox = new();
    private readonly DataGridView _componentsGrid = new();
    private readonly DataGridView _operationsGrid = new();
    private readonly Button _newButton = new();
    private readonly Button _deleteButton = new();
    private readonly Button _importButton = new();
    private readonly Button _exportButton = new();
    private readonly Button _importExcelButton = new();
    private readonly Button _exportExcelButton = new();
    private readonly Button _addFromRecipeButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _closeButton = new();
    private readonly Label _statusLabel = new();

    // Индексы столбцов таблицы «Состав» (последний — вычисляемый индикатор).
    private const int ColCompLabel = 0;
    private const int ColCompName = 1;
    private const int ColCompQty = 2;
    private const int ColCompUnit = 3;
    private const int ColCompRecipe = 4;

    /// <summary>Имя рецепта, который сейчас открыт в редакторе (null = ничего).</summary>
    private string? _currentName;

    /// <summary>Блокирует обработчик смены выбора при программной замене списка.</summary>
    private bool _suppressListEvents;

    public RecipeEditorForm(string xmlPath)
    {
        _xmlPath = xmlPath;
        EnsureDefaultXml();

        _repository = new RecipeRepository(xmlPath);
        try
        {
            _recipes = _repository.LoadAll();
        }
        catch (Exception ex)
        {
            _recipes = new Dictionary<string, Recipe>();
            MessageBox.Show(
                "Не удалось прочитать recipes.xml:\n" + ex.Message +
                "\nПродолжайте с пустым списком (кнопка «Новый рецепт» создаст новый файл).",
                "Редактор рецептов", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        InitializeUi();
        RefreshList(selectName: FirstRecipeName());
        _statusLabel.Text = $"Рецептов: {_recipes.Count}";
    }

    private string? FirstRecipeName()
        => _recipes.Keys.OrderBy(n => n, StringComparer.CurrentCulture).FirstOrDefault();

    private void EnsureDefaultXml()
    {
        if (File.Exists(_xmlPath))
            return;

        try
        {
            File.WriteAllText(_xmlPath, DefaultRecipes.Xml, Encoding.UTF8);
        }
        catch
        {
            // Пусть форма продолжит работу с пустым списком — сохранение создаст файл.
        }
    }

    // ── Интерфейс ──────────────────────────────────────────────────────

    private void InitializeUi()
    {
        Text = "Редактор рецептов";
        ClientSize = new Size(1040, 660);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1
        };

        // ── Слева: список рецептов ──
        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(6)
        };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _recipeList.Dock = DockStyle.Fill;
        _recipeList.IntegralHeight = false;
        _recipeList.SelectedIndexChanged += OnSelectedRecipeChanged;

        _newButton.Text = "Новый";
        _newButton.AutoSize = true;
        _newButton.MinimumSize = new Size(104, 30);
        _newButton.Click += OnNewClick;

        _deleteButton.Text = "Удалить";
        _deleteButton.AutoSize = true;
        _deleteButton.MinimumSize = new Size(104, 30);
        _deleteButton.Click += OnDeleteClick;

        _importButton.Text = "Импорт из XML…";
        _importButton.AutoSize = true;
        _importButton.MinimumSize = new Size(140, 30);
        _importButton.Click += OnImportClick;

        _exportButton.Text = "Экспорт в XML…";
        _exportButton.AutoSize = true;
        _exportButton.MinimumSize = new Size(140, 30);
        _exportButton.Click += OnExportClick;

        _importExcelButton.Text = "Импорт из Excel…";
        _importExcelButton.AutoSize = true;
        _importExcelButton.MinimumSize = new Size(150, 30);
        _importExcelButton.Click += OnImportExcelClick;

        _exportExcelButton.Text = "Экспорт в Excel…";
        _exportExcelButton.AutoSize = true;
        _exportExcelButton.MinimumSize = new Size(150, 30);
        _exportExcelButton.Click += OnExportExcelClick;

        var listButtons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 6, 0, 0)
        };
        listButtons.Controls.Add(_newButton);
        listButtons.Controls.Add(_deleteButton);
        listButtons.Controls.Add(_importButton);
        listButtons.Controls.Add(_exportButton);
        listButtons.Controls.Add(_importExcelButton);
        listButtons.Controls.Add(_exportExcelButton);

        left.Controls.Add(new Label { Text = "Рецепты:", AutoSize = true }, 0, 0);
        left.Controls.Add(_recipeList, 0, 1);
        left.Controls.Add(listButtons, 0, 2);

        split.Panel1.Controls.Add(left);

        // ── Справа: редактор одного рецепта ──
        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(6)
        };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // название
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // партия изготовления
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 50));     // состав
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 50));     // операции
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // кнопки

        _nameBox.Dock = DockStyle.Fill;
        _nameBox.Margin = new Padding(3, 3, 20, 3);

        right.Controls.Add(new Label
        {
            Text = "Название детали / прибора:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 8, 3)
        }, 0, 0);
        right.Controls.Add(_nameBox, 1, 0);

        // Партия изготовления: сколько штук делается за один производственный
        // цикл (например, пневмосистемы заливаются сразу по 20 шт).
        _runQtyBox.Minimum = 1;
        _runQtyBox.Maximum = 1000000;
        _runQtyBox.Value = 1;
        _runQtyBox.Width = 110;
        _runQtyBox.Margin = new Padding(3, 3, 20, 3);

        var runQtyHint = "Сколько штук изготавливается за один производственный " +
            "цикл (например, пневмосистемы заливаются сразу по 20 шт, хотя на прибор " +
            "нужна одна). Наряды этой детали считаются партиями такого размера; " +
            "время без участия человека — один раз на партию.";

        var runQtyLabel = new Label
        {
            Text = "Изготавливать за раз, шт:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 8, 3)
        };
        var runQtyTip = new ToolTip();
        runQtyTip.SetToolTip(runQtyLabel, runQtyHint);
        runQtyTip.SetToolTip(_runQtyBox, runQtyHint);

        right.Controls.Add(runQtyLabel, 0, 1);
        right.Controls.Add(_runQtyBox, 1, 1);

        var compGroup = new GroupBox
        {
            Text = "Состав (компоненты): метка — для подстановки в операции, имя — деталь/материал",
            Dock = DockStyle.Fill
        };
        ConfigureComponentsGrid();

        // Кнопка добавления существующего рецепта в состав + подсказка
        // о механизме вложенных рецептов («наследования»).
        _addFromRecipeButton.Text = "Добавить из рецептов…";
        _addFromRecipeButton.AutoSize = true;
        _addFromRecipeButton.MinimumSize = new Size(170, 28);
        _addFromRecipeButton.Click += OnAddFromRecipeClick;

        var compHint = new Label
        {
            Text = "Имя, совпадающее с названием другого рецепта — наша деталь: её состав " +
                   "и операции войдут в расчёт, для неё создадутся отдельные наряды. " +
                   "Совпадения нет — покупное сырьё.",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(10, 6, 3, 3)
        };

        var compBottom = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 4, 0, 2)
        };
        compBottom.Controls.Add(_addFromRecipeButton);
        compBottom.Controls.Add(compHint);

        var compLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        compLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        compLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        compLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _componentsGrid.Dock = DockStyle.Fill;
        compLayout.Controls.Add(_componentsGrid, 0, 0);
        compLayout.Controls.Add(compBottom, 0, 1);

        compGroup.Controls.Add(compLayout);
        right.Controls.Add(compGroup, 0, 2);
        right.SetColumnSpan(compGroup, 2);

        var opsGroup = new GroupBox
        {
            Text = "Операции: время — мин на 1 шт.; без человека — задержка без участия человека " +
                   "(застывание, сушка…), мин на одно выполнение операции (партию); " +
                   "пусто или 0 = нормировка не выполнена",
            Dock = DockStyle.Fill
        };
        ConfigureOperationsGrid();
        _operationsGrid.Dock = DockStyle.Fill;
        opsGroup.Controls.Add(_operationsGrid);
        right.Controls.Add(opsGroup, 0, 3);
        right.SetColumnSpan(opsGroup, 2);

        _saveButton.Text = "Сохранить изменения";
        _saveButton.AutoSize = true;
        _saveButton.MinimumSize = new Size(170, 32);
        _saveButton.Click += OnSaveClick;

        _closeButton.Text = "Закрыть";
        _closeButton.AutoSize = true;
        _closeButton.MinimumSize = new Size(110, 32);
        _closeButton.Click += (_, _) => Close();

        _statusLabel.AutoSize = true;
        _statusLabel.Anchor = AnchorStyles.Left;
        _statusLabel.ForeColor = Color.DimGray;
        _statusLabel.Text = "";

        var bottom = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false
        };
        bottom.Controls.Add(_closeButton);
        bottom.Controls.Add(_saveButton);
        bottom.Controls.Add(_statusLabel);

        right.Controls.Add(bottom, 0, 4);
        right.SetColumnSpan(bottom, 2);

        split.Panel2.Controls.Add(right);

        Controls.Add(split);
    }

    private void ConfigureComponentsGrid()
    {
        var grid = _componentsGrid;
        grid.AllowUserToResizeRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

        grid.Columns.Add("Label", "Метка");
        grid.Columns.Add("Name", "Наименование");
        grid.Columns.Add("Qty", "Кол-во");
        grid.Columns.Add("Unit", "Ед. изм.");
        grid.Columns.Add("RecipeRef", "Рецепт");
        grid.Columns[0].FillWeight = 22;
        grid.Columns[1].FillWeight = 90;
        grid.Columns[2].FillWeight = 30;
        grid.Columns[3].FillWeight = 26;
        grid.Columns[4].FillWeight = 44;

        // Столбец-индикатор вычисляется программно и вручную не редактируется.
        grid.Columns[4].ReadOnly = true;

        // Живое обновление индикатора «Рецепт» при вводе имени.
        grid.CellValueChanged += OnComponentsCellValueChanged;
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
    }

    private void ConfigureOperationsGrid()
    {
        var grid = _operationsGrid;
        grid.AllowUserToResizeRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

        grid.Columns.Add("Label", "Метка");
        grid.Columns.Add("Description", "Описание операции");
        grid.Columns.Add("Time", "Время, мин");
        grid.Columns.Add("NonHuman", "Без человека, мин");
        grid.Columns[0].FillWeight = 20;
        grid.Columns[1].FillWeight = 100;
        grid.Columns[2].FillWeight = 30;
        grid.Columns[3].FillWeight = 30;

        grid.Columns[2].ToolTipText = "Норма времени исполнителя, минут на 1 шт. детали.";
        grid.Columns[3].ToolTipText = "Задержка без участия человека (застывание, сушка и т.п.), " +
            "минут — на одно выполнение операции (на партию изготовления), " +
            "независимо от количества штук в партии.";
    }

    // ── Загрузка / выгрузка рецепта в редактор ─────────────────────────

    private void RefreshList(string? selectName)
    {
        _suppressListEvents = true;
        try
        {
            _recipeList.BeginUpdate();
            _recipeList.Items.Clear();
            foreach (var name in _recipes.Keys.OrderBy(n => n, StringComparer.CurrentCulture))
                _recipeList.Items.Add(name);
            _recipeList.SelectedIndex = selectName is null
                ? -1
                : _recipeList.Items.IndexOf(selectName);
            _recipeList.EndUpdate();
        }
        finally
        {
            _suppressListEvents = false;
        }

        LoadRecipeIntoEditor(_recipeList.SelectedItem as string);
    }

    private void LoadRecipeIntoEditor(string? name)
    {
        _currentName = name;

        if (name is not null && _recipes.TryGetValue(name, out var recipe))
        {
            _nameBox.Text = recipe.Name;
            _runQtyBox.Value = Math.Clamp(recipe.RunQty, 1, 1000000);

            _componentsGrid.Rows.Clear();
            foreach (var c in recipe.Components)
                _componentsGrid.Rows.Add(c.Label, c.Name, AppFormat.FormatQty(c.Qty), c.Unit);
            UpdateAllRecipeIndicators();

            _operationsGrid.Rows.Clear();
            foreach (var o in recipe.Operations)
                _operationsGrid.Rows.Add(o.Label, o.Description,
                    o.HasTime ? AppFormat.FormatQty(o.TimeMinutes) : "",
                    o.HasNonHumanTime ? AppFormat.FormatQty(o.NonHumanMinutes) : "");
        }
        else
        {
            _nameBox.Text = "";
            _runQtyBox.Value = 1;
            _componentsGrid.Rows.Clear();
            _operationsGrid.Rows.Clear();
        }
    }

    // ── Обработчики ────────────────────────────────────────────────────

    private void OnSelectedRecipeChanged(object? sender, EventArgs e)
    {
        if (_suppressListEvents)
            return;

        if (!TryApplyEditorToCurrentRecipe(out var error))
        {
            // Не даём переключиться, пока не исправлены ошибки: вернуть выделение.
            _suppressListEvents = true;
            _recipeList.SelectedIndex = _currentName is null
                ? -1
                : _recipeList.Items.IndexOf(_currentName);
            _suppressListEvents = false;

            MessageBox.Show(this, error, "Проверка данных",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        LoadRecipeIntoEditor(_recipeList.SelectedItem as string);
    }

    private void OnNewClick(object? sender, EventArgs e)
    {
        if (!TryApplyEditorToCurrentRecipe(out var error))
        {
            MessageBox.Show(this, error, "Проверка данных",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var baseName = "Новый рецепт";
        var name = baseName;
        var i = 2;
        while (_recipes.ContainsKey(name))
            name = baseName + " " + i++;

        _recipes[name] = new Recipe(name);
        SaveAllToDisk();
        RefreshList(selectName: name);
    }

    private void OnDeleteClick(object? sender, EventArgs e)
    {
        if (_currentName is null)
        {
            MessageBox.Show(this, "Выберите рецепт в списке.",
                "Редактор рецептов", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var referencing = RecipeRepository.FindReferencing(_recipes, _currentName)
            .Where(n => !string.Equals(n, _currentName, StringComparison.Ordinal))
            .ToList();

        var message = $"Удалить рецепт «{_currentName}»?";
        if (referencing.Count > 0)
        {
            message += "\n\nВнимание: на этот рецепт ссылаются как на компонент:\n  • " +
                       string.Join("\n  • ", referencing) +
                       "\nПосле удаления этот компонент будет считаться покупным сырьём.";
        }

        if (MessageBox.Show(this, message, "Удаление рецепта",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _recipes.Remove(_currentName);
        _currentName = null;
        SaveAllToDisk();
        RefreshList(selectName: FirstRecipeName());
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        var newName = _nameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(newName))
        {
            MessageBox.Show(this, "Укажите название детали/прибора.",
                "Проверка данных", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!TryApplyEditorToCurrentRecipe(out var error))
        {
            MessageBox.Show(this, error, "Проверка данных",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var oldName = _currentName;

        if (oldName is not null && newName != oldName)
        {
            if (_recipes.ContainsKey(newName))
            {
                MessageBox.Show(this, $"Рецепт «{newName}» уже существует.",
                    "Проверка данных", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Кто ссылается на старое имя как на компонент состава —
            // вложенные рецепты связаны именно по имени.
            var referencing = RecipeRepository.FindReferencing(_recipes, oldName);

            if (referencing.Count > 0)
            {
                var message =
                    $"На компонент «{oldName}» ссылаются рецепты:\n  • " +
                    string.Join("\n  • ", referencing) +
                    $"\n\nОбновить ссылки на новое имя «{newName}»?\n\n" +
                    "Да — обновить (рекомендуется).\n" +
                    "Нет — оставить как есть (эти строки станут покупным сырьём).\n" +
                    "Отмена — не переименовывать.";

                var answer = MessageBox.Show(this, message, "Переименование рецепта",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                if (answer == DialogResult.Cancel)
                    return;

                if (answer == DialogResult.Yes)
                {
                    foreach (var r in _recipes.Values)
                        foreach (var c in r.Components)
                            if (string.Equals(c.Name, oldName, StringComparison.Ordinal))
                                c.Name = newName;
                }
            }

            var recipe = _recipes[oldName];
            _recipes.Remove(oldName);
            recipe.Name = newName;
            _recipes[newName] = recipe;
        }

        SaveAllToDisk();
        RefreshList(selectName: newName);
    }

    // ── Импорт / экспорт рецептов (отдельный XML-файл) ────────────────

    private void OnExportClick(object? sender, EventArgs e)
    {
        // Сначала фиксируем правки текущего рецепта.
        if (!TryApplyEditorToCurrentRecipe(out var error))
        {
            MessageBox.Show(this, error, "Проверка данных",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_recipes.Count == 0)
        {
            MessageBox.Show(this, "Нет рецептов для экспорта.",
                "Экспорт", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Экспорт рецептов в XML",
            Filter = "XML-файлы (*.xml)|*.xml|Все файлы (*.*)|*.*",
            FileName = "recipes_export.xml"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            RecipeRepository.SaveTo(dialog.FileName, _recipes);
            _statusLabel.Text = $"Экспортировано рецептов: {_recipes.Count}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Не удалось выполнить экспорт:\n" + ex.Message,
                "Ошибка экспорта", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnImportClick(object? sender, EventArgs e)
    {
        // Сначала фиксируем правки текущего рецепта.
        if (!TryApplyEditorToCurrentRecipe(out var error))
        {
            MessageBox.Show(this, error, "Проверка данных",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Импорт рецептов из XML",
            Filter = "XML-файлы (*.xml)|*.xml|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var imported = RecipeRepository.LoadFrom(dialog.FileName);

            if (imported.Count == 0)
            {
                MessageBox.Show(this, "В выбранном файле нет рецептов.",
                    "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int added = 0, updated = 0;
            foreach (var pair in imported)
            {
                if (_recipes.ContainsKey(pair.Key))
                    updated++;   // рецепт с таким именем уже есть — заменяем
                else
                    added++;
                _recipes[pair.Key] = pair.Value;
            }

            SaveAllToDisk();

            var keepName = _currentName is not null && _recipes.ContainsKey(_currentName)
                ? _currentName
                : FirstRecipeName();
            RefreshList(selectName: keepName);

            MessageBox.Show(this,
                $"Импорт завершён.\nДобавлено рецептов: {added}\nОбновлено рецептов: {updated}",
                "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Не удалось выполнить импорт:\n" + ex.Message,
                "Ошибка импорта", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Импорт / экспорт рецептов (Excel .xlsx) ───────────────────────

    private void OnExportExcelClick(object? sender, EventArgs e)
    {
        // Сначала фиксируем правки текущего рецепта.
        if (!TryApplyEditorToCurrentRecipe(out var error))
        {
            MessageBox.Show(this, error, "Проверка данных",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_recipes.Count == 0)
        {
            MessageBox.Show(this, "Нет рецептов для экспорта.",
                "Экспорт", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Экспорт рецептов в Excel",
            Filter = "Книги Excel (*.xlsx)|*.xlsx|Все файлы (*.*)|*.*",
            FileName = "recipes_export.xlsx"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            RecipesExcel.ExportToExcel(dialog.FileName, _recipes.Values);
            _statusLabel.Text = $"Экспортировано в Excel рецептов: {_recipes.Count}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Не удалось выполнить экспорт:\n" + ex.Message,
                "Ошибка экспорта", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnImportExcelClick(object? sender, EventArgs e)
    {
        // Сначала фиксируем правки текущего рецепта.
        if (!TryApplyEditorToCurrentRecipe(out var error))
        {
            MessageBox.Show(this, error, "Проверка данных",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Импорт рецептов из Excel",
            Filter = "Книги Excel (*.xlsx)|*.xlsx|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var imported = RecipesExcel.ImportFromExcel(dialog.FileName);

            if (imported.Count == 0)
            {
                MessageBox.Show(this, "В выбранной книге нет листов с рецептами.",
                    "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int added = 0, updated = 0;
            foreach (var pair in imported)
            {
                if (_recipes.ContainsKey(pair.Key))
                    updated++;   // рецепт с таким именем уже есть — заменяем
                else
                    added++;
                _recipes[pair.Key] = pair.Value;
            }

            SaveAllToDisk();

            var keepName = _currentName is not null && _recipes.ContainsKey(_currentName)
                ? _currentName
                : FirstRecipeName();
            RefreshList(selectName: keepName);

            MessageBox.Show(this,
                $"Импорт из Excel завершён.\nДобавлено рецептов: {added}\nОбновлено рецептов: {updated}",
                "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Не удалось выполнить импорт:\n" + ex.Message,
                "Ошибка импорта", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Вложенные рецепты («наследование»): индикатор и выбор из списка ─

    private void OnComponentsCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != ColCompName)
            return;

        UpdateRecipeIndicator(_componentsGrid.Rows[e.RowIndex]);
    }

    private void UpdateAllRecipeIndicators()
    {
        foreach (DataGridViewRow row in _componentsGrid.Rows)
            UpdateRecipeIndicator(row);
    }

    /// <summary>
    /// Пересчитать индикатор строки состава: имя совпадает с рецептом →
    /// «✓ наша деталь» (зелёный), совпадения нет → «— покупное», ссылка
    /// замыкает цикл на текущий рецепт → «⚠ цикл!» (красный).
    /// </summary>
    private void UpdateRecipeIndicator(DataGridViewRow row)
    {
        if (row.IsNewRow)
            return;

        var cell = row.Cells[ColCompRecipe];
        var name = (row.Cells[ColCompName].Value?.ToString() ?? "").Trim();

        if (name.Length == 0)
        {
            cell.Value = "";
            cell.Style.BackColor = Color.White;
            cell.ToolTipText = null;
            return;
        }

        if (_recipes.ContainsKey(name))
        {
            var isCycle = _currentName is not null &&
                          RecipeRepository.Reaches(_recipes, name, _currentName);

            if (isCycle)
            {
                cell.Value = name == _currentName ? "⚠ сам на себя!" : "⚠ цикл!";
                cell.Style.BackColor = Color.FromArgb(255, 210, 210);
                cell.ToolTipText = "Циклическая ссылка: расчёт такого состава " +
                    "завершится ошибкой «циклическая ссылка в составе».";
            }
            else
            {
                cell.Value = "✓ наша деталь";
                cell.Style.BackColor = Color.FromArgb(213, 239, 213);
                cell.ToolTipText = "Имя совпадает с рецептом «" + name + "»: строка считается " +
                    "нашей деталью — её состав и операции войдут в расчёт, для неё " +
                    "создадутся отдельные наряды. Рецепт при этом остаётся самостоятельным.";
            }
            return;
        }

        cell.Value = "— покупное";
        cell.Style.BackColor = Color.White;
        cell.ToolTipText = "Рецепта с таким именем нет — строка считается покупным сырьём. " +
            "Имя должно совпадать с названием рецепта точно (включая регистр и пробелы).";
    }

    private void OnAddFromRecipeClick(object? sender, EventArgs e)
    {
        if (_currentName is null)
        {
            MessageBox.Show(this, "Сначала выберите или создайте рецепт.",
                "Редактор рецептов", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Завершаем возможное редактирование ячейки, чтобы не потерять ввод.
        _componentsGrid.EndEdit();

        var candidates = _recipes.Keys
            .Where(n => !string.Equals(n, _currentName, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.CurrentCulture);

        using var picker = new RecipePickerForm(candidates);
        if (picker.ShowDialog(this) != DialogResult.OK ||
            picker.SelectedNames.Count == 0)
            return;

        // Имена, уже присутствующие в составе (дубли не добавляем).
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (DataGridViewRow row in _componentsGrid.Rows)
        {
            if (row.IsNewRow)
                continue;
            var name = (row.Cells[ColCompName].Value?.ToString() ?? "").Trim();
            if (name.Length > 0)
                existing.Add(name);
        }

        var added = 0;
        var skipped = new List<string>();
        foreach (var name in picker.SelectedNames)
        {
            if (!existing.Add(name))
            {
                skipped.Add(name);
                continue;
            }
            _componentsGrid.Rows.Add("", name, "1", "шт");
            added++;
        }

        UpdateAllRecipeIndicators();

        if (added == 0)
        {
            MessageBox.Show(this,
                "Выбранные детали уже есть в составе — ничего не добавлено.",
                "Добавление из рецептов", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else if (skipped.Count > 0)
        {
            MessageBox.Show(this,
                "Добавлено строк: " + added +
                "\nУже были в составе: " + string.Join(", ", skipped),
                "Добавление из рецептов", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ── Чтение таблиц редактора в модель ───────────────────────────────

    private bool TryApplyEditorToCurrentRecipe(out string? error)
    {
        error = null;

        if (_currentName is null || !_recipes.TryGetValue(_currentName, out var recipe))
            return true;

        var components = ReadComponentsGrid(out error);
        if (error is not null)
            return false;

        var operations = ReadOperationsGrid(out error);
        if (error is not null)
            return false;

        recipe.Components = components;
        recipe.Operations = operations;
        recipe.RunQty = (int)_runQtyBox.Value;
        return true;
    }

    private List<Component> ReadComponentsGrid(out string? error)
    {
        error = null;
        var list = new List<Component>();
        var seen = new Dictionary<string, string>();

        foreach (DataGridViewRow row in _componentsGrid.Rows)
        {
            if (row.IsNewRow)
                continue;

            var label = Cell(row, ColCompLabel).Trim();
            var name = Cell(row, ColCompName).Trim();
            var qtyText = Cell(row, ColCompQty).Trim();
            var unit = Cell(row, ColCompUnit).Trim();

            if (label.Length == 0 && name.Length == 0 && qtyText.Length == 0)
                continue; // пустая строка — пропускаем

            if (name.Length == 0)
            {
                error = $"Состав, строка {row.Index + 1}: не указано наименование.";
                return list;
            }

            if (!AppFormat.TryParseDouble(qtyText, out var qty) || qty <= 0)
            {
                error = $"Состав, строка {row.Index + 1}: количество должно быть числом больше нуля (пример: 8 или 1,5).";
                return list;
            }

            if (label.Length > 0)
            {
                if (seen.TryGetValue(label, out var prevName) && prevName != name)
                {
                    error = $"Состав, строка {row.Index + 1}: дубль метки «{label}» с другим именем ({prevName}).";
                    return list;
                }
                seen[label] = name;
            }

            list.Add(new Component(label, name, qty,
                unit.Length == 0 ? "шт" : unit));
        }

        return list;
    }

    private List<Operation> ReadOperationsGrid(out string? error)
    {
        error = null;
        var list = new List<Operation>();
        var seen = new HashSet<string>();

        foreach (DataGridViewRow row in _operationsGrid.Rows)
        {
            if (row.IsNewRow)
                continue;

            var label = Cell(row, 0).Trim();
            var description = Cell(row, 1).Trim();
            var timeText = Cell(row, 2).Trim();
            var nonHumanText = Cell(row, 3).Trim();

            if (label.Length == 0 && description.Length == 0 &&
                timeText.Length == 0 && nonHumanText.Length == 0)
                continue;

            if (description.Length == 0)
            {
                error = $"Операции, строка {row.Index + 1}: пустое описание.";
                return list;
            }

            double time = 0;
            if (timeText.Length > 0 &&
                (!AppFormat.TryParseDouble(timeText, out time) || time < 0))
            {
                error = $"Операции, строка {row.Index + 1}: время должно быть числом минут (0 или пусто = нормировка не выполнена).";
                return list;
            }

            double nonHuman = 0;
            if (nonHumanText.Length > 0 &&
                (!AppFormat.TryParseDouble(nonHumanText, out nonHuman) || nonHuman < 0))
            {
                error = $"Операции, строка {row.Index + 1}: время без участия человека должно быть числом минут (0 или пусто = нет).";
                return list;
            }

            if (label.Length > 0 && !seen.Add(label))
            {
                error = $"Операции, строка {row.Index + 1}: дубль метки «{label}».";
                return list;
            }

            list.Add(new Operation(label, description,
                time > 0 ? time : null,
                nonHuman > 0 ? nonHuman : null));
        }

        return list;
    }

    private static string Cell(DataGridViewRow row, int index)
        => row.Cells[index].Value?.ToString() ?? "";

    // ── Сохранение ─────────────────────────────────────────────────────

    private void SaveAllToDisk()
    {
        try
        {
            _repository.SaveAll(_recipes);
            _statusLabel.Text = $"Сохранено: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Не удалось сохранить файл рецептов:\n" + ex.Message,
                "Ошибка сохранения", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
