using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Интерпретатор_машины_Тьюринга
{
    public partial class Form1
    {
        // ──────────────────────────────────────────────────────────
        // Поля таблицы состояний
        // ──────────────────────────────────────────────────────────
        private DataGridViewCell currentStepCell = null;
        private DataGridView stateTable;
        private BindingList<TuringState> states = new BindingList<TuringState>();
        private Dictionary<string, Dictionary<string, string>> tableData = new Dictionary<string, Dictionary<string, string>>();
        private const int StateTableCellWidth = 80;
        private const int CommentBoxWidth = 200;
        private const string FileFilter = "Файлы машины Тьюринга (*.turing)|*.turing|Все файлы (*.*)|*.*";

        // ──────────────────────────────────────────────────────────
        // Создание таблицы состояний
        // ──────────────────────────────────────────────────────────
        private void CreateStateTable()
        {
            stateTable = new DataGridView
            {
                Top = 180,
                Left = 20,
                Width = Math.Max(500, ClientSize.Width - 270),
                Height = ClientSize.Height - 300,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                ScrollBars = ScrollBars.Both,
                EditMode = DataGridViewEditMode.EditOnKeystroke,
                BackgroundColor = ControlBackColor,
                BorderStyle = BorderStyle.Fixed3D,
                GridColor = Color.LightGray,
                AllowUserToOrderColumns = false,
                AllowUserToResizeColumns = false,
                AllowUserToResizeRows = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false,
                MultiSelect = false,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = ControlBackColor,
                    SelectionBackColor = Color.FromArgb(230, 230, 250),
                    ForeColor = SystemColors.ControlText,
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    Font = MainFont
                },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = HeaderColor,
                    ForeColor = Color.White,
                    Font = HeaderFont,
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                }
            };

            var stateColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "Состояние",
                Name = "State",
                ReadOnly = true,
                Width = StateTableCellWidth + 10,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Frozen = true
            };
            stateTable.Columns.Add(stateColumn);
            stateTable.CellPainting += StateTable_CellPainting;
            stateTable.SelectionChanged += (sender, e) =>
            {
                stateTable.Invalidate();
                UpdateButtonsState();
            };
            stateTable.CellClick += (sender, e) =>
            {
                if (e.RowIndex >= 0 && e.RowIndex < stateTable.Rows.Count)
                {
                    stateTable.Rows[e.RowIndex].Selected = true;
                    UpdateButtonsState();
                }
            };
            stateTable.CellBeginEdit += StateTable_CellBeginEdit;
            stateTable.CellEndEdit += StateTable_CellEndEdit;
            stateTable.DataError += (sender, e) => e.ThrowException = false;
            this.Controls.Add(stateTable);
            UpdateStateTableColumns();
            UpdateStateTable();
        }

        // ──────────────────────────────────────────────────────────
        // Обновление таблицы состояний
        // ──────────────────────────────────────────────────────────
        private void UpdateStateTable()
        {
            if (stateTable == null) return;
            try
            {
                stateTable.SuspendLayout();
                stateTable.Rows.Clear();
                foreach (var state in states)
                {
                    if (state == null) continue;
                    int rowIndex = stateTable.Rows.Add();
                    stateTable.Rows[rowIndex].Cells["State"].Value = state.Name;
                }
            }
            finally
            {
                stateTable.ResumeLayout();
            }
        }

        private void UpdateStateTableColumns()
        {
            if (stateTable == null || alphabetTextBox == null) return;
            var savedData = SaveTableData();
            while (stateTable.Columns.Count > 1)
                stateTable.Columns.RemoveAt(1);

            var alphabet = new HashSet<char>();
            foreach (char c in alphabetTextBox.Text)
            {
                if (c != EmptyCellSymbol)
                    alphabet.Add(c);
            }
            alphabet.Add(EmptyCellSymbol);

            foreach (char symbol in alphabet.OrderBy(c => c))
            {
                var col = new DataGridViewTextBoxColumn
                {
                    HeaderText = symbol.ToString(),
                    Name = symbol.ToString(),
                    Width = StateTableCellWidth
                };
                stateTable.Columns.Add(col);
            }
            RestoreTableData(savedData);
        }

        // ──────────────────────────────────────────────────────────
        // CRUD состояний
        // ──────────────────────────────────────────────────────────
        private void AddStateBefore()
        {
            this.ActiveControl = null;
            if (stateTable.SelectedRows.Count == 0) return;
            int selectedRow = stateTable.SelectedRows[0].Index;
            int selectedCol = stateTable.CurrentCell?.ColumnIndex ?? 0;
            var savedData = SaveTableData();
            states.Insert(selectedRow, new TuringState { Name = $"q{states.Count}" });
            RenumberStates();
            UpdateStateTable();
            var newSavedData = new Dictionary<string, Dictionary<string, string>>();
            foreach (var kvp in savedData)
            {
                int oldNum = int.Parse(kvp.Key.Substring(1));
                newSavedData[$"q{(oldNum >= selectedRow ? oldNum + 1 : oldNum)}"] = kvp.Value;
            }
            RestoreTableData(newSavedData);
            if (stateTable.Rows.Count > selectedRow + 1)
                stateTable.CurrentCell = stateTable[selectedCol, selectedRow];
            UpdateButtonsState();
        }

        private void AddStateAfter()
        {
            this.ActiveControl = null;
            if (stateTable.SelectedRows.Count == 0) return;
            int selectedRow = stateTable.SelectedRows[0].Index;
            int selectedCol = stateTable.CurrentCell?.ColumnIndex ?? 0;
            var savedData = SaveTableData();
            states.Insert(selectedRow + 1, new TuringState { Name = $"q{states.Count}" });
            RenumberStates();
            UpdateStateTable();
            var newSavedData = new Dictionary<string, Dictionary<string, string>>();
            foreach (var kvp in savedData)
            {
                int oldNum = int.Parse(kvp.Key.Substring(1));
                newSavedData[$"q{(oldNum > selectedRow ? oldNum + 1 : oldNum)}"] = kvp.Value;
            }
            RestoreTableData(newSavedData);
            if (stateTable.Rows.Count > selectedRow + 1)
                stateTable.CurrentCell = stateTable[selectedCol, selectedRow];
            UpdateButtonsState();
        }

        private void AddStateLast()
        {
            this.ActiveControl = null;
            if (stateTable.SelectedRows.Count == 0) return;
            int selectedRow = stateTable.SelectedRows[0].Index;
            int selectedCol = stateTable.CurrentCell?.ColumnIndex ?? 0;
            string selectedState = states[selectedRow].Name;
            var savedData = SaveTableData();
            states.Add(new TuringState { Name = $"q{states.Count}" });
            UpdateStateTable();
            RestoreTableData(savedData);
            if (stateTable.Rows.Count > selectedRow)
            {
                for (int i = 0; i < stateTable.Rows.Count; i++)
                {
                    if (stateTable.Rows[i].Cells["State"].Value?.ToString() == selectedState)
                    {
                        stateTable.CurrentCell = stateTable[selectedCol, i];
                        stateTable.Rows[i].Selected = true;
                        break;
                    }
                }
            }
            UpdateButtonsState();
        }

        private void RemoveState()
        {
            this.ActiveControl = null;
            if (stateTable.SelectedRows.Count == 0 || states.Count <= 1) return;
            int selectedRow = stateTable.SelectedRows[0].Index;
            int selectedCol = stateTable.CurrentCell?.ColumnIndex ?? 0;
            bool wasInitial = states[selectedRow].IsInitial;
            var savedData = SaveTableData();
            savedData.Remove(states[selectedRow].Name);
            states.RemoveAt(selectedRow);
            RenumberStates();
            if (wasInitial && states.Count > 0)
                states[0].IsInitial = true;
            UpdateStateTable();
            var newSavedData = new Dictionary<string, Dictionary<string, string>>();
            foreach (var kvp in savedData)
            {
                int oldNum = int.Parse(kvp.Key.Substring(1));
                newSavedData[$"q{(oldNum > selectedRow ? oldNum - 1 : oldNum)}"] = kvp.Value;
            }
            RestoreTableData(newSavedData);
            if (stateTable.Rows.Count > 0)
            {
                int newRow = Math.Min(selectedRow, stateTable.Rows.Count - 1);
                stateTable.CurrentCell = stateTable[selectedCol, newRow];
            }
            UpdateButtonsState();
        }

        private void RenumberStates()
        {
            for (int i = 0; i < states.Count; i++)
                states[i].Name = $"q{i}";
        }

        // ──────────────────────────────────────────────────────────
        // Сохранение и восстановление данных таблицы
        // ──────────────────────────────────────────────────────────
        private void RestoreTableData(Dictionary<string, Dictionary<string, string>> dataToRestore)
        {
            foreach (DataGridViewRow row in stateTable.Rows)
            {
                if (row.IsNewRow) continue;
                string state = row.Cells["State"].Value?.ToString();
                if (string.IsNullOrEmpty(state) || !dataToRestore.ContainsKey(state)) continue;
                foreach (var kvp in dataToRestore[state])
                {
                    if (stateTable.Columns.Contains(kvp.Key))
                        row.Cells[kvp.Key].Value = kvp.Value;
                }
            }
        }

        private Dictionary<string, Dictionary<string, string>> SaveTableData()
        {
            var savedData = new Dictionary<string, Dictionary<string, string>>();
            foreach (DataGridViewRow row in stateTable.Rows)
            {
                if (row.IsNewRow) continue;
                string state = row.Cells["State"].Value?.ToString();
                if (string.IsNullOrEmpty(state)) continue;
                savedData[state] = new Dictionary<string, string>();
                foreach (DataGridViewCell cell in row.Cells)
                {
                    if (cell.OwningColumn != null && cell.OwningColumn.Name != "State")
                        savedData[state][cell.OwningColumn.Name] = cell.Value?.ToString();
                }
            }
            return savedData;
        }

        private bool HasAnyRules()
        {
            foreach (DataGridViewRow row in stateTable.Rows)
            {
                if (row.IsNewRow) continue;
                foreach (DataGridViewCell cell in row.Cells)
                {
                    if (cell.OwningColumn.Name != "State" && !string.IsNullOrEmpty(cell.Value?.ToString()))
                        return true;
                }
            }
            return false;
        }

        // ──────────────────────────────────────────────────────────
        // Валидация и форматирование ввода ячеек
        // ──────────────────────────────────────────────────────────
        private bool ValidateCellInput(string input, out string errorMessage)
        {
            errorMessage = "";
            input = input?.Trim() ?? "";

            if (input == "0>qF") return true;

            if (input.Length < 2)
            {
                errorMessage = "Формат: символ направление [состояние] (например: 1> или 1>q0)\n" +
                               "Для конечного состояния используйте: 0>qF";
                return false;
            }

            char symbol = input[0];
            if (!(alphabetTextBox.Text + EmptyCellSymbol).Contains(symbol))
            {
                errorMessage = $"Символ '{symbol}' отсутствует в алфавите!\n" +
                               $"Допустимые символы: {alphabetTextBox.Text}{(alphabetTextBox.Text.Contains("_") ? "" : ", _")}";
                return false;
            }

            char direction = input[1];
            if ("<>.".IndexOf(direction) == -1)
            {
                errorMessage = "Направление должно быть:\n" +
                               "< или ← (влево)\n" +
                               "> или → (вправо)\n" +
                               ". или • (на месте)";
                return false;
            }

            if (input.Length > 2)
            {
                string stateRef = input.Substring(2).Trim();
                if (stateRef != "qF" && (!stateRef.StartsWith("q") || !states.Any(s => s.Name == stateRef)))
                {
                    errorMessage = $"Неизвестное состояние '{stateRef}'\n" +
                                   $"Допустимые состояния: {string.Join(", ", states.Select(s => s.Name))}" +
                                   (states.Any(s => s.Name == "qF") ? "" : "\nДля конечного состояния используйте qF");
                    return false;
                }
            }
            return true;
        }

        // ──────────────────────────────────────────────────────────
        // Обработчики событий таблицы состояний
        // ──────────────────────────────────────────────────────────
        private void StateTable_SelectionChanged(object sender, EventArgs e)
        {
            UpdateButtonsState();
        }

        private void StateTable_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (e.ColumnIndex > 0)
            {
                var cell = stateTable.Rows[e.RowIndex].Cells[e.ColumnIndex];
                string displayValue = cell.Value?.ToString() ?? "";
                cell.Value = DisplayToEditableFormat(displayValue);
            }
        }

        private void StateTable_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (stateTable.SelectedRows.Count == 0) return;
            int selectedRow = stateTable.SelectedRows[0].Index;
            int selectedCol = stateTable.CurrentCell?.ColumnIndex ?? 0;

            if (e.ColumnIndex > 0)
            {
                var cell = stateTable.Rows[e.RowIndex].Cells[e.ColumnIndex];
                string input = cell.Value?.ToString() ?? "";

                if (ValidateCellInput(input, out string error))
                    cell.Value = EditToDisplayFormat(input);
                else
                {
                    cell.Value = "";
                    if (!string.IsNullOrEmpty(error))
                        MessageBox.Show(error);
                }
            }

            if (selectedRow >= 0 && stateTable.Rows.Count > selectedRow)
                stateTable.CurrentCell = stateTable[selectedCol, selectedRow];
        }
    }
}
