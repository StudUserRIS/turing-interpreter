using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace Интерпретатор_машины_Тьюринга
{
    public partial class Form1
    {
        // ──────────────────────────────────────────────────────────
        // Поля режима проверки
        // ──────────────────────────────────────────────────────────
        private int currentSubmissionId;
        private int currentSubmissionVersion;
        private int currentAssignmentId;
        private Panel checkingModePanel;
        private bool wasCommentVisible = true;
        private Form manageCoursesFormRef = null;
        private Form gradesDetailFormRef = null;
        private Action<string, string, int> _checkingModeBusHandler = null;

        // ──────────────────────────────────────────────────────────
        // Активация / выход из режима проверки
        // ──────────────────────────────────────────────────────────
        public async void ActivateCheckingMode(int submissionId, string studentName, string taskName, int maxScore, string solutionJson, Form manageForm = null, int submissionVersion = 0, Form gradesForm = null)
        {
            BackupMainInterpreterState();
            currentSubmissionId = submissionId;
            currentSubmissionVersion = submissionVersion;
            manageCoursesFormRef = manageForm;
            gradesDetailFormRef = gradesForm;

            var subs = await ApiClient.GetSubmissionsAsync(currentAssignmentId);
            var thisSub = subs?.FirstOrDefault(x => x.Id == submissionId);
            string subStatus = thisSub?.Status ?? "Не оценено";

            var assignmentState = await ApiClient.GetAssignmentStateAsync(currentAssignmentId);
            DateTime deadline = assignmentState?.Deadline ?? DateTime.MaxValue;
            bool deadlinePassed = DateTime.Now >= deadline;

            bool canGrade = (subStatus == "Оценено") || deadlinePassed;

            if (canGrade)
            {
                await ApiClient.StartCheckAsync(submissionId);
            }

            LockInterpreterForTeacherChecking();

            if (manageCoursesFormRef != null) manageCoursesFormRef.Hide();
            if (gradesDetailFormRef != null) gradesDetailFormRef.Hide();

            if (btnUserMenu != null) btnUserMenu.Visible = false;
            if (btnExecution != null) btnExecution.Enabled = true;

            string[] blockedButtonTags = { "New", "Load", "Reset" };
            foreach (var btn in controlPanel.Controls.OfType<Button>())
            {
                if (btn.Tag != null && blockedButtonTags.Contains(btn.Tag.ToString()))
                    btn.Enabled = false;
            }

            int gradingWidth = 280;
            int commentWidth = 240;

            if (tapeControl != null) { tapeControl.Anchor &= ~AnchorStyles.Right; tapeControl.Width = this.ClientSize.Width - gradingWidth; tapeControl.Anchor |= AnchorStyles.Right; }
            if (controlPanel != null) { controlPanel.Anchor &= ~AnchorStyles.Right; controlPanel.Width = this.ClientSize.Width - gradingWidth; controlPanel.Anchor |= AnchorStyles.Right; }
            if (UslovieZadachi != null) { UslovieZadachi.Anchor &= ~AnchorStyles.Right; UslovieZadachi.Width = this.ClientSize.Width - gradingWidth; UslovieZadachi.Anchor |= AnchorStyles.Right; }

            if (mainPanel != null)
            {
                mainPanel.Anchor &= ~AnchorStyles.Right;
                mainPanel.Width = this.ClientSize.Width - gradingWidth - commentWidth;
                mainPanel.Anchor |= AnchorStyles.Right;
            }

            if (commentPanel != null)
            {
                wasCommentVisible = commentPanel.Visible;
                commentPanel.Visible = true;
                commentPanel.Anchor &= ~AnchorStyles.Right;
                commentPanel.Left = mainPanel.Right;
                commentPanel.Width = commentWidth;
                commentPanel.Top = mainPanel.Top;
                commentPanel.Height = mainPanel.Height;
                commentPanel.Anchor |= AnchorStyles.Right;
                commentPanel.BringToFront();
            }

            checkingModePanel = new Panel
            {
                Width = gradingWidth,
                Height = this.ClientSize.Height - controlPanel.Top - 22,
                Location = new Point(this.ClientSize.Width - gradingWidth, controlPanel.Top),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.WhiteSmoke,
                Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom
            };

            Label lblTitle = new Label { Text = canGrade ? "Режим проверки" : "Режим просмотра", Font = new Font("Segoe UI", 12, FontStyle.Bold), Location = new Point(10, 10), AutoSize = true };
            Label lblStudent = new Label { Text = "Студент:\n" + studentName, Location = new Point(10, 40), AutoSize = true, MaximumSize = new Size(gradingWidth - 20, 0) };
            Label lblTask = new Label { Text = "Задание:\n" + taskName, Location = new Point(10, 80), AutoSize = true, MaximumSize = new Size(gradingWidth - 20, 0) };

            Label lblDeadlineHint = null;
            if (!canGrade)
            {
                lblDeadlineHint = new Label
                {
                    Text = $"⚠ Срок сдачи: {deadline:dd.MM.yyyy HH:mm}\nОценивание разрешено только после дедлайна.\nСейчас доступен только просмотр.",
                    Location = new Point(10, 120),
                    Size = new Size(gradingWidth - 20, 50),
                    Font = new Font("Segoe UI", 8, FontStyle.Bold),
                    ForeColor = Color.DarkOrange
                };
            }

            Label lblScore = new Label { Text = $"Оценка (max {maxScore}):", Location = new Point(10, 175), AutoSize = true };
            NumericUpDown numScore = new NumericUpDown
            {
                Location = new Point(10, 195),
                Width = 100,
                Minimum = 0,
                Maximum = maxScore,
                Value = maxScore,
                Enabled = canGrade
            };

            Label lblComment = new Label { Text = "Комментарий преподавателя:", Location = new Point(10, 230), AutoSize = true };

            int panelHeight = checkingModePanel.Height;
            int bottomMargin = 15;
            int btnHeight = 30;
            int gap = 8;

            int cancelBtnY = panelHeight - btnHeight - bottomMargin;
            int commentBtnY = cancelBtnY - gap - btnHeight;
            int gradeBtnY = commentBtnY - gap - btnHeight;

            Button btnCancel = new Button { Text = "Отмена", Location = new Point(10, cancelBtnY), Size = new Size(gradingWidth - 20, btnHeight), FlatStyle = FlatStyle.Standard, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, BackColor = SystemColors.Control };
            Button btnSendComment = new Button { Text = "Отправить комментарий", Location = new Point(10, commentBtnY), Size = new Size(gradingWidth - 20, btnHeight), FlatStyle = FlatStyle.Standard, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, BackColor = SystemColors.Control, Enabled = canGrade };
            Button btnGrade = new Button { Text = "Оценить", Location = new Point(10, gradeBtnY), Size = new Size(gradingWidth - 20, btnHeight), FlatStyle = FlatStyle.Standard, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, BackColor = SystemColors.Control, Font = new Font("Segoe UI", 9, FontStyle.Bold), Enabled = canGrade };

            int txtCommentY = 250;
            int txtCommentHeight = Math.Max(50, gradeBtnY - gap - txtCommentY);

            TextBox txtTeacherComment = new TextBox
            {
                Location = new Point(10, txtCommentY),
                Size = new Size(gradingWidth - 20, txtCommentHeight),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ReadOnly = !canGrade
            };

            if (thisSub != null)
            {
                if (!string.IsNullOrEmpty(thisSub.TeacherComment)) txtTeacherComment.Text = thisSub.TeacherComment;
                if (thisSub.Grade.HasValue && thisSub.Grade.Value >= 0 && thisSub.Grade.Value <= maxScore)
                    numScore.Value = thisSub.Grade.Value;
            }

            btnGrade.Click += async (s, e) =>
            {
                if (numScore.Value < 0 || numScore.Value > maxScore)
                {
                    ShowWarningDialog($"Оценка должна быть в диапазоне от 0 до {maxScore}.");
                    return;
                }
                btnGrade.Text = "Сохранение...";
                btnGrade.Enabled = false; btnSendComment.Enabled = false; btnCancel.Enabled = false;
                bool success = await ApiClient.UpdateSubmissionAsync(currentSubmissionId, (int)numScore.Value, txtTeacherComment.Text, "Оценено", currentSubmissionVersion);
                if (success)
                {
                    ShowSuccessDialog("Оценка успешно сохранена!");
                    DataRefreshBus.Raise("Submission", "Updated", currentSubmissionId);
                    ExitCheckingMode();
                }
                else
                {
                    btnGrade.Text = "Оценить";
                    btnGrade.Enabled = true; btnSendComment.Enabled = true; btnCancel.Enabled = true;
                }
            };

            btnSendComment.Click += async (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtTeacherComment.Text))
                {
                    ShowWarningDialog("Введите комментарий перед отправкой.");
                    return;
                }
                btnSendComment.Text = "Отправка...";
                btnGrade.Enabled = false; btnSendComment.Enabled = false; btnCancel.Enabled = false;
                bool success = await ApiClient.SendCommentOnlyAsync(currentSubmissionId, txtTeacherComment.Text, currentSubmissionVersion);
                if (success)
                {
                    ShowSuccessDialog("Комментарий отправлен студенту. Статус работы не изменён.");
                    DataRefreshBus.Raise("Submission", "Updated", currentSubmissionId);
                    ExitCheckingMode();
                }
                else
                {
                    btnSendComment.Text = "Отправить комментарий";
                    btnGrade.Enabled = canGrade; btnSendComment.Enabled = canGrade; btnCancel.Enabled = true;
                }
            };

            btnCancel.Click += (s, e) => ExitCheckingMode();

            var ctrlList = new List<Control> { lblTitle, lblStudent, lblTask, lblScore, numScore, lblComment, txtTeacherComment, btnGrade, btnSendComment, btnCancel };
            if (lblDeadlineHint != null) ctrlList.Add(lblDeadlineHint);
            checkingModePanel.Controls.AddRange(ctrlList.ToArray());
            this.Controls.Add(checkingModePanel);
            checkingModePanel.BringToFront();

            if (!string.IsNullOrEmpty(solutionJson))
            {
                try
                {
                    var data = JsonConvert.DeserializeObject<TuringMachineData>(solutionJson);
                    if (data != null)
                    {
                        if (alphabetTextBox != null) alphabetTextBox.Text = data.Alphabet ?? "01";
                        if (tapeControl != null)
                        {
                            tapeControl.tapeRenderer.AllowedSymbols = alphabetTextBox.Text + "_";
                            tapeControl.tapeRenderer._tape.Clear();
                            tapeControl.tapeRenderer._tape[0] = new TapeCell(' ');
                            if (data.TapeContent != null)
                                foreach (var kvp in data.TapeContent)
                                    tapeControl.tapeRenderer._tape[kvp.Key] = new TapeCell(kvp.Value == '_' ? ' ' : kvp.Value);
                            tapeControl.SetHeadPosition(data.HeadPosition);
                        }
                        states.Clear();
                        if (data.States != null) foreach (var st in data.States) states.Add(st);
                        UpdateStateTableColumns();
                        UpdateStateTable();
                        if (data.TransitionRules != null) RestoreTableData(data.TransitionRules);
                        var pTextBox = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
                        if (pTextBox != null) pTextBox.Text = data.ProblemCondition ?? "";
                    }
                }
                catch { }
            }

            bool guard = false;
            Action<string, string, int> onBusChanged = (entity, action, id) =>
            {
                if (checkingModePanel == null || checkingModePanel.IsDisposed) return;
                bool relevant = (entity == "Submission" && id == submissionId)
                                || entity == "Assignment"
                                || entity == "Course"
                                || entity == "CourseGroups";
                if (!relevant) return;
                if (guard) return;
                guard = true;
                try
                {
                    this.BeginInvoke(new Action(async () =>
                    {
                        try
                        {
                            if (checkingModePanel == null || checkingModePanel.IsDisposed) return;
                            var freshSubs = await ApiClient.GetSubmissionsAsync(currentAssignmentId);
                            var fresh = freshSubs?.FirstOrDefault(x => x.Id == submissionId);
                            if (fresh == null)
                            {
                                ShowWarningDialog("Эта работа больше не существует (возможно, задание было удалено). Режим проверки будет закрыт.");
                                ExitCheckingMode();
                                return;
                            }
                            if (fresh.Version != currentSubmissionVersion)
                            {
                                string detail = fresh.Status switch
                                {
                                    "Оценено" => "Эта работа была оценена другим преподавателем. Режим проверки будет закрыт.",
                                    "Не сдано" => "Студент отозвал свою работу с проверки. Режим проверки будет закрыт.",
                                    _ => "Состояние работы изменилось. Режим проверки будет закрыт."
                                };
                                ShowWarningDialog(detail);
                                ExitCheckingMode();
                            }
                        }
                        catch { }
                        finally { guard = false; }
                    }));
                }
                catch { guard = false; }
            };
            DataRefreshBus.Changed += onBusChanged;
            _checkingModeBusHandler = onBusChanged;
        }

        private void ExitCheckingMode()
        {
            if (_checkingModeBusHandler != null)
            {
                try { DataRefreshBus.Changed -= _checkingModeBusHandler; } catch { }
                _checkingModeBusHandler = null;
            }

            if (checkingModePanel != null)
            {
                try { this.Controls.Remove(checkingModePanel); } catch { }
                try { checkingModePanel.Dispose(); } catch { }
                checkingModePanel = null;
            }

            UnlockInterpreter();

            if (btnUserMenu != null) btnUserMenu.Visible = true;

            string[] blockedButtonTags = { "New", "Load", "Reset" };
            foreach (var btn in controlPanel.Controls.OfType<Button>())
            {
                if (btn.Tag != null && blockedButtonTags.Contains(btn.Tag.ToString()))
                    btn.Enabled = true;
            }

            if (tapeControl != null) { tapeControl.Anchor &= ~AnchorStyles.Right; tapeControl.Width = this.ClientSize.Width; tapeControl.Anchor |= AnchorStyles.Right; }
            if (controlPanel != null) { controlPanel.Anchor &= ~AnchorStyles.Right; controlPanel.Width = this.ClientSize.Width; controlPanel.Anchor |= AnchorStyles.Right; }
            if (UslovieZadachi != null) { UslovieZadachi.Anchor &= ~AnchorStyles.Right; UslovieZadachi.Width = this.ClientSize.Width; UslovieZadachi.Anchor |= AnchorStyles.Right; }

            if (mainPanel != null)
            {
                mainPanel.Anchor &= ~AnchorStyles.Right;
                mainPanel.Width = this.ClientSize.Width - (commentPanel != null && wasCommentVisible ? commentPanel.Width : 0);
                mainPanel.Anchor |= AnchorStyles.Right;
            }
            if (commentPanel != null)
            {
                commentPanel.Visible = wasCommentVisible;
                commentPanel.Left = mainPanel.Right;
                commentPanel.Top = mainPanel.Top;
                commentPanel.Height = mainPanel.Height;
            }

            RestoreMainInterpreterState();

            if (gradesDetailFormRef != null && !gradesDetailFormRef.IsDisposed)
            {
                try { gradesDetailFormRef.Show(); gradesDetailFormRef.BringToFront(); } catch { }
                gradesDetailFormRef = null;
                manageCoursesFormRef = null;
                return;
            }

            var manageRef = manageCoursesFormRef;
            manageCoursesFormRef = null;
            gradesDetailFormRef = null;
            if (manageRef != null && !manageRef.IsDisposed)
            {
                try { manageRef.Show(); manageRef.BringToFront(); } catch { }
            }
        }

        private async Task ReopenSubmissionsCheckWindow()
        {
            if (manageCoursesFormRef != null)
            {
                manageCoursesFormRef.Show();
                manageCoursesFormRef.BringToFront();
            }

            DialogResult res = await ShowSubmissionsCheckWindow(currentAssignmentId, manageCoursesFormRef);

            if (res != DialogResult.OK)
            {
                manageCoursesFormRef = null;
            }
        }

        // ──────────────────────────────────────────────────────────
        // Блокировка / разблокировка интерпретатора
        // ──────────────────────────────────────────────────────────
        private void LockInterpreterForTeacherChecking()
        {
            var problemTextBox = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
            if (problemTextBox != null)
            {
                problemTextBox.ReadOnly = true;
                problemTextBox.BackColor = SystemColors.Window;
            }

            if (commentTextBox != null)
            {
                commentTextBox.ReadOnly = true;
                commentTextBox.BackColor = SystemColors.Window;
            }

            stateTable.ReadOnly = false;
            alphabetTextBox.Enabled = true;
            btnAddBefore.Enabled = true;
            btnAddAfter.Enabled = true;
            btnRemoveState.Enabled = true;
            btnAddLast.Enabled = true;

            if (tapeControl != null) tapeControl.Enabled = true;
        }

        private void LockInterpreterForChecking()
        {
            stateTable.ReadOnly = true;
            alphabetTextBox.Enabled = false;
            btnAddBefore.Enabled = false;
            btnAddAfter.Enabled = false;
            btnRemoveState.Enabled = false;
            btnAddLast.Enabled = false;
            if (commentTextBox != null) commentTextBox.ReadOnly = true;
        }

        private void UnlockInterpreter()
        {
            stateTable.ReadOnly = false;
            alphabetTextBox.Enabled = true;
            btnAddBefore.Enabled = true;
            btnAddAfter.Enabled = true;
            btnRemoveState.Enabled = true;
            btnAddLast.Enabled = true;

            if (commentTextBox != null)
                commentTextBox.ReadOnly = false;

            var problemTextBox = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
            if (problemTextBox != null)
                problemTextBox.ReadOnly = false;
        }

        // ──────────────────────────────────────────────────────────
        // Загрузка решения студента / очистка интерпретатора
        // ──────────────────────────────────────────────────────────
        private void LoadStudentSolution(string json)
        {
            try
            {
                var data = JsonConvert.DeserializeObject<TuringMachineData>(json);
                if (data == null) return;

                alphabetTextBox.Text = data.Alphabet ?? "01";
                tapeControl.tapeRenderer.AllowedSymbols = alphabetTextBox.Text + "_";

                if (data.TapeContent != null)
                {
                    tapeControl.tapeRenderer._tape.Clear();
                    foreach (var cell in data.TapeContent)
                        tapeControl.tapeRenderer._tape[cell.Key] = new TapeCell(cell.Value == '_' ? ' ' : cell.Value);
                    tapeControl.SetHeadPosition(data.HeadPosition);
                }

                states.Clear();
                if (data.States != null)
                {
                    foreach (var state in data.States) states.Add(state);
                }

                UpdateStateTableColumns();
                UpdateStateTable();

                if (data.TransitionRules != null)
                {
                    foreach (var stateRule in data.TransitionRules)
                    {
                        foreach (DataGridViewRow row in stateTable.Rows)
                        {
                            if (row.Cells["State"].Value?.ToString() == stateRule.Key)
                            {
                                foreach (var rule in stateRule.Value)
                                {
                                    if (stateTable.Columns.Contains(rule.Key))
                                        row.Cells[rule.Key].Value = rule.Value;
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки решения: {ex.Message}");
            }
        }
    }
}
