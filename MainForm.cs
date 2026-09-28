// ============================================================================
// MainForm.cs — главное окно: выбор прибора, количество, размер партии,
// кнопка «Сформировать» (потребность в компонентах + общее время; заказ
// сохраняется в отдельный XML со всеми заказами — orders.xml),
// кнопка «Заказы…» (таблица всех заказов, переход к конкретному),
// кнопка «Скачать заказ-наряды…» (пачка .docx по шаблону).
// ============================================================================
using System.Diagnostics;
using System.Text;
using Recipes.Models;
using Recipes.Services;

namespace Recipes;

public class MainForm : Form
{
    private readonly string _xmlPath;
    private readonly string _ordersPath;
    private readonly RecipeRepository _repository;
    private readonly OrdersRepository _ordersRepository;
    private Dictionary<string, Recipe> _recipes = new();

    // ── Элементы управления ────────────────────────────────────────────
    private readonly ComboBox _deviceCombo = new();
    private readonly NumericUpDown _quantityNumeric = new();
    private readonly NumericUpDown _batchSizeNumeric = new();
    private readonly CheckBox _showAllCheckBox = new();
    private readonly Button _generateButton = new();
    private readonly Button _downloadButton = new();
    private readonly Button _ordersButton = new();
    private readonly Button _editButton = new();
    private readonly DataGridView _materialsGrid = new();
    private readonly DataGridView _partsGrid = new();
    private readonly Label _totalTimeLabel = new();
    private readonly Label _warningLabel = new();
    private readonly Label _orderStatusLabel = new();

    // ── Последний сформированный заказ (для «Скачать заказ-наряды…») ──
    private CalculationResult? _lastResult;
    private string? _lastDevice;
    private double _lastQty;
    private Order? _lastSavedOrder;

    public MainForm()
    {
        _xmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recipes.xml");
        _ordersPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orders.xml");
        _ordersRepository = new OrdersRepository(_ordersPath);
        EnsureDefaultXml();

        _repository = new RecipeRepository(_xmlPath);
        LoadRecipes(showError: true);

        InitializeUi();
        PopulateDeviceCombo();
    }

    // ── Данные ─────────────────────────────────────────────────────────

    private void EnsureDefaultXml()
    {
        if (File.Exists(_xmlPath))
            return;

        try
        {
            File.WriteAllText(_xmlPath, DefaultRecipes.Xml, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Не удалось создать файл рецептов recipes.xml:\n" + ex.Message,
                "Recipes", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadRecipes(bool showError)
    {
        try
        {
            _recipes = _repository.LoadAll();
        }
        catch (Exception ex)
        {
            _recipes = new Dictionary<string, Recipe>();

            if (!showError)
                return;

            var answer = MessageBox.Show(
                "Не удалось прочитать файл рецептов:\n" + ex.Message +
                "\n\nВосстановить recipes.xml с примерными рецептами?",
                "Ошибка чтения XML", MessageBoxButtons.YesNo, MessageBoxIcon.Error);

            if (answer != DialogResult.Yes)
                return;

            try
            {
                File.WriteAllText(_xmlPath, DefaultRecipes.Xml, Encoding.UTF8);
                _recipes = _repository.LoadAll();
            }
            catch (Exception ex2)
            {
                MessageBox.Show(
                    "Не удалось восстановить файл:\n" + ex2.Message,
                    "Recipes", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void PopulateDeviceCombo()
    {
        var selected = _deviceCombo.SelectedItem as string;

        _deviceCombo.Items.Clear();

        var items = _showAllCheckBox.Checked
            ? _recipes.Keys.OrderBy(n => n, StringComparer.CurrentCulture).ToList()
            : RecipeRepository.GetRootProducts(_recipes);

        foreach (var name in items)
            _deviceCombo.Items.Add(name);

        if (items.Count == 0)
        {
            _deviceCombo.SelectedIndex = -1;
            return;
        }

        var index = selected is null ? -1 : items.IndexOf(selected);
        _deviceCombo.SelectedIndex = index >= 0 ? index : 0;
    }

    // ── Интерфейс ──────────────────────────────────────────────────────

    private void InitializeUi()
    {
        Text = "Recipes — планировщик производства";
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(1060, 720);
        MinimumSize = new Size(940, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(10)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));              // параметры
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));           // покупное сырьё
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));           // промежуточные детали
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));              // итоги

        root.Controls.Add(BuildParametersPanel(), 0, 0);
        root.Controls.Add(BuildGridGroup("Покупное сырьё (что закупить)", _materialsGrid), 0, 1);
        root.Controls.Add(BuildGridGroup("Промежуточные детали (изготавливаются по рецептам)", _partsGrid), 0, 2);
        root.Controls.Add(BuildTotalsPanel(), 0, 3);

        Controls.Add(root);
    }

