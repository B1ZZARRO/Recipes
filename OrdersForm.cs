// ============================================================================
// OrdersForm.cs — «страница заказов»: общая таблица всех заказов
// (orders.xml), переход к конкретному заказу, удаление, выгрузка нарядов.
// Накладные ведутся в карточке конкретного заказа (OrderDetailsForm,
// верхняя таблица «Позиции заказа»), поэтому здесь их нет.
// ============================================================================
using System.Diagnostics;
using Recipes.Models;
using Recipes.Services;

namespace Recipes;

public class OrdersForm : Form
{
    private readonly OrdersRepository _repository;
    private List<Order> _orders = new();

    private readonly DataGridView _grid = new();
    private readonly Button _openButton = new();
    private readonly Button _exportButton = new();
    private readonly Button _deleteButton = new();
    private readonly Button _refreshButton = new();
    private readonly Button _closeButton = new();
    private readonly Label _infoLabel = new();

    public OrdersForm(string ordersPath)
    {
        _repository = new OrdersRepository(ordersPath);
        InitializeUi();
        ReloadOrders();
    }

    // ── Интерфейс ──────────────────────────────────────────────────────

    private void InitializeUi()
    {
        Text = "Заказы — Recipes";
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(1120, 580);
        MinimumSize = new Size(940, 440);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // таблица
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // кнопки
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // подвал

        ConfigureGrid();

        var group = new GroupBox
        {
            Text = "Все заказы (orders.xml)",
            Dock = DockStyle.Fill
        };
        _grid.Dock = DockStyle.Fill;
        group.Controls.Add(_grid);

        root.Controls.Add(group, 0, 0);
        root.Controls.Add(BuildButtonsPanel(), 0, 1);
        root.Controls.Add(BuildFooterPanel(), 0, 2);

        Controls.Add(root);

        CancelButton = _closeButton;
    }

    private void ConfigureGrid()
    {
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AllowUserToOrderColumns = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

        _grid.CellDoubleClick += OnGridCellDoubleClick;

        _grid.Columns.Add("Number", "№");                                // 0
        _grid.Columns.Add("Date", "Дата");                               // 1
        _grid.Columns.Add("Device", "Прибор");                           // 2
        _grid.Columns.Add("Qty", "Количество");                          // 3
        _grid.Columns.Add("Batch", "Партия");                            // 4
        _grid.Columns.Add("Time", "Время производства");                 // 5
        _grid.Columns.Add("Items", "Позиций");                           // 6
        _grid.Columns.Add("Tasks", "Нарядов");                           // 7

        int[] weights = { 5, 13, 26, 10, 7, 16, 8, 8 };
        for (var i = 0; i < weights.Length; i++)
            _grid.Columns[i].FillWeight = weights[i];

        _grid.Columns[0].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.Columns[3].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.Columns[4].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.Columns[6].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.Columns[7].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
    }

    private Control BuildButtonsPanel()
    {
        _openButton.Text = "Открыть";
        _openButton.AutoSize = true;
        _openButton.MinimumSize = new Size(110, 32);
        _openButton.Click += (_, _) => OpenSelected();

        _exportButton.Text = "Скачать заказ-наряды…";
        _exportButton.AutoSize = true;
        _exportButton.MinimumSize = new Size(210, 32);
        _exportButton.Click += (_, _) => ExportSelected();

        _deleteButton.Text = "Удалить";
        _deleteButton.AutoSize = true;
        _deleteButton.MinimumSize = new Size(110, 32);
        _deleteButton.Click += (_, _) => DeleteSelected();

        _refreshButton.Text = "Обновить";
        _refreshButton.AutoSize = true;
        _refreshButton.MinimumSize = new Size(110, 32);
        _refreshButton.Click += (_, _) => ReloadOrders();

        _closeButton.Text = "Закрыть";
        _closeButton.AutoSize = true;
        _closeButton.MinimumSize = new Size(110, 32);
        _closeButton.DialogResult = DialogResult.Cancel;

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        panel.Controls.Add(_closeButton);
        panel.Controls.Add(_refreshButton);
        panel.Controls.Add(_deleteButton);
        panel.Controls.Add(_exportButton);
        panel.Controls.Add(_openButton);

        return panel;
    }

    private Control BuildFooterPanel()
    {
        _infoLabel.AutoSize = true;
        _infoLabel.ForeColor = Color.DimGray;
        _infoLabel.Padding = new Padding(2, 4, 2, 2);
        return _infoLabel;
    }

    // ── Данные ─────────────────────────────────────────────────────────

