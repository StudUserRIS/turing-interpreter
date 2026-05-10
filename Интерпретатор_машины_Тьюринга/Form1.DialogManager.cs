using System;
using System.Drawing;
using System.Windows.Forms;

namespace Интерпретатор_машины_Тьюринга
{
    public partial class Form1
    {
        // ──────────────────────────────────────────────────────────
        // Антидубликатор диалогов
        // ──────────────────────────────────────────────────────────
        private static string   _lastDialogKey  = null;
        private static DateTime _lastDialogTime = DateTime.MinValue;
        private static readonly object _dialogSync = new object();

        private bool TryRegisterDialog(string title, string message)
        {
            lock (_dialogSync)
            {
                string key = (title ?? "") + "||" + (message ?? "");
                var now = DateTime.Now;
                if (_lastDialogKey == key && (now - _lastDialogTime).TotalMilliseconds < 1500)
                    return false;
                _lastDialogKey  = key;
                _lastDialogTime = now;
                return true;
            }
        }

        // ──────────────────────────────────────────────────────────
        // Информационные диалоги
        // ──────────────────────────────────────────────────────────
        private void ShowSuccessDialog(string message)
            => ShowFixedSizeDialog(message, "Успех", SystemIcons.Information.ToBitmap());

        private void ShowErrorDialog(string message)
            => ShowFixedSizeDialog(message, "Ошибка", SystemIcons.Error.ToBitmap());

        private void ShowWarningDialog(string message)
            => ShowFixedSizeDialog(message, "Предупреждение", SystemIcons.Warning.ToBitmap());

