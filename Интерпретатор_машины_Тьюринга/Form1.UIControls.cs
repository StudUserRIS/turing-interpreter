using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Интерпретатор_машины_Тьюринга
{
    public partial class Form1
    {
        // ──────────────────────────────────────────────────────────
        // Поля UI
        // ──────────────────────────────────────────────────────────
        Label alphabetLabel;
        private Panel mainPanel;
        private Panel controlPanel;
        private Panel UslovieZadachi;
        TextBox commentTextBox;
        private Panel commentPanel;
        private TextBox alphabetTextBox;
        private Button btnAddBefore;
        private Button btnAddAfter;
        private Button btnRemoveState;
        private Button btnAddLast;
        private Button btnSpeed;
        private ToolStripMenuItem currentSpeedItem;
        private Button btnExecution;
        private StatusStrip statusStrip;
        private Button btnClear;
        private List<string> actionHistory = new List<string>();
        private Button btnHistory;
        private Button btnHelp;
        private const string HelpFileName = "HelpFile by Ivan Novokreshchenov RIS-24-2.chm";

        // ──────────────────────────────────────────────────────────
        // Инициализация всех элементов управления
        // ──────────────────────────────────────────────────────────
        private void SetupControls(object sender, EventArgs e)
        {
            this.SuspendLayout();

            CreateTapeControl();
            CreateScrollButtons();

            mainPanel = new Panel
            {
                BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Top = 263,
                Left = 0,
                Width = ClientSize.Width,
                Height = ClientSize.Height - 263,
                BackColor = ControlBackColor
            };
            this.Controls.Add(mainPanel);

            controlPanel = new Panel
            {
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = ControlBackColor,
                Top = 25,
                Height = 50,
                Width = ClientSize.Width,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            this.Controls.Add(controlPanel);

            UslovieZadachi = new Panel
            {
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = ControlBackColor,
                Top = 150,
                Height = 113,
                Width = ClientSize.Width,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            this.Controls.Add(UslovieZadachi);

            TextBox ProblemСondition = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Top = 20,
                Height = 93,
                Width = ClientSize.Width,
                Left = 0,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Arial", 10)
            };
            Label ProblemСonditionLabel = new Label
            {
                Text = "Условие задачи",
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleCenter,
                Height = 20,
                Font = new Font("Arial", 10)
            };
            ProblemСondition.Text = "";
            UslovieZadachi.Controls.Add(ProblemСondition);
            UslovieZadachi.Controls.Add(ProblemСonditionLabel);

            CreateAlphabetTextBox();
            CreateStateTable();
            CreateStateManagementButtons();
            CreateCommentBox();

            controlPanel.Controls.Add(btnRun);
            controlPanel.Controls.Add(btnPause);
            controlPanel.Controls.Add(btnStop);
            controlPanel.Controls.Add(btnStep);

            mainPanel.Controls.Add(alphabetLabel);
            mainPanel.Controls.Add(alphabetTextBox);
            mainPanel.Controls.Add(btnAddBefore);
            mainPanel.Controls.Add(btnAddAfter);
            mainPanel.Controls.Add(btnRemoveState);
            mainPanel.Controls.Add(btnAddLast);
            mainPanel.Controls.Add(stateTable);

            currentHeadPosition = 0;
            cellValues.Clear();
            this.ResumeLayout(true);
            UpdateLayout();
            UpdateTapeDisplay();
            UpdateButtonsState();
        }

        // ──────────────────────────────────────────────────────────
        // Поле ввода алфавита
        // ──────────────────────────────────────────────────────────
        private void CreateAlphabetTextBox()
        {
            alphabetLabel = new Label
            {
                Text = "Алфавит:",
                Width = 70,
                Height = 20,
                Top = 100,
                Left = ScrollButtonWidth,
                Font = new Font("Arial", 10)
            };
            this.Controls.Add(alphabetLabel);

            alphabetTextBox = new TextBox
            {
                Text = "01ABC",
                Width = 200,
                Height = 30,
                Top = 100,
                Left = ScrollButtonWidth + 75,
                Font = new Font("Arial", 10),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            alphabetTextBox.KeyPress += AlphabetTextBox_KeyPress;
            alphabetTextBox.TextChanged += (s, args) =>
            {
                string newText = alphabetTextBox.Text;
                bool textChanged = false;
                var cleanText = new StringBuilder();
                foreach (char c in newText)
                {
                    if (char.IsControl(c)) continue;
                    if ("←→•<>!_ ".Contains(c)) { textChanged = true; continue; }
                    if (cleanText.ToString().Contains(c.ToString())) { textChanged = true; continue; }
                    cleanText.Append(c);
                }
                if (textChanged)
                {
                    string finalText = cleanText.ToString();
                    alphabetTextBox.Text = finalText;
                    alphabetTextBox.SelectionStart = finalText.Length;
                    if (newText != finalText)
                        MessageBox.Show("Были удалены дублирующиеся или недопустимые символы", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                tapeControl.tapeRenderer.AllowedSymbols = alphabetTextBox.Text + "_";
                UpdateStateTableColumns();
            };
            this.Controls.Add(alphabetTextBox);
        }

        private void AlphabetTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (alphabetTextBox.Text.Contains(e.KeyChar.ToString()) && e.KeyChar != (char)Keys.Back && !char.IsControl(e.KeyChar))
            {
                e.Handled = true;
                ShowWarningDialog($"Символ «{e.KeyChar}» уже присутствует в алфавите.");
                return;
            }
            if ("←→•<>!_ ".Contains(e.KeyChar))
            {
                e.Handled = true;
                ShowWarningDialog($"Символ «{e.KeyChar}» зарезервирован для использования в правилах перехода.");
            }
        }

        // ──────────────────────────────────────────────────────────
        // Кнопки управления состояниями и выполнением
        // ──────────────────────────────────────────────────────────
        private void CreateStateManagementButtons()
        {
            int buttonWidth = 40;
            int buttonHeight = 30;
            int margin = 5;
            int topPosition = 160;
            int leftPosition = 20;

            Action<Button> ApplyButtonStyle = btn =>
            {
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderSize = 1;
                btn.BackColor = ControlBackColor;
                btn.Font = new Font("Segoe UI", 9);
                btn.Cursor = Cursors.Hand;
                btn.ForeColor = Color.Black;
                btn.TextAlign = ContentAlignment.MiddleCenter;
            };

            btnHistory = new Button { Text = "📜", Width = 40, Height = 30, Top = 6, Left = 365, Font = new Font("Arial", 10, FontStyle.Bold), BackColor = Color.FromArgb(200, 255, 200), Tag = "History" };
            ApplyTextButtonStyle(btnHistory);
            ApplyButtonStyle(btnHistory);
            btnHistory.Click += (s, e) => SaveActionHistory();
            controlPanel.Controls.Add(btnHistory);

            var btnLoad = new Button { Text = " 📁", Width = 40, Height = 30, Top = 6, Left = 150, Font = new Font("Arial", 10, FontStyle.Bold), BackColor = Color.FromArgb(200, 200, 255), Tag = "Load" };
            ApplyTextButtonStyle(btnLoad); ApplyButtonStyle(btnLoad);
            btnLoad.Click += (s, e) => OpenFile();

            var btnSave = new Button { Text = "💾", Width = 40, Height = 30, Top = 6, Left = 235, Font = new Font("Arial", 10, FontStyle.Bold), BackColor = Color.FromArgb(200, 200, 255), Tag = "Save" };
            ApplyTextButtonStyle(btnSave); ApplyButtonStyle(btnSave);
            btnSave.Click += (s, e) => SaveFile();

            var btnReset = new Button { Text = "♻", Width = 40, Height = 30, Top = 6, Left = 320, Font = new Font("Arial", 10, FontStyle.Bold), BackColor = Color.FromArgb(200, 200, 255), Tag = "Reset" };
            ApplyTextButtonStyle(btnReset); ApplyButtonStyle(btnReset);
            btnReset.Click += (s, e) => ResetAll();

            controlPanel.Controls.Add(btnLoad);
            controlPanel.Controls.Add(btnSave);
            controlPanel.Controls.Add(btnReset);

            btnClear = new Button { Text = "Очистка", Width = 70, Height = 25, Top = 0, Left = 280, Font = new Font("Segoe UI", 9), Tag = "Clear" };
            ApplyTextButtonStyle(btnClear);

            btnAddBefore = new Button { Text = "↑+", Width = buttonWidth, Height = buttonHeight, Top = topPosition, Left = leftPosition, Font = new Font("Arial", 10, FontStyle.Bold), Tag = "AddBefore" };
            ApplyButtonStyle(btnAddBefore); btnAddBefore.Click += (s, args) => AddStateBefore();

            btnAddAfter = new Button { Text = "+↓", Width = buttonWidth, Height = buttonHeight, Top = topPosition, Left = btnAddBefore.Right + margin, Font = new Font("Arial", 10, FontStyle.Bold), Tag = "AddAfter" };
            ApplyButtonStyle(btnAddAfter); btnAddAfter.Click += (s, args) => AddStateAfter();

            btnRemoveState = new Button { Text = "✕", Width = buttonWidth, Height = buttonHeight, Top = topPosition, Left = btnAddAfter.Right + margin, Font = new Font("Arial", 10, FontStyle.Bold), Tag = "RemoveState" };
            ApplyButtonStyle(btnRemoveState); btnRemoveState.Click += (s, args) => RemoveState();

            btnExecution = new Button { Text = "Выполнение", Width = 90, Height = 25, Top = 0, Left = 50, Font = new Font("Segoe UI", 9), Tag = "Execution" };
            ApplyTextButtonStyle(btnExecution);

            var clearMenu = new ContextMenuStrip();
            var clearAllItem    = new ToolStripMenuItem("Очистить всё");
            var clearTapeItem   = new ToolStripMenuItem("Очистить ленту");
            var clearTableItem  = new ToolStripMenuItem("Очистить таблицу");
            var clearProblemItem  = new ToolStripMenuItem("Очистить условие задачи");
            var clearCommentItem  = new ToolStripMenuItem("Очистить комментарий");
            var clearAlphabetItem = new ToolStripMenuItem("Очистить алфавит");
            clearAllItem.Click     += (s, e) => ClearAll();
            clearTapeItem.Click    += (s, e) => ClearTape();
            clearTableItem.Click   += (s, e) => ClearStateTable();
            clearProblemItem.Click += (s, e) => ClearProblemCondition();
            clearCommentItem.Click += (s, e) => ClearComment();
            clearAlphabetItem.Click += (s, e) => ClearAlphabet();
            clearMenu.Items.AddRange(new ToolStripItem[] { clearAllItem, new ToolStripSeparator(), clearTapeItem, clearTableItem, new ToolStripSeparator(), clearProblemItem, clearCommentItem, new ToolStripSeparator(), clearAlphabetItem });
            btnClear.Click += (s, e) =>
            {
                clearProblemItem.Enabled = (activeAssignmentId == null);
                clearMenu.Show(btnClear, new Point(0, btnClear.Height));
            };
            this.Controls.Add(btnClear);

            var executionMenu = new ContextMenuStrip();
            var runItem         = new ToolStripMenuItem("Запуск (F5)");
            var stepItem        = new ToolStripMenuItem("Шаг (F11)");
            var pauseItem       = new ToolStripMenuItem("Пауза");
            var stopItem        = new ToolStripMenuItem("Остановка (Esc)");
            var instantRunItem  = new ToolStripMenuItem("Мгновенное выполнение (Ctrl+I)");
            runItem.Click        += (s, e) => { StartExecution(); this.ActiveControl = null; };
            stepItem.Click       += (s, e) => { ExecuteSingleStep(); this.ActiveControl = null; };
            pauseItem.Click      += (s, e) => { executionTimer.Stop(); this.ActiveControl = null; };
            stopItem.Click       += (s, e) => { StopExecution(); this.ActiveControl = null; };
            instantRunItem.Click += (s, e) => { ExecuteInstant(); this.ActiveControl = null; };
            executionMenu.Items.AddRange(new ToolStripItem[] { runItem, stepItem, pauseItem, new ToolStripSeparator(), instantRunItem, new ToolStripSeparator(), stopItem });
            btnExecution.Click += (s, e) => executionMenu.Show(btnExecution, new Point(0, btnExecution.Height));

            btnAddLast = new Button { Text = "⤓+", Width = buttonWidth, Height = buttonHeight, Top = topPosition, Left = btnRemoveState.Right + margin, Font = new Font("Arial", 10, FontStyle.Bold), Tag = "AddLast" };
            ApplyButtonStyle(btnAddLast); btnAddLast.Click += (s, args) => AddStateLast();

            int runButtonsLeft = btnAddLast.Right + 20;
            btnRun = new Button { Text = "▶", Width = buttonWidth, Height = buttonHeight, Top = topPosition, Left = runButtonsLeft, Font = new Font("Arial", 10, FontStyle.Bold), BackColor = Color.FromArgb(200, 255, 200), Tag = "Run" };
            ApplyButtonStyle(btnRun); btnRun.Click += (s, args) => StartExecution();

            btnPause = new Button { Text = "⏸", Width = buttonWidth, Height = buttonHeight, Top = topPosition, Left = btnRun.Right + margin, Font = new Font("Arial", 10, FontStyle.Bold), BackColor = Color.FromArgb(255, 255, 200), Enabled = false, Tag = "Pause" };
            ApplyButtonStyle(btnPause);
            btnPause.Click += (s, args) => { executionTimer.Stop(); isPaused = true; UpdateButtonsState(); };

            btnStep = new Button { Text = "⏯", Width = buttonWidth, Height = buttonHeight, Top = topPosition, Left = btnRun.Right + margin, Font = new Font("Arial", 10, FontStyle.Bold), BackColor = Color.FromArgb(200, 200, 255), Tag = "Step" };
            ApplyButtonStyle(btnStep); btnStep.Click += (s, args) => ExecuteSingleStep();

            btnStop = new Button { Text = "■", Width = buttonWidth, Height = buttonHeight, Top = topPosition, Left = btnStep.Right + margin, Font = new Font("Arial", 10, FontStyle.Bold), BackColor = Color.FromArgb(255, 200, 200), Enabled = false, Tag = "Stop" };
            ApplyButtonStyle(btnStop); btnStop.Click += (s, args) => StopExecution();

            btnInstantRun = new Button { Text = "⚡", Width = buttonWidth, Height = buttonHeight, Top = topPosition, Left = btnStop.Right + margin, Font = new Font("Arial", 10, FontStyle.Bold), BackColor = Color.FromArgb(255, 215, 0), Tag = "InstantRun" };
            ApplyButtonStyle(btnInstantRun); btnInstantRun.Click += (s, args) => ExecuteInstant();
            controlPanel.Controls.Add(btnInstantRun);

            btnSpeed = new Button { Text = "Скорость", Width = 70, Height = 25, Top = 0, Left = 140, Font = new Font("Segoe UI", 9), Tag = "Speed" };
            ApplyTextButtonStyle(btnSpeed);

            btnFile = new Button { Text = "Файл", Width = 50, Height = 25, Top = 0, Left = 0, Font = new Font("Segoe UI", 9), Tag = "File" };
            ApplyTextButtonStyle(btnFile);

            btnHelp = new Button { Text = "Помощь", Width = 70, Height = 25, Top = 0, Left = 210, Font = new Font("Segoe UI", 9), Tag = "Help" };
            ApplyTextButtonStyle(btnHelp);
            btnHelp.Click += (s, e) => ShowHelp(null);
            this.Controls.Add(btnHelp);

            var speedMenu = new ContextMenuStrip();
            var verySlowItem = new ToolStripMenuItem("Очень медленно");
            var slowItem     = new ToolStripMenuItem("Медленно");
            var mediumItem   = new ToolStripMenuItem("Средне");
            var fastItem     = new ToolStripMenuItem("Быстро");
            var veryFastItem = new ToolStripMenuItem("Очень быстро");
            verySlowItem.Click += (s, e) => { SetExecutionSpeed(VerySlowSpeed, verySlowItem); this.ActiveControl = null; };
            slowItem.Click     += (s, e) => { SetExecutionSpeed(SlowSpeed, slowItem);         this.ActiveControl = null; };
            mediumItem.Click   += (s, e) => { SetExecutionSpeed(MediumSpeed, mediumItem);     this.ActiveControl = null; };
            fastItem.Click     += (s, e) => { SetExecutionSpeed(FastSpeed, fastItem);         this.ActiveControl = null; };
            veryFastItem.Click += (s, e) => { SetExecutionSpeed(VeryFastSpeed, veryFastItem); this.ActiveControl = null; };
            speedMenu.Items.AddRange(new ToolStripItem[] { verySlowItem, slowItem, mediumItem, fastItem, veryFastItem });
            btnSpeed.Click += (s, e) => { speedMenu.Show(btnSpeed, new Point(0, btnSpeed.Height)); this.ActiveControl = null; };

            var fileMenu    = new ContextMenuStrip();
            var newItem     = new ToolStripMenuItem("Новый");
            var openItem    = new ToolStripMenuItem("Открыть...");
            var saveItem    = new ToolStripMenuItem("Сохранить");
            var saveAsItem  = new ToolStripMenuItem("Сохранить как...");
            var importItem  = new ToolStripMenuItem("Импорт программы...");
            var exportItem  = new ToolStripMenuItem("Экспорт программы...");
            var exitItem    = new ToolStripMenuItem("Выход");
            var historyItem = new ToolStripMenuItem("Сохранить историю действий");
            historyItem.Click += (s, e) => SaveActionHistory();
            newItem.Click    += (s, e) => { NewFile();       this.ActiveControl = null; };
            openItem.Click   += (s, e) => { OpenFile();      this.ActiveControl = null; };
            saveItem.Click   += (s, e) => { SaveFile();      this.ActiveControl = null; };
            saveAsItem.Click += (s, e) => { SaveFileAs();    this.ActiveControl = null; };
            importItem.Click += (s, e) => { ImportProgram(); this.ActiveControl = null; };
            exportItem.Click += (s, e) => { ExportProgram(); this.ActiveControl = null; };
            exitItem.Click   += (s, e) =>
            {
                if (isExecuting) { MessageBox.Show("Невозможно сохранить программу во время выполнения.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                System.Windows.Forms.Application.Exit();
                this.ActiveControl = null;
            };
            fileMenu.Items.AddRange(new ToolStripItem[] { newItem, openItem, new ToolStripSeparator(), saveItem, saveAsItem, new ToolStripSeparator(), importItem, exportItem, new ToolStripSeparator(), historyItem, new ToolStripSeparator(), exitItem });
            btnFile.Click += (s, e) =>
            {
                bool locked = (activeAssignmentId != null) || (checkingModePanel != null);
                newItem.Enabled    = !locked;
                openItem.Enabled   = !locked;
                importItem.Enabled = !locked;
                fileMenu.Show(btnFile, new Point(0, btnFile.Height));
                this.ActiveControl = null;
            };

            this.Controls.Add(btnExecution);
            this.Controls.Add(btnAddBefore);
            this.Controls.Add(btnAddAfter);
            this.Controls.Add(btnRemoveState);
            this.Controls.Add(btnAddLast);
            this.Controls.Add(btnRun);
            this.Controls.Add(btnPause);
            this.Controls.Add(btnStep);
            this.Controls.Add(btnStop);
            this.Controls.Add(btnSpeed);
            this.Controls.Add(btnFile);

            btnUserMenu = new Button { Text = "👤 Войти ▼", Width = 160, Height = 25, Top = 0, Left = ClientSize.Width - 160, Anchor = AnchorStyles.Top | AnchorStyles.Right, Font = new Font("Segoe UI", 9), TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 10, 0) };
            ApplyTextButtonStyle(btnUserMenu);
            this.Controls.Add(btnUserMenu);

            userContextMenu = new ContextMenuStrip();
            btnUserMenu.Click += (s, ev) =>
            {
                if (currentUserRole == UserRole.Guest) ShowLoginForm();
                else userContextMenu.Show(btnUserMenu, new Point(0, btnUserMenu.Height));
            };

            UpdateAuthState(UserRole.Guest);
            SetExecutionSpeed(MediumSpeed, mediumItem);
            AddToolTips();
            UpdateButtonsState();
        }

        // ──────────────────────────────────────────────────────────
        // Панель комментария
        // ──────────────────────────────────────────────────────────
        private void CreateCommentBox()
        {
            commentPanel = new Panel
            {
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = ControlBackColor,
                Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom
            };
            var commentLabel = new Label { Text = "Комментарий", Width = CommentBoxWidth, Height = 20, Top = 0, Left = 75, Font = new Font("Arial", 10) };
            commentPanel.Controls.Add(commentLabel);
            commentTextBox = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Width = CommentBoxWidth, Top = 25, Left = 0, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, Font = new Font("Arial", 10) };
            commentPanel.Controls.Add(commentTextBox);
            commentTextBox.Text = "";
            this.Controls.Add(commentPanel);
            commentPanel.BringToFront();
        }

        // ──────────────────────────────────────────────────────────
        // Строка состояния
        // ──────────────────────────────────────────────────────────
        private void CreateStatusBar()
        {
            statusStrip = new StatusStrip { BackColor = MainBackColor };

            connectionStatusLabel = new ToolStripStatusLabel { Text = "🟠 Не авторизован.", Margin = new Padding(5, 0, 10, 0) };
            syncStatusLabel = new ToolStripStatusLabel { Text = "", BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Etched, Padding = new Padding(10, 0, 10, 0) };
            executionStatusLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleRight };

            statusStrip.Items.Add(connectionStatusLabel);
            statusStrip.Items.Add(syncStatusLabel);
            statusStrip.Items.Add(executionStatusLabel);
            this.Controls.Add(statusStrip);
        }

        // ──────────────────────────────────────────────────────────
        // Разметка и подсказки
        // ──────────────────────────────────────────────────────────
        private void UpdateLayout()
        {
            try
            {
                var btnHistoryInPanel = controlPanel.Controls.OfType<Button>().FirstOrDefault(b => b.Tag as string == "History");
                if (btnHistoryInPanel != null) { btnHistoryInPanel.Left = 365; btnHistoryInPanel.Top = 6; }

                btnScrollRight.Left = ClientSize.Width - ScrollButtonWidth;
                mainPanel.Width = ClientSize.Width - 263;
                mainPanel.Height = ClientSize.Height - mainPanel.Top - 22;
                alphabetLabel.Top  = 272 - mainPanel.Top;
                alphabetLabel.Left = 180;
                alphabetTextBox.Top  = 270 - mainPanel.Top;
                alphabetTextBox.Left = 250;
                alphabetTextBox.Width = stateTable.Width - 250;
                btnAddBefore.Top  = 263 - mainPanel.Top + 4; btnAddBefore.Left  = 50;
                btnAddAfter.Top   = 263 - mainPanel.Top + 4; btnAddAfter.Left   = 5;
                btnRemoveState.Top = 263 - mainPanel.Top + 4; btnRemoveState.Left = 95;
                btnAddLast.Top    = 263 - mainPanel.Top + 4; btnAddLast.Left    = 140;

                var bRun   = controlPanel.Controls.OfType<Button>().FirstOrDefault(b => b.Tag as string == "Run");
                var bStep  = controlPanel.Controls.OfType<Button>().FirstOrDefault(b => b.Tag as string == "Step");
                var bStop  = controlPanel.Controls.OfType<Button>().FirstOrDefault(b => b.Tag as string == "Stop");
                var bLoad  = controlPanel.Controls.OfType<Button>().FirstOrDefault(b => b.Tag as string == "Load");
                var bSave  = controlPanel.Controls.OfType<Button>().FirstOrDefault(b => b.Tag as string == "Save");
                var bReset = controlPanel.Controls.OfType<Button>().FirstOrDefault(b => b.Tag as string == "Reset");

                if (bRun != null)   { bRun.Left = 10;   bRun.Top = 6; }
                btnPause.Left = 55;  btnPause.Top = 6;
                if (bStep != null)  { bStep.Left = 100; bStep.Top = 6; }
                if (bStop != null)  { bStop.Left = 145; bStop.Top = 6; }
                btnInstantRun.Left = 190; btnInstantRun.Top = 6;
                if (bLoad != null)  { bLoad.Left = 235;  bLoad.Top = 6; }
                if (bSave != null)  { bSave.Left = 280;  bSave.Top = 6; }
                if (bReset != null) { bReset.Left = 325; bReset.Top = 6; }
                if (btnHistoryInPanel != null) { btnHistoryInPanel.Left = 370; btnHistoryInPanel.Top = 6; }

                stateTable.Top = 300 - mainPanel.Top;
                stateTable.Left = 0;
                stateTable.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
                stateTable.Height = ClientSize.Height - 327;

                if (commentPanel != null && stateTable != null)
                {
                    commentPanel.Top = mainPanel.Top;
                    commentPanel.Left = mainPanel.Right;
                    commentPanel.Width = ClientSize.Width - stateTable.Right;
                    commentPanel.Height = mainPanel.Height;
                    commentTextBox.Width = commentPanel.Width - 10;
                    commentTextBox.Height = commentPanel.Height - commentTextBox.Top - 5;
                }
                UpdateScrollButtonsState();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в UpdateLayout: {ex.Message}");
            }
        }

        private void AddToolTips()
        {
            var toolTip = new ToolTip { AutoPopDelay = 5000, InitialDelay = 500, ReshowDelay = 500, ShowAlways = true };
            toolTip.SetToolTip(btnClear,       "Меню очистки данных\nПозволяет очистить отдельные элементы или всё сразу");
            toolTip.SetToolTip(btnInstantRun,  "Мгновенно выполнить программу (Ctrl+I)\nПриведет ленту сразу в конечное состояние");
            toolTip.SetToolTip(btnHistory,     "Сохранить историю действий");
            toolTip.SetToolTip(btnPause,       "Приостановить выполнение программы");
            toolTip.SetToolTip(alphabetTextBox,"Введите символы алфавита, которые будет использовать машина Тьюринга");
            toolTip.SetToolTip(btnRun,         "Запустить выполнение программы (F5)");
            toolTip.SetToolTip(btnStep,        "Выполнить один шаг (F11)");
            toolTip.SetToolTip(btnStop,        "Остановить выполнение (Esc)");
            toolTip.SetToolTip(btnAddBefore,   "Добавить состояние перед выбранным");
            toolTip.SetToolTip(btnAddAfter,    "Добавить состояние после выбранного");
            toolTip.SetToolTip(btnRemoveState, "Удалить выбранное состояние");
            toolTip.SetToolTip(btnAddLast,     "Добавить состояние в конец списка");
            toolTip.SetToolTip(btnSpeed,       "Изменить скорость выполнения");
            toolTip.SetToolTip(btnFile,        "Меню работы с файлами");
            toolTip.SetToolTip(btnExecution,   "Меню выполнения программы машины Тьюринга");
            toolTip.SetToolTip(btnScrollLeft,  "Сдвинуть ленту влево");
            toolTip.SetToolTip(btnScrollRight, "Сдвинуть ленту вправо");

            if (tapeControl != null)
            {
                var btnLeftField  = typeof(TapeControl).GetField("btnLeft",  System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var btnRightField = typeof(TapeControl).GetField("btnRight", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (btnLeftField?.GetValue(tapeControl) is Button btnLeft2)   toolTip.SetToolTip(btnLeft2, "Сдвинуть головку влево");
                if (btnRightField?.GetValue(tapeControl) is Button btnRight2) toolTip.SetToolTip(btnRight2, "Сдвинуть головку вправо");
            }

            foreach (Control control in controlPanel.Controls)
            {
                if (control is Button button && (string.IsNullOrEmpty(button.Text) || button.Text.Length == 1))
                {
                    switch (button.Tag as string)
                    {
                        case "Load":    toolTip.SetToolTip(button, "Загрузить программу из файла (Ctrl+O)"); break;
                        case "Save":    toolTip.SetToolTip(button, "Сохранить программу (Ctrl+S)"); break;
                        case "Reset":   toolTip.SetToolTip(button, "Сбросить все данные (Ctrl+R)"); break;
                        case "History": toolTip.SetToolTip(button, "Сохранить историю действий (Ctrl+H)"); break;
                    }
                }
            }
        }

        // ──────────────────────────────────────────────────────────
        // Состояние кнопок
        // ──────────────────────────────────────────────────────────
        private void UpdateButtonsState()
        {
            try
            {
                if (btnAddBefore == null || btnAddAfter == null || btnRemoveState == null || stateTable == null) return;
                bool hasSelection = stateTable.SelectedRows.Count > 0 && stateTable.SelectedRows[0].Index >= 0 && stateTable.SelectedRows[0].Index < states.Count;
                btnClear.Enabled       = !isExecuting;
                btnAddBefore.Enabled   = hasSelection && !isExecuting;
                btnAddAfter.Enabled    = hasSelection && !isExecuting;
                btnRemoveState.Enabled = hasSelection && states.Count > 1 && !isExecuting;
                btnAddLast.Enabled     = !isExecuting;
                btnInstantRun.Enabled  = !isExecuting && states.Any(s => s.IsInitial) && HasAnyRules();
                btnStep.Enabled        = !isExecuting || isPaused;
                btnRun.Enabled         = !isExecuting || isPaused;
                btnPause.Enabled       = isExecuting && !isPaused;
                btnStop.Enabled        = isExecuting;
                btnExecution.Enabled   = states.Any(s => s.IsInitial);
                stateTable.Enabled     = !isExecuting;
                alphabetTextBox.Enabled = !isExecuting;
                tapeControl.Enabled    = !isExecuting;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateButtonsState: {ex.Message}");
            }
        }

        // ──────────────────────────────────────────────────────────
        // Горячие клавиши
        // ──────────────────────────────────────────────────────────
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.H)) { SaveActionHistory(); return true; }
            if (keyData == (Keys.Control | Keys.I)) { ExecuteInstant(); return true; }
            if (keyData == (Keys.Control | Keys.N)) { NewFile(); return true; }
            if (keyData == (Keys.Control | Keys.O)) { OpenFile(); return true; }
            if (keyData == (Keys.Control | Keys.S)) { SaveFile(); return true; }
            if (keyData == Keys.F5) { if (!isExecuting || isPaused) StartExecution(); return true; }
            if (keyData == Keys.F6 && isExecuting && !isPaused) { executionTimer.Stop(); isPaused = true; UpdateButtonsState(); return true; }
            if (keyData == Keys.F11) { ExecuteSingleStep(); return true; }
            if (keyData == Keys.Escape && isExecuting) { StopExecution(); return true; }
            if (keyData == (Keys.Control | Keys.R)) { ResetAll(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ──────────────────────────────────────────────────────────
        // Очистка и сброс
        // ──────────────────────────────────────────────────────────
        private void ClearAll()
        {
            if (isExecuting) { ShowWarningDialog("Невозможно выполнить очистку во время выполнения программы."); return; }
            if (!ShowConfirmDialog("Вы уверены, что хотите очистить все данные? Это действие нельзя отменить.", "Подтверждение очистки")) return;
            ClearTape();
            ClearStateTable();
            if (activeAssignmentId == null) ClearProblemCondition();
            ClearComment();
            ClearAlphabet();
        }

        private void ClearTape()
        {
            if (isExecuting) { ShowWarningDialog("Невозможно очистить ленту во время выполнения программы."); return; }
            tapeControl.tapeRenderer.ResetTape();
            cellValues.Clear();
            currentHeadPosition = 0;
            tapeControl.SetHeadPosition(0);
        }

        private void ClearStateTable()
        {
            if (isExecuting) { ShowWarningDialog("Невозможно очистить таблицу во время выполнения программы."); return; }
            states.Clear();
            states.Add(new TuringState { Name = "q0", IsInitial = true });
            UpdateStateTable();
            UpdateStateTableColumns();
        }

        private void ClearProblemCondition()
        {
            if (isExecuting) { ShowWarningDialog("Невозможно очистить условие задачи во время выполнения программы."); return; }
            var problemTextBox = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
            if (problemTextBox != null) problemTextBox.Text = "";
        }

        private void ClearAlphabet()
        {
            if (isExecuting) { ShowWarningDialog("Невозможно очистить алфавит во время выполнения программы."); return; }
            alphabetTextBox.Text = "";
            tapeControl.tapeRenderer.AllowedSymbols = "_";
            UpdateStateTableColumns();
        }

        private void ClearComment()
        {
            if (isExecuting) { ShowWarningDialog("Невозможно очистить комментарий во время выполнения программы."); return; }
            if (commentTextBox != null) commentTextBox.Text = "";
        }

        private void ResetAll()
        {
            actionHistory.Clear();
            if (isExecuting) { ShowWarningDialog("Невозможно выполнить сброс во время выполнения программы."); return; }
            if (!ShowConfirmDialog("Вы уверены, что хотите сбросить все данные? Это действие нельзя отменить.", "Подтверждение сброса")) return;
            tapeControl.tapeRenderer.ResetTape();
            cellValues.Clear();
            currentHeadPosition = 0;
            alphabetTextBox.Text = "";
            tapeControl.tapeRenderer.AllowedSymbols = alphabetTextBox.Text + "_";
            states.Clear();
            states.Add(new TuringState { Name = "q0", IsInitial = true });
            var problemTextBox = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
            if (problemTextBox != null) problemTextBox.Text = "";
            if (commentTextBox != null) commentTextBox.Text = "";
            currentFilePath = null;
            UpdateStateTable();
            UpdateStateTableColumns();
            ShowSuccessDialog("Все данные были сброшены.");
        }

        // ──────────────────────────────────────────────────────────
        // История действий
        // ──────────────────────────────────────────────────────────
        private void SaveActionHistory()
        {
            if (isExecuting) { ShowWarningDialog("Невозможно сохранить историю действий во время выполнения программы."); return; }
            if (actionHistory.Count == 0) { ShowWarningDialog("История действий пуста — сохранять нечего."); return; }
            string baseFileName = string.IsNullOrEmpty(currentFilePath) ? "Новый алгоритм" : Path.GetFileNameWithoutExtension(currentFilePath);
            using (var saveDialog = new SaveFileDialog())
            {
                saveDialog.Filter = "Текстовые файлы (*.txt)|*.txt";
                saveDialog.Title = "Сохранить историю действий";
                saveDialog.FileName = $"История действий {baseFileName}.txt";
                saveDialog.DefaultExt = "txt";
                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var header = new List<string>
                        {
                            "История действий машины Тьюринга",
                            $"Алфавит: {alphabetTextBox.Text}",
                            $"Начальное состояние: {states.FirstOrDefault(s => s.IsInitial)?.Name ?? "не задано"}",
                            $"Дата: {DateTime.Now:dd.MM.yyyy HH:mm:ss}",
                            $"Всего шагов: {actionHistory.Count}",
                            "",
                            "Детализация шагов:",
                            ""
                        };
                        header.AddRange(actionHistory);
                        File.WriteAllLines(saveDialog.FileName, header);
                        ShowSuccessDialog("История действий успешно сохранена.");
                    }
                    catch (Exception ex)
                    {
                        ShowErrorDialog($"Ошибка при сохранении истории: {ex.Message}");
                    }
                }
            }
        }

        // ──────────────────────────────────────────────────────────
        // Справка
        // ──────────────────────────────────────────────────────────
        private void ShowHelp(string topic)
        {
            this.ActiveControl = null;
            try
            {
                string helpPath = Path.Combine(System.Windows.Forms.Application.StartupPath, HelpFileName);
                if (!File.Exists(helpPath)) { ShowErrorDialog($"Файл справки {HelpFileName} не найден в папке программы."); return; }
                if (string.IsNullOrEmpty(topic)) Help.ShowHelp(this, helpPath);
                else Help.ShowHelp(this, helpPath, HelpNavigator.Topic, topic);
            }
            catch (Exception ex)
            {
                ShowErrorDialog($"Ошибка при открытии справки: {ex.Message}");
            }
        }
    }
}
