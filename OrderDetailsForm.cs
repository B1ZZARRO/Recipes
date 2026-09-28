// ============================================================================
// OrderDetailsForm.cs — карточка конкретного заказа из orders.xml.
//
// Верхняя таблица «Позиции заказа» — позиции + накладные. Два столбца:
// «Накладная (PDF)» и «Единиц по накладной». Первая накладная прикрепляется
// прямо в строке позиции; если указано единиц МЕНЬШЕ, чем количества
// позиции (столбец слева), таблица «сдвигается» — под строкой появляется
// пустая строка, где можно добавить ещё одну накладную, и так пока сумма
// единиц не станет >= количества. Пустая ячейка единиц правило переноса
// не запускает. Прикрепление PDF — заглушка (файл не сохраняется; при
// переходе с XML на БД вложения переедут в базу).
//
// Нижняя таблица — наряды по партиям с временем; предупреждение о
// ненормированных операциях; выгрузка нарядов .docx.
// ============================================================================
using System.Diagnostics;
using Recipes.Models;
using Recipes.Services;

namespace Recipes;

public class OrderDetailsForm : Form
{
    // Индексы столбцов накладных в таблице позиций (последние два).
    private const int ColPdf = 4;
    private const int ColUnits = 5;

    private readonly Order _order;

    /// <summary>
    /// Сохранение orders.xml после изменений накладных (делегат владельца —
    /// списка заказов). null — карточка открыта только для просмотра.
    /// </summary>
    private readonly Action? _onPersist;

    private readonly DataGridView _itemsGrid = new();
    private readonly DataGridView _tasksGrid = new();
    private readonly Label _warningLabel = new();
    private readonly Button _exportButton = new();
    private readonly Button _closeButton = new();

    /// <summary>Метка строки таблицы позиций: строка позиции или накладной.</summary>
    private sealed class RowTag
    {
        public OrderItem Item { get; init; } = null!;
        public OrderInvoice? Invoice { get; init; }
        public bool IsItemRow { get; init; }
    }

    public OrderDetailsForm(Order order, Action? onPersist = null)
    {
        _order = order;
        _onPersist = onPersist;
        InitializeUi();
        FillData();
    }

    // ── Интерфейс ──────────────────────────────────────────────────────

