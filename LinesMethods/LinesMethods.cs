using HikrobotCameraProcessor.Classes;
using HikrobotCameraProcessor.FromSamples;
using MvCamCtrl.NET;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;


namespace HikrobotCameraProcessor.LineMethods
{

    // ========================================================================
    // РЕАЛИЗАЦИЯ МЕТОДОВ ДЕЙСТВИЙ (ACTIONS)
    // ========================================================================

    public class BlinkAction : IActionMethod
    {
        private NLog.ILogger _fileLogger;
        public string Name => "LED Blink";

        public List<LinesParameters> Parameters { get; set; } = new List<LinesParameters>
    {
        new LinesParameters { Name = "Интервал (мс)", Type = "Int", Value = "250" },
        new LinesParameters { Name = "Длительность (сек)", Type = "Int", Value = "3" }
    };

        public async void Execute(uint lineIndex, MyCamera cam)
        {
            _fileLogger = NLog.LogManager.GetLogger($"{Name}_Line{lineIndex}");
            try
            {
                // Берем значения из индексов массива параметров
                int interval = int.Parse(Parameters[0].Value);
                int duration = int.Parse(Parameters[1].Value);

                _fileLogger.Info($"Запущено асинхронное мигание на Line {lineIndex} с интервалом {interval}мс");

                // Здесь будет ваша логика мигания через переданный cam...
            }
            catch (Exception ex)
            {
                _fileLogger.Error(ex, $"Ошибка выполнения BlinkAction на Line {lineIndex}");
            }
        }

        public void Stop()
        {
            //заглушка для интерфейса, реализовывать в данном случае не нужно
        }
    }




    // ========================================================================
    // УНИВЕРСАЛЬНЫЙ МОНИТОРИНГ (MONITORS)
    // ========================================================================
    // подписываемся на события (Callback-функции) изменения состояния шины.
    // Соответственно, класс монитора — это ПОДПИСЧИК на Callback.

    public class TriggerMonitor : IMonitorMethod
    {
        private NLog.ILogger _fileLogger;
        public string Name => "Фото по кнопке, отправка имени по TCP";

        // Параметры метода, которые автоматически отобразятся в GroupBox
        public List<LinesParameters> Parameters { get; set; } = new List<LinesParameters>
        {
            new LinesParameters { Name = "DelayMs", Type = "Int", Value = "0" },
            new LinesParameters { Name = "Image folder", Type = "String", Value = "D:\\ImageToWork" },
            new LinesParameters { Name = "IP сервера", Type = "String", Value = "127.0.0.1" },
            new LinesParameters { Name = "Порт", Type = "Int", Value = "2000" }
        };

        private KeepAliveTcpClient _tcpClient;
        private CancellationTokenSource _cts;

        // --- РЕАЛИЗАЦИЯ НОВЫХ СВОЙСТВ ИНТЕРФЕЙСА ---
        public event EventHandler<MonitorStateEventArgs> OnStateUpdated;
        public string HealthStatus { get; private set; } = "Stopped";
        public string LastError { get; private set; } = string.Empty;

        private MyCamera _cam;
        private uint _lineIndex;
        private object _cameraLock;

        // Жесткая ссылка на делегат, чтобы Garbage Collector не удалил его во время работы
        private MyCamera.cbOutputExdelegate _callbackDelegate;

        public void Start(uint lineIndex, MyCamera cam, object cameraLock)
        {
            _cam = cam;
            _lineIndex = lineIndex;
            _cameraLock = cameraLock;
            _callbackDelegate = OnLineSignalReceived;
            _cts = new CancellationTokenSource();

            _fileLogger = NLog.LogManager.GetLogger($"{Name}_Line{_lineIndex}");

            // Читаем сетевые параметры из GroupBox
            string ip = Parameters.FirstOrDefault(p => p.Name == "IP сервера")?.Value ?? "127.0.0.1";
            if (!int.TryParse(Parameters.FirstOrDefault(p => p.Name == "Порт")?.Value, out int port)) port = 2000;

            // 1. Инициализируем и запускаем службу постоянного keep-alive клиента
            _tcpClient = new KeepAliveTcpClient(ip, port, _fileLogger);
            _tcpClient.Start(_cts.Token);

            lock (_cameraLock)
            {
                try
                {
                    _cam.MV_CC_SetEnumValue_NET("TriggerMode", 1);
                    _cam.MV_CC_SetEnumValue_NET("TriggerSource", _lineIndex);

                    int nRet = _cam.MV_CC_RegisterImageCallBackEx_NET(_callbackDelegate, IntPtr.Zero);
                    if (nRet == MyCamera.MV_OK)
                    {
                        _cam.MV_CC_StartGrabbing_NET();
                        HealthStatus = "OK";
                        LastError = string.Empty;
                        _fileLogger.Info($"Монитор триггера на Line {_lineIndex} успешно запущен.");
                        OnStateUpdated?.Invoke(this, new MonitorStateEventArgs { Status = "Running" });
                    }
                    else throw new Exception($"Ошибка регистрации Callback: 0x{nRet:X8}");
                }
                catch (Exception ex)
                {
                    HealthStatus = "Error";
                    LastError = ex.Message;
                    _fileLogger.Error(ex, "Ошибка старта монитора");
                    _tcpClient?.Stop(); // Глушим сокет при ошибке старта SDK
                    OnStateUpdated?.Invoke(this, new MonitorStateEventArgs { Status = "Error", ErrorMessage = ex.Message });
                    throw;
                }
            }
        }

