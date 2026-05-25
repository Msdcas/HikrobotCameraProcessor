using MvCamCtrl.NET;
using MvCameraControl;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Net;
using System.Runtime.InteropServices;
using static HikrobotCameraProcessor.FromSamples.HrCameraSearcher;

// в ходе реализации полагались на пример Grab_GigEMultiCast

namespace HikrobotCameraProcessor.FromSamples
{
    internal static class HrCameraSearcher
    {
        const DeviceTLayerType devLayerType = DeviceTLayerType.MvGigEDevice;
        public static bool _bExit = false;

        public static BindingList<CameraData> CameraDatas = new BindingList<CameraData>();

        public struct CameraData
        {
            public IPAddress IPAddress { get; }
            // Храним напрямую структуру, которую понимает SDK
            public MyCamera.MV_CC_DEVICE_INFO DeviceInfo { get; }
            public string ModelName { get; }      // Добавили
            public string CameraSeries { get; }   // Добавили
            public string ErrorMessage { get; }
            public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
            public string DisplayName => GetDescription();

            // Главный конструктор для успешного поиска (Добавили modelName и series)
            public CameraData(MyCamera.MV_CC_DEVICE_INFO deviceInfo, IPAddress ipAddress, string modelName, string series)
            {
                DeviceInfo = deviceInfo;
                IPAddress = ipAddress;
                ModelName = modelName;
                CameraSeries = series;
                ErrorMessage = null;
            }

            // Конструктор для ошибок
            public CameraData(string error)
            {
                DeviceInfo = default;
                IPAddress = IPAddress.None;
                ModelName = "Unknown";
                CameraSeries = "Unknown";
                ErrorMessage = error;
            }

            public string GetDescription()
            {
                if (!IsSuccess) return ErrorMessage;

                // Переписываем вывод: лаконично, с серией, моделью и IP
                return $"[{CameraSeries}] {ModelName} — {IPAddress}";
            }

            // Переопределяем ToString, чтобы ComboBox автоматически подхватывал DisplayName (или GetDescription)
            public override string ToString()
            {
                return GetDescription();
            }
        }



        //public static string GetDescription()
        //{
        //    //if (!IsSuccess) return $"[ERROR] {ErrorMessage}";

        //    string model = "Unknown Model";
        //    string sn = "No S/N";

        //    // 1. Если камера подключена по Ethernet (GigE)
        //    if (DeviceInfo.nTLayerType == MyCamera.MV_GIGE_DEVICE)
        //    {
        //        var gigeInfo = DeviceInfo.SpecialInfo.stGigEInfo;

        //        // В SDK Hikrobot строки внутри структур часто лежат в виде массивов байт,
        //        // поэтому преобразуем их в C#-строки, обрезая лишние нули (\0)
        //        model = System.Text.Encoding.UTF8.GetString(gigeInfo.chModelName).TrimEnd('\0').Trim();
        //        sn = System.Text.Encoding.UTF8.GetString(gigeInfo.chSerialNumber).TrimEnd('\0').Trim();

        //        return $"{model} ({IPAddress}) SN:{sn}";
        //    }

        //    // 2. Если камера подключена по USB
        //    //if (DeviceInfo.nTLayerType == MyCamera.MV_USB_DEVICE)
        //    //{
        //    //    var usbInfo = DeviceInfo.SpecialInfo.stUsb3VInfo;

        //    //    model = System.Text.Encoding.UTF8.GetString(usbInfo.chModelName).TrimEnd('\0').Trim();
        //    //    sn = System.Text.Encoding.UTF8.GetString(usbInfo.chSerialNumber).TrimEnd('\0').Trim();

        //    //    return $"[USB] {model} SN:{sn}";
        //    //}

        //    return "Unknown Device Type";
        //}



        public static string[] GetCameraDescriptions()
        {
            return CameraDatas
                .Select(c => c.GetDescription())
                .ToArray();
        }


        static void WorkThread(object obj)
        {
            IStreamGrabber streamGrabber = (IStreamGrabber)obj;
            while (true)
            {
                IFrameOut frame;

                //ch：获取一帧图像 | en: Get one frame
                int ret = streamGrabber.GetImageBuffer(1000, out frame);
                if (ret == MvError.MV_OK)
                {
                    Console.WriteLine("Get one frame: Width[{0}] , Height[{1}] , FrameNum[{2}]", frame.Image.Width, frame.Image.Height, frame.FrameNum);

                    //ch: 释放图像缓存  | en: Release the image buffer
                    streamGrabber.FreeImageBuffer(frame);
                }
                else
                {
                    Console.WriteLine("Get Image failed:{0:x8}", ret);
                }

                if (_bExit)
                {
                    break;
                }
            }
        }

