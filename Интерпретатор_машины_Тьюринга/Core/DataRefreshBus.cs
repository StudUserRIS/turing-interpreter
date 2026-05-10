using System;

namespace Интерпретатор_машины_Тьюринга.Core
{
    /// <summary>
    /// Реализация шины обновления данных.
    ///
    /// Изменения относительно исходного варианта:
    ///   • Класс больше не является глобальным <c>internal static</c> с публичным <c>event</c>:
    ///     теперь это полноценный класс, реализующий интерфейс <see cref="IDataRefreshBus"/>.
    ///   • <see cref="event"/> объявлен с явными add/remove-аксессорами с блокировкой —
    ///     внешний код не может присвоить или сбросить весь делегат разом, инкапсуляция
    ///     соблюдена.
    ///   • Доступ к синглтону осуществляется через свойство <see cref="Instance"/>,
    ///     которое возвращает абстракцию <see cref="IDataRefreshBus"/> — подписчики
    ///     зависят от контракта, а не от конкретной реализации.
    ///   • Для обратной совместимости со старым кодом (Form1.CourseWindows.cs и др.)
    ///     сохранён фасад в корневом пространстве имён, делегирующий вызовы синглтону.
    /// </summary>
    public sealed class DataRefreshBus : IDataRefreshBus
    {
        private static readonly Lazy<DataRefreshBus> _instance =
            new Lazy<DataRefreshBus>(() => new DataRefreshBus());

        public static IDataRefreshBus Instance => _instance.Value;

        private readonly object _sync = new object();
        private Action<string, string, int> _changed;

        private DataRefreshBus() { }

        public event Action<string, string, int> Changed
        {
            add    { lock (_sync) { _changed += value; } }
            remove { lock (_sync) { _changed -= value; } }
        }

        public void Raise(string entity, string action, int id = 0)
        {
            Action<string, string, int> snapshot;
            lock (_sync) { snapshot = _changed; }
            try { snapshot?.Invoke(entity ?? "", action ?? "", id); } catch { }
        }
    }
}