        public void Stop()
        {
            _cts?.Cancel();

            // 2. Глушим постоянный клиент при остановке пульта формы
            if (_tcpClient != null)
            {
                _tcpClient.Stop();
                _tcpClient = null;
            }

            if (_cam != null)
            {
                lock (_cameraLock)
                {
                    try
                    {
                        _cam.MV_CC_StopGrabbing_NET();
                        _cam.MV_CC_RegisterImageCallBackEx_NET(null, IntPtr.Zero);
                    }
                    catch { }
                }
            }

            _callbackDelegate = null;
            _cts?.Dispose();
            _cts = null;
            HealthStatus = "Stopped";
            _fileLogger.Info($"Монитор на Line {_lineIndex} полностью остановлен.");
            OnStateUpdated?.Invoke(this, new MonitorStateEventArgs { Status = "Stopped" });
        }

        // ЭТОТ МЕТОД ВЫЗОВЕТ ДРАЙВЕР КАМЕРЫ, КОГДА НА ПРОВОД ПРИДЕТ СИГНАЛ С ДАТЧИКА
        private void OnLineSignalReceived(IntPtr pData, ref MyCamera.MV_FRAME_OUT_INFO_EX pFrameInfo, IntPtr pUser)
        {
            try
            {
                _fileLogger.Info($"[Hardware Trigger] Аппаратный импульс на Линии {_lineIndex}! Обработка запущена.");

                // 1. ВАЛИДАЦИЯ ЗАДЕРЖКИ: Проверяем число на адекватность
                if (!MethodHelper.TryGetDelayMs(Parameters, "DelayMs", _lineIndex, out int delayMs))
                {
                    HealthStatus = "Error";
                    LastError = "Ошибка значения задержки";
                    OnStateUpdated?.Invoke(this, new MonitorStateEventArgs { Status = "Error", ErrorMessage = LastError });
                    return;
                }

                // 3. Мгновенно отправляем сигнал "Вспышки" на форму (PictureBox моргнет)
                OnStateUpdated?.Invoke(this, new MonitorStateEventArgs { Status = "Triggered" });

                // 4. ВАЛИДАЦИЯ ДИРЕКТОРИИ: Наш старый чистый валидатор папок
                string rawDirectory = Parameters.FirstOrDefault(p => p.Name == "LogFolder")?.Value ?? "C:\\Logs";
                if (!MethodHelper.TryPrepareDirectory(Parameters, "Image folder", _lineIndex, out string targetDirectory))
                {
                    HealthStatus = "Error";
                    LastError = "Ошибка директории сохранения";
                    OnStateUpdated?.Invoke(this, new MonitorStateEventArgs { Status = "Error", ErrorMessage = LastError });
                    return;
                }

                // 2. ОТРАБОТКА ЗАДЕРЖКИ: Если задержка задана — приостанавливаем поток перед фиксацией кадра
                if (delayMs > 0)
                {
                    _fileLogger.Info($"[Hardware Trigger] Применение задержки: ожидание {delayMs} мс до сохранения кадра...");
                    Thread.Sleep(delayMs);
                }

                // 5. Формируем пути
                string fileName = $"trigger_img_line{_lineIndex}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
                string fullImagePath = Path.Combine(targetDirectory, fileName);

                // 6. Подготовка параметров сохранения
                MyCamera.MV_SAVE_IMG_TO_FILE_PARAM stSaveParam = new MyCamera.MV_SAVE_IMG_TO_FILE_PARAM();
                stSaveParam.enImageType = MyCamera.MV_SAVE_IAMGE_TYPE.MV_Image_Png;
                stSaveParam.enPixelType = pFrameInfo.enPixelType;
                stSaveParam.nWidth = pFrameInfo.nWidth;
                stSaveParam.nHeight = pFrameInfo.nHeight;
                stSaveParam.pData = pData;
                stSaveParam.nDataLen = pFrameInfo.nFrameLen;
                stSaveParam.pImagePath = fullImagePath;

                int saveRet;
                lock (_cameraLock)
                {
                    saveRet = _cam.MV_CC_SaveImageToFile_NET(ref stSaveParam);
                }

                if (saveRet == MyCamera.MV_OK)
                {
                    _fileLogger.Info($"Фото успешно сохранено на диск: {fullImagePath}");
                    HealthStatus = "OK";
                    LastError = string.Empty;

                    // === ШАГ 5: ПОЛНОСТЬЮ АВТОНОМНЫЙ ВЫЗОВ TCP ОТПРАВКИ ===
                    // Передаем только имя файла. Метод сам вытащит IP/Порт из Parameters и сделает всё в фоне
                    _tcpClient?.SendMessage(fileName);
                }
                else
                {
                    throw new Exception($"SDK не смог упаковать буфер Callback в файл. Код ошибки железа: 0x{saveRet:X8}");
                }
            }
            catch (Exception ex)
            {
                HealthStatus = "Error";
                LastError = ex.Message;
                OnStateUpdated?.Invoke(this, new MonitorStateEventArgs { Status = "Error", ErrorMessage = ex.Message });
                _fileLogger.Error(ex, $"Критический сбой обработки Callback кадра на Line {_lineIndex}");
            }
        }

    }

