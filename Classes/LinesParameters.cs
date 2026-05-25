using HikrobotCameraProcessor.FromSamples;
using MvCamCtrl.NET;
using Newtonsoft.Json;
using System.Reflection;
using System.Xml;

namespace HikrobotCameraProcessor.Classes
{
    // Базовый класс для динамических параметров метода
    //public class LinesParameters
    //{
    //    public string Name { get; set; }
    //    public string Type { get; set; } // "Int", "Float", "String", "Bool"
    //    public string Value { get; set; }
    //    public string Min { get; set; }
    //    public string Max { get; set; }
    //}

    public class LinesParameters
    {
        public string Name { get; set; }
        public string Type { get; set; } // "Int", "Float", "String", "Bool", "ComboBox"
        public string Value { get; set; }

        // Новое свойство: список доступных вариантов (используется, если Type == "ComboBox")
        public List<string> Options { get; set; } = new List<string>();
    }



    public interface ICameraMethod
    {
        string Name { get; }
        List<LinesParameters> Parameters { get; set; }
        //void Execute(uint lineIndex, object camHandle);
    }

    // Интерфейсы-маркеры для разделения списков
    public interface IMonitorMethod : ICameraMethod
    {
        // Событие для связи с формой
        event EventHandler<MonitorStateEventArgs> OnStateUpdated;

        string HealthStatus { get; } // "OK", "Error", "Stopped"
        string LastError { get; }

        void Start(uint lineIndex, MyCamera cam, object cameraLock);
        void Stop();
    }
    public interface IActionMethod : ICameraMethod
    {
        // Метод действия принимает объект камеры, чтобы выполнить команду в её контексте
        void Execute(uint lineIndex, MyCamera cam);
        void Stop();
    }

    // Класс для передачи статуса и данных от железа к UI
    public class MonitorStateEventArgs : EventArgs
    {
        public string Status { get; set; } // "Running", "Error", "Triggered"
        public string ErrorMessage { get; set; }
        public float AnalogValue { get; set; } // Для аналоговых датчиков
        public bool IsAnalog { get; set; }
    }








    public class MethodDataSnapshot
    {
        public string MethodName { get; set; }
        public List<LinesParameters> Parameters { get; set; } = new List<LinesParameters>();
    }


    public class LineConfigState
    {
        public uint LineIndex { get; set; }
        public string SelectedMonitorName { get; set; }
        public string SelectedActionName { get; set; }
        // Храним ВСЕ параметры ВСЕХ методов для этой линии, чтобы настройки не терялись при переключении
        public List<MethodDataSnapshot> SavedMethodsData { get; set; } = new List<MethodDataSnapshot>();
    }




    public class HardwareLineSnapshot
    {
        public uint LineIndex { get; set; }
        public string Mode { get; set; }
        public string Source { get; set; }
        public bool IsInverted { get; set; }
        public long DebouncerTime { get; set; }
    }

    public class AppConfigRoot
    {
        // Новый список: хранит физическое состояние регистров камеры
        public List<HardwareLineSnapshot> HardwareLines { get; set; } = new List<HardwareLineSnapshot>();

        // Старый список: хранит программные методы и их GroupBox-параметры
        public List<LineConfigState> Lines { get; set; } = new List<LineConfigState>();
    }





    public static class CameraLineHandler
    {
        // Фабрика методов для создания независимых копий под каждую линию
        //public static List<IMonitorMethod> CreateMonitorContainer() => new List<IMonitorMethod> { new TriggerMonitor(), new SoftwarePollMonitor() };
        // public static List<IActionMethod> CreateActionContainer() => new List<IActionMethod> { new BlinkAction(), new LogToNodeRedAction() };

        //получаем список методов из опроса наследников интерфейсов
        public static List<IMonitorMethod> CreateMonitorContainer()
        {
            return Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => typeof(IMonitorMethod).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .Select(t => (IMonitorMethod)Activator.CreateInstance(t))
                .ToList();
        }

        public static List<IActionMethod> CreateActionContainer()
        {
            return Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => typeof(IActionMethod).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .Select(t => (IActionMethod)Activator.CreateInstance(t))
                .ToList();
        }

        // Хранилище «живых» назначенных методов для каждой линии (индекс линии -> метод)
        public static Dictionary<uint, IMonitorMethod> ActiveMonitors = new Dictionary<uint, IMonitorMethod>();
        public static Dictionary<uint, IActionMethod> ActiveActions = new Dictionary<uint, IActionMethod>();

