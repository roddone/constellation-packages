using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace XiaomiMiTemperatureAndHumiditySensor.Core
{
    public static class Discovery
    {
        private static Guid _characteristicGuid = Guid.Parse("ebe0ccc1-7a0a-4b0c-8a1a-6ff2997da3a6");
        private static Guid _serviceUuid = Guid.Parse("ebe0ccb0-7a0a-4b0c-8a1a-6ff2997da3a6");
        private static Guid BATTERY_SERVICE_UUID = Guid.Parse("0000180f-0000-1000-8000-00805f9b34fb");
        private static Guid BATTERY_LEVEL_UUID = Guid.Parse("00002a19-0000-1000-8000-00805f9b34fb");

        public static async Task<IEnumerable<DeviceInformation>> DiscoverDevices()
        {
            //string BluetoothDeviceSelector = "System.Devices.DevObjectType:=5 AND System.Devices.Aep.ProtocolId:=\"{E0CBF06C-CD8B-4647-BB8A-263B43F0F974}\"";
            string BluetoothLEDeviceSelector = "System.Devices.DevObjectType:=5 AND System.Devices.Aep.ProtocolId:=\"{BB7BB05E-5972-42B5-94FC-76EAA7084D49}\"";
            var ledevices = await DeviceInformation.FindAllAsync(BluetoothLEDeviceSelector);
            return ledevices.Where(d => d.Name == "LYWSD03MMC");
        }

        private static byte[] ExtractBytesFromBuffer(Windows.Storage.Streams.IBuffer buffer)
        {
            List<byte> data = new List<byte>(capacity: (int)buffer.Length);
            using (Windows.Storage.Streams.DataReader dr = Windows.Storage.Streams.DataReader.FromBuffer(buffer))
            {
                while (dr.UnconsumedBufferLength > 0)
                {
                    data.Add(dr.ReadByte());
                }
            }

            return data.ToArray();
        }

        private static MiDeviceData ReadDatas(Windows.Storage.Streams.IBuffer buffer)
        {
            var data = ExtractBytesFromBuffer(buffer);

            byte[] readableValue = data.ToArray();
            double temperature = ((readableValue[1] & 0x7F) << 8 | readableValue[0]) / 100.0;
            byte moisture = readableValue[2];

            return new MiDeviceData()
            {
                Temperature = temperature,
                Moisture = moisture
            };
        }

        private static async Task<short?> GetBattery(BluetoothLEDevice mi_device)
        {
            var servicesContainer = await mi_device.GetGattServicesForUuidAsync(BATTERY_SERVICE_UUID);

            if (servicesContainer?.Services?.Count == 0)
            {
                return null;
            }
            using (var service = servicesContainer.Services.First())
            {
                var characteristicContainer = await service.GetCharacteristicsForUuidAsync(BATTERY_LEVEL_UUID);
                if (characteristicContainer?.Characteristics.Count == 0)
                {
                    return null;
                }

                var carac = characteristicContainer.Characteristics.First();
                var battery = await carac.ReadValueAsync();

                byte[] batteryLevel = ExtractBytesFromBuffer(battery.Value);
                return Convert.ToInt16(batteryLevel[0]);
            }
        }

        private static async Task<GattDeviceService> GetService(BluetoothLEDevice mi_device)
        {
            var servicesContainer = await mi_device.GetGattServicesForUuidAsync(_serviceUuid);

            if (servicesContainer?.Services?.Count == 0)
            {
                return null;
            }
            return servicesContainer.Services.First();
        }

        private static async Task<GattCharacteristic> GetCharacteristic(GattDeviceService service)
        {
            var characteristicContainer = await service.GetCharacteristicsForUuidAsync(_characteristicGuid);
            if (characteristicContainer?.Characteristics.Count == 0)
            {
                return null;
            }

            return characteristicContainer.Characteristics.First();
        }
        public static async Task<MiDeviceData> GetData(string deviceId)
        {
            using (var mi_device = await BluetoothLEDevice.FromIdAsync(deviceId))
            {
                return await GetData(mi_device);
            }
        }
        public static async Task<MiDeviceData> GetData(DeviceInformation device) => await GetData(device.Id);
        public static async Task<MiDeviceData> GetData(BluetoothLEDevice mi_device)
        {
            TaskCompletionSource<MiDeviceData> tcs = new TaskCompletionSource<MiDeviceData>();
            var service = await GetService(mi_device);
            using (service)
            {
                var characteristic = await GetCharacteristic(service);
                var data = await GetSingleValueFromCharacteristic(characteristic);
                MiDeviceData deviceDatas = ReadDatas(data);
               

                //deviceDatas.BatteryLevel = await GetBattery(mi_device);
                return deviceDatas;
            }
        }

        public static async Task<CancellationToken> Subscribe(string deviceId, IProgress<MiDeviceData> progress)
        {
            var mi_device = await BluetoothLEDevice.FromIdAsync(deviceId);
            return await Subscribe(mi_device, progress);
        }
        public static async Task<CancellationToken> Subscribe(DeviceInformation device, IProgress<MiDeviceData> progress)
            => await Subscribe(device.Id, progress);

        private static async Task<IBuffer> GetSingleValueFromCharacteristic(GattCharacteristic characteristic)
        {
            TaskCompletionSource<Windows.Storage.Streams.IBuffer> tcs = new TaskCompletionSource<Windows.Storage.Streams.IBuffer>();

            await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);

            characteristic.ValueChanged += async (sender, evt) =>
            {
                await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                tcs.SetResult(evt.CharacteristicValue);
            };

            return await tcs.Task;
        }

        public static async Task<CancellationToken> Subscribe(BluetoothLEDevice mi_device, IProgress<MiDeviceData> progress)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            using (var service = await GetService(mi_device))
            {
                var characteristic = await GetCharacteristic(service);
                await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);

                characteristic.ValueChanged += async (sender, evt) =>
                {
                    var deviceDatas = ReadDatas(evt.CharacteristicValue);
                    progress.Report(deviceDatas);
                    if (cts.Token.IsCancellationRequested)
                    {
                        await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                        mi_device.Dispose();
                    }
                };
            }

            return cts.Token;
        }

        public static async Task<MiDeviceData> Do()
        {
            IEnumerable<DeviceInformation> devices = await DiscoverDevices();

            foreach (DeviceInformation device in devices)
            {
                using (var mi_device = await BluetoothLEDevice.FromIdAsync(device.Id))
                {
                    MiDeviceData datas = await GetData(mi_device);
                    Console.WriteLine($"device : {device.Id} : {datas.Moisture}/{datas.Temperature}");
                }
            }
            return null;
        }
    }
    public class MiDeviceData
    {
        public double Temperature { get; set; }
        public double Moisture { get; set; }

        //public short? BatteryLevel { get; set; }

        public override string ToString()
        {
            return $"temperature : {Temperature}°C | moisture : {Moisture}%"; 
        }
    }
}