    public class SoftwarePollMonitor : IMonitorMethod
    {
        private NLog.ILogger _fileLogger;
        public string Name => "Программный курок";

        public List<LinesParameters> Parameters { get; set; } = new List<LinesParameters>
        {
            new LinesParameters { Name = "Директория сохранения изображения", Type = "String", Value = "D:\\ImageToWork" },
            new LinesParameters { Name = "Интервал опроса (мс)", Type = "Int", Value = "100" }
        };

        public event EventHandler<MonitorStateEventArgs> OnStateUpdated;
        public string HealthStatus { get; private set; } = "Stopped";
        public string LastError { get; private set; } = string.Empty;

        private MyCamera _cam;
        private uint _lineIndex;
        private object _cameraLock;

        // Токен для безопасной остановки фонового потока опроса
        private CancellationTokenSource _cts;

        public void Start(uint lineIndex, MyCamera cam, object cameraLock)
        {
            _fileLogger = NLog.LogManager.GetLogger($"{Name}_Line{lineIndex}");

            _cam = cam;
            _lineIndex = lineIndex;
            _cameraLock = cameraLock;

            _cts = new CancellationTokenSource();
            HealthStatus = "OK";
            LastError = string.Empty;

            _fileLogger.Info($"Монитор на Line {_lineIndex} запущен. Инициализация потока опроса...");

            // Запускаем бесконечный фоновый цикл, который будет жить сам по себе,
            // пока пользователь не нажмет кнопку "Остановить"
            Task.Run(() => PollCameraLineLoop(_cts.Token));

            OnStateUpdated?.Invoke(this, new MonitorStateEventArgs { Status = "Running" });
        }

        public void Stop()
        {
            // Сигнализируем фоновому потоку, что нужно завершить работу
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            HealthStatus = "Stopped";
            _fileLogger.Info($"Монитор на Line {_lineIndex} успешно остановлен.");
            OnStateUpdated?.Invoke(this, new MonitorStateEventArgs { Status = "Stopped" });
        }