        public static bool RefreshList()
        {
            CameraDatas.Clear();
            int ret = MvError.MV_OK;

            try
            {
                List<IDeviceInfo> devInfoList;
                ret = DeviceEnumerator.EnumDevices(DeviceTLayerType.MvGigEDevice, out devInfoList);

                if (ret != MvError.MV_OK)
                {
                    return false;
                }

                if (devInfoList == null || devInfoList.Count == 0)
                {
                    return false;
                }

                foreach (var devInfo in devInfoList)
                {
                    IPAddress iPAddress = IPAddress.None;
                    MyCamera.MV_CC_DEVICE_INFO stDeviceInfo = new MyCamera.MV_CC_DEVICE_INFO();

                    // 1. Вытаскиваем имя модели из объектного API (оно всегда заполнено)
                    string modelName = devInfo.ModelName ?? "Unknown";
                    string cameraSeries = "MV-Series"; // Базовый вариант

                    // ПАРСЕР СЕРИИ: Смотрим на префикс названия модели
                    if (modelName.StartsWith("MV-"))
                    {
                        string subStr = modelName.Substring(3);
                        if (subStr.StartsWith("CE")) cameraSeries = "MV-CE";
                        else if (subStr.StartsWith("CH")) cameraSeries = "MV-CH";
                        else if (subStr.StartsWith("CS")) cameraSeries = "MV-CS";
                        else if (subStr.StartsWith("CA")) cameraSeries = "MV-CA";
                    }

                    // 2. Извлекаем низкоуровневую структуру и IP-адрес для GigE
                    if (devInfo is IGigEDeviceInfo gigeDevInfo)
                    {
                        byte[] ipBytes = BitConverter.GetBytes(gigeDevInfo.CurrentIp).Reverse().ToArray();
                        iPAddress = new IPAddress(ipBytes);
                        stDeviceInfo.nTLayerType = MyCamera.MV_GIGE_DEVICE;
                    }

                    // 3. Создаем структуру ОДИН РАЗ, передавая все вычисленные параметры
                    // Так как это структура, C# скопирует её в список побитово, ничего не зануляя
                    CameraDatas.Add(new CameraData(stDeviceInfo, iPAddress, modelName, cameraSeries));
                }


            }
            catch (Exception ex)
            {
                AppLogger.LogError(ex,"Ошибка обновления списка камер");
            }

            return true;
        }