        /// <summary>
        /// Универсальный информационный диалог с автомасштабированием.
        /// Защищён от каскадных дубликатов через TryRegisterDialog.
        /// </summary>
        private void ShowFixedSizeDialog(string message, string title, System.Drawing.Bitmap icon)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => ShowFixedSizeDialog(message, title, icon)));
                return;
            }

            if (!TryRegisterDialog(title, message)) return;

            message = message ?? "";

            const int minFormWidth   = 440;
            const int maxFormWidth   = 720;
            const int iconAreaWidth  = 70;
            const int rightPadding   = 25;
            const int btnHeight      = 28;
            const int btnTopPadding  = 12;
            const int separatorOffset = 18;
            const int textTopPadding = 22;
            const int bottomPadding  = 18;

            int formWidth = minFormWidth;
            int textHeight;
            var font = new Font("Segoe UI", 9);

            using (var g = this.CreateGraphics())
            {
                int availableTextWidth = formWidth - iconAreaWidth - rightPadding;
                var size = g.MeasureString(message, font, availableTextWidth);
                textHeight = (int)Math.Ceiling(size.Height) + 8;

                while (textHeight > 260 && formWidth < maxFormWidth)
                {
                    formWidth = Math.Min(formWidth + 40, maxFormWidth);
                    availableTextWidth = formWidth - iconAreaWidth - rightPadding;
                    size = g.MeasureString(message, font, availableTextWidth);
                    textHeight = (int)Math.Ceiling(size.Height) + 8;
                }
            }

            int finalTextWidth   = formWidth - iconAreaWidth - rightPadding;
            int textBlockHeight  = Math.Max(50, textHeight);
            int separatorY       = textTopPadding + textBlockHeight + separatorOffset;
            int btnY             = separatorY + btnTopPadding;
            int formHeight       = btnY + btnHeight + bottomPadding;

            Form dlg = new Form
            {
                Text            = title,
                ClientSize      = new Size(formWidth, formHeight),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition   = FormStartPosition.CenterParent,
                MaximizeBox     = false,
                MinimizeBox     = false,
                ShowInTaskbar   = false,
                BackColor       = Color.WhiteSmoke,
                Font            = font
            };

            PictureBox iconBox = new PictureBox
            {
                Location = new Point(20, textTopPadding + 5),
                Size     = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.StretchImage,
                Image    = icon
            };

            Label lblMessage = new Label
            {
                Text      = message,
                Location  = new Point(iconAreaWidth, textTopPadding),
                Size      = new Size(finalTextWidth, textBlockHeight),
                TextAlign = ContentAlignment.TopLeft,
                Font      = font,
                AutoEllipsis = false
            };

            Panel separator = new Panel
            {
                Location  = new Point(0, separatorY),
                Size      = new Size(formWidth, 1),
                BackColor = Color.LightGray
            };

            Button btnOk = new Button
            {
                Text         = "ОК",
                Location     = new Point(formWidth - 100, btnY),
                Size         = new Size(85, btnHeight),
                DialogResult = DialogResult.OK
            };
            ApplyDialogButtonStyle(btnOk);

            dlg.Controls.AddRange(new Control[] { iconBox, lblMessage, separator, btnOk });
            dlg.AcceptButton = btnOk;
            dlg.CancelButton = btnOk;
            dlg.Shown += (s, e) => dlg.ActiveControl = null;

            if (this.IsHandleCreated && this.Visible)
                dlg.ShowDialog(this);
            else
                dlg.ShowDialog();
        }

        // ──────────────────────────────────────────────────────────
        // Диалог подтверждения (Да / Нет)
        // ──────────────────────────────────────────────────────────
        private bool ShowConfirmDialog(string message, string title)
        {
            if (this.InvokeRequired)
            {
                bool result = false;
                this.Invoke(new Action(() => result = ShowConfirmDialog(message, title)));
                return result;
            }

            if (!TryRegisterDialog(title, message)) return false;

            message = message ?? "";

            const int minFormWidth   = 440;
            const int maxFormWidth   = 720;
            const int iconAreaWidth  = 70;
            const int rightPadding   = 25;
            const int btnHeight      = 28;
            const int btnTopPadding  = 12;
            const int separatorOffset = 18;
            const int textTopPadding = 22;
            const int bottomPadding  = 18;

            int formWidth = minFormWidth;
            int textHeight;
            var font = new Font("Segoe UI", 9);

            using (var g = this.CreateGraphics())
            {
                int availableTextWidth = formWidth - iconAreaWidth - rightPadding;
                var size = g.MeasureString(message, font, availableTextWidth);
                textHeight = (int)Math.Ceiling(size.Height) + 8;

                while (textHeight > 260 && formWidth < maxFormWidth)
                {
                    formWidth = Math.Min(formWidth + 40, maxFormWidth);
                    availableTextWidth = formWidth - iconAreaWidth - rightPadding;
                    size = g.MeasureString(message, font, availableTextWidth);
                    textHeight = (int)Math.Ceiling(size.Height) + 8;
                }
            }

            int finalTextWidth  = formWidth - iconAreaWidth - rightPadding;
            int textBlockHeight = Math.Max(50, textHeight);
            int separatorY      = textTopPadding + textBlockHeight + separatorOffset;
            int btnY            = separatorY + btnTopPadding;
            int formHeight      = btnY + btnHeight + bottomPadding;

            Form dlg = new Form
            {
                Text            = title,
                ClientSize      = new Size(formWidth, formHeight),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition   = FormStartPosition.CenterParent,
                MaximizeBox     = false,
                MinimizeBox     = false,
                ShowInTaskbar   = false,
                BackColor       = Color.WhiteSmoke,
                Font            = font
            };

            PictureBox iconBox = new PictureBox
            {
                Location = new Point(20, textTopPadding + 5),
                Size     = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.StretchImage,
                Image    = SystemIcons.Question.ToBitmap()
            };

            Label lblMessage = new Label
            {
                Text      = message,
                Location  = new Point(iconAreaWidth, textTopPadding),
                Size      = new Size(finalTextWidth, textBlockHeight),
                TextAlign = ContentAlignment.TopLeft,
                Font      = font,
                AutoEllipsis = false
            };

            Panel separator = new Panel
            {
                Location  = new Point(0, separatorY),
                Size      = new Size(formWidth, 1),
                BackColor = Color.LightGray
            };

            Button btnNo = new Button
            {
                Text         = "Нет",
                Location     = new Point(formWidth - 200, btnY),
                Size         = new Size(85, btnHeight),
                DialogResult = DialogResult.No
            };
            ApplyDialogButtonStyle(btnNo);

            Button btnYes = new Button
            {
                Text         = "Да",
                Location     = new Point(formWidth - 100, btnY),
                Size         = new Size(85, btnHeight),
                DialogResult = DialogResult.Yes
            };
            ApplyDialogButtonStyle(btnYes);

            dlg.Controls.AddRange(new Control[] { iconBox, lblMessage, separator, btnNo, btnYes });
            dlg.AcceptButton = btnYes;
            dlg.CancelButton = btnNo;
            dlg.Shown += (s, e) => dlg.ActiveControl = btnNo;

            DialogResult res;
            if (this.IsHandleCreated && this.Visible)
                res = dlg.ShowDialog(this);
            else
                res = dlg.ShowDialog();
            return res == DialogResult.Yes;
        }

        // ──────────────────────────────────────────────────────────
        // Предупреждение о активной сессии пользователя
        // ──────────────────────────────────────────────────────────
        private bool ShowActiveSessionWarningDialog(
            string fullName, string ipAddress, DateTime lastActivity,
            string actionDescription, string confirmButtonText)
        {
            if (this.InvokeRequired)
            {
                bool result = false;
                this.Invoke(new Action(() =>
                    result = ShowActiveSessionWarningDialog(fullName, ipAddress, lastActivity, actionDescription, confirmButtonText)));
                return result;
            }

            string message =
                $"Пользователь «{fullName}» сейчас находится в системе.\n\n" +
                $"IP-адрес: {ipAddress}\n" +
                $"Последняя активность: {lastActivity:dd.MM.yyyy HH:mm:ss}\n\n" +
                $"{actionDescription}\n\n" +
                "При подтверждении операции его текущая сессия будет НЕМЕДЛЕННО прервана, " +
                "а пользователь получит уведомление с причиной завершения работы.\n\n" +
                "Сохранить изменения?";

            const int minFormWidth   = 520;
            const int maxFormWidth   = 720;
            const int iconAreaWidth  = 70;
            const int rightPadding   = 25;
            const int btnHeight      = 28;
            const int btnTopPadding  = 12;
            const int separatorOffset = 18;
            const int textTopPadding = 22;
            const int bottomPadding  = 18;

            int formWidth = minFormWidth;
            int textHeight;
            var font = new Font("Segoe UI", 9);

            using (var g = this.CreateGraphics())
            {
                int availableTextWidth = formWidth - iconAreaWidth - rightPadding;
                var size = g.MeasureString(message, font, availableTextWidth);
                textHeight = (int)Math.Ceiling(size.Height) + 8;

                while (textHeight > 320 && formWidth < maxFormWidth)
                {
                    formWidth = Math.Min(formWidth + 40, maxFormWidth);
                    availableTextWidth = formWidth - iconAreaWidth - rightPadding;
                    size = g.MeasureString(message, font, availableTextWidth);
                    textHeight = (int)Math.Ceiling(size.Height) + 8;
                }
            }

            int finalTextWidth  = formWidth - iconAreaWidth - rightPadding;
            int textBlockHeight = Math.Max(50, textHeight);
            int separatorY      = textTopPadding + textBlockHeight + separatorOffset;
            int btnY            = separatorY + btnTopPadding;
            int formHeight      = btnY + btnHeight + bottomPadding;

            Form dlg = new Form
            {
                Text            = "Пользователь онлайн",
                ClientSize      = new Size(formWidth, formHeight),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition   = FormStartPosition.CenterParent,
                MaximizeBox     = false,
                MinimizeBox     = false,
                ShowInTaskbar   = false,
                BackColor       = Color.WhiteSmoke,
                Font            = font
            };

            PictureBox iconBox = new PictureBox
            {
                Location = new Point(20, textTopPadding + 5),
                Size     = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.StretchImage,
                Image    = SystemIcons.Warning.ToBitmap()
            };

            Label lblMessage = new Label
            {
                Text      = message,
                Location  = new Point(iconAreaWidth, textTopPadding),
                Size      = new Size(finalTextWidth, textBlockHeight),
                TextAlign = ContentAlignment.TopLeft,
                Font      = font,
                AutoEllipsis = false
            };

            Panel separator = new Panel
            {
                Location  = new Point(0, separatorY),
                Size      = new Size(formWidth, 1),
                BackColor = Color.LightGray
            };

            Button btnNo = new Button
            {
                Text         = "Отмена",
                Location     = new Point(formWidth - 200, btnY),
                Size         = new Size(85, btnHeight),
                DialogResult = DialogResult.No
            };
            ApplyDialogButtonStyle(btnNo);

            Button btnYes = new Button
            {
                Text         = confirmButtonText,
                Location     = new Point(formWidth - 100, btnY),
                Size         = new Size(85, btnHeight),
                DialogResult = DialogResult.Yes
            };
            ApplyDialogButtonStyle(btnYes);

            dlg.Controls.AddRange(new Control[] { iconBox, lblMessage, separator, btnNo, btnYes });
            dlg.AcceptButton = btnYes;
            dlg.CancelButton = btnNo;
            dlg.Shown += (s, e) => dlg.ActiveControl = btnNo;

            DialogResult res;
            if (this.IsHandleCreated && this.Visible)
                res = dlg.ShowDialog(this);
            else
                res = dlg.ShowDialog();
            return res == DialogResult.Yes;
        }

        // ──────────────────────────────────────────────────────────
        // Специализированные диалоги
        // ──────────────────────────────────────────────────────────

        /// <summary>Диалог для случая, когда данные окна устарели из-за действий другого пользователя.</summary>
        private void ShowAdminStaleDataDialog()
        {
            ShowFixedSizeDialog(
                "Данные окна не являются актуальными, окно будет принудительно закрыто, данные будут обновлены",
                "Данные устарели",
                SystemIcons.Warning.ToBitmap());
        }

        /// <summary>Стандартный диалог отмены несохранённых изменений. Возвращает true, если пользователь согласен закрыть.</summary>
        private bool ConfirmDiscardChanges()
        {
            return ShowConfirmDialog(
                "У вас есть несохранённые изменения.\nЗакрыть окно без сохранения?",
                "Несохранённые изменения");
        }
    }
}