        /// <summary>
        /// Фоновый цикл, который опрашивает физический статус линии через SDK
        /// </summary>
        private async Task PollCameraLineLoop(CancellationToken token)
        {
            // Читаем интервал опроса из параметров GroupBox (защита: если кривое число — берем 100мс)
            var intervalParam = Parameters.FirstOrDefault(p => p.Name == "Интервал опроса (мс)");
            int pollInterval = int.TryParse(intervalParam?.Value, out int res) ? res : 100;


            bool lastLineStatus = false; // Предыдущее состояние линии, чтобы ловить именно момент нажатия (Edge)

            while (!token.IsCancellationRequested)
            {
                try
                {
                    bool currentLineStatus = false;

                    lock (_cameraLock)
                    {
                        if (_cam == null) break;

                        // 1. Выбираем линию, которую хотим проверить
                        _cam.MV_CC_SetEnumValue_NET("LineSelector", _lineIndex);

                        // 2. Читаем её текущий физический статус (есть ток на входе или нет)
                        // Регистр "LineStatus" возвращает true, если на пин подан сигнал
                        _cam.MV_CC_GetBoolValue_NET("LineStatus", ref currentLineStatus);
                    }

                    // 3. ЛОГИКА ДЕТЕКЦИИ ФРОНТА СИГНАЛА (Сигнал изменился с false на true)
                    if (currentLineStatus && !lastLineStatus)
                    {
                        _fileLogger.Info($"[Poll Loop] Зафиксирован программный триггер на Line {_lineIndex}! Вызываем съемку.");

                        ForceCaptureAndSave();
                    }

                    lastLineStatus = currentLineStatus;

                    // Пауза между опросами, чтобы не перегружать процессор
                    await Task.Delay(pollInterval, token);
                }
                catch (TaskCanceledException)
                {
                    // Нормальная ситуация при остановке мониторинга
                    break;
                }
                catch (Exception ex)
                {
                    HealthStatus = "Error";
                    LastError = ex.Message;
                    OnStateUpdated?.Invoke(this, new MonitorStateEventArgs { Status = "Error", ErrorMessage = ex.Message });
                    _fileLogger.Error(ex, $"Ошибка в цикле опроса Line {_lineIndex}");
                    break;
                }
            }
        }


        public void ForceCaptureAndSave()
        {
            if (_cam == null || HealthStatus == "Stopped") return;

            // 1. ВАЛИДАЦИЯ ЗАДЕРЖКИ: Проверяем число на адекватность
            if (!MethodHelper.TryGetDelayMs(Parameters, "DelayMs", _lineIndex, out int delayMs))
            {
                HealthStatus = "Error";
                LastError = "Ошибка значения задержки";
                OnStateUpdated?.Invoke(this, new MonitorStateEventArgs { Status = "Error", ErrorMessage = LastError });
                return;
            }

            // 2. ВАЛИДАЦИЯ ДИРЕКТОРИИ: Проверяем путь и создаем папку
            if (!MethodHelper.TryPrepareDirectory(Parameters, "Директория сохранения изображения", _lineIndex, out string targetDirectory))
            {
                HealthStatus = "Error";
                LastError = "Ошибка директории сохранения";
                OnStateUpdated?.Invoke(this, new MonitorStateEventArgs { Status = "Error", ErrorMessage = LastError });
                return;
            }

            // 3. ОТРАБОТКА ЗАДЕРЖКИ: Если всё валидно — делаем паузу конвейера перед отправкой команды курка
            if (delayMs > 0)
            {
                _fileLogger.Info($"[Software Trigger] Применение задержки: ожидание {delayMs} мс перед программным снимком...");
                Thread.Sleep(delayMs);
            }

            // 4. Мгновенно отправляем сигнал "Вспышки" на форму (PictureBox подмигнет салатовым)
            OnStateUpdated?.Invoke(this, new MonitorStateEventArgs { Status = "Triggered" });

            // 5. Генерируем имя файла
            string fileName = $"img_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            string fullImagePath = Path.Combine(targetDirectory, fileName);

            IntPtr pData = IntPtr.Zero;

            // Защищаем обращение к камере через lock
            lock (_cameraLock)
            {
                try
                {
                    int nRet;
                    _cam.MV_CC_StopGrabbing_NET();
                    _cam.MV_CC_SetEnumValue_NET("TriggerMode", 1); // On
                    _cam.MV_CC_SetEnumValue_NET("TriggerSource", 7); // Software

                    nRet = _cam.MV_CC_StartGrabbing_NET();
                    if (nRet != MyCamera.MV_OK) throw new Exception($"Старт Grabbing провален: 0x{nRet:X8}");

                    nRet = _cam.MV_CC_SetCommandValue_NET("TriggerSoftware");
                    if (nRet != MyCamera.MV_OK) throw new Exception($"Команда TriggerSoftware провалена: 0x{nRet:X8}");

                    MyCamera.MVCC_INTVALUE stPayload = new MyCamera.MVCC_INTVALUE();
                    _cam.MV_CC_GetIntValue_NET("PayloadSize", ref stPayload);

                    pData = Marshal.AllocHGlobal((int)stPayload.nCurValue);
                    MyCamera.MV_FRAME_OUT_INFO_EX stFrameInfo = new MyCamera.MV_FRAME_OUT_INFO_EX();

                    nRet = _cam.MV_CC_GetOneFrameTimeout_NET(pData, stPayload.nCurValue, ref stFrameInfo, 2000);

                    if (nRet == MyCamera.MV_OK)
                    {
                        MyCamera.MV_SAVE_IMG_TO_FILE_PARAM stSaveParam = new MyCamera.MV_SAVE_IMG_TO_FILE_PARAM();
                        stSaveParam.enImageType = MyCamera.MV_SAVE_IAMGE_TYPE.MV_Image_Png;
                        stSaveParam.enPixelType = stFrameInfo.enPixelType;
                        stSaveParam.nWidth = stFrameInfo.nWidth;
                        stSaveParam.nHeight = stFrameInfo.nHeight;
                        stSaveParam.pData = pData;
                        stSaveParam.nDataLen = stFrameInfo.nFrameLen;
                        stSaveParam.pImagePath = fullImagePath;

                        int saveRet = _cam.MV_CC_SaveImageToFile_NET(ref stSaveParam);

                        if (saveRet == MyCamera.MV_OK)
                        {
                            _fileLogger.Info($"Фото успешно сохранено: {fullImagePath}");
                            HealthStatus = "OK";
                            LastError = string.Empty;

                            if (CameraLineHandler.ActiveActions.TryGetValue(_lineIndex, out IActionMethod assignedAction))
                            {
                                //if (assignedAction is LogToNodeRedAction nodeRedAction)
                                //{
                                //    nodeRedAction.ExecuteWithFileName(_lineIndex, _cam, fileName);
                                //}
                                //else
                                //{
                                //    assignedAction.Execute(_lineIndex, _cam);
                                //}
                            }
                        }
                        else throw new Exception($"Ошибка сохранения файла: 0x{saveRet:X8}");
                    }
                    else throw new Exception($"Кадр не получен из буфера по таймауту: 0x{nRet:X8}");
                }
                catch (Exception e)
                {
                    HealthStatus = "Error";
                    LastError = e.Message;
                    OnStateUpdated?.Invoke(this, new MonitorStateEventArgs { Status = "Error", ErrorMessage = e.Message });
                    _fileLogger.Error(e, $"Критический сбой ForceCaptureAndSave на Line {_lineIndex}");
                }
                finally
                {
                    if (pData != IntPtr.Zero) Marshal.FreeHGlobal(pData);
                }
            }
        }


    }