        private static void Main_FromSample(string[] args)
        {
            {
                int ret = MvError.MV_OK;
                IDevice device = null;

                // ch: 初始化 SDK | en: Initialize SDK
                SDKSystem.Initialize();

                try
                {
                    List<IDeviceInfo> devInfoList;

                    // ch:枚举设备 | en:Enum device
                    ret = DeviceEnumerator.EnumDevices(devLayerType, out devInfoList);
                    if (ret != MvError.MV_OK)
                    {
                        Console.WriteLine("Enum device failed:{0:x8}", ret);
                        return;
                    }

                    Console.WriteLine("Enum device count : {0}", devInfoList.Count);

                    if (0 == devInfoList.Count)
                    {
                        return;
                    }

                    // ch:打印设备信息 en:Print device info
                    int devIndex = 0;
                    foreach (var devInfo in devInfoList)
                    {
                        Console.WriteLine("[Device {0}]:", devIndex);
                        if (devInfo.TLayerType == DeviceTLayerType.MvGigEDevice || devInfo.TLayerType == DeviceTLayerType.MvVirGigEDevice || devInfo.TLayerType == DeviceTLayerType.MvGenTLGigEDevice)
                        {
                            IGigEDeviceInfo gigeDevInfo = devInfo as IGigEDeviceInfo;
                            uint nIp1 = ((gigeDevInfo.CurrentIp & 0xff000000) >> 24);
                            uint nIp2 = ((gigeDevInfo.CurrentIp & 0x00ff0000) >> 16);
                            uint nIp3 = ((gigeDevInfo.CurrentIp & 0x0000ff00) >> 8);
                            uint nIp4 = (gigeDevInfo.CurrentIp & 0x000000ff);
                            Console.WriteLine("DevIP: {0}.{1}.{2}.{3}", nIp1, nIp2, nIp3, nIp4);
                        }

                        Console.WriteLine("ModelName:" + devInfo.ModelName);
                        Console.WriteLine("SerialNumber:" + devInfo.SerialNumber);
                        Console.WriteLine();
                        devIndex++;
                    }

                    Console.Write("Please input index(0-{0:d}):", devInfoList.Count - 1);

                    devIndex = Convert.ToInt32(Console.ReadLine());

                    if (devIndex > devInfoList.Count - 1 || devIndex < 0)
                    {
                        Console.Write("Input Error!\n");
                        return;
                    }

                    // ch:创建设备 | en:Create device
                    device = DeviceFactory.CreateDevice(devInfoList[devIndex]);

                    // ch:查询用户使用的模式
                    // Query the user for the mode to use.
                    bool monitorMode = false;
                    {
                        string key = "";

                        // Ask the user to launch the multicast controlling application or the multicast monitoring application.
                        Console.WriteLine("Start multicast sample in (c)ontrol or in (m)onitor mode? (c/m)");
                        do
                        {
                            key = Convert.ToString(Console.ReadLine());
                        }
                        while ((key != "c") && (key != "m") && (key != "C") && (key != "M"));
                        monitorMode = (key == "m") || (key == "M");
                    }

                    // ch:打开设备 | en:Open device
                    if (monitorMode)
                    {
                        ret = device.Open(DeviceAccessMode.AccessMonitor, 0);
                        if (ret != MvError.MV_OK)
                        {
                            Console.WriteLine("Open device failed:{0:x8}", ret);
                            return;
                        }
                    }
                    else
                    {
                        ret = device.Open(DeviceAccessMode.AccessControl, 0);
                        if (ret != MvError.MV_OK)
                        {
                            Console.WriteLine("Open device failed:{0:x8}", ret);
                            return;
                        }
                    }

                    // ch:探测网络最佳包大小(只对GigE相机有效) | en:Detection network optimal package size(It only works for the GigE camera)
                    if (device is IGigEDevice)
                    {
                        int packetSize;
                        ret = (device as IGigEDevice).GetOptimalPacketSize(out packetSize);
                        if (packetSize > 0)
                        {
                            ret = device.Parameters.SetIntValue("GevSCPSPacketSize", packetSize);
                            if (ret != MvError.MV_OK)
                            {
                                Console.WriteLine("Warning: Set Packet Size failed {0:x8}", ret);
                            }
                            else
                            {
                                Console.WriteLine("Set PacketSize to {0}", packetSize);
                            }
                        }
                        else
                        {
                            Console.WriteLine("Warning: Get Packet Size failed {0:x8}", ret);
                        }
                    }

                    // ch:指定组播ip | en:The specified multicast IP
                    string strIp = "239.192.1.1";
                    var parts = strIp.Split('.');
                    int nDestIp1 = Convert.ToInt32(parts[0]);
                    int nDestIp2 = Convert.ToInt32(parts[1]);
                    int nDestIp3 = Convert.ToInt32(parts[2]);
                    int nDestIp4 = Convert.ToInt32(parts[3]);
                    int nDestIp = (nDestIp1 << 24) | (nDestIp2 << 16) | (nDestIp3 << 8) | nDestIp4;

                    ret = (device as IGigEDevice).SetTransmissionType(TransmissionType.Multicast, (uint)nDestIp, 1024);
                    if (ret != MvError.MV_OK)
                    {
                        Console.WriteLine("Set transmission type failed:{0:x8}", ret);
                        return;
                    }

                    // ch:开启抓图 | en: start grab image
                    ret = device.StreamGrabber.StartGrabbing();
                    if (ret != MvError.MV_OK)
                    {
                        Console.WriteLine("Start grabbing failed:{0:x8}", ret);
                        return;
                    }

                    Thread thr = new Thread(WorkThread);
                    thr.Start(device.StreamGrabber);

                    Console.WriteLine("Press enter to exit");
                    Console.ReadLine();

                    _bExit = true;
                    thr.Join();

                    // ch:停止抓图 | en:Stop grabbing
                    ret = device.StreamGrabber.StopGrabbing();
                    if (ret != MvError.MV_OK)
                    {
                        Console.WriteLine("Stop grabbing failed:{0:x8}", ret);
                        return;
                    }

                    // ch:关闭设备 | en:Close device
                    ret = device.Close();
                    if (ret != MvError.MV_OK)
                    {
                        Console.WriteLine("Close device failed:{0:x8}", ret);
                        return;
                    }

                    // ch:销毁设备 | en:Destroy device
                    device.Dispose();
                    device = null;
                }
                catch (Exception e)
                {
                    Console.Write("Exception: " + e.Message);
                }
                finally
                {
                    // ch:销毁设备 | en:Destroy device
                    if (device != null || ret != MvError.MV_OK)
                    {
                        device.Dispose();
                        device = null;
                    }

                    // ch: 反初始化SDK | en: Finalize SDK
                    SDKSystem.Finalize();

                    Console.WriteLine("Press enter to exit");
                    Console.ReadKey();
                }


            }
        }

    }

}
