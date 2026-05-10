using System;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace Интерпретатор_машины_Тьюринга
{
    public partial class Form1
    {
        // ──────────────────────────────────────────────────────────
        // Поля режима выполнения задания
        // ──────────────────────────────────────────────────────────
        private Button btnSaveTMTop;
        private Button btnReturnTMTop;
        private string originalAssignmentDescription = null;
        private string originalStudentSolution       = null;

        private int?  activeAssignmentId    = null;
        private bool  isTaskExecutionDirty  = false;
        private bool  isTaskReadOnlyMode    = false;
        private Form  studentTasksMenuRef   = null;

        private TuringMachineData mainInterpreterBackup = null;

        private Button btnDraftTop;
        private Button btnSubmitTop;
        private Button btnReturnTop;
        private Button btnRevokeTop;
        private Button btnResetSolutionTop;
        private Button btnResetToNewMTTop;
        private Panel panelMTOutdated;

        // ──────────────────────────────────────────────────────────
        // Резервирование / восстановление состояния интерпретатора
        // ──────────────────────────────────────────────────────────
        private void BackupMainInterpreterState()
        {
            var problemTextBox = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
            mainInterpreterBackup = new TuringMachineData
            {
                Alphabet         = alphabetTextBox.Text,
                TapeContent      = tapeControl.tapeRenderer._tape.ToDictionary(k => k.Key, v => v.Value.Value),
                HeadPosition     = tapeControl.tapeRenderer.GetHeadPosition(),
                States           = states.ToList(),
                TransitionRules  = SaveTableData(),
                ProblemCondition = problemTextBox?.Text ?? "",
                Comment          = commentTextBox?.Text ?? ""
            };
        }

        private void RestoreMainInterpreterState()
        {
            if (mainInterpreterBackup == null) return;
            alphabetTextBox.Text = mainInterpreterBackup.Alphabet;
            tapeControl.tapeRenderer.AllowedSymbols = alphabetTextBox.Text + "_";
            tapeControl.tapeRenderer._tape.Clear();
            foreach (var kvp in mainInterpreterBackup.TapeContent)
                tapeControl.tapeRenderer._tape[kvp.Key] = new TapeCell(kvp.Value);
            tapeControl.SetHeadPosition(mainInterpreterBackup.HeadPosition);
            states.Clear();
            foreach (var st in mainInterpreterBackup.States) states.Add(st);
            UpdateStateTableColumns();
            UpdateStateTable();
            RestoreTableData(mainInterpreterBackup.TransitionRules);
            var problemTextBox = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
            if (problemTextBox != null) problemTextBox.Text = mainInterpreterBackup.ProblemCondition;
            if (commentTextBox != null) commentTextBox.Text = mainInterpreterBackup.Comment;
            mainInterpreterBackup = null;
        }

        private void MarkTaskDirty(object sender, EventArgs e)
        {
            if (!isTaskReadOnlyMode) isTaskExecutionDirty = true;
        }

        private void ClearInterpreterSilent()
        {
            alphabetTextBox.Text = "01";
            if (tapeControl != null && tapeControl.tapeRenderer != null)
            {
                tapeControl.tapeRenderer.AllowedSymbols = "01_";
                tapeControl.tapeRenderer._tape.Clear();
                tapeControl.tapeRenderer._tape[0] = new TapeCell(' ');
                tapeControl.SetHeadPosition(0);
            }
            if (states != null) { states.Clear(); states.Add(new TuringState { Name = "q0", IsInitial = true }); }
            UpdateStateTableColumns();
            UpdateStateTable();
        }

        // ──────────────────────────────────────────────────────────
        // Конструктор МТ для преподавателя.
        //
        // ВАЖНО ПО АРХИТЕКТУРЕ (исправление бага «не сохраняется МТ»):
        // Раньше при нажатии «Изменить МТ» / «Создать МТ» в окне редактирования
        // задания это окно ЗАКРЫВАЛОСЬ (taskForm.Close), потом вызывался
        // EnterTeacherTMBuilderMode, а при возврате открывалось НОВОЕ окно
        // редактирования через ShowTaskEditorWindow. Это приводило к нескольким
        // серьёзным проблемам:
        //   1) Между закрытием первого окна и открытием второго асинхронно
        //      выполнялся await LoadAllData() (в обработчике btnEditTask.Click),
        //      а также мог сработать DataRefreshBus из-за SignalR-эха —
        //      внутреннее состояние state.Version при этом не пересчитывалось,
        //      из-за чего сервер при PUT /assignments/{id} возвращал
        //      VersionConflict («задание было изменено другим пользователем»),
        //      хотя реально его никто не менял.
        //   2) Из-за повторного открытия окна в новом контексте часть
        //      изменений визуально «терялась» — пользователь думал, что
        //      «ничего не сохранилось».
        //
        // Новая модель: TM-builder теперь принимает callback onReturn(bool saved).
        // Окно редактирования задания НЕ закрывается, а скрывается (Hide).
        // При возврате из TM-builder caller сам решает, что делать дальше:
        // обновить индикатор МТ и заново показать тот же taskForm — без
        // пересоздания, без гонки с LoadAllData, без рассинхрона state.Version.
        // ──────────────────────────────────────────────────────────
        private void EnterTeacherTMBuilderMode(
            TaskEditorState state,
            Action<bool> onReturn)
        {
            BackupMainInterpreterState();
            // Видимость окна редактирования и «Мои курсы» контролирует caller
            // (ShowTaskEditorWindow): до вызова этого метода они уже скрыты,
            // после вызова onReturn — будут показаны обратно.
            if (btnUserMenu != null) btnUserMenu.Visible = false;
            this.Text = $"Конструктор МТ — {state.Title}";

            int rightEdge = this.ClientSize.Width;
            btnReturnTMTop = new Button { Text = "Вернуться", Width = 90, Height = 25, Top = 0, Left = rightEdge - 90, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            ApplyTextButtonStyle(btnReturnTMTop);
            btnSaveTMTop = new Button { Text = "Сохранить", Width = 110, Height = 25, Top = 0, Left = btnReturnTMTop.Left - 110, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            ApplyTextButtonStyle(btnSaveTMTop);
            btnSaveTMTop.Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold);
            this.Controls.Add(btnReturnTMTop); this.Controls.Add(btnSaveTMTop);
            btnReturnTMTop.BringToFront(); btnSaveTMTop.BringToFront();

            if (!string.IsNullOrWhiteSpace(state.ConfigurationJson))
            {
                try
                {
                    var data = JsonConvert.DeserializeObject<TuringMachineData>(state.ConfigurationJson);
                    if (data != null)
                    {
                        alphabetTextBox.Text = data.Alphabet ?? "01";
                        tapeControl.tapeRenderer.AllowedSymbols = alphabetTextBox.Text + "_";
                        tapeControl.tapeRenderer._tape.Clear();
                        tapeControl.tapeRenderer._tape[0] = new TapeCell(' ');
                        if (data.TapeContent != null) foreach (var kvp in data.TapeContent) tapeControl.tapeRenderer._tape[kvp.Key] = new TapeCell(kvp.Value == '_' ? ' ' : kvp.Value);
                        tapeControl.SetHeadPosition(data.HeadPosition);
                        states.Clear();
                        if (data.States != null) foreach (var st in data.States) states.Add(st);
                        UpdateStateTableColumns(); UpdateStateTable();
                        if (data.TransitionRules != null) RestoreTableData(data.TransitionRules);
                        var problemTextBox = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
                        if (problemTextBox != null) problemTextBox.Text = data.ProblemCondition ?? "";
                    }
                }
                catch { ShowErrorDialog("Не удалось загрузить ранее сохранённую конфигурацию машины Тьюринга. Поле будет очищено."); }
            }
            else
            {
                ClearInterpreterSilent();
                var problemTextBox = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
                if (problemTextBox != null) problemTextBox.Text = "";
            }

            if (commentTextBox != null) { commentTextBox.Enabled = false; commentTextBox.ReadOnly = true; commentTextBox.BackColor = System.Drawing.SystemColors.Window; commentTextBox.Text = ""; }
            UnlockInterpreter();

            // Защита от двойного срабатывания обработчиков (например, повторный клик
            // во время асинхронной обработки): однократный флаг.
            bool exited = false;

            btnReturnTMTop.Click += (s, e) =>
            {
                if (exited) return;
                exited = true;
                ExitTeacherTMBuilderMode();
                try { onReturn?.Invoke(false); } catch { }
            };
            btnSaveTMTop.Click += (s, e) =>
            {
                if (exited) return;
                exited = true;
                var problemTextBox2 = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
                var currentData = new TuringMachineData
                {
                    Alphabet = alphabetTextBox.Text,
                    TapeContent = tapeControl.tapeRenderer._tape.ToDictionary(k => k.Key, v => v.Value.Value),
                    HeadPosition = tapeControl.tapeRenderer.GetHeadPosition(),
                    States = states.ToList(),
                    TransitionRules = SaveTableData(),
                    ProblemCondition = problemTextBox2?.Text ?? "",
                    Comment = ""
                };
                state.ConfigurationJson = JsonConvert.SerializeObject(currentData);
                ShowSuccessDialog("Конфигурация МТ успешно подготовлена. Для окончательного сохранения нажмите «Сохранить изменения» в окне редактирования задания.");
                ExitTeacherTMBuilderMode();
                try { onReturn?.Invoke(true); } catch { }
            };
        }

        private void ExitTeacherTMBuilderMode()
        {
            if (btnSaveTMTop != null) { this.Controls.Remove(btnSaveTMTop); btnSaveTMTop.Dispose(); btnSaveTMTop = null; }
            if (btnReturnTMTop != null) { this.Controls.Remove(btnReturnTMTop); btnReturnTMTop.Dispose(); btnReturnTMTop = null; }
            if (btnUserMenu != null) btnUserMenu.Visible = true;
            if (commentTextBox != null) { commentTextBox.Enabled = true; commentTextBox.ReadOnly = false; commentTextBox.Text = ""; }
            this.Text = "Интерпретатор машины Тьюринга";
            RestoreMainInterpreterState();
            // Показ parentManageForm/taskForm выполняет вызывающий код — caller
            // знает в каком порядке восстанавливать видимость окон.
        }

        // ──────────────────────────────────────────────────────────
        // Кнопки режима выполнения задания
        // ──────────────────────────────────────────────────────────
        private void SetupTaskExecutionButtons()
        {
            if (btnDraftTop != null) return;
            int rightEdge = this.ClientSize.Width;

            btnReturnTop = new Button { Text = "Вернуться", Width = 90, Height = 25, Top = 0, Left = rightEdge - 90, Anchor = AnchorStyles.Top | AnchorStyles.Right, Visible = false };
            ApplyTextButtonStyle(btnReturnTop);
            btnSubmitTop = new Button { Text = "Сохранить решение", Width = 150, Height = 25, Top = 0, Left = btnReturnTop.Left - 150, Anchor = AnchorStyles.Top | AnchorStyles.Right, Visible = false };
            ApplyTextButtonStyle(btnSubmitTop); btnSubmitTop.Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold);
            btnDraftTop = new Button { Text = "Сохранить и продолжить", Width = 170, Height = 25, Top = 0, Left = btnSubmitTop.Left - 170, Anchor = AnchorStyles.Top | AnchorStyles.Right, Visible = false };
            ApplyTextButtonStyle(btnDraftTop);
            btnResetSolutionTop = new Button { Text = "Сбросить решение", Width = 130, Height = 25, Top = 0, Left = btnDraftTop.Left - 130, Anchor = AnchorStyles.Top | AnchorStyles.Right, Visible = false };
            ApplyTextButtonStyle(btnResetSolutionTop);
            btnRevokeTop = new Button { Text = "Отозвать работу", Width = 140, Height = 25, Top = 0, Left = btnReturnTop.Left - 140, Anchor = AnchorStyles.Top | AnchorStyles.Right, Visible = false };
            ApplyTextButtonStyle(btnRevokeTop);
            btnResetToNewMTTop = new Button { Text = "Начать с новой МТ", Width = 145, Height = 25, Top = 0, Left = btnResetSolutionTop.Left - 145, Anchor = AnchorStyles.Top | AnchorStyles.Right, Visible = false };
            ApplyTextButtonStyle(btnResetToNewMTTop);
            btnResetToNewMTTop.BackColor = System.Drawing.Color.FromArgb(255, 220, 180);

            // Плашка «МТ обновлена» — полоса под кнопками
            panelMTOutdated = new Panel
            {
                Height = 28,
                Dock = DockStyle.Top,
                BackColor = System.Drawing.Color.FromArgb(255, 243, 205),
                Visible = false
            };
            var lblOutdated = new Label
            {
                Text = "⚠  Преподаватель обновил МТ задания. Ваше решение основано на старой версии. Сохраните его в файл, затем нажмите «Начать с новой МТ».",
                Dock = DockStyle.Fill,
                Font = new System.Drawing.Font("Segoe UI", 8.5f, System.Drawing.FontStyle.Bold),
                ForeColor = System.Drawing.Color.FromArgb(133, 77, 0),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };
            panelMTOutdated.Controls.Add(lblOutdated);
            this.Controls.Add(panelMTOutdated);
            panelMTOutdated.BringToFront();

            void DisableExecutionButtons() { if (btnDraftTop != null) btnDraftTop.Enabled = false; if (btnSubmitTop != null) btnSubmitTop.Enabled = false; if (btnResetSolutionTop != null) btnResetSolutionTop.Enabled = false; if (btnRevokeTop != null) btnRevokeTop.Enabled = false; if (btnResetToNewMTTop != null) btnResetToNewMTTop.Enabled = false; }

            async System.Threading.Tasks.Task<bool> EnsureExecutionStillAvailable()
            {
                if (!activeAssignmentId.HasValue) return false;
                var fresh = await ApiClient.GetAssignmentStateAsync(activeAssignmentId.Value);
                if (fresh == null || fresh.IsDeleted) { DisableExecutionButtons(); ShowWarningDialog("Задание было удалено преподавателем. Сохраните МТ через «Файл → Экспорт программы» и покиньте окно выполнения."); return false; }
                if (fresh.Status == "Архив" || fresh.Status == "Скрыто" || fresh.Status == "Черновик") { DisableExecutionButtons(); ShowWarningDialog("Задание было переведено в архив и сейчас недоступно. Сохраните МТ через «Файл → Экспорт программы» и покиньте окно выполнения."); return false; }
                if (fresh.CourseArchived == 1) { DisableExecutionButtons(); ShowWarningDialog("Курс этого задания был отправлен в архив. Сохраните МТ через «Файл → Экспорт программы» и покиньте окно выполнения."); return false; }
                if (DateTime.Now >= fresh.Deadline) { DisableExecutionButtons(); ShowWarningDialog($"Срок сдачи задания истёк ({fresh.Deadline:dd.MM.yyyy HH:mm}). Изменение решения больше невозможно. Сохраните МТ через «Файл → Экспорт программы» и покиньте окно выполнения."); return false; }
                return true;
            }

            btnRevokeTop.Click += async (s, e) =>
            {
                if (activeAssignmentId == null) return;
                if (!await EnsureExecutionStillAvailable()) return;
                if (!ShowConfirmDialog("Отозвать работу с проверки?\n\nРешение будет переведено в статус «Не сдано». Вы сможете внести правки и снова сохранить — но только пока не истёк срок сдачи и преподаватель не начал проверку.", "Отозвать работу")) return;

                btnRevokeTop.Text = "Отзыв..."; btnRevokeTop.Enabled = false;
                bool success = await ApiClient.RevokeSubmissionAsync(activeAssignmentId.Value);
                if (success)
                {
                    DataRefreshBus.Raise("Submission", "Updated", activeAssignmentId.Value);
                    ShowSuccessDialog("Работа отозвана. Теперь вы можете редактировать решение и снова его сохранить.");
                    btnRevokeTop.Visible = false;
                    btnDraftTop.Visible = btnSubmitTop.Visible = btnResetSolutionTop.Visible = true;
                    this.Text = this.Text.Replace("Просмотр работы", "Выполнение задания");
                    isTaskReadOnlyMode = false;
                    UnlockInterpreter();
                    var problemTextBox = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
                    if (problemTextBox != null) problemTextBox.ReadOnly = true;
                    tapeControl.tapeRenderer.ValueChanged -= MarkTaskDirty;
                    stateTable.CellValueChanged -= MarkTaskDirty;
                    if (commentTextBox != null) commentTextBox.TextChanged -= MarkTaskDirty;
                    tapeControl.tapeRenderer.ValueChanged += MarkTaskDirty;
                    stateTable.CellValueChanged += MarkTaskDirty;
                    if (commentTextBox != null) commentTextBox.TextChanged += MarkTaskDirty;
                }
                btnRevokeTop.Text = "Отозвать работу"; btnRevokeTop.Enabled = true;
            };

            btnResetSolutionTop.Click += (s, e) =>
            {
                if (string.IsNullOrEmpty(originalAssignmentDescription)) { ShowWarningDialog("Не удалось получить исходную конфигурацию задания от преподавателя."); return; }
                if (!ShowConfirmDialog("Сбросить решение к исходной конфигурации задания?\n\nВсе ваши текущие изменения МТ и комментарий будут потеряны.", "Сброс решения")) return;
                try
                {
                    var data = JsonConvert.DeserializeObject<TuringMachineData>(originalAssignmentDescription);
                    if (data != null)
                    {
                        alphabetTextBox.Text = data.Alphabet ?? "01";
                        tapeControl.tapeRenderer.AllowedSymbols = alphabetTextBox.Text + "_";
                        tapeControl.tapeRenderer._tape.Clear(); tapeControl.tapeRenderer._tape[0] = new TapeCell(' ');
                        if (data.TapeContent != null) foreach (var kvp in data.TapeContent) tapeControl.tapeRenderer._tape[kvp.Key] = new TapeCell(kvp.Value == '_' ? ' ' : kvp.Value);
                        tapeControl.SetHeadPosition(data.HeadPosition);
                        states.Clear();
                        if (data.States != null && data.States.Count > 0) foreach (var st in data.States) states.Add(st);
                        else states.Add(new TuringState { Name = "q0", IsInitial = true, IsFinal = false });
                        UpdateStateTableColumns(); UpdateStateTable();
                        if (data.TransitionRules != null) RestoreTableData(data.TransitionRules);
                        var problemTextBox = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
                        if (problemTextBox != null) problemTextBox.Text = data.ProblemCondition ?? "";
                        if (commentTextBox != null) commentTextBox.Text = "";
                        isTaskExecutionDirty = true;
                        ShowSuccessDialog("Решение сброшено к исходной конфигурации преподавателя. Не забудьте сохранить.");
                    }
                }
                catch { ShowErrorDialog("Не удалось разобрать исходную конфигурацию задания."); }
            };

            btnDraftTop.Click += async (s, e) =>
            {
                if (activeAssignmentId == null) return;
                if (!await EnsureExecutionStillAvailable()) return;
                var pTextBox = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
                var currentData = new TuringMachineData { Alphabet = alphabetTextBox.Text, TapeContent = tapeControl.tapeRenderer._tape.ToDictionary(k => k.Key, v => v.Value.Value), HeadPosition = tapeControl.tapeRenderer.GetHeadPosition(), States = states.ToList(), TransitionRules = SaveTableData(), ProblemCondition = pTextBox?.Text ?? "", Comment = commentTextBox.Text };
                btnDraftTop.Text = "Сохранение..."; btnDraftTop.Enabled = false;
                bool success = await ApiClient.SaveDraftAsync(activeAssignmentId.Value, JsonConvert.SerializeObject(currentData));
                if (success) { isTaskExecutionDirty = false; DataRefreshBus.Raise("Submission", "Updated", activeAssignmentId.Value); ShowSuccessDialog("Решение сохранено и считается загруженным. Вы можете продолжить редактирование — пока не истёк срок сдачи или преподаватель не начал проверку."); }
                btnDraftTop.Text = "Сохранить и продолжить"; btnDraftTop.Enabled = btnDraftTop.Visible;
            };

            btnSubmitTop.Click += async (s, e) =>
            {
                if (activeAssignmentId == null) return;
                if (!await EnsureExecutionStillAvailable()) return;
                if (!ShowConfirmDialog("Сохранить решение и закрыть окно?\n\nРешение будет считаться загруженным (статус «Не оценено»). Вы сможете отозвать его и внести правки, пока не истёк срок сдачи и преподаватель не начал проверку.", "Сохранение решения")) return;
                var pTextBox = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
                var currentData = new TuringMachineData { Alphabet = alphabetTextBox.Text, TapeContent = tapeControl.tapeRenderer._tape.ToDictionary(k => k.Key, v => v.Value.Value), HeadPosition = tapeControl.tapeRenderer.GetHeadPosition(), States = states.ToList(), TransitionRules = SaveTableData(), Comment = commentTextBox.Text, ProblemCondition = pTextBox?.Text ?? "" };
                btnSubmitTop.Text = "Сохранение..."; btnSubmitTop.Enabled = false;
                bool draftOk = await ApiClient.SaveDraftAsync(activeAssignmentId.Value, JsonConvert.SerializeObject(currentData));
                if (!draftOk) { btnSubmitTop.Text = "Сохранить решение"; btnSubmitTop.Enabled = true; return; }
                await ApiClient.SubmitAssignmentAsync(activeAssignmentId.Value);
                DataRefreshBus.Raise("Submission", "Updated", activeAssignmentId.Value);
                ShowSuccessDialog("Решение успешно сохранено и загружено!");
                ExitTaskExecutionMode();
            };

            btnResetToNewMTTop.Click += async (s, e) =>
            {
                if (activeAssignmentId == null) return;
                if (!ShowConfirmDialog(
                    "Начать заново с новой версией МТ от преподавателя?\n\nВаше текущее решение будет удалено с сервера. Заранее экспортируйте его через «Файл → Экспорт программы».",
                    "Сброс к новой версии МТ")) return;
                btnResetToNewMTTop.Text = "Сброс..."; btnResetToNewMTTop.Enabled = false;
                bool ok = await ApiClient.ResetSubmissionAsync(activeAssignmentId.Value);
                if (ok)
                {
                    DataRefreshBus.Raise("Submission", "Deleted", activeAssignmentId.Value);
                    ShowSuccessDialog("Решение сброшено. Окно выполнения закроется — откройте задание заново, чтобы начать с новой МТ.");
                    ExitTaskExecutionMode();
                }
                else
                {
                    btnResetToNewMTTop.Text = "Начать с новой МТ"; btnResetToNewMTTop.Enabled = true;
                }
            };

            btnReturnTop.Click += (s, e) =>
            {
                if (isTaskExecutionDirty && !isTaskReadOnlyMode && btnDraftTop.Enabled)
                {
                    if (!ShowConfirmDialog("У вас есть несохранённые изменения. Выйти без сохранения?\n\nНесохранённое решение будет потеряно.", "Внимание")) return;
                }
                ExitTaskExecutionMode();
            };

            this.Controls.Add(btnReturnTop); this.Controls.Add(btnSubmitTop); this.Controls.Add(btnDraftTop);
            this.Controls.Add(btnResetSolutionTop); this.Controls.Add(btnRevokeTop); this.Controls.Add(btnResetToNewMTTop);
            btnReturnTop.BringToFront(); btnSubmitTop.BringToFront(); btnDraftTop.BringToFront();
            btnResetSolutionTop.BringToFront(); btnRevokeTop.BringToFront(); btnResetToNewMTTop.BringToFront();
        }

        // ──────────────────────────────────────────────────────────
        // Вход / выход из режима выполнения задания
        // ──────────────────────────────────────────────────────────
        private void EnterTaskExecutionMode(int assignmentId, string taskName, string jsonToLoad, bool isReadOnlyMode, bool canRevoke, Form menuRef, bool isOutdated = false)
        {
            BackupMainInterpreterState();
            SetupTaskExecutionButtons();
            studentTasksMenuRef = menuRef;
            activeAssignmentId  = assignmentId;
            isTaskReadOnlyMode  = isReadOnlyMode;

            if (btnUserMenu != null) btnUserMenu.Visible = false;
            if (btnExecution != null) btnExecution.Enabled = true;

            bool showOutdatedControls = isOutdated && !isReadOnlyMode;

            btnReturnTop.Visible            = true;
            btnDraftTop.Visible             = !isReadOnlyMode;
            btnSubmitTop.Visible            = !isReadOnlyMode;
            btnResetSolutionTop.Visible     = !isReadOnlyMode;
            btnResetToNewMTTop.Visible      = showOutdatedControls;
            panelMTOutdated.Visible         = showOutdatedControls;
            btnRevokeTop.Visible            = isReadOnlyMode && canRevoke;
            btnDraftTop.Enabled             = !isReadOnlyMode;
            btnSubmitTop.Enabled            = !isReadOnlyMode;
            btnResetSolutionTop.Enabled     = !isReadOnlyMode;
            btnResetToNewMTTop.Enabled      = showOutdatedControls;
            btnRevokeTop.Enabled            = isReadOnlyMode && canRevoke;

            this.Text = isReadOnlyMode ? $"Просмотр работы — {taskName}" : $"Выполнение задания — {taskName}";

            var problemTextBox = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
            if (problemTextBox != null) problemTextBox.ReadOnly = true;

            string[] blockedButtonTags = { "New", "Load", "Reset" };
            foreach (var btn in controlPanel.Controls.OfType<Button>())
                if (btn.Tag != null && blockedButtonTags.Contains(btn.Tag.ToString())) btn.Enabled = false;

            try
            {
                var data = JsonConvert.DeserializeObject<TuringMachineData>(jsonToLoad);
                if (data != null)
                {
                    alphabetTextBox.Text = data.Alphabet ?? "01";
                    tapeControl.tapeRenderer.AllowedSymbols = alphabetTextBox.Text + "_";
                    tapeControl.tapeRenderer._tape.Clear(); tapeControl.tapeRenderer._tape[0] = new TapeCell(' ');
                    if (data.TapeContent != null) foreach (var kvp in data.TapeContent) tapeControl.tapeRenderer._tape[kvp.Key] = new TapeCell(kvp.Value == '_' ? ' ' : kvp.Value);
                    tapeControl.SetHeadPosition(data.HeadPosition);
                    states.Clear();
                    if (data.States != null && data.States.Count > 0) foreach (var st in data.States) states.Add(st);
                    else states.Add(new TuringState { Name = "q0", IsInitial = true, IsFinal = false });
                    UpdateStateTableColumns(); UpdateStateTable();
                    if (data.TransitionRules != null) RestoreTableData(data.TransitionRules);
                    if (problemTextBox != null) problemTextBox.Text = data.ProblemCondition ?? "";
                    if (commentTextBox != null) commentTextBox.Text = data.Comment ?? "";
                }
            }
            catch { ShowErrorDialog("Не удалось загрузить конфигурацию стенда."); }

            tapeControl.tapeRenderer.ValueChanged -= MarkTaskDirty;
            stateTable.CellValueChanged -= MarkTaskDirty;
            if (commentTextBox != null) commentTextBox.TextChanged -= MarkTaskDirty;

            if (isReadOnlyMode) LockInterpreterForChecking();
            else
            {
                UnlockInterpreter();
                if (problemTextBox != null) problemTextBox.ReadOnly = true;
                tapeControl.tapeRenderer.ValueChanged += MarkTaskDirty;
                stateTable.CellValueChanged += MarkTaskDirty;
                if (commentTextBox != null) commentTextBox.TextChanged += MarkTaskDirty;
            }

            if (studentTasksMenuRef != null) studentTasksMenuRef.Hide();
            isTaskExecutionDirty = false;
            UpdateButtonsState();
        }

        private void ExitTaskExecutionMode()
        {
            activeAssignmentId = null;
            originalAssignmentDescription = null;
            originalStudentSolution = null;

            if (btnDraftTop != null)         btnDraftTop.Visible = false;
            if (btnSubmitTop != null)        btnSubmitTop.Visible = false;
            if (btnResetSolutionTop != null) btnResetSolutionTop.Visible = false;
            if (btnReturnTop != null)        btnReturnTop.Visible = false;
            if (btnRevokeTop != null)        btnRevokeTop.Visible = false;
            if (btnResetToNewMTTop != null)  btnResetToNewMTTop.Visible = false;
            if (panelMTOutdated != null)     panelMTOutdated.Visible = false;

            if (btnUserMenu != null) btnUserMenu.Visible = true;
            if (btnFile != null) btnFile.Enabled = true;

            this.Text = "Интерпретатор машины Тьюринга";

            var problemTextBox = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
            if (problemTextBox != null) problemTextBox.ReadOnly = false;

            UnlockInterpreter();
            RestoreMainInterpreterState();

            string[] blockedButtonTags = { "New", "Load", "Save", "SaveAs", "Print", "Export", "Reset" };
            foreach (var btn in controlPanel.Controls.OfType<Button>())
                if (btn.Tag != null && blockedButtonTags.Contains(btn.Tag.ToString())) btn.Enabled = true;

            if (studentTasksMenuRef != null) { studentTasksMenuRef.Show(); studentTasksMenuRef = null; }
        }
    }
}
