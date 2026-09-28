// ============================================================================
// RecipePickerForm.cs — диалог выбора существующего рецепта для вставки
// в состав другого рецепта (вложенные рецепты / «наследование»).
// Список фильтруется по подстроке, выбор — множественный; выбранные
// рецепты добавляются в таблицу «Состав» строками (количество 1 шт).
// ============================================================================
namespace Recipes;

public class RecipePickerForm : Form
{
    /// <summary>Выбранные имена рецептов (заполняется при подтверждении).</summary>
    public List<string> SelectedNames { get; } = new();

    private readonly List<string> _allNames;
    private readonly TextBox _filterBox = new();
    private readonly ListBox _list = new();
    private readonly Button _okButton = new();
    private readonly Button _cancelButton = new();

    public RecipePickerForm(IEnumerable<string> names)
    {
        _allNames = names.ToList();
        InitializeUi();
        ApplyFilter("");
    }

    private void InitializeUi()
    {
        Text = "Добавить деталь из существующих рецептов";
        ClientSize = new Size(440, 490);
        MinimumSize = new Size(380, 400);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        MaximizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // пояснение
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // фильтр
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // список
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // кнопки

        layout.Controls.Add(new Label
        {
            Text = "Выберите рецепты — они будут добавлены в состав как наши " +
                   "детали (количество 1 шт, поправьте при необходимости).",
            AutoSize = true,
            MaximumSize = new Size(430, 0)
        }, 0, 0);

        var filterPanel = new Panel { Dock = DockStyle.Fill, Height = 30, Padding = new Padding(0, 4, 0, 0) };
        _filterBox.Dock = DockStyle.Fill;
        _filterBox.PlaceholderText = "Поиск по названию…";
        _filterBox.TextChanged += (_, _) => ApplyFilter(_filterBox.Text);
        filterPanel.Controls.Add(_filterBox);
        layout.Controls.Add(filterPanel, 0, 1);

        _list.Dock = DockStyle.Fill;
        _list.IntegralHeight = false;
        _list.SelectionMode = SelectionMode.MultiExtended;
        _list.SelectedIndexChanged += (_, _) =>
            _okButton.Enabled = _list.SelectedIndices.Count > 0;
        _list.DoubleClick += (_, _) =>
        {
            if (_list.SelectedIndices.Count > 0)
            {
                SaveSelection();
                DialogResult = DialogResult.OK;
            }
        };
        layout.Controls.Add(_list, 0, 2);

        _okButton.Text = "Добавить";
        _okButton.AutoSize = true;
        _okButton.MinimumSize = new Size(120, 30);
        _okButton.Enabled = false;
        _okButton.Click += (_, _) =>
        {
            SaveSelection();
            DialogResult = DialogResult.OK;
        };

        _cancelButton.Text = "Отмена";
        _cancelButton.AutoSize = true;
        _cancelButton.MinimumSize = new Size(100, 30);
        _cancelButton.DialogResult = DialogResult.Cancel;

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0)
        };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_okButton);
        layout.Controls.Add(buttons, 0, 3);

        Controls.Add(layout);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }

    private void ApplyFilter(string filter)
    {
        // Выделение стараемся сохранить между сменами фильтра.
        var selected = new HashSet<string>(
            _list.SelectedItems.Cast<string>(), StringComparer.Ordinal);

        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var name in _allNames)
        {
            if (filter.Length == 0 ||
                name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0)
            {
                _list.Items.Add(name);
                if (selected.Contains(name))
                    _list.SelectedItems.Add(name);
            }
        }

        _list.EndUpdate();
        _okButton.Enabled = _list.SelectedIndices.Count > 0;
    }

    private void SaveSelection()
    {
        SelectedNames.Clear();
        SelectedNames.AddRange(_list.SelectedItems.Cast<string>()
            .OrderBy(n => n, StringComparer.CurrentCulture));
    }
}