    public class TcpListenerActionServer : IActionMethod
    {
        private NLog.ILogger _fileLogger;
        public string Name => "Получение команды по TCP (Сервер)";

        public List<LinesParameters> Parameters { get; set; }

        private CancellationTokenSource _cts;
        private uint _lineIndex;
        private KeepAliveTcpServer _tcpServer;

        public TcpListenerActionServer()
        {
            // Динамически получаем все локальные IPv4 адреса этого компьютера
            var localIpList = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName())
                .AddressList
                .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(ip => ip.ToString())
                .ToList();

            //if (localIpList.Count == 0) 
            localIpList.Add("127.0.0.1");

            Parameters = new List<LinesParameters>
        {
            // Переключаем тип на "ComboBox" и передаем список найденных IP-адресов
            new LinesParameters { Name = "IP сервера прослушивания", Type = "ComboBox", Value = localIpList[0], Options = localIpList },
            new LinesParameters { Name = "Порт прослушивания", Type = "Int", Value = "3000" },
            new LinesParameters { Name = "TCP сообщение", Type = "String", Value = "blink" },
            new LinesParameters { Name = "Интервал мигания (мс)", Type = "Int", Value = "200" },
            new LinesParameters { Name = "Длительность (сек)", Type = "Int", Value = "3" }
        };
        }

