using Constellation;
using Constellation.Package;
using InTheHand.Bluetooth;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using XiaomiMiTemperatureAndHumiditySensor.Core;

namespace XiaomiThermometer
{
    public class Program : PackageBase
    {
        private CancellationTokenSource _ct = new CancellationTokenSource();
        static void Main(string[] args)
        {
            PackageHost.Start<Program>(args);
        }

        private async Task<bool> TryConnect(BluetoothDevice mi_device)
        {
            int attempts = PackageHost.GetSettingValue<int>("miDeviceMaxConnectionAttempts");
            for (int i = 0; i < attempts; i++)
            {
                PackageHost.WriteInfo($"Trying to connect to {mi_device.Name}/{mi_device.Id} ... (attempt n°{i + 1})");
                await mi_device.Gatt.ConnectAsync();
                if (mi_device.Gatt.IsConnected)
                {
                    return true;
                }
                PackageHost.WriteInfo($"attempt to connect failed");
                await Task.Delay(100);
            }
            return false;
        }
        private async Task<GattCharacteristic> GetTemperatureCharacteristic(BluetoothDevice mi_device)
        {
            var mainService = await mi_device.Gatt.GetPrimaryServiceAsync(BluetoothUuid.FromGuid(Guid.Parse("ebe0ccb0-7a0a-4b0c-8a1a-6ff2997da3a6")));
            var tempCharacteristic = await mainService.GetCharacteristicAsync(BluetoothUuid.FromGuid(Guid.Parse("ebe0ccc1-7a0a-4b0c-8a1a-6ff2997da3a6")));
            return tempCharacteristic;
        }

        private SensorInformations GetSensorInformationsFromRawValue(byte[] raw)
        {
            return new SensorInformations()
            {
                Temperature = ((raw[1] & 0x7F) << 8 | raw[0]) / 100.0,
                Moisture = raw[2]
            };
        }

        private void PublishSO(string deviceId, string deviceName, MiDeviceData sensorInformations)
        {
            string displayName = string.Join("/", deviceName, deviceId).Trim('/');
            PackageHost.WriteInfo($"device {displayName} : {sensorInformations}");
            PackageHost.PushStateObject($"{displayName}", sensorInformations, lifetime: 60 * 60);
        }

        //private async Task GetBatterySavingTaskAsync(string device_id, CancellationToken ct)
        //{
        //    while (!ct.IsCancellationRequested)
        //    {
        //        BluetoothDevice mi_device = await BluetoothDevice.FromIdAsync(device_id);
        //        PackageHost.WriteInfo($"device {mi_device.Name}/{mi_device.Id} found");
        //        bool connected = await TryConnect(mi_device);

        //        if (!connected || !mi_device.Gatt.IsConnected)
        //        {
        //            PackageHost.WriteError($"Failed to connect to {mi_device.Name}/{mi_device.Id}");
        //            return;
        //        }

        //        PackageHost.WriteInfo($"device {mi_device.Name}/{mi_device.Id} connected");

        //        TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
        //        var tempCharacteristic = await GetTemperatureCharacteristic(mi_device);
        //        if (tempCharacteristic == null)
        //        {
        //            PackageHost.WriteWarn($"device {mi_device.Name}/{mi_device.Id} cannot access temperature");
        //        }
        //        else
        //        {
        //            await tempCharacteristic.StartNotificationsAsync();
        //            tempCharacteristic.CharacteristicValueChanged += async (sender, evt) =>
        //            {
        //                if (tcs.Task.IsCompleted) return;

        //                SensorInformations sensorInformations = GetSensorInformationsFromRawValue(evt.Value);
        //                PublishSO(mi_device, sensorInformations);
        //                tcs.SetResult(null);
        //            };
        //            await tcs.Task;
        //            await tempCharacteristic.StopNotificationsAsync();

        //            TaskCompletionSource<object> tcs2 = new TaskCompletionSource<object>();
        //            mi_device.GattServerDisconnected += (sender, e) =>
        //            {
        //                if (tcs2.Task.IsCompleted) return;

