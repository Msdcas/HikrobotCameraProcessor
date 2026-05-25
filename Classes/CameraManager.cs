 using MvCamCtrl.NET;
using System.Runtime.InteropServices;
using static HikrobotCameraProcessor.FromSamples.HrCameraSearcher;
using static MvCamCtrl.NET.MyCamera;

namespace HikrobotCameraProcessor.FromSamples
{
    public static class CameraManager
    {
        public static readonly object _cameraLock = new object();

        public delegate void OnInputStateChanged(bool isPressed);
        public static event OnInputStateChanged ButtonStateChanged;

        public static MyCamera Cam { get; private set; } = new MyCamera();
        public static CancellationTokenSource _monitorCts = new CancellationTokenSource();
        private static List<Task> _inputTasks = new List<Task>();

        public static List<UILineRow> LastLoadedLines { get; private set; } = new List<UILineRow>();
        public static event Action OnLineConfigsReady;

        public class UILineRow
        {
            public uint Index { get; set; }
            public string Mode { get; set; }
            public List<string> ModeOptions { get; set; } = new List<string>(); // Доступные режимы
            public string Source { get; set; }
            public List<string> SourceOptions { get; set; } = new List<string>(); // Доступные источники
            public bool IsInverted { get; set; }
            public long DebouncerTime { get; set; }

            public long DebouncerMin { get; set; } = 0;
            public long DebouncerMax { get; set; } = 65535;
        }

        public class LineInfo
        {
            public uint Index;
            public string Mode;
            public uint Format;
            public string Source;
            public bool IsInverted;
            public uint DebouncerTime;

            public event Action<bool> OnStateChanged;

            public void RaiseStateChanged(bool state)
            {
                OnStateChanged?.Invoke(state);
            }
            public void ClearHandlers()
            {
                OnStateChanged = null;
            }
            public bool IsInput => Mode != null && Mode.Contains("Input", StringComparison.OrdinalIgnoreCase);

        }

        public static List<LineInfo> Lines = new List<LineInfo>();


        // Вспомогательный метод для получения всех возможных значений Enum из камеры
        public static List<string> GetEnumOptions(string featureName)
        {
            var list = new List<string>();
            MyCamera.MVCC_ENUMVALUE stEnum = new MyCamera.MVCC_ENUMVALUE();
            int nRet = Cam.MV_CC_GetEnumValue_NET(featureName, ref stEnum);

            if (nRet == MyCamera.MV_OK)
            {
                for (int i = 0; i < stEnum.nSupportedNum; i++)
                {
                    // Здесь должна быть ваша логика получения текста по индексу stEnum.nSupportValue[i]
                    list.Add(GetEnumText(featureName, stEnum.nSupportValue[i]));
                }
            }
            return list;
        }

        private static bool Check(int nRet, string action)
        {
            if (nRet != MyCamera.MV_OK)
            {
                AppLogger.LogError(null, $"Camera error: {action} failed with code {nRet:X8}");
                //throw new Exception($"{action} failed: {nRet:X8}");
                return false;
            }
            return true;
        }