        public void Execute(uint lineIndex, MyCamera cam)
        {
            _lineIndex = lineIndex;
            _fileLogger = NLog.LogManager.GetLogger($"{Name}_Line{lineIndex}");
            _cts = new CancellationTokenSource();

            // Читаем выбранный IP-адрес и порт из параметров
            string ipStr = Parameters.FirstOrDefault(p => p.Name == "IP сервера прослушивания")?.Value ?? "127.0.0.1";
            var portParam = Parameters.FirstOrDefault(p => p.Name == "Порт прослушивания");
            if (!int.TryParse(portParam?.Value, out int port)) port = 3000;

            // Передаем КОНКРЕТНЫЙ IP-адрес в конструктор сервера
            _tcpServer = new KeepAliveTcpServer(ipStr, port, _fileLogger);

            // Подписываемся на события и запускаем
            _tcpServer.OnCommandReceived += Server_OnCommandReceived;
            _tcpServer.Start(_cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            if (_tcpServer != null)
            {
                _tcpServer.OnCommandReceived -= Server_OnCommandReceived;
                _tcpServer.Stop();
                _tcpServer = null;
            }
            _cts?.Dispose();
            _cts = null;
        }

        private void Server_OnCommandReceived(string command)
        {
            var targetMsg = Parameters.FirstOrDefault(p => p.Name == "TCP сообщение")?.Value ?? "blink";

            if (command == targetMsg.ToLower())
            {
                int interval = int.TryParse(Parameters.FirstOrDefault(p => p.Name == "Интервал мигания (мс)")?.Value, out int i) ? i : 200;
                int duration = int.TryParse(Parameters.FirstOrDefault(p => p.Name == "Длительность (сек)")?.Value, out int d) ? d : 3;

                _fileLogger.Info($"[Action Context] Команда совпала. Мигаем выходом...");
                _ = CameraLineHandler.BlinkOutputLineAsync(_lineIndex, interval, duration);
            }
        }
    }







    public class TcpClientAction : IActionMethod
    {
        private NLog.ILogger _fileLogger;
        public string Name => "Получение команды по TCP (Клиент)";

        public List<LinesParameters> Parameters { get; set; }

        private CancellationTokenSource _cts;
        private uint _lineIndex;

        private KeepAliveTcpClient _tcpClient;

        public TcpClientAction()
        {
            // Динамически получаем все локальные IPv4 адреса этого компьютера
            var localIpList = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName())
                .AddressList
                .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(ip => ip.ToString())
                .ToList();

            localIpList.Add("127.0.0.1");

            Parameters = new List<LinesParameters>
            {
                new LinesParameters { Name = "IP TCP сервера", Type = "ComboBox", Value = localIpList[0], Options = localIpList },
                new LinesParameters { Name = "Порт Сервера", Type = "Int", Value = "3000" },
                new LinesParameters { Name = "TCP сообщение", Type = "String", Value = "blink" },
                new LinesParameters { Name = "Интервал мигания (мс)", Type = "Int", Value = "200" },
                new LinesParameters { Name = "Длительность (сек)", Type = "Int", Value = "3" }
            };
        }

        public void Execute(uint lineIndex, MyCamera cam)
        {
            _lineIndex = lineIndex;
            _fileLogger = NLog.LogManager.GetLogger($"{Name}_Line{lineIndex}");
            _cts = new CancellationTokenSource();

            // Читаем выбранный IP-адрес и порт Node-RED из параметров
            string ipStr = Parameters.FirstOrDefault(p => p.Name == "IP сервера TCP")?.Value ?? "127.0.0.1";
            var portParam = Parameters.FirstOrDefault(p => p.Name == "Порт TCP");
            if (!int.TryParse(portParam?.Value, out int port)) port = 3000;

            _tcpClient = new KeepAliveTcpClient(ipStr, port, _fileLogger);

            // ИСПРАВЛЕНИЕ: Подписываемся на событие получения команды по постоянному каналу связи
            _tcpClient.OnCommandReceived += Client_OnCommandReceived;

            // ИСПРАВЛЕНИЕ: Запускаем исходящий поток удержания связи
            _tcpClient.Start(_cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            if (_tcpClient != null)
            {
                // ИСПРАВЛЕНИЕ: Отписываемся и глушим сокет клиента
                _tcpClient.OnCommandReceived -= Client_OnCommandReceived;
                _tcpClient.Stop();
                _tcpClient = null;
            }
            _cts?.Dispose();
            _cts = null;
        }

        private void Client_OnCommandReceived(string command)
        {
            var targetMsg = Parameters.FirstOrDefault(p => p.Name == "TCP сообщение")?.Value ?? "blink";

            if (command == targetMsg.ToLower())
            {
                int interval = int.TryParse(Parameters.FirstOrDefault(p => p.Name == "Интервал мигания (мс)")?.Value, out int i) ? i : 200;
                int duration = int.TryParse(Parameters.FirstOrDefault(p => p.Name == "Длительность (сек)")?.Value, out int d) ? d : 3;

                _fileLogger.Info($"[Action Context] Команда совпала. Мигаем выходом...");

                // Наше проверенное легальное мигание через инвертор
                _ = CameraLineHandler.BlinkOutputLineAsync(_lineIndex, interval, duration);
            }
        }
    }





}




