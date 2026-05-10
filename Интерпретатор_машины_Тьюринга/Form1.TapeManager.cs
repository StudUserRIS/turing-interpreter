using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Интерпретатор_машины_Тьюринга
{
    public partial class Form1
    {
        // ──────────────────────────────────────────────────────────
        // Поля ленты
        // ──────────────────────────────────────────────────────────
        private TapeControl tapeControl;
        private Button btnScrollLeft;
        private Button btnScrollRight;
        private Dictionary<int, char> cellValues = new Dictionary<int, char>();
        private int currentHeadPosition = 0;
        private const int MinIndex = -99;
        private const int MaxIndex = 99;
        private const int TapeHeight = 50;
        private const int ScrollButtonWidth = 40;
        private const char EmptyCellSymbol = '_';

        // ──────────────────────────────────────────────────────────
        // Создание элементов управления лентой
        // ──────────────────────────────────────────────────────────
        private void CreateTapeControl()
        {
            if (tapeControl != null)
            {
                this.Controls.Remove(tapeControl);
                tapeControl.Dispose();
            }

            tapeControl = new TapeControl
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Height = 75,
                Width = this.ClientSize.Width,
                Top = 75
            };
            this.Controls.Add(tapeControl);
        }

        private void CreateScrollButtons()
        {
            btnScrollLeft = new Button
            {
                Text = "◄",
                Width = ScrollButtonWidth,
                Height = TapeHeight,
                Font = new System.Drawing.Font("Arial", 12, System.Drawing.FontStyle.Bold),
                Top = 25,
                Left = 0,
                Enabled = false
            };
            btnScrollLeft.Click += (s, args) => ShiftIndices(-1);

            btnScrollRight = new Button
            {
                Text = "►",
                Width = ScrollButtonWidth,
                Height = TapeHeight,
                Font = new System.Drawing.Font("Arial", 12, System.Drawing.FontStyle.Bold),
                Top = 25,
                Left = ClientSize.Width - ScrollButtonWidth,
                Enabled = true
            };
            btnScrollRight.Click += (s, args) => ShiftIndices(1);
        }

        // ──────────────────────────────────────────────────────────
        // Обновление и скролл ленты
        // ──────────────────────────────────────────────────────────
        private void UpdateTapeDisplay()
        {
            if (tapeControl == null) return;

            var sb = new StringBuilder();
            int minIndex = cellValues.Keys.Any() ? cellValues.Keys.Min() : 0;
            int maxIndex = cellValues.Keys.Any() ? cellValues.Keys.Max() : 0;

            for (int i = minIndex; i <= maxIndex; i++)
            {
                sb.Append(cellValues.TryGetValue(i, out var value) ? value : EmptyCellSymbol);
            }

            tapeControl.InitializeTapeContent(sb.ToString());
            tapeControl.SetHeadPosition(currentHeadPosition);
        }

        private void UpdateScrollButtonsState()
        {
            if (btnScrollLeft == null || btnScrollRight == null) return;
            btnScrollLeft.Enabled = currentHeadPosition > MinIndex;
            btnScrollRight.Enabled = currentHeadPosition < MaxIndex;
        }

        private void ShiftIndices(int direction)
        {
            int newPosition = currentHeadPosition + direction;
            if (newPosition < MinIndex || newPosition > MaxIndex) return;
            currentHeadPosition = newPosition;
            tapeControl.SetHeadPosition(currentHeadPosition);
        }

        private void RestoreTape(Dictionary<int, char> tapeBackup, int headPosition)
        {
            tapeControl.tapeRenderer._tape.Clear();
            foreach (var cell in tapeBackup)
                tapeControl.tapeRenderer._tape[cell.Key] = new TapeCell(cell.Value);
            tapeControl.tapeRenderer.SetHeadPosition(headPosition);
            tapeControl.tapeRenderer.Invalidate();
        }

        // ──────────────────────────────────────────────────────────
        // Обработчики ввода в ячейки ленты
        // ──────────────────────────────────────────────────────────
        private void TapeGridView_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            if (e.Control is TextBox textBox)
            {
                textBox.MaxLength = 1;
                textBox.KeyPress -= TapeGridView_TextBoxKeyPress;
                textBox.KeyPress += TapeGridView_TextBoxKeyPress;
            }
        }

        private void TapeGridView_TextBoxKeyPress(object sender, KeyPressEventArgs e)
        {
            if (!alphabetTextBox.Text.Contains(e.KeyChar.ToString()) && e.KeyChar != (char)Keys.Back && e.KeyChar != EmptyCellSymbol)
            {
                e.Handled = true;
                if (!char.IsControl(e.KeyChar))
                    ShowWarningDialog($"Символ «{e.KeyChar}» отсутствует в алфавите.");
            }
        }
    }
}