    private Control BuildParametersPanel()
    {
        var panel = new GroupBox
        {
            Text = "Параметры заказа",
            Dock = DockStyle.Fill,
            AutoSize = true
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 5,
            RowCount = 3,
            Padding = new Padding(6)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var deviceLabel = new Label
        {
            Text = "Прибор:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 9, 8, 3)
        };

        _deviceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _deviceCombo.Dock = DockStyle.Fill;

        _showAllCheckBox.Text = "показать все рецепты (включая промежуточные детали)";
        _showAllCheckBox.AutoSize = true;
        _showAllCheckBox.Anchor = AnchorStyles.Left;
        _showAllCheckBox.Padding = new Padding(10, 0, 10, 0);
        _showAllCheckBox.CheckedChanged += (_, _) => PopulateDeviceCombo();

        var qtyLabel = new Label
        {
            Text = "Количество:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 9, 8, 3)
        };

        _quantityNumeric.Minimum = 1;
        _quantityNumeric.Maximum = 1_000_000;
        _quantityNumeric.DecimalPlaces = 1;
        _quantityNumeric.Increment = 1;
        _quantityNumeric.ThousandsSeparator = true;
        _quantityNumeric.Value = 1;
        _quantityNumeric.Width = 120;
        _quantityNumeric.Anchor = AnchorStyles.Left;

        var batchLabel = new Label
        {
            Text = "Размер партии:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(18, 9, 8, 3)
        };

        _batchSizeNumeric.Minimum = 1;
        _batchSizeNumeric.Maximum = 100_000;
        _batchSizeNumeric.Increment = 1;
        _batchSizeNumeric.ThousandsSeparator = true;
        _batchSizeNumeric.Value = 1;
        _batchSizeNumeric.Width = 90;
        _batchSizeNumeric.Anchor = AnchorStyles.Left;

        var toolTip = new ToolTip();
        toolTip.SetToolTip(_batchSizeNumeric,
            "Сколько изделий собирается за один заказ-наряд.\n" +
            "Детали делятся на партии этого размера — на каждую партию отдельный наряд.");

        _generateButton.Text = "Сформировать";
        _generateButton.AutoSize = true;
        _generateButton.MinimumSize = new Size(140, 32);
        _generateButton.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _generateButton.Click += OnGenerateClick;

        _downloadButton.Text = "Скачать заказ-наряды…";
        _downloadButton.AutoSize = true;
        _downloadButton.MinimumSize = new Size(200, 32);
        _downloadButton.Enabled = false;
        _downloadButton.Click += OnDownloadClick;

        _ordersButton.Text = "Заказы…";
        _ordersButton.AutoSize = true;
        _ordersButton.MinimumSize = new Size(120, 32);
        _ordersButton.Click += OnOrdersClick;

        _editButton.Text = "Редактор рецептов…";
        _editButton.AutoSize = true;
        _editButton.MinimumSize = new Size(170, 32);
        _editButton.Click += OnEditClick;

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            WrapContents = false
        };
        buttons.Controls.Add(_generateButton);
        buttons.Controls.Add(_downloadButton);
        buttons.Controls.Add(_ordersButton);
        buttons.Controls.Add(_editButton);

        // Строка 0: прибор.
        layout.Controls.Add(deviceLabel, 0, 0);
        layout.Controls.Add(_deviceCombo, 1, 0);
        layout.SetColumnSpan(_deviceCombo, 4);

        // Строка 1: количество, размер партии, чекбокс.
        layout.Controls.Add(qtyLabel, 0, 1);
        layout.Controls.Add(_quantityNumeric, 1, 1);
        layout.Controls.Add(batchLabel, 2, 1);
        layout.Controls.Add(_batchSizeNumeric, 3, 1);
        layout.Controls.Add(_showAllCheckBox, 4, 1);

        // Строка 2: кнопки справа.
        layout.Controls.Add(buttons, 0, 2);
        layout.SetColumnSpan(buttons, 5);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildGridGroup(string title, DataGridView grid)
    {
        ConfigureGrid(grid);

        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill
        };

        grid.Dock = DockStyle.Fill;
        group.Controls.Add(grid);
        return group;
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AllowUserToOrderColumns = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

        grid.Columns.Clear();
        grid.Columns.Add("Name", "Наименование");
        grid.Columns.Add("Qty", "Количество");
        grid.Columns.Add("Unit", "Ед. изм.");
        grid.Columns[0].FillWeight = 100;
        grid.Columns[1].FillWeight = 40;
        grid.Columns[2].FillWeight = 30;

        grid.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        grid.Columns[1].DefaultCellStyle.Format = "0.###";
    }