        // Текущий снимок состояния параметров всего пульта
        public static AppConfigRoot CurrentConfig = new AppConfigRoot();

        public static void SaveConfigToFile(string path)
        {
            string json = JsonConvert.SerializeObject(CurrentConfig, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(path, json);
        }

        public static void LoadConfigFromFile(string path)
        {
            if (!File.Exists(path)) return;
            string json = File.ReadAllText(path);
            CurrentConfig = JsonConvert.DeserializeObject<AppConfigRoot>(json);
            // Дополнительно: здесь пишется логика применения настроек к «живым» методам
        }


        public static async Task BlinkOutputLineAsync(uint lineIndex, int intervalMs, int durationSec)
        {
            AppLogger.LogInfo($"[Blink Hardware] Запущено мигание на Line {lineIndex}: интервал {intervalMs}мс, длительность {durationSec}сек.");

            DateTime startTime = DateTime.Now;
            double totalDurationMs = durationSec * 1000.0;
            bool currentState = true;

            try
            {
                // Цикл выполняется строго заданное пользователем время
                while ((DateTime.Now - startTime).TotalMilliseconds < totalDurationMs)
                {
                    // Используем наш базовый метод управления физическим пином
                    bool success = CameraManager.SetOutputLineState(lineIndex, currentState);

                    if (!success)
                    {
                        AppLogger.LogError(null, $"[Blink Hardware] Прерывание мигания на Line {lineIndex}: камера отклонила команду UserOutputValue.");
                        break;
                    }

                    // Инвертируем флаг для следующего шага (Вкл -> Выкл -> Вкл)
                    currentState = !currentState;

                    // Ждем интервал времени. Поток "засыпает", освобождая процессор
                    await Task.Delay(intervalMs);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError(ex, $"[Blink Hardware] Сбой во время циклического мигания на Line {lineIndex}");
            }
            finally
            {
                // ГАРАНТИРОВАННАЯ ЗАЩИТА: Обязательно гасим диод после окончания цикла или при ошибке,
                // чтобы реле/диод не остались в замкнутом (включенном) состоянии навсегда.
                CameraManager.SetOutputLineState(lineIndex, false);
                AppLogger.LogInfo($"[Blink Hardware] Мигание на Line {lineIndex} полностью завершено. Диод выключен.");
            }
        }

    }


    public static class ThreadManager
    {
        private static MyCamera _cam;
        private static object _cameraLock;

        // Словарь "живых" мониторов (LineIndex -> Экземпляр метода)
        public static Dictionary<uint, IMonitorMethod> ActiveMonitors = new Dictionary<uint, IMonitorMethod>();
        public static Dictionary<uint, IActionMethod> ActiveActions = new Dictionary<uint, IActionMethod>();


        // Флаг общей работы системы
        public static bool IsSystemRunning { get; private set; } = false;

        public static void InitializeContext(MyCamera camera, object cameraLock)
        {
            _cam = camera ?? throw new ArgumentNullException(nameof(camera));
            _cameraLock = cameraLock ?? throw new ArgumentNullException(nameof(cameraLock));
        }

        public static void StartAllMonitors()
        {
            if (IsSystemRunning) return;

            if (_cam == null || _cameraLock == null)
            {
                AppLogger.LogError(null, "Невозможно запустить систему: контекст камеры не инициализирован.");
                return;
            }

            IsSystemRunning = true;

            // === 1. ЗАПУСК МОНИТОРОВ (ВХОДЫ) ===
            foreach (var pair in ActiveMonitors)
            {
                uint lineIndex = pair.Key;
                IMonitorMethod monitor = pair.Value;

                if (monitor == null || monitor.Name == "Отключен") continue;

                Task.Run(() =>
                {
                    try
                    {
                        monitor.Start(lineIndex, _cam, _cameraLock);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError(ex, $"Критический сбой старта монитора на Line {lineIndex}");
                    }
                });
            }

            // === 2. ИСПРАВЛЕНИЕ: ЗАПУСК ДЕЙСТВИЙ И TCP-СЕРВЕРОВ (ВЫХОДЫ) ===
            // Теперь метод Execute() у класса TcpListenerAction гарантированно вызовется в фоне!
            foreach (var pair in ActiveActions)
            {
                uint lineIndex = pair.Key;
                IActionMethod action = pair.Value;

                if (action == null || action.Name == "Отключен") continue;

                Task.Run(() =>
                {
                    try
                    {
                        // Вызываем метод Execute, передавая индекс линии и живой объект камеры
                        action.Execute(lineIndex, _cam);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError(ex, $"Критический сбой запуска действия/сервера на Line {lineIndex}");
                    }
                });
            }

            AppLogger.LogInfo($"Все системы мониторинга ({ActiveMonitors.Count}) и TCP-серверы действий ({ActiveActions.Count}) запущены.");
        }

        /// <summary>
        /// Главный метод корректной остановки ВСЕХ процессов пульта
        /// </summary>
        public static void StopAllMonitors()
        {
            if (!IsSystemRunning) return;
            IsSystemRunning = false;

            // === 1. ОСТАНОВКА МОНИТОРОВ ===
            foreach (var pair in ActiveMonitors.Values)
            {
                try
                {
                    pair?.Stop();
                }
                catch (Exception ex)
                {
                    AppLogger.LogError(ex, "Ошибка при остановке монитора входа");
                }
            }

            // === 2. ИСПРАВЛЕНИЕ: ОСТАНОВКА ДЕЙСТВИЙ И ТСР-СЕРВЕРОВ ===
            // Принудительно вызываем Stop() у каждого действия, освобождая порты Windows
            foreach (var pair in ActiveActions.Values)
            {
                try
                {
                    pair?.Stop();
                }
                catch (Exception ex)
                {
                    AppLogger.LogError(ex, "Ошибка при остановке TCP-сервера действия вывода");
                }
            }

            AppLogger.LogInfo("Все системы мониторинга и сетевые TCP-серверы успешно остановлены.");
        }



        /// <summary>
        /// Синхронизирует выбор в UI с активными контейнерами.
        /// Передаем panelIO снаружи из формы как параметр!
        /// </summary>
        public static void SyncUiSelectionWithActiveContainers(FlowLayoutPanel panelIO)
        {
            ActiveMonitors.Clear();
            ActiveActions.Clear();

            foreach (Control row in panelIO.Controls)
            {
                if (row is FlowLayoutPanel rowPanel && rowPanel.Tag != null)
                {
                    uint lineIndex = (uint)rowPanel.Tag;

                    var cbMonitor = rowPanel.Controls.OfType<ComboBox>().FirstOrDefault(c => c.Tag?.ToString() == "MonitorSelect");
                    var cbAction = rowPanel.Controls.OfType<ComboBox>().FirstOrDefault(c => c.Tag?.ToString() == "ActionSelect");

                    // Синхронизация Монитора
                    if (cbMonitor != null && cbMonitor.SelectedItem != null)
                    {
                        string selectedMonitorName = cbMonitor.SelectedItem.ToString();
                        if (selectedMonitorName != "Отключен")
                        {
                            // Ссылаемся на ваш переименованный класс поиска/обработки CameraLineHandler
                            var availableMonitors = CameraLineHandler.CreateMonitorContainer();
                            var matchedMonitor = availableMonitors.FirstOrDefault(m => m.Name == selectedMonitorName);

                            if (matchedMonitor != null)
                            {
                                RestoreSavedParameters(lineIndex, matchedMonitor);
                                ActiveMonitors[lineIndex] = matchedMonitor;
                            }
                        }
                    }

                    // Синхронизация Действия
                    if (cbAction != null && cbAction.SelectedItem != null)
                    {
                        string selectedActionName = cbAction.SelectedItem.ToString();
                        if (selectedActionName != "Отключен")
                        {
                            var availableActions = CameraLineHandler.CreateActionContainer();
                            var matchedAction = availableActions.FirstOrDefault(a => a.Name == selectedActionName);

                            if (matchedAction != null)
                            {
                                RestoreSavedParameters(lineIndex, matchedAction);
                                ActiveActions[lineIndex] = matchedAction;
                            }
                        }
                    }
                }
            }
        }

        private static void RestoreSavedParameters(uint lineIndex, ICameraMethod methodInstance)
        {
            var lineConf = CameraLineHandler.CurrentConfig.Lines.FirstOrDefault(l => l.LineIndex == lineIndex);
            if (lineConf == null) return;

            var savedMethodSnapshot = lineConf.SavedMethodsData.FirstOrDefault(m => m.MethodName == methodInstance.Name);
            if (savedMethodSnapshot == null) return;

            foreach (var liveParam in methodInstance.Parameters)
            {
                var savedParam = savedMethodSnapshot.Parameters.FirstOrDefault(p => p.Name == liveParam.Name);
                if (savedParam != null)
                {
                    liveParam.Value = savedParam.Value;
                }
            }
        }

    }





















}