        public static bool TryDisconnect()
        {
            if (Cam == null)
            {
                AppLogger.LogInfo("Camera is already null. Disconnection skipped.");
                return true;
            }

            if (!Cam.MV_CC_IsDeviceConnected_NET())
            {
                AppLogger.LogInfo("Камера уже отключена физически. Зануляем объект.");
                Cam = null; // Освобождаем ссылку
                return true;
            }

            try
            {
                int nRet = Cam.MV_CC_CloseDevice_NET();

                if (nRet == MyCamera.MV_OK)
                {
                    AppLogger.LogInfo("Camera disconnected successfully");
                    return true;
                }
                else
                {
                    // Если камера уже была отключена физически, метод вернет ошибку, 
                    // но для нас это все равно "успешное" закрытие ресурса.
                    AppLogger.LogError(null, $"CloseDevice returned error code: 0x{nRet:X}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError(ex, "Critical error during camera disconnection");
                return false;
            }
        }

        public static bool TryConnect(int index)
        {
            // 1. Проверяем индекс по нашему списку UI
            if (index < 0 || index >= CameraDatas.Count)
            {
                AppLogger.LogError(null, $"Connect failed: Index {index} out of range.");
                return false;
            }

            int nRet;

            try
            {
                // 2. Делаем мгновенный нативный поиск камер (как в вашем рабочем коде)
                MyCamera.MV_CC_DEVICE_INFO_LIST stDeviceList = new MyCamera.MV_CC_DEVICE_INFO_LIST();
                nRet = MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref stDeviceList);

                if (nRet != MyCamera.MV_OK || stDeviceList.nDeviceNum == 0)
                {
                    AppLogger.LogError(null, "Камеры не найдены в системе при попытке подключения.");
                    return false;
                }

                if (index >= stDeviceList.nDeviceNum)
                {
                    AppLogger.LogError(null, $"Индекс {index} больше, чем доступно физических камер ({stDeviceList.nDeviceNum}).");
                    return false;
                }

                // 3. Маршалим структуру СВЕЖЕЙ прямо из памяти по выбранному индексу
                IntPtr pDeviceInfo = stDeviceList.pDeviceInfo[index];
                var stDeviceInfo = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(pDeviceInfo, typeof(MyCamera.MV_CC_DEVICE_INFO));

                // 4. Очищаем старое устройство у глобального объекта cam (используем ваш глобальный cam)
                if (Cam.MV_CC_IsDeviceConnected_NET())
                {
                    Cam.MV_CC_CloseDevice_NET();
                }
                Cam.MV_CC_DestroyDevice_NET();

                // 5. Инициализируем и открываем
                nRet = Cam.MV_CC_CreateDevice_NET(ref stDeviceInfo);
                if (nRet != MyCamera.MV_OK)
                {
                    AppLogger.LogError(null, $"MV_CC_CreateDevice_NET failed: 0x{nRet:X8}");
                    return false;
                }

                nRet = Cam.MV_CC_OpenDevice_NET();
                if (nRet != MyCamera.MV_OK)
                {
                    AppLogger.LogError(null, $"MV_CC_OpenDevice_NET failed: 0x{nRet:X8}. Возможно, камера занята.");
                    return false;
                }

                AppLogger.LogInfo($"Камера '{CameraDatas[index].GetDescription()}' успешно подключена.");

                GetLinesConfig();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError(ex, "Критическое исключение внутри метода Connect");
                return false;
            }
        }

        public static void GetLinesConfig()
        {
            var rows = new List<UILineRow>();

            // Использовать тот же объект блокировки, что и при записи параметров!
            lock (_cameraLock)
            {
                try
                {
                    for (uint i = 0; i < 4; i++)
                    {
                        // 1. Выбираем линию
                        int nRet = Cam.MV_CC_SetEnumValue_NET("LineSelector", i);
                        if (nRet != MyCamera.MV_OK) continue;

                        var rowData = new UILineRow { Index = i };

                        MyCamera.MVCC_ENUMVALUE stMode = new MyCamera.MVCC_ENUMVALUE();
                        if (Cam.MV_CC_GetEnumValue_NET("LineMode", ref stMode) == MyCamera.MV_OK)
                        {
                            rowData.Mode = GetEnumText("LineMode", stMode.nCurValue);
                            for (int k = 0; k < stMode.nSupportedNum; k++)
                                rowData.ModeOptions.Add(GetEnumText("LineMode", stMode.nSupportValue[k]));
                        }

                        MyCamera.MVCC_ENUMVALUE stSource = new MyCamera.MVCC_ENUMVALUE();
                        if (Cam.MV_CC_GetEnumValue_NET("LineSource", ref stSource) == MyCamera.MV_OK)
                        {
                            rowData.Source = GetEnumText("LineSource", stSource.nCurValue);
                            for (int k = 0; k < stSource.nSupportedNum; k++)
                                rowData.SourceOptions.Add(GetEnumText("LineSource", stSource.nSupportValue[k]));
                        }

                        // === 3. ИНВЕРСИЯ ===
                        bool isInverted = false;
                        Cam.MV_CC_GetBoolValue_NET("LineInverter", ref isInverted);
                        rowData.IsInverted = isInverted;

                        // === 4. ИСПРАВЛЕННЫЙ ДЕБАУНС ЧЕРЕЗ FLOAT === // в зависимости от прошивки камеры, иногда нужно брать MV_CC_GetIntValue_NET
                        MyCamera.MVCC_FLOATVALUE stDebouncerFloat = new MyCamera.MVCC_FLOATVALUE();

                        // Вызываем метод чтения Float параметров, как в мануале MVS
                        int debouncerRet = Cam.MV_CC_GetFloatValue_NET("LineDebouncerTime", ref stDebouncerFloat);

                        if (debouncerRet == MyCamera.MV_OK)
                        {
                            // Округляем до целого для отображения в TextBox
                            rowData.DebouncerTime = (long)Math.Round(stDebouncerFloat.fCurValue);

                            // Записываем реальные лимиты из прошивки камеры
                            rowData.DebouncerMin = (long)Math.Round(stDebouncerFloat.fMin);
                            rowData.DebouncerMax = (long)Math.Round(stDebouncerFloat.fMax);
                        }
                        else
                        {
                            // Если линия выходная или произошла ошибка типа — ставим нули
                            rowData.DebouncerTime = 0;
                            rowData.DebouncerMin = 0;
                            rowData.DebouncerMax = 0;
                        }


                        rows.Add(rowData);
                    }

                    lock (LastLoadedLines)
                    {
                        LastLoadedLines = rows;
                    }

                    // Уведомляем форму
                    OnLineConfigsReady?.Invoke();
                }
                catch (Exception ex)
                {
                    AppLogger.LogError(ex, "Ошибка при потокобезопасном чтении параметров шин");
                }
            }
        }


        public static bool TryUpdateLineParameter(uint lineIndex, string columnName, object newValue)
        {
            lock (_cameraLock)
            {
                try
                {
                    // 1. ОБЯЗАТЕЛЬНО: Выбираем линию в камере
                    int nRet = Cam.MV_CC_SetEnumValue_NET("LineSelector", lineIndex);
                    if (nRet != MyCamera.MV_OK)
                    {
                        AppLogger.LogError(null, $"Не удалось выбрать Line {lineIndex}. Код: 0x{nRet:X8}");
                        return false;
                    }

                    // 2. Применяем параметры напрямую без лишних проверок лимитов
                    switch (columnName)
                    {
                        case "Mode":
                            return SetEnumByName("LineMode", newValue.ToString());

                        case "Source":
                            // Как в вашем старом коде: перед сменой Source переводим линию в Output (обычно это ID 1 или 8 в зависимости от прошивки, 
                            // но так как пользователь выбрал Source, логично, что режим должен стать Output)
                            // Если в вашей камере Output — это число 1, то ставим 1. В вашем примере было написано 8.
                            Cam.MV_CC_SetEnumValue_NET("LineMode", 1); // Или 8, проверьте по вашей модели
                            return SetEnumByName("LineSource", newValue.ToString());

                        case "Inverted":
                            bool invertValue = (bool)newValue;
                            nRet = Cam.MV_CC_SetBoolValue_NET("LineInverter", invertValue);
                            return nRet == MyCamera.MV_OK;

                        case "Debouncer":
                            if (float.TryParse(newValue.ToString(), out float debounceUs))
                            {
                                nRet = Cam.MV_CC_SetFloatValue_NET("LineDebouncerTime", debounceUs);

                                if (nRet == MyCamera.MV_OK)
                                {
                                    AppLogger.LogInfo($"Line {lineIndex}: Успешно установлен Float-дебаунс {debounceUs} us");
                                    return true;
                                }
                                else
                                {
                                    AppLogger.LogError(null, $"Камера отклонила Float-дебаунс. Код ошибки SDK: 0x{nRet:X8}");
                                    return false;
                                }
                            }
                            return false;


                        default:
                            return false;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError(ex, $"Исключение при обновлении параметра {columnName} для линии {lineIndex} на значение {newValue}");
                    return false;
                }
            }
        }

        // Вспомогательный метод поиска числового ID по тексту и его записи в камеру
        private static bool SetEnumByName(string featureName, string textValue)
        {
            MyCamera.MVCC_ENUMVALUE stEnum = new MyCamera.MVCC_ENUMVALUE();
            if (Cam.MV_CC_GetEnumValue_NET(featureName, ref stEnum) == MyCamera.MV_OK)
            {
                for (int i = 0; i < stEnum.nSupportedNum; i++)
                {
                    // Используем ваш рабочий метод получения текста GetEnumText
                    if (GetEnumText(featureName, stEnum.nSupportValue[i]) == textValue)
                    {
                        int nRet = Cam.MV_CC_SetEnumValue_NET(featureName, stEnum.nSupportValue[i]);
                        if (nRet == MyCamera.MV_OK)
                            AppLogger.LogInfo($"{featureName} -> {textValue} (0x{stEnum.nSupportValue[i]:X})");
                        return nRet == MyCamera.MV_OK;
                    }
                }
            }
            return false;
        }

        private static readonly object _ioLock = new object();

        private static string GetEnumText(string featureName, uint value)
        {
            MyCamera.MVCC_ENUMVALUE stEnum = new MyCamera.MVCC_ENUMVALUE();
            Cam.MV_CC_GetEnumValue_NET(featureName, ref stEnum);

            // В SDK Hikrobot можно получить строковое описание текущего значения
            // Но для простоты вернем ID, если текст получить сложно
            if (featureName == "LineMode")
            {
                return value == 0 ? "Input" : (value == 8 ? "Strobe" : $"Output");
            }
            if (featureName == "LineSource")
            {
                return value == 5 ? "UserOutput" : (value == 0 ? "Exposure" : $"Source({value})");
            }
            return value.ToString();
        }



        public static bool SetOutputLineState(uint lineIndex, bool state)
        {
            // Строгий lock для защиты нативного хендла в многопоточной среде
            lock (_cameraLock)
            {
                try
                {
                    int nRet;

                    // ШАГ 1: Выбираем физическую выходную линию (Line 1)
                    nRet = Cam.MV_CC_SetEnumValue_NET("LineSelector", lineIndex);
                    if (nRet != MyCamera.MV_OK)
                    {
                        AppLogger.LogError(null, $"[SDK Error] Не удалось выбрать LineSelector для Line {lineIndex}. Код: 0x{nRet:X8}");
                        return false;
                    }

                    // ШАГ 2: Проверяем режим работы пина (Output = 1)
                    nRet = Cam.MV_CC_SetEnumValue_NET("LineMode", 1);
                    if (nRet != MyCamera.MV_OK)
                    {
                        AppLogger.LogError(null, $"[SDK Error] Не удалось перевести Line {lineIndex} в режим Output. Код: 0x{nRet:X8}");
                        return false;
                    }

                    // ШАГ 3: Переводим источник в UserOutput (значение 5 из вашего GetEnumText)
                    // Это разблокирует управление пином со стороны софта C#
                    nRet = Cam.MV_CC_SetEnumValue_NET("LineSource", 5);
                    if (nRet != MyCamera.MV_OK)
                    {
                        AppLogger.LogError(null, $"[SDK Error] Не удалось установить LineSource = UserOutput для Line {lineIndex}. Код: 0x{nRet:X8}");
                        return false;
                    }

                    // ШАГ 4: УПРАВЛЕНИЕ ОПТОПАРОЙ ЧЕРЕЗ ИНВЕРТОР (Легальный метод для WTX)
                    // Так как прямое триггерение бита заблокировано процессором для защиты оптопары,
                    // мы управляем состоянием диода через LineInverter. 
                    // state == true -> включаем инверсию (ток идет, диод горит)
                    // state == false -> отключаем инверсию (цепь разомкнута, диод гаснет)
                    nRet = Cam.MV_CC_SetBoolValue_NET("LineInverter", state);

                    if (nRet == MyCamera.MV_OK)
                    {
                        AppLogger.LogInfo($"[Hardware Success] Оптоизолированная Line {lineIndex} переведена в состояние: {(state ? "ВКЛ" : "ВЫКЛ")}");
                        return true;
                    }
                    else
                    {
                        // Если прошивка выдаст ошибку, мы увидим её точный код в NLog
                        AppLogger.LogError(null, $"[SDK Error] Сбой записи в LineInverter для Line {lineIndex}. Код ошибки железа: 0x{nRet:X8}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError(ex, $"Критическое системное исключение при управлении оптопарой Line {lineIndex}");
                    return false;
                }
            }
        }













        ///// <summary>
        ///// Переключает состояние диода на противоположное
        ///// </summary>
        //public static bool ToggleOutputLine(uint lineIndex)
        //{
        //    lock (_ioLock)
        //    {
        //        try
        //        {
        //            // Выбираем линию и селектор вывода, чтобы считать актуальное значение
        //            Cam.MV_CC_SetEnumValue_NET("LineSelector", lineIndex);
        //            uint userOutputIndex = lineIndex > 0 ? lineIndex - 1 : 0;
        //            Cam.MV_CC_SetEnumValue_NET("UserOutputSelector", userOutputIndex);

        //            // Читаем текущее состояние
        //            bool currentState = false;
        //            int nRet = Cam.MV_CC_GetBoolValue_NET("UserOutputValue", ref currentState);

        //            if (nRet != MyCamera.MV_OK)
        //            {
        //                AppLogger.LogError(null, $"Не удалось считать текущее состояние UserOutputValue для Line {lineIndex}");
        //                return false;
        //            }

        //            // Записываем противоположное значение
        //            return SetOutputLineState(lineIndex, !currentState);
        //        }
        //        catch (Exception ex)
        //        {
        //            AppLogger.LogError(ex, $"Ошибка при инверсии состояния Line {lineIndex}");
        //            return false;
        //        }
        //    }
        //}

        /// <summary>
        /// Асинхронно мигает диодом в течение 3 секунд с заданной частотой
        /// </summary>
        //public static async Task BlinkOutputLineAsync(uint lineIndex, int intervalMs = 250)
        //{
        //    AppLogger.LogInfo($"Запущено мигание диода на Line {lineIndex} в течение 3 секунд.");

        //    // Запоминаем время старта
        //    DateTime startTime = DateTime.Now;
        //    bool state = true;

        //    try
        //    {
        //        // Цикл выполняется ровно 3000 миллисекунд
        //        while ((DateTime.Now - startTime).TotalMilliseconds < 3000)
        //        {
        //            // Меняем состояние диода
        //            SetOutputLineState(lineIndex, state);
        //            state = !state; // Инвертируем флаг для следующего шага

        //            // Ждем интервал БЕЗ заморозки основного потока программы
        //            await Task.Delay(intervalMs);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        AppLogger.LogError(ex, $"Сбой во время мигания диода на Line {lineIndex}");
        //    }
        //    finally
        //    {
        //        // Гарантированно выключаем диод после окончания 3 секунд
        //        SetOutputLineState(lineIndex, false);
        //        AppLogger.LogInfo($"Мигание диода на Line {lineIndex} завершено.");
        //    }
        //}

        //public static void ForceCaptureAndSave(string fileName)
        //{
        //    AppLogger.LogInfo("Force capture started");

        //    IntPtr pData = IntPtr.Zero;
        //    try  //защита утечки unmanaged памяти
        //    {
        //        int nRet;

        //        // 1. Убеждаемся, что захват остановлен для смены режима
        //        Cam.MV_CC_StopGrabbing_NET();

        //        // 2. Включаем режим триггера
        //        nRet = Cam.MV_CC_SetEnumValue_NET("TriggerMode", 1); // On

        //        // 3. Устанавливаем источник - Software. 
        //        // ВАЖНО: У некоторых моделей индекс может отличаться. Проверьте 7 или 0.
        //        nRet = Cam.MV_CC_SetEnumValue_NET("TriggerSource", 0);

        //        // 4. Сначала ЗАПУСКАЕМ захват
        //        nRet = Cam.MV_CC_StartGrabbing_NET();
        //        if (nRet != MyCamera.MV_OK)
        //        {
        //            Console.WriteLine($"Ошибка старта захвата: {nRet:X8}");
        //            return;
        //        }

        //        // 5. Только ПОСЛЕ старта шлем программный курок
        //        nRet = Cam.MV_CC_SetCommandValue_NET("TriggerSoftware");
        //        if (nRet != MyCamera.MV_OK)
        //        {
        //            // Если здесь 80000000, значит связь с камерой потеряна или хэндл разрушен
        //            Console.WriteLine($"Ошибка TriggerSoftware: {nRet:X8}. Проверьте подключение.");
        //            return;
        //        }

        //        // 6. Подготовка буфера и получение кадра
        //        MyCamera.MVCC_INTVALUE stPayload = new MyCamera.MVCC_INTVALUE();
        //        Cam.MV_CC_GetIntValue_NET("PayloadSize", ref stPayload);

        //        pData = Marshal.AllocHGlobal((int)stPayload.nCurValue);
        //        MyCamera.MV_FRAME_OUT_INFO_EX stFrameInfo = new MyCamera.MV_FRAME_OUT_INFO_EX();

        //        nRet = Cam.MV_CC_GetOneFrameTimeout_NET(pData, stPayload.nCurValue, ref stFrameInfo, 2000);

        //        if (nRet == MyCamera.MV_OK)
        //        {
        //            MyCamera.MV_SAVE_IMG_TO_FILE_PARAM stSaveParam = new MyCamera.MV_SAVE_IMG_TO_FILE_PARAM();
        //            stSaveParam.enImageType = MyCamera.MV_SAVE_IAMGE_TYPE.MV_Image_Png;
        //            stSaveParam.enPixelType = stFrameInfo.enPixelType;
        //            stSaveParam.nWidth = stFrameInfo.nWidth;
        //            stSaveParam.nHeight = stFrameInfo.nHeight;
        //            stSaveParam.pData = pData;
        //            stSaveParam.nDataLen = stFrameInfo.nFrameLen;
        //            stSaveParam.pImagePath = fileName;

        //            Cam.MV_CC_SaveImageToFile_NET(ref stSaveParam);
        //            Console.WriteLine($"Фото сохранено: {fileName}");
        //        }
        //        else
        //        {
        //            Console.WriteLine($"Кадр не получен: {nRet:X8}");
        //        }
        //    }
        //    catch (Exception e)
        //    {
        //        AppLogger.LogInfo($"Force capture failed: {e.ToString()}");
        //    }
        //    finally
        //    {
        //        if (pData != IntPtr.Zero)
        //            Marshal.FreeHGlobal(pData);
        //    }

        //}



    }

}
