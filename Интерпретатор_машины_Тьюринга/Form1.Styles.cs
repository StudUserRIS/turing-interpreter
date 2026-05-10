using System;
using System.Drawing;
using System.Windows.Forms;

namespace Интерпретатор_машины_Тьюринга
{
    public partial class Form1
    {
        // ──────────────────────────────────────────────────────────
        // Цвета и шрифты (константы темы)
        // ──────────────────────────────────────────────────────────
        private readonly Color MainBackColor    = Color.FromArgb(240, 240, 240);
        private readonly Color ControlBackColor = Color.White;
        private readonly Color HeaderColor      = Color.FromArgb(51, 51, 76);
        private readonly Color AccentColor      = Color.FromArgb(0, 122, 204);
        private readonly System.Drawing.Font MainFont   = new System.Drawing.Font("Segoe UI", 9);
        private readonly System.Drawing.Font HeaderFont = new System.Drawing.Font("Segoe UI", 10, FontStyle.Bold);

        // ──────────────────────────────────────────────────────────
        // Стили кнопок
        // ──────────────────────────────────────────────────────────
        private void ApplyTextButtonStyle(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Color.Transparent;
            button.ForeColor = SystemColors.ControlText;
            button.Font = new System.Drawing.Font("Segoe UI", 9, FontStyle.Regular);
            button.Cursor = Cursors.Hand;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.FlatAppearance.MouseOverBackColor  = Color.FromArgb(20, 0, 0, 0);
            button.FlatAppearance.MouseDownBackColor  = Color.FromArgb(40, 0, 0, 0);
        }

        // ──────────────────────────────────────────────────────────
        // Стили диалоговых окон
        // ──────────────────────────────────────────────────────────
        private void ApplyDialogStyle(Form dlg)
        {
            dlg.BackColor = Color.WhiteSmoke;
            dlg.Font = new Font("Segoe UI", 9);
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
            dlg.MaximizeBox = false;
            dlg.MinimizeBox = false;
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.ShowInTaskbar = false;
        }

        private void ApplyDialogButtonStyle(Button btn)
        {
            btn.FlatStyle = FlatStyle.Standard;
            btn.Font = new Font("Segoe UI", 9);
            btn.UseVisualStyleBackColor = true;
            btn.BackColor = SystemColors.Control;
            btn.ForeColor = SystemColors.ControlText;
            btn.Height = 28;
        }