        //                PackageHost.WriteInfo($"device {mi_device.Name}/{mi_device.Id} disconnected");
        //                tcs2.SetResult(null);
        //            };
        //            mi_device.Gatt.Disconnect();
        //            await tcs.Task;
        //        }

        //        GC.SuppressFinalize(mi_device);
        //        PackageHost.WriteInfo($"device {mi_device.Name}/{mi_device.Id} waiting for next fetching");

        //        await Task.Delay(PackageHost.GetSettingValue<int>("saveBatteryLifeFetchingInterval") * 1000, ct);
        //    }
        //}

        //private async Task GetTaskAsync(BluetoothDevice mi_device)
        //{
        //    PackageHost.WriteInfo($"device {mi_device.Name}/{mi_device.Id} found");
        //    bool connected = await TryConnect(mi_device);

        //    if (!connected || !mi_device.Gatt.IsConnected)
        //    {
        //        PackageHost.WriteError($"Failed to connect to {mi_device.Name}/{mi_device.Id}");
        //        return;
        //    }

        //    PackageHost.WriteInfo($"device {mi_device.Name}/{mi_device.Id} connected");
        //    mi_device.GattServerDisconnected += async (sender, evt) =>
        //    {
        //        PackageHost.WriteWarn($"device {mi_device.Name}/{mi_device.Id} disconnected, trying to reconnect ...");

        //        while (!mi_device.Gatt.IsConnected)
        //        {
        //            await Task.Delay(1000);
        //            await mi_device.Gatt.ConnectAsync();
        //        }
        //        PackageHost.WriteWarn($"device {mi_device.Name}/{mi_device.Id} reconnected");

        //    };

        //    var tempCharacteristic = await GetTemperatureCharacteristic(mi_device);
        //    await tempCharacteristic.StartNotificationsAsync();

        //    tempCharacteristic.CharacteristicValueChanged += (sender, evt) =>
        //    {
        //        SensorInformations sensorInformations = GetSensorInformationsFromRawValue(evt.Value);
        //        PublishSO(mi_device, sensorInformations);
        //    };
        //}
        public override void OnPreShutdown()
        {
            _ct.Cancel();
            base.OnPreShutdown();
        }

        [MessageCallback]
        public IEnumerable<DeviceInformation> GetDevices()
        {
            return Discovery.DiscoverDevices().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public override void OnStart()
        {
            PackageHost.WriteInfo("Package starting - IsRunning: {0} - IsConnected: {1}", PackageHost.IsRunning, PackageHost.IsConnected);
            Task.Run(async () =>
            {
                if (PackageHost.TryGetSettingAsJsonObject("miDeviceIds", out Dictionary<string, string> ids))
                {
                    PackageHost.WriteInfo("Fetching preconfigured devices ...");
                    while (PackageHost.IsRunning)
                    {
                        foreach (KeyValuePair<string, string> pair in ids)
                        {
                            try
                            {
                                var result = await Discovery.GetData(pair.Value);
                                PublishSO(string.Empty, pair.Key, result);
                            }
                            catch (Exception ex)
                            {
                                PackageHost.WriteWarn($"An error occured while fetching device ${pair.Key}", ex);
                            }
                        }

                        int interval = PackageHost.GetSettingValue<int>("saveBatteryLifeFetchingInterval");
                        PackageHost.WriteInfo($"Waiting for {interval} seconds before next fetch");
                        await Task.Delay(interval * 1000);
                    }
                }
                else
                {
                    PackageHost.WriteInfo("No preconfigured devices, discovering Mi devices (may take a minute) ...");
                    IEnumerable<DeviceInformation> devices = await Discovery.DiscoverDevices();
                    PackageHost.WriteInfo($"{devices.Count()} devices found");
                    while (PackageHost.IsRunning)
                    {
                        foreach (var device in devices)
                        {
                            var result = await Discovery.GetData(device);
                            PublishSO(device.Id, device.Name, result);
                        }

                        int interval = PackageHost.GetSettingValue<int>("saveBatteryLifeFetchingInterval");
                        PackageHost.WriteInfo($"Waiting for {interval} seconds before next fetch");
                        await Task.Delay(interval * 1000);
                    }
                    PackageHost.WriteWarn("Package has stopped !");
                }
            });
        }
    }
}
