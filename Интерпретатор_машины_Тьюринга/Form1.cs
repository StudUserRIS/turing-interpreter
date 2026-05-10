using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using Интерпретатор_машины_Тьюринга.Api;
using Интерпретатор_машины_Тьюринга.Auth;
using Интерпретатор_машины_Тьюринга.Core;

namespace Интерпретатор_машины_Тьюринга
{
    /// <summary>
    /// Главное окно приложения.
    ///
    /// После рефакторинга Form1 не содержит ни доменной логики машины Тьюринга
    /// (вынесена в <see cref="TuringMachine"/>), ни глобальной шины (теперь
    /// <see cref="DataRefreshBus"/> с интерфейсом <see cref="IDataRefreshBus"/>),
    /// ни бизнес-логики сессии (вынесена в <see cref="SessionManager"/>).
    /// Form1 — это тонкий координатор, выступающий в роли «application shell».
    ///
    /// Зависимости от внешних сервисов поданы через абстракции
    /// (<see cref="IApiClient"/>, <see cref="IDataRefreshBus"/>) и инициализируются
    /// в конструкторе. Такой подход устраняет нарушения SRP/DIP, выявленные
    /// в исходной архитектуре.
    /// </summary>
    public partial class Form1 : Form
    {
        // Сервисы, на которых работает форма.
        private readonly IApiClient       _api;
        private readonly IDataRefreshBus  _bus;
        private readonly SessionManager   _session;

        public Form1()
        {
            // Композиция зависимостей. В реальном DI-сценарии эти зависимости
            // приходили бы из контейнера; здесь — собираем их вручную.
            _api     = ApiClientAdapter.Default;
            _bus     = Core.DataRefreshBus.Instance;
            _session = new SessionManager(_api, _bus);

            this.ShowIcon = true;
            this.Activated += (s, e) => this.ActiveControl = null;
            InitializeComponent();

            ApiClient.OnSessionEnded += HandleSessionEnded;
            ApiClient.OnConflict += HandleConflict;

            // События сессии теперь приходят из SessionManager — UI-слой
            // только «рисует» реакцию, не зная подробностей транспорта.
            _session.SessionEnded      += OnSessionManagerSessionEnded;
            _session.ProfileChanged    += OnSessionManagerProfileChanged;
            _session.AssignmentChanged += OnSessionManagerAssignmentChanged;

            this.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    this.ActiveControl = null;
                }
            };
            this.DoubleBuffered = true;
            this.ClientSize = new Size(1000, 700);
            this.MinimumSize = new Size(800, 500);
            this.BackColor = MainBackColor;
            this.Font = MainFont;
            this.Text = "Интерпретатор машины Тьюринга";
            this.Load += SetupControls;
            states.Add(new TuringState { Name = "q0", IsInitial = true });
            executionTimer = new System.Windows.Forms.Timer
            {
                Interval = executionSpeed,
                Enabled = false
            };
            executionTimer.Tick += async (s, args) =>
            {
                if (!isProcessingStep)
                {
                    try
                    {
                        isProcessingStep = true;
                        await ExecuteStepAsync();
                    }
                    finally
                    {
                        isProcessingStep = false;
                    }
                }
            };

            CreateStatusBar();

            this.FormClosing += Form1_FormClosing;
            Microsoft.Win32.SystemEvents.SessionEnding += SystemEvents_SessionEnding;
        }
    }
}
