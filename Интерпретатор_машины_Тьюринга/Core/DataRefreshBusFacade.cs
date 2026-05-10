using System;

namespace Интерпретатор_машины_Тьюринга
{
    /// <summary>
    /// Фасад обратной совместимости для старого кода, который обращается к
    /// <c>DataRefreshBus.Raise(...)</c> и <c>DataRefreshBus.Changed += ...</c>
    /// напрямую (Form1.CourseWindows.cs, Form1.CheckingMode.cs и т. п.).
    ///
    /// Все вызовы делегируются единственному экземпляру
    /// <see cref="Core.DataRefreshBus"/>, который реализует контракт
    /// <see cref="Core.IDataRefreshBus"/>. Это снимает с подписчиков знание о
    /// конкретной реализации шины и одновременно сохраняет работоспособность
    /// уже существующих окон без их повторной перепиcки.
    ///
    /// Прежнее объявление "internal static class DataRefreshBus" в Form1.cs
    /// было удалено: его роль теперь выполняет полноценный класс с интерфейсом.
    /// </summary>
    internal static class DataRefreshBus
    {
        public static event Action<string, string, int> Changed
        {
            add    { Core.DataRefreshBus.Instance.Changed += value; }
            remove { Core.DataRefreshBus.Instance.Changed -= value; }
        }

        public static void Raise(string entity, string action, int id = 0)
            => Core.DataRefreshBus.Instance.Raise(entity, action, id);
    }
}
