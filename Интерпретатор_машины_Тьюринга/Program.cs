using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Интерпретатор_машины_Тьюринга
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Глобальные обработчики необработанных исключений — критично для удалённой работы,
            // когда любой сетевой сбой не должен ронять приложение.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => HandleGlobalException(e.Exception, "ThreadException");
            AppDomain.CurrentDomain.UnhandledException += (s, e) => HandleGlobalException(e.ExceptionObject as Exception, "AppDomain");
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                HandleGlobalException(e.Exception, "TaskScheduler");
                e.SetObserved();
            };

            // Принудительно используем TLS 1.2/1.3 — некоторые серверы Let's Encrypt
            // не работают со старыми протоколами.
            try
            {
                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls12 |
                    (System.Net.SecurityProtocolType)12288 /* Tls13 */;
            }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

        private static void HandleGlobalException(Exception ex, string source)
        {
            if (ex == null) return;
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TuringInterpreter");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                string log = Path.Combine(folder, "error.log");
                File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\r\n\r\n");
            }
            catch { }

            try
            {
                MessageBox.Show(
                    "Произошла непредвиденная ошибка. Возможно, проблема со связью с сервером.\n\n" +
                    "Детали записаны в файл error.log в папке профиля пользователя.\n\n" +
                    ex.Message,
                    "Ошибка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