        // ──────────────────────────────────────────────────────────
        // Единый стиль таблиц DataGridView
        //
        // Синяя рамка-индикатор выбранной ячейки/строки рисуется ТОЛЬКО когда
        // в таблице действительно есть пользовательский выбор
        // (SelectedRows / SelectedCells), а НЕ просто когда WinForms сам
        // выставил CurrentCell на (0,0) после Rows.Add.
        //
        // Это закрывает требование: при открытии окна ни одна ячейка не
        // должна быть выделена синей рамкой автоматически. То же относится
        // к перестройке таблицы поиском/фильтром: если фильтр снимает выбор
        // визуально (через ClearSelection), то синяя рамка тоже должна
        // исчезнуть, даже если CurrentCell ещё не успел стать null.
        // ──────────────────────────────────────────────────────────
        private void ApplyModernTableStyle(DataGridView dgv)
        {
            dgv.DefaultCellStyle.SelectionBackColor = Color.White;
            dgv.DefaultCellStyle.SelectionForeColor = Color.Black;
            dgv.EnableHeadersVisualStyles = false;
            dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = dgv.ColumnHeadersDefaultCellStyle.BackColor;
            dgv.ColumnHeadersDefaultCellStyle.SelectionForeColor = dgv.ColumnHeadersDefaultCellStyle.ForeColor;

            dgv.CellPainting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                e.Paint(e.CellBounds, DataGridViewPaintParts.All
                    & ~DataGridViewPaintParts.Border
                    & ~DataGridViewPaintParts.SelectionBackground);

                // Рисуем синюю рамку только если ячейка/строка реально выбрана
                // пользователем (SelectedRows/SelectedCells непусты).
                // CurrentCell сам по себе НЕ является признаком выбора —
                // WinForms автоматически выставляет его в (0,0) после
                // Rows.Add, и это не должно приводить к появлению
                // синей рамки.
                bool hasSelection = false;
                try
                {
                    if (dgv.CurrentCell != null
                        && dgv.CurrentCell.RowIndex == e.RowIndex
                        && dgv.CurrentCell.ColumnIndex == e.ColumnIndex)
                    {
                        if (dgv.SelectionMode == DataGridViewSelectionMode.FullRowSelect
                            || dgv.SelectionMode == DataGridViewSelectionMode.RowHeaderSelect)
                        {
                            // Для построчного выбора рамка появляется только если
                            // строка действительно числится в SelectedRows.
                            for (int i = 0; i < dgv.SelectedRows.Count; i++)
                            {
                                if (dgv.SelectedRows[i].Index == e.RowIndex)
                                {
                                    hasSelection = true;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            // Для CellSelect/ColumnSelect — рамка появляется,
                            // только если ячейка реально выбрана.
                            for (int i = 0; i < dgv.SelectedCells.Count; i++)
                            {
                                var sc = dgv.SelectedCells[i];
                                if (sc.RowIndex == e.RowIndex && sc.ColumnIndex == e.ColumnIndex)
                                {
                                    hasSelection = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { hasSelection = false; }

                if (hasSelection)
                {
                    using (var bluePen = new Pen(Color.Blue, 1))
                        e.Graphics.DrawRectangle(bluePen,
                            new Rectangle(e.CellBounds.X, e.CellBounds.Y,
                                          e.CellBounds.Width - 1, e.CellBounds.Height - 1));
                }
                else
                {
                    e.Graphics.DrawRectangle(Pens.LightGray,
                        new Rectangle(e.CellBounds.X, e.CellBounds.Y,
                                      e.CellBounds.Width - 1, e.CellBounds.Height - 1));
                }
                e.Handled = true;
            };
        }

        // ──────────────────────────────────────────────────────────
        // Отрисовка ячеек таблицы состояний
        // ──────────────────────────────────────────────────────────
        private void StateTable_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var cell = stateTable.Rows[e.RowIndex].Cells[e.ColumnIndex];
            bool isCurrentCell = stateTable.CurrentCell == cell;
            e.Graphics.FillRectangle(SystemBrushes.Window, e.CellBounds);

            if (e.ColumnIndex == 0)
            {
                var state = states[e.RowIndex];
                if (state.IsInitial || state.IsFinal)
                {
                    e.Graphics.FillRectangle(
                        state.IsInitial ? Brushes.LightGreen : Brushes.LightCoral,
                        e.CellBounds);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                cell.Value?.ToString() ?? "",
                stateTable.Font,
                e.CellBounds,
                SystemColors.ControlText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);

            if (cell == currentStepCell)
            {
                using (var highlightBrush = new SolidBrush(Color.FromArgb(200, 100, 149, 237)))
                    e.Graphics.FillRectangle(highlightBrush, e.CellBounds);
            }
            else if (!isExecuting && !isProcessingStep && isCurrentCell)
            {
                using (var bluePen = new Pen(Color.Blue, 1))
                    e.Graphics.DrawRectangle(bluePen,
                        new Rectangle(e.CellBounds.X, e.CellBounds.Y,
                                      e.CellBounds.Width - 1, e.CellBounds.Height - 1));
            }
            else
            {
                e.Graphics.DrawRectangle(Pens.LightGray,
                    new Rectangle(e.CellBounds.X, e.CellBounds.Y,
                                  e.CellBounds.Width - 1, e.CellBounds.Height - 1));
            }

            e.Handled = true;
            stateTable.EnableHeadersVisualStyles = false;
            stateTable.ColumnHeadersDefaultCellStyle.SelectionBackColor = stateTable.ColumnHeadersDefaultCellStyle.BackColor;
            stateTable.ColumnHeadersDefaultCellStyle.SelectionForeColor = stateTable.ColumnHeadersDefaultCellStyle.ForeColor;
        }

        // ──────────────────────────────────────────────────────────
        // Вспомогательные форматирующие методы
        // ──────────────────────────────────────────────────────────
        private string DisplayToEditableFormat(string displayValue)
        {
            if (string.IsNullOrEmpty(displayValue)) return "";
            return displayValue.Replace("←", "<").Replace("→", ">").Replace("•", ".");
        }

        private string EditToDisplayFormat(string editValue)
        {
            if (string.IsNullOrEmpty(editValue)) return "";
            return editValue.Replace("<", "←").Replace(">", "→").Replace(".", "•");
        }
    }
}
