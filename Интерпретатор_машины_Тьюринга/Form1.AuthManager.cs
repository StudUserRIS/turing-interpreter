using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Интерпретатор_машины_Тьюринга.Api;
using Интерпретатор_машины_Тьюринга.Auth;
using Интерпретатор_машины_Тьюринга.Core;

namespace Интерпретатор_машины_Тьюринга
{
    /// <summary>
    /// UI-слой аутентификации.
    ///
    /// После рефакторинга этот partial отвечает ИСКЛЮЧИТЕЛЬНО за интерфейс:
    ///   • показывает форму входа/выхода/смены пароля/сброса пароля;
    ///   • отображает меню пользователя и переключает доступные пункты;
    ///   • рисует диалоги при завершении сессии и реагирует на push-уведомления.
    ///
    /// Бизнес-логика (heartbeat, поддержание SignalR-сессии, обработка push-сообщений,
    /// аварийный logout) вынесена в <see cref="SessionManager"/>. Здесь остаётся
    /// только тонкая «прослойка», подписывающаяся на доменные события менеджера.
    /// Это устраняет смешение ответственностей внутри одного partial-класса,
    /// на которое указывал разбор архитектуры.
    /// </summary>
    public partial class Form1
    {
        // ──────────────────────────────────────────────────────────
        // Поля UI-аутентификации
        // ──────────────────────────────────────────────────────────
        private enum UserRole { Guest, Student, Teacher, Admin }
        private UserRole currentUserRole = UserRole.Guest;
        private Button btnUserMenu;
        private ContextMenuStrip userContextMenu;
        private ToolStripStatusLabel connectionStatusLabel;
        private ToolStripStatusLabel syncStatusLabel;
        private ToolStripStatusLabel executionStatusLabel;

        // Защита от каскадных окон завершения сессии
        private static bool     _sessionEndInProgress = false;
        private static DateTime _sessionEndStartedAt  = DateTime.MinValue;
        private static readonly object _sessionEndSync = new object();

        // ──────────────────────────────────────────────────────────
        // Совместимость со старым кодом окон (CourseWindows и др.).
        // Эти свойства проксируют доступ к «последним известным» атрибутам
        // профиля внутри SessionManager — старый код может продолжать
        // обращаться к ним напрямую как к полям формы.
        // ──────────────────────────────────────────────────────────
        private string lastKnownLogin
        {
            get => _session?.LastKnownLogin;
            set { /* устанавливается только из SessionManager */ }
        }
        private string lastKnownFullName
        {
            get => _session?.LastKnownFullName;
            set { /* устанавливается только из SessionManager */ }
        }
        private string lastKnownGroup
        {
            get => _session?.LastKnownGroup;
            set { /* устанавливается только из SessionManager */ }
        }

        // ──────────────────────────────────────────────────────────
        // Обновление состояния аутентификации (UI-слой)
        // ──────────────────────────────────────────────────────────
        private void UpdateAuthState(UserRole role, string userName = "")
        {
            currentUserRole = role;
            userContextMenu.Items.Clear();

            if (role == UserRole.Guest)
            {
                btnUserMenu.Text = "👤 Войти";
                connectionStatusLabel.Text = "🟠 Не авторизован.";
                syncStatusLabel.Text = "";
            }
            else
            {
                string roleSuffix = role == UserRole.Admin ? " (Admin)" : role == UserRole.Teacher ? " (Преп.)" : "";
                btnUserMenu.Text = $"👤 {userName}{roleSuffix} ▼";
                connectionStatusLabel.Text = "🟢 Подключено к серверу";
                syncStatusLabel.Text = $"Последняя синхронизация: {DateTime.Now:dd.MM.yyyy HH:mm}";

                var profileItem = new ToolStripMenuItem("Профиль");
                profileItem.Click += (s, e) => ShowStudentProfile();
                userContextMenu.Items.Add(profileItem);

                if (role == UserRole.Student)
                {
                    var myCoursesItem = new ToolStripMenuItem("Мои курсы");
                    myCoursesItem.Click += (s, e) => ShowStudentUnifiedWindow();
                    userContextMenu.Items.Add(myCoursesItem);
                }
                else if (role == UserRole.Teacher)
                {
                    var coursesItem = new ToolStripMenuItem("Мои курсы");
                    coursesItem.Click += (s, e) => ShowTeacherUnifiedWindow();
                    userContextMenu.Items.Add(coursesItem);
                }
                else if (role == UserRole.Admin)
                {
                    var coursesItem = new ToolStripMenuItem("Курсы");
                    coursesItem.Click += (s, e) => ShowTeacherUnifiedWindow();
                    userContextMenu.Items.Add(coursesItem);

                    var adminItem = new ToolStripMenuItem("Администрирование");
                    adminItem.Click += (s, e) => ShowAdministrationWindow();
                    userContextMenu.Items.Add(adminItem);
                }

                userContextMenu.Items.Add(new ToolStripSeparator());
                var logoutItem = new ToolStripMenuItem("Выйти");
                logoutItem.Click += (s, e) => ShowLogoutDialog();
                userContextMenu.Items.Add(logoutItem);
            }
        }

