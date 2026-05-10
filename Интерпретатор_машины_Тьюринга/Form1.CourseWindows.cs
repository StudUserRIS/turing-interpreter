using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Интерпретатор_машины_Тьюринга
{
    public partial class Form1
    {
        public static double CalculateFinalGrade(IEnumerable<(string Type, int? Grade)> tasks, string policyJson)
        {
            // Защита от пустой/некорректной политики и пустого набора оценок
            if (tasks == null) return 0;
            if (string.IsNullOrWhiteSpace(policyJson)) return 0;
            Dictionary<string, double> weights;
            try
            {
                weights = JsonConvert.DeserializeObject<Dictionary<string, double>>(policyJson)
                          ?? new Dictionary<string, double>();
            }
            catch
            {
                return 0;
            }
            if (weights.Count == 0) return 0;

            var publishedTypes = tasks.Select(t => t.Type).Distinct().ToList();
            double totalActiveWeight = 0;
            var activeWeights = new Dictionary<string, double>();
            bool hasCustomPolicy = weights.Count > 0;

            // Шаг 1: Собираем веса только тех категорий, которые реально выданы
            foreach (var pt in publishedTypes)
            {
                double w = hasCustomPolicy ? (weights.ContainsKey(pt) ? weights[pt] : 0.0) : 1.0;
                if (w > 0)
                {
                    activeWeights[pt] = w;
                    totalActiveWeight += w;
                }
            }

            if (totalActiveWeight == 0) return 0;

            // Шаг 2: Считаем пропорциональную оценку
            double finalScore = 0;
            foreach (var pt in activeWeights.Keys)
            {
                var typeTasks = tasks.Where(t => t.Type == pt).ToList();
                double avgType = typeTasks.Average(t => t.Grade ?? 0); // Несданные работы считаются как 0
                double normalizedWeight = activeWeights[pt] / totalActiveWeight;
                finalScore += avgType * normalizedWeight;
            }

            return finalScore;
        }
        private void ShowGradingPolicyWindow(int courseId, string courseName, string currentPolicy)
        {
            bool isDirty = false;
            bool isSaved = false;
            bool isClosing = false;
            bool hasErrors = false;

            Form gradingForm = new Form
            {
                Text = "Формула оценки: " + courseName,
                ClientSize = new Size(560, 400),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.WhiteSmoke
            };

            Label lblInfo = new Label
            {
                Text = "Укажите веса для типов заданий (например, 0.4 или 2).\nСистема автоматически рассчитает их реальную долю в итоговой оценке.",
                Location = new Point(15, 15),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            DataGridView grid = new DataGridView
            {
                Location = new Point(15, 60),
                Size = new Size(530, 235),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Тип задания", ReadOnly = true, FillWeight = 45 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Weight", HeaderText = "Вес (ввод)", FillWeight = 25 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Percent", HeaderText = "Доля (%)", ReadOnly = true, FillWeight = 30 });
            ApplyModernTableStyle(grid);
            grid.Columns[2].DefaultCellStyle.BackColor = Color.WhiteSmoke;
            grid.Columns[2].DefaultCellStyle.ForeColor = Color.DimGray;
            grid.Columns[2].DefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);

            void LoadFromPolicy(string policyJson)
            {
                grid.Rows.Clear();
                var weights = new Dictionary<string, double>();
                if (!string.IsNullOrWhiteSpace(policyJson))
                    try { weights = JsonConvert.DeserializeObject<Dictionary<string, double>>(policyJson); } catch { }
                string[] defaultTypes = { "Домашняя работа", "Лабораторная работа", "Самостоятельная работа", "Контрольная работа", "Экзамен" };
                foreach (var type in defaultTypes)
                {
                    double w = weights.ContainsKey(type) ? weights[type] : 1.0;
                    grid.Rows.Add(type, w.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), "0%");
                }
            }

            LoadFromPolicy(currentPolicy);

            Button btnSave = new Button { Text = "Сохранить", Location = new Point(435, 355), Size = new Size(110, 30), Font = new Font("Segoe UI", 9), FlatStyle = FlatStyle.Standard };
            Button btnRefresh = new Button { Text = "🔄 Обновить", Location = new Point(15, 355), Size = new Size(110, 30), Font = new Font("Segoe UI", 9), FlatStyle = FlatStyle.Standard };

            void RecalculatePercentages()
            {
                double totalWeight = 0;
                hasErrors = false;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    string wStr = row.Cells[1].Value?.ToString()?.Replace(",", ".") ?? "0";
                    if (double.TryParse(wStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double w))
                    {
                        if (w < 0) { row.Cells[1].Style.BackColor = Color.LightPink; hasErrors = true; }
                        else { row.Cells[1].Style.BackColor = Color.White; totalWeight += w; }
                    }
                    else { row.Cells[1].Style.BackColor = Color.LightPink; hasErrors = true; }
                }
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (hasErrors || totalWeight == 0) row.Cells[2].Value = "—";
                    else
                    {
                        string wStr = row.Cells[1].Value?.ToString()?.Replace(",", ".") ?? "0";
                        if (double.TryParse(wStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double w) && w >= 0)
                        {
                            double pct = (w / totalWeight) * 100;
                            row.Cells[2].Value = pct.ToString("0.0") + "%";
                        }
                    }
                }
            }
            RecalculatePercentages();

            grid.CellValueChanged += (s, e) => { if (e.ColumnIndex == 1) { isDirty = true; RecalculatePercentages(); } };
            grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty && grid.CurrentCell.ColumnIndex == 1) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };

            // Реализация перезагрузки актуальных данных из БД — используется как кнопкой
            // "🔄 Обновить", так и автоматически при срабатывании DataRefreshBus.
            async Task ReloadFromServer(bool fromAutoBus)
            {
                try
                {
                    var courses = await ApiClient.GetMyCoursesAsync();
                    var fresh = courses?.FirstOrDefault(c => c.Id == courseId);
                    if (fresh == null)
                    {
                        // Курс был удалён другим пользователем — закрываем окно с предупреждением.
                        if (!gradingForm.IsDisposed)
                        {
                            ShowWarningDialog($"Курс «{courseName}» был удалён другим пользователем. Окно «Формула оценки» будет закрыто.");
                            isClosing = true;
                            isDirty = false;
                            gradingForm.Close();
                        }
                        return;
                    }
                    // Если есть несохранённые изменения и обновление пришло автоматически —
                    // не перетираем поле, чтобы не потерять работу пользователя.
                    if (fromAutoBus && isDirty) return;

                    LoadFromPolicy(fresh.GradingPolicy);
                    isDirty = false;
                    RecalculatePercentages();
                }
                catch { }
            }

            async Task<bool> PerformSave()
            {
                grid.EndEdit();
                RecalculatePercentages();
                if (hasErrors) { ShowErrorDialog("Пожалуйста, исправьте ошибки (выделены красным) перед сохранением.\nВес должен быть положительным числом или нулём."); return false; }
                var newWeights = new Dictionary<string, double>();
                foreach (DataGridViewRow row in grid.Rows)
                {
                    string type = row.Cells[0].Value.ToString();
                    string wStr = row.Cells[1].Value?.ToString()?.Replace(",", ".") ?? "0";
                    if (double.TryParse(wStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double w))
                        newWeights[type] = w;
                }
                btnSave.Text = "Сохранение..."; btnSave.Enabled = false;
                if (await ApiClient.UpdateGradingPolicyAsync(courseId, JsonConvert.SerializeObject(newWeights)))
                {
                    isSaved = true; isDirty = false;
                    ShowSuccessDialog("Формула оценки успешно сохранена!");
                    // Локально триггерим шину: окна "Мои курсы" и "Детализация оценок"
                    // сразу пересчитают итоговую оценку, не дожидаясь SignalR-эха.
                    DataRefreshBus.Raise("GradingPolicy", "Updated", courseId);
                    btnSave.Text = "Сохранить"; btnSave.Enabled = true;
                    return true;
                }
                ShowErrorDialog("Ошибка сохранения в базу данных.");
                btnSave.Enabled = true; btnSave.Text = "Сохранить";
                return false;
            }

            btnSave.Click += async (s, e) => { if (await PerformSave()) { isClosing = true; gradingForm.Close(); } };

            btnRefresh.Click += async (s, e) =>
            {
                if (isDirty)
                {
                    if (!ShowConfirmDialog("У вас есть несохранённые изменения. Обновить и потерять их?", "Обновление")) return;
                }
                await ReloadFromServer(false);
            };

            gradingForm.FormClosing += async (s, e) =>
            {
                if (isDirty && !isSaved && !isClosing)
                {
                    e.Cancel = true;
                    var res = MessageBox.Show("У вас есть несохранённые изменения.\nСохранить перед выходом?", "Внимание", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                    if (res == DialogResult.Cancel) return;
                    if (res == DialogResult.Yes) { bool saved = await PerformSave(); if (!saved) return; }
                    isClosing = true;
                    gradingForm.Close();
                }
            };

            // Автоматическое обновление окна «Формула оценки» при изменениях, пришедших
            // через SignalR push (другой админ изменил формулу, или курс был удалён).
            // Логика: реагируем на сущности GradingPolicy / Course (только для текущего курса),
            // а также на любое связанное удаление курса — в этом случае окно закрывается.
            Action<string, string, int> onBusChanged = (entity, action, id) =>
            {
                if (gradingForm.IsDisposed) return;
                bool relevant = (entity == "GradingPolicy" && id == courseId)
                                || entity == "Course"
                                || entity == "CourseGroups";
                if (!relevant) return;
                try
                {
                    gradingForm.BeginInvoke(new Action(async () => { try { await ReloadFromServer(true); } catch { } }));
                }
                catch { }
            };
            DataRefreshBus.Changed += onBusChanged;
            gradingForm.FormClosed += (s, e) => { DataRefreshBus.Changed -= onBusChanged; };

            gradingForm.Controls.AddRange(new Control[] { lblInfo, grid, btnRefresh, btnSave });
            gradingForm.ShowDialog(this);
        }


        // Read-only вариант для студента
        // Read-only вариант для студента
        private void ShowGradingPolicyViewerWindow(int courseId, string courseName, string currentPolicy)
        {
            Form viewer = new Form
            {
                Text = "Формула оценки (просмотр): " + courseName,
                ClientSize = new Size(560, 400),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.WhiteSmoke
            };

            Label lblInfo = new Label
            {
                Text = "Веса типов заданий, заданные преподавателем.\nИтоговая оценка курса формируется по этой формуле.",
                Location = new Point(15, 15),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            DataGridView grid = new DataGridView
            {
                Location = new Point(15, 60),
                Size = new Size(530, 235),
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Тип задания", FillWeight = 45 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Weight", HeaderText = "Вес", FillWeight = 25 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Percent", HeaderText = "Доля (%)", FillWeight = 30 });
            ApplyModernTableStyle(grid);

            void LoadPolicy(string policyJson)
            {
                grid.Rows.Clear();
                var weights = new Dictionary<string, double>();
                if (!string.IsNullOrWhiteSpace(policyJson))
                    try { weights = JsonConvert.DeserializeObject<Dictionary<string, double>>(policyJson); } catch { }

                double total = weights.Values.Where(v => v > 0).Sum();

                string[] defaultTypes = { "Домашняя работа", "Лабораторная работа", "Самостоятельная работа", "Контрольная работа", "Экзамен" };
                foreach (var t in defaultTypes)
                {
                    double w = weights.ContainsKey(t) ? weights[t] : 0;
                    string pct = total > 0 ? ((w / total) * 100).ToString("0.0") + "%" : "—";
                    grid.Rows.Add(t, w.ToString("0.0"), pct);
                }
            }
            LoadPolicy(currentPolicy);

            Button btnRefresh = new Button { Text = "🔄 Обновить", Location = new Point(15, 355), Size = new Size(110, 30), FlatStyle = FlatStyle.Standard };
            Button btnClose = new Button { Text = "Закрыть", Location = new Point(460, 355), Size = new Size(85, 30), FlatStyle = FlatStyle.Standard };

            // Подтягиваем актуальную формулу с сервера. Используется как для ручного,
            // так и для автоматического (через шину DataRefreshBus) обновления.
            async Task ReloadFromServer()
            {
                try
                {
                    var courses = await ApiClient.GetMyCoursesAsync();
                    var fresh = courses?.FirstOrDefault(c => c.Id == courseId);
                    if (fresh == null)
                    {
                        if (!viewer.IsDisposed)
                        {
                            ShowWarningDialog($"Курс «{courseName}» больше недоступен. Окно «Формула оценки» будет закрыто.");
                            viewer.Close();
                        }
                        return;
                    }
                    LoadPolicy(fresh.GradingPolicy);
                }
                catch { }
            }

            btnRefresh.Click += async (s, e) => await ReloadFromServer();
            btnClose.Click += (s, e) => viewer.Close();

            // Автоматическое обновление окна «Формула оценки (просмотр)» при изменении
            // формулы преподавателем или удалении курса — без участия студента.
            Action<string, string, int> onBusChanged = (entity, action, id) =>
            {
                if (viewer.IsDisposed) return;
                bool relevant = (entity == "GradingPolicy" && id == courseId)
                                || entity == "Course"
                                || entity == "CourseGroups";
                if (!relevant) return;
                try
                {
                    viewer.BeginInvoke(new Action(async () => { try { await ReloadFromServer(); } catch { } }));
                }
                catch { }
            };
            DataRefreshBus.Changed += onBusChanged;
            viewer.FormClosed += (s, e) => { DataRefreshBus.Changed -= onBusChanged; };

            viewer.Controls.AddRange(new Control[] { lblInfo, grid, btnRefresh, btnClose });
            viewer.ShowDialog(this);
        }

        // ИСПРАВЛЕНИЕ: Изменен тип параметра deadlineStr на DateTime deadline
        private async void ShowAssignmentWindow(int assignmentId, string courseName, string taskName, string taskType, DateTime deadline, string status, string grade, string description, Form parentForm = null)
        {
            // Актуальная версия конфигурации задания. Используется при входе в окно
            // выполнения, чтобы сообщить серверу версию, на которую опирается студент,
            // и при сохранении корректно сбрасывать/сохранять флаг IsOutdated.
            int currentConfigVersion = 1;
            if (assignmentId > 0)
            {
                var freshState = await ApiClient.GetAssignmentStateAsync(assignmentId);
                if (freshState == null || freshState.IsDeleted)
                {
                    ShowWarningDialog("Данные были изменены, обновите окно");
                    return;
                }
                // НОВАЯ ЛОГИКА: только статус "Опубликовано" даёт студенту доступ к заданию.
                if (freshState.Status != "Опубликовано")
                {
                    ShowWarningDialog("Данные были изменены, обновите окно");
                    return;
                }
                if (freshState.CourseArchived == 1)
                {
                    ShowWarningDialog("Данные были изменены, обновите окно");
                    return;
                }
                taskName = freshState.Title ?? taskName;
                taskType = freshState.Type ?? taskType;
                deadline = freshState.Deadline;
                status = freshState.Status ?? status;
                description = freshState.Description ?? description;
                currentConfigVersion = freshState.ConfigVersion;
            }

            Submission mySubmission = null;
            if (assignmentId > 0)
                mySubmission = await ApiClient.GetMySubmissionAsync(assignmentId);

            string submissionStatus = mySubmission?.Status;

            Form taskDetailForm = new Form
            {
                Text = "Информация о задании",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.WhiteSmoke
            };

            Font fontBold = new Font("Segoe UI", 9, FontStyle.Bold);
            Font fontRegular = new Font("Segoe UI", 9, FontStyle.Regular);

            int currentY = 20; int spacing = 32; int leftX = 20; int rightX = 110; int inputW = 290;

            void AddRow(string labelText, string valueText, Color? textColor = null)
            {
                Label lbl = new Label { Text = labelText, Location = new Point(leftX, currentY + 3), AutoSize = true, Font = fontBold, ForeColor = Color.DimGray };
                TextBox txt = new TextBox { Text = valueText, Location = new Point(rightX, currentY), Width = inputW, ReadOnly = true, BackColor = SystemColors.Window, Font = fontRegular };
                if (textColor.HasValue) txt.ForeColor = textColor.Value;
                taskDetailForm.Controls.Add(lbl);
                taskDetailForm.Controls.Add(txt);
                currentY += spacing;
            }

            AddRow("Курс:", courseName);
            AddRow("Задание:", taskName);
            AddRow("Тип:", taskType);
            AddRow("Срок сдачи:", deadline.ToString("dd.MM.yyyy HH:mm"));

            bool isDeadlinePassed = DateTime.Now >= deadline;

            // НОВАЯ ЛОГИКА статусов работы студента: "Не сдано", "Не оценено", "Оценено".
            string displayStatus;
            if (string.IsNullOrEmpty(submissionStatus) || submissionStatus == "Не сдано")
            {
                displayStatus = isDeadlinePassed ? "Срок вышел (Не сдано)" : "Не сдано";
            }
            else if (submissionStatus == "Не оценено")
            {
                displayStatus = "Не оценено";
            }
            else if (submissionStatus == "Оценено")
            {
                displayStatus = "Оценено";
            }
            else
            {
                displayStatus = submissionStatus;
            }

            Color statusColor = Color.Black;
            if (displayStatus == "Оценено") statusColor = Color.ForestGreen;
            else if (displayStatus == "Не оценено") statusColor = Color.DodgerBlue;
            else if (displayStatus.Contains("Не сдано")) statusColor = Color.IndianRed;

            AddRow("Статус:", displayStatus, statusColor);

            string displayGrade = mySubmission?.Grade?.ToString() ?? grade;
            AddRow("Оценка:", displayGrade);

            if (mySubmission != null && !string.IsNullOrEmpty(mySubmission.TeacherComment))
            {
                currentY += 5;
                Label lblComment = new Label { Text = "Комментарий преподавателя:", Location = new Point(leftX, currentY), AutoSize = true, Font = fontBold, ForeColor = Color.DarkRed };
                currentY += 25;
                TextBox txtComment = new TextBox { Text = mySubmission.TeacherComment, Location = new Point(leftX, currentY), Width = inputW + (rightX - leftX), Height = 55, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = SystemColors.Info, Font = fontRegular };
                taskDetailForm.Controls.Add(lblComment);
                taskDetailForm.Controls.Add(txtComment);
                currentY += 70;
            }

            bool isOutdated = (mySubmission?.IsOutdated ?? 0) == 1;

            // Логика кнопок:
            //   • Если работа в "Оценено" — кнопка "Просмотр решения" (read-only, во всю ширину).
            //   • Если работа в "Не оценено" (отправлена на проверку) — кнопка "Просмотр решения"
            //     (отозвать работу можно из окна выполнения).
            //   • Если есть сохранённый черновик и дедлайн не вышел —
            //     "Начать с начала" + "Продолжить работу".
            //   • Если работы ещё нет — одна кнопка "Приступить" во всю ширину.
            //   • Если дедлайн истёк — только просмотр (если что-то загружено).
            //
            // ВАЖНО (UX): набор кнопок при наличии и отсутствии изменений МТ ОДИНАКОВЫЙ.
            // Окна отличаются только наличием информационной надписи «Начальная МТ у задания
            // была изменена.» над кнопками. Никаких дополнительных кнопок при изменении МТ
            // не появляется.

            bool hasSubmission = mySubmission != null && submissionStatus != "Не сдано";
            bool isGraded = submissionStatus == "Оценено";
            bool isUngraded = submissionStatus == "Не оценено";
            bool isBeingChecked = (mySubmission?.IsBeingChecked ?? 0) == 1;

            bool canRevoke = isUngraded && !isDeadlinePassed && assignmentId > 0 && !isBeingChecked;

            // Read-only режим: если оценено или дедлайн истёк (студент уже не может редактировать).
            bool isReadOnly = isGraded || isDeadlinePassed || isBeingChecked || assignmentId <= 0;

            // Историческая компоновка двух кнопок: 185x30 в окне шириной 420px.
            int formClientWidth = 420;

            if (isOutdated)
            {
                // Только информационная надпись над кнопками. Никаких дополнительных
                // элементов управления — окно отличается от обычного исключительно
                // наличием этой подписи.
                currentY += 5;
                int warnWidth = formClientWidth - leftX * 2;
                int warnHeight = 20;
                Label lblWarn = new Label
                {
                    Text = "Начальная МТ у задания была изменена.",
                    Location = new Point(leftX, currentY),
                    Size = new Size(warnWidth, warnHeight),
                    Font = new Font("Segoe UI", 8, FontStyle.Bold),
                    ForeColor = Color.DarkOrange
                };
                taskDetailForm.Controls.Add(lblWarn);
                currentY += warnHeight + 10;
            }

            currentY += 15;

            // У студента есть сохранённая работа (черновик или загруженное решение),
            // если в submission непустой SolutionJson. На основе этого решаем, нужна
            // ли в окне отдельная кнопка «Начать с начала» или достаточно одной
            // кнопки «Приступить» во всю ширину.
            bool hasSavedWork = mySubmission != null && !string.IsNullOrEmpty(mySubmission.SolutionJson);

            int btnHeight = 30;
            int btnLeftX = 20;
            int fullBtnWidth = formClientWidth - btnLeftX * 2;
            int halfBtnWidth = 185;
            int btnRightX = 215;

            // Кнопка «Начать с начала» показывается только когда у студента есть
            // сохранённая работа и он ещё может её редактировать (не оценено,
            // не на проверке, дедлайн не истёк).
            bool showRestartButton = hasSavedWork && !isReadOnly;

            Button btnRestart = null;
            Button btnStart;

            if (showRestartButton)
            {
                btnRestart = new Button
                {
                    Text = "Начать с начала",
                    Size = new Size(halfBtnWidth, btnHeight),
                    FlatStyle = FlatStyle.Standard,
                    Font = fontRegular,
                    Location = new Point(btnLeftX, currentY)
                };
                btnStart = new Button
                {
                    Size = new Size(halfBtnWidth, btnHeight),
                    FlatStyle = FlatStyle.Standard,
                    Font = fontRegular,
                    Location = new Point(btnRightX, currentY)
                };
            }
            else
            {
                btnStart = new Button
                {
                    Size = new Size(fullBtnWidth, btnHeight),
                    FlatStyle = FlatStyle.Standard,
                    Font = fontRegular,
                    Location = new Point(btnLeftX, currentY)
                };
            }

            if (isReadOnly)
            {
                btnStart.Text = hasSubmission ? "Просмотр решения" : "Просмотр задания";
            }
            else
            {
                btnStart.Text = hasSavedWork ? "Продолжить работу" : "Приступить";
            }

            async Task<bool> EnsureAssignmentStillAvailable(string actionVerb)
            {
                if (assignmentId <= 0) return true;
                var fresh = await ApiClient.GetAssignmentStateAsync(assignmentId);
                if (fresh == null || fresh.IsDeleted)
                {
                    ShowWarningDialog("Задание было удалено, окно информирования будет принудительно закрыто.");
                    taskDetailForm.Close();
                    return false;
                }
                if (fresh.Status != "Опубликовано")
                {
                    ShowWarningDialog("Задание было переведено в архив, окно информирования будет принудительно закрыто.");
                    taskDetailForm.Close();
                    return false;
                }
                if (fresh.CourseArchived == 1)
                {
                    ShowWarningDialog("Курс этого задания был отправлен в архив. Окно информирования будет принудительно закрыто.");
                    taskDetailForm.Close();
                    return false;
                }
                return true;
            }

            if (btnRestart != null)
            {
                btnRestart.Click += async (s, e) =>
                {
                    if (!await EnsureAssignmentStillAvailable("начать задание с начала")) return;
                    if (!ShowConfirmDialog(
                        "Начать задание с начала?\n\n" +
                        "Ваше сохранённое решение будет удалено, а машина Тьюринга вернётся к исходной конфигурации преподавателя. Это действие нельзя отменить.",
                        "Начать с начала")) return;
                    btnRestart.Enabled = false;
                    if (await ApiClient.ResetSubmissionAsync(assignmentId))
                    {
                        DataRefreshBus.Raise("Submission", "Updated", assignmentId);
                        originalAssignmentDescription = description;
                        originalStudentSolution = null;
                        taskDetailForm.Close();
                        // После /reset запись submission удалена. Студент входит в окно
                        // с актуальной МТ и без сохранения — isOutdated=false, базовая
                        // версия = текущая. При первом /draft создастся новый submission
                        // с IsOutdated=0 и AssignmentConfigVersion = currentConfigVersion.
                        EnterTaskExecutionMode(assignmentId, taskName, description, false, false, parentForm, false, currentConfigVersion);
                    }
                    else
                    {
                        btnRestart.Enabled = true;
                    }
                };
            }

            btnStart.Click += async (s, e) =>
            {
                if (!await EnsureAssignmentStillAvailable("приступить к заданию")) return;
                originalAssignmentDescription = description;
                originalStudentSolution = mySubmission?.SolutionJson;

                string jsonToLoad = hasSavedWork ? mySubmission.SolutionJson : description;
                taskDetailForm.Close();
                EnterTaskExecutionMode(assignmentId, taskName, jsonToLoad, isReadOnly, canRevoke, parentForm, isOutdated, currentConfigVersion);
            };

            if (btnRestart != null)
                taskDetailForm.Controls.AddRange(new Control[] { btnRestart, btnStart });
            else
                taskDetailForm.Controls.Add(btnStart);
            taskDetailForm.ClientSize = new Size(formClientWidth, currentY + 45);

            bool autoRefreshing = false;
            Action<string, string, int> onBusChanged = (entity, action, id) =>
            {
                if (taskDetailForm.IsDisposed) return;
                bool relevant = (entity == "Assignment" && (id == assignmentId || id == 0))
                                || (entity == "Submission")
                                || (entity == "Course")
                                || (entity == "CourseGroups");
                if (!relevant) return;
                if (autoRefreshing) return;
                autoRefreshing = true;
                try
                {
                    taskDetailForm.BeginInvoke(new Action(async () =>
                    {
                        try
                        {
                            if (taskDetailForm.IsDisposed) return;
                            if (assignmentId > 0)
                            {
                                var fresh = await ApiClient.GetAssignmentStateAsync(assignmentId);
                                if (fresh == null || fresh.IsDeleted
                                    || fresh.Status != "Опубликовано"
                                    || fresh.CourseArchived == 1)
                                {
                                    if (!taskDetailForm.IsDisposed)
                                    {
                                        ShowWarningDialog("Данные о задании изменились, окно будет закрыто. Откройте задание заново для актуальных сведений.");
                                        taskDetailForm.Close();
                                    }
                                    return;
                                }
                            }
                            if (!taskDetailForm.IsDisposed)
                            {
                                ShowWarningDialog("Данные по этому заданию обновились, окно будет закрыто. Откройте задание заново для актуальных сведений.");
                                taskDetailForm.Close();
                            }
                        }
                        catch { }
                        finally { autoRefreshing = false; }
                    }));
                }
                catch { autoRefreshing = false; }
            };
            DataRefreshBus.Changed += onBusChanged;
            taskDetailForm.FormClosed += (s, e) => { DataRefreshBus.Changed -= onBusChanged; };

            taskDetailForm.ShowDialog(parentForm ?? this);
        }










        // Изменили сигнатуру: добавили параметры с пустыми значениями по умолчанию
        private void ShowTaskEditorWindow(int courseId, string courseName, TaskEditorState state, Form parentManageForm)
        {
            bool isEdit = state.TaskId.HasValue;

            string originalStatus = isEdit ? (state.Status ?? "Архив") : "Архив";

            // Старые значения "Черновик"/"Скрыто" автоматически нормализуем в "Архив".
            if (originalStatus == "Черновик" || originalStatus == "Скрыто")
                originalStatus = "Архив";

            // СНИМОК ИСХОДНОГО Description (JSON МТ), с которым открылось окно.
            // Используется при сохранении для безопасной синхронизации state.Version с
            // сервером: если в БД Description совпадает с этим снимком — значит МТ
            // никто реально не менял, можно безопасно подхватить актуальный Version
            // (бывает при SignalR-эхе или служебных обновлениях), не показывая
            // пользователю ложное «было изменено другим пользователем».
            string originalDescriptionAtOpen = state.ConfigurationJson ?? "";

            // НОВАЯ ЛОГИКА: МТ заблокирована если:
            //   • Это редактирование существующего задания, которое
            //   • Было хотя бы раз опубликовано (state.IsLocked = true)
            bool everPublishedFlag = isEdit && state.IsLocked;
            bool fieldsLocked = false;       // Название/Тип/Срок/Статус всегда редактируемы
            bool isMtLocked = everPublishedFlag;

            int startY = 20; int spacing = 35; int leftX = 20; int rightX = 120; int inputWidth = 250;
            int configTop = startY + spacing * 5 + 10;
            int configH = 75;
            int btnSaveTop = configTop + configH + 35 + 20;
            int formHeight = btnSaveTop + 35 + 20;

            Form taskForm = new Form
            {
                Text = isEdit ? "Редактирование задания" : "Создание задания",
                ClientSize = new Size(400, formHeight),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.White
            };

            Font mainFont = new Font("Segoe UI", 9);
            Font lblFont = new Font("Segoe UI", 9, FontStyle.Regular);
            Color lblColor = Color.DimGray;

            Label lblCourse = new Label { Text = "Курс:", Location = new Point(leftX, startY), AutoSize = true, Font = lblFont, ForeColor = lblColor };
            Label lblCourseName = new Label { Text = courseName, Location = new Point(rightX, startY), Size = new Size(inputWidth, 18), Font = new Font("Segoe UI", 9, FontStyle.Bold), AutoEllipsis = true };

            Label lblTitle = new Label { Text = "Название:", Location = new Point(leftX, startY + spacing), AutoSize = true, Font = lblFont, ForeColor = lblColor };
            TextBox txtTitle = new TextBox { Text = state.Title, Location = new Point(rightX, startY + spacing - 3), Width = inputWidth, Font = mainFont, ReadOnly = fieldsLocked };

            Label lblType = new Label { Text = "Тип:", Location = new Point(leftX, startY + spacing * 2), AutoSize = true, Font = lblFont, ForeColor = lblColor };
            ComboBox cbType = new ComboBox { Location = new Point(rightX, startY + spacing * 2 - 3), Width = inputWidth, DropDownStyle = ComboBoxStyle.DropDownList, Font = mainFont, Enabled = !fieldsLocked };
            cbType.Items.AddRange(new[] { "Домашняя работа", "Лабораторная работа", "Самостоятельная работа", "Контрольная работа", "Экзамен" });
            cbType.SelectedItem = string.IsNullOrEmpty(state.Type) ? "Домашняя работа" : state.Type;
            if (cbType.SelectedIndex < 0) cbType.SelectedIndex = 0;

            Label lblDeadline = new Label { Text = "Срок:", Location = new Point(leftX, startY + spacing * 3), AutoSize = true, Font = lblFont, ForeColor = lblColor };
            DateTimePicker dtpDeadline = new DateTimePicker { Value = state.Deadline, Location = new Point(rightX, startY + spacing * 3 - 3), Width = inputWidth, Format = DateTimePickerFormat.Custom, CustomFormat = "dd.MM.yyyy HH:mm", Font = mainFont, Enabled = !fieldsLocked };
            dtpDeadline.ShowUpDown = true;

            Label lblStatus = new Label { Text = "Статус:", Location = new Point(leftX, startY + spacing * 4), AutoSize = true, Font = lblFont, ForeColor = lblColor };
            ComboBox cbStatus = new ComboBox { Location = new Point(rightX, startY + spacing * 4 - 3), Width = inputWidth, DropDownStyle = ComboBoxStyle.DropDownList, Font = mainFont };

            // НОВАЯ ЛОГИКА: только два статуса.
            cbStatus.Items.AddRange(new[] { "Опубликовано", "Архив" });
            cbStatus.SelectedItem = originalStatus;
            if (cbStatus.SelectedIndex < 0) cbStatus.SelectedIndex = 0;

            Label lblConfigLabel = new Label { Text = "Машина Тьюринга:", Location = new Point(leftX, configTop), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };

            Panel pnlConfig = new Panel { Location = new Point(leftX, configTop + 25), Size = new Size(350, configH), BackColor = Color.WhiteSmoke, BorderStyle = BorderStyle.FixedSingle };

            bool hasConfig = !string.IsNullOrWhiteSpace(state.ConfigurationJson) && state.ConfigurationJson.Trim().StartsWith("{");

            Label lblConfigStatus = new Label
            {
                Text = isMtLocked ? "🔒 МТ заблокирована (задание уже было опубликовано)" : (hasConfig ? "✅ МТ настроена и сохранена" : "⚠️ МТ ещё не создана"),
                ForeColor = isMtLocked ? Color.Red : (hasConfig ? Color.ForestGreen : Color.DarkOrange),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 5),
                Size = new Size(348, 22),
                Font = new Font("Segoe UI", 9, FontStyle.Regular)
            };

            Button btnCreateTM = new Button
            {
                Text = hasConfig ? "Очистить МТ" : "Создать МТ",
                Location = new Point(10, 32),
                Size = new Size(155, 28),
                Font = new Font("Segoe UI", 9),
                Enabled = !isMtLocked
            };
            ApplyTextButtonStyle(btnCreateTM);
            btnCreateTM.FlatAppearance.BorderSize = 1;
            btnCreateTM.BackColor = hasConfig ? Color.FromArgb(255, 240, 240) : Color.FromArgb(240, 248, 255);

            Button btnEditTM = new Button
            {
                Text = "Изменить МТ",
                Location = new Point(180, 32),
                Size = new Size(155, 28),
                Font = new Font("Segoe UI", 9),
                Enabled = hasConfig && !isMtLocked
            };
            ApplyTextButtonStyle(btnEditTM);
            btnEditTM.FlatAppearance.BorderSize = 1;
            btnEditTM.BackColor = Color.FromArgb(240, 248, 255);

            pnlConfig.Controls.AddRange(new Control[] { lblConfigStatus, btnCreateTM, btnEditTM });

            // Единая функция обновления UI-индикатора МТ по текущему hasConfig.
            // Вызывается как при «Очистить МТ», так и при возврате из TM-builder
            // (чтобы сразу отразить «✅ МТ настроена» без пересоздания окна).
            void RefreshConfigPanel()
            {
                if (isMtLocked)
                {
                    lblConfigStatus.Text = "🔒 МТ заблокирована (задание уже было опубликовано)";
                    lblConfigStatus.ForeColor = Color.Red;
                    btnCreateTM.Enabled = false;
                    btnEditTM.Enabled = false;
                    return;
                }
                if (hasConfig)
                {
                    lblConfigStatus.Text = "✅ МТ настроена и сохранена";
                    lblConfigStatus.ForeColor = Color.ForestGreen;
                    btnCreateTM.Text = "Очистить МТ";
                    btnCreateTM.BackColor = Color.FromArgb(255, 240, 240);
                    btnEditTM.Enabled = true;
                }
                else
                {
                    lblConfigStatus.Text = "⚠️ МТ ещё не создана";
                    lblConfigStatus.ForeColor = Color.DarkOrange;
                    btnCreateTM.Text = "Создать МТ";
                    btnCreateTM.BackColor = Color.FromArgb(240, 248, 255);
                    btnEditTM.Enabled = false;
                }
            }

            // Общий переход в TM-builder без закрытия текущего taskForm.
            // 1. Сохраняем введённые поля в state, чтобы после возврата они всё были в полях.
            // 2. Прячем taskForm и parentManageForm — их покажем обратно в callback'е.
            // 3. Входим в EnterTeacherTMBuilderMode с onReturn-функцией, которая
            //    восстановит видимость и обновит индикатор МТ в исходном окне.
            void EnterTMBuilderForCurrentForm()
            {
                state.Title = txtTitle.Text;
                state.Type = cbType.SelectedItem?.ToString() ?? "Домашняя работа";
                state.Deadline = dtpDeadline.Value;
                state.Status = cbStatus.SelectedItem?.ToString() ?? "Архив";

                taskForm.Hide();
                if (parentManageForm != null && !parentManageForm.IsDisposed) parentManageForm.Hide();

                EnterTeacherTMBuilderMode(state, saved =>
                {
                    // Была ли реально сохранена новая МТ — пересчитываем hasConfig
                    // из актуального state.ConfigurationJson, поскольку TM-builder мог
                    // его обновить (при saved=true) или оставить прежним (saved=false).
                    hasConfig = !string.IsNullOrWhiteSpace(state.ConfigurationJson)
                                 && state.ConfigurationJson.Trim().StartsWith("{");
                    if (taskForm.IsDisposed) return;
                    RefreshConfigPanel();
                    if (parentManageForm != null && !parentManageForm.IsDisposed) parentManageForm.Show();
                    taskForm.Show();
                    taskForm.Activate();
                    taskForm.BringToFront();
                });
            }

            btnCreateTM.Click += (s, e) => {
                if (hasConfig)
                {
                    if (ShowConfirmDialog("Удалить текущую конфигурацию МТ?\nЭто действие нельзя отменить.", "Сброс МТ"))
                    {
                        state.ConfigurationJson = "";
                        hasConfig = false;
                        RefreshConfigPanel();
                    }
                }
                else
                {
                    string taskErr2;
                    if (!ValidationRules.ValidateAssignmentTitle(txtTitle.Text, out taskErr2))
                    {
                        ShowWarningDialog(taskErr2);
                        txtTitle.Focus();
                        return;
                    }
                    EnterTMBuilderForCurrentForm();
                }
            };

            btnEditTM.Click += (s, e) => {
                EnterTMBuilderForCurrentForm();
            };

            Label lblHintLocked = null;

            Panel lineSeparator = new Panel
            {
                Location = new Point(0, btnSaveTop - 12),
                Size = new Size(400, 1),
                BackColor = Color.LightGray
            };

            Button btnSave = new Button
            {
                Text = isEdit ? "Сохранить изменения" : "Создать задание",
                Location = new Point(leftX, btnSaveTop),
                Size = new Size(220, 32),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 100, 185);

            Button btnCancelForm = new Button
            {
                Text = "Отмена",
                Location = new Point(leftX + 230, btnSaveTop),
                Size = new Size(140, 32),
                Font = new Font("Segoe UI", 9)
            };
            ApplyTextButtonStyle(btnCancelForm);
            btnCancelForm.FlatAppearance.BorderSize = 1;
            btnCancelForm.Click += (s, e) => {
                taskForm.DialogResult = DialogResult.Cancel;
                taskForm.Close();
            };

            btnSave.Click += async (s, e) => {
                string newStatus = cbStatus.SelectedItem?.ToString() ?? "Архив";

                string taskErr;
                if (!ValidationRules.ValidateAssignmentTitle(txtTitle.Text, out taskErr))
                {
                    ShowWarningDialog(taskErr);
                    txtTitle.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(state.ConfigurationJson) || !state.ConfigurationJson.Trim().StartsWith("{"))
                {
                    ShowWarningDialog("Необходимо создать конфигурацию Машины Тьюринга.\nНажмите кнопку «Создать МТ» и настройте машину.");
                    return;
                }

                // Срок не может быть в прошлом для опубликованного задания.
                if (newStatus == "Опубликовано" && dtpDeadline.Value <= DateTime.Now)
                {
                    ShowWarningDialog("Срок сдачи опубликованного задания должен быть в будущем. Установите корректную дату.");
                    dtpDeadline.Focus();
                    return;
                }

                // Дополнительное предупреждение о публикации.
                if (!isEdit && newStatus == "Опубликовано")
                {
                    if (!ShowConfirmDialog(
                        "Вы публикуете задание сразу при создании.\n\n" +
                        "После публикации Машину Тьюринга изменить будет нельзя (только название, тип, срок и статус). Продолжить?",
                        "Публикация задания"))
                        return;
                }

                if (isEdit && originalStatus == "Архив" && newStatus == "Опубликовано")
                {
                    if (!ShowConfirmDialog(
                        "Задание будет опубликовано — студенты привязанных групп сразу его увидят.\n\n" +
                        "После публикации Машину Тьюринга изменить будет нельзя (только название, тип, срок и статус). Продолжить?",
                        "Публикация задания"))
                        return;
                }

                if (isEdit && originalStatus == "Опубликовано" && newStatus == "Архив")
                {
                    if (!ShowConfirmDialog(
                        "Задание будет переведено в архив — студенты больше не смогут с ним работать.\n\n" +
                        "Сданные ранее работы и оценки сохранятся. Продолжить?",
                        "Архивация задания"))
                        return;
                }

                btnSave.Text = "Сохранение..."; btnSave.Enabled = false;
                btnCancelForm.Enabled = false;

                string trimmedTitle = txtTitle.Text.Trim();
                ApiClient.AdminOpResult res;
                if (isEdit)
                {
                    // БЕЗОПАСНАЯ СИНХРОНИЗАЦИЯ Version: перед PUT запрашиваем
                    // актуальное состояние задания с сервера. Если оказывается, что
                    // Version в БД увеличился, НО Description остался равным тому, что
                    // было при открытии окна (реально МТ никто не менял) — тихо
                    // подхватываем актуальный Version. Это устраняет ложный
                    // VersionConflict от SignalR-эха/служебных обновлений, сохраняя при
                    // этом защиту от реальной конкуренции пользователей.
                    int versionToSend = state.Version;
                    try
                    {
                        var fresh = await ApiClient.GetAssignmentStateAsync(state.TaskId.Value);
                        if (fresh != null && !fresh.IsDeleted)
                        {
                            string freshDesc = fresh.Description ?? "";
                            // Сравниваем БД-версию Description именно с оригиналамом на момент
                            // открытия окна (originalDescriptionAtOpen). Если они совпадают —
                            // другой пользователь МТ не редактировал: подхватываем актуальный
                            // Version и продолжаем. Если различаются — это реальный конфликт,
                            // сервер вернёт VersionConflict и UI покажет корректное предупреждение.
                            if (string.Equals(freshDesc, originalDescriptionAtOpen, StringComparison.Ordinal))
                            {
                                versionToSend = fresh.Version;
                                state.Version = fresh.Version;
                            }
                        }
                    }
                    catch { /* сетевые/временные ошибки — PUT всё равно отработает корректно */ }

                    res = await ApiClient.UpdateAssignmentAdminAsync(
                        state.TaskId.Value,
                        trimmedTitle,
                        cbType.SelectedItem.ToString(),
                        dtpDeadline.Value,
                        newStatus,
                        state.ConfigurationJson,
                        versionToSend);
                }
                else
                {
                    res = await ApiClient.CreateAssignmentAdminAsync(
                        trimmedTitle,
                        cbType.SelectedItem.ToString(),
                        dtpDeadline.Value,
                        newStatus,
                        courseId,
                        state.ConfigurationJson);
                }

                if (res.IsSuccess)
                {
                    DataRefreshBus.Raise(isEdit ? "Assignment" : "Assignment", isEdit ? "Updated" : "Created", 0);
                    ShowSuccessDialog(isEdit ? "Задание успешно обновлено!" : "Задание успешно создано!");
                    taskForm.DialogResult = DialogResult.OK;
                    taskForm.Close();
                    return;
                }

                if (res.IsDuplicateName)
                {
                    ShowWarningDialog(string.IsNullOrEmpty(res.Message)
                        ? $"В этом курсе уже есть задание с названием «{trimmedTitle}». Выберите другое название."
                        : res.Message);
                    btnSave.Text = isEdit ? "Сохранить изменения" : "Создать задание";
                    btnSave.Enabled = true;
                    btnCancelForm.Enabled = true;
                    txtTitle.Focus();
                    txtTitle.SelectAll();
                    return;
                }

                if (res.IsConflict)
                {
                    if (!string.IsNullOrEmpty(res.Message))
                    {
                        var icon = SystemIcons.Warning.ToBitmap();
                        string title;
                        switch (res.Reason)
                        {
                            case "AssignmentDeleted": title = "Задание удалено"; break;
                            case "CourseDeleted": title = "Курс удалён"; break;
                            case "CourseArchived": title = "Курс в архиве"; break;
                            case "VersionConflict": title = "Изменения другого пользователя"; break;
                            default: title = "Изменения другого пользователя"; break;
                        }
                        ShowFixedSizeDialog(res.Message, title, icon);
                    }
                    else
                    {
                        ShowAdminStaleDataDialog();
                    }
                    taskForm.DialogResult = DialogResult.OK;
                    taskForm.Close();
                    return;
                }

                if (res.Status == ApiClient.AdminOpStatus.SessionEnded)
                {
                    taskForm.DialogResult = DialogResult.Cancel;
                    taskForm.Close();
                    return;
                }

                if (res.Status == ApiClient.AdminOpStatus.ValidationError)
                {
                    ShowErrorDialog(string.IsNullOrEmpty(res.Message)
                        ? "Не удалось сохранить задание. Проверьте введённые данные."
                        : res.Message);
                    btnSave.Text = isEdit ? "Сохранить изменения" : "Создать задание";
                    btnSave.Enabled = true;
                    btnCancelForm.Enabled = true;
                    return;
                }

                string fallbackMsg = !string.IsNullOrEmpty(res.Message)
                    ? res.Message
                    : (isEdit
                        ? "Не удалось сохранить задание. Проверьте подключение к серверу и попробуйте снова."
                        : "Не удалось создать задание. Проверьте подключение к серверу и попробуйте снова.");
                ShowErrorDialog(fallbackMsg);
                btnSave.Text = isEdit ? "Сохранить изменения" : "Создать задание";
                btnSave.Enabled = true;
                btnCancelForm.Enabled = true;
            };

            var controls = new List<Control> { lblCourse, lblCourseName, lblTitle, txtTitle, lblType, cbType, lblDeadline, dtpDeadline, lblStatus, cbStatus, lblConfigLabel, pnlConfig, lineSeparator, btnSave, btnCancelForm };
            if (lblHintLocked != null) controls.Add(lblHintLocked);
            taskForm.Controls.AddRange(controls.ToArray());
            taskForm.AcceptButton = btnSave;
            taskForm.CancelButton = btnCancelForm;
            taskForm.ShowDialog(parentManageForm ?? this);
        }





        private async Task<DialogResult> ShowSubmissionsCheckWindow(int assignmentId, Form manageForm = null)
        {
            currentAssignmentId = assignmentId;

            // Получаем актуальное состояние задания (нужно для проверки дедлайна).
            var assignmentState = await ApiClient.GetAssignmentStateAsync(assignmentId);
            if (assignmentState == null || assignmentState.IsDeleted)
            {
                ShowWarningDialog("Задание было удалено или больше недоступно.");
                return DialogResult.Cancel;
            }
            DateTime assignmentDeadline = assignmentState.Deadline;
            bool deadlinePassed = DateTime.Now >= assignmentDeadline;

            Form checkForm = new Form
            {
                Text = "Проверка работ",
                Size = new Size(820, 540),
                MinimumSize = new Size(820, 540),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.WhiteSmoke
            };

            List<Submission> submissionsFromDb = new List<Submission>();

            Label lblCourse = new Label { Text = "Проверка решений студентов", Font = new Font("Segoe UI", 11, FontStyle.Bold), Location = new Point(15, 15), AutoSize = true, ForeColor = Color.Black };

            // Информация о дедлайне сверху — препод сразу видит, можно ли уже оценивать.
            Label lblDeadlineInfo = new Label
            {
                Text = deadlinePassed
                    ? $"Срок сдачи истёк {assignmentDeadline:dd.MM.yyyy HH:mm}. Оценивание разрешено."
                    : $"Срок сдачи: {assignmentDeadline:dd.MM.yyyy HH:mm}. Оценивание возможно только после истечения срока (можно лишь просматривать решения).",
                Location = new Point(15, 35),
                AutoSize = true,
                Font = new Font("Segoe UI", 8, FontStyle.Italic),
                ForeColor = deadlinePassed ? Color.ForestGreen : Color.DarkOrange
            };

            Label lblStatus = new Label { Text = "Статус:", Location = new Point(15, 73), AutoSize = true };
            ComboBox cbStatus = new ComboBox { Location = new Point(65, 70), Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
            cbStatus.Items.AddRange(new object[] { "Все", "Не оценено", "Оценено" });
            cbStatus.SelectedIndex = 0;

            Label lblGroup = new Label { Text = "Группа:", Location = new Point(205, 73), AutoSize = true };
            ComboBox cbGroup = new ComboBox { Location = new Point(260, 70), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            cbGroup.Items.Add("Все");
            cbGroup.SelectedIndex = 0;

            Label lblStudent = new Label { Text = "Поиск:", Location = new Point(390, 73), AutoSize = true };
            TextBox txtSearchStudent = new TextBox { Location = new Point(440, 70), Width = 200 };

            Button btnReset = new Button { Text = "Сбросить фильтры", Location = new Point(650, 69), Size = new Size(140, 26), FlatStyle = FlatStyle.Standard };

            DataGridView gridSubs = new DataGridView
            {
                Location = new Point(15, 110),
                Size = new Size(775, 345),
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            ApplyModernTableStyle(gridSubs);

            gridSubs.Columns.Add("Name", "Студент"); gridSubs.Columns[0].FillWeight = 27;
            gridSubs.Columns.Add("Group", "Группа"); gridSubs.Columns[1].FillWeight = 12;
            gridSubs.Columns.Add("Date", "Дата отправки"); gridSubs.Columns[2].FillWeight = 20;
            gridSubs.Columns.Add("Status", "Статус"); gridSubs.Columns[3].FillWeight = 13;
            gridSubs.Columns.Add("Grade", "Оценка"); gridSubs.Columns[4].FillWeight = 9;
            gridSubs.Columns.Add("Checking", "Проверяется"); gridSubs.Columns[5].FillWeight = 9;
            gridSubs.Columns.Add("MTVersion", "Версия МТ"); gridSubs.Columns[6].FillWeight = 10;
            gridSubs.Columns.Add("SubId", "ID"); gridSubs.Columns[7].Visible = false;
            gridSubs.Columns.Add("Solution", "JSON"); gridSubs.Columns[8].Visible = false;
            gridSubs.Columns.Add("Version", "Version"); gridSubs.Columns[9].Visible = false;

            void UpdateGrid()
            {
                gridSubs.Rows.Clear();
                string selectedStatus = cbStatus.SelectedItem?.ToString() ?? "Все";
                string selectedGroup = cbGroup.SelectedItem?.ToString() ?? "Все";
                string search = txtSearchStudent.Text.Trim().ToLower();

                // Только реальные сданные работы: "Не оценено" и "Оценено".
                // "Не сдано" в эту таблицу не попадает (сервер их и так не отдаёт).
                var filtered = submissionsFromDb.Where(s =>
                    (s.Status == "Не оценено" || s.Status == "Оценено") &&
                    (selectedStatus == "Все" || s.Status == selectedStatus) &&
                    (selectedGroup == "Все" || (s.GroupName ?? "—") == selectedGroup) &&
                    (s.StudentName ?? "").ToLower().Contains(search)
                ).ToList();

                foreach (var s in filtered)
                {
                    string checking = s.IsBeingChecked == 1 ? "🔒 Да" : "—";
                    string mtVersion = s.IsOutdated == 1 ? "⚠ Старая" : "Актуальная";
                    gridSubs.Rows.Add(s.StudentName ?? "Неизвестно", s.GroupName ?? "—",
                                      s.SubmittedAt.ToString("dd.MM.yyyy HH:mm"), s.Status,
                                      s.Grade?.ToString() ?? "—", checking, mtVersion,
                                      s.Id, s.SolutionJson ?? "", s.Version);
                    // Выделить строки со старой версией МТ жёлтым цветом
                    if (s.IsOutdated == 1)
                        gridSubs.Rows[gridSubs.Rows.Count - 1].DefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(255, 243, 205);
                }
            }

            async Task LoadData()
            {
                submissionsFromDb = await ApiClient.GetSubmissionsAsync(assignmentId) ?? new List<Submission>();

                string prevGroup = cbGroup.SelectedItem?.ToString() ?? "Все";
                cbGroup.Items.Clear();
                cbGroup.Items.Add("Все");
                foreach (var gn in submissionsFromDb.Select(x => x.GroupName ?? "—").Distinct())
                    cbGroup.Items.Add(gn);
                int idx = cbGroup.Items.IndexOf(prevGroup);
                cbGroup.SelectedIndex = idx >= 0 ? idx : 0;

                UpdateGrid();
            }

            cbStatus.SelectedIndexChanged += (s, e) => UpdateGrid();
            cbGroup.SelectedIndexChanged += (s, e) => UpdateGrid();
            txtSearchStudent.TextChanged += (s, e) => UpdateGrid();
            btnReset.Click += (s, e) => { cbStatus.SelectedIndex = 0; cbGroup.SelectedIndex = 0; txtSearchStudent.Text = ""; UpdateGrid(); };

            Button btnRefresh = new Button
            {
                Text = "🔄 Обновить",
                Location = new Point(15, 465),
                Size = new Size(110, 30),
                FlatStyle = FlatStyle.Standard,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnRefresh.Click += async (s, e) => { await LoadData(); };

            Button btnUnaccept = new Button
            {
                Text = "Отозвать оценку",
                Location = new Point(135, 465),
                Size = new Size(140, 30),
                FlatStyle = FlatStyle.Standard,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Enabled = false
            };

            Button btnCheck = new Button
            {
                Text = deadlinePassed ? "Проверить" : "Просмотреть",
                Location = new Point(575, 465),
                Size = new Size(115, 30),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnCheck.FlatAppearance.BorderSize = 0;

            Button btnClose = new Button
            {
                Text = "Закрыть",
                Location = new Point(700, 465),
                Size = new Size(85, 30),
                FlatStyle = FlatStyle.Standard,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            gridSubs.SelectionChanged += (s, e) =>
            {
                if (gridSubs.CurrentCell != null && gridSubs.CurrentCell.RowIndex >= 0)
                {
                    string st = gridSubs.Rows[gridSubs.CurrentCell.RowIndex].Cells[3].Value?.ToString() ?? "";
                    // Отзыв оценки доступен в любое время — даже до дедлайна.
                    btnUnaccept.Enabled = (st == "Оценено");
                }
                else btnUnaccept.Enabled = false;
            };

            btnUnaccept.Click += async (s, e) =>
            {
                if (gridSubs.CurrentCell == null) return;
                int row = gridSubs.CurrentCell.RowIndex;
                int subId = Convert.ToInt32(gridSubs.Rows[row].Cells[7].Value);
                string studentName = gridSubs.Rows[row].Cells[0].Value?.ToString() ?? "";

                if (!ShowConfirmDialog(
                    $"Отозвать оценку у студента «{studentName}»?\n\n" +
                    "Работа вернётся в статус «Не оценено», оценка и комментарий будут сброшены.\n\n" +
                    (deadlinePassed
                        ? "Срок сдачи уже истёк — студент НЕ сможет изменить решение, но вы сможете повторно его оценить."
                        : "Срок сдачи ещё не истёк — студент сможет внести правки и заново отправить решение."),
                    "Отзыв оценки"))
                    return;

                btnUnaccept.Enabled = false;
                if (await ApiClient.UnacceptSubmissionAsync(subId))
                {
                    ShowSuccessDialog("Оценка успешно отозвана.");
                    DataRefreshBus.Raise("Submission", "Updated", subId);
                    await LoadData();
                }
                else
                {
                    btnUnaccept.Enabled = true;
                    await LoadData();
                }
            };

            btnCheck.Click += (s, e) =>
            {
                if (gridSubs.CurrentCell != null && gridSubs.CurrentCell.RowIndex >= 0)
                {
                    int row = gridSubs.CurrentCell.RowIndex;
                    string studentName = gridSubs.Rows[row].Cells[0].Value.ToString();
                    int subId = Convert.ToInt32(gridSubs.Rows[row].Cells[7].Value);
                    string realJson = gridSubs.Rows[row].Cells[8].Value?.ToString() ?? "";
                    int subVersion = Convert.ToInt32(gridSubs.Rows[row].Cells[9].Value);
                    string subStatus = gridSubs.Rows[row].Cells[3].Value?.ToString() ?? "";

                    if (subStatus == "Оценено")
                    {
                        if (!ShowConfirmDialog("Эта работа уже оценена. Открыть её для смены оценки или комментария?", "Открыть оценённую работу"))
                            return;
                    }
                    else if (subStatus == "Не оценено" && !deadlinePassed)
                    {
                        if (!ShowConfirmDialog(
                            "Срок сдачи задания ещё не истёк — оценить работу пока нельзя, но вы можете её просмотреть.\n\n" +
                            "Открыть работу для просмотра?",
                            "Просмотр работы до дедлайна"))
                            return;
                    }

                    checkForm.DialogResult = DialogResult.OK;
                    checkForm.Close();
                    this.BeginInvoke(new Action(() => ActivateCheckingMode(subId, studentName, "Проверка работы", 10, realJson, manageForm, subVersion)));
                }
                else
                {
                    ShowWarningDialog("Выберите работу студента.");
                }
            };

            btnClose.Click += (s, e) => { checkForm.DialogResult = DialogResult.Cancel; checkForm.Close(); };

            await LoadData();

            Action<string, string, int> onBusChanged = (entity, action, id) =>
            {
                if (checkForm.IsDisposed) return;
                if (entity != "Submission" && entity != "Assignment") return;
                try
                {
                    checkForm.BeginInvoke(new Action(async () => { try { await LoadData(); } catch { } }));
                }
                catch { }
            };
            DataRefreshBus.Changed += onBusChanged;
            checkForm.FormClosed += (s, e) => { DataRefreshBus.Changed -= onBusChanged; };

            checkForm.Controls.AddRange(new Control[] { lblCourse, lblDeadlineInfo, lblStatus, cbStatus, lblGroup, cbGroup, lblStudent, txtSearchStudent, btnReset, gridSubs, btnRefresh, btnUnaccept, btnCheck, btnClose });

            return checkForm.ShowDialog(manageForm ?? this);
        }



        private void ShowAdministrationWindow()
        {
            bool isAdmin = (currentUserRole == UserRole.Admin);

            Form adminForm = new Form
            {
                Text = isAdmin ? "Администрирование (Администратор)" : "Администрирование (Преподаватель)",
                ClientSize = new Size(720, 520),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.Sizable,
                MinimumSize = new Size(720, 520),
                MaximizeBox = true,
                MinimizeBox = false,
                BackColor = Color.WhiteSmoke
            };

            TabControl tabControl = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9) };

            // Локальные коллекции
            List<StudentUser> allStudents = new List<StudentUser>();
            List<StudentUser> allTeachers = new List<StudentUser>();
            List<Group> allGroups = new List<Group>();
            List<Course> allCourses = new List<Course>();

            // ============================================================
            // ВКЛАДКА «СТУДЕНТЫ»
            // ============================================================
            TabPage tabSt = new TabPage("Студенты") { BackColor = Color.White };

            Panel pnlStTop = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Color.White };
            Label lblStSearch = new Label { Text = "Поиск (ФИО или логин):", Location = new Point(10, 5), AutoSize = true };
            TextBox txtStSearch = new TextBox { Location = new Point(10, 27), Width = 300, Font = new Font("Segoe UI", 9) };
            Label lblStFilter = new Label { Text = "Фильтр по группе:", Location = new Point(330, 5), AutoSize = true };
            ComboBox cbStFilter = new ComboBox { Location = new Point(330, 27), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9) };
            pnlStTop.Controls.AddRange(new Control[] { lblStSearch, txtStSearch, lblStFilter, cbStFilter });

            Panel pnlStBottom = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = Color.White };
            Button btnStCreate = new Button { Text = "Создать", Location = new Point(10, 10), Size = new Size(110, 30), FlatStyle = FlatStyle.Standard };
            Button btnStEdit = new Button { Text = "Изменить", Location = new Point(130, 10), Size = new Size(110, 30), FlatStyle = FlatStyle.Standard, Enabled = false };
            Button btnStDelete = new Button { Text = "Удалить", Location = new Point(250, 10), Size = new Size(110, 30), FlatStyle = FlatStyle.Standard, Enabled = false };
            Button btnStRefresh = new Button { Text = "🔄 Обновить", Location = new Point(370, 10), Size = new Size(120, 30), FlatStyle = FlatStyle.Standard };
            pnlStBottom.Controls.AddRange(new Control[] { btnStCreate, btnStEdit, btnStDelete, btnStRefresh });

            DataGridView gridStudents = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9)
            };
            gridStudents.Columns.Add("FullName", "ФИО"); gridStudents.Columns[0].FillWeight = 35;
            gridStudents.Columns.Add("Login", "Логин"); gridStudents.Columns[1].FillWeight = 20;
            gridStudents.Columns.Add("Group", "Группа"); gridStudents.Columns[2].FillWeight = 25;
            gridStudents.Columns.Add("LastLogin", "Последний вход"); gridStudents.Columns[3].FillWeight = 20;
            ApplyModernTableStyle(gridStudents);

            tabSt.Controls.Add(gridStudents);
            tabSt.Controls.Add(pnlStTop);
            tabSt.Controls.Add(pnlStBottom);

            // ============================================================
            // ВКЛАДКА «ГРУППЫ»
            // ============================================================
            TabPage tabGr = new TabPage("Группы") { BackColor = Color.White };

            Panel pnlGrTop = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Color.White };
            Label lblGrSearch = new Label { Text = "Поиск:", Location = new Point(10, 5), AutoSize = true };
            TextBox txtGrSearch = new TextBox { Location = new Point(10, 27), Width = 300, Font = new Font("Segoe UI", 9) };
            pnlGrTop.Controls.AddRange(new Control[] { lblGrSearch, txtGrSearch });

            Panel pnlGrBottom = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = Color.White };
            Button btnGrCreate = new Button { Text = "Создать", Location = new Point(10, 10), Size = new Size(110, 30), FlatStyle = FlatStyle.Standard };
            Button btnGrEdit = new Button { Text = "Изменить", Location = new Point(130, 10), Size = new Size(110, 30), FlatStyle = FlatStyle.Standard, Enabled = false };
            Button btnGrDelete = new Button { Text = "Удалить", Location = new Point(250, 10), Size = new Size(110, 30), FlatStyle = FlatStyle.Standard, Enabled = false };
            Button btnGrRefresh = new Button { Text = "🔄 Обновить", Location = new Point(370, 10), Size = new Size(120, 30), FlatStyle = FlatStyle.Standard };
            pnlGrBottom.Controls.AddRange(new Control[] { btnGrCreate, btnGrEdit, btnGrDelete, btnGrRefresh });

            DataGridView gridGroups = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9)
            };
            gridGroups.Columns.Add("Name", "Название группы"); gridGroups.Columns[0].FillWeight = 70;
            gridGroups.Columns.Add("Count", "Студентов"); gridGroups.Columns[1].FillWeight = 30;
            ApplyModernTableStyle(gridGroups);

            tabGr.Controls.Add(gridGroups);
            tabGr.Controls.Add(pnlGrTop);
            tabGr.Controls.Add(pnlGrBottom);

            // ============================================================
            // ВКЛАДКА «КУРСЫ»
            // ============================================================
            TabPage tabCo = new TabPage("Курсы") { BackColor = Color.White };

            // Верхняя панель с поиском и фильтрами (увеличенная высота под две строки фильтров)
            Panel pnlCoTop = new Panel { Dock = DockStyle.Top, Height = 110, BackColor = Color.White };

            Label lblCoSearch = new Label { Text = "Поиск:", Location = new Point(10, 5), AutoSize = true };
            TextBox txtCoSearch = new TextBox { Location = new Point(10, 27), Width = 300, Font = new Font("Segoe UI", 9) };

            Label lblCoStatus = new Label { Text = "Статус:", Location = new Point(330, 5), AutoSize = true };
            ComboBox cbCoStatus = new ComboBox { Location = new Point(330, 27), Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9) };
            cbCoStatus.Items.AddRange(new[] { "Все", "Активные", "В архиве" });
            cbCoStatus.SelectedIndex = 0;

            Label lblCoTeacher = new Label { Text = "Преподаватель:", Location = new Point(10, 55), AutoSize = true };
            ComboBox cbCoTeacher = new ComboBox { Location = new Point(10, 77), Width = 300, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9) };
            cbCoTeacher.Items.Add("Все");
            cbCoTeacher.SelectedIndex = 0;

            pnlCoTop.Controls.AddRange(new Control[] { lblCoSearch, txtCoSearch, lblCoStatus, cbCoStatus, lblCoTeacher, cbCoTeacher });

            Panel pnlCoBottom = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = Color.White };
            Button btnCoCreate = new Button { Text = "Создать", Location = new Point(10, 10), Size = new Size(110, 30), FlatStyle = FlatStyle.Standard };
            Button btnCoEdit = new Button { Text = "Изменить", Location = new Point(130, 10), Size = new Size(110, 30), FlatStyle = FlatStyle.Standard, Enabled = false };
            Button btnCoDelete = new Button { Text = "Удалить", Location = new Point(250, 10), Size = new Size(110, 30), FlatStyle = FlatStyle.Standard, Enabled = false };
            Button btnCoRefresh = new Button { Text = "🔄 Обновить", Location = new Point(370, 10), Size = new Size(120, 30), FlatStyle = FlatStyle.Standard };
            pnlCoBottom.Controls.AddRange(new Control[] { btnCoCreate, btnCoEdit, btnCoDelete, btnCoRefresh });

            DataGridView gridCourses = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9)
            };
            gridCourses.Columns.Add("Name", "Название курса"); gridCourses.Columns[0].FillWeight = 50;
            gridCourses.Columns.Add("Teacher", "Преподаватель"); gridCourses.Columns[1].FillWeight = 35;
            gridCourses.Columns.Add("Status", "Статус"); gridCourses.Columns[2].FillWeight = 15;
            ApplyModernTableStyle(gridCourses);

            tabCo.Controls.Add(gridCourses);
            tabCo.Controls.Add(pnlCoTop);
            tabCo.Controls.Add(pnlCoBottom);

            // ============================================================
            // ВКЛАДКА «ПРЕПОДАВАТЕЛИ» — только для Admin
            // ============================================================
            TabPage tabTe = null;
            DataGridView gridTeachers = null;
            TextBox txtTeSearch = null;
            Button btnTeCreate = null, btnTeEdit = null, btnTeDelete = null, btnTeRefresh = null;

            if (isAdmin)
            {
                tabTe = new TabPage("Преподаватели") { BackColor = Color.White };

                Panel pnlTeTop = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Color.White };
                Label lblTeSearch = new Label { Text = "Поиск:", Location = new Point(10, 5), AutoSize = true };
                txtTeSearch = new TextBox { Location = new Point(10, 27), Width = 300, Font = new Font("Segoe UI", 9) };
                pnlTeTop.Controls.AddRange(new Control[] { lblTeSearch, txtTeSearch });

                Panel pnlTeBottom = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = Color.White };
                btnTeCreate = new Button { Text = "Создать", Location = new Point(10, 10), Size = new Size(110, 30), FlatStyle = FlatStyle.Standard };
                btnTeEdit = new Button { Text = "Изменить", Location = new Point(130, 10), Size = new Size(110, 30), FlatStyle = FlatStyle.Standard, Enabled = false };
                btnTeDelete = new Button { Text = "Удалить", Location = new Point(250, 10), Size = new Size(110, 30), FlatStyle = FlatStyle.Standard, Enabled = false };
                btnTeRefresh = new Button { Text = "🔄 Обновить", Location = new Point(370, 10), Size = new Size(120, 30), FlatStyle = FlatStyle.Standard };
                pnlTeBottom.Controls.AddRange(new Control[] { btnTeCreate, btnTeEdit, btnTeDelete, btnTeRefresh });

                gridTeachers = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    AllowUserToAddRows = false,
                    ReadOnly = true,
                    RowHeadersVisible = false,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    MultiSelect = false,
                    BackgroundColor = Color.White,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Segoe UI", 9)
                };
                gridTeachers.Columns.Add("FullName", "ФИО"); gridTeachers.Columns[0].FillWeight = 50;
                gridTeachers.Columns.Add("Login", "Логин"); gridTeachers.Columns[1].FillWeight = 25;
                gridTeachers.Columns.Add("LastLogin", "Последний вход"); gridTeachers.Columns[2].FillWeight = 25;
                ApplyModernTableStyle(gridTeachers);

                tabTe.Controls.Add(gridTeachers);
                tabTe.Controls.Add(pnlTeTop);
                tabTe.Controls.Add(pnlTeBottom);
            }

            // Порядок вкладок:
            //  • Admin: Преподаватели → Студенты → Группы → Курсы
            //  • Teacher: Студенты → Группы → Курсы
            if (isAdmin && tabTe != null) tabControl.TabPages.Add(tabTe);
            tabControl.TabPages.Add(tabSt);
            tabControl.TabPages.Add(tabGr);
            tabControl.TabPages.Add(tabCo);

            adminForm.Controls.Add(tabControl);

            // ============================================================
            // ФУНКЦИИ ОБНОВЛЕНИЯ ТАБЛИЦ
            // ============================================================
            //
            // Единая логика поиска/фильтрации для всех таблиц окна:
            //   • При открытии окна — ни одна строка не выделена.
            //   • Изменение поиска или фильтра НЕ сбрасывает текущий выбор:
            //     если выбранный элемент остаётся в результатах — он
            //     остаётся выделенным; если уходит — выделение снимается.
            //     Сбрасывать выбор может только сам пользователь (клик)
            //     или подтверждённое удаление/обновление данных.
            //   • Авто-выделение первой строки запрещено (CurrentCell=null
            //     после Rows.Add, иначе DataGridView сам ставит фокус на [0]).
            //
            // Флаги isRefreshing* подавляют SelectionChanged во время
            // программной перестройки строк, чтобы не затирать сохранённый Id.

            bool isRefreshingStudents = false;
            bool isRefreshingGroups = false;
            bool isRefreshingCoursesAdm = false;
            bool isRefreshingTeachers = false;
            int? selectedStudentId = null;
            int? selectedGroupId = null;
            int? selectedCourseIdAdm = null;
            int? selectedTeacherId = null;

            // Программно снимает выделение и фокус ячейки. Сначала CurrentCell=null
            // (убирает синюю рамку), затем ClearSelection (убирает фон).
            void ResetGridSelection(DataGridView g)
            {
                if (g == null) return;
                g.ClearSelection();
                g.BeginInvoke(new Action(() =>
                {
                    try { g.CurrentCell = null; } catch { }
                    g.ClearSelection();
                }));
            }

            void SelectGridRow(DataGridView g, int rowIdx)
            {
                g.ClearSelection();
                g.Rows[rowIdx].Selected = true;
                try { g.CurrentCell = g.Rows[rowIdx].Cells[0]; } catch { }
            }

            void RefreshStudentsGrid()
            {
                isRefreshingStudents = true;
                try
                {
                    gridStudents.Rows.Clear();
                    string search = txtStSearch.Text.Trim().ToLower();

                    int? filterGroupId = null;
                    bool filterNoGroup = false;
                    if (cbStFilter.SelectedItem is Group selG)
                    {
                        filterGroupId = selG.Id;
                    }
                    else if (cbStFilter.SelectedItem is string sFilter && sFilter == "— Без группы —")
                    {
                        filterNoGroup = true;
                    }

                    int? rowToSelect = null;
                    foreach (var s in allStudents
                        .Where(x => string.IsNullOrEmpty(search) || x.FullName.ToLower().Contains(search) || x.Login.ToLower().Contains(search))
                        .Where(x =>
                            (!filterGroupId.HasValue && !filterNoGroup) ||
                            (filterGroupId.HasValue && x.GroupId == filterGroupId.Value) ||
                            (filterNoGroup && !x.GroupId.HasValue)))
                    {
                        string groupName = s.GroupId.HasValue ? (allGroups.FirstOrDefault(g => g.Id == s.GroupId.Value)?.Name ?? "—") : "—";
                        string lastLogin = s.LastLoginAt.HasValue ? s.LastLoginAt.Value.ToString("dd.MM.yyyy HH:mm") : "—";
                        int idx = gridStudents.Rows.Add(s.FullName, s.Login, groupName, lastLogin);
                        gridStudents.Rows[idx].Tag = s;
                        if (selectedStudentId.HasValue && s.Id == selectedStudentId.Value)
                            rowToSelect = idx;
                    }

                    if (rowToSelect.HasValue)
                        SelectGridRow(gridStudents, rowToSelect.Value);
                    else
                        ResetGridSelection(gridStudents);
                }
                finally { isRefreshingStudents = false; }
                UpdateStudentButtons();
            }

            void UpdateStudentButtons()
            {
                bool any = gridStudents.SelectedRows.Count > 0;
                btnStEdit.Enabled = any;
                btnStDelete.Enabled = any;
            }

            void RefreshGroupsGrid()
            {
                isRefreshingGroups = true;
                try
                {
                    gridGroups.Rows.Clear();
                    string search = txtGrSearch.Text.Trim().ToLower();
                    int? rowToSelect = null;
                    foreach (var g in allGroups.Where(x => string.IsNullOrEmpty(search) || x.Name.ToLower().Contains(search)))
                    {
                        int cnt = allStudents.Count(s => s.GroupId == g.Id);
                        int idx = gridGroups.Rows.Add(g.Name, cnt.ToString());
                        gridGroups.Rows[idx].Tag = g;
                        if (selectedGroupId.HasValue && g.Id == selectedGroupId.Value)
                            rowToSelect = idx;
                    }
                    if (rowToSelect.HasValue)
                        SelectGridRow(gridGroups, rowToSelect.Value);
                    else
                        ResetGridSelection(gridGroups);
                }
                finally { isRefreshingGroups = false; }
                UpdateGroupButtons();
            }

            void UpdateGroupButtons()
            {
                bool any = gridGroups.SelectedRows.Count > 0;
                btnGrEdit.Enabled = any;
                btnGrDelete.Enabled = any;
            }

            void RefreshCoursesGrid()
            {
                isRefreshingCoursesAdm = true;
                try
                {
                    gridCourses.Rows.Clear();
                    string search = txtCoSearch.Text.Trim().ToLower();
                    string filterStatus = cbCoStatus.SelectedItem?.ToString() ?? "Все";

                    // Определяем выбранного преподавателя:
                    //   "Все"           → null
                    //   "— Без преподавателя —" → специальный режим (TeacherId = 0/неназначен)
                    //   StudentUser     → конкретный Id
                    int? filterTeacherId = null;
                    bool filterNoTeacher = false;
                    if (cbCoTeacher.SelectedItem is StudentUser selT)
                    {
                        filterTeacherId = selT.Id;
                    }
                    else if (cbCoTeacher.SelectedItem is string sT && sT == "— Без преподавателя —")
                    {
                        filterNoTeacher = true;
                    }

                    int? rowToSelect = null;
                    foreach (var c in allCourses
                                .Where(x => string.IsNullOrEmpty(search) || (x.Name ?? "").ToLower().Contains(search))
                                .Where(x =>
                                    filterStatus == "Все" ||
                                    (filterStatus == "Активные" && x.Archived == 0) ||
                                    (filterStatus == "В архиве" && x.Archived == 1))
                                .Where(x =>
                                    (!filterTeacherId.HasValue && !filterNoTeacher) ||
                                    (filterTeacherId.HasValue && x.TeacherId == filterTeacherId.Value) ||
                                    (filterNoTeacher && (x.TeacherId == 0 || string.IsNullOrEmpty(x.TeacherName)))))
                    {
                        string status = c.Archived == 1 ? "🗄 Архив" : "✅ Активен";
                        int idx = gridCourses.Rows.Add(c.Name, c.TeacherName ?? "—", status);
                        gridCourses.Rows[idx].Tag = c;
                        if (selectedCourseIdAdm.HasValue && c.Id == selectedCourseIdAdm.Value)
                            rowToSelect = idx;
                    }
                    if (rowToSelect.HasValue)
                        SelectGridRow(gridCourses, rowToSelect.Value);
                    else
                        ResetGridSelection(gridCourses);
                }
                finally { isRefreshingCoursesAdm = false; }
                UpdateCourseButtons();
            }

            void UpdateCourseButtons()
            {
                bool any = gridCourses.SelectedRows.Count > 0;
                btnCoEdit.Enabled = any;
                btnCoDelete.Enabled = any;
            }

            void RefreshTeachersGrid()
            {
                if (gridTeachers == null) return;
                isRefreshingTeachers = true;
                try
                {
                    gridTeachers.Rows.Clear();
                    string search = txtTeSearch.Text.Trim().ToLower();
                    int? rowToSelect = null;
                    foreach (var t in allTeachers.Where(x => string.IsNullOrEmpty(search) || x.FullName.ToLower().Contains(search) || x.Login.ToLower().Contains(search)))
                    {
                        string lastLogin = t.LastLoginAt.HasValue ? t.LastLoginAt.Value.ToString("dd.MM.yyyy HH:mm") : "—";
                        int idx = gridTeachers.Rows.Add(t.FullName, t.Login, lastLogin);
                        gridTeachers.Rows[idx].Tag = t;
                        if (selectedTeacherId.HasValue && t.Id == selectedTeacherId.Value)
                            rowToSelect = idx;
                    }
                    if (rowToSelect.HasValue)
                        SelectGridRow(gridTeachers, rowToSelect.Value);
                    else
                        ResetGridSelection(gridTeachers);
                }
                finally { isRefreshingTeachers = false; }
                UpdateTeacherButtons();
            }

            void UpdateTeacherButtons()
            {
                if (gridTeachers == null) return;
                bool any = gridTeachers.SelectedRows.Count > 0;
                btnTeDelete.Enabled = any;
                if (btnTeEdit != null) btnTeEdit.Enabled = any;
            }

            // Перестраивает выпадающий фильтр преподавателей на вкладке "Курсы",
            // сохраняя ранее выбранное значение, если оно ещё доступно.
            void RefreshCourseTeacherFilter()
            {
                object prevSelected = cbCoTeacher.SelectedItem;
                string prevText = prevSelected?.ToString() ?? "Все";

                cbCoTeacher.Items.Clear();
                cbCoTeacher.Items.Add("Все");
                cbCoTeacher.Items.Add("— Без преподавателя —");

                // Список преподавателей, которые реально упомянуты в курсах
                // (плюс полный список allTeachers — для админа). Объединяем через Id.
                var teacherIdsInCourses = allCourses
                    .Where(c => c.TeacherId > 0)
                    .Select(c => c.TeacherId)
                    .Distinct()
                    .ToHashSet();

                var teachersToShow = new List<StudentUser>();
                if (isAdmin && allTeachers != null && allTeachers.Count > 0)
                {
                    teachersToShow = allTeachers.OrderBy(t => t.FullName).ToList();
                }
                else
                {
                    // Для преподавателя allTeachers недоступен — соберём минимальный список
                    // из самих курсов, чтобы фильтр всё-таки был полезен.
                    foreach (var c in allCourses
                        .Where(c => c.TeacherId > 0 && !string.IsNullOrEmpty(c.TeacherName))
                        .GroupBy(c => c.TeacherId)
                        .Select(g => g.First()))
                    {
                        teachersToShow.Add(new StudentUser { Id = c.TeacherId, FullName = c.TeacherName, Login = "" });
                    }
                    teachersToShow = teachersToShow.OrderBy(t => t.FullName).ToList();
                }

                cbCoTeacher.DisplayMember = "FullName";
                foreach (var t in teachersToShow) cbCoTeacher.Items.Add(t);

                // Восстанавливаем прежний выбор, если он остался валидным
                int newIdx = 0;
                for (int i = 0; i < cbCoTeacher.Items.Count; i++)
                {
                    var item = cbCoTeacher.Items[i];
                    if (item is StudentUser su && prevSelected is StudentUser pu && su.Id == pu.Id) { newIdx = i; break; }
                    if (item is string s && s == prevText) { newIdx = i; break; }
                }
                cbCoTeacher.SelectedIndex = newIdx;
            }

            async Task RefreshAllData()
            {
                allGroups = await ApiClient.GetGroupsAsync() ?? new List<Group>();
                allCourses = await ApiClient.GetMyCoursesAsync() ?? new List<Course>();
                allStudents = await ApiClient.GetStudentsAsync() ?? new List<StudentUser>();
                if (isAdmin) allTeachers = await ApiClient.GetTeachersAsync() ?? new List<StudentUser>();

                // Фильтр групп на вкладке "Студенты"
                object prevGroupSel = cbStFilter.SelectedItem;
                cbStFilter.Items.Clear();
                cbStFilter.Items.Add("Все группы");
                cbStFilter.Items.Add("— Без группы —");
                foreach (var g in allGroups) cbStFilter.Items.Add(g);
                int restoreIdx = 0;
                for (int i = 0; i < cbStFilter.Items.Count; i++)
                {
                    var item = cbStFilter.Items[i];
                    if (item is Group g1 && prevGroupSel is Group g2 && g1.Id == g2.Id) { restoreIdx = i; break; }
                    if (item is string ss && prevGroupSel is string ps && ss == ps) { restoreIdx = i; break; }
                }
                cbStFilter.SelectedIndex = restoreIdx;

                // Фильтр преподавателей на вкладке "Курсы"
                RefreshCourseTeacherFilter();

                RefreshStudentsGrid();
                RefreshGroupsGrid();
                RefreshCoursesGrid();
                if (isAdmin) RefreshTeachersGrid();
            }

            // ============================================================
            // СОБЫТИЯ ВЫБОРА В ТАБЛИЦАХ
            // ============================================================

            gridStudents.SelectionChanged += (s, e) =>
            {
                if (isRefreshingStudents) return;
                if (gridStudents.SelectedRows.Count > 0)
                    selectedStudentId = (gridStudents.SelectedRows[0].Tag as StudentUser)?.Id;
                else
                    selectedStudentId = null;
                UpdateStudentButtons();
            };

            gridGroups.SelectionChanged += (s, e) =>
            {
                if (isRefreshingGroups) return;
                if (gridGroups.SelectedRows.Count > 0)
                    selectedGroupId = (gridGroups.SelectedRows[0].Tag as Group)?.Id;
                else
                    selectedGroupId = null;
                UpdateGroupButtons();
            };

            gridCourses.SelectionChanged += (s, e) =>
            {
                if (isRefreshingCoursesAdm) return;
                if (gridCourses.SelectedRows.Count > 0)
                    selectedCourseIdAdm = (gridCourses.SelectedRows[0].Tag as Course)?.Id;
                else
                    selectedCourseIdAdm = null;
                UpdateCourseButtons();
            };

            if (gridTeachers != null)
            {
                gridTeachers.SelectionChanged += (s, e) =>
                {
                    if (isRefreshingTeachers) return;
                    if (gridTeachers.SelectedRows.Count > 0)
                        selectedTeacherId = (gridTeachers.SelectedRows[0].Tag as StudentUser)?.Id;
                    else
                        selectedTeacherId = null;
                    UpdateTeacherButtons();
                };
            }

            txtStSearch.TextChanged += (s, e) => RefreshStudentsGrid();
            cbStFilter.SelectedIndexChanged += (s, e) => RefreshStudentsGrid();
            txtGrSearch.TextChanged += (s, e) => RefreshGroupsGrid();
            txtCoSearch.TextChanged += (s, e) => RefreshCoursesGrid();
            cbCoStatus.SelectedIndexChanged += (s, e) => RefreshCoursesGrid();
            cbCoTeacher.SelectedIndexChanged += (s, e) => RefreshCoursesGrid();
            if (txtTeSearch != null) txtTeSearch.TextChanged += (s, e) => RefreshTeachersGrid();

            // ============================================================
            // ОБРАБОТЧИКИ КНОПОК — СТУДЕНТЫ
            // ============================================================

            btnStCreate.Click += async (s, e) =>
            {
                if (await ShowStudentEditDialog(null, allGroups))
                {
                    DataRefreshBus.Raise("Student", "Created", 0);
                    await RefreshAllData();
                }
            };

            btnStEdit.Click += async (s, e) =>
            {
                if (gridStudents.SelectedRows.Count == 0) return;
                var st = (StudentUser)gridStudents.SelectedRows[0].Tag;
                if (await ShowStudentEditDialog(st, allGroups))
                {
                    DataRefreshBus.Raise("Student", "Updated", st.Id);
                    await RefreshAllData();
                }
            };

            btnStDelete.Click += async (s, e) =>
            {
                if (gridStudents.SelectedRows.Count == 0) return;
                var st = (StudentUser)gridStudents.SelectedRows[0].Tag;

                var status = await CheckUserOnlineStatus(st.Id);
                string warnPart = status.IsOnline
                    ? $"\n\n⚠ ВНИМАНИЕ: студент сейчас в системе (IP: {status.IpAddress}, активность: {status.LastActivity:HH:mm:ss}). Его сессия будет немедленно завершена."
                    : "";

                if (ShowConfirmDialog($"Удалить студента «{st.FullName}»?\nВсе его сданные работы будут безвозвратно удалены.{warnPart}",
                        "Подтверждение удаления"))
                {
                    var res = await ApiClient.DeleteStudentAdminAsync(st.Id);
                    if (res.IsSuccess)
                    {
                        ShowSuccessDialog($"Студент «{st.FullName}» удалён.");
                        DataRefreshBus.Raise("Student", "Deleted", st.Id);
                        await RefreshAllData();
                    }
                    else if (res.IsConflict)
                    {
                        ShowAdminStaleDataDialog();
                        await RefreshAllData();
                    }
                    else if (res.Status != ApiClient.AdminOpStatus.SessionEnded)
                    {
                        ShowErrorDialog(string.IsNullOrEmpty(res.Message)
                            ? $"Не удалось удалить студента «{st.FullName}»."
                            : res.Message);
                        await RefreshAllData();
                    }
                }
            };



            btnStRefresh.Click += async (s, e) =>
            {
                btnStRefresh.Enabled = false;
                try { await RefreshAllData(); }
                finally { btnStRefresh.Enabled = true; }
            };

            // ============================================================
            // ОБРАБОТЧИКИ КНОПОК — ГРУППЫ
            // ============================================================

            btnGrCreate.Click += async (s, e) =>
            {
                if (await ShowGroupEditDialog(null))
                {
                    DataRefreshBus.Raise("Group", "Created", 0);
                    await RefreshAllData();
                }
            };

            btnGrEdit.Click += async (s, e) =>
            {
                if (gridGroups.SelectedRows.Count == 0) return;
                var g = (Group)gridGroups.SelectedRows[0].Tag;
                if (await ShowGroupEditDialog(g))
                {
                    DataRefreshBus.Raise("Group", "Updated", g.Id);
                    await RefreshAllData();
                }
            };

            btnGrDelete.Click += async (s, e) =>
            {
                if (gridGroups.SelectedRows.Count == 0) return;
                var g = (Group)gridGroups.SelectedRows[0].Tag;
                int affected = allStudents.Count(x => x.GroupId == g.Id);
                string msg = $"Удалить группу «{g.Name}»?\n\nЗатронуто студентов: {affected} (их связь с группой сбросится).\nПривязки этой группы к курсам будут удалены.";
                if (ShowConfirmDialog(msg, "Подтверждение удаления"))
                {
                    var res = await ApiClient.DeleteGroupAdminAsync(g.Id);
                    if (res.IsSuccess)
                    {
                        ShowSuccessDialog("Удалено.");
                        DataRefreshBus.Raise("Group", "Deleted", g.Id);
                        await RefreshAllData();
                    }
                    else if (res.IsConflict)
                    {
                        ShowAdminStaleDataDialog();
                        await RefreshAllData();
                    }
                    else if (res.Status != ApiClient.AdminOpStatus.SessionEnded)
                    {
                        ShowErrorDialog(string.IsNullOrEmpty(res.Message)
                            ? $"Не удалось удалить группу «{g.Name}»."
                            : res.Message);
                        await RefreshAllData();
                    }
                }
            };



            btnGrRefresh.Click += async (s, e) =>
            {
                btnGrRefresh.Enabled = false;
                try { await RefreshAllData(); }
                finally { btnGrRefresh.Enabled = true; }
            };

            // ============================================================
            // ОБРАБОТЧИКИ КНОПОК — КУРСЫ
            // ============================================================

            btnCoCreate.Click += async (s, e) =>
            {
                if (await ShowCourseEditDialog(null, allGroups))
                {
                    DataRefreshBus.Raise("Course", "Created", 0);
                    await RefreshAllData();
                }
            };

            btnCoEdit.Click += async (s, e) =>
            {
                if (gridCourses.SelectedRows.Count == 0) return;
                var c = (Course)gridCourses.SelectedRows[0].Tag;
                if (await ShowCourseEditDialog(c, allGroups))
                {
                    DataRefreshBus.Raise("Course", "Updated", c.Id);
                    await RefreshAllData();
                }
            };

            btnCoDelete.Click += async (s, e) =>
            {
                if (gridCourses.SelectedRows.Count == 0) return;
                var c = (Course)gridCourses.SelectedRows[0].Tag;
                if (ShowConfirmDialog($"⚠ ВНИМАНИЕ!\n\nПри удалении курса «{c.Name}» будут удалены:\n• Все задания курса\n• Все сданные решения студентов\n• Все оценки и комментарии\n• Все привязки групп\n\nДействие НЕЛЬЗЯ отменить. Продолжить?", "Критическое подтверждение"))
                {
                    var res = await ApiClient.DeleteCourseAdminAsync(c.Id);
                    if (res.IsSuccess)
                    {
                        ShowSuccessDialog("Удалено.");
                        DataRefreshBus.Raise("Course", "Deleted", c.Id);
                        await RefreshAllData();
                    }
                    else if (res.IsConflict)
                    {
                        ShowAdminStaleDataDialog();
                        await RefreshAllData();
                    }
                    else if (res.Status != ApiClient.AdminOpStatus.SessionEnded)
                    {
                        ShowErrorDialog(string.IsNullOrEmpty(res.Message)
                            ? $"Не удалось удалить курс «{c.Name}»."
                            : res.Message);
                        await RefreshAllData();
                    }
                }
            };



            btnCoRefresh.Click += async (s, e) =>
            {
                btnCoRefresh.Enabled = false;
                try { await RefreshAllData(); }
                finally { btnCoRefresh.Enabled = true; }
            };

            // ============================================================
            // ОБРАБОТЧИКИ КНОПОК — ПРЕПОДАВАТЕЛИ (только Admin)
            // ============================================================

            if (isAdmin)
            {
                btnTeCreate.Click += async (s, e) =>
                {
                    if (await ShowTeacherCreateDialog())
                    {
                        DataRefreshBus.Raise("Teacher", "Created", 0);
                        await RefreshAllData();
                    }
                };

                btnTeEdit.Click += async (s, e) =>
                {
                    if (gridTeachers.SelectedRows.Count == 0) return;
                    var t = (StudentUser)gridTeachers.SelectedRows[0].Tag;
                    if (await ShowTeacherEditDialog(t))
                    {
                        DataRefreshBus.Raise("Teacher", "Updated", t.Id);
                        await RefreshAllData();
                    }
                };

                btnTeDelete.Click += async (s, e) =>
                {
                    if (gridTeachers.SelectedRows.Count == 0) return;
                    var t = (StudentUser)gridTeachers.SelectedRows[0].Tag;
                    if (ApiClient.CurrentUser != null && t.Login == ApiClient.CurrentUser.Login)
                    {
                        ShowWarningDialog("Нельзя удалить собственную учётную запись.");
                        return;
                    }

                    var status = await CheckUserOnlineStatus(t.Id);
                    string warnPart = status.IsOnline
                        ? $"\n\n⚠ ВНИМАНИЕ: преподаватель сейчас в системе (IP: {status.IpAddress}, активность: {status.LastActivity:HH:mm:ss}). Его сессия будет немедленно завершена."
                        : "";

                    if (ShowConfirmDialog($"Вы действительно хотите удалить этот профиль?\n\nПреподаватель: {t.FullName}{warnPart}",
                            "Подтверждение удаления"))
                    {
                        var res = await ApiClient.DeleteTeacherAdminAsync(t.Id);
                        if (res.IsSuccess)
                        {
                            ShowSuccessDialog($"Преподаватель «{t.FullName}» удалён.");
                            DataRefreshBus.Raise("Teacher", "Deleted", t.Id);
                            await RefreshAllData();
                        }
                        else if (res.IsConflict)
                        {
                            ShowAdminStaleDataDialog();
                            await RefreshAllData();
                        }
                        else if (res.Status != ApiClient.AdminOpStatus.SessionEnded)
                        {
                            ShowErrorDialog(string.IsNullOrEmpty(res.Message)
                                ? $"Не удалось удалить преподавателя «{t.FullName}»."
                                : res.Message);
                            await RefreshAllData();
                        }
                    }
                };




                btnTeRefresh.Click += async (s, e) =>
                {
                    btnTeRefresh.Enabled = false;
                    try { await RefreshAllData(); }
                    finally { btnTeRefresh.Enabled = true; }
                };
            }

            // Автоматическое обновление окна «Администрирование» при любых изменениях
            // в системе. Это требование UX: после создания/изменения/удаления групп,
            // студентов, преподавателей и курсов таблицы должны обновляться сами.
            Action<string, string, int> onBusChanged = (entity, action, id) =>
            {
                if (adminForm.IsDisposed) return;
                bool relevant = entity == "Group" || entity == "Course" || entity == "Student"
                                || entity == "Teacher" || entity == "CourseGroups"
                                || entity == "GradingPolicy";
                if (!relevant) return;
                try
                {
                    adminForm.BeginInvoke(new Action(async () => { try { await RefreshAllData(); } catch { } }));
                }
                catch { }
            };
            DataRefreshBus.Changed += onBusChanged;
            adminForm.FormClosed += (s, e) => { DataRefreshBus.Changed -= onBusChanged; };

            adminForm.Load += async (s, e) => await RefreshAllData();
            adminForm.ShowDialog(this);
        }




        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание/редактирование СТУДЕНТА
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание/редактирование СТУДЕНТА
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание/редактирование СТУДЕНТА
        // ============================================================
        private async Task<bool> ShowStudentEditDialog(StudentUser existing, List<Group> groups)
        {
            bool isEdit = existing != null;
            bool resultOk = false;
            bool isDirty = false;
            bool forceClose = false;

            Form dlg = new Form
            {
                Text = isEdit ? "Редактирование студента" : "Создание студента",
                ClientSize = new Size(440, isEdit ? 346 : 361)
            };
            ApplyDialogStyle(dlg);

            string origLast = "", origFirst = "", origMiddle = "";
            bool origNoMiddle = false;
            if (isEdit)
            {
                ValidationRules.SplitFullName(existing.FullName, out origLast, out origFirst, out origMiddle, out origNoMiddle);
            }

            string origLogin = existing?.Login ?? "";
            int? origGroupId = existing?.GroupId;

            Label lblFio = new Label { Text = "ФИО (только русские буквы, заглавная первая):", Location = new Point(15, 15), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };

            Label lblLastName = new Label { Text = "Фамилия:", Location = new Point(15, 40), AutoSize = true };
            TextBox txtLastName = new TextBox { Location = new Point(95, 37), Width = 320, Text = origLast };

            Label lblFirstName = new Label { Text = "Имя:", Location = new Point(15, 70), AutoSize = true };
            TextBox txtFirstName = new TextBox { Location = new Point(95, 67), Width = 320, Text = origFirst };

            Label lblMiddleName = new Label { Text = "Отчество:", Location = new Point(15, 100), AutoSize = true };
            TextBox txtMiddleName = new TextBox { Location = new Point(95, 97), Width = 320, Text = origMiddle };

            CheckBox chkNoMiddle = new CheckBox
            {
                Text = "Без отчества",
                Location = new Point(95, 125),
                AutoSize = true,
                Checked = origNoMiddle,
                Font = new Font("Segoe UI", 9)
            };
            chkNoMiddle.CheckedChanged += (s, e) =>
            {
                txtMiddleName.Enabled = !chkNoMiddle.Checked;
                if (chkNoMiddle.Checked)
                {
                    txtMiddleName.Text = "";
                    txtMiddleName.BackColor = SystemColors.Control;
                }
                else
                {
                    txtMiddleName.BackColor = SystemColors.Window;
                }
            };
            txtMiddleName.Enabled = !origNoMiddle;
            if (origNoMiddle) txtMiddleName.BackColor = SystemColors.Control;

            KeyPressEventHandler nameInputFilter = (s, e) =>
            {
                if (char.IsControl(e.KeyChar)) return;
                bool isRussian = (e.KeyChar >= 'А' && e.KeyChar <= 'я') || e.KeyChar == 'Ё' || e.KeyChar == 'ё';
                bool isAllowedExtra = e.KeyChar == '-' || e.KeyChar == '\'' || e.KeyChar == ' ';
                if (!isRussian && !isAllowedExtra)
                {
                    e.Handled = true;
                    ShowWarningDialog("В ФИО разрешены только русские буквы, дефис и апостроф. Латиница, цифры и спецсимволы недопустимы.");
                }
            };
            txtLastName.KeyPress += nameInputFilter;
            txtFirstName.KeyPress += nameInputFilter;
            txtMiddleName.KeyPress += nameInputFilter;

            Label lblLogin = new Label { Text = "Логин (латиница, минимум 3 символа):", Location = new Point(15, 153), AutoSize = true };
            TextBox txtLogin = new TextBox { Location = new Point(15, 173), Width = 400, Text = origLogin };
            txtLogin.KeyPress += (s, e) =>
            {
                if (char.IsControl(e.KeyChar)) return;
                if (e.KeyChar == ' ')
                {
                    e.Handled = true;
                    ShowWarningDialog("Логин не должен содержать пробелы.");
                    return;
                }
                bool isLatinLetter = (e.KeyChar >= 'A' && e.KeyChar <= 'Z') || (e.KeyChar >= 'a' && e.KeyChar <= 'z');
                bool isDigit = e.KeyChar >= '0' && e.KeyChar <= '9';
                bool isAllowedSym = e.KeyChar == '.' || e.KeyChar == '-' || e.KeyChar == '_';
                if (!isLatinLetter && !isDigit && !isAllowedSym)
                {
                    e.Handled = true;
                    ShowWarningDialog("Логин может содержать только латинские буквы, цифры, точку, дефис и подчёркивание.");
                }
            };

            Label lblPwd = new Label
            {
                Text = $"Пароль (минимум {ValidationRules.PasswordMin} символов, латиница + цифры):",
                Location = new Point(15, 203),
                AutoSize = true,
                Visible = !isEdit
            };
            TextBox txtPwd = new TextBox { Location = new Point(15, 223), Width = 305, PasswordChar = '*', Visible = !isEdit };
            CheckBox chkShow = new CheckBox { Text = "Показать", Location = new Point(330, 225), AutoSize = true, Font = new Font("Segoe UI", 8), Visible = !isEdit };
            chkShow.CheckedChanged += (s, e) => txtPwd.PasswordChar = chkShow.Checked ? '\0' : '*';

            int groupY = isEdit ? 203 : 258;
            Label lblGroup = new Label { Text = "Группа:", Location = new Point(15, groupY), AutoSize = true };
            ComboBox cbGroup = new ComboBox { Location = new Point(15, groupY + 20), Width = 400, DropDownStyle = ComboBoxStyle.DropDownList };
            cbGroup.Items.Add("— Без группы —");
            foreach (var g in groups) cbGroup.Items.Add(g);
            cbGroup.DisplayMember = "Name";
            if (isEdit && existing.GroupId.HasValue)
            {
                var sel = groups.FirstOrDefault(g => g.Id == existing.GroupId.Value);
                if (sel != null) cbGroup.SelectedItem = sel;
                else cbGroup.SelectedIndex = 0;
            }
            else
            {
                cbGroup.SelectedIndex = 0;
            }

            // isEdit:   cbGroup bottom = 244  → hint Y=254, sep Y=294, btn Y=306, form H=346
            // !isEdit:  cbGroup bottom = 299  → sep Y=309, btn Y=321, form H=361
            int hintY = isEdit ? 254 : 0;
            Label lblHint = new Label
            {
                Text = "Логин и ФИО можно изменить. Для смены пароля используйте кнопку «Сбросить пароль».",
                Location = new Point(15, hintY),
                Size = new Size(410, 32),
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.DimGray,
                Visible = isEdit
            };

            int separatorY = isEdit ? 294 : 309;
            Panel separator = new Panel { Location = new Point(0, separatorY), Size = new Size(440, 1), BackColor = Color.LightGray };

            int buttonsY = separatorY + 12;
            Button btnResetPwd = new Button { Text = "Сбросить пароль", Location = new Point(15, buttonsY), Size = new Size(140, 28), Visible = isEdit };
            Button btnCancel = new Button { Text = "Отмена", Location = new Point(245, buttonsY), Size = new Size(85, 28) };
            Button btnSave = new Button { Text = isEdit ? "Сохранить" : "Создать", Location = new Point(340, buttonsY), Size = new Size(85, 28) };
            ApplyDialogButtonStyle(btnResetPwd);
            ApplyDialogButtonStyle(btnCancel);
            ApplyDialogButtonStyle(btnSave);

            EventHandler markDirty = (s, e) =>
            {
                int? curGid = (cbGroup.SelectedItem is Group sg) ? (int?)sg.Id : null;
                if (isEdit)
                {
                    isDirty = txtLastName.Text.Trim() != origLast
                            || txtFirstName.Text.Trim() != origFirst
                            || txtMiddleName.Text.Trim() != origMiddle
                            || chkNoMiddle.Checked != origNoMiddle
                            || txtLogin.Text.Trim() != origLogin
                            || curGid != origGroupId;
                }
                else
                {
                    isDirty = !string.IsNullOrEmpty(txtLastName.Text)
                            || !string.IsNullOrEmpty(txtFirstName.Text)
                            || !string.IsNullOrEmpty(txtMiddleName.Text)
                            || !string.IsNullOrEmpty(txtLogin.Text)
                            || !string.IsNullOrEmpty(txtPwd.Text);
                }
            };
            txtLastName.TextChanged += markDirty;
            txtFirstName.TextChanged += markDirty;
            txtMiddleName.TextChanged += markDirty;
            chkNoMiddle.CheckedChanged += markDirty;
            txtLogin.TextChanged += markDirty;
            txtPwd.TextChanged += markDirty;
            cbGroup.SelectedIndexChanged += markDirty;

            if (isEdit)
            {
                btnResetPwd.Click += async (s, e) =>
                {
                    if (ApiClient.CurrentUser != null && existing.Login == ApiClient.CurrentUser.Login)
                    {
                        ShowWarningDialog("Нельзя сбросить собственный пароль через окно администрирования. Используйте «Сменить пароль» в профиле.");
                        return;
                    }
                    await ShowResetPasswordWindow(existing.Id, existing.FullName, "Студент");
                };
            }

            btnCancel.Click += (s, e) =>
            {
                dlg.DialogResult = DialogResult.Cancel;
                dlg.Close();
            };

            btnSave.Click += async (s, e) =>
            {
                string fioErr;
                if (!ValidationRules.ValidateFullNameParts(
                        txtLastName.Text, txtFirstName.Text, txtMiddleName.Text, chkNoMiddle.Checked, out fioErr))
                {
                    ShowWarningDialog(fioErr);
                    return;
                }

                string lgErr;
                if (!ValidationRules.ValidateLogin(txtLogin.Text, out lgErr))
                {
                    ShowWarningDialog(lgErr);
                    return;
                }

                if (!isEdit)
                {
                    string pwErr;
                    if (!ValidationRules.ValidatePassword(txtPwd.Text, out pwErr))
                    {
                        ShowWarningDialog(pwErr);
                        return;
                    }
                }
                else if (!string.IsNullOrEmpty(txtPwd.Text))
                {
                    string pwErr;
                    if (!ValidationRules.ValidatePassword(txtPwd.Text, out pwErr))
                    {
                        ShowWarningDialog(pwErr);
                        return;
                    }
                }

                string newName = ValidationRules.CombineFullName(
                    txtLastName.Text, txtFirstName.Text, txtMiddleName.Text, chkNoMiddle.Checked);
                string newLogin = txtLogin.Text.Trim();
                string newPwd = txtPwd.Text;

                int? gid = (cbGroup.SelectedItem is Group sg2) ? (int?)sg2.Id : null;

                if (gid == null)
                {
                    if (!ShowConfirmDialog(
                        "Студент сохраняется БЕЗ группы.\n\n" +
                        "Без группы он не увидит ни одного курса и не сможет сдавать задания, пока ему не назначат группу.\n\n" +
                        "Продолжить?",
                        "Подтверждение"))
                        return;
                }

                if (isEdit)
                {
                    string origFullName = existing.FullName;
                    bool loginChanged = newLogin != existing.Login;
                    bool passwordChanged = !string.IsNullOrEmpty(newPwd);
                    bool nameChanged = newName != origFullName;
                    bool groupChanged = gid != origGroupId;

                    bool anyChanged = loginChanged || passwordChanged || nameChanged || groupChanged;

                    if (anyChanged)
                    {
                        var sessionStatus = await CheckUserOnlineStatus(existing.Id);
                        if (sessionStatus.IsOnline)
                        {
                            var changes = new List<string>();
                            if (nameChanged) changes.Add("ФИО");
                            if (loginChanged) changes.Add("логин");
                            if (passwordChanged) changes.Add("пароль");
                            if (groupChanged) changes.Add("группу");
                            string changesList = string.Join(", ", changes);

                            bool willTerminateSession = loginChanged || passwordChanged;
                            string actionDescription;
                            if (willTerminateSession)
                            {
                                actionDescription =
                                    $"Вы изменяете {changesList} студента.\n" +
                                    "Так как меняются критичные данные для входа (логин/пароль), " +
                                    "его сессия будет принудительно завершена и пользователю потребуется войти заново.";
                            }
                            else
                            {
                                actionDescription =
                                    $"Вы изменяете {changesList} студента.\n" +
                                    "Сессия пользователя останется активной, но все его открытые окна будут автоматически закрыты, " +
                                    "и он увидит уведомление с обновлёнными данными.";
                            }

                            bool confirmed = ShowActiveSessionWarningDialog(
                                existing.FullName,
                                sessionStatus.IpAddress,
                                sessionStatus.LastActivity,
                                actionDescription,
                                "Сохранить");

                            if (!confirmed) return;
                        }
                    }
                }

                btnSave.Enabled = false;
                btnCancel.Enabled = false;
                btnResetPwd.Enabled = false;

                var res = isEdit
                    ? await ApiClient.UpdateStudentAdminAsync(existing.Id, newLogin, newPwd, newName, gid, existing.Version)
                    : await ApiClient.CreateStudentAdminAsync(newLogin, newPwd, newName, gid);

                if (res.IsSuccess)
                {
                    ShowSuccessDialog(isEdit ? $"Данные студента «{newName}» успешно обновлены." : $"Студент «{newName}» успешно создан.");
                    resultOk = true;
                    isDirty = false;
                    forceClose = true;
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                    return;
                }

                // Логин занят другим пользователем — окно остаётся открытым,
                // даём пользователю возможность сразу поправить ввод и попробовать снова.
                if (res.IsDuplicateName)
                {
                    ShowWarningDialog(string.IsNullOrEmpty(res.Message)
                        ? $"Логин «{newLogin}» уже занят другим пользователем. Выберите другой логин."
                        : res.Message);
                    btnSave.Enabled = true;
                    btnCancel.Enabled = true;
                    btnResetPwd.Enabled = true;
                    txtLogin.Focus();
                    txtLogin.SelectAll();
                    return;
                }

                if (res.IsConflict)
                {
                    resultOk = true;
                    isDirty = false;
                    forceClose = true;
                    ShowAdminStaleDataDialog();
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                    return;
                }

                if (res.Status == ApiClient.AdminOpStatus.SessionEnded)
                {
                    forceClose = true;
                    dlg.DialogResult = DialogResult.Cancel;
                    dlg.Close();
                    return;
                }

                string msg = !string.IsNullOrEmpty(res.Message)
                    ? res.Message
                    : (isEdit
                        ? $"Не удалось сохранить изменения для студента «{newName}»."
                        : $"Не удалось создать студента «{newName}».");
                ShowErrorDialog(msg);
                btnSave.Enabled = true;
                btnCancel.Enabled = true;
                btnResetPwd.Enabled = true;
            };

            dlg.FormClosing += (s, e) =>
            {
                if (forceClose) return;
                if (isDirty && !ConfirmDiscardChanges()) e.Cancel = true;
            };

            dlg.Controls.AddRange(new Control[]
            {
                lblFio, lblLastName, txtLastName, lblFirstName, txtFirstName,
                lblMiddleName, txtMiddleName, chkNoMiddle,
                lblLogin, txtLogin, lblPwd, txtPwd, chkShow,
                lblGroup, cbGroup, lblHint, separator, btnResetPwd, btnCancel, btnSave
            });
            dlg.AcceptButton = btnSave;
            dlg.CancelButton = btnCancel;
            dlg.ShowDialog(this);
            return resultOk;
        }







        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание/редактирование ГРУППЫ
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание/редактирование ГРУППЫ
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание/редактирование ГРУППЫ
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание/редактирование ГРУППЫ
        // ============================================================
        private async Task<bool> ShowGroupEditDialog(Group existing)
        {
            bool isEdit = existing != null;
            bool resultOk = false;
            bool isDirty = false;
            bool forceClose = false;

            Form dlg = new Form
            {
                Text = isEdit ? "Редактирование группы" : "Создание группы",
                ClientSize = new Size(380, 145)
            };
            ApplyDialogStyle(dlg);

            string origName = existing?.Name ?? "";

            Label lblName = new Label { Text = "Название группы:", Location = new Point(15, 18), AutoSize = true };
            TextBox txtName = new TextBox { Location = new Point(15, 40), Width = 350, Text = origName };

            Panel separator = new Panel { Location = new Point(0, 85), Size = new Size(380, 1), BackColor = Color.LightGray };

            Button btnCancel = new Button { Text = "Отмена", Location = new Point(185, 100), Size = new Size(85, 28) };
            Button btnSave = new Button { Text = isEdit ? "Сохранить" : "Создать", Location = new Point(280, 100), Size = new Size(85, 28) };
            ApplyDialogButtonStyle(btnCancel);
            ApplyDialogButtonStyle(btnSave);

            txtName.TextChanged += (s, e) => isDirty = txtName.Text.Trim() != origName;

            btnCancel.Click += (s, e) =>
            {
                dlg.DialogResult = DialogResult.Cancel;
                dlg.Close();
            };

            btnSave.Click += async (s, e) =>
            {
                string grpErr;
                if (!ValidationRules.ValidateGroupName(txtName.Text, out grpErr))
                {
                    ShowWarningDialog(grpErr);
                    return;
                }

                btnSave.Enabled = false;
                btnCancel.Enabled = false;

                var res = isEdit
                    ? await ApiClient.UpdateGroupAdminAsync(existing.Id, txtName.Text.Trim(), existing.Version)
                    : await ApiClient.CreateGroupAdminAsync(txtName.Text.Trim());

                if (res.IsSuccess)
                {
                    ShowSuccessDialog(isEdit ? "Группа обновлена." : "Группа создана.");
                    resultOk = true;
                    isDirty = false;
                    forceClose = true;
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                    return;
                }

                // Имя группы занято — НЕ закрываем окно, показываем конкретное сообщение,
                // даём пользователю возможность сразу поправить название и попробовать снова.
                if (res.IsDuplicateName)
                {
                    ShowWarningDialog(string.IsNullOrEmpty(res.Message)
                        ? "Группа с таким названием уже существует. Выберите другое."
                        : res.Message);
                    btnSave.Enabled = true;
                    btnCancel.Enabled = true;
                    txtName.Focus();
                    txtName.SelectAll();
                    return;
                }

                if (res.IsConflict)
                {
                    resultOk = true;
                    isDirty = false;
                    forceClose = true;
                    ShowAdminStaleDataDialog();
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                    return;
                }

                if (res.Status == ApiClient.AdminOpStatus.SessionEnded)
                {
                    forceClose = true;
                    dlg.DialogResult = DialogResult.Cancel;
                    dlg.Close();
                    return;
                }

                string msg = !string.IsNullOrEmpty(res.Message)
                    ? res.Message
                    : (isEdit
                        ? $"Не удалось переименовать группу «{origName}»."
                        : $"Не удалось создать группу «{txtName.Text.Trim()}».");
                ShowErrorDialog(msg);
                btnSave.Enabled = true;
                btnCancel.Enabled = true;
            };

            dlg.FormClosing += (s, e) =>
            {
                if (forceClose) return;
                if (isDirty && !ConfirmDiscardChanges()) e.Cancel = true;
            };

            dlg.Controls.AddRange(new Control[] { lblName, txtName, separator, btnCancel, btnSave });
            dlg.AcceptButton = btnSave;
            dlg.CancelButton = btnCancel;
            dlg.ShowDialog(this);
            return resultOk;
        }





        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание/редактирование КУРСА
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание/редактирование КУРСА
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание КУРСА (только для Admin/Teacher)
        // Редактирование разнесено в ShowCourseMetaDialog / ShowCourseGroupsDialog.
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание КУРСА (только для Admin/Teacher)
        // Изменения:
        //   • Поле "Описание" удалено (по требованию). Описание задаётся при редактировании
        //     через окно ShowCourseMetaDialog ("Описание / архивация").
        //   • Привязка групп теперь делается прямо здесь, а не отдельным окном:
        //     внизу диалога расположена таблица групп с поисковой строкой и чекбоксами.
        //   • Для редактирования по-прежнему открывается ShowCourseMetaDialog
        //     (метаданные) и ShowCourseGroupsDialog (привязка групп) — отдельно.
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание/редактирование КУРСА
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание/редактирование КУРСА
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание КУРСА (только для Admin/Teacher)
        // Редактирование разнесено в ShowCourseMetaDialog / ShowCourseGroupsDialog.
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание КУРСА (только для Admin/Teacher)
        //
        // Изменения:
        //   • Поле "Описание" удалено (по требованию). Описание задаётся при редактировании
        //     через окно ShowCourseMetaDialog ("Описание / архивация").
        //   • Привязка групп делается прямо здесь, а не отдельным окном:
        //     внизу диалога расположена таблица групп с поисковой строкой и чекбоксами.
        //   • Добавлен чекбокс "Курс в архиве" — даёт администратору возможность сразу
        //     создать курс в архивном состоянии (например, при импорте старых курсов).
        //     Интерфейс этого блока полностью идентичен окну редактирования
        //     (ShowCourseMetaDialog), но без кнопки "Обновить" — потому что обновлять
        //     при создании ещё нечего: курс ещё не существует на сервере.
        //   • Для редактирования по-прежнему открывается ShowCourseMetaDialog
        //     (метаданные + привязка групп) — отдельно.
        // ============================================================
        private async Task<bool> ShowCourseEditDialog(Course existing, List<Group> groups)
        {
            bool isEdit = existing != null;
            bool resultOk = false;
            bool isAdmin = (currentUserRole == UserRole.Admin);
            bool isDirty = false;
            bool forceClose = false;

            if (isEdit)
            {
                return await ShowCourseMetaDialog(existing);
            }

            Form dlg = new Form
            {
                Text = "Создание курса",
                ClientSize = new Size(560, 670)
            };
            ApplyDialogStyle(dlg);

            string origName = "";

            Label lblName = new Label { Text = "Название курса:", Location = new Point(15, 18), AutoSize = true };
            TextBox txtName = new TextBox { Location = new Point(15, 40), Width = 530, Text = origName };

            Label lblTeacher = new Label { Text = "Преподаватель:", Location = new Point(15, 78), AutoSize = true };
            ComboBox cbTeacher = new ComboBox { Location = new Point(15, 100), Width = 530, DropDownStyle = ComboBoxStyle.DropDownList };

            if (isAdmin)
            {
                var teachers = await ApiClient.GetTeachersAsync();
                foreach (var t in teachers) cbTeacher.Items.Add(t);
                cbTeacher.DisplayMember = "FullName";
                if (cbTeacher.Items.Count > 0) cbTeacher.SelectedIndex = 0;
            }
            else
            {
                cbTeacher.Items.Add(ApiClient.CurrentUser?.FullName ?? "Я");
                cbTeacher.SelectedIndex = 0;
                cbTeacher.Enabled = false;
            }

            CheckBox chkArchived = new CheckBox
            {
                Text = "Курс в архиве (студенты не увидят его в своём списке)",
                Location = new Point(15, 138),
                AutoSize = true,
                Checked = false
            };

            Label lblArchHint = new Label
            {
                Text = "Архивный курс остаётся в системе со всеми заданиями и оценками,\nно скрывается из активных списков у студентов.",
                Location = new Point(35, 161),
                AutoSize = true,
                ForeColor = Color.DimGray,
                Font = new Font("Segoe UI", 8)
            };

            Label lblGroups = new Label
            {
                Text = "Группы, которым доступен курс:",
                Location = new Point(15, 210),
                AutoSize = true,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            Label lblGroupsHint = new Label
            {
                Text = "Отметьте галочкой группы, студенты которых должны видеть этот курс.",
                Location = new Point(15, 232),
                Size = new Size(530, 18),
                ForeColor = Color.DimGray,
                Font = new Font("Segoe UI", 8)
            };

            Label lblSearchGroups = new Label { Text = "Поиск группы:", Location = new Point(15, 258), AutoSize = true };
            TextBox txtSearchGroups = new TextBox { Location = new Point(15, 278), Width = 530, Font = new Font("Segoe UI", 9) };

            DataGridView gridCG = new DataGridView
            {
                Location = new Point(15, 310),
                Size = new Size(530, 270),
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9)
            };
            gridCG.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Check", HeaderText = "✔", FillWeight = 15 });
            gridCG.Columns.Add("GroupName", "Группа");
            gridCG.Columns[1].FillWeight = 85;
            gridCG.Columns[1].ReadOnly = true;
            ApplyModernTableStyle(gridCG);
            gridCG.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (gridCG.IsCurrentCellDirty) gridCG.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            var checkedGroupIds = new HashSet<int>();

            void RefreshGroupsGrid()
            {
                gridCG.Rows.Clear();
                string s = txtSearchGroups.Text.Trim().ToLower();
                foreach (var g in groups.Where(x => string.IsNullOrEmpty(s) || (x.Name ?? "").ToLower().Contains(s)))
                {
                    int idx = gridCG.Rows.Add(checkedGroupIds.Contains(g.Id), g.Name);
                    gridCG.Rows[idx].Tag = g;
                }
            }
            RefreshGroupsGrid();

            txtSearchGroups.TextChanged += (s, e) => RefreshGroupsGrid();

            Panel separator = new Panel { Location = new Point(0, 595), Size = new Size(560, 1), BackColor = Color.LightGray };

            Button btnCancel = new Button { Text = "Отмена", Location = new Point(365, 610), Size = new Size(85, 28) };
            Button btnSave = new Button { Text = "Создать", Location = new Point(460, 610), Size = new Size(85, 28) };
            ApplyDialogButtonStyle(btnCancel);
            ApplyDialogButtonStyle(btnSave);

            EventHandler markDirty = (s, e) =>
            {
                isDirty = !string.IsNullOrEmpty(txtName.Text.Trim())
                       || checkedGroupIds.Count > 0
                       || chkArchived.Checked;
            };
            txtName.TextChanged += markDirty;
            cbTeacher.SelectedIndexChanged += markDirty;
            chkArchived.CheckedChanged += markDirty;
            gridCG.CellValueChanged += (s, e) =>
            {
                if (e.ColumnIndex != 0 || e.RowIndex < 0) return;
                var row = gridCG.Rows[e.RowIndex];
                if (row.Tag is Group grp)
                {
                    bool ch = (bool)(row.Cells[0].Value ?? false);
                    if (ch) checkedGroupIds.Add(grp.Id); else checkedGroupIds.Remove(grp.Id);
                }
                markDirty(s, e);
            };

            btnCancel.Click += (s, e) =>
            {
                dlg.DialogResult = DialogResult.Cancel;
                dlg.Close();
            };

            btnSave.Click += async (s, e) =>
            {
                string crsErr;
                if (!ValidationRules.ValidateCourseName(txtName.Text, out crsErr))
                {
                    ShowWarningDialog(crsErr);
                    return;
                }

                int? teacherId = null;
                if (isAdmin)
                {
                    if (cbTeacher.SelectedItem is StudentUser selT) teacherId = selT.Id;
                    else { ShowWarningDialog("Выберите преподавателя."); return; }
                }

                bool archiveOnCreate = chkArchived.Checked;

                if (archiveOnCreate)
                {
                    if (!ShowConfirmDialog(
                        "Курс будет создан сразу в архивном состоянии.\n\n" +
                        "Студенты привязанных групп НЕ увидят его в своём списке курсов, " +
                        "пока курс не будет переведён обратно в активное состояние.\n\nПродолжить?",
                        "Создание архивного курса"))
                    {
                        return;
                    }
                }

                btnSave.Enabled = false;
                btnCancel.Enabled = false;

                var selectedGroupIds = checkedGroupIds.ToList();

                var createRes = await ApiClient.CreateCourseAdminAsync(txtName.Text.Trim(), "", teacherId);

                if (!createRes.IsSuccess)
                {
                    // Имя курса занято — оставляем окно открытым, чтобы пользователь
                    // мог сразу поправить название и попробовать снова.
                    if (createRes.IsDuplicateName)
                    {
                        ShowWarningDialog(string.IsNullOrEmpty(createRes.Message)
                            ? $"Курс с названием «{txtName.Text.Trim()}» уже существует. Выберите другое название."
                            : createRes.Message);
                        btnSave.Enabled = true;
                        btnCancel.Enabled = true;
                        txtName.Focus();
                        txtName.SelectAll();
                        return;
                    }

                    if (createRes.IsConflict)
                    {
                        resultOk = true;
                        isDirty = false;
                        forceClose = true;
                        ShowAdminStaleDataDialog();
                        dlg.DialogResult = DialogResult.OK;
                        dlg.Close();
                        return;
                    }

                    if (createRes.Status == ApiClient.AdminOpStatus.SessionEnded)
                    {
                        forceClose = true;
                        dlg.DialogResult = DialogResult.Cancel;
                        dlg.Close();
                        return;
                    }

                    string msg = !string.IsNullOrEmpty(createRes.Message)
                        ? createRes.Message
                        : "Не удалось создать курс. Проверьте выбор преподавателя и название.";
                    ShowErrorDialog(msg);
                    btnSave.Enabled = true;
                    btnCancel.Enabled = true;
                    return;
                }

                Course freshCourse = null;
                try
                {
                    var allCourses = await ApiClient.GetMyCoursesAsync();
                    freshCourse = allCourses?
                        .Where(c => c.Name == txtName.Text.Trim()
                                    && (!isAdmin || c.TeacherId == (teacherId ?? 0)))
                        .OrderByDescending(c => c.Id)
                        .FirstOrDefault();
                }
                catch { freshCourse = null; }

                bool archiveApplied = !archiveOnCreate;
                if (archiveOnCreate && freshCourse != null)
                {
                    var archRes = await ApiClient.UpdateCourseMetaAdminAsync(
                        freshCourse.Id,
                        freshCourse.Name,
                        freshCourse.Description ?? "",
                        freshCourse.TeacherId,
                        1,
                        freshCourse.Version);

                    if (archRes.IsSuccess)
                    {
                        archiveApplied = true;
                    }
                    else if (archRes.IsConflict)
                    {
                        resultOk = true;
                        isDirty = false;
                        forceClose = true;
                        ShowAdminStaleDataDialog();
                        dlg.DialogResult = DialogResult.OK;
                        dlg.Close();
                        return;
                    }
                    else if (archRes.Status == ApiClient.AdminOpStatus.SessionEnded)
                    {
                        forceClose = true;
                        dlg.DialogResult = DialogResult.Cancel;
                        dlg.Close();
                        return;
                    }
                    else
                    {
                        string msg = !string.IsNullOrEmpty(archRes.Message)
                            ? archRes.Message
                            : "Курс создан, но не удалось перевести его в архив. Сделайте это позже через «Описание / архивация».";
                        ShowErrorDialog(msg);
                    }
                }

                if (selectedGroupIds.Count > 0 && freshCourse != null)
                {
                    var grpRes = await ApiClient.UpdateCourseGroupsAdminAsync(freshCourse.Id, selectedGroupIds);
                    if (grpRes.IsConflict)
                    {
                        resultOk = true;
                        isDirty = false;
                        forceClose = true;
                        ShowAdminStaleDataDialog();
                        dlg.DialogResult = DialogResult.OK;
                        dlg.Close();
                        return;
                    }
                    if (grpRes.Status == ApiClient.AdminOpStatus.SessionEnded)
                    {
                        forceClose = true;
                        dlg.DialogResult = DialogResult.Cancel;
                        dlg.Close();
                        return;
                    }
                    if (!grpRes.IsSuccess)
                    {
                        string msg = !string.IsNullOrEmpty(grpRes.Message)
                            ? grpRes.Message
                            : "Курс создан, но не удалось сохранить привязку групп. Сделайте это позже через окно редактирования курса.";
                        ShowErrorDialog(msg);
                    }
                }

                ShowSuccessDialog(archiveOnCreate && archiveApplied
                    ? "Курс создан в архивном состоянии."
                    : "Курс создан.");

                resultOk = true;
                isDirty = false;
                forceClose = true;
                dlg.DialogResult = DialogResult.OK;
                dlg.Close();
            };

            dlg.FormClosing += (s, e) =>
            {
                if (forceClose) return;
                if (isDirty && !ConfirmDiscardChanges()) e.Cancel = true;
            };

            dlg.Controls.AddRange(new Control[]
            {
                lblName, txtName,
                lblTeacher, cbTeacher,
                chkArchived, lblArchHint,
                lblGroups, lblGroupsHint,
                lblSearchGroups, txtSearchGroups,
                gridCG,
                separator, btnCancel, btnSave
            });
            dlg.AcceptButton = btnSave;
            dlg.CancelButton = btnCancel;
            dlg.ShowDialog(this);
            return resultOk;
        }





        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Только привязка групп к курсу
        // Открывается из вкладки "Настройки курса" → кнопка "Привязка групп".
        // Не трогает название/описание/преподавателя/архив.
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Только привязка групп к курсу
        // ============================================================
        private async Task<bool> ShowCourseGroupsDialog(Course course, List<Group> groups)
        {
            if (course == null) return false;

            bool resultOk = false;
            bool isDirty = false;
            bool forceClose = false;

            Form dlg = new Form
            {
                Text = $"Привязка групп — {course.Name}",
                ClientSize = new Size(460, 510)
            };
            ApplyDialogStyle(dlg);

            Label lblTitle = new Label
            {
                Text = "Группы, которым доступен курс",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(15, 15),
                AutoSize = true
            };
            Label lblHint = new Label
            {
                Text = "Отметьте галочкой группы, студенты которых должны видеть этот курс.\nСнятая галочка скрывает курс от соответствующих студентов (но их сданные работы сохраняются).",
                Location = new Point(15, 38),
                Size = new Size(430, 45),
                ForeColor = Color.DimGray,
                Font = new Font("Segoe UI", 8)
            };

            Label lblSearch = new Label { Text = "Поиск группы:", Location = new Point(15, 92), AutoSize = true };
            TextBox txtSearch = new TextBox { Location = new Point(15, 113), Width = 430, Font = new Font("Segoe UI", 9) };

            DataGridView gridCG = new DataGridView
            {
                Location = new Point(15, 145),
                Size = new Size(430, 280),
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9)
            };
            gridCG.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Check", HeaderText = "✔", FillWeight = 15 });
            gridCG.Columns.Add("GroupName", "Группа");
            gridCG.Columns[1].FillWeight = 85;
            gridCG.Columns[1].ReadOnly = true;
            ApplyModernTableStyle(gridCG);
            gridCG.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (gridCG.IsCurrentCellDirty) gridCG.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            List<int> origGroupIds = await ApiClient.GetCourseGroupsAsync(course.Id) ?? new List<int>();
            HashSet<int> currentChecked = new HashSet<int>(origGroupIds);

            void RefreshGrid()
            {
                gridCG.Rows.Clear();
                string s = txtSearch.Text.Trim().ToLower();
                foreach (var g in groups.Where(x => string.IsNullOrEmpty(s) || x.Name.ToLower().Contains(s)))
                {
                    int idx = gridCG.Rows.Add(currentChecked.Contains(g.Id), g.Name);
                    gridCG.Rows[idx].Tag = g;
                }
            }
            RefreshGrid();

            txtSearch.TextChanged += (s, e) => RefreshGrid();
            gridCG.CellValueChanged += (s, e) =>
            {
                if (e.ColumnIndex != 0 || e.RowIndex < 0) return;
                var row = gridCG.Rows[e.RowIndex];
                var grp = (Group)row.Tag;
                bool ch = (bool)(row.Cells[0].Value ?? false);
                if (ch) currentChecked.Add(grp.Id); else currentChecked.Remove(grp.Id);
                var orig = origGroupIds.OrderBy(x => x).ToList();
                var cur = currentChecked.OrderBy(x => x).ToList();
                isDirty = !cur.SequenceEqual(orig);
            };

            Panel separator = new Panel { Location = new Point(0, 445), Size = new Size(460, 1), BackColor = Color.LightGray };

            Button btnRefreshBtn = new Button { Text = "🔄 Обновить", Location = new Point(15, 460), Size = new Size(110, 28) };
            Button btnCancel = new Button { Text = "Отмена", Location = new Point(265, 460), Size = new Size(85, 28) };
            Button btnSave = new Button { Text = "Сохранить", Location = new Point(360, 460), Size = new Size(85, 28) };
            ApplyDialogButtonStyle(btnRefreshBtn);
            ApplyDialogButtonStyle(btnCancel);
            ApplyDialogButtonStyle(btnSave);

            btnRefreshBtn.Click += async (s, e) =>
            {
                if (isDirty)
                {
                    if (!ShowConfirmDialog("У вас есть несохранённые изменения. Обновить и потерять их?", "Обновление")) return;
                }
                origGroupIds = await ApiClient.GetCourseGroupsAsync(course.Id) ?? new List<int>();
                currentChecked = new HashSet<int>(origGroupIds);
                RefreshGrid();
                isDirty = false;
            };

            btnCancel.Click += (s, e) => { dlg.DialogResult = DialogResult.Cancel; dlg.Close(); };

            btnSave.Click += async (s, e) =>
            {
                var selectedGroupIds = currentChecked.ToList();
                var removedGroupIds = origGroupIds.Except(selectedGroupIds).ToList();
                if (removedGroupIds.Count > 0)
                {
                    var removedNames = groups.Where(g => removedGroupIds.Contains(g.Id)).Select(g => g.Name).ToList();
                    if (!ShowConfirmDialog($"Вы убираете курс у следующих групп:\n{string.Join(", ", removedNames)}\n\nСтуденты этих групп больше не увидят курс. Продолжить?", "Подтверждение отвязки")) return;
                }
                var addedGroupIds = selectedGroupIds.Except(origGroupIds).ToList();
                if (addedGroupIds.Count > 0)
                {
                    var addedNames = groups.Where(g => addedGroupIds.Contains(g.Id)).Select(g => g.Name).ToList();
                    if (!ShowConfirmDialog($"Вы добавляете курс группам:\n{string.Join(", ", addedNames)}\n\nСтуденты этих групп сразу увидят все опубликованные задания. Продолжить?", "Подтверждение привязки")) return;
                }
                btnSave.Enabled = false; btnCancel.Enabled = false; btnRefreshBtn.Enabled = false;

                var res = await ApiClient.UpdateCourseGroupsAdminAsync(course.Id, selectedGroupIds);

                if (res.IsSuccess)
                {
                    ShowSuccessDialog("Привязка групп успешно обновлена.");
                    resultOk = true; isDirty = false; forceClose = true;
                    dlg.DialogResult = DialogResult.OK; dlg.Close();
                    return;
                }

                if (res.IsConflict)
                {
                    resultOk = true; isDirty = false; forceClose = true;
                    ShowAdminStaleDataDialog();
                    dlg.DialogResult = DialogResult.OK; dlg.Close();
                    return;
                }

                if (res.Status == ApiClient.AdminOpStatus.SessionEnded)
                {
                    forceClose = true;
                    dlg.DialogResult = DialogResult.Cancel; dlg.Close();
                    return;
                }

                string msg = !string.IsNullOrEmpty(res.Message)
                    ? res.Message
                    : $"Не удалось сохранить привязки групп для курса «{course.Name}».";
                ShowErrorDialog(msg);
                btnSave.Enabled = true; btnCancel.Enabled = true; btnRefreshBtn.Enabled = true;
            };

            dlg.FormClosing += (s, e) =>
            {
                if (forceClose) return;
                if (isDirty && !ConfirmDiscardChanges()) e.Cancel = true;
            };

            dlg.Controls.AddRange(new Control[] { lblTitle, lblHint, lblSearch, txtSearch, gridCG, separator, btnRefreshBtn, btnCancel, btnSave });
            dlg.AcceptButton = btnSave;
            dlg.CancelButton = btnCancel;
            dlg.ShowDialog(this);
            return resultOk;
        }



        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Только метаданные курса (название/описание/преподаватель/архив)
        // Открывается из вкладки "Настройки курса" → кнопка "Описание / архивация".
        // Не трогает привязки групп.
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Только метаданные курса
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Редактирование КУРСА (метаданные + привязка групп)
        //
        // Изменения по требованию:
        //   • Текстовое поле "Описание" удалено из окна редактирования
        //     (значение в БД сохраняется без изменений — мы передаём существующее
        //      описание обратно при UPDATE, чтобы не затереть его).
        //   • Добавлен блок привязки групп с поисковой строкой и таблицей чекбоксов,
        //     полностью аналогичный тому, что используется в окне создания курса
        //     (ShowCourseEditDialog). При сохранении сначала обновляются метаданные
        //     (имя/преподаватель/архив), затем — список привязанных групп.
        //   • Кнопка "Обновить" перечитывает и метаданные, и привязки групп с сервера.
        // ============================================================
        private async Task<bool> ShowCourseMetaDialog(Course course)
        {
            if (course == null) return false;

            bool resultOk = false;
            bool isDirty = false;
            bool forceClose = false;
            bool isAdmin = (currentUserRole == UserRole.Admin);

            Form dlg = new Form
            {
                Text = $"Редактирование курса — {course.Name}",
                ClientSize = new Size(560, 720)
            };
            ApplyDialogStyle(dlg);

            string origName = course.Name ?? "";
            string origDesc = course.Description ?? "";
            int origTeacherId = course.TeacherId;
            int origArchived = course.Archived;

            Label lblName = new Label { Text = "Название курса:", Location = new Point(15, 18), AutoSize = true };
            TextBox txtName = new TextBox { Location = new Point(15, 40), Width = 530, Text = origName };

            Label lblTeacher = new Label { Text = "Преподаватель:", Location = new Point(15, 78), AutoSize = true };
            ComboBox cbTeacher = new ComboBox { Location = new Point(15, 100), Width = 530, DropDownStyle = ComboBoxStyle.DropDownList };

            if (isAdmin)
            {
                var teachers = await ApiClient.GetTeachersAsync() ?? new List<StudentUser>();
                foreach (var t in teachers) cbTeacher.Items.Add(t);
                cbTeacher.DisplayMember = "FullName";
                var selT = teachers.FirstOrDefault(t => t.Id == origTeacherId);
                if (selT != null) cbTeacher.SelectedItem = selT;
                else if (cbTeacher.Items.Count > 0) cbTeacher.SelectedIndex = 0;
            }
            else
            {
                cbTeacher.Items.Add(course.TeacherName ?? (ApiClient.CurrentUser?.FullName ?? "Я"));
                cbTeacher.SelectedIndex = 0;
                cbTeacher.Enabled = false;
            }

            CheckBox chkArchived = new CheckBox
            {
                Text = "Курс в архиве (студенты не увидят его в своём списке)",
                Location = new Point(15, 138),
                AutoSize = true,
                Checked = origArchived == 1
            };

            Label lblArchHint = new Label
            {
                Text = "Архивный курс остаётся в системе со всеми заданиями и оценками,\nно скрывается из активных списков у студентов.",
                Location = new Point(35, 161),
                AutoSize = true,
                ForeColor = Color.DimGray,
                Font = new Font("Segoe UI", 8)
            };

            Label lblGroups = new Label
            {
                Text = "Группы, которым доступен курс:",
                Location = new Point(15, 210),
                AutoSize = true,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            Label lblGroupsHint = new Label
            {
                Text = "Отметьте галочкой группы, студенты которых должны видеть этот курс.\nСнятая галочка скрывает курс от соответствующих студентов (но их сданные работы сохраняются).",
                Location = new Point(15, 232),
                Size = new Size(530, 32),
                ForeColor = Color.DimGray,
                Font = new Font("Segoe UI", 8)
            };

            Label lblSearchGroups = new Label { Text = "Поиск группы:", Location = new Point(15, 272), AutoSize = true };
            TextBox txtSearchGroups = new TextBox { Location = new Point(15, 292), Width = 530, Font = new Font("Segoe UI", 9) };

            DataGridView gridCG = new DataGridView
            {
                Location = new Point(15, 324),
                Size = new Size(530, 290),
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9)
            };
            gridCG.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Check", HeaderText = "✔", FillWeight = 15 });
            gridCG.Columns.Add("GroupName", "Группа");
            gridCG.Columns[1].FillWeight = 85;
            gridCG.Columns[1].ReadOnly = true;
            ApplyModernTableStyle(gridCG);
            gridCG.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (gridCG.IsCurrentCellDirty) gridCG.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            List<Group> allGroups = await ApiClient.GetGroupsAsync() ?? new List<Group>();
            List<int> origGroupIds = await ApiClient.GetCourseGroupsAsync(course.Id) ?? new List<int>();
            HashSet<int> currentChecked = new HashSet<int>(origGroupIds);

            void RefreshGroupsGrid()
            {
                gridCG.Rows.Clear();
                string s = txtSearchGroups.Text.Trim().ToLower();
                foreach (var g in allGroups.Where(x => string.IsNullOrEmpty(s) || (x.Name ?? "").ToLower().Contains(s)))
                {
                    int idx = gridCG.Rows.Add(currentChecked.Contains(g.Id), g.Name);
                    gridCG.Rows[idx].Tag = g;
                }
            }
            RefreshGroupsGrid();

            txtSearchGroups.TextChanged += (s, e) => RefreshGroupsGrid();

            Panel separator = new Panel { Location = new Point(0, 632), Size = new Size(560, 1), BackColor = Color.LightGray };

            Button btnRefreshBtn = new Button { Text = "🔄 Обновить", Location = new Point(15, 648), Size = new Size(110, 28) };
            Button btnCancel = new Button { Text = "Отмена", Location = new Point(365, 648), Size = new Size(85, 28) };
            Button btnSave = new Button { Text = "Сохранить", Location = new Point(460, 648), Size = new Size(85, 28) };
            ApplyDialogButtonStyle(btnRefreshBtn);
            ApplyDialogButtonStyle(btnCancel);
            ApplyDialogButtonStyle(btnSave);

            EventHandler markDirty = (s, e) =>
            {
                int curTeacherId = origTeacherId;
                if (isAdmin && cbTeacher.SelectedItem is StudentUser su) curTeacherId = su.Id;
                int curArch = chkArchived.Checked ? 1 : 0;

                bool metaChanged = txtName.Text.Trim() != origName
                                   || curTeacherId != origTeacherId
                                   || curArch != origArchived;

                var origSorted = origGroupIds.OrderBy(x => x).ToList();
                var curSorted = currentChecked.OrderBy(x => x).ToList();
                bool groupsChanged = !curSorted.SequenceEqual(origSorted);

                isDirty = metaChanged || groupsChanged;
            };
            txtName.TextChanged += markDirty;
            cbTeacher.SelectedIndexChanged += markDirty;
            chkArchived.CheckedChanged += markDirty;
            gridCG.CellValueChanged += (s, e) =>
            {
                if (e.ColumnIndex != 0 || e.RowIndex < 0) return;
                var row = gridCG.Rows[e.RowIndex];
                if (row.Tag is Group grp)
                {
                    bool ch = (bool)(row.Cells[0].Value ?? false);
                    if (ch) currentChecked.Add(grp.Id); else currentChecked.Remove(grp.Id);
                }
                markDirty(s, e);
            };

            btnRefreshBtn.Click += async (s, e) =>
            {
                if (isDirty)
                {
                    if (!ShowConfirmDialog("У вас есть несохранённые изменения. Обновить и потерять их?", "Обновление")) return;
                }

                var freshCourses = await ApiClient.GetMyCoursesAsync();
                var fresh = freshCourses?.FirstOrDefault(c => c.Id == course.Id);
                if (fresh == null)
                {
                    ShowAdminStaleDataDialog();
                    forceClose = true;
                    dlg.DialogResult = DialogResult.Cancel;
                    dlg.Close();
                    return;
                }

                course.Name = fresh.Name;
                course.Description = fresh.Description;
                course.TeacherId = fresh.TeacherId;
                course.Archived = fresh.Archived;
                course.Version = fresh.Version;

                origName = fresh.Name ?? "";
                origDesc = fresh.Description ?? "";
                origTeacherId = fresh.TeacherId;
                origArchived = fresh.Archived;

                txtName.Text = origName;
                chkArchived.Checked = origArchived == 1;
                if (isAdmin && cbTeacher.Items.Count > 0)
                {
                    foreach (var item in cbTeacher.Items)
                        if (item is StudentUser su && su.Id == origTeacherId) { cbTeacher.SelectedItem = item; break; }
                }

                allGroups = await ApiClient.GetGroupsAsync() ?? new List<Group>();
                origGroupIds = await ApiClient.GetCourseGroupsAsync(course.Id) ?? new List<int>();
                currentChecked = new HashSet<int>(origGroupIds);
                RefreshGroupsGrid();

                isDirty = false;
            };

            btnCancel.Click += (s, e) =>
            {
                dlg.DialogResult = DialogResult.Cancel;
                dlg.Close();
            };

            btnSave.Click += async (s, e) =>
            {
                string crsErr;
                if (!ValidationRules.ValidateCourseName(txtName.Text, out crsErr))
                {
                    ShowWarningDialog(crsErr);
                    return;
                }

                int? newTeacherId = null;
                if (isAdmin)
                {
                    if (cbTeacher.SelectedItem is StudentUser selT) newTeacherId = selT.Id;
                    else { ShowWarningDialog("Выберите преподавателя курса."); return; }
                }
                int newArchived = chkArchived.Checked ? 1 : 0;

                if (isAdmin && newTeacherId.HasValue && newTeacherId.Value != origTeacherId)
                {
                    if (!ShowConfirmDialog("Вы меняете преподавателя курса.\n\nПрежний преподаватель потеряет доступ к этому курсу. Продолжить?", "Смена преподавателя")) return;
                }
                if (newArchived == 1 && origArchived == 0)
                {
                    if (!ShowConfirmDialog("Курс будет переведён в архив.\n\nСтуденты больше не увидят курс в активных. Продолжить?", "Архивация")) return;
                }

                var selectedGroupIds = currentChecked.ToList();
                var removedGroupIds = origGroupIds.Except(selectedGroupIds).ToList();
                if (removedGroupIds.Count > 0)
                {
                    var removedNames = allGroups.Where(g => removedGroupIds.Contains(g.Id)).Select(g => g.Name).ToList();
                    if (!ShowConfirmDialog(
                        $"Вы убираете курс у следующих групп:\n{string.Join(", ", removedNames)}\n\nСтуденты этих групп больше не увидят курс. Продолжить?",
                        "Подтверждение отвязки")) return;
                }
                var addedGroupIds = selectedGroupIds.Except(origGroupIds).ToList();
                if (addedGroupIds.Count > 0)
                {
                    var addedNames = allGroups.Where(g => addedGroupIds.Contains(g.Id)).Select(g => g.Name).ToList();
                    if (!ShowConfirmDialog(
                        $"Вы добавляете курс группам:\n{string.Join(", ", addedNames)}\n\nСтуденты этих групп сразу увидят все опубликованные задания. Продолжить?",
                        "Подтверждение привязки")) return;
                }

                btnSave.Enabled = false; btnCancel.Enabled = false; btnRefreshBtn.Enabled = false;

                var metaRes = await ApiClient.UpdateCourseMetaAdminAsync(course.Id, txtName.Text.Trim(), origDesc, newTeacherId, newArchived, course.Version);

                // Имя курса занято — оставляем окно открытым.
                if (metaRes.IsDuplicateName)
                {
                    ShowWarningDialog(string.IsNullOrEmpty(metaRes.Message)
                        ? $"Курс с названием «{txtName.Text.Trim()}» уже существует. Выберите другое название."
                        : metaRes.Message);
                    btnSave.Enabled = true; btnCancel.Enabled = true; btnRefreshBtn.Enabled = true;
                    txtName.Focus();
                    txtName.SelectAll();
                    return;
                }

                if (metaRes.IsConflict)
                {
                    resultOk = true; isDirty = false; forceClose = true;
                    ShowAdminStaleDataDialog();
                    dlg.DialogResult = DialogResult.OK; dlg.Close();
                    return;
                }
                if (metaRes.Status == ApiClient.AdminOpStatus.SessionEnded)
                {
                    forceClose = true;
                    dlg.DialogResult = DialogResult.Cancel; dlg.Close();
                    return;
                }
                if (!metaRes.IsSuccess)
                {
                    string msg = !string.IsNullOrEmpty(metaRes.Message)
                        ? metaRes.Message
                        : $"Не удалось сохранить изменения курса «{course.Name}».";
                    ShowErrorDialog(msg);
                    btnSave.Enabled = true; btnCancel.Enabled = true; btnRefreshBtn.Enabled = true;
                    return;
                }

                bool groupsChanged = removedGroupIds.Count > 0 || addedGroupIds.Count > 0;
                if (groupsChanged)
                {
                    var grpRes = await ApiClient.UpdateCourseGroupsAdminAsync(course.Id, selectedGroupIds);
                    if (grpRes.IsConflict)
                    {
                        resultOk = true; isDirty = false; forceClose = true;
                        ShowAdminStaleDataDialog();
                        dlg.DialogResult = DialogResult.OK; dlg.Close();
                        return;
                    }
                    if (grpRes.Status == ApiClient.AdminOpStatus.SessionEnded)
                    {
                        forceClose = true;
                        dlg.DialogResult = DialogResult.Cancel; dlg.Close();
                        return;
                    }
                    if (!grpRes.IsSuccess)
                    {
                        string msg = !string.IsNullOrEmpty(grpRes.Message)
                            ? grpRes.Message
                            : $"Метаданные курса «{course.Name}» сохранены, но не удалось обновить привязку групп. Попробуйте обновить окно и сохранить ещё раз.";
                        ShowErrorDialog(msg);
                        btnSave.Enabled = true; btnCancel.Enabled = true; btnRefreshBtn.Enabled = true;
                        return;
                    }
                }

                ShowSuccessDialog("Изменения курса успешно сохранены.");
                resultOk = true; isDirty = false; forceClose = true;
                dlg.DialogResult = DialogResult.OK; dlg.Close();
            };

            dlg.FormClosing += (s, e) =>
            {
                if (forceClose) return;
                if (isDirty && !ConfirmDiscardChanges()) e.Cancel = true;
            };

            dlg.Controls.AddRange(new Control[]
            {
                lblName, txtName,
                lblTeacher, cbTeacher,
                chkArchived, lblArchHint,
                lblGroups, lblGroupsHint,
                lblSearchGroups, txtSearchGroups,
                gridCG,
                separator, btnRefreshBtn, btnCancel, btnSave
            });
            dlg.AcceptButton = btnSave;
            dlg.CancelButton = btnCancel;
            dlg.ShowDialog(this);
            return resultOk;
        }







        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание ПРЕПОДАВАТЕЛЯ (только Admin)
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание ПРЕПОДАВАТЕЛЯ (только Admin)
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Создание ПРЕПОДАВАТЕЛЯ (только Admin)
        // ============================================================
        private async Task<bool> ShowTeacherCreateDialog()
        {
            bool resultOk = false;
            bool isDirty = false;
            bool forceClose = false;

            Form dlg = new Form
            {
                Text = "Создание преподавателя",
                ClientSize = new Size(440, 380)
            };
            ApplyDialogStyle(dlg);

            Label lblFio = new Label { Text = "ФИО (только русские буквы, заглавная первая):", Location = new Point(15, 15), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };

            Label lblLastName = new Label { Text = "Фамилия:", Location = new Point(15, 40), AutoSize = true };
            TextBox txtLastName = new TextBox { Location = new Point(95, 37), Width = 320 };

            Label lblFirstName = new Label { Text = "Имя:", Location = new Point(15, 70), AutoSize = true };
            TextBox txtFirstName = new TextBox { Location = new Point(95, 67), Width = 320 };

            Label lblMiddleName = new Label { Text = "Отчество:", Location = new Point(15, 100), AutoSize = true };
            TextBox txtMiddleName = new TextBox { Location = new Point(95, 97), Width = 320 };

            CheckBox chkNoMiddle = new CheckBox { Text = "Без отчества", Location = new Point(95, 125), AutoSize = true, Font = new Font("Segoe UI", 9) };
            chkNoMiddle.CheckedChanged += (s, e) =>
            {
                txtMiddleName.Enabled = !chkNoMiddle.Checked;
                if (chkNoMiddle.Checked)
                {
                    txtMiddleName.Text = "";
                    txtMiddleName.BackColor = SystemColors.Control;
                }
                else txtMiddleName.BackColor = SystemColors.Window;
            };

            KeyPressEventHandler nameFilter = (s, e) =>
            {
                if (char.IsControl(e.KeyChar)) return;
                bool isRussian = (e.KeyChar >= 'А' && e.KeyChar <= 'я') || e.KeyChar == 'Ё' || e.KeyChar == 'ё';
                bool isAllowedExtra = e.KeyChar == '-' || e.KeyChar == '\'' || e.KeyChar == ' ';
                if (!isRussian && !isAllowedExtra)
                {
                    e.Handled = true;
                    ShowWarningDialog("В ФИО разрешены только русские буквы, дефис и апостроф.");
                }
            };
            txtLastName.KeyPress += nameFilter;
            txtFirstName.KeyPress += nameFilter;
            txtMiddleName.KeyPress += nameFilter;

            Label lblLogin = new Label { Text = "Логин (латиница, минимум 3 символа):", Location = new Point(15, 160), AutoSize = true };
            TextBox txtLogin = new TextBox { Location = new Point(15, 182), Width = 400 };
            txtLogin.KeyPress += (s, e) =>
            {
                if (char.IsControl(e.KeyChar)) return;
                if (e.KeyChar == ' ')
                {
                    e.Handled = true;
                    ShowWarningDialog("Логин не должен содержать пробелы.");
                    return;
                }
                bool isLatinLetter = (e.KeyChar >= 'A' && e.KeyChar <= 'Z') || (e.KeyChar >= 'a' && e.KeyChar <= 'z');
                bool isDigit = e.KeyChar >= '0' && e.KeyChar <= '9';
                bool isAllowedSym = e.KeyChar == '.' || e.KeyChar == '-' || e.KeyChar == '_';
                if (!isLatinLetter && !isDigit && !isAllowedSym)
                {
                    e.Handled = true;
                    ShowWarningDialog("Логин может содержать только латинские буквы, цифры, точку, дефис и подчёркивание.");
                }
            };

            Label lblPwd = new Label { Text = $"Временный пароль (минимум {ValidationRules.PasswordMin} символов, латиница + цифры):", Location = new Point(15, 215), AutoSize = true };
            TextBox txtPwd = new TextBox { Location = new Point(15, 237), Width = 305, PasswordChar = '*' };
            CheckBox chkShow = new CheckBox { Text = "Показать", Location = new Point(330, 239), AutoSize = true, Font = new Font("Segoe UI", 8) };
            chkShow.CheckedChanged += (s, e) => txtPwd.PasswordChar = chkShow.Checked ? '\0' : '*';

            Label lblHint = new Label
            {
                Text = "Пароль временный — при первом входе пользователь обязан задать новый постоянный пароль.",
                Location = new Point(15, 270),
                Size = new Size(400, 30),
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.DimGray
            };

            Panel separator = new Panel { Location = new Point(0, 315), Size = new Size(440, 1), BackColor = Color.LightGray };

            Button btnCancel = new Button { Text = "Отмена", Location = new Point(245, 330), Size = new Size(85, 28) };
            Button btnSave = new Button { Text = "Создать", Location = new Point(340, 330), Size = new Size(85, 28) };
            ApplyDialogButtonStyle(btnCancel);
            ApplyDialogButtonStyle(btnSave);

            EventHandler markDirty = (s, e) =>
            {
                isDirty = !string.IsNullOrEmpty(txtLastName.Text) || !string.IsNullOrEmpty(txtFirstName.Text)
                       || !string.IsNullOrEmpty(txtMiddleName.Text) || !string.IsNullOrEmpty(txtLogin.Text)
                       || !string.IsNullOrEmpty(txtPwd.Text);
            };
            txtLastName.TextChanged += markDirty;
            txtFirstName.TextChanged += markDirty;
            txtMiddleName.TextChanged += markDirty;
            txtLogin.TextChanged += markDirty;
            txtPwd.TextChanged += markDirty;

            btnCancel.Click += (s, e) => { dlg.DialogResult = DialogResult.Cancel; dlg.Close(); };

            btnSave.Click += async (s, e) =>
            {
                string fioErr;
                if (!ValidationRules.ValidateFullNameParts(txtLastName.Text, txtFirstName.Text, txtMiddleName.Text, chkNoMiddle.Checked, out fioErr))
                {
                    ShowWarningDialog(fioErr);
                    return;
                }
                string lgErr;
                if (!ValidationRules.ValidateLogin(txtLogin.Text, out lgErr))
                {
                    ShowWarningDialog(lgErr);
                    return;
                }
                string pwErr;
                if (!ValidationRules.ValidatePassword(txtPwd.Text, out pwErr))
                {
                    ShowWarningDialog(pwErr);
                    return;
                }

                string fullName = ValidationRules.CombineFullName(txtLastName.Text, txtFirstName.Text, txtMiddleName.Text, chkNoMiddle.Checked);
                string login = txtLogin.Text.Trim();

                btnSave.Enabled = false;
                btnCancel.Enabled = false;

                var res = await ApiClient.CreateTeacherAdminAsync(login, txtPwd.Text, fullName);

                if (res.IsSuccess)
                {
                    ShowSuccessDialog($"Преподаватель создан.\nЛогин: {login}\nВременный пароль: {txtPwd.Text}\n\nПри первом входе пользователь сменит пароль.");
                    resultOk = true;
                    isDirty = false;
                    forceClose = true;
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                    return;
                }

                // Логин занят другим пользователем — оставляем окно открытым,
                // показываем конкретное сообщение и фокус сразу на поле логина.
                if (res.IsDuplicateName)
                {
                    ShowWarningDialog(string.IsNullOrEmpty(res.Message)
                        ? $"Логин «{login}» уже занят другим пользователем. Выберите другой логин."
                        : res.Message);
                    btnSave.Enabled = true;
                    btnCancel.Enabled = true;
                    txtLogin.Focus();
                    txtLogin.SelectAll();
                    return;
                }

                if (res.IsConflict)
                {
                    resultOk = true;
                    isDirty = false;
                    forceClose = true;
                    ShowAdminStaleDataDialog();
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                    return;
                }

                if (res.Status == ApiClient.AdminOpStatus.SessionEnded)
                {
                    forceClose = true;
                    dlg.DialogResult = DialogResult.Cancel;
                    dlg.Close();
                    return;
                }

                string msg = !string.IsNullOrEmpty(res.Message)
                    ? res.Message
                    : $"Не удалось создать преподавателя. Возможно, логин «{login}» уже занят.";
                ShowErrorDialog(msg);
                btnSave.Enabled = true;
                btnCancel.Enabled = true;
            };

            dlg.FormClosing += (s, e) =>
            {
                if (forceClose) return;
                if (isDirty && !ConfirmDiscardChanges()) e.Cancel = true;
            };

            dlg.Controls.AddRange(new Control[]
            {
                lblFio, lblLastName, txtLastName, lblFirstName, txtFirstName,
                lblMiddleName, txtMiddleName, chkNoMiddle,
                lblLogin, txtLogin, lblPwd, txtPwd, chkShow, lblHint,
                separator, btnCancel, btnSave
            });
            dlg.AcceptButton = btnSave;
            dlg.CancelButton = btnCancel;
            dlg.ShowDialog(this);
            return resultOk;
        }




        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Редактирование ПРЕПОДАВАТЕЛЯ (только Admin)
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Редактирование ПРЕПОДАВАТЕЛЯ (только Admin)
        // ============================================================
        // ============================================================
        // МОДАЛЬНОЕ ОКНО — Редактирование ПРЕПОДАВАТЕЛЯ (только Admin)
        // ============================================================
        private async Task<bool> ShowTeacherEditDialog(StudentUser existing)
        {
            bool resultOk = false;
            bool isDirty = false;
            bool forceClose = false;

            Form dlg = new Form
            {
                Text = "Редактирование преподавателя",
                ClientSize = new Size(440, 360)
            };
            ApplyDialogStyle(dlg);

            string origLast, origFirst, origMiddle;
            bool origNoMiddle;
            ValidationRules.SplitFullName(existing.FullName, out origLast, out origFirst, out origMiddle, out origNoMiddle);
            string originalLogin = existing.Login ?? "";
            string originalFullName = existing.FullName ?? "";

            Label lblFio = new Label { Text = "ФИО (только русские буквы, заглавная первая):", Location = new Point(15, 15), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };

            Label lblLastName = new Label { Text = "Фамилия:", Location = new Point(15, 40), AutoSize = true };
            TextBox txtLastName = new TextBox { Location = new Point(95, 37), Width = 320, Text = origLast };

            Label lblFirstName = new Label { Text = "Имя:", Location = new Point(15, 70), AutoSize = true };
            TextBox txtFirstName = new TextBox { Location = new Point(95, 67), Width = 320, Text = origFirst };

            Label lblMiddleName = new Label { Text = "Отчество:", Location = new Point(15, 100), AutoSize = true };
            TextBox txtMiddleName = new TextBox { Location = new Point(95, 97), Width = 320, Text = origMiddle };

            CheckBox chkNoMiddle = new CheckBox { Text = "Без отчества", Location = new Point(95, 125), AutoSize = true, Checked = origNoMiddle, Font = new Font("Segoe UI", 9) };
            chkNoMiddle.CheckedChanged += (s, e) =>
            {
                txtMiddleName.Enabled = !chkNoMiddle.Checked;
                if (chkNoMiddle.Checked) { txtMiddleName.Text = ""; txtMiddleName.BackColor = SystemColors.Control; }
                else txtMiddleName.BackColor = SystemColors.Window;
            };
            txtMiddleName.Enabled = !origNoMiddle;
            if (origNoMiddle) txtMiddleName.BackColor = SystemColors.Control;

            KeyPressEventHandler nameFilter = (s, e) =>
            {
                if (char.IsControl(e.KeyChar)) return;
                bool isRussian = (e.KeyChar >= 'А' && e.KeyChar <= 'я') || e.KeyChar == 'Ё' || e.KeyChar == 'ё';
                bool isAllowedExtra = e.KeyChar == '-' || e.KeyChar == '\'' || e.KeyChar == ' ';
                if (!isRussian && !isAllowedExtra)
                {
                    e.Handled = true;
                    ShowWarningDialog("В ФИО разрешены только русские буквы, дефис и апостроф.");
                }
            };
            txtLastName.KeyPress += nameFilter;
            txtFirstName.KeyPress += nameFilter;
            txtMiddleName.KeyPress += nameFilter;

            Label lblLogin = new Label { Text = "Логин (латиница, минимум 3 символа):", Location = new Point(15, 160), AutoSize = true };
            TextBox txtLogin = new TextBox { Location = new Point(15, 182), Width = 400, Text = originalLogin };
            txtLogin.KeyPress += (s, e) =>
            {
                if (char.IsControl(e.KeyChar)) return;
                if (e.KeyChar == ' ')
                {
                    e.Handled = true;
                    ShowWarningDialog("Логин не должен содержать пробелы.");
                    return;
                }
                bool isLatinLetter = (e.KeyChar >= 'A' && e.KeyChar <= 'Z') || (e.KeyChar >= 'a' && e.KeyChar <= 'z');
                bool isDigit = e.KeyChar >= '0' && e.KeyChar <= '9';
                bool isAllowedSym = e.KeyChar == '.' || e.KeyChar == '-' || e.KeyChar == '_';
                if (!isLatinLetter && !isDigit && !isAllowedSym)
                {
                    e.Handled = true;
                    ShowWarningDialog("Логин может содержать только латинские буквы, цифры, точку, дефис и подчёркивание.");
                }
            };

            Label lblHint = new Label
            {
                Text = "Логин и ФИО можно изменить. Для смены пароля используйте кнопку «Сбросить пароль».",
                Location = new Point(15, 215),
                Size = new Size(410, 32),
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.DimGray
            };

            Panel separator = new Panel { Location = new Point(0, 295), Size = new Size(440, 1), BackColor = Color.LightGray };

            Button btnResetPwd = new Button { Text = "Сбросить пароль", Location = new Point(15, 310), Size = new Size(140, 28) };
            ApplyDialogButtonStyle(btnResetPwd);

            Button btnCancel = new Button { Text = "Отмена", Location = new Point(245, 310), Size = new Size(85, 28) };
            ApplyDialogButtonStyle(btnCancel);

            Button btnSave = new Button { Text = "Сохранить", Location = new Point(340, 310), Size = new Size(85, 28) };
            ApplyDialogButtonStyle(btnSave);

            EventHandler markDirty = (s, e) =>
            {
                isDirty = txtLastName.Text.Trim() != origLast
                       || txtFirstName.Text.Trim() != origFirst
                       || txtMiddleName.Text.Trim() != origMiddle
                       || chkNoMiddle.Checked != origNoMiddle
                       || txtLogin.Text.Trim() != originalLogin;
            };
            txtLastName.TextChanged += markDirty;
            txtFirstName.TextChanged += markDirty;
            txtMiddleName.TextChanged += markDirty;
            chkNoMiddle.CheckedChanged += markDirty;
            txtLogin.TextChanged += markDirty;

            btnResetPwd.Click += async (s, e) =>
            {
                if (ApiClient.CurrentUser != null && existing.Login == ApiClient.CurrentUser.Login)
                {
                    ShowWarningDialog("Нельзя сбросить собственный пароль через окно администрирования. Используйте «Сменить пароль» в профиле.");
                    return;
                }
                await ShowResetPasswordWindow(existing.Id, existing.FullName, "Преподаватель");
            };

            btnCancel.Click += (s, e) => { dlg.DialogResult = DialogResult.Cancel; dlg.Close(); };

            btnSave.Click += async (s, e) =>
            {
                string fioErr;
                if (!ValidationRules.ValidateFullNameParts(txtLastName.Text, txtFirstName.Text, txtMiddleName.Text, chkNoMiddle.Checked, out fioErr))
                {
                    ShowWarningDialog(fioErr);
                    return;
                }
                string lgErr;
                if (!ValidationRules.ValidateLogin(txtLogin.Text, out lgErr))
                {
                    ShowWarningDialog(lgErr);
                    return;
                }

                string newName = ValidationRules.CombineFullName(txtLastName.Text, txtFirstName.Text, txtMiddleName.Text, chkNoMiddle.Checked);
                string newLogin = txtLogin.Text.Trim();

                if (newName == originalFullName && newLogin == originalLogin)
                {
                    forceClose = true;
                    dlg.DialogResult = DialogResult.Cancel;
                    dlg.Close();
                    return;
                }

                bool isSelfEdit = ApiClient.CurrentUser != null && existing.Login == ApiClient.CurrentUser.Login;
                if (isSelfEdit && newLogin != originalLogin)
                {
                    ShowWarningDialog("Нельзя менять собственный логин через окно администрирования.");
                    return;
                }

                bool loginChanged = newLogin != originalLogin;
                bool nameChanged = newName != originalFullName;

                if ((loginChanged || nameChanged) && !isSelfEdit)
                {
                    var sessionStatus = await CheckUserOnlineStatus(existing.Id);
                    if (sessionStatus.IsOnline)
                    {
                        var changes = new List<string>();
                        if (nameChanged) changes.Add("ФИО");
                        if (loginChanged) changes.Add("логин");
                        string changesList = string.Join(", ", changes);

                        string actionDescription = loginChanged
                            ? $"Вы изменяете {changesList} преподавателя.\nТак как меняется логин, его сессия будет принудительно завершена и пользователю потребуется войти заново."
                            : $"Вы изменяете {changesList} преподавателя.\nСессия пользователя останется активной, но все его открытые окна будут автоматически закрыты, и он увидит уведомление с обновлёнными данными.";

                        bool confirmed = ShowActiveSessionWarningDialog(existing.FullName, sessionStatus.IpAddress, sessionStatus.LastActivity, actionDescription, "Сохранить");
                        if (!confirmed) return;
                    }
                }

                btnSave.Enabled = false;
                btnCancel.Enabled = false;
                btnResetPwd.Enabled = false;

                var res = await ApiClient.UpdateTeacherAdminAsync(existing.Id, newLogin, newName, existing.Version);

                if (res.IsSuccess)
                {
                    ShowSuccessDialog($"Данные преподавателя «{newName}» обновлены.");
                    resultOk = true;
                    isDirty = false;
                    forceClose = true;
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                    return;
                }

                // Логин занят другим пользователем — окно остаётся открытым.
                if (res.IsDuplicateName)
                {
                    ShowWarningDialog(string.IsNullOrEmpty(res.Message)
                        ? $"Логин «{newLogin}» уже занят другим пользователем. Выберите другой логин."
                        : res.Message);
                    btnSave.Enabled = true;
                    btnCancel.Enabled = true;
                    btnResetPwd.Enabled = true;
                    txtLogin.Focus();
                    txtLogin.SelectAll();
                    return;
                }

                if (res.IsConflict)
                {
                    resultOk = true;
                    isDirty = false;
                    forceClose = true;
                    ShowAdminStaleDataDialog();
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                    return;
                }

                if (res.Status == ApiClient.AdminOpStatus.SessionEnded)
                {
                    forceClose = true;
                    dlg.DialogResult = DialogResult.Cancel;
                    dlg.Close();
                    return;
                }

                string msg = !string.IsNullOrEmpty(res.Message)
                    ? res.Message
                    : $"Не удалось сохранить изменения для преподавателя «{newName}».";
                ShowErrorDialog(msg);
                btnSave.Enabled = true;
                btnCancel.Enabled = true;
                btnResetPwd.Enabled = true;
            };

            dlg.FormClosing += (s, e) =>
            {
                if (forceClose) return;
                if (isDirty && !ConfirmDiscardChanges()) e.Cancel = true;
            };

            dlg.Controls.AddRange(new Control[]
            {
                lblFio, lblLastName, txtLastName, lblFirstName, txtFirstName,
                lblMiddleName, txtMiddleName, chkNoMiddle,
                lblLogin, txtLogin, lblHint,
                separator, btnResetPwd, btnCancel, btnSave
            });
            dlg.AcceptButton = btnSave;
            dlg.CancelButton = btnCancel;
            dlg.ShowDialog(this);
            return resultOk;
        }








        private async void ShowStudentGradesWindow(string studentName, string courseName, Form parentForm = null)
        {
            Form gradesForm = new Form
            {
                Text = $"Детализация оценок: {studentName}",
                Size = new Size(720, 540),
                MinimumSize = new Size(720, 540),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.Sizable,
                MaximizeBox = true,
                MinimizeBox = false,
                BackColor = Color.WhiteSmoke
            };

            Font lblFont = new Font("Segoe UI", 9, FontStyle.Regular);
            Font valFont = new Font("Segoe UI", 9, FontStyle.Bold);

            int startY = 15;
            Label lblCourse = new Label { Text = "Курс:", Location = new Point(15, startY + 3), AutoSize = true, Font = lblFont, ForeColor = Color.DimGray };
            TextBox txtCourse = new TextBox { Text = courseName, Location = new Point(60, startY), Width = 240, ReadOnly = true, BackColor = SystemColors.Window, Font = valFont };
            Label lblName = new Label { Text = "ФИО:", Location = new Point(320, startY + 3), AutoSize = true, Font = lblFont, ForeColor = Color.DimGray };
            TextBox txtName = new TextBox { Text = studentName, Location = new Point(365, startY), Width = 325, ReadOnly = true, BackColor = SystemColors.Window, Font = valFont, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

            startY += 35;
            Label lblAvg = new Label { Text = "Сформированная оценка:", Location = new Point(15, startY + 3), AutoSize = true, Font = lblFont, ForeColor = Color.DimGray };
            TextBox txtAvg = new TextBox { Location = new Point(175, startY), Width = 95, ReadOnly = true, BackColor = SystemColors.Window, Font = valFont };
            Label lblFinal = new Label { Text = "Итог:", Location = new Point(285, startY + 3), AutoSize = true, Font = lblFont, ForeColor = Color.DimGray };
            TextBox txtFinal = new TextBox { Location = new Point(325, startY), Width = 365, ReadOnly = true, BackColor = SystemColors.Window, Font = valFont, ForeColor = Color.ForestGreen, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

            startY += 40;
            Label lblSearch = new Label { Text = "Поиск:", Location = new Point(15, startY + 3), AutoSize = true, Font = lblFont };
            TextBox txtSearch = new TextBox { Location = new Point(65, startY), Width = 230, Font = lblFont };
            Label lblType = new Label { Text = "Тип:", Location = new Point(310, startY + 3), AutoSize = true, Font = lblFont };
            ComboBox cbType = new ComboBox { Location = new Point(345, startY), Width = 345, DropDownStyle = ComboBoxStyle.DropDownList, Font = lblFont, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            cbType.Items.AddRange(new[] { "Все", "Домашняя работа", "Лабораторная работа", "Самостоятельная работа", "Контрольная работа", "Экзамен" });
            cbType.SelectedIndex = 0;

            startY += 35;

            Button btnRefresh = new Button { Text = "🔄 Обновить", Location = new Point(15, gradesForm.ClientSize.Height - 45), Size = new Size(120, 30), FlatStyle = FlatStyle.Standard, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            Button btnOpenSubmission = new Button { Text = "Открыть", Location = new Point(145, gradesForm.ClientSize.Height - 45), Size = new Size(120, 30), FlatStyle = FlatStyle.Standard, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Enabled = false };

            DataGridView gridGrades = new DataGridView
            {
                Location = new Point(15, startY),
                Size = new Size(675, gradesForm.ClientSize.Height - startY - 55),
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            ApplyModernTableStyle(gridGrades);
            gridGrades.Columns.Add("Task", "Задание"); gridGrades.Columns[0].FillWeight = 40;
            gridGrades.Columns.Add("Type", "Тип"); gridGrades.Columns[1].FillWeight = 25;
            gridGrades.Columns.Add("Grade", "Оценка"); gridGrades.Columns[2].FillWeight = 12;
            gridGrades.Columns.Add("Status", "Статус"); gridGrades.Columns[3].FillWeight = 18;
            gridGrades.Columns.Add("SubId", "SubId"); gridGrades.Columns[4].Visible = false;
            gridGrades.Columns.Add("AssignmentId", "AssignmentId"); gridGrades.Columns[5].Visible = false;

            List<PerformanceRecord> studentGrades = new List<PerformanceRecord>();
            List<Submission> courseSubmissions = new List<Submission>();
            List<Assignment> courseAssignments = new List<Assignment>();
            int? studentId = null;

            void RefreshGradesGrid()
            {
                gridGrades.Rows.Clear();
                string search = txtSearch.Text.Trim().ToLower();
                string filterType = cbType.SelectedItem?.ToString() ?? "Все";

                var visible = studentGrades.Where(g => !string.IsNullOrEmpty(g.Task));

                foreach (var g in visible)
                {
                    if (!string.IsNullOrEmpty(search) && !(g.Task ?? "").ToLower().Contains(search)) continue;
                    if (filterType != "Все" && (g.Type ?? "") != filterType) continue;

                    string statusText;
                    if (g.Status == "Оценено") statusText = "Оценено";
                    else if (g.Status == "Не оценено") statusText = "Не оценено";
                    else statusText = "Не сдано";


                    var assignment = courseAssignments.FirstOrDefault(a => a.Title == g.Task);
                    int subId = 0;
                    int aid = assignment?.Id ?? 0;

                    gridGrades.Rows.Add(g.Task, g.Type ?? "Домашняя работа", g.Grade?.ToString() ?? "—", statusText, subId, aid);
                }
            }

            void UpdateOpenButtonState()
            {
                if (gridGrades.CurrentRow == null) { btnOpenSubmission.Enabled = false; return; }
                string st = gridGrades.CurrentRow.Cells[3].Value?.ToString() ?? "";
                btnOpenSubmission.Enabled = (st == "Не оценено" || st == "Оценено");
            }

            async Task LoadData()
            {
                try
                {
                    var globalData = await ApiClient.GetGlobalPerformanceAsync() ?? new List<PerformanceRecord>();
                    studentGrades = globalData
                        .Where(p => p != null && p.Name == studentName && p.Course == courseName)
                        .ToList();

                    string policy = studentGrades.FirstOrDefault()?.GradingPolicy
                                    ?? globalData.FirstOrDefault(p => p != null && p.Course == courseName)?.GradingPolicy
                                    ?? "";

                    var validRows = studentGrades.Where(g => !string.IsNullOrEmpty(g.Task)).ToList();
                    var graded = validRows.Where(g => g.Grade.HasValue).ToList();
                    double avgGrade = graded.Count > 0 ? graded.Average(g => g.Grade.Value) : 0;

                    double finalGrade = 0;
                    try
                    {
                        finalGrade = CalculateFinalGrade(
                            validRows.Where(g => !string.IsNullOrEmpty(g.Type))
                                     .Select(g => (g.Type, g.Grade)),
                            policy);
                    }
                    catch { finalGrade = 0; }

                    var allAssignments = await ApiClient.GetAssignmentsAsync() ?? new List<Assignment>();
                    courseAssignments = allAssignments.Where(a => a != null && a.CourseName == courseName).ToList();

                    var courseStudents = courseAssignments.Count > 0
                        ? await ApiClient.GetCourseStudentsAsync(courseAssignments[0].CourseId)
                        : new List<CourseStudent>();
                    var thisStudent = courseStudents.FirstOrDefault(s => s.FullName == studentName);
                    studentId = thisStudent?.Id;

                    courseSubmissions = new List<Submission>();
                    foreach (var a in courseAssignments)
                    {
                        var subs = await ApiClient.GetSubmissionsAsync(a.Id) ?? new List<Submission>();
                        var mySub = subs.FirstOrDefault(s => s.StudentName == studentName);
                        if (mySub != null)
                        {
                            courseSubmissions.Add(mySub);
                        }
                    }

                    txtAvg.Text = avgGrade.ToString("0.0");
                    txtFinal.Text = ((int)Math.Round(finalGrade)).ToString();

                    gridGrades.Rows.Clear();
                    string search = txtSearch.Text.Trim().ToLower();
                    string filterType = cbType.SelectedItem?.ToString() ?? "Все";

                    foreach (var g in validRows)
                    {
                        if (!string.IsNullOrEmpty(search) && !(g.Task ?? "").ToLower().Contains(search)) continue;
                        if (filterType != "Все" && (g.Type ?? "") != filterType) continue;

                        string statusText;
                        if (g.Status == "Оценено") statusText = "Оценено";
                        else if (g.Status == "Не оценено") statusText = "Не оценено";
                        else statusText = "Не сдано";


                        var assignment = courseAssignments.FirstOrDefault(a => a.Title == g.Task);
                        int aid = assignment?.Id ?? 0;
                        int subId = 0;
                        if (assignment != null)
                        {
                            var subs = await ApiClient.GetSubmissionsAsync(assignment.Id) ?? new List<Submission>();
                            var mySub = subs.FirstOrDefault(s => s.StudentName == studentName);
                            if (mySub != null) subId = mySub.Id;
                        }

                        gridGrades.Rows.Add(g.Task, g.Type ?? "Домашняя работа", g.Grade?.ToString() ?? "—", statusText, subId, aid);
                    }

                    UpdateOpenButtonState();
                }
                catch (Exception ex)
                {
                    txtAvg.Text = "0.0";
                    txtFinal.Text = "0";
                    studentGrades = new List<PerformanceRecord>();
                    gridGrades.Rows.Clear();
                    ShowErrorDialog($"Не удалось загрузить детализацию оценок: {ex.Message}");
                }
            }

            txtSearch.TextChanged += (s, e) => RefreshGradesGrid();
            cbType.SelectedIndexChanged += (s, e) => RefreshGradesGrid();
            btnRefresh.Click += async (s, e) => await LoadData();
            gridGrades.SelectionChanged += (s, e) => UpdateOpenButtonState();

            btnOpenSubmission.Click += async (s, e) =>
            {
                if (gridGrades.CurrentRow == null) return;
                string st = gridGrades.CurrentRow.Cells[3].Value?.ToString() ?? "";
                if (st != "Не оценено" && st != "Оценено") return;

                int subId = 0;
                int aid = 0;
                try
                {
                    subId = Convert.ToInt32(gridGrades.CurrentRow.Cells[4].Value ?? 0);
                    aid = Convert.ToInt32(gridGrades.CurrentRow.Cells[5].Value ?? 0);
                }
                catch { }

                if (subId == 0 || aid == 0)
                {
                    ShowWarningDialog("Не удалось определить работу студента. Попробуйте обновить окно.");
                    return;
                }

                var subs = await ApiClient.GetSubmissionsAsync(aid) ?? new List<Submission>();
                var mySub = subs.FirstOrDefault(x => x.Id == subId);
                if (mySub == null)
                {
                    ShowWarningDialog("Работа недоступна. Возможно, она была удалена. Обновите окно.");
                    return;
                }

                if(mySub.Status == "Оценено")
{
                    if (!ShowConfirmDialog("Эта работа уже оценена. Открыть её для смены комментария или отзыва оценки?", "Открыть оценённую работу"))
                        return;
                }


                string taskName = gridGrades.CurrentRow.Cells[0].Value?.ToString() ?? "Проверка работы";
                currentAssignmentId = aid;

                // Скрываем окно "Детализация оценок", не закрывая его — чтобы при выходе из режима
                // проверки восстановить его в той же позиции и с тем же содержимым.
                // Передаём gradesForm в ActivateCheckingMode как третий "источник" окна — это сигнал
                // ExitCheckingMode не переоткрывать "Проверку работ", а вернуться в детализацию.
                this.BeginInvoke(new Action(() => ActivateCheckingMode(subId, studentName, taskName, 10, mySub.SolutionJson ?? "", parentForm, mySub.Version, gradesForm)));
            };

            await LoadData();

            // Автоматическое обновление окна «Детализация оценок»: при изменении
            // работ или заданий курса — таблица обновится сама.
            Action<string, string, int> onBusChanged = (entity, action, id) =>
            {
                if (gradesForm.IsDisposed) return;
                if (entity != "Submission" && entity != "Assignment" && entity != "GradingPolicy") return;
                try
                {
                    gradesForm.BeginInvoke(new Action(async () => { try { await LoadData(); } catch { } }));
                }
                catch { }
            };
            DataRefreshBus.Changed += onBusChanged;
            gradesForm.FormClosed += (s, e) => { DataRefreshBus.Changed -= onBusChanged; };

            gradesForm.Controls.AddRange(new Control[] { lblCourse, txtCourse, lblName, txtName, lblAvg, txtAvg, lblFinal, txtFinal, lblSearch, txtSearch, lblType, cbType, gridGrades, btnRefresh, btnOpenSubmission });
            gradesForm.ShowDialog(parentForm ?? this);
        }







        // ====================================================================
        // НОВОЕ ОКНО: единое окно преподавателя/админа "Мои курсы"
        // Слева — список курсов, справа — TabControl с тремя вкладками
        // ====================================================================
        // ====================================================================
        // ОКНО "Мои курсы" для преподавателя/администратора.
        //
        // Изменения по требованию:
        //   • Удалена вкладка "Настройки курса".
        //   • В левой панели под таблицей курсов кнопки "Создать курс"/"Изменить"/"Удалить"
        //     удалены — управление курсами вынесено целиком в окно "Администрирование"
        //     (доступно только администратору).
        //   • Под таблицей курсов оставлена единственная кнопка "📊 Формула оценки",
        //     которая открывает окно редактирования формулы для выбранного курса.
        //   • Преподаватель не имеет административных полномочий, поэтому ему теперь
        //     доступны только просмотр заданий и сводной статистики студентов плюс
        //     редактирование формулы оценки своего курса.
        // ====================================================================
        private async void ShowTeacherUnifiedWindow()
        {
            const int LEFT_MIN = 380;
            const int RIGHT_MIN = 630;
            const int DEFAULT_LEFT = 380;

            Form form = new Form
            {
                Text = "Мои курсы",
                ClientSize = new Size(1200, 700),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.White,
                MinimumSize = new Size(LEFT_MIN + RIGHT_MIN + 40, 700)
            };

            SplitContainer split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                SplitterWidth = 5,
                BackColor = Color.LightGray
            };
            split.Panel1.Padding = new Padding(10);
            split.Panel2.Padding = new Padding(10);
            split.Panel1.BackColor = Color.White;
            split.Panel2.BackColor = Color.White;

            GroupBox gbCourses = new GroupBox
            {
                Text = "Курсы",
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.White
            };

            Panel pnlCourseSearch = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Color.White, Padding = new Padding(0, 0, 0, 10) };
            TextBox txtSearchCourse = new TextBox { Dock = DockStyle.Top, Font = new Font("Segoe UI", 9) };
            pnlCourseSearch.Controls.Add(txtSearchCourse);

            // Под таблицей курсов оставлены только две кнопки: "Формула оценки" (главное действие
            // преподавателя/админа в этом окне) и "🔄 Обновить". Остальные операции по управлению
            // курсами теперь живут в окне "Администрирование".
            FlowLayoutPanel pnlCourseBtns = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor = Color.White,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 12, 0, 5)
            };
            Button btnGradingFormula = new Button
            {
                Text = "📊 Формула оценки",
                Margin = new Padding(0, 0, 5, 0),
                Size = new Size(180, 28),
                FlatStyle = FlatStyle.Standard,
                Enabled = false
            };
            Button btnRefreshCourses = new Button
            {
                Text = "🔄 Обновить",
                Margin = new Padding(0, 0, 5, 0),
                Size = new Size(110, 28),
                FlatStyle = FlatStyle.Standard
            };
            pnlCourseBtns.Controls.AddRange(new Control[] { btnGradingFormula, btnRefreshCourses });

            DataGridView gridCourses = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Font = new Font("Segoe UI", 9),
                BorderStyle = BorderStyle.FixedSingle
            };
            gridCourses.Columns.Add("Name", "Название");
            gridCourses.Columns.Add("Status", "Статус"); gridCourses.Columns[1].FillWeight = 35;
            gridCourses.Columns[0].FillWeight = 65;
            ApplyModernTableStyle(gridCourses);

            gbCourses.Controls.Add(gridCourses);
            gbCourses.Controls.Add(pnlCourseSearch);
            gbCourses.Controls.Add(pnlCourseBtns);
            split.Panel1.Controls.Add(gbCourses);

            Label lblCourseTitle = new Label { Text = "Выберите курс слева", Font = new Font("Segoe UI", 14, FontStyle.Bold), Dock = DockStyle.Top, Height = 32 };
            TabControl tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9) };
            TabPage tabAssignments = new TabPage("Задания") { BackColor = Color.White };
            TabPage tabStudents = new TabPage("Студенты и оценки") { BackColor = Color.White };
            // Вкладка "Настройки курса" удалена — её функционал больше не относится
            // к окну "Мои курсы". Формула оценки выведена в левую панель отдельной кнопкой,
            // а описание/архивация/привязка групп — в окно "Администрирование".
            tabs.TabPages.AddRange(new[] { tabAssignments, tabStudents });
            split.Panel2.Controls.Add(tabs);
            split.Panel2.Controls.Add(lblCourseTitle);
            form.Controls.Add(split);

            // ===== ВКЛАДКА "ЗАДАНИЯ" =====
            Panel pnlTaskFilters = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.White, Padding = new Padding(0, 8, 0, 8) };
            Label lblTaskSearch = new Label { Text = "Поиск:", Location = new Point(5, 8), AutoSize = true };
            TextBox txtTaskSearch = new TextBox { Location = new Point(5, 28), Width = 200, Font = new Font("Segoe UI", 9) };
            Label lblTaskType = new Label { Text = "Тип:", Location = new Point(220, 8), AutoSize = true };
            ComboBox cbTaskType = new ComboBox { Location = new Point(220, 28), Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9) };
            cbTaskType.Items.AddRange(new[] { "Все", "Домашняя работа", "Лабораторная работа", "Самостоятельная работа", "Контрольная работа", "Экзамен" });
            cbTaskType.SelectedIndex = 0;
            Label lblTaskStatus = new Label { Text = "Статус:", Location = new Point(395, 8), AutoSize = true };

            ComboBox cbTaskStatus = new ComboBox { Location = new Point(395, 28), Width = 130, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9) };
            // НОВЫЕ статусы заданий у преподавателя: только "Опубликовано" и "Архив".
            cbTaskStatus.Items.AddRange(new[] { "Все", "Опубликовано", "Архив" });
            cbTaskStatus.SelectedIndex = 0;


            pnlTaskFilters.Controls.AddRange(new Control[] { lblTaskSearch, txtTaskSearch, lblTaskType, cbTaskType, lblTaskStatus, cbTaskStatus });

            DataGridView gridTasks = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Font = new Font("Segoe UI", 9),
                BorderStyle = BorderStyle.FixedSingle
            };
            gridTasks.Columns.Add("Title", "Название");
            gridTasks.Columns.Add("Type", "Тип");
            gridTasks.Columns.Add("Deadline", "Срок");
            gridTasks.Columns.Add("Status", "Статус");
            ApplyModernTableStyle(gridTasks);

            FlowLayoutPanel pnlTaskBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 12, 0, 5) };
            Button btnCreateTask = new Button { Text = "+ Создать задание", Margin = new Padding(0, 0, 5, 0), Size = new Size(150, 28), FlatStyle = FlatStyle.Standard };
            Button btnEditTask = new Button { Text = "Изменить", Margin = new Padding(0, 0, 5, 0), Size = new Size(90, 28), FlatStyle = FlatStyle.Standard, Enabled = false };
            Button btnDeleteTask = new Button { Text = "Удалить", Margin = new Padding(0, 0, 5, 0), Size = new Size(85, 28), FlatStyle = FlatStyle.Standard, Enabled = false };
            Button btnCheckTask = new Button { Text = "Проверить работы", Margin = new Padding(0, 0, 5, 0), Size = new Size(160, 28), FlatStyle = FlatStyle.Standard, Enabled = false };
            Button btnRefreshTasks = new Button { Text = "🔄 Обновить", Margin = new Padding(0, 0, 5, 0), Size = new Size(100, 28), FlatStyle = FlatStyle.Standard };
            pnlTaskBtns.Controls.AddRange(new Control[] { btnCreateTask, btnEditTask, btnDeleteTask, btnCheckTask, btnRefreshTasks });
            tabAssignments.Controls.Add(gridTasks);
            tabAssignments.Controls.Add(pnlTaskFilters);
            tabAssignments.Controls.Add(pnlTaskBtns);

            // ===== ВКЛАДКА "СТУДЕНТЫ И ОЦЕНКИ" =====
            Panel pnlStudFilters = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.White, Padding = new Padding(0, 8, 0, 8) };
            Label lblStudSearch = new Label { Text = "Поиск (ФИО):", Location = new Point(5, 8), AutoSize = true };
            TextBox txtStudSearch = new TextBox { Location = new Point(5, 28), Width = 250, Font = new Font("Segoe UI", 9) };
            Label lblStudGroup = new Label { Text = "Группа:", Location = new Point(270, 8), AutoSize = true };
            ComboBox cbStudGroup = new ComboBox { Location = new Point(270, 28), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9) };
            cbStudGroup.Items.Add("Все");
            cbStudGroup.SelectedIndex = 0;
            pnlStudFilters.Controls.AddRange(new Control[] { lblStudSearch, txtStudSearch, lblStudGroup, cbStudGroup });

            DataGridView gridStudents = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Font = new Font("Segoe UI", 9),
                BorderStyle = BorderStyle.FixedSingle
            };
            gridStudents.Columns.Add("Name", "ФИО"); gridStudents.Columns[0].FillWeight = 40;
            gridStudents.Columns.Add("Group", "Группа"); gridStudents.Columns[1].FillWeight = 15;
            gridStudents.Columns.Add("Done", "Сдано / Всего"); gridStudents.Columns[2].FillWeight = 15;
            gridStudents.Columns.Add("Final", "Итог"); gridStudents.Columns[3].FillWeight = 15;

            ApplyModernTableStyle(gridStudents);

            FlowLayoutPanel pnlStudBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(0, 8, 0, 5) };
            Button btnViewStudent = new Button { Text = "Открыть детализацию", Margin = new Padding(0, 0, 5, 5), Size = new Size(180, 28), FlatStyle = FlatStyle.Standard, Enabled = false };
            Button btnRefreshStudents = new Button { Text = "🔄 Обновить", Margin = new Padding(0, 0, 5, 5), Size = new Size(110, 28), FlatStyle = FlatStyle.Standard };
            pnlStudBtns.Controls.AddRange(new Control[] { btnViewStudent, btnRefreshStudents });
            tabStudents.Controls.Add(gridStudents);
            tabStudents.Controls.Add(pnlStudFilters);
            tabStudents.Controls.Add(pnlStudBtns);

            List<Course> allCourses = new List<Course>();
            List<Assignment> allAssignments = new List<Assignment>();
            List<PerformanceRecord> allPerf = new List<PerformanceRecord>();
            List<Group> allGroups = new List<Group>();

            // Флаги подавления SelectionChanged во время программной перестройки гридов.
            // Id/ключи текущего выбора (null/"" = ничего не выбрано).
            // Поиск/фильтр НЕ сбрасывает выбор: если элемент остаётся в выборке —
            // он остаётся выделенным; если уходит — выделение снимается. Авто-выбор
            // первой строки при открытии или при наборе в поиске запрещён.
            bool isRefreshingCourses = false;
            bool isRefreshingTasks = false;
            bool isRefreshingStudents = false;
            int? selectedCourseId = null;
            int? selectedTaskId = null;
            string selectedStudentKey = null; // "Имя|Группа"

            int? GetSelectedCourseId() => selectedCourseId;
            Course FindCourse(int? cid) => cid.HasValue ? allCourses.FirstOrDefault(x => x.Id == cid.Value) : null;

            // Программно снимает выделение и убирает синюю рамку фокуса.
            // Для FullRowSelect: ClearSelection + CurrentCell=null через BeginInvoke,
            // потому что после Rows.Add WinForms восстанавливает CurrentCell в очереди сообщений.
            void ResetGridSelection(DataGridView g)
            {
                if (g == null) return;
                g.ClearSelection();
                g.BeginInvoke(new Action(() =>
                {
                    try { g.CurrentCell = null; } catch { }
                    g.ClearSelection();
                }));
            }

            // Выставляет выделение на конкретную строку (без триггера SelectionChanged — флаг уже установлен снаружи).
            void SelectGridRow(DataGridView g, int rowIdx)
            {
                g.ClearSelection();
                g.Rows[rowIdx].Selected = true;
                try { g.CurrentCell = g.Rows[rowIdx].Cells[0]; } catch { }
            }

            // Перестраивает список курсов с учётом поиска.
            // Восстанавливает ранее выбранный курс, если он попадает в результаты.
            // Если нет — снимает выделение (не авто-выделяет первую строку).
            void RefreshCoursesGrid()
            {
                isRefreshingCourses = true;
                try
                {
                    gridCourses.Rows.Clear();
                    string search = txtSearchCourse.Text.Trim().ToLower();
                    int? rowToSelect = null;
                    foreach (var c in allCourses.Where(x => string.IsNullOrEmpty(search) || x.Name.ToLower().Contains(search)))
                    {
                        string st = c.Archived == 1 ? "🗄 Архив" : "✅";
                        int idx = gridCourses.Rows.Add(c.Name, st);
                        gridCourses.Rows[idx].Tag = c.Id;
                        if (selectedCourseId.HasValue && c.Id == selectedCourseId.Value)
                            rowToSelect = idx;
                    }
                    if (rowToSelect.HasValue)
                        SelectGridRow(gridCourses, rowToSelect.Value);
                    else
                    {
                        selectedCourseId = null;
                        ResetGridSelection(gridCourses);
                    }
                }
                finally
                {
                    isRefreshingCourses = false;
                }
                OnCourseChanged();
            }

            void RefreshTasksGrid()
            {
                isRefreshingTasks = true;
                try
                {
                    gridTasks.Rows.Clear();
                    var c = FindCourse(GetSelectedCourseId());
                    if (c == null) { btnCreateTask.Enabled = false; ResetGridSelection(gridTasks); return; }
                    btnCreateTask.Enabled = true;
                    if (allAssignments == null) { ResetGridSelection(gridTasks); return; }
                    string search = txtTaskSearch.Text.Trim().ToLower();
                    string filterType = cbTaskType.SelectedItem?.ToString() ?? "Все";
                    string filterStatus = cbTaskStatus.SelectedItem?.ToString() ?? "Все";
                    var filtered = allAssignments
                        .Where(a => a != null && ((a.CourseId != 0 && a.CourseId == c.Id) || (a.CourseId == 0 && !string.IsNullOrEmpty(a.CourseName) && a.CourseName == c.Name)))
                        .Where(a => string.IsNullOrEmpty(search) || (a.Title ?? "").ToLower().Contains(search))
                        .Where(a => filterType == "Все" || (a.Type ?? "") == filterType)
                        .Where(a => filterStatus == "Все" || (a.Status ?? "") == filterStatus)
                        .OrderBy(a => a.Deadline).ToList();
                    int? rowToSelect = null;
                    foreach (var a in filtered)
                    {
                        int idx = gridTasks.Rows.Add(a.Title ?? "(без названия)", a.Type ?? "Домашняя работа", a.Deadline.ToString("dd.MM.yyyy HH:mm"), a.Status ?? "Черновик");
                        gridTasks.Rows[idx].Tag = a.Id;
                        if (selectedTaskId.HasValue && a.Id == selectedTaskId.Value)
                            rowToSelect = idx;
                    }
                    if (rowToSelect.HasValue)
                        SelectGridRow(gridTasks, rowToSelect.Value);
                    else
                    {
                        selectedTaskId = null;
                        ResetGridSelection(gridTasks);
                    }
                }
                catch { }
                finally
                {
                    isRefreshingTasks = false;
                    bool any = gridTasks.SelectedRows.Count > 0;
                    btnEditTask.Enabled = btnDeleteTask.Enabled = btnCheckTask.Enabled = any;
                }
            }

            void RefreshStudentsGrid()
            {
                isRefreshingStudents = true;
                try
                {
                    gridStudents.Rows.Clear();
                    var c = FindCourse(GetSelectedCourseId());
                    if (c == null) { ResetGridSelection(gridStudents); return; }
                    if (allPerf == null) { ResetGridSelection(gridStudents); return; }
                    string search = txtStudSearch.Text.Trim().ToLower();
                    string filterGroup = cbStudGroup.SelectedItem?.ToString() ?? "Все";
                    var grouped = allPerf.Where(p => p != null && p.Course == c.Name)
                        .GroupBy(p => new { Name = p.Name ?? "—", Group = p.Group ?? "—" })
                        .Where(g => string.IsNullOrEmpty(search) || g.Key.Name.ToLower().Contains(search))
                        .Where(g => filterGroup == "Все" || g.Key.Group == filterGroup).ToList();
                    int? rowToSelect = null;
                    foreach (var g in grouped)
                    {
                        var withTask = g.Where(x => !string.IsNullOrEmpty(x.Task)).ToList();
                        int total = withTask.Count;
                        int done = withTask.Count(x => x.Status == "Оценено");
                        double final = 0;
                        try { final = CalculateFinalGrade(withTask.Where(x => !string.IsNullOrEmpty(x.Type)).Select(x => (x.Type, x.Grade)), c.GradingPolicy); } catch { final = 0; }
                        int idx = gridStudents.Rows.Add(g.Key.Name, g.Key.Group, $"{done}/{total}", ((int)Math.Round(final)).ToString());
                        string key = g.Key.Name + "|" + g.Key.Group;
                        gridStudents.Rows[idx].Tag = key;
                        if (!string.IsNullOrEmpty(selectedStudentKey) && key == selectedStudentKey)
                            rowToSelect = idx;
                    }
                    if (rowToSelect.HasValue)
                        SelectGridRow(gridStudents, rowToSelect.Value);
                    else
                    {
                        selectedStudentKey = null;
                        ResetGridSelection(gridStudents);
                    }
                }
                catch { }
                finally
                {
                    isRefreshingStudents = false;
                    btnViewStudent.Enabled = gridStudents.SelectedRows.Count > 0;
                }
            }


            void RefreshStudGroupFilter()
            {
                var c = FindCourse(GetSelectedCourseId());
                string prev = cbStudGroup.SelectedItem?.ToString() ?? "Все";
                cbStudGroup.Items.Clear();
                cbStudGroup.Items.Add("Все");
                if (c != null && allPerf != null)
                {
                    var groups = allPerf.Where(p => p != null && p.Course == c.Name && !string.IsNullOrEmpty(p.Group)).Select(p => p.Group).Distinct().OrderBy(x => x).ToList();
                    foreach (var gn in groups) cbStudGroup.Items.Add(gn);
                }
                int newIdx = cbStudGroup.Items.IndexOf(prev);
                cbStudGroup.SelectedIndex = newIdx >= 0 ? newIdx : 0;
            }

            bool isLoading = false;
            bool loadPending = false;
            async Task LoadAllData()
            {
                if (isLoading) { loadPending = true; return; }
                isLoading = true;
                try
                {
                    try { allCourses = await ApiClient.GetMyCoursesAsync() ?? new List<Course>(); } catch { allCourses = new List<Course>(); }
                    try { allAssignments = await ApiClient.GetAssignmentsAsync() ?? new List<Assignment>(); } catch { allAssignments = new List<Assignment>(); }
                    try { allPerf = await ApiClient.GetGlobalPerformanceAsync() ?? new List<PerformanceRecord>(); } catch { allPerf = new List<PerformanceRecord>(); }
                    try { allGroups = await ApiClient.GetGroupsAsync() ?? new List<Group>(); } catch { allGroups = new List<Group>(); }
                    // Если выбранный курс был удалён — сбрасываем выбор
                    if (selectedCourseId.HasValue && !allCourses.Any(c => c.Id == selectedCourseId.Value))
                        selectedCourseId = null;
                    RefreshCoursesGrid();
                }
                finally
                {
                    isLoading = false;
                    if (loadPending) { loadPending = false; _ = LoadAllData(); }
                }
            }

            void OnCourseChanged()
            {
                var c = FindCourse(GetSelectedCourseId());
                if (c == null)
                {
                    lblCourseTitle.Text = "Выберите курс слева";
                    btnGradingFormula.Enabled = false;
                    selectedTaskId = null;
                    selectedStudentKey = null;
                    gridTasks.Rows.Clear();
                    gridStudents.Rows.Clear();
                    ResetGridSelection(gridTasks);
                    ResetGridSelection(gridStudents);
                    btnCreateTask.Enabled = false;
                    btnEditTask.Enabled = false;
                    btnDeleteTask.Enabled = false;
                    btnCheckTask.Enabled = false;
                    btnViewStudent.Enabled = false;
                    return;
                }
                lblCourseTitle.Text = $"📘 {c.Name}" + (c.Archived == 1 ? " [Архив]" : "");
                btnGradingFormula.Enabled = true;
                RefreshStudGroupFilter();
                RefreshTasksGrid();
                RefreshStudentsGrid();
            }

            gridCourses.SelectionChanged += (s, e) =>
            {
                if (isRefreshingCourses || isLoading) return;
                if (gridCourses.SelectedRows.Count > 0)
                    selectedCourseId = gridCourses.SelectedRows[0].Tag as int?;
                else
                    selectedCourseId = null;
                OnCourseChanged();
            };
            txtSearchCourse.TextChanged += (s, e) => RefreshCoursesGrid();
            txtTaskSearch.TextChanged += (s, e) => RefreshTasksGrid();
            cbTaskType.SelectedIndexChanged += (s, e) => RefreshTasksGrid();
            cbTaskStatus.SelectedIndexChanged += (s, e) => RefreshTasksGrid();
            txtStudSearch.TextChanged += (s, e) => RefreshStudentsGrid();
            cbStudGroup.SelectedIndexChanged += (s, e) => RefreshStudentsGrid();
            gridTasks.SelectionChanged += (s, e) =>
            {
                if (isRefreshingTasks) return;
                if (gridTasks.SelectedRows.Count > 0 && gridTasks.SelectedRows[0].Tag is int tid)
                    selectedTaskId = tid;
                else
                    selectedTaskId = null;
                bool any = gridTasks.SelectedRows.Count > 0;
                btnEditTask.Enabled = btnDeleteTask.Enabled = btnCheckTask.Enabled = any;
            };
            gridStudents.SelectionChanged += (s, e) =>
            {
                if (isRefreshingStudents) return;
                selectedStudentKey = gridStudents.SelectedRows.Count > 0 ? (gridStudents.SelectedRows[0].Tag as string) : null;
                btnViewStudent.Enabled = gridStudents.SelectedRows.Count > 0;
            };

            // Создание задания. После закрытия модального окна редактора:
            //   1) Локально дёргаем DataRefreshBus.Raise — это мгновенно перезагружает
            //      ТЕКУЩЕЕ окно «Мои курсы» и любые другие открытые окна, даже если
            //      SignalR-канал временно недоступен и серверный push не дошёл.
            //   2) В дополнение явно вызываем await LoadAllData() — гарантия для случая,
            //      когда подписчик шины пропустил событие из-за гонки.
            btnCreateTask.Click += async (s, e) =>
            {
                var c = FindCourse(GetSelectedCourseId());
                if (c == null) return;
                // Новое задание по умолчанию создаём в архиве — преподаватель может настроить
                // МТ, проверить, и потом перевести в "Опубликовано".
                var state = new TaskEditorState
                {
                    TaskId = null,
                    Title = "",
                    Type = "Домашняя работа",
                    Deadline = DateTime.Now.AddDays(7),
                    Status = "Архив",
                    ConfigurationJson = "",
                    IsLocked = false,
                    Version = 0
                };
                ShowTaskEditorWindow(c.Id, c.Name, state, form);
                await LoadAllData();
            };


            // Редактирование задания. После закрытия модального редактора обновляем данные.
            // DataRefreshBus.Raise вызывается внутри редактора при успешном сохранении,
            // что гарантирует обновление без гонки isLoading.
            btnEditTask.Click += async (s, e) =>
            {
                if (gridTasks.SelectedRows.Count == 0) return;
                int taskId = (int)gridTasks.SelectedRows[0].Tag;
                var a = allAssignments.FirstOrDefault(x => x.Id == taskId);
                var c = FindCourse(GetSelectedCourseId());
                if (a == null || c == null) return;

                // Нормализация старых статусов в новые (на случай, если в БД остался "Черновик"/"Скрыто").
                string normalizedStatus = a.Status ?? "Архив";
                if (normalizedStatus == "Черновик" || normalizedStatus == "Скрыто") normalizedStatus = "Архив";

                var state = new TaskEditorState
                {
                    TaskId = a.Id,
                    Title = a.Title,
                    Type = a.Type ?? "Домашняя работа",
                    Deadline = a.Deadline,
                    Status = normalizedStatus,
                    ConfigurationJson = a.Description ?? "",
                    IsLocked = a.IsLocked == 1,
                    Version = a.Version
                };
                ShowTaskEditorWindow(c.Id, c.Name, state, form);
                await LoadAllData();
            };


            // Удаление задания. Здесь шина уже поднимается в обработчике, оставляем как было,
            // но добавляем явный LoadAllData в успешной ветке для гарантии локального отклика.
            btnDeleteTask.Click += async (s, e) =>
            {
                if (gridTasks.SelectedRows.Count == 0) return;
                int taskId = (int)gridTasks.SelectedRows[0].Tag;
                if (ShowConfirmDialog("Удалить задание?\nВсе сданные работы будут безвозвратно удалены.", "Подтверждение"))
                {
                    if (await ApiClient.DeleteAssignmentAsync(taskId))
                    {
                        DataRefreshBus.Raise("Assignment", "Deleted", taskId);
                        await LoadAllData();
                    }
                    else
                    {
                        await LoadAllData();
                    }
                }
            };

            // "Проверить работы" — после возврата из дочернего окна гарантируем обновление.
            btnCheckTask.Click += async (s, e) =>
            {
                if (gridTasks.SelectedRows.Count == 0) return;
                int taskId = (int)gridTasks.SelectedRows[0].Tag;
                await ShowSubmissionsCheckWindow(taskId, form);
                DataRefreshBus.Raise("Submission", "Updated", taskId);
                await LoadAllData();
            };



            btnRefreshTasks.Click += async (s, e) => await LoadAllData();
            btnRefreshStudents.Click += async (s, e) => await LoadAllData();

            btnViewStudent.Click += (s, e) =>
            {
                if (gridStudents.SelectedRows.Count == 0) return;
                string studentName = gridStudents.SelectedRows[0].Cells[0].Value?.ToString();
                var c = FindCourse(GetSelectedCourseId());
                if (c == null || string.IsNullOrEmpty(studentName)) return;
                ShowStudentGradesWindow(studentName, c.Name, form);
            };

            // Кнопка "Формула оценки" в левой панели — основной (и теперь единственный)
            // способ открыть редактор формулы курса из этого окна.
            btnGradingFormula.Click += async (s, e) =>
            {
                var c = FindCourse(GetSelectedCourseId());
                if (c == null) { ShowWarningDialog("Выберите курс в списке слева."); return; }
                ShowGradingPolicyWindow(c.Id, c.Name, c.GradingPolicy);
                await LoadAllData();
            };
            btnRefreshCourses.Click += async (s, e) => await LoadAllData();

            form.Shown += (s, e) =>
            {
                try
                {
                    int width = split.Width;
                    int desired = DEFAULT_LEFT;
                    int safeMax = Math.Max(25, width - RIGHT_MIN - split.SplitterWidth - 1);
                    int safeMin = Math.Min(LEFT_MIN, safeMax);
                    split.SplitterDistance = Math.Max(safeMin, Math.Min(desired, safeMax));
                    split.Panel1MinSize = LEFT_MIN;
                    split.Panel2MinSize = RIGHT_MIN;
                }
                catch { }
            };
            // Автоматическое обновление окна «Мои курсы» (преподаватель/администратор)
            // при любых изменениях в системе. Подписываемся на глобальную шину DataRefreshBus
            // и при срабатывании события на любую сущность, влияющую на содержимое
            // этого окна (курсы, задания, работы, студенты, группы, формула оценки),
            // вызываем тот же метод, что и кнопка «🔄 Обновить».
            //
            // Это закрывает требование: после действий «Создать задание», «Изменить»,
            // «Удалить задание», «Проверить работы» и т.п. таблицы должны обновиться
            // сами, без нажатия пользователем кнопки «Обновить».
            Action<string, string, int> onBusChanged = (entity, action, id) =>
            {
                if (form.IsDisposed) return;
                // Фильтр: реагируем только на сущности, которые могут отображаться в этом окне.
                bool relevant = entity == "Course" || entity == "Assignment" || entity == "Submission"
                                || entity == "GradingPolicy" || entity == "CourseGroups"
                                || entity == "Group" || entity == "Student" || entity == "Teacher";
                if (!relevant) return;
                try
                {
                    if (form.InvokeRequired)
                        form.BeginInvoke(new Action(async () => { try { await LoadAllData(); } catch { } }));
                    else
                        form.BeginInvoke(new Action(async () => { try { await LoadAllData(); } catch { } }));
                }
                catch { }
            };
            DataRefreshBus.Changed += onBusChanged;

            form.Load += async (s, e) => { form.MinimumSize = form.Size; await LoadAllData(); };
            form.VisibleChanged += (s, e) => { if (!this.IsDisposed && !form.IsDisposed) this.Enabled = !form.Visible; };
            form.FormClosed += (s, e) =>
            {
                DataRefreshBus.Changed -= onBusChanged;
                if (!this.IsDisposed) this.Enabled = true;
            };
            form.Show(this);
        }





        // ====================================================================
        // НОВОЕ ОКНО: единое окно студента "Мои курсы"
        // Стиль, координаты, логика масштабирования и оформление панелей
        // полностью соответствуют ShowTeacherUnifiedWindow.
        // Различие — только в наборе вкладок (у студента 2: Задания и
        // Моя успеваемость) и в отсутствии кнопок управления курсами слева
        // (студент не имеет прав на их создание/изменение/удаление).
        // ====================================================================
        // ====================================================================
        // НОВОЕ ОКНО: единое окно студента "Мои курсы"
        // Слева — список курсов, справа — единое представление без вкладок:
        //   • Заголовок курса
        //   • Сводная статистика (Всего заданий, Сдано, Сформированная оценка, Итог)
        //   • Поисковая строка и фильтры (Тип, Статус, Оценка)
        //   • Единая таблица с заданиями и оценками
        //   • Кнопки внизу: "Просмотр формулы", "Открыть задание", "Обновить"
        // ====================================================================
        private async void ShowStudentUnifiedWindow()
        {
            const int LEFT_MIN = 380;
            const int RIGHT_MIN = 560;
            const int DEFAULT_LEFT = 380;

            Form form = new Form
            {
                Text = "Мои курсы",
                ClientSize = new Size(1200, 700),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.White,
                MinimumSize = new Size(LEFT_MIN + RIGHT_MIN + 40, 700)
            };

            SplitContainer split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                SplitterWidth = 5,
                BackColor = Color.LightGray
            };
            split.Panel1.Padding = new Padding(10);
            split.Panel2.Padding = new Padding(10);
            split.Panel1.BackColor = Color.White;
            split.Panel2.BackColor = Color.White;

            GroupBox gbCourses = new GroupBox { Text = "Курсы", Dock = DockStyle.Fill, Padding = new Padding(8), Font = new Font("Segoe UI", 10, FontStyle.Bold), BackColor = Color.White };
            Panel pnlCourseSearch = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Color.White, Padding = new Padding(0, 0, 0, 10) };
            TextBox txtSearch = new TextBox { Dock = DockStyle.Top, Font = new Font("Segoe UI", 9) };
            pnlCourseSearch.Controls.Add(txtSearch);

            DataGridView gridCourses = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Font = new Font("Segoe UI", 9),
                BorderStyle = BorderStyle.FixedSingle
            };
            gridCourses.Columns.Add("Name", "Курс");
            ApplyModernTableStyle(gridCourses);

            gbCourses.Controls.Add(gridCourses);
            gbCourses.Controls.Add(pnlCourseSearch);
            split.Panel1.Controls.Add(gbCourses);

            Label lblCourseTitle = new Label
            {
                Text = "Выберите курс слева",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 32
            };

            Panel pnlContent = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(0, 5, 0, 0)
            };

            Panel pnlStats = new Panel { Dock = DockStyle.Top, Height = 110, BackColor = Color.White, Padding = new Padding(5, 5, 5, 5) };
            Label lblTotal = new Label { Text = "Всего заданий: —", Location = new Point(5, 5), AutoSize = true, Font = new Font("Segoe UI", 10) };
            Label lblDone = new Label { Text = "Сдано: —", Location = new Point(5, 30), AutoSize = true, Font = new Font("Segoe UI", 10) };
            Label lblAvg = new Label { Text = "Сформированная оценка: —", Location = new Point(5, 55), AutoSize = true, Font = new Font("Segoe UI", 10) };
            Label lblFinal = new Label { Text = "Итог: —", Location = new Point(5, 80), AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = Color.ForestGreen };
            pnlStats.Controls.AddRange(new Control[] { lblTotal, lblDone, lblAvg, lblFinal });

            Panel pnlFilters = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.White, Padding = new Padding(5, 5, 5, 5) };
            Label lblSearch = new Label { Text = "Поиск:", Location = new Point(5, 5), AutoSize = true };
            TextBox txtTaskSearch = new TextBox { Location = new Point(5, 25), Width = 200, Font = new Font("Segoe UI", 9) };

            Label lblType = new Label { Text = "Тип:", Location = new Point(220, 5), AutoSize = true };
            ComboBox cbType = new ComboBox { Location = new Point(220, 25), Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9) };
            cbType.Items.AddRange(new[] { "Все", "Домашняя работа", "Лабораторная работа", "Самостоятельная работа", "Контрольная работа", "Экзамен" });
            cbType.SelectedIndex = 0;

            Label lblStatus = new Label { Text = "Статус:", Location = new Point(395, 5), AutoSize = true };
            ComboBox cbStatus = new ComboBox { Location = new Point(395, 25), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9) };
            // НОВЫЕ статусы для студента: "Не сдано", "Не оценено", "Оценено", "Срок вышел"
            cbStatus.Items.AddRange(new[] { "Все", "Не сдано", "Не оценено", "Оценено", "Срок вышел" });
            cbStatus.SelectedIndex = 0;

            Label lblGrade = new Label { Text = "Оценка:", Location = new Point(550, 5), AutoSize = true };
            ComboBox cbGrade = new ComboBox { Location = new Point(550, 25), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9) };
            cbGrade.Items.AddRange(new[] { "Все", "С оценкой", "Без оценки" });
            cbGrade.SelectedIndex = 0;

            pnlFilters.Controls.AddRange(new Control[] { lblSearch, txtTaskSearch, lblType, cbType, lblStatus, cbStatus, lblGrade, cbGrade });

            DataGridView gridTasks = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Font = new Font("Segoe UI", 9),
                BorderStyle = BorderStyle.FixedSingle
            };
            gridTasks.Columns.Add("Title", "Название"); gridTasks.Columns[0].FillWeight = 35;
            gridTasks.Columns.Add("Type", "Тип"); gridTasks.Columns[1].FillWeight = 22;
            gridTasks.Columns.Add("Deadline", "Срок"); gridTasks.Columns[2].FillWeight = 18;
            gridTasks.Columns.Add("Status", "Статус"); gridTasks.Columns[3].FillWeight = 15;
            gridTasks.Columns.Add("Grade", "Оценка"); gridTasks.Columns[4].FillWeight = 10;
            ApplyModernTableStyle(gridTasks);

            FlowLayoutPanel pnlBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(5, 12, 5, 5), BackColor = Color.White };
            Button btnViewFormula = new Button { Text = "📊 Просмотр формулы", Margin = new Padding(0, 0, 5, 0), Size = new Size(180, 28), FlatStyle = FlatStyle.Standard, Enabled = false };
            Button btnOpenTask = new Button { Text = "Открыть задание", Margin = new Padding(0, 0, 5, 0), Size = new Size(150, 28), FlatStyle = FlatStyle.Standard, Enabled = false };
            Button btnRefresh = new Button { Text = "🔄 Обновить", Margin = new Padding(0, 0, 5, 0), Size = new Size(110, 28), FlatStyle = FlatStyle.Standard };
            pnlBtns.Controls.AddRange(new Control[] { btnViewFormula, btnOpenTask, btnRefresh });

            pnlContent.Controls.Add(gridTasks);
            pnlContent.Controls.Add(pnlBtns);
            pnlContent.Controls.Add(pnlFilters);
            pnlContent.Controls.Add(pnlStats);

            split.Panel2.Controls.Add(pnlContent);
            split.Panel2.Controls.Add(lblCourseTitle);
            form.Controls.Add(split);

            List<Course> myCourses = new List<Course>();
            List<Assignment> myAssignments = new List<Assignment>();
            List<StudentTaskRecord> myPerf = new List<StudentTaskRecord>();

            // Флаги подавления SelectionChanged во время программной перестройки гридов.
            // Поиск/фильтр НЕ сбрасывает выбор: если элемент остаётся в выборке — он
            // остаётся выделенным; если уходит — выделение снимается. Авто-выбор
            // первой строки при открытии или при наборе в поиске запрещён.
            bool isRefreshingCourses = false;
            bool isRefreshingTasks = false;
            int? selectedCourseId = null;
            int? selectedTaskId = null;

            // НОВАЯ функция определения статуса работы студента.
            string GetStudentStatus(Assignment a)
            {
                if (a.SubmissionStatus == "Оценено") return "Оценено";
                if (a.SubmissionStatus == "Не оценено") return "Не оценено";
                // "Не сдано" или null
                if (DateTime.Now >= a.Deadline) return "Срок вышел";
                return "Не сдано";
            }

            int? GetSelectedCourseId() => selectedCourseId;
            Course FindCourse(int? cid) => cid.HasValue ? myCourses.FirstOrDefault(x => x.Id == cid.Value) : null;
            string GetCourseName() { var c = FindCourse(GetSelectedCourseId()); return c?.Name ?? ""; }

            // Программно снимает выделение и убирает синюю рамку фокуса.
            void ResetGridSelection(DataGridView g)
            {
                if (g == null) return;
                g.ClearSelection();
                g.BeginInvoke(new Action(() =>
                {
                    try { g.CurrentCell = null; } catch { }
                    g.ClearSelection();
                }));
            }

            // Выставляет выделение на конкретную строку.
            void SelectGridRow(DataGridView g, int rowIdx)
            {
                g.ClearSelection();
                g.Rows[rowIdx].Selected = true;
                try { g.CurrentCell = g.Rows[rowIdx].Cells[0]; } catch { }
            }

            // Перестраивает список курсов с учётом поиска.
            // Восстанавливает ранее выбранный курс, если он попадает в результаты.
            // Если нет — снимает выделение (не авто-выделяет первую строку).
            void RefreshCoursesGrid()
            {
                isRefreshingCourses = true;
                try
                {
                    gridCourses.Rows.Clear();
                    string s = txtSearch.Text.Trim().ToLower();
                    int? rowToSelect = null;
                    foreach (var c in myCourses.Where(x => string.IsNullOrEmpty(s) || x.Name.ToLower().Contains(s)))
                    {
                        int idx = gridCourses.Rows.Add(c.Name);
                        gridCourses.Rows[idx].Tag = c.Id;
                        if (selectedCourseId.HasValue && c.Id == selectedCourseId.Value)
                            rowToSelect = idx;
                    }
                    if (rowToSelect.HasValue)
                        SelectGridRow(gridCourses, rowToSelect.Value);
                    else
                    {
                        selectedCourseId = null;
                        ResetGridSelection(gridCourses);
                    }
                }
                finally
                {
                    isRefreshingCourses = false;
                }
                OnCourseChanged();
            }

            void RefreshTasksAndStats()
            {
                isRefreshingTasks = true;
                try
                {
                    gridTasks.Rows.Clear();
                    string courseName = GetCourseName();
                    if (string.IsNullOrEmpty(courseName))
                    {
                        lblTotal.Text = "Всего заданий: —";
                        lblDone.Text = "Сдано: —";
                        lblAvg.Text = "Сформированная оценка: —";
                        lblFinal.Text = "Итог: —";
                        btnViewFormula.Enabled = false;
                        btnOpenTask.Enabled = false;
                        ResetGridSelection(gridTasks);
                        return;
                    }
                    btnViewFormula.Enabled = true;

                    var perfRows = (myPerf ?? new List<StudentTaskRecord>()).Where(p => p != null && p.Course == courseName).ToList();
                    int total = perfRows.Count;
                    int done = perfRows.Count(r => r.Status == "Оценено");
                    var graded = perfRows.Where(r => r.Grade.HasValue).ToList();
                    double avg = graded.Count > 0 ? graded.Average(r => r.Grade.Value) : 0;
                    string policy = perfRows.FirstOrDefault()?.GradingPolicy ?? "";
                    double final = 0;
                    try { final = CalculateFinalGrade(perfRows.Where(x => !string.IsNullOrEmpty(x.Type)).Select(x => (x.Type, x.Grade)), policy); } catch { final = 0; }

                    lblTotal.Text = $"Всего заданий: {total}";
                    lblDone.Text = $"Сдано (оценено): {done}";
                    lblAvg.Text = $"Сформированная оценка: {avg:0.0}";
                    lblFinal.Text = $"Итог: {final:0.0}";

                    if (myAssignments == null) { ResetGridSelection(gridTasks); return; }
                    string search = txtTaskSearch.Text.Trim().ToLower();
                    string filterType = cbType.SelectedItem?.ToString() ?? "Все";
                    string filterStatus = cbStatus.SelectedItem?.ToString() ?? "Все";
                    string filterGrade = cbGrade.SelectedItem?.ToString() ?? "Все";

                    int? rowToSelect = null;
                    foreach (var a in myAssignments
                                .Where(x => x != null && x.CourseName == courseName)
                                .OrderBy(x => x.Deadline))
                    {
                        if (!string.IsNullOrEmpty(search) && !(a.Title ?? "").ToLower().Contains(search)) continue;
                        if (filterType != "Все" && (a.Type ?? "") != filterType) continue;

                        string st = GetStudentStatus(a);
                        if (filterStatus != "Все" && st != filterStatus) continue;

                        var perfMatch = perfRows.FirstOrDefault(p => p.Task == a.Title && p.Type == a.Type);
                        int? gradeValue = perfMatch?.Grade;

                        if (filterGrade == "С оценкой" && !gradeValue.HasValue) continue;
                        if (filterGrade == "Без оценки" && gradeValue.HasValue) continue;

                        int idx = gridTasks.Rows.Add(
                            a.Title ?? "(без названия)",
                            a.Type ?? "Домашняя работа",
                            a.Deadline.ToString("dd.MM.yyyy HH:mm"),
                            st,
                            gradeValue?.ToString() ?? "—");
                        gridTasks.Rows[idx].Tag = a.Id;
                        if (selectedTaskId.HasValue && a.Id == selectedTaskId.Value)
                            rowToSelect = idx;
                    }
                    if (rowToSelect.HasValue)
                        SelectGridRow(gridTasks, rowToSelect.Value);
                    else
                    {
                        selectedTaskId = null;
                        ResetGridSelection(gridTasks);
                    }
                }
                catch { }
                finally
                {
                    isRefreshingTasks = false;
                    btnOpenTask.Enabled = gridTasks.SelectedRows.Count > 0;
                }
            }

            void OnCourseChanged()
            {
                var c = FindCourse(GetSelectedCourseId());
                if (c == null)
                {
                    lblCourseTitle.Text = "Выберите курс слева";
                    selectedTaskId = null;
                    gridTasks.Rows.Clear();
                    ResetGridSelection(gridTasks);
                    btnOpenTask.Enabled = false;
                    btnViewFormula.Enabled = false;
                    lblTotal.Text = "Всего заданий: —";
                    lblDone.Text = "Сдано: —";
                    lblAvg.Text = "Сформированная оценка: —";
                    lblFinal.Text = "Итог: —";
                    return;
                }
                lblCourseTitle.Text = $"📘 {c.Name}";
                RefreshTasksAndStats();
            }

            bool isLoading = false;
            bool loadPending = false;
            async Task LoadAllData()
            {
                if (isLoading) { loadPending = true; return; }
                isLoading = true;
                try
                {
                    try { myCourses = await ApiClient.GetMyCoursesAsync() ?? new List<Course>(); } catch { myCourses = new List<Course>(); }
                    try { myAssignments = await ApiClient.GetAssignmentsAsync() ?? new List<Assignment>(); } catch { myAssignments = new List<Assignment>(); }
                    try { myPerf = await ApiClient.GetMyPerformanceAsync() ?? new List<StudentTaskRecord>(); } catch { myPerf = new List<StudentTaskRecord>(); }

                    // Если выбранный курс был удалён — сбрасываем выбор
                    if (selectedCourseId.HasValue && !myCourses.Any(c => c.Id == selectedCourseId.Value))
                        selectedCourseId = null;
                    RefreshCoursesGrid();
                }
                finally
                {
                    isLoading = false;
                    if (loadPending) { loadPending = false; _ = LoadAllData(); }
                }
            }

            gridCourses.SelectionChanged += (s, e) =>
            {
                if (isRefreshingCourses || isLoading) return;
                if (gridCourses.SelectedRows.Count > 0)
                    selectedCourseId = gridCourses.SelectedRows[0].Tag as int?;
                else
                    selectedCourseId = null;
                OnCourseChanged();
            };
            txtSearch.TextChanged += (s, e) => RefreshCoursesGrid();
            txtTaskSearch.TextChanged += (s, e) => RefreshTasksAndStats();
            cbType.SelectedIndexChanged += (s, e) => RefreshTasksAndStats();
            cbStatus.SelectedIndexChanged += (s, e) => RefreshTasksAndStats();
            cbGrade.SelectedIndexChanged += (s, e) => RefreshTasksAndStats();

            gridTasks.SelectionChanged += (s, e) =>
            {
                if (isRefreshingTasks) return;
                if (gridTasks.SelectedRows.Count > 0 && gridTasks.SelectedRows[0].Tag is int tid)
                    selectedTaskId = tid;
                else
                    selectedTaskId = null;
                btnOpenTask.Enabled = gridTasks.SelectedRows.Count > 0;
            };

            Action openTask = async () =>
            {
                if (gridTasks.SelectedRows.Count == 0) return;
                if (gridTasks.SelectedRows[0].Tag == null) return;
                int taskId = (int)gridTasks.SelectedRows[0].Tag;
                var a = myAssignments.FirstOrDefault(x => x.Id == taskId);
                if (a == null)
                {
                    ShowWarningDialog("Данные были изменены, обновите окно.");
                    await LoadAllData();
                    return;
                }
                var freshState = await ApiClient.GetAssignmentStateAsync(a.Id);
                if (freshState == null || freshState.IsDeleted)
                {
                    ShowWarningDialog($"Задание «{a.Title}» было удалено преподавателем. Данные были изменены, обновите окно.");
                    await LoadAllData();
                    return;
                }
                if (freshState.Status != "Опубликовано" || freshState.CourseArchived == 1)
                {
                    ShowWarningDialog("Данные были изменены, обновите окно.");
                    await LoadAllData();
                    return;
                }
                ShowAssignmentWindow(a.Id, a.CourseName, a.Title, a.Type ?? "Домашняя работа",
                                     a.Deadline, a.Status, "—", a.Description, form);
            };
            btnOpenTask.Click += (s, e) => openTask();
            gridTasks.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) openTask(); };

            btnRefresh.Click += async (s, e) => await LoadAllData();

            btnViewFormula.Click += (s, e) =>
            {
                var c = FindCourse(GetSelectedCourseId());
                if (c == null) return;
                ShowGradingPolicyViewerWindow(c.Id, c.Name, c.GradingPolicy);
            };

            form.Shown += (s, e) =>
            {
                try
                {
                    int width = split.Width;
                    int desired = DEFAULT_LEFT;
                    int safeMax = Math.Max(25, width - RIGHT_MIN - split.SplitterWidth - 1);
                    int safeMin = Math.Min(LEFT_MIN, safeMax);
                    split.SplitterDistance = Math.Max(safeMin, Math.Min(desired, safeMax));
                    split.Panel1MinSize = LEFT_MIN;
                    split.Panel2MinSize = RIGHT_MIN;
                }
                catch { }
            };

            Action<string, string, int> onBusChanged = (entity, action, id) =>
            {
                if (form.IsDisposed) return;
                bool relevant = entity == "Course" || entity == "Assignment" || entity == "Submission"
                                || entity == "GradingPolicy" || entity == "CourseGroups"
                                || entity == "Group" || entity == "Student";
                if (!relevant) return;
                try
                {
                    form.BeginInvoke(new Action(async () => { try { await LoadAllData(); } catch { } }));
                }
                catch { }
            };
            DataRefreshBus.Changed += onBusChanged;

            form.Load += async (s, e) => await LoadAllData();
            form.VisibleChanged += (s, e) => { if (!this.IsDisposed && !form.IsDisposed) this.Enabled = !form.Visible; };
            form.FormClosed += (s, e) =>
            {
                DataRefreshBus.Changed -= onBusChanged;
                if (!this.IsDisposed) this.Enabled = true;
            };
            form.Show(this);
        }




        // Вспомогательный класс для возврата статуса сессии пользователя

        // Проверяет, есть ли у пользователя активная сессия в системе.
        // Используется перед редактированием/удалением, чтобы предупредить администратора.
        // Проверяет, есть ли у пользователя активная сессия в системе.
        // Используется перед редактированием/удалением, чтобы предупредить администратора.
        private async Task<UserSessionStatusInfo> CheckUserOnlineStatus(int userId)
        {
            try
            {
                var url = System.Configuration.ConfigurationManager.AppSettings["ApiBaseUrl"] ?? "http://localhost:5007/api/";
                using (var http = new HttpClient())
                {
                    http.DefaultRequestHeaders.Add("X-Session-Id", ApiClient.SessionId ?? "");
                    var resp = await http.GetAsync(url + $"Admin/user-session-status/{userId}");
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadAsStringAsync();

                        // Бэкенд (System.Text.Json) сериализует поля в camelCase: isOnline, lastActivityAt, ipAddress.
                        // Парсим через JObject с case-insensitive поиском, чтобы не зависеть от регистра.
                        var jo = Newtonsoft.Json.Linq.JObject.Parse(json);

                        bool isOnline = false;
                        var tokOnline = jo.GetValue("IsOnline", StringComparison.OrdinalIgnoreCase);
                        if (tokOnline != null) isOnline = tokOnline.Value<bool>();

                        DateTime lastActivity = DateTime.MinValue;
                        var tokLast = jo.GetValue("LastActivityAt", StringComparison.OrdinalIgnoreCase);
                        if (tokLast != null && tokLast.Type != Newtonsoft.Json.Linq.JTokenType.Null)
                        {
                            try { lastActivity = tokLast.Value<DateTime>(); } catch { lastActivity = DateTime.MinValue; }
                        }

                        string ip = "неизвестно";
                        var tokIp = jo.GetValue("IpAddress", StringComparison.OrdinalIgnoreCase);
                        if (tokIp != null && tokIp.Type != Newtonsoft.Json.Linq.JTokenType.Null)
                        {
                            string s = tokIp.Value<string>();
                            if (!string.IsNullOrEmpty(s)) ip = s;
                        }

                        return new UserSessionStatusInfo
                        {
                            IsOnline = isOnline,
                            LastActivity = lastActivity,
                            IpAddress = ip
                        };
                    }
                }
            }
            catch { }
            return new UserSessionStatusInfo { IsOnline = false };
        }
    }
}
