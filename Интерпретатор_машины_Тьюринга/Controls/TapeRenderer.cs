using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Интерпретатор_машины_Тьюринга
{
    public class TapeRenderer : Control
    {
        public string AllowedSymbols { get; set; } = "01ABC_";
        private const int CellWidth = 30;
        private const int CellHeight = 30;
        private const int VisibleCells = 61;
        private const char BlankSymbol = '_';
        internal readonly Dictionary<int, TapeCell> _tape = new Dictionary<int, TapeCell>();
        private int _headPosition = 0;
        private Bitmap _offscreenBuffer;
        private TextBox _editTextBox;
        private readonly System.Drawing.Font _cellFont = new System.Drawing.Font("Consolas", 12, FontStyle.Bold);
        private readonly System.Drawing.Font _indexFont = new System.Drawing.Font("Arial", 8);
        private const int CellPadding = 2;
        private int? _editingCellIndex = null;

        public char GetCurrentSymbol()
        {
            return _tape[_headPosition].Value;
        }
        public int GetHeadPosition()
        {
            return _headPosition;
        }
        public void SetCurrentSymbol(char symbol)
        {
            _tape[_headPosition].Value = symbol;
            Invalidate();
        }
        private void ShowError(string message)
        {
            MessageBox.Show(message, "Ошибка ввода", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        public void ResetTape()
        {
            _tape.Clear();
            _headPosition = 0;
            InitializeTape();
            Invalidate();
        }
        public void SetHeadPosition(int position)
        {
            _headPosition = position;
            UpdateHeadPosition();
            Invalidate();
        }
        private void FinishEditing(bool commit)
        {
            if (!_editTextBox.Visible || !_editingCellIndex.HasValue)
                return;
            if (commit)
            {
                char newValue = ' ';
                if (!string.IsNullOrEmpty(_editTextBox.Text))
                {
                    newValue = _editTextBox.Text[0];
                }

                _tape[_editingCellIndex.Value].Value = newValue;
                OnValueChanged(EventArgs.Empty);
            }
            _editTextBox.Visible = false;
            _editingCellIndex = null;
            Invalidate();
            _editTextBox.Parent?.Focus();
        }
        public event EventHandler ValueChanged;
        protected virtual void OnValueChanged(EventArgs e)
        {
            if (ValueChanged != null)
            {
                ValueChanged(this, e);
            }
        }
        public TapeRenderer()
        {
            InitializeTape();
            InitializeEditBox();
            SetupControl();
            foreach (var cell in _tape.Values)
            {
                if (cell.Value == '_')
                    cell.Value = ' ';
            }
            this.Resize += (s, e) =>
            {
                if (_editTextBox.Visible)
                    FinishEditing(true);
                Invalidate();
            };
        }
        private void InitializeTape()
        {
            for (int i = -10; i <= 10; i++)
            {
                _tape[i] = new TapeCell(' ');
            }
            _tape[0].IsHead = true;
        }
        public void InitializeTapeContent(string initialContent)
        {
            for (int position = 0; position < initialContent.Length; position++)
            {
                char cellValue = initialContent[position];
                if (cellValue == '_')
                {
                    cellValue = ' ';
                }
                _tape[position] = new TapeCell(cellValue);
            }
            Invalidate();
        }
        private void InitializeEditBox()
        {
            _editTextBox = new TextBox
            {
                Visible = false,
                BorderStyle = BorderStyle.None,
                Font = _cellFont,
                ForeColor = Color.DarkBlue,
                BackColor = Color.LightYellow,
                TextAlign = HorizontalAlignment.Center,
                MaxLength = 2
            };
            _editTextBox.KeyPress += (s, e) =>
            {
                if (char.IsControl(e.KeyChar))
                {
                    return;
                }
                if (!AllowedSymbols.Contains(e.KeyChar))
                {
                    e.Handled = true;
                    ShowError($"Символ '{e.KeyChar}' отсутствует в алфавите");
                }
            };
            _editTextBox.TextChanged += (s, e) =>
            {
                if (_editTextBox.Text.Length > 1)
                {
                    ShowError("Ячейка может содержать только 1 символ");
                    _editTextBox.Text = _editTextBox.Text.Substring(0, 1);
                    _editTextBox.SelectionStart = 1;
                }
            };
            _editTextBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
                {
                    FinishEditing(e.KeyCode == Keys.Enter);
                    e.Handled = e.SuppressKeyPress = true;
                }
            };
            _editTextBox.LostFocus += (s, e) => FinishEditing(true);
            Controls.Add(_editTextBox);
        }
        private void SetupControl()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.DoubleClick += TapeRenderer_DoubleClick;
            UpdateStyles();
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width == 0 || Height == 0) return;
            if (_offscreenBuffer == null || _offscreenBuffer.Width != Width || _offscreenBuffer.Height != Height)
            {
                if (_offscreenBuffer != null)
                {
                    _offscreenBuffer.Dispose();
                }
                _offscreenBuffer = new Bitmap(Width, Height);
            }
            Graphics graphics = Graphics.FromImage(_offscreenBuffer);
            graphics.Clear(this.BackColor);
            DrawTape(graphics);
            graphics.Dispose();
            e.Graphics.DrawImage(_offscreenBuffer, 0, 0);
        }
        private void DrawTape(Graphics g)
        {
            float centerX = Width / 2f - CellWidth / 2;
            float yPos = (Height - CellHeight) / 2;
            int leftmost = _headPosition - VisibleCells / 2;
            int rightmost = _headPosition + VisibleCells / 2;
            for (int i = leftmost; i <= rightmost; i++)
            {
                if (!_tape.TryGetValue(i, out var cell))
                {
                    cell = _tape[i] = new TapeCell(' ');
                }
                else if (cell.Value == '_')
                {
                    cell.Value = ' ';
                }
                float xPos = centerX + (i - _headPosition) * CellWidth;
                cell.Bounds = new RectangleF(xPos, yPos, CellWidth, CellHeight);
                DrawCell(g, cell);
                DrawCellIndex(g, i, xPos, yPos);
            }
        }
        private void DrawCell(Graphics g, TapeCell cell)
        {
            var rect = cell.Bounds;
            SolidBrush brush;
            Pen pen;
            if (cell.IsHead)
            {
                brush = new SolidBrush(Color.LightGoldenrodYellow);
            }
            else
            {
                brush = new SolidBrush(Color.White);
            }
            g.FillRectangle(brush, rect);
            brush.Dispose();
            if (cell.IsHead)
                pen = new Pen(Color.DarkGoldenrod);
            else
                pen = new Pen(Color.Black);
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            pen.Dispose();
            string displayValue;
            if (cell.Value == ' ')
            {
                displayValue = "_";
            }
            else
            {
                displayValue = cell.Value.ToString();
            }
            StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            if (cell.IsHead)
                g.DrawString(displayValue, _cellFont, Brushes.Red, rect, format);
            else
                g.DrawString(displayValue, _cellFont, Brushes.Black, rect, format);
        }
        private void DrawCellIndex(Graphics g, int index, float xPos, float yPos)
        {
            string indexText = index.ToString();
            var textSize = g.MeasureString(indexText, _indexFont);
            float textX = xPos + (CellWidth - textSize.Width) / 2;
            float textY = yPos - textSize.Height - 2;
            g.DrawString(indexText, _indexFont, Brushes.Gray, textX, textY);
        }
        private void TapeRenderer_DoubleClick(object sender, EventArgs e)
        {
            var mouseArgs = (MouseEventArgs)e;
            if (_editingCellIndex.HasValue)
            {
                FinishEditing(true);
            }
            foreach (var entry in _tape)
            {
                if (entry.Value.Bounds.Contains(mouseArgs.Location))
                {
                    StartEditingCell(entry.Key);
                    break;
                }
            }
        }
        private void StartEditingCell(int cellIndex)
        {
            if (!_tape.TryGetValue(cellIndex, out var cell))
            {
                cell = _tape[cellIndex] = new TapeCell(' ');
            }
            _editingCellIndex = cellIndex;
            var editBounds = new RectangleF
            (
                cell.Bounds.X + CellPadding,
                cell.Bounds.Y + CellPadding,
                cell.Bounds.Width - 2 * CellPadding,
                cell.Bounds.Height - 2 * CellPadding
            );
            _editTextBox.Clear();
            _editTextBox.Text = cell.Value == ' ' ? string.Empty : cell.Value.ToString();
            _editTextBox.Bounds = Rectangle.Round(editBounds);
            _editTextBox.Visible = true;
            _editTextBox.BringToFront();
            _editTextBox.Focus();
            _editTextBox.SelectAll();
        }
        public void MoveHeadLeft()
        {
            _headPosition--;
            EnsureCellExists(_headPosition);
            if (_tape[_headPosition].Value == '_')
            {
                _tape[_headPosition].Value = ' ';
            }
            UpdateHeadPosition();
            Invalidate();
        }
        public void MoveHeadRight()
        {
            _headPosition++;
            EnsureCellExists(_headPosition);
            if (_tape[_headPosition].Value == '_')
            {
                _tape[_headPosition].Value = ' ';
            }
            UpdateHeadPosition();
            Invalidate();
        }
        public void AddCellToLeft()
        {
            int minIndex = _tape.Keys.Min();
            _tape[minIndex - 1] = new TapeCell(' ');
            Invalidate();
        }
        public void AddCellToRight()
        {
            int maxIndex = _tape.Keys.Max();
            _tape[maxIndex + 1] = new TapeCell(' ');
            Invalidate();
        }
        private void EnsureCellExists(int position)
        {
            if (!_tape.ContainsKey(position))
            {
                _tape[position] = new TapeCell(' ');
            }
            else if (_tape[position].Value == '_')
            {
                _tape[position].Value = ' ';
            }
        }
        public void ClearCell(int position)
        {
            if (_tape.ContainsKey(position))
            {
                _tape[position].Value = BlankSymbol;
                Invalidate();
            }
        }
        private void UpdateHeadPosition()
        {
            foreach (var cell in _tape.Values)
            {
                cell.IsHead = false;
            }
            _tape[_headPosition].IsHead = true;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _offscreenBuffer?.Dispose();
                _editTextBox?.Dispose();
                _indexFont?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