        // ──────────────────────────────────────────────────────────
        // Форма входа
        // ──────────────────────────────────────────────────────────
        private void ShowLoginForm()
        {
            var settings = ConnectionSettings.Load();
            _api.Configure(settings.ApiBaseUrl);

            Form loginForm = new Form { Text = "Вход в систему", ClientSize = new Size(420, 155) };
            ApplyDialogStyle(loginForm);

            Label lblLogin    = new Label { Text = "Логин:",  Location = new Point(20, 18), AutoSize = true };
            TextBox txtLogin  = new TextBox { Location = new Point(95, 15), Width = 305, Text = settings.LastLogin };
            Label lblPassword = new Label { Text = "Пароль:", Location = new Point(20, 53), AutoSize = true };
            TextBox txtPassword = new TextBox { Location = new Point(95, 50), Width = 305, PasswordChar = '*' };
            Panel separator   = new Panel { Location = new Point(0, 90), Size = new Size(420, 1), BackColor = Color.LightGray };
            Button btnLogin   = new Button { Text = "Войти", Location = new Point(310, 105), Size = new Size(90, 30) };
            ApplyDialogButtonStyle(btnLogin);

            btnLogin.Click += async (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtLogin.Text)) { ShowWarningDialog("Введите логин."); return; }
                if (string.IsNullOrWhiteSpace(txtPassword.Text)) { ShowWarningDialog("Введите пароль."); return; }

                btnLogin.Enabled = false;
                btnLogin.Text = "Вход...";

                var (isSuccess, err) = await _api.LoginAsync(txtLogin.Text, txtPassword.Text);
                if (isSuccess)
                {
                    var user = _api.CurrentUser;
                    UserRole role = user.Role switch { "Admin" => UserRole.Admin, "Teacher" => UserRole.Teacher, _ => UserRole.Student };
                    UpdateAuthState(role, user.FullName);
                    settings.LastLogin = txtLogin.Text;
                    settings.Save();
                    StartSessionHeartbeat();
                    loginForm.Close();
                    if (user.MustChangePassword)
                    {
                        ShowWarningDialog("Администратор сбросил Ваш пароль. Сейчас потребуется задать новый постоянный пароль.");
                        ShowChangePasswordWindow(forceChange: true);
                    }
                }
                else
                {
                    switch (err?.Reason)
                    {
                        case "AlreadyLoggedIn":
                        case "TooManyAttempts": ShowWarningDialog(err.Message); break;
                        case "NetworkError":    ShowErrorDialog(err.Message); break;
                        default: ShowErrorDialog(err?.Message ?? "Не удалось войти в систему. Попробуйте ещё раз."); break;
                    }
                    btnLogin.Enabled = true;
                    btnLogin.Text = "Войти";
                }
            };