    private Control BuildTotalsPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(2)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _totalTimeLabel.Text = "Укажите прибор и количество, затем нажмите «Сформировать».";
        _totalTimeLabel.AutoSize = true;
        _totalTimeLabel.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        _totalTimeLabel.Padding = new Padding(2, 6, 2, 2);

        _warningLabel.Text = "";
        _warningLabel.AutoSize = true;
        _warningLabel.ForeColor = Color.FromArgb(176, 96, 0); // тёмно-оранжевый
        _warningLabel.Padding = new Padding(2, 2, 2, 4);

        _orderStatusLabel.Text = "";
        _orderStatusLabel.AutoSize = true;
        _orderStatusLabel.ForeColor = Color.FromArgb(0, 110, 64); // тёмно-зелёный
        _orderStatusLabel.Padding = new Padding(2, 2, 2, 4);

        layout.Controls.Add(_totalTimeLabel, 0, 0);
        layout.Controls.Add(_warningLabel, 0, 1);
        layout.Controls.Add(_orderStatusLabel, 0, 2);

        return layout;
    }

    // ── Действия ───────────────────────────────────────────────────────

    private void OnGenerateClick(object? sender, EventArgs e)
    {
        ClearResults();

        if (_deviceCombo.SelectedItem is not string device || string.IsNullOrWhiteSpace(device))
        {
            MessageBox.Show(this, "Выберите прибор из списка.",
                "Recipes", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var quantity = (double)_quantityNumeric.Value;

        var calculator = new BomCalculator(_recipes);
        var result = calculator.Calculate(device, quantity);

        if (result.Error is not null)
        {
            MessageBox.Show(this, result.Error,
                "Ошибка расчёта", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _lastResult = result;
        _lastDevice = device;
        _lastQty = quantity;
        _downloadButton.Enabled = true;

        // Заказ сохраняется в отдельный XML-файл со всеми заказами.
        SaveOrder(device, quantity, result);

        FillGrid(_materialsGrid, result.PurchasedMaterials);
        FillGrid(_partsGrid, result.ManufacturedParts);

        // Итоговое время (уже включает надбавку 10% — отдельно не показываем).
        if (result.PureMinutes > 0)
        {
            _totalTimeLabel.Text =
                $"Общее время производства: {AppFormat.FormatMinutes(result.TotalMinutes)}  ({result.TotalMinutes} мин)";
        }
        else if (result.HasUnnormedOperations)
        {
            _totalTimeLabel.Text = "Общее время производства: нормировка не выполнена";
        }
        else
        {
            _totalTimeLabel.Text = "В дереве рецептов нет операций.";
        }

        // «Журнал непорядка» — какие рецепты содержат операции без нормы.
        if (result.HasUnnormedOperations)
        {
            var lines = result.Unnormed
                .GroupBy(u => u.RecipeName)
                .Select(g => "  • " + g.Key + ":" + Environment.NewLine +
                    string.Join(Environment.NewLine, g.Select(u =>
                        string.IsNullOrEmpty(u.OpLabel)
                            ? "      " + u.OpDescription
                            : "      " + u.OpLabel + ". " + u.OpDescription)));

            _warningLabel.Text =
                "Нормировка не выполнена для операций (в расчёт времени они не вошли):" +
                Environment.NewLine + string.Join(Environment.NewLine, lines);
        }
    }

    private void OnDownloadClick(object? sender, EventArgs e)
    {
        if (_lastResult is null || _lastDevice is null)
        {
            MessageBox.Show(this, "Сначала сформируйте заказ кнопкой «Сформировать».",
                "Recipes", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Папка, в которую будет сохранён заказ\n" +
                          "(наряды .docx и сводка материалов):",
            ShowNewFolderButton = true,
            SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var templatePath = DocxExporter.EnsureTemplate(AppDomain.CurrentDomain.BaseDirectory);

            // Если заказ сохранён — печатаем наряды из снимка заказа (orders.xml),
            // чтобы файлы совпадали с тем, что хранится в XML.
            var orderDir = _lastSavedOrder is not null
                ? OrderWriter.WriteOrderFolderFromOrder(
                    dialog.SelectedPath, _lastSavedOrder, templatePath)
                : OrderWriter.WriteOrderFolder(
                    dialog.SelectedPath,
                    _lastDevice,
                    _lastQty,
                    _lastResult,
                    _recipes,
                    (int)_batchSizeNumeric.Value,
                    templatePath);

            var answer = MessageBox.Show(this,
                "Заказ-наряды сохранены:\n" + orderDir + "\n\nОткрыть папку?",
                "Готово", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (answer == DialogResult.Yes)
                Process.Start(new ProcessStartInfo { FileName = orderDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Не удалось сформировать заказ-наряды:\n" + ex.Message,
                "Ошибка экспорта", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Перейти к «странице заказов»: таблица всех заказов orders.xml.</summary>
    private void OnOrdersClick(object? sender, EventArgs e)
    {
        using var form = new OrdersForm(_ordersPath);
        form.ShowDialog(this);
    }

    /// <summary>Сохранить сформированный заказ в orders.xml (файл всех заказов).</summary>
    private void SaveOrder(string device, double quantity, CalculationResult result)
    {
        _lastSavedOrder = null;
        _orderStatusLabel.Text = "";

        try
        {
            var order = OrderFactory.FromCalculation(
                device, quantity, (int)_batchSizeNumeric.Value, result, _recipes);
            _ordersRepository.Add(order);

            _lastSavedOrder = order;
            _orderStatusLabel.Text =
                $"Заказ №{order.Number} от {order.CreatedAt:dd.MM.yyyy HH:mm} сохранён в orders.xml " +
                "(см. «Заказы…»)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Расчёт выполнен, но заказ не удалось сохранить в orders.xml:\n" + ex.Message,
                "Ошибка сохранения заказа", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnEditClick(object? sender, EventArgs e)
    {
        using var editor = new RecipeEditorForm(_xmlPath);
        editor.ShowDialog(this);

        LoadRecipes(showError: true);
        PopulateDeviceCombo();
        ClearResults();
    }

    private static void FillGrid(DataGridView grid, IEnumerable<Requirement> rows)
    {
        grid.SuspendLayout();
        grid.Rows.Clear();
        foreach (var r in rows)
            grid.Rows.Add(r.Name, AppFormat.FormatQty(r.Qty), r.Unit);
        grid.ResumeLayout();
    }

    private void ClearResults()
    {
        _materialsGrid.Rows.Clear();
        _partsGrid.Rows.Clear();
        _lastResult = null;
        _lastDevice = null;
        _lastQty = 0;
        _lastSavedOrder = null;
        _downloadButton.Enabled = false;
        _totalTimeLabel.Text = "Укажите прибор и количество, затем нажмите «Сформировать».";
        _warningLabel.Text = "";
        _orderStatusLabel.Text = "";
    }
}
