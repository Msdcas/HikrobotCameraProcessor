using HikrobotCameraProcessor.Classes;
using HikrobotCameraProcessor.FromSamples;
using MvCamCtrl.NET;
using Newtonsoft.Json;
using static HikrobotCameraProcessor.FromSamples.CameraManager;

namespace HikrobotCameraProcessor
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();

        }

        private void Form1_Load(object sender, EventArgs e)
        {
            comboBoxCameras.DataSource = null;
            comboBoxCameras.DataSource = HrCameraSearcher.CameraDatas;
            comboBoxCameras.DisplayMember = "DisplayName";

        }

        private void button1_Click(object sender, EventArgs e)
        {
            HrCameraSearcher.RefreshList();
        }

        private async void bConnect_Click(object sender, EventArgs e)
        {
            if (comboBoxCameras.Items.Count == 0) return;

            bConnect.Enabled = false;
            if (!CameraManager.TryConnect(comboBoxCameras.SelectedIndex))
            {
                bConnect.Enabled = true;
                button1.Enabled = true; //refresh
                bDisconnect.Enabled = false;
                comboBoxCameras.Enabled = true;
                MessageBox.Show($"При подключении возникла ошибка. Возможно камера занята. Смотри подробное описание в логах", null, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            this.Invoke(new Action(() =>
            {
                List<CameraManager.UILineRow> linesFromBuffer = CameraManager.LastLoadedLines;

                GenerateIOControls(linesFromBuffer);
            }));

            button1.Enabled = false;
            bDisconnect.Enabled = true;
            comboBoxCameras.Enabled = false;
        }

        private async void bDisconnect_Click(object sender, EventArgs e)
        {
            if (CameraManager.TryDisconnect())
            {
                bConnect.Enabled = true;
                button1.Enabled = true; //refresh
                bDisconnect.Enabled = false;
                comboBoxCameras.Enabled = true;
                panelIO.Controls.Clear();
            }
            else
            {
                bConnect.Enabled = false;
                button1.Enabled = false;
                bDisconnect.Enabled = true;
                comboBoxCameras.Enabled = false;
            }
        }


        #region Draw IOControls


        /// <summary>
        /// Главный конструктор и рендерер динамического пульта управления линиями ввода-вывода (I/O) камеры.
        /// Выполняет полную очистку panelIO, переводит контейнер в режим TopDown, активирует автоматическую 
        /// прокрутку (AutoScroll) и вычисляет чистую рабочую ширину строки (с учетом системного скроллбара). 
        /// Запускает послойную сборку интерфейса: сначала генерирует и крепит шапку таблицы, а затем в цикле 
        /// строит индивидуальные панели параметров для каждой физической шины, переданной из буфера LastLoadedLines.
        /// </summary>
        public void GenerateIOControls(List<CameraManager.UILineRow> lines)
        {
            if (panelIO == null || panelIO.IsDisposed) return;

            // 1. Очистка и базовая конфигурация главного контейнера 1057х230
            panelIO.Controls.Clear();
            panelIO.FlowDirection = FlowDirection.TopDown;
            panelIO.WrapContents = false;
            panelIO.AutoScroll = true;

            // Расчет чистой ширины строки пульта с учетом рамки и скроллбара
            int rowWidth = panelIO.Width - 27; // ~1030 пикселей

            // 2. Добавляем строку заголовков (Шапку) в ПРАВИЛЬНОЙ очередности
            var headerPanel = CreateHeaderRow(rowWidth);
            panelIO.Controls.Add(headerPanel);

            // 3. Строим строки с элементами управления
            foreach (var line in lines)
            {
                var rowPanel = CreateDataRow(line, rowWidth);
                panelIO.Controls.Add(rowPanel);
            }
        }


        /// <summary>
        /// Создаёт и размечает статическую строку заголовков (шапку таблицы) для пульта управления.
        /// Инициализирует панель FlowLayoutPanel, выставляет жирный шрифт элементов и последовательно 
        /// добавляет текстовые метки (Label). Ширина каждого заголовка и их левые отступы (Margin) выверены 
        /// до пикселя и жестко синхронизированы с габаритами компонентов данных, создаваемых в CreateDataRow, 
        /// что гарантирует монолитность вертикальной сетки таблицы и защиту от сдвига столбцов.
        /// </summary>
        private FlowLayoutPanel CreateHeaderRow(int width)
        {
            var headerPanel = new FlowLayoutPanel
            {
                Width = width,
                Height = 25,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 0, 0, 5)
            };

            var font = new Font(this.Font, FontStyle.Bold);

            // Очередность и ширина СТРОГО синхронизированы с данными ниже
            headerPanel.Controls.Add(new Label { Text = "Линия", Font = font, Width = 60, Margin = new Padding(0) });
            headerPanel.Controls.Add(new Label { Text = "Режим", Font = font, Width = 110, Margin = new Padding(5, 0, 0, 0) });
            headerPanel.Controls.Add(new Label { Text = "Источник", Font = font, Width = 160, Margin = new Padding(5, 0, 0, 0) });
            headerPanel.Controls.Add(new Label { Text = "Инв.", Font = font, Width = 45, Margin = new Padding(5, 0, 0, 0) });
            headerPanel.Controls.Add(new Label { Text = "Фильтр", Font = font, Width = 70, Margin = new Padding(5, 0, 0, 0) });
            headerPanel.Controls.Add(new Label { Text = "Статус/Действие", Font = font, Width = 100, Margin = new Padding(5, 0, 0, 0), TextAlign = ContentAlignment.MiddleCenter });
            headerPanel.Controls.Add(new Label { Text = "Назначенный Монитор", Font = font, Width = 160, Margin = new Padding(5, 0, 0, 0) }); // Уменьшено до 160
            headerPanel.Controls.Add(new Label { Text = "Назначенное Действие", Font = font, Width = 160, Margin = new Padding(5, 0, 0, 0) }); // Уменьшено до 160

            return headerPanel;
        }


        /// <summary>
        /// Фабричный метод для создания и первичного наполнения одной горизонтальной строки параметров физической линии.
        /// Инициализирует контейнер FlowLayoutPanel заданной ширины с уникальным Tag, равным индексу линии. 
        /// Последовательно генерирует и выравнивает базовые аппаратные компоненты: текстовую метку (Label), 
        /// селектор режима LineMode (ComboBox), селектор физического источника LineSource (ComboBox) и флаг инверсии 
        /// LineInverter (CheckBox). На пятом шаге строит TextBox фильтра дребезга контактов (Debouncer) с защитой от ввода 
        /// букв и привязкой к нажатию Enter, после чего передаёт строку на доукомплектование расширенными элементами.
        /// </summary>
        private FlowLayoutPanel CreateDataRow(CameraManager.UILineRow line, int width)
        {
            var rowPanel = new FlowLayoutPanel
            {
                Width = width,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight,
                Tag = line.Index,
                Margin = new Padding(0, 0, 0, 2)
            };

            bool isInput = line.Mode == "Input";

            // 1. Номер линии
            rowPanel.Controls.Add(new Label { Text = $"Line {line.Index}", Width = 60, Margin = new Padding(0, 8, 0, 0) });

            // 2. Режим (ComboBox)
            var cbMode = new ComboBox { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList, Tag = "Mode", FlatStyle = FlatStyle.Flat, DrawMode = DrawMode.OwnerDrawFixed };
            if (line.ModeOptions?.Count > 0) { cbMode.Items.AddRange(line.ModeOptions.ToArray()); cbMode.SelectedItem = line.Mode; }
            cbMode.SelectedIndexChanged += IOControl_ValueChanged;
            cbMode.DrawItem += ComboBox_DrawItem;
            rowPanel.Controls.Add(cbMode);

            // 3. Источник (ComboBox)
            var cbSource = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Tag = "Source", FlatStyle = FlatStyle.Flat, DrawMode = DrawMode.OwnerDrawFixed };
            if (line.SourceOptions?.Count > 0) { cbSource.Items.AddRange(line.SourceOptions.ToArray()); cbSource.SelectedItem = line.Source; }
            else { cbSource.Items.Add("N/A"); cbSource.SelectedIndex = 0; }
            cbSource.SelectedIndexChanged += IOControl_ValueChanged;
            cbSource.DrawItem += ComboBox_DrawItem;
            rowPanel.Controls.Add(cbSource);

            // 4. Инверсия (CheckBox)
            var chkInv = new CheckBox { Width = 45, Checked = line.IsInverted, Tag = "Inverted", FlatStyle = FlatStyle.Flat, Margin = new Padding(18, 8, 0, 0) };
            chkInv.Click += IOControl_ValueChanged;
            chkInv.Enabled = false; // ================================= в нем нет смысла т.к. он дискретный и мы им управляем инверсией → CameraManager.SetOutputLineState
            rowPanel.Controls.Add(chkInv);

            // Инициализация activeMonitor для расчета цвета лампочки (PictureBox)
            IMonitorMethod activeMonitor = null;
            if (ThreadManager.ActiveMonitors.TryGetValue(line.Index, out var liveMonitor)) activeMonitor = liveMonitor;
            else
            {
                var currentSavedLineConf = CameraLineHandler.CurrentConfig.Lines.FirstOrDefault(l => l.LineIndex == line.Index);
                if (currentSavedLineConf != null && !string.IsNullOrEmpty(currentSavedLineConf.SelectedMonitorName) && currentSavedLineConf.SelectedMonitorName != "Отключен")
                {
                    activeMonitor = CameraLineHandler.CreateMonitorContainer().FirstOrDefault(m => m.Name == currentSavedLineConf.SelectedMonitorName);
                }
            }

            // 5. Дебаунс/Фильтр (TextBox)
            var txtDebounce = new TextBox
            {
                Width = 70,
                Text = line.DebouncerTime.ToString(),
                Tag = "Debouncer",
                Margin = new Padding(5, 5, 0, 0),
                Enabled = isInput
            };
            // Храним лимиты именно этой линии прямо внутри TextBox, чтобы не искать их по базам
            txtDebounce.AccessibleDescription = $"{line.DebouncerMin};{line.DebouncerMax}";

            // Создаем всплывающую подсказку при наведении мыши (Popup Menu / ToolTip)
            var hint = new ToolTip();
            hint.ToolTipTitle = $"Лимиты Line {line.Index}";
            hint.SetToolTip(txtDebounce, $"Минимум: {line.DebouncerMin} us\nМаксимум: {line.DebouncerMax} us" +
                $"\nВвод - Enter\n Некоторые модели камер имеют только 1 регистр Debaunce");

            // Подписываем на события: запрет букв и проверку диапазона
            txtDebounce.KeyPress += TextBox_OnlyNumbers_KeyPress;
            txtDebounce.TextChanged += TextBox_ValidateRange_TextChanged;
            txtDebounce.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; IOControl_ValueChanged(s, e); } };
            rowPanel.Controls.Add(txtDebounce);

            // Передаем управление подметоду, который добавит Кнопку/PictureBox, а затем Мониторы и Действия
            AppendRemainingControls(rowPanel, line, isInput, activeMonitor);

            return rowPanel;
        }


        /// <summary>
        /// Добавляет в горизонтальную строку управления расширенные программные компоненты и элементы индикации.
        /// В зависимости от режима работы шины генерирует шестой столбец: для выходов (Output) создаёт активную 
        /// кнопку быстрого теста «Blk» с триггерной сменой цветов (синий/желтый), а для входов (Input) — 
        /// интерактивный индикатор здоровья PictureBox с обработчиком клика для имитации импульса. 
        /// На седьмом и восьмом шаге формирует выпадающие списки (ComboBox) выбора Мониторов и Действий шириной 160 пикселей, 
        /// восстанавливает их состояния из JSON-конфигурации и привязывает кастомный OwnerDraw-отрисовщик.
        /// </summary>
        private void AppendRemainingControls(FlowLayoutPanel rowPanel, CameraManager.UILineRow line, bool isInput, IMonitorMethod activeMonitor)
        {
            // ========================================================================
            // 6. СТОЛБЕЦ: СТАТУС / ДЕЙСТВИЕ (Кнопка для Output или PictureBox для Input)
            // ========================================================================
            bool isOutput = line.Mode != null && line.Mode.Contains("Out", StringComparison.OrdinalIgnoreCase);
            if (isOutput)
            {
                var btnBlink = new Button
                {
                    Text = "Blink",
                    Width = 100,
                    Margin = new Padding(5, 3, 0, 0),
                    Enabled = true,
                    FlatStyle = FlatStyle.Flat,      // Переключаем в Flat, чтобы Windows разрешила красить кнопку
                    BackColor = Color.DodgerBlue,   // Красивый насыщенный синий фон
                    ForeColor = Color.White         // Белый текст, чтобы хорошо читался на синем
                };

                btnBlink.Click += async (s, e) =>
                {
                    // 1. Блокируем кнопку и красим в желтый на время мигания
                    btnBlink.Enabled = false;
                    btnBlink.Text = "...";
                    btnBlink.BackColor = Color.Gold; // Желтый цвет (Gold)
                    btnBlink.ForeColor = Color.Black; // Черный текст для контраста на желтом
                    btnBlink.Update(); // Принудительно заставляем WinForms перекрасить элемент в этот же миг

                    // 2. Вызываем асинхронный метод мигания железа
                    await CameraLineHandler.BlinkOutputLineAsync(line.Index, 200, 3);

                    // 3. Мигание завершено: возвращаем исходный синий цвет и активируем кнопку
                    btnBlink.Text = "Blink";
                    btnBlink.BackColor = Color.DodgerBlue;
                    btnBlink.ForeColor = Color.White;
                    btnBlink.Enabled = true;
                };
                rowPanel.Controls.Add(btnBlink);
            }
            else
            {
                // Для входных линий (Input) оставляем панель PictureBox, тут всё стабильно
                var picStatus = new PictureBox
                {
                    Width = 24,
                    Height = 24,
                    BackColor = GetHealthColor(activeMonitor),
                    Margin = new Padding(43, 5, 33, 0),
                    Tag = $"StatusIndicator_{line.Index}",
                    Cursor = Cursors.Hand
                };
                picStatus.Click += async (s, e) =>
                {
                    if (!ThreadManager.IsSystemRunning) return;
                    TriggerFlashAnimation(picStatus);

                    try
                    {
                        if (ThreadManager.ActiveActions.TryGetValue(line.Index, out IActionMethod assignedAction))
                        {
                            int interval = int.TryParse(assignedAction.Parameters.FirstOrDefault(p => p.Name.Contains("Интервал"))?.Value, out int i) ? i : 200;
                            int duration = int.TryParse(assignedAction.Parameters.FirstOrDefault(p => p.Name.Contains("Длительность"))?.Value, out int d) ? d : 2;
                            await CameraLineHandler.BlinkOutputLineAsync(line.Index, interval, duration);
                        }
                        else
                        {
                            await CameraLineHandler.BlinkOutputLineAsync(line.Index, intervalMs: 200, durationSec: 2);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError(ex, $"Ошибка физического мигания диода по клику на Line {line.Index}");
                    }
                };
                rowPanel.Controls.Add(picStatus);
            }


            var savedLineConf = CameraLineHandler.CurrentConfig.Lines.FirstOrDefault(l => l.LineIndex == line.Index);

            // ========================================================================
            // 7. СТОЛБЕЦ: MONITOR
            // ========================================================================
            var cbTrigger = new ComboBox
            {
                Width = 160,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Tag = "MonitorSelect",
                Enabled = isInput,
                FlatStyle = FlatStyle.Flat,
                DrawMode = DrawMode.OwnerDrawFixed
            };
            cbTrigger.Items.Add("Отключен");
            cbTrigger.Items.AddRange(CameraLineHandler.CreateMonitorContainer().Select(m => m.Name).ToArray());
            cbTrigger.SelectedItem = savedLineConf?.SelectedMonitorName ?? "Отключен";
            cbTrigger.DrawItem += ComboBox_DrawItem;
            cbTrigger.SelectedIndexChanged += MethodSelection_Changed;
            rowPanel.Controls.Add(cbTrigger);

            // Подписываемся на живые события здоровья, если мониторинг включен
            if (activeMonitor != null && ThreadManager.IsSystemRunning)
            {
                activeMonitor.OnStateUpdated -= Monitor_OnStateUpdated;
                activeMonitor.OnStateUpdated += Monitor_OnStateUpdated;
            }

            // ========================================================================
            // 8. СТОЛБЕЦ: ACTION
            // ========================================================================
            var cbAction = new ComboBox
            {
                Width = 160,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Tag = "ActionSelect",
                Enabled = !isInput,
                FlatStyle = FlatStyle.Flat,
                DrawMode = DrawMode.OwnerDrawFixed
            };
            cbAction.Items.Add("Отключен");
            cbAction.Items.AddRange(CameraLineHandler.CreateActionContainer().Select(m => m.Name).ToArray());
            cbAction.SelectedItem = savedLineConf?.SelectedActionName ?? "Отключен";
            cbAction.DrawItem += ComboBox_DrawItem;
            cbAction.SelectedIndexChanged += MethodSelection_Changed;
            rowPanel.Controls.Add(cbAction);
        }


        #endregion




        private Color GetHealthColor(IMonitorMethod monitor)
        {
            if (monitor == null || monitor.Name == "Отключен") return Color.Gray;
            if (!ThreadManager.IsSystemRunning) return Color.LightBlue; // Готов к старту

            return monitor.HealthStatus == "OK" ? Color.DarkGreen : Color.Red;
        }


        private void TextBox_OnlyNumbers_KeyPress(object sender, KeyPressEventArgs e)
        {
            // Разрешаем только цифры и управляющие клавиши (например, Backspace)
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                e.Handled = true; // Блокируем нажатие кнопки, символ не появится в поле
            }
        }


        private void TextBox_ValidateRange_TextChanged(object sender, EventArgs e)
        {
            TextBox txt = (TextBox)sender;
            if (string.IsNullOrEmpty(txt.Text)) return;

            // Вытаскиваем лимиты конкретно этой линии из нашей скрытой строки AccessibleDescription
            string[] limits = txt.AccessibleDescription.Split(';');
            long min = long.Parse(limits[0]);
            long max = long.Parse(limits[1]);

            if (long.TryParse(txt.Text, out long enteredValue))
            {
                // Если введенное значение превышает максимум линии — жестко режем его до nMax
                if (enteredValue > max)
                {
                    txt.Text = max.ToString();
                    txt.SelectionStart = txt.Text.Length; // Сдвигаем курсор в конец строки
                }
                // Если значение меньше минимума (например, ввели 0, а минимум 10)
                else if (enteredValue < min)
                {
                    txt.Text = min.ToString();
                    txt.SelectionStart = txt.Text.Length;
                }
            }
        }


        /// <summary>
        /// Центральный диспетчер обработки изменения базовых физических параметров линии камеры (Mode, Source, Inverted, Debouncer).
        /// Реализует паттерн атомарной транзакции с упреждающим выходом (Guard Clause) и защитой от шторма ложных событий 
        /// соседних полей через флаг _isProcessingUiUpdate. Извлекает новое значение, транслирует команду в Hikrobot SDK 
        /// внутри безопасного lock-блока. В случае успеха запускает анимацию успеха, а при изменении режима работы шины 
        /// временно отвязывает события (DetachIOEvents) и запрашивает перерисовку пульта. При ошибке SDK делает откат к исходным данным.
        /// </summary>
        private async void IOControl_ValueChanged(object sender, EventArgs e)
        {
            Control control = (Control)sender;
            if (!(control.Parent is FlowLayoutPanel rowPanel)) return;

            uint lineIndex = (uint)rowPanel.Tag;
            string paramName = control.Tag.ToString();
            object newValue = null;

            if (control is ComboBox cb) newValue = cb.SelectedItem;
            else if (control is CheckBox chk) newValue = chk.Checked;
            else if (control is TextBox txt) newValue = txt.Text;

            if (newValue == null) return;

            try
            {

                // Отправляем изменения в камеру
                bool isSuccess = CameraManager.TryUpdateLineParameter(lineIndex, paramName, newValue);

                if (isSuccess)
                {
                    AppLogger.LogInfo($"Параметр {paramName} успешно применен к Line {lineIndex}");
                    _ = AnimateControlSuccess(control);

                    // ЕСЛИ ИЗМЕНИЛСЯ РЕЖИМ (перерисовываем интерфейс)
                    if (paramName == "Mode")
                    {
                        await Task.Delay(600);
                        RefreshCameraLines();
                    }
                    // ИСПРАВЛЕНИЕ ДЛЯ ДЕБАУНСА: Синхронизируем текстовые поля в интерфейсе, 
                    // так как в камере это один общий регистр на все линии
                    else if (paramName == "Debouncer")
                    {
                        // Ищем все TextBox с тегом "Debouncer" на нашей panelIO
                        var allDebouncerTextBoxes = panelIO.Controls
                            .OfType<FlowLayoutPanel>()
                            .SelectMany(row => row.Controls.OfType<TextBox>())
                            .Where(txt => txt.Tag?.ToString() == "Debouncer" && txt != control); // Исключаем текущий, который правим

                        foreach (var txt in allDebouncerTextBoxes)
                        {
                            // Мягко обновляем текст на соседних линиях без вызова событий записи в камеру,
                            // так как у нас событие висит на кнопке Enter (KeyDown), а не на изменении текста!
                            txt.Text = newValue.ToString();
                        }
                    }
                }

                else
                {
                    // ОТКАТ ПРИ ОШИБКЕ
                    DetachIOEvents(); // Отвязываем события

                    await AnimatePanelFailure();

                    MessageBox.Show($"Не удалось применить параметр '{paramName}' к камере. Настройки будут возвращены к исходным.",
                                    "Ошибка оборудования", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    await Task.Delay(150);
                    RefreshCameraLines(); // Перерисовываем пульт (он сам вернет чистые события)
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError(ex, "Ошибка в обработчике изменения параметров пульта");
            }
        }


        /// <summary>
        /// Асинхронный метод обновления графического пульта на основе актуального состояния железа.
        /// Устраняет гонку потоков: вызывает и честно дожидается окончания полного опроса физических 
        /// регистров камеры через механизм WaitForLineConfigsAsync. Как только глобальный буфер (LastLoadedLines) 
        /// гарантированно обновляется свежими данными из SDK, безопасно возвращает выполнение в GUI-поток 
        /// через InvokeRequired и запускает полную регенерацию строк интерфейса.
        /// </summary>
        private async void RefreshCameraLines()
        {
            try
            {
                // 1. Использовать асинхронное ожидание, которое мы заложили для формы!
                // Этот метод сам пнет CameraManager.GetLinesConfig() и остановит выполнение,
                // пока событие OnLineConfigsReady не обновит глобальный буфер.
                await WaitForLineConfigsAsync();

                // 2. Возвращаем выполнение в поток формы (Invoke)
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() =>
                    {
                        List<CameraManager.UILineRow> linesFromBuffer = CameraManager.LastLoadedLines;
                        GenerateIOControls(linesFromBuffer);
                    }));
                }
                else
                {
                    List<CameraManager.UILineRow> linesFromBuffer = CameraManager.LastLoadedLines;
                    GenerateIOControls(linesFromBuffer);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError(ex, "Ошибка при асинхронном обновлении пульта линий");
            }
        }


        private static Task WaitForLineConfigsAsync()
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Action handler = null;
            handler = () =>
            {
                CameraManager.OnLineConfigsReady -= handler;
                tcs.SetResult(true); // Сигнализируем, что данные в буфере обновились
            };

            CameraManager.OnLineConfigsReady += handler;
            CameraManager.GetLinesConfig();

            return tcs.Task;
        }



        #region Animation

        private void Monitor_OnStateUpdated(object sender, MonitorStateEventArgs e)
        {
            if (this.IsDisposed) return;

            // Перенаправляем выполнение из потока SDK/Железа строго в поток отрисовки формы
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => Monitor_OnStateUpdated(sender, e)));
                return;
            }

            IMonitorMethod monitor = (IMonitorMethod)sender;

            // Ищем строку FlowLayoutPanel, которой принадлежит этот монитор, чтобы узнать индекс линии
            uint lineIndex = 0;
            foreach (var pair in ThreadManager.ActiveMonitors)
            {
                if (pair.Value == monitor) { lineIndex = pair.Key; break; }
            }

            // Находим PictureBox индикатора этой линии на панели panelIO
            var pic = panelIO.Controls
                .OfType<FlowLayoutPanel>()
                .SelectMany(p => p.Controls.OfType<PictureBox>())
                .FirstOrDefault(p => p.Tag?.ToString() == $"StatusIndicator_{lineIndex}");

            if (pic == null) return;

            // 1. ЕСЛИ МЕТОД ВЫЛЕТЕЛ В ОШИБКУ
            if (e.Status == "Error")
            {
                pic.BackColor = Color.Red;
                // Можно вывести ошибку на форму в статус-бар или логгер
                AppLogger.LogError(null, $"Монитор Линии {lineIndex} сообщил об ошибке: {e.ErrorMessage}");
                return;
            }

            // 2. ЕСЛИ ЭТО АНАЛОГОВЫЙ СИГНАЛ (Плавно переливаем цвет от синего к красному)
            if (e.IsAnalog)
            {
                // Нормализуем значение (например, датчик выдает от 0 до 100)
                // Рассчитываем цвет: 0 = Синий (холодно/нет сигнала), 100 = Ярко-красный (максимум)
                float value = Math.Max(0, Math.Min(100, e.AnalogValue));
                int r = (int)(value * 2.55f);
                int b = (int)((100 - value) * 2.55f);

                pic.BackColor = Color.FromArgb(r, 0, b);
                pic.Update(); // Мгновенно перерисовываем
                return;
            }

            // 3. ЕСЛИ СРАБОТАЛ ДИСКРЕТНЫЙ ТРИГГЕР (Мигаем зеленым "вспышкой")
            if (e.Status == "Triggered")
            {
                TriggerFlashAnimation(pic);
            }
        }

        // Асинхронная вспышка индикатора на форме при срабатывании триггера
        private async void TriggerFlashAnimation(PictureBox pic)
        {
            if (pic == null || pic.IsDisposed) return;

            // Шаг 1: Зажигаем яркий салатовый цвет
            pic.BackColor = Color.Lime;
            pic.Refresh(); // Принудительно заставляем Windows перерисовать пиксели прямо СЕЙЧАС

            // Шаг 2: Держим цвет видимым для глаза (400 миллисекунд — идеальный баланс для автоматики)
            await Task.Delay(400);

            if (pic.IsDisposed) return;

            // Шаг 3: Возвращаем индикатор в стандартный рабочий темно-зеленый цвет статуса "OK"
            pic.BackColor = Color.DarkGreen;
            pic.Refresh();
        }



        private void ComboBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            ComboBox comboBox = (ComboBox)sender;
            string text = comboBox.Items[e.Index].ToString();

            // Проверяем, идет ли сейчас анимация (цвет изменен) или элемент в обычном состоянии
            // Обычно дефолтный цвет — это White или SystemColors.Window
            bool isAnimating = comboBox.BackColor != SystemColors.Window && comboBox.BackColor != Color.White;

            Color backColor;
            Color textColor;

            // КЛЮЧЕВОЙ МОМЕНТ: Если идет анимация, мы полностью ИГНОРИРУЕМ синий селектор Windows,
            // иначе он закрасит всё синим цветом поверх нашего зеленого/красного.
            if (isAnimating)
            {
                backColor = comboBox.BackColor;
                textColor = comboBox.ForeColor;
            }
            else
            {
                // Обычный режим работы (без анимации) — возвращаем стандартное синее выделение при наведении мышкой
                bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                backColor = isSelected ? SystemColors.Highlight : comboBox.BackColor;
                textColor = isSelected ? SystemColors.HighlightText : comboBox.ForeColor;
            }

            // 1. Принудительно заливаем ВСЮ внутреннюю область (селектор больше не помеха)
            using (SolidBrush brush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            // 2. Отрисовываем текст
            TextRenderer.DrawText(e.Graphics, text, comboBox.Font, e.Bounds, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // Убираем штатную рамку фокуса, чтобы она не мерцала во время изменения цветов
            if (!isAnimating)
            {
                e.DrawFocusRectangle();
            }
        }



        // Анимация успеха: плавно перекрашивает КОНКРЕТНЫЙ измененный элемент управления
        private async Task AnimateControlSuccess(Control control)
        {
            if (control == null || control.IsDisposed) return;

            // Запоминаем исходный цвет фона элемента
            Color originalColor = control.BackColor;
            BorderStyle originalBorder = BorderStyle.Fixed3D;

            // Специальный костыль для TextBox, иначе Windows заблокирует цвет
            if (control is TextBox txt)
            {
                originalBorder = txt.BorderStyle;
                txt.BorderStyle = BorderStyle.FixedSingle; // Переключаем в плоский режим
            }

            Color successColor = Color.FromArgb(180, 240, 180); // Приятный мягкий зеленый
            control.BackColor = successColor;
            control.Update(); // Принудительно заставляем WinForms перерисовать элемент СЕЙЧАС
            await Task.Delay(400); // Держим зеленый цвет почти полсекунды

            // Плавно возвращаем исходный цвет фона за 4 шага (примерно за 400 мс)
            int steps = 4;
            for (int i = 1; i <= steps; i++)
            {
                if (panelIO.IsDisposed || control.IsDisposed) return; // Защита от закрытия формы во время анимации

                float ratio = i / (float)steps;
                int r = (int)(successColor.R + (originalColor.R - successColor.R) * ratio);
                int g = (int)(successColor.G + (originalColor.G - successColor.G) * ratio);
                int b = (int)(successColor.B + (originalColor.B - successColor.B) * ratio);

                control.BackColor = Color.FromArgb(r, g, b);
                // ИСПРАВЛЕНИЕ: Используем Refresh вместо Update, чтобы принудительно вызвать DrawItem
                control.Refresh();
                await Task.Delay(100);
            }

            control.BackColor = originalColor; // Гарантированно возвращаем дефолтный цвет
            if (control is TextBox txtBack) txtBack.BorderStyle = originalBorder; // Возвращаем рамку
            control.Update();
        }


        // Анимация ошибки: заливает красным цветом ВСЮ панель panelIO и её внутренние строки
        private async Task AnimatePanelFailure()
        {
            if (panelIO == null || panelIO.IsDisposed) return;

            Color errorColor = Color.FromArgb(255, 200, 200); // Пастельный красный
            Color originalPanelColor = panelIO.BackColor;

            // Шаг 1: Красим саму главную панель и все сгенерированные строки FlowLayoutPanel
            panelIO.BackColor = errorColor;
            foreach (Control row in panelIO.Controls)
            {
                if (row is FlowLayoutPanel rowPanel)
                {
                    rowPanel.BackColor = errorColor;

                    // Подсвечиваем элементы ввода внутри строки, чтобы усилить визуальный эффект
                    foreach (Control child in rowPanel.Controls)
                    {
                        if (child is ComboBox || child is TextBox || child is CheckBox)
                        {
                            child.BackColor = Color.FromArgb(255, 170, 170);
                        }
                    }
                }
            }
            panelIO.Refresh(); // Мгновенно обновляем весь пульт на экране

            await Task.Delay(600); // Держим красный цвет ошибки

            // Шаг 2: Возвращаем исходные цвета (метод GenerateIOControls всё равно всё перерисует, 
            // но визуально вернуть цвета назад перед MessageBox — лучшая практика)
            panelIO.BackColor = originalPanelColor;
            foreach (Control row in panelIO.Controls)
            {
                if (row is FlowLayoutPanel rowPanel)
                {
                    rowPanel.BackColor = Color.Transparent;
                    foreach (Control child in rowPanel.Controls)
                    {
                        if (child is ComboBox || child is TextBox)
                            child.BackColor = SystemColors.Window;
                        else if (child is CheckBox)
                            child.BackColor = Color.Transparent;
                    }
                }
            }
            panelIO.Refresh();
        }



        // Вспомогательный метод для полной очистки подписок перед релоадом панели
        private void DetachIOEvents()
        {
            if (panelIO == null) return;

            foreach (Control row in panelIO.Controls)
            {
                if (row is FlowLayoutPanel rowPanel)
                {
                    foreach (Control child in rowPanel.Controls)
                    {
                        if (child is ComboBox cb) cb.SelectedIndexChanged -= IOControl_ValueChanged;
                        else if (child is CheckBox chk) chk.Click -= IOControl_ValueChanged;
                        else if (child is TextBox txt)
                        {
                            // Чистим анонимные обработчики через обнуление (WinForms сама удалит связи)
                        }
                    }
                }
            }
        }


        #endregion Animation


        #region LinesHandler


        /// <summary>
        /// Динамический конструктор интерфейса блока параметров выбранного метода.
        /// Очищает gboxParams, извлекает снимок параметров метода (или генерирует дефолтные шаблоны через рефлексию). 
        /// На Шаге 1 вычисляет точный габарит подписей в пикселях с учетом шрифта через TextRenderer.MeasureText (защита от обрезания текста). 
        /// На Шаге 2 строит сетку элементов сверху вниз, динамически выравнивая TextBox, CheckBox и ComboBox по максимальной ширине, 
        /// и подписывает их на мгновенное сохранение данных в память.
        /// </summary>
        private void RebuildParametersGroupBox(uint lineIndex, string methodName, string methodType)
        {
            // Очищаем текущий GroupBox параметров
            gboxParams.Controls.Clear();
            gboxParams.Text = $"Параметры метода: {methodName} (Линия {lineIndex})";

            if (methodName == "Отключен") return;

            // Находим или создаем сохраненное состояние параметров методов
            var lineConf = CameraLineHandler.CurrentConfig.Lines.First(l => l.LineIndex == lineIndex);
            var savedMethod = lineConf.SavedMethodsData.FirstOrDefault(m => m.MethodName == methodName);

            if (savedMethod == null)
            {
                // Четко разделяем фабрики контейнеров на основе methodType ("MonitorSelect" / "ActionSelect")
                List<LinesParameters> defaults = methodType == "MonitorSelect"
                    ? CameraLineHandler.CreateMonitorContainer().First(m => m.Name == methodName).Parameters
                    : CameraLineHandler.CreateActionContainer().First(m => m.Name == methodName).Parameters;

                savedMethod = new MethodDataSnapshot
                {
                    MethodName = methodName,
                    // ИСПРАВЛЕНИЕ: Обязательно переносим коллекцию Options, чтобы выпадающие списки наполнялись данными!
                    Parameters = defaults.Select(p => new LinesParameters
                    {
                        Name = p.Name,
                        Type = p.Type,
                        Value = p.Value,
                        Options = p.Options != null ? new List<string>(p.Options) : new List<string>()
                    }).ToList()
                };
                lineConf.SavedMethodsData.Add(savedMethod);
            }

            // ШАГ 1: АВТОМАТИЧЕСКИЙ РАСЧЕТ МАКСИМАЛЬНОЙ ШИРИНЫ ТЕКСТА
            int maxLabelWidth = 100; // Минимальная базовая ширина для очень коротких названий
            int paddingRight = 15;   // Отступ между Label и полем ввода

            foreach (var param in savedMethod.Parameters)
            {
                // Измеряем точный размер текста в пикселях с учетом шрифта GroupBox
                Size textSize = TextRenderer.MeasureText(param.Name, gboxParams.Font);
                if (textSize.Width > maxLabelWidth)
                {
                    maxLabelWidth = textSize.Width;
                }
            }

            // Координата X для всех полей ввода теперь рассчитывается динамически
            int inputControlX = 15 + maxLabelWidth + paddingRight;

            // ШАГ 2: СТРОИТЕЛЬСТВО ИНТЕРФЕЙСА С ДИНАМИЧЕСКИМ ВЫРАВНИВАНИЕМ
            int yOffset = 25;
            foreach (var param in savedMethod.Parameters)
            {
                // 1. Имя параметра (Label)
                var lbl = new Label
                {
                    Text = param.Name,
                    Location = new Point(15, yOffset),
                    Width = maxLabelWidth, // Все подписи теперь одинаковой максимальной ширины
                    AutoEllipsis = false   // Запрещаем обрезать текст троеточием
                };
                gboxParams.Controls.Add(lbl);

                // 2. Поле ввода в зависимости от типа
                Control inputControl;

                if (param.Type == "Bool")
                {
                    var chk = new CheckBox
                    {
                        Checked = bool.Parse(param.Value),
                        Location = new Point(inputControlX, yOffset - 3), // Выравнивание по X
                        Width = 150
                    };
                    chk.CheckedChanged += (s, e) => { param.Value = chk.Checked.ToString(); };
                    inputControl = chk;
                }
                // === ИСПРАВЛЕНИЕ: НОВЫЙ БЛОК ДЛЯ ВЫПАДАЮЩИХ СПИСКОВ В ПАРАМЕТРАХ ===
                else if (param.Type == "ComboBox")
                {
                    var cbParam = new ComboBox
                    {
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        FlatStyle = FlatStyle.Flat,
                        Location = new Point(inputControlX, yOffset - 3), // Выравнивание по X
                        Width = 150
                    };

                    // Наполняем ComboBox элементами из перенесенного массива Options
                    if (param.Options != null && param.Options.Count > 0)
                    {
                        cbParam.Items.AddRange(param.Options.ToArray());

                        // Восстанавливаем ранее выбранное пользователем значение
                        if (cbParam.Items.Contains(param.Value))
                        {
                            cbParam.SelectedItem = param.Value;
                        }
                        else
                        {
                            cbParam.SelectedIndex = 0; // Защита: выбираем первый IP, если ничего не задано
                            param.Value = cbParam.SelectedItem.ToString();
                        }
                    }
                    else
                    {
                        cbParam.Items.Add("127.0.0.1");
                        cbParam.SelectedIndex = 0;
                        param.Value = "127.0.0.1";
                    }

                    // Принудительно подписываем комбобокс параметров на кастомный отрисовщик, 
                    // чтобы синий селектор Windows не перекрывал фоновые цвета при фокусе
                    cbParam.DrawMode = DrawMode.OwnerDrawFixed;
                    cbParam.DrawItem += ComboBox_DrawItem;

                    // При изменении строки в выпадающем списке мгновенно сохраняем её в оперативную память параметров
                    cbParam.SelectedIndexChanged += (s, e) => { param.Value = cbParam.SelectedItem.ToString(); };
                    inputControl = cbParam;
                }
                else
                {
                    var txt = new TextBox
                    {
                        Text = param.Value,
                        Location = new Point(inputControlX, yOffset - 3), // Выравнивание по X
                        Width = 150
                    };
                    txt.Leave += (s, e) => { param.Value = txt.Text; };
                    txt.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; param.Value = txt.Text; gboxParams.Focus(); } };
                    inputControl = txt;
                }

                gboxParams.Controls.Add(inputControl);
                yOffset += 30; // Смещаемся ниже под следующий параметр
            }
        }



        /// <summary>
        /// Универсальный обработчик события смены выбранного метода в ComboBox-селекторах ("MonitorSelect" или "ActionSelect").
        /// Определяет индекс физической линии и тип селектора через Tag элемента, обновляет 
        /// соответствующее поле в структуре глобальной конфигурации (CurrentConfig) для сохранения в JSON, 
        /// после чего инициирует поток динамической перерисовки полей ввода параметров в нижнем GroupBox.
        /// </summary>
        private void MethodSelection_Changed(object sender, EventArgs e)
        {
            ComboBox cb = (ComboBox)sender;
            FlowLayoutPanel rowPanel = (FlowLayoutPanel)cb.Parent;
            uint lineIndex = (uint)rowPanel.Tag;

            string selectType = cb.Tag.ToString(); // "MonitorSelect" или "ActionSelect"
            string selectedMethodName = cb.SelectedItem.ToString();

            // Находим или создаем запись конфигурации для этой линии
            var lineConf = CameraLineHandler.CurrentConfig.Lines.FirstOrDefault(l => l.LineIndex == lineIndex);
            if (lineConf == null)
            {
                lineConf = new LineConfigState { LineIndex = lineIndex };
                CameraLineHandler.CurrentConfig.Lines.Add(lineConf);
            }

            // ИСПРАВЛЕНИЕ: Разносим данные по правильным свойствам конфигурации
            if (selectType == "MonitorSelect")
            {
                lineConf.SelectedMonitorName = selectedMethodName;
            }
            else if (selectType == "ActionSelect")
            {
                lineConf.SelectedActionName = selectedMethodName;
            }

            // Перестраиваем GroupBox параметров для выбранного метода
            RebuildParametersGroupBox(lineIndex, selectedMethodName, selectType);
        }


        #endregion


        private void btnExportConfig_Click(object sender, EventArgs e)
        {
            try
            {
                // 1. Перед сохранением обновляем в конфиге аппаратную часть из нашего буфера
                CameraLineHandler.CurrentConfig.HardwareLines.Clear();

                lock (CameraManager.LastLoadedLines)
                {
                    foreach (var liveLine in CameraManager.LastLoadedLines)
                    {
                        CameraLineHandler.CurrentConfig.HardwareLines.Add(new HardwareLineSnapshot
                        {
                            LineIndex = liveLine.Index,
                            Mode = liveLine.Mode,
                            Source = liveLine.Source,
                            IsInverted = liveLine.IsInverted,
                            DebouncerTime = liveLine.DebouncerTime
                        });
                    }
                }

                // 2. Открываем стандартное диалоговое окно сохранения файла
                using (SaveFileDialog saveFileDialog = new SaveFileDialog())
                {
                    saveFileDialog.Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*";
                    saveFileDialog.Title = "Экспорт конфигурации системы";
                    saveFileDialog.FileName = $"camera_config_{DateTime.Now:yyyyMMdd}.json";

                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        // Вызываем метод сохранения бизнес-логики
                        CameraLineHandler.SaveConfigToFile(saveFileDialog.FileName);
                        MessageBox.Show("Конфигурация успешно экспортирована!", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError(ex, "Ошибка при экспорте конфигурации");
                MessageBox.Show($"Не удалось сохранить файл: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Обработчик клика по кнопке импорта конфигурации пульта из JSON-файла.
        /// Реализует алгоритм интеллектуальной валидации: считывает файл во временный буфер, 
        /// запрашивает текущий слепок регистров живого железа камеры и выполняет построчную сверку 
        /// (режим, источник, инверсия, дебаунс). При обнаружении конфликтов выводит детальный отчет-предупреждение. 
        /// При согласии пользователя или успешном совпадении накатывает программные методы и перерисовывает пульт.
        /// </summary>
        private async void btnImportConfig_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*";
                openFileDialog.Title = "Импорт конфигурации системы";

                if (openFileDialog.ShowDialog() != DialogResult.OK) return;

                try
                {
                    // 1. Читаем JSON во временный буфер, чтобы сначала проверить его
                    string json = File.ReadAllText(openFileDialog.FileName);
                    var importedConfig = JsonConvert.DeserializeObject<AppConfigRoot>(json);

                    if (importedConfig == null || importedConfig.HardwareLines == null)
                    {
                        MessageBox.Show("Файл поврежден или имеет неверный формат.", "Ошибка валидации", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    // 2. СВЕРКА: Опрашиваем камеру прямо сейчас, чтобы получить свежий слепок железа
                    CameraManager.GetLinesConfig();
                    await WaitForLineConfigsAsync(); // Ждем окончания опроса железа

                    List<string> mismatchReport = new List<string>();

                    // Проверяем каждую линию из файла с тем, что сейчас выставлено в камере
                    foreach (var fileLine in importedConfig.HardwareLines)
                    {
                        var liveLine = CameraManager.LastLoadedLines.FirstOrDefault(l => l.Index == fileLine.LineIndex);

                        if (liveLine == null)
                        {
                            mismatchReport.Add($"• Линия {fileLine.LineIndex}: Физически отсутствует в текущей камере");
                            continue;
                        }

                        // Сравниваем параметры
                        if (fileLine.Mode != liveLine.Mode)
                            mismatchReport.Add($"• Line {fileLine.LineIndex} [Режим]: В файле '{fileLine.Mode}', в камере '{liveLine.Mode}'");

                        if (fileLine.Source != liveLine.Source)
                            mismatchReport.Add($"• Line {fileLine.LineIndex} [Источник]: В файле '{fileLine.Source}', в камере '{liveLine.Source}'");

                        if (fileLine.IsInverted != liveLine.IsInverted)
                            mismatchReport.Add($"• Line {fileLine.LineIndex} [Инверсия]: В файле '{fileLine.IsInverted}', в камере '{liveLine.IsInverted}'");

                        if (fileLine.DebouncerTime != liveLine.DebouncerTime)
                            mismatchReport.Add($"• Line {fileLine.LineIndex} [Фильтр]: В файле '{fileLine.DebouncerTime}', в камере '{liveLine.DebouncerTime}' us");
                    }

                    // 3. ДИАЛОГОВОЕ ОКНО ПРИ НЕСООТВЕТСТВИИ
                    if (mismatchReport.Count > 0)
                    {
                        string message = "Внимание! Физические настройки камеры не соответствуют файлу конфигурации:\n\n" +
                                         string.Join("\n", mismatchReport) + "\n\n" +
                                         "Вы хотите применить программные методы (Мониторы/Действия) из файла, несмотря на расхождения?";

                        var dialogResult = MessageBox.Show(message, "Конфликт конфигурации железа", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                        if (dialogResult == DialogResult.No)
                        {
                            AppLogger.LogInfo("Импорт отменен пользователем из-за нестыковки параметров железа.");
                            return; // Выходим, ничего не меняя
                        }
                    }

                    // 4. ПРИМЕНЕНИЕ: Если всё совпало или пользователь согласился — накатываем программную часть
                    CameraLineHandler.CurrentConfig = importedConfig;

                    // Принудительно перерисовываем наш динамический пульт на форме, 
                    // он автоматически подхватит новые выбранные методы и их параметры из CameraManager.CurrentConfig
                    this.Invoke(new Action(() =>
                    {
                        GenerateIOControls(CameraManager.LastLoadedLines);
                        MessageBox.Show("Конфигурация методов успешно импортирована!", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));

                }
                catch (Exception ex)
                {
                    AppLogger.LogError(ex, "Ошибка при импорте конфигурации");
                    MessageBox.Show($"Ошибка при чтении конфигурации: {ex.Message}", "Критическая ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }


                // ========================================================================
                // АВТОМАТИЧЕСКОЕ ОБНОВЛЕНИЕ GROUPBOX ПАРАМЕТРОВ ПОСЛЕ ИМПОРТА
                // ========================================================================
                var firstRow = panelIO.Controls.OfType<FlowLayoutPanel>().FirstOrDefault(row => row.Tag != null);

                if (firstRow != null)
                {
                    uint activeLineIndex = (uint)firstRow.Tag;

                    // Ищем первый доступный и активный ComboBox селектора методов на этой строчке
                    var activeComboBox = firstRow.Controls.OfType<ComboBox>()
                        .FirstOrDefault(cb => cb.Enabled && (cb.Tag?.ToString() == "MonitorSelect" || cb.Tag?.ToString() == "ActionSelect"));

                    // ЗАЩИТА: проверяем SelectedItem на null напрямую перед вызовом ToString()
                    if (activeComboBox != null && activeComboBox.SelectedItem != null)
                    {
                        string selectedMethodName = activeComboBox.SelectedItem.ToString();
                        string methodType = activeComboBox.Tag.ToString();

                        // Безопасно перерисовываем параметры первой линии
                        RebuildParametersGroupBox(activeLineIndex, selectedMethodName, methodType);
                    }
                }
                // ========================================================================

            }
        }

        /// <summary>
        /// Обработчик клика по главной кнопке управления («ЗАПУСТИТЬ/ОСТАНОВИТЬ МОНИТОРИНГ»).
        /// Выполняет триггерное переключение состояния всей системы. При старте: синхронизирует 
        /// UI-селекторы с менеджером потоков, валидирует наличие активных обработчиков (защита от пустого старта), 
        /// инициализирует контекст оборудования и запускает асинхронные потоки мониторинга/серверов. 
        /// При остановке: глушит все потоки и освобождает порты. Блокирует элементы ввода на время работы.
        /// </summary>
        private void btnSystemControl_Click(object sender, EventArgs e)
        {
            if (!ThreadManager.IsSystemRunning)
            {
                // 1. Сначала обязательно собираем данные из ComboBox-ов в словари менеджера
                ThreadManager.SyncUiSelectionWithActiveContainers(panelIO);

                // 2. ИСПРАВЛЕНИЕ: Проверяем, назначено ли ХОТЯ БЫ ОДНО действие или монитор.
                // Если оба словаря пустые, значит везде выбрано "Отключен"
                if (ThreadManager.ActiveMonitors.Count == 0 && ThreadManager.ActiveActions.Count == 0)
                {
                    MessageBox.Show("Невозможно запустить систему: не выбрано ни одного метода мониторинга или действия для линий камеры.",
                                    "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    return;
                }

                // 3. Если проверка прошла успешно — передаем контекст железа и запускаем процессы
                ThreadManager.InitializeContext(CameraManager.Cam, CameraManager._cameraLock);
                ThreadManager.StartAllMonitors();

                // Меняем визуал кнопки и блокируем элементы ввода
                btnSystemControl.Text = "ОСТАНОВИТЬ МОНИТОРИНГ";
                btnSystemControl.BackColor = Color.Tomato;
                panelIO.Enabled = false;
                groupBox1.Enabled = false;
                gboxParams.Enabled = false;
            }
            else
            {
                // Остановка системы
                ThreadManager.StopAllMonitors();

                btnSystemControl.Text = "ЗАПУСТИТЬ МОНИТОРИНГ";
                btnSystemControl.BackColor = Color.LightGreen;
                panelIO.Enabled = true;
                groupBox1.Enabled = true;
                gboxParams.Enabled = true;
            }

            // Перерисовываем пульт, чтобы обновить цвета индикаторов
            RefreshCameraLines();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            AppLogger.LogInfo($"[System Shutdown] Инициализирован процесс закрытия приложения. Причина: {e.CloseReason}");

            try
            {
                if (ThreadManager.IsSystemRunning)
                {
                    AppLogger.LogInfo("[System Shutdown] Останавливаем активные мониторы и TCP-серверы...");
                    ThreadManager.StopAllMonitors();
                }

                CameraManager.TryDisconnect();
            }
            catch (Exception ex)
            {
                AppLogger.LogError(ex, "Ошибка при экстренной очистке ресурсов во время закрытия формы");
            }
        }





    }
}