    private void ReloadOrders()
    {
        try
        {
            _orders = _repository.LoadAll();
        }
        catch (Exception ex)
        {
            _orders = new List<Order>();
            MessageBox.Show(this,
                "Не удалось прочитать файл заказов:\n" + ex.Message,
                "Ошибка чтения XML", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        FillGrid();
    }

    /// <summary>Перестроить таблицу: свежие заказы сверху.</summary>
    private void FillGrid()
    {
        var scroll = _grid.RowCount > 0 ? _grid.FirstDisplayedScrollingRowIndex : -1;

        _grid.SuspendLayout();
        _grid.Rows.Clear();

        foreach (var order in _orders.AsEnumerable().Reverse())
            AddOrderRow(order);

        _grid.ResumeLayout();

        if (scroll >= 0 && _grid.RowCount > 0)
            _grid.FirstDisplayedScrollingRowIndex = Math.Min(scroll, _grid.RowCount - 1);

        var hasOrders = _orders.Count > 0;
        _openButton.Enabled = hasOrders;
        _exportButton.Enabled = hasOrders;
        _deleteButton.Enabled = hasOrders;

        _infoLabel.Text =
            $"Всего заказов: {_orders.Count}   •   Файл: {_repository.FilePath}\n" +
            "Двойной клик по строке — открыть карточку заказа.";
    }

    private void AddOrderRow(Order order)
    {
        var time = order.PureMinutes > 0
            ? AppFormat.FormatMinutes(order.TotalMinutes)
            : order.HasUnnormedOperations
                ? "нормировка не выполнена"
                : "нет операций";

        var index = _grid.Rows.Add(
            order.Number,
            order.CreatedAt.ToString("dd.MM.yyyy HH:mm"),
            order.Device,
            AppFormat.FormatQty(order.Quantity) + " шт",
            order.BatchSize,
            time,
            order.Items.Count,
            order.Tasks.Count);

        _grid.Rows[index].Tag = order;
    }

    private Order? SelectedOrder => _grid.CurrentRow?.Tag as Order;

    // ── Действия ───────────────────────────────────────────────────────

    /// <summary>Перейти к конкретному заказу (двойной клик / «Открыть»).</summary>
    private void OpenSelected()
    {
        if (SelectedOrder is not { } order)
        {
            MessageBox.Show(this, "Выберите заказ в таблице.",
                "Заказы", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Изменения накладных в карточке сохраняются через общий список.
        using var details = new OrderDetailsForm(order, PersistOrders);
        details.ShowDialog(this);
    }

    private void OnGridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0)
            OpenSelected();
    }

    /// <summary>
    /// Сохранить orders.xml. Вызывается и карточкой заказа (OrderDetailsForm)
    /// после изменений накладных: тот же объект Order лежит в _orders,
    /// поэтому достаточно сериализовать общий список.
    /// </summary>
    private void PersistOrders()
    {
        try
        {
            _repository.SaveAll(_orders);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Не удалось сохранить orders.xml:\n" + ex.Message,
                "Ошибка записи", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportSelected()
    {
        if (SelectedOrder is not { } order)
        {
            MessageBox.Show(this, "Выберите заказ в таблице.",
                "Заказы", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = $"Папка, в которую будет выгружен заказ №{order.Number}\n" +
                          "(наряды .docx и сводка материалов):",
            ShowNewFolderButton = true,
            SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var templatePath =
                DocxExporter.EnsureTemplate(AppDomain.CurrentDomain.BaseDirectory);
            var orderDir = OrderWriter.WriteOrderFolderFromOrder(
                dialog.SelectedPath, order, templatePath);

            var answer = MessageBox.Show(this,
                $"Заказ-наряды заказа №{order.Number} сохранены:\n" + orderDir + "\n\nОткрыть папку?",
                "Готово", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (answer == DialogResult.Yes)
                Process.Start(new ProcessStartInfo
                {
                    FileName = orderDir,
                    UseShellExecute = true
                });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Не удалось сформировать заказ-наряды:\n" + ex.Message,
                "Ошибка экспорта", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteSelected()
    {
        if (SelectedOrder is not { } order)
        {
            MessageBox.Show(this, "Выберите заказ в таблице.",
                "Заказы", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var answer = MessageBox.Show(this,
            $"Удалить заказ №{order.Number} «{order.Device}» " +
            $"({AppFormat.FormatQty(order.Quantity)} шт) из orders.xml?",
            "Удаление заказа", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
            return;

        try
        {
            _repository.Delete(order.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Не удалось удалить заказ:\n" + ex.Message,
                "Ошибка записи", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        ReloadOrders();
    }
}