            loginForm.Controls.AddRange(new Control[] { lblLogin, txtLogin, lblPassword, txtPassword, separator, btnLogin });
            loginForm.AcceptButton = btnLogin;
            KeyEventHandler enterShortcut = (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; btnLogin.PerformClick(); } };
            txtLogin.KeyDown    += enterShortcut;
            txtPassword.KeyDown += enterShortcut;
            loginForm.Shown += (s, e) => { if (string.IsNullOrEmpty(txtLogin.Text)) loginForm.ActiveControl = txtLogin; else loginForm.ActiveControl = txtPassword; };
            loginForm.ShowDialog(this);
        }

        // ──────────────────────────────────────────────────────────
        // Выход
        // ──────────────────────────────────────────────────────────
        private async void ShowLogoutDialog()
        {
            Form logoutForm = new Form { Text = "Подтверждение выхода", ClientSize = new Size(420, 145) };
            ApplyDialogStyle(logoutForm);

            Label lblTitle = new Label { Text = "Выход из системы",  Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(15, 15), AutoSize = true };
            Label lblSub   = new Label { Text = "Вы действительно хотите выйти?\nВсе несохранённые изменения будут потеряны.", Font = new Font("Segoe UI", 9), Location = new Point(15, 40), Size = new Size(390, 40) };
            Panel separator = new Panel { Location = new Point(0, 90), Size = new Size(420, 1), BackColor = Color.LightGray };
            Button btnCancel = new Button { Text = "Отмена",    DialogResult = DialogResult.Cancel, Location = new Point(225, 100), Size = new Size(85, 28) };
            Button btnYes    = new Button { Text = "Да, выйти", DialogResult = DialogResult.Yes,    Location = new Point(320, 100), Size = new Size(85, 28) };
            ApplyDialogButtonStyle(btnCancel); ApplyDialogButtonStyle(btnYes);

            logoutForm.Controls.AddRange(new Control[] { lblTitle, lblSub, separator, btnCancel, btnYes });
            logoutForm.AcceptButton = btnCancel; logoutForm.CancelButton = btnCancel;
            logoutForm.Shown += (s, e) => logoutForm.ActiveControl = null;

            if (logoutForm.ShowDialog(this) == DialogResult.Yes)
            {
                StopSessionHeartbeat();
                await _api.LogoutAsync();
                UpdateAuthState(UserRole.Guest);
            }
        }

        // ──────────────────────────────────────────────────────────
        // Профиль пользователя
        // ──────────────────────────────────────────────────────────
        private void ShowStudentProfile()
        {
            string formTitle = currentUserRole switch { UserRole.Admin => "Профиль администратора", UserRole.Teacher => "Профиль преподавателя", _ => "Профиль студента" };
            Form profileForm = new Form { Text = formTitle, Size = new Size(440, 240), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = Color.WhiteSmoke };

            Button dummyFocus = new Button { Location = new Point(-50, -50), Size = new Size(0, 0) };
            profileForm.Controls.Add(dummyFocus);

            GroupBox grp = new GroupBox { Text = "Личные данные", Location = new Point(15, 10), Size = new Size(395, 180), Font = new Font("Segoe UI", 9) };
            string fName  = _api.CurrentUser?.FullName ?? "";
            string login  = _api.CurrentUser?.Login ?? "";
            string roleStr = _api.CurrentUser?.Role switch { "Admin" => "Администратор", "Teacher" => "Преподаватель", _ => "Студент" };
            string group  = _api.CurrentUser?.Group ?? "—";

            grp.Controls.Add(new Label { Text = "ФИО:", Location = new Point(15, 28), AutoSize = true });
            TextBox txtFullName = new TextBox { Text = fName, Location = new Point(75, 25), Width = 300, ReadOnly = true, BackColor = SystemColors.Control, Font = new Font("Segoe UI", 9) };
            txtFullName.Enter += (s, e) => dummyFocus.Focus();
            grp.Controls.Add(txtFullName);

            grp.Controls.Add(new Label { Text = "Логин:", Location = new Point(15, 60), AutoSize = true });
            TextBox txtLogin = new TextBox { Text = login, Location = new Point(75, 57), Width = 300, ReadOnly = true, BackColor = SystemColors.Control, Font = new Font("Segoe UI", 9) };
            txtLogin.Enter += (s, e) => dummyFocus.Focus();
            grp.Controls.Add(txtLogin);

            if (currentUserRole == UserRole.Student)
            {
                grp.Controls.Add(new Label { Text = "Группа:", Location = new Point(15, 92), AutoSize = true });
                TextBox txtGroup = new TextBox { Text = group, Location = new Point(75, 89), Width = 130, ReadOnly = true, BackColor = SystemColors.Control, Font = new Font("Segoe UI", 9) };
                txtGroup.Enter += (s, e) => dummyFocus.Focus();
                grp.Controls.Add(txtGroup);

                grp.Controls.Add(new Label { Text = "Роль:", Location = new Point(225, 92), AutoSize = true });
                TextBox txtRole = new TextBox { Text = roleStr, Location = new Point(265, 89), Width = 110, ReadOnly = true, BackColor = SystemColors.Control, Font = new Font("Segoe UI", 9) };
                txtRole.Enter += (s, e) => dummyFocus.Focus();
                grp.Controls.Add(txtRole);
            }
            else
            {
                grp.Controls.Add(new Label { Text = "Роль:", Location = new Point(15, 92), AutoSize = true });
                TextBox txtRole = new TextBox { Text = roleStr, Location = new Point(75, 89), Width = 300, ReadOnly = true, BackColor = SystemColors.Control, Font = new Font("Segoe UI", 9) };
                txtRole.Enter += (s, e) => dummyFocus.Focus();
                grp.Controls.Add(txtRole);
            }

            grp.Controls.Add(new Label { Text = "ФИО можно изменить только через администратора.", Location = new Point(15, 118), Size = new Size(360, 18), Font = new Font("Segoe UI", 8), ForeColor = Color.DimGray });
            Button btnChangePwd = new Button { Text = "Сменить пароль", Location = new Point(15, 140), Size = new Size(360, 28), FlatStyle = FlatStyle.Standard, Font = new Font("Segoe UI", 9) };
            btnChangePwd.Click += (s, e) => ShowChangePasswordWindow(forceChange: false);
            grp.Controls.Add(btnChangePwd);

            profileForm.Controls.Add(grp);
            profileForm.Load += (s, e) => profileForm.ActiveControl = dummyFocus;
            profileForm.ShowDialog(this);
        }

        // ──────────────────────────────────────────────────────────
        // Смена пароля
        // ──────────────────────────────────────────────────────────
        private void ShowChangePasswordWindow(bool forceChange = false)
        {
            bool isDirty = false;
            bool forceClose = false;

            Form pwdForm = new Form { Text = forceChange ? "Обязательная смена пароля" : "Смена пароля", ClientSize = new Size(460, 270), ControlBox = !forceChange };
            ApplyDialogStyle(pwdForm);

            Label lblOld = new Label { Text = "Текущий пароль:", Location = new Point(15, 18), AutoSize = true };
            TextBox txtOld = new TextBox { Location = new Point(15, 40), Width = 430, PasswordChar = '*' };
            Label lblNew = new Label { Text = $"Новый пароль (минимум {ValidationRules.PasswordMin} символов, латиница + цифры):", Location = new Point(15, 75), AutoSize = true };
            TextBox txtNew = new TextBox { Location = new Point(15, 97), Width = 430, PasswordChar = '*' };
            Label lblConfirm = new Label { Text = "Подтвердите новый пароль:", Location = new Point(15, 132), AutoSize = true };
            TextBox txtConfirm = new TextBox { Location = new Point(15, 154), Width = 430, PasswordChar = '*' };
            Label lblHint = new Label { Text = "Требования к паролю: латинские буквы, цифры и стандартные спецсимволы. Обязательно минимум одна буква и одна цифра. Пробелы и русские буквы запрещены.", Location = new Point(15, 182), Size = new Size(430, 35), Font = new Font("Segoe UI", 8), ForeColor = Color.DimGray };
            Panel separator = new Panel { Location = new Point(0, 225), Size = new Size(460, 1), BackColor = Color.LightGray };
            Button btnCancel = new Button { Text = "Отмена",  Location = new Point(265, 235), Size = new Size(85, 28), Enabled = !forceChange };
            Button btnSave   = new Button { Text = "Сменить", Location = new Point(360, 235), Size = new Size(85, 28) };
            ApplyDialogButtonStyle(btnCancel); ApplyDialogButtonStyle(btnSave);

            EventHandler markDirty = (s, e) => isDirty = !string.IsNullOrEmpty(txtOld.Text) || !string.IsNullOrEmpty(txtNew.Text) || !string.IsNullOrEmpty(txtConfirm.Text);
            txtOld.TextChanged += markDirty; txtNew.TextChanged += markDirty; txtConfirm.TextChanged += markDirty;

            btnSave.Click += async (s, e) =>
            {
                if (string.IsNullOrEmpty(txtOld.Text)) { ShowWarningDialog("Введите текущий пароль."); return; }
                if (!ValidationRules.ValidatePassword(txtNew.Text, out string pwErr)) { ShowWarningDialog(pwErr); return; }
                if (txtNew.Text != txtConfirm.Text) { ShowWarningDialog("Введённые новые пароли не совпадают. Проверьте оба поля."); return; }
                if (txtOld.Text == txtNew.Text) { ShowWarningDialog("Новый пароль должен отличаться от текущего."); return; }

                btnSave.Enabled = false; btnCancel.Enabled = false; btnSave.Text = "Сохранение...";
                bool ok = await _api.ChangePasswordAsync(txtOld.Text, txtNew.Text);
                if (ok)
                {
                    ShowSuccessDialog("Пароль успешно изменён."); isDirty = false; forceClose = true;
                    pwdForm.DialogResult = DialogResult.OK; pwdForm.Close();
                }
                else
                {
                    ShowErrorDialog("Не удалось сменить пароль. Проверьте, что текущий пароль введён правильно.");
                    btnSave.Enabled = true; btnSave.Text = "Сменить"; btnCancel.Enabled = !forceChange;
                }
            };
            btnCancel.Click += (s, e) => { pwdForm.DialogResult = DialogResult.Cancel; pwdForm.Close(); };
            pwdForm.FormClosing += (s, e) =>
            {
                if (forceChange && pwdForm.DialogResult != DialogResult.OK) { ShowWarningDialog("Сначала смените пароль для продолжения работы."); e.Cancel = true; return; }
                if (forceClose) return;
                if (isDirty && !ConfirmDiscardChanges()) e.Cancel = true;
            };

            pwdForm.Controls.AddRange(new Control[] { lblOld, txtOld, lblNew, txtNew, lblConfirm, txtConfirm, lblHint, separator, btnCancel, btnSave });
            pwdForm.AcceptButton = btnSave;
            if (!forceChange) pwdForm.CancelButton = btnCancel;
            pwdForm.ShowDialog(this);
        }

        // ──────────────────────────────────────────────────────────
        // Сброс пароля пользователю (администратором)
        // ──────────────────────────────────────────────────────────
        private async Task<bool> ShowResetPasswordWindow(int userId, string fullName, string roleHint)
        {
            bool resultOk = false;
            bool isDirty = false;
            bool forceClose = false;

            Form pwdForm = new Form { Text = "Сброс пароля пользователя", ClientSize = new Size(470, 330) };
            ApplyDialogStyle(pwdForm);

            Label lblTitle = new Label { Text = "Назначение нового временного пароля", Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(15, 15), AutoSize = true };
            Label lblSub = new Label
            {
                Text = $"Пользователь: {fullName}\nРоль: {roleHint}\n\nПри следующем входе пользователь будет обязан задать новый постоянный пароль. Текущая сессия пользователя (если он сейчас в системе) будет принудительно завершена.",
                Font = new Font("Segoe UI", 9), Location = new Point(15, 40), Size = new Size(440, 75)
            };
            Label lblNew     = new Label { Text = $"Новый временный пароль (минимум {ValidationRules.PasswordMin} символов):", Location = new Point(15, 125), AutoSize = true };
            TextBox txtNew   = new TextBox { Location = new Point(15, 147), Width = 440, PasswordChar = '*' };
            Label lblConfirm = new Label { Text = "Повторите новый пароль:", Location = new Point(15, 180), AutoSize = true };
            TextBox txtConfirm = new TextBox { Location = new Point(15, 202), Width = 440, PasswordChar = '*' };
            CheckBox chkShow = new CheckBox { Text = "Показать пароль", Location = new Point(15, 233), AutoSize = true, Font = new Font("Segoe UI", 8) };
            chkShow.CheckedChanged += (s, e) => { txtNew.PasswordChar = chkShow.Checked ? '\0' : '*'; txtConfirm.PasswordChar = chkShow.Checked ? '\0' : '*'; };
            Label lblHint = new Label { Text = "Требования: латиница + цифры, минимум одна буква и одна цифра, без пробелов и русских символов.", Location = new Point(15, 256), Size = new Size(440, 30), Font = new Font("Segoe UI", 8), ForeColor = Color.DimGray };
            Panel separator = new Panel { Location = new Point(0, 285), Size = new Size(470, 1), BackColor = Color.LightGray };
            Button btnCancel = new Button { Text = "Отмена",   Location = new Point(275, 295), Size = new Size(85, 28) };
            Button btnOk     = new Button { Text = "Сбросить", Location = new Point(370, 295), Size = new Size(85, 28) };
            ApplyDialogButtonStyle(btnCancel); ApplyDialogButtonStyle(btnOk);

            EventHandler markDirty = (s, e) => isDirty = !string.IsNullOrEmpty(txtNew.Text) || !string.IsNullOrEmpty(txtConfirm.Text);
            txtNew.TextChanged += markDirty; txtConfirm.TextChanged += markDirty;

            btnOk.Click += async (s, e) =>
            {
                if (!ValidationRules.ValidatePassword(txtNew.Text, out string pwErr)) { ShowWarningDialog(pwErr); return; }
                if (txtNew.Text != txtConfirm.Text) { ShowWarningDialog("Введённые пароли не совпадают. Проверьте правильность ввода в обоих полях."); return; }

                var sessionStatus = await CheckUserOnlineStatus(userId);
                if (sessionStatus.IsOnline)
                {
                    bool confirmed = ShowActiveSessionWarningDialog(fullName, sessionStatus.IpAddress, sessionStatus.LastActivity, "Вы сбрасываете пароль пользователю.", "Сбросить");
                    if (!confirmed) return;
                }

                btnOk.Enabled = false; btnCancel.Enabled = false;
                var res = await ApiClient.ResetUserPasswordAdminAsync(userId, txtNew.Text);
                if (res.IsSuccess)
                {
                    ShowSuccessDialog($"Пароль успешно сброшен.\n\nНовый временный пароль: {txtNew.Text}\n\nСообщите его пользователю любым доступным каналом. При следующем входе он сменит пароль на постоянный.");
                    resultOk = true; isDirty = false; forceClose = true;
                    pwdForm.DialogResult = DialogResult.OK; pwdForm.Close(); return;
                }
                if (res.IsConflict) { resultOk = true; isDirty = false; forceClose = true; ShowAdminStaleDataDialog(); pwdForm.DialogResult = DialogResult.OK; pwdForm.Close(); return; }
                if (res.Status == ApiClient.AdminOpStatus.SessionEnded) { forceClose = true; pwdForm.DialogResult = DialogResult.Cancel; pwdForm.Close(); return; }
                ShowErrorDialog(!string.IsNullOrEmpty(res.Message) ? res.Message : $"Не удалось сбросить пароль пользователя «{fullName}».");
                btnOk.Enabled = true; btnCancel.Enabled = true;
            };
            btnCancel.Click += (s, e) => { pwdForm.DialogResult = DialogResult.Cancel; pwdForm.Close(); };
            pwdForm.FormClosing += (s, e) => { if (forceClose) return; if (isDirty && !ConfirmDiscardChanges()) e.Cancel = true; };

            pwdForm.Controls.AddRange(new Control[] { lblTitle, lblSub, lblNew, txtNew, lblConfirm, txtConfirm, chkShow, lblHint, separator, btnCancel, btnOk });
            pwdForm.AcceptButton = btnOk; pwdForm.CancelButton = btnCancel;
            pwdForm.Shown += (s, e) => txtNew.Focus();
            pwdForm.ShowDialog(this);
            return resultOk;
        }

        // ──────────────────────────────────────────────────────────
        // Запуск/остановка сессии — теперь это тонкие обёртки над SessionManager.
        // Бизнес-логика heartbeat живёт в SessionManager (Auth/SessionManager.cs).
        // ──────────────────────────────────────────────────────────
        private void StartSessionHeartbeat()
        {
            _ = _session.StartAsync();
        }

        private void StopSessionHeartbeat()
        {
            _session.Stop();
        }

        private Task CheckSessionHeartbeatAsync() => Task.CompletedTask;

        private void CloseAllChildForms()
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(CloseAllChildForms)); return; }
            try
            {
                foreach (var f in System.Windows.Forms.Application.OpenForms.Cast<Form>().ToList())
                {
                    if (f != this && !f.IsDisposed) try { f.Close(); } catch { }
                }
            }
            catch { }
        }

        // ──────────────────────────────────────────────────────────
        // Доменные события SessionManager → реакция UI.
        // Здесь только UI-действия: меню, диалоги, закрытие окон.
        // ──────────────────────────────────────────────────────────
        private void OnSessionManagerSessionEnded(string reason, string message)
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(() => OnSessionManagerSessionEnded(reason, message))); return; }
            HandleSessionEnded(new ApiClient.ApiError { Reason = reason, Message = message });
        }

        private void OnSessionManagerProfileChanged(string fullName, string login, string group)
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(() => OnSessionManagerProfileChanged(fullName, login, group))); return; }

            bool loginChanged = !string.Equals(_session.LastKnownLogin, login, StringComparison.Ordinal);
            bool nameChanged  = !string.Equals(_session.LastKnownFullName, fullName, StringComparison.Ordinal);
            bool groupChanged = !string.Equals(_session.LastKnownGroup ?? "", group ?? "", StringComparison.Ordinal);
            if (!loginChanged && !nameChanged && !groupChanged) return;

            if (ApiClient.CurrentUser != null) { ApiClient.CurrentUser.Login = login; ApiClient.CurrentUser.FullName = fullName; ApiClient.CurrentUser.Group = group; }
            _session.UpdateLastKnownProfile(fullName, login, group);

            if (btnUserMenu != null && btnUserMenu.IsHandleCreated)
            {
                string roleSuffix = currentUserRole switch { UserRole.Admin => " (Admin)", UserRole.Teacher => " (Преп.)", _ => "" };
                btnUserMenu.Text = $"👤 {fullName}{roleSuffix} ▼";
            }

            CloseAllChildForms();
            var changes = new List<string>();
            if (nameChanged)  changes.Add("ФИО");
            if (loginChanged) changes.Add("логин");
            if (groupChanged) changes.Add("группа");
            string msg = $"Администратор изменил ваши данные ({string.Join(", ", changes)}).\n\nТекущее ФИО: {fullName}\nТекущий логин: {login}";
            if (!string.IsNullOrEmpty(group)) msg += $"\nГруппа: {group}";
            else if (groupChanged) msg += "\nГруппа: не назначена";
            msg += "\n\nВсе открытые окна были закрыты, чтобы избежать работы с устаревшими данными. Откройте их заново — там будут актуальные сведения.";
            ShowWarningDialog(msg);
        }

        private void OnSessionManagerAssignmentChanged(int assignmentId, string reason, string message)
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(() => OnSessionManagerAssignmentChanged(assignmentId, reason, message))); return; }
            if (!activeAssignmentId.HasValue || activeAssignmentId.Value != assignmentId) return;
            if (isTaskReadOnlyMode) return;

            if (reason == "MTUpdated")
            {
                // Показать плашку и кнопку сброса, не блокировать работу
                if (panelMTOutdated != null) panelMTOutdated.Visible = true;
                if (btnResetToNewMTTop != null) { btnResetToNewMTTop.Visible = true; btnResetToNewMTTop.Enabled = true; }
                ShowWarningDialog("Преподаватель обновил конфигурацию МТ задания.\n\nВаше текущее решение основано на старой версии. Вы можете сохранить его в файл (Файл → Экспорт программы), затем нажать «Начать с новой МТ».");
                return;
            }

            if (btnDraftTop != null) btnDraftTop.Enabled = false;
            if (btnSubmitTop != null) btnSubmitTop.Enabled = false;
            if (btnResetSolutionTop != null) btnResetSolutionTop.Enabled = false;
            if (btnRevokeTop != null) btnRevokeTop.Enabled = false;
            ShowWarningDialog(message ?? "Состояние задания изменилось. Сохраните МТ и покиньте окно выполнения.");
        }

        // ──────────────────────────────────────────────────────────
        // Обработка завершения сессии (UI-слой)
        // ──────────────────────────────────────────────────────────
        private void HandleSessionEnded(ApiClient.ApiError err)
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(() => HandleSessionEnded(err))); return; }
            lock (_sessionEndSync)
            {
                if (_sessionEndInProgress) return;
                if (string.IsNullOrEmpty(_api.SessionId) && currentUserRole == UserRole.Guest) return;
                if (err != null && err.Reason == "NoSession" && string.IsNullOrEmpty(_api.SessionId)) return;
                _sessionEndInProgress = true;
                _sessionEndStartedAt = DateTime.Now;
            }

            try { _api.ClearSessionLocally(); } catch { }
            StopSessionHeartbeat();
            CloseAllChildForms();

            string msg, title;
            switch (err?.Reason)
            {
                case "LoggedInElsewhere":      title = "Вход с другого устройства";       msg = string.IsNullOrEmpty(err.Message) ? "В вашу учётную запись только что вошли с другого устройства. Текущая сессия завершена. Если это были не вы — немедленно смените пароль после повторного входа." : err.Message; break;
                case "PasswordReset":          title = "Пароль сброшен администратором";  msg = string.IsNullOrEmpty(err.Message) ? "Администратор сбросил пароль для вашей учётной записи. Текущая сессия завершена. Войдите заново — при входе потребуется задать новый постоянный пароль." : err.Message; break;
                case "LoginChanged":           title = "Логин изменён администратором";   msg = string.IsNullOrEmpty(err.Message) ? "Администратор изменил ваш логин. Текущая сессия завершена. Войдите в систему, используя новый логин." : err.Message; break;
                case "LoginAndPasswordChanged": title = "Логин и пароль изменены";        msg = string.IsNullOrEmpty(err.Message) ? "Администратор одновременно изменил ваш логин и пароль. Текущая сессия завершена. Войдите заново с новыми учётными данными." : err.Message; break;
                case "AccountDeleted":
                case "UserDeleted":            title = "Учётная запись удалена";          msg = string.IsNullOrEmpty(err.Message) ? "Ваша учётная запись была удалена администратором. Войти в систему больше нельзя." : err.Message; break;
                case "SessionExpired":         title = "Сессия истекла";                  msg = string.IsNullOrEmpty(err.Message) ? "Ваша сессия истекла из-за длительного бездействия. Войдите заново." : err.Message; break;
                case "NoSession":              title = "Не авторизован";                  msg = string.IsNullOrEmpty(err.Message) ? "Войдите в систему, чтобы продолжить работу." : err.Message; break;
                case "ProfileDataChanged":     title = "Данные профиля изменены";         msg = string.IsNullOrEmpty(err.Message) ? "Данные вашего профиля были изменены. Сессия принудительно завершена." : err.Message; break;
                case "SessionRevoked":         title = "Сессия завершена";                msg = string.IsNullOrEmpty(err.Message) ? "Текущая сессия была завершена. Войдите в систему заново." : err.Message; break;
                default:                       title = "Сессия завершена";                msg = string.IsNullOrEmpty(err?.Message) ? "Ваша сессия была завершена. Войдите заново." : err.Message; break;
            }

            if (activeAssignmentId.HasValue || checkingModePanel != null)
                msg += "\n\nЕсли вы редактировали решение задания — экспортируйте его сейчас через меню «Файл → Экспорт программы», прежде чем входить заново.";

            ShowFixedSizeDialog(msg, title, SystemIcons.Warning.ToBitmap());
            UpdateAuthState(UserRole.Guest);
            if (activeAssignmentId.HasValue) ExitTaskExecutionMode();
            if (checkingModePanel != null) ExitCheckingMode();

            var resetTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            resetTimer.Tick += (s, e) => { resetTimer.Stop(); resetTimer.Dispose(); lock (_sessionEndSync) { _sessionEndInProgress = false; } };
            resetTimer.Start();
        }

        private void HandleConflict(ApiClient.ApiError err)
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(() => HandleConflict(err))); return; }

            bool isDuplicateName = err != null && string.Equals(err.Reason, "DuplicateName", StringComparison.OrdinalIgnoreCase);
            string title = err?.Reason switch
            {
                "DuplicateName"        => "Имя уже занято",
                "AssignmentDeleted"    => "Задание удалено",
                "AssignmentArchived"   => "Задание в архиве",
                "CourseDeleted"        => "Курс удалён",
                "GroupDeleted"         => "Группа удалена",
                "UserDeleted"          => "Пользователь удалён",
                "SubmissionDeleted"    => "Работа недоступна",
                "VersionConflict"      => "Изменения другого пользователя",
                "AssignmentClosed"     => "Задание закрыто",
                "DeadlineExpired"      => "Срок сдачи истёк",
                "DeadlineNotPassed"    => "Срок ещё не истёк",
                "DeadlineChanged"      => "Срок сдачи изменён",
                "CourseArchived"       => "Курс в архиве",
                "SubmissionLocked"     => "Работа заблокирована",
                "SubmissionStateChanged" => "Состояние работы изменилось",
                "AlreadyChecked"       => "Работа уже проверена",
                "AlreadyRevoked"       => "Работа уже отозвана",
                "BeingChecked"         => "Работа на проверке",
                "GradeRevoked"         => "Оценка отозвана",
                "NoDraft"              => "Нет загруженного решения",
                "NoSubmission"         => "Нет работы",
                "NetworkError"         => "Нет соединения",
                "ServerError"          => "Ошибка сервера",
                _                     => isDuplicateName ? "Имя уже занято" : "Конфликт данных"
            };

            string msg = isDuplicateName
                ? (string.IsNullOrEmpty(err.Message) ? "Указанное имя уже занято другим объектом. Выберите другое значение." : err.Message)
                : (string.IsNullOrEmpty(err.Message) ? "Состояние данных на сервере изменилось. Окно будет закрыто, откройте его заново для актуальных данных." : err.Message);

            var icon = (err?.Reason == "NetworkError" || err?.Reason == "ServerError") ? SystemIcons.Error.ToBitmap() : SystemIcons.Warning.ToBitmap();
            ShowFixedSizeDialog(msg, title, icon);
        }

        // ──────────────────────────────────────────────────────────
        // Закрытие формы / выключение ПК — делегировано SessionManager.
        // ──────────────────────────────────────────────────────────
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                try { Microsoft.Win32.SystemEvents.SessionEnding -= SystemEvents_SessionEnding; } catch { }
                if (string.IsNullOrEmpty(_api.SessionId)) return;
                _session.FastFireAndForgetLogout(TimeSpan.FromMilliseconds(800));
            }
            catch { }
        }

        private void SystemEvents_SessionEnding(object sender, Microsoft.Win32.SessionEndingEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_api.SessionId)) return;
                _session.FastFireAndForgetLogout(TimeSpan.FromMilliseconds(500));
            }
            catch { }
        }
    }
}
