using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HikrobotCameraProcessor.Classes
{
    public class KeepAliveTcpServer
    {
        public event Action<string> OnCommandReceived;

        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private readonly List<TcpClient> _connectedClients = new List<TcpClient>();
        private readonly NLog.ILogger _logger;
        private readonly string _ipStr; // Сохраняем строку IP
        private readonly int _port;

        public KeepAliveTcpServer(string ipStr, int port, NLog.ILogger logger)
        {
            _ipStr = ipStr;
            _port = port;
            _logger = logger ?? NLog.LogManager.GetCurrentClassLogger();
        }

        public void Start(CancellationToken externalToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            _logger.Info($"[TCP Server] Подготовка службы прослушивания на адресе {_ipStr}:{_port}...");

            Task.Run(() => StartServerLoop(_cts.Token));
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();

                lock (_connectedClients)
                {
                    foreach (var client in _connectedClients)
                    {
                        try { client?.Close(); } catch { }
                    }
                    _connectedClients.Clear();
                }

                _listener?.Stop();
                _cts?.Dispose();
                _cts = null;
                _logger.Info($"[TCP Server] Порт {_port} успешно закрыт, сетевые ресурсы освобождены.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TCP Server] Ошибка при остановке сервера на порту {_port}");
            }
        }

        private async Task StartServerLoop(CancellationToken token)
        {
            try
            {
                // ПАРСИМ СТРОКУ В НАДЁЖНЫЙ ОБЪЕКТ IPAddress
                if (!IPAddress.TryParse(_ipStr, out IPAddress targetIp))
                {
                    _logger.Error($"[TCP Server] Не удалось распарсить IP-адрес: {_ipStr}. Откат на локальный хост.");
                    targetIp = IPAddress.Loopback; // 127.0.0.1 в случае сбоя
                }

                // ИСПРАВЛЕНИЕ: Привязываем сокет строго к выбранной сетевой карте
                _listener = new TcpListener(targetIp, _port);
                _listener.Start();
                _logger.Info($"[TCP Server] Сервер запущен. Адрес {targetIp}:{_port} открыт для Клиента(ов).");

                while (!token.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync();

                    lock (_connectedClients)
                    {
                        _connectedClients.Add(client);
                    }

                    _ = Task.Run(() => HandleClientCommunication(client, token));
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TCP Server] Критическая ошибка запуска сокета на {_ipStr}:{_port}");
            }
        }


        // ========================================================================
        // ИЗОЛИРОВАННЫЙ КАНАЛ СВЯЗИ С АВТО-ВОССТАНОВЛЕНИЕМ ПОСЛЕ ПАДЕНИЙ
        // ========================================================================
        private async Task HandleClientCommunication(TcpClient client, CancellationToken token)
        {
            string clientInfo = "Неизвестный хост";
            try { clientInfo = client.Client.RemoteEndPoint.ToString(); } catch { }

            _logger.Info($"[TCP Server] Клиент ({clientInfo}) подключился по keep-alive каналу.");

            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                byte[] buffer = new byte[1024];

                try
                {
                    // Бесконечный цикл чтения потока для конкретного подключенного клиента
                    while (!token.IsCancellationRequested && client.Connected)
                    {
                        // Поток асинхронно засыпает тут, не нагружая CPU, в ожидании новых байт от клиентов
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);

                        // Если bytesRead == 0, значит клиент TCP корректно закрыл сокет (например, при деплое)
                        if (bytesRead == 0)
                        {
                            _logger.Info($"[TCP Server] Клиент ({clientInfo}) штатно закрыл соединение.");
                            break;
                        }

                        // Декодируем и очищаем команду
                        string command = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim().ToLower();
                        _logger.Info($"[TCP Server] Получена строка от ({clientInfo}): '{command}'");

                        // ГЕНЕРИРУЕМ СОБЫТИЕ: Выкидываем строку наружу. 
                        // Класс, который нас вызвал, сам разберется, что с ней делать
                        OnCommandReceived?.Invoke(command);
                    }
                }
                catch (Exception ex)
                {
                    // Сюда мы попадем, если клиент упал или оборвалась сеть (Connection reset)
                    _logger.Warn($"[TCP Server] Соединение с клиентом ({clientInfo}) аварийно потеряно. Ошибка: {ex.Message}");
                }
                finally
                {
                    lock (_connectedClients)
                    {
                        _connectedClients.Remove(client);
                    }
                    _logger.Info($"[TCP Server] Поток keep-alive для клиента ({clientInfo}) завершен. Сервер ждет новых подключений.");
                }
            }
        }

    }





    public class KeepAliveTcpClient
    {
        public event Action<string> OnCommandReceived;

        private readonly string _ip;
        private readonly int _port;
        private readonly NLog.ILogger _logger;

        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private bool _isConnected = false;

        // Потокобезопасная очередь для сообщений, если они прилетели в момент дисконнекта
        private readonly ConcurrentQueue<string> _sendQueue = new ConcurrentQueue<string>();
        private readonly AutoResetEvent _sendSignal = new AutoResetEvent(false);

        public KeepAliveTcpClient(string ip, int port, NLog.ILogger logger)
        {
            _ip = ip;
            _port = port;
            _logger = logger ?? NLog.LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Запускает фоновые потоки удержания связи и отправки данных
        /// </summary>
        public void Start(CancellationToken externalToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);

            // Запускаем два параллельных фоновых потока: один на реконнект, другой на отправку
            Task.Run(() => ConnectionLoop(_cts.Token));
            Task.Run(() => SendingLoop(_cts.Token));
        }

        /// <summary>
        /// Добавляет сообщение в очередь на отправку. Метод мгновенный и не блокирует вызывающий поток.
        /// </summary>
        public void SendMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            _sendQueue.Enqueue(message);
            _sendSignal.Set(); // Будим рабочий поток отправки
        }

        /// <summary>
        /// Корректно закрывает сокеты и останавливает потоки реконнекта
        /// </summary>
        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _sendSignal.Set(); // Освобождаем поток отправки из ожидания

                CloseConnection();

                _cts?.Dispose();
                _cts = null;
                _logger.Info($"[TCP Client] Служба keep-alive клиента для {_ip}:{_port} успешно остановлена.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TCP Client] Ошибка при остановке клиента");
            }
        }

        // БЕСКОНЕЧНЫЙ ЦИКЛ ПОДДЕРЖАНИЯ СВЯЗИ (АВТО-РЕКОННЕКТ)
        private async Task ConnectionLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (!_isConnected)
                {
                    try
                    {
                        _logger.Info($"[TCP Client] Попытка установить постоянное соединение с сервером ({_ip}:{_port})...");

                        _client = new TcpClient();

                        // Асинхронно пытаемся подключиться
                        var connectTask = _client.ConnectAsync(_ip, _port);

                        // Ограничиваем таймаут попытки подключения 3 секундами
                        if (await Task.WhenAny(connectTask, Task.Delay(3000, token)) == connectTask && _client.Connected)
                        {
                            _stream = _client.GetStream();
                            _isConnected = true;
                            _logger.Info($"[TCP Client] Успешно подключено к серверу ({_ip}:{_port}). Канал keep-alive активен.");

                            // Запускаем фоновое чтение, чтобы вовремя узнать, если сервер принудительно разорвет связь
                            _ = Task.Run(() => MonitorServerDisconnect(token));
                        }
                        else
                        {
                            CloseConnection();
                            _logger.Warn($"[TCP Client] Не удалось связаться с сервером {_ip}:{_port}. Повтор через 4 секунды...");
                            await Task.Delay(4000, token); // Пауза перед следующим реконнектом
                        }
                    }
                    catch (Exception ex)
                    {
                        CloseConnection();
                        _logger.Warn($"[TCP Client] Ошибка реконнекта: {ex.Message}. Ожидание 4 сек...");
                        await Task.Delay(4000, token);
                    }
                }
                else
                {
                    // Если связь есть — просто спим и проверяем статус каждые 2 секунды
                    await Task.Delay(2000, token);
                }
            }
        }

        // БЕСКОНЕЧНЫЙ ЦИКЛ ОТПРАВКИ СООБЩЕНИЙ ИЗ ОЧЕРЕДИ
        private async Task SendingLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // Ждем сигнала появления новых данных в очереди (не загружая CPU)
                _sendSignal.WaitOne(1000);

                if (token.IsCancellationRequested) break;

                while (_sendQueue.TryPeek(out string message))
                {
                    if (_isConnected && _stream != null)
                    {
                        try
                        {
                            // Формируем пакет с переносом строки (\n), как ждет сервер
                            byte[] data = Encoding.UTF8.GetBytes(message + "\n");

                            await _stream.WriteAsync(data, 0, data.Length, token);
                            await _stream.FlushAsync(token);

                            // Если отправка успешна — безвозвратно удаляем сообщение из очереди
                            _sendQueue.TryDequeue(out _);
                            _logger.Info($"[TCP Client] Сообщение успешно доставлено серверу: {message}");
                        }
                        catch (Exception ex)
                        {
                            _isConnected = false;
                            _logger.Warn($"[TCP Client] Сбой отправки пакета. Канал связи потерян: {ex.Message}");
                            break; // Прерываем цикл отправки, ждем пока ConnectionLoop восстановит связь
                        }
                    }
                    else
                    {
                        // Если связи нет, не удаляем данные из очереди — они подождут реконнекта!
                        break;
                    }
                }
            }
        }

        // Метод следит за тем, не закрыл ли сервер сокет со своей стороны
        private async Task MonitorServerDisconnect(CancellationToken token)
        {
            byte[] detectBuffer = new byte[1];
            try
            {
                while (_isConnected && _stream != null && !token.IsCancellationRequested)
                {
                    // Если ReadAsync возвращает 0 — значит удаленный сервер закрыл связь
                    int read = await _stream.ReadAsync(detectBuffer, 0, detectBuffer.Length, token);
                    if (read == 0)
                    {
                        _logger.Warn("[TCP Client] Сервер разорвал keep-alive соединение со своей стороны.");
                        _isConnected = false;
                        break;
                    }
                }
            }
            catch { _isConnected = false; }
        }

        private void CloseConnection()
        {
            _isConnected = false;
            try { _stream?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }
            _stream = null;
            _client = null;
        }
    }











}