    private void InitializeUi()
    {
        Text = $"Заказ №{_order.Number} — {_order.Device} — Recipes";
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(1000, 680);
        MinimumSize = new Size(820, 520);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(10)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // шапка
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // предупреждение
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));   // позиции + накладные
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));   // наряды
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // кнопки

        root.Controls.Add(BuildHeaderPanel(), 0, 0);
        root.Controls.Add(BuildWarningPanel(), 0, 1);
        root.Controls.Add(BuildGridGroup(
            "Позиции заказа и накладные", _itemsGrid, ConfigureItemsGrid, ItemsHint), 0, 2);
        root.Controls.Add(BuildGridGroup(
            "Наряды по партиям (время — как в заказ-наряде)", _tasksGrid, ConfigureTasksGrid), 0, 3);
        root.Controls.Add(BuildButtonsPanel(), 0, 4);

        Controls.Add(root);

        CancelButton = _closeButton;
    }

    private static string ItemsHint =>
        "Накладные: клик по ячейке «Накладная (PDF)» — прикрепить (пока заглушка); " +
        "укажите единицы — пока их сумма меньше количества позиции, ниже появляется " +
        "строка для следующей накладной.";

    private Control BuildHeaderPanel()
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

        var title = new Label
        {
            Text = $"Заказ №{_order.Number}   от {_order.CreatedAt:dd.MM.yyyy HH:mm}",
            AutoSize = true,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Padding = new Padding(2, 0, 2, 4)
        };

        var device = new Label
        {
            Text = $"Прибор: {_order.Device}      " +
                   $"Количество: {AppFormat.FormatQty(_order.Quantity)} шт      " +
                   $"Размер партии: {_order.BatchSize} шт",
            AutoSize = true,
            Padding = new Padding(2, 0, 2, 2)
        };

        var time = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            Padding = new Padding(2, 0, 2, 2)
        };
        if (_order.PureMinutes > 0)
            time.Text = $"Общее время производства: " +
                        $"{AppFormat.FormatMinutes(_order.TotalMinutes)}  ({_order.TotalMinutes} мин)";
        else if (_order.HasUnnormedOperations)
            time.Text = "Общее время производства: нормировка не выполнена";
        else
            time.Text = "В дереве рецептов нет операций.";

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(device, 0, 1);
        layout.Controls.Add(time, 0, 2);

        return layout;
    }

    private Control BuildWarningPanel()
    {
        _warningLabel.AutoSize = true;
        _warningLabel.ForeColor = Color.FromArgb(176, 96, 0); // тёмно-оранжевый
        _warningLabel.Padding = new Padding(2, 2, 2, 4);
        _warningLabel.Visible = false;
        return _warningLabel;
    }

    private Control BuildGridGroup(
        string title, DataGridView grid, Action<DataGridView> configure,
        string? hint = null)
    {
        configure(grid);

        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill
        };

        if (string.IsNullOrEmpty(hint))
        {
            grid.Dock = DockStyle.Fill;
            group.Controls.Add(grid);
            return group;
        }

        // Сетка + серая подсказка про накладные под ней.
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        grid.Dock = DockStyle.Fill;
        panel.Controls.Add(grid, 0, 0);
        panel.Controls.Add(new Label
        {
            Text = hint,
            AutoSize = true,
            ForeColor = Color.DimGray,
            Padding = new Padding(2, 4, 2, 2)
        }, 0, 1);

        group.Controls.Add(panel);
        return group;
    }

    private void ConfigureItemsGrid(DataGridView grid)
    {
        ConfigureEditableGrid(grid);

        grid.CellClick += OnItemsCellClick;
        grid.CellEndEdit += OnItemsCellEndEdit;

        grid.Columns.Add("Kind", "Тип");                                 // 0
        grid.Columns.Add("Name", "Наименование");                        // 1
        grid.Columns.Add("Qty", "Количество");                           // 2
        grid.Columns.Add("Unit", "Ед. изм.");                            // 3
        grid.Columns.Add("Invoice", "Накладная (PDF)");                  // 4
        grid.Columns.Add("Units", "Единиц по накладной");                // 5

        int[] weights = { 14, 34, 10, 8, 22, 12 };
        for (var i = 0; i < weights.Length; i++)
            grid.Columns[i].FillWeight = weights[i];

        grid.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        grid.Columns[ColUnits].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

        grid.Columns[ColPdf].ToolTipText =
            "Клик по ячейке — прикрепить PDF накладной к позиции (пока заглушка).";
        grid.Columns[ColUnits].ToolTipText =
            "Сколько единиц позиции покрывает накладная. Если меньше, чем количество " +
            "позиции слева, ниже появится строка для следующей накладной.";
    }

    private void ConfigureTasksGrid(DataGridView grid)
    {
        ConfigureBaseGrid(grid);

        grid.Columns.Add("Number", "№");
        grid.Columns.Add("Detail", "Деталь");
        grid.Columns.Add("Batch", "Партия");
        grid.Columns.Add("Qty", "Кол-во, шт");
        grid.Columns.Add("Materials", "Материалов");
        grid.Columns.Add("Time", "Время партии (Tисп + Tнез)");
        grid.Columns.Add("NonHuman", "Без человека (Tнез)");

        grid.Columns[0].FillWeight = 6;
        grid.Columns[1].FillWeight = 26;
        grid.Columns[2].FillWeight = 11;
        grid.Columns[3].FillWeight = 12;
        grid.Columns[4].FillWeight = 12;
        grid.Columns[5].FillWeight = 20;
        grid.Columns[6].FillWeight = 13;

        grid.Columns[0].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        grid.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        grid.Columns[3].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        grid.Columns[4].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

        grid.Columns[5].ToolTipText =
            "Общий срок выполнения партии, как в строке «Срок выполнения» заказ-наряда: " +
            "время исполнителя (с надбавкой) плюс время задержек без участия человека.";
        grid.Columns[6].ToolTipText =
            "Задержка на процессы без участия человека (застывание, сушка и т.п.) — " +
            "по одному разу на каждую операцию партии.";
    }

    private static void ConfigureBaseGrid(DataGridView grid)
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
    }

    /// <summary>Таблица позиций: редактируются только ячейки единиц накладных.</summary>
    private static void ConfigureEditableGrid(DataGridView grid)
    {
        ConfigureBaseGrid(grid);
        grid.ReadOnly = false;
        grid.MultiSelect = false;
    }

    private Control BuildButtonsPanel()
    {
        _exportButton.Text = "Скачать заказ-наряды…";
        _exportButton.AutoSize = true;
        _exportButton.MinimumSize = new Size(210, 32);
        _exportButton.Click += (_, _) => ExportOrder();

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
        panel.Controls.Add(_exportButton);

        return panel;
    }

    // ── Данные ─────────────────────────────────────────────────────────

    private void FillData()
    {
        FillItems();
        FillTasks();

        var warning = BuildWarningText();
        if (warning.Length > 0)
        {
            _warningLabel.Text = warning;
            _warningLabel.Visible = true;
        }
    }

    /// <summary>
    /// Перестроить таблицу позиций. На каждую позицию — строка позиции
    /// (первая накладная — прямо в ней) плюс строки накладных 2..N; пока
    /// суммарное покрытие меньше количества позиции, ниже стоит пустая
    /// строка для новой накладной (таблица «сдвигается» вниз на ячейку).
    /// </summary>
    private void FillItems(OrderItem? focusItem = null)
    {
        var scroll = _itemsGrid.RowCount > 0
            ? _itemsGrid.FirstDisplayedScrollingRowIndex : -1;

        _itemsGrid.SuspendLayout();
        _itemsGrid.Rows.Clear();

        foreach (var item in _order.Items)
        {
            AddItemRow(item);

            // Заполненные строки накладных 2..N.
            for (var i = 1; i < item.Invoices.Count; i++)
                AddInvoiceRow(item, i);

            // Пустая строка под следующую накладную, пока позиция не покрыта.
            if (item.Invoices.Count >= 1 && !item.IsFullyInvoiced)
                AddInvoiceRow(item, item.Invoices.Count);
        }

        _itemsGrid.ResumeLayout();

        if (scroll >= 0 && _itemsGrid.RowCount > 0)
            _itemsGrid.FirstDisplayedScrollingRowIndex =
                Math.Min(scroll, _itemsGrid.RowCount - 1);

        if (focusItem is not null)
        {
            // Фокус — на последнюю строку накладных этой позиции.
            var row = _itemsGrid.Rows.Cast<DataGridViewRow>().LastOrDefault(r =>
                r.Tag is RowTag t && t.Item == focusItem);
            if (row is not null)
                _itemsGrid.CurrentCell = row.Cells[ColUnits];
        }
    }

    private void FillTasks()
    {
        _tasksGrid.Rows.Clear();
        foreach (var task in _order.Tasks)
        {
            var time = DocxExporter.FormatExpectedDeadlineTime(task);
            var nonHuman = task.NonHumanMinutes > 0
                ? AppFormat.FormatMinutes(task.NonHumanMinutes)
                : "";
            _tasksGrid.Rows.Add(
                task.Number,
                task.DetailName,
                $"{task.BatchIndex} из {task.BatchCount}",
                AppFormat.FormatQty(task.BatchQty),
                task.Materials.Count,
                time,
                nonHuman);
        }
    }

    private static string InvoiceCellText(OrderInvoice? invoice)
    {
        if (invoice is null)
            return "(клик — прикрепить PDF)";
        return invoice.FileName.Length > 0 ? invoice.FileName : "(не прикреплено)";
    }

    /// <summary>Строка позиции: данные позиции + первая накладная (если есть).</summary>
    private void AddItemRow(OrderItem item)
    {
        var invoice = item.Invoices.Count > 0 ? item.Invoices[0] : null;

        var index = _itemsGrid.Rows.Add(
            item.KindText,
            item.Name,
            AppFormat.FormatQty(item.Qty),
            item.Unit,
            InvoiceCellText(invoice),
            invoice is not null && invoice.Units > 0 ? AppFormat.FormatQty(invoice.Units) : "");

        var row = _itemsGrid.Rows[index];
        row.Tag = new RowTag { Item = item, Invoice = invoice, IsItemRow = true };

        MakeInvoiceCellsEditable(row);

        if (invoice is not null && invoice.FileName.Length > 0)
            row.Cells[ColPdf].ToolTipText =
                "Заглушка: PDF-файл не сохраняется (до перехода на БД). Клик — заменить.";

        // Покрытие накладными — цветом в ячейке единиц первой накладной.
        if (item.Invoices.Count > 0)
        {
            row.Cells[ColUnits].ToolTipText =
                $"Накладных: {item.Invoices.Count}; покрыто " +
                $"{AppFormat.FormatQty(item.InvoicedUnits)} из " +
                $"{AppFormat.FormatQty(item.Qty)} {item.Unit}.";
            row.Cells[ColUnits].Style.ForeColor = item.IsFullyInvoiced
                ? Color.FromArgb(0, 110, 64)
                : Color.FromArgb(176, 96, 0);
            row.Cells[ColUnits].Style.Font = new Font(_itemsGrid.Font, FontStyle.Bold);
        }
    }

    /// <summary>Строка накладной №invoiceIndex+1; index == count → пустая строка.</summary>
    private void AddInvoiceRow(OrderItem item, int invoiceIndex)
    {
        var invoice = invoiceIndex < item.Invoices.Count
            ? item.Invoices[invoiceIndex]
            : null;
        var label = $"↳ накладная {invoiceIndex + 1}" + (invoice is null ? " — новая" : "");

        var index = _itemsGrid.Rows.Add(
            "",
            label,
            AppFormat.FormatQty(item.Qty),
            item.Unit,
            InvoiceCellText(invoice),
            invoice is not null && invoice.Units > 0 ? AppFormat.FormatQty(invoice.Units) : "");

        var row = _itemsGrid.Rows[index];
        row.Tag = new RowTag { Item = item, Invoice = invoice, IsItemRow = false };

        MakeInvoiceCellsEditable(row);

        if (invoice is null)
            row.DefaultCellStyle.ForeColor = Color.DimGray;
    }

    /// <summary>Данные позиции — только чтение; ячейки накладных — рабочие.</summary>
    private static void MakeInvoiceCellsEditable(DataGridViewRow row)
    {
        for (var c = 0; c < ColPdf; c++)
            row.Cells[c].ReadOnly = true;
        row.Cells[ColPdf].ReadOnly = true;    // прикрепление — по клику
        row.Cells[ColUnits].ReadOnly = false; // ввод единиц
    }

    private List<string> GetUnnormedLines()
    {
        if (_order.UnnormedOperations.Count > 0)
            return _order.UnnormedOperations;

        // Страховка: восстановить из снимка нарядов.
        return _order.Tasks
            .Where(t => t.HasUntimedOperations)
            .GroupBy(t => t.DetailName)
            .Select(g => g.Key + ": " + string.Join("; ",
                g.SelectMany(t => t.Operations)
                    .Where(o => o.IsUntimed)
                    .Select(o => o.Description)
                    .Distinct()))
            .ToList();
    }

    private string BuildWarningText()
    {
        var lines = GetUnnormedLines();
        if (lines.Count == 0)
            return "";

        return "Нормировка не выполнена для операций (в расчёт времени они не вошли):" +
               Environment.NewLine +
               string.Join(Environment.NewLine, lines.Select(l => "  • " + l));
    }

    // ── Действия ───────────────────────────────────────────────────────

    /// <summary>Клик по ячейке «Накладная (PDF)» — прикрепить файл (заглушка).</summary>
    private void OnItemsCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != ColPdf)
            return;
        if (_itemsGrid.Rows[e.RowIndex].Tag is not RowTag tag)
            return;

        var item = tag.Item;
        var isNew = tag.Invoice is null;

        var message = "ЗАГЛУШКА загрузки PDF.\n\n" +
                      "Здесь будет выбор PDF-файла накладной. Пока файл не сохраняется — " +
                      "при переходе с XML на отдельную БД вложения будут храниться в базе.\n\n" +
                      (isNew
                          ? $"Прикрепить накладную к позиции «{item.Name}» (заглушка)?"
                          : $"Текущая накладная: «{tag.Invoice!.FileName}».\nПрикрепить заново (заглушка)?");

        var answer = MessageBox.Show(this, message,
            "Накладная (PDF) — заглушка",
            MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer != DialogResult.Yes)
            return;

        var stubName =
            $"накладная_{_order.Number:000}" +
            $"_поз{Math.Max(1, _order.Items.IndexOf(item) + 1)}" +
            $"_{DateTime.Now:yyyyMMdd_HHmm}.pdf";

        BeginInvoke(new Action(() =>
        {
            if (tag.Invoice is null)
                item.Invoices.Add(new OrderInvoice { FileName = stubName, Units = 0 });
            else
                tag.Invoice.FileName = stubName;

            Persist();
            FillItems(item);
        }));
    }

    /// <summary>
    /// Ввод единиц по накладной. Пустая ячейка правило переноса вниз не
    /// запускает (у заполненной накладной очистка единиц удаляет её);
    /// число меньше остатка количества → появляется строка под следующую
    /// накладную, пока сумма не станет &gt;= количества позиции.
    /// </summary>
    private void OnItemsCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != ColUnits)
            return;
        if (_itemsGrid.Rows[e.RowIndex].Tag is not RowTag tag)
            return;

        var item = tag.Item;
        var text = _itemsGrid.Rows[e.RowIndex].Cells[ColUnits].Value?.ToString()?.Trim() ?? "";

        if (text.Length == 0)
        {
            if (tag.Invoice is null)
                return; // пустая ячейка — ничего не делаем

            BeginInvoke(new Action(() =>
            {
                item.Invoices.Remove(tag.Invoice!);
                Persist();
                FillItems(item);
            }));
            return;
        }

        if (!AppFormat.TryParseDouble(text, out var value) || value <= 0)
        {
            MessageBox.Show(this,
                "Введите число больше нуля — сколько единиц позиции покрывает накладная " +
                "(например 40 или 1,5).",
                "Единицы по накладной", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            BeginInvoke(new Action(() => FillItems(item))); // откат значения
            return;
        }

        BeginInvoke(new Action(() =>
        {
            if (tag.Invoice is null)
                item.Invoices.Add(new OrderInvoice { FileName = "", Units = value });
            else
                tag.Invoice.Units = value;

            Persist();
            FillItems(item);
        }));
    }

    /// <summary>Сохранить orders.xml (через делегат владельца списка заказов).</summary>
    private void Persist()
    {
        try
        {
            _onPersist?.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Не удалось сохранить orders.xml:\n" + ex.Message,
                "Ошибка записи", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportOrder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = $"Папка, в которую будет выгружен заказ №{_order.Number}\n" +
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
                dialog.SelectedPath, _order, templatePath);

            var answer = MessageBox.Show(this,
                $"Заказ-наряды заказа №{_order.Number} сохранены:\n" + orderDir + "\n\nОткрыть папку?",
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
}
