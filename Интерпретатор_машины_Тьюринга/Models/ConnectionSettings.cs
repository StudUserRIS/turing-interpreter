using Newtonsoft.Json;
using System;
using System.IO;

namespace Интерпретатор_машины_Тьюринга
{
    /// <summary>
    /// Настройки подключения клиента к серверу.
    /// URL сервера задаётся жёстко в App.config (ApiBaseUrl / SignalRHubUrl).
    /// Файл %AppData%\TuringInterpreter\settings.json используется только
    /// для запоминания последнего введённого логина — чтобы пользователю
    /// не приходилось каждый раз вводить его заново.
    /// </summary>
    public class ConnectionSettings
    {
        public string ApiBaseUrl { get; set; } = "";
        public string SignalRHubUrl { get; set; } = "";
        public string LastLogin { get; set; } = "";

        private static string GetSettingsFolder()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "TuringInterpreter");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return folder;
        }

        private static string GetSettingsPath() => Path.Combine(GetSettingsFolder(), "settings.json");

        public static ConnectionSettings Load()
        {
            // Сначала всегда читаем актуальные URL из App.config — это «правда» для развёртывания.
            var result = new ConnectionSettings();
            try
            {
                result.ApiBaseUrl = System.Configuration.ConfigurationManager.AppSettings["ApiBaseUrl"] ?? "";
                result.SignalRHubUrl = System.Configuration.ConfigurationManager.AppSettings["SignalRHubUrl"] ?? "";
            }
            catch { }

            // Если в App.config адресов нет — используем дефолтные значения для localhost-запуска.
            if (string.IsNullOrWhiteSpace(result.ApiBaseUrl))
                result.ApiBaseUrl = "http://localhost:5007/api/";
            if (string.IsNullOrWhiteSpace(result.SignalRHubUrl))
                result.SignalRHubUrl = "http://localhost:5007/hubs/notifications";

            // Из локального файла берём только последний введённый логин (для удобства пользователя).
            try
            {
                string path = GetSettingsPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var s = JsonConvert.DeserializeObject<ConnectionSettings>(json);
                    if (s != null && !string.IsNullOrEmpty(s.LastLogin))
                        result.LastLogin = s.LastLogin;
                }
            }
            catch { }

            return result;
        }

        public void Save()
        {
            try
            {
                // Сохраняем только то, что относится к пользователю (логин), а не URL сервера.
                string path = GetSettingsPath();
                var toSave = new ConnectionSettings { LastLogin = this.LastLogin };
                File.WriteAllText(path, JsonConvert.SerializeObject(toSave, Formatting.Indented));
            }
            catch { }
        }
    }
}
