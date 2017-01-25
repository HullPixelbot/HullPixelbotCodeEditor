using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Common.Exceptions;
using Microsoft.ServiceBus.Messaging;
using System.Threading;
using Microsoft.Azure.Devices.Common;

namespace HullPixelbotCode
{

    public interface IDisplayMQTTMessage
    {
        void DisplayMessageLine(string message);
    }

    public class DeviceEntity : IComparable<DeviceEntity>
    {
        public string Id { get; set; }
        public string PrimaryKey { get; set; }
        public string SecondaryKey { get; set; }
        public string PrimaryThumbPrint { get; set; }
        public string SecondaryThumbPrint { get; set; }
        public string ConnectionString { get; set; }
        public string ConnectionState { get; set; }
        public DateTime LastActivityTime { get; set; }
        public DateTime LastConnectionStateUpdatedTime { get; set; }
        public DateTime LastStateUpdatedTime { get; set; }
        public int MessageCount { get; set; }
        public string State { get; set; }
        public string SuspensionReason { get; set; }

        public int CompareTo(DeviceEntity other)
        {
            return string.Compare(this.Id, other.Id, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString()
        {
            return Id;
//            return $"Device ID = {this.Id}, Primary Key = {this.PrimaryKey}, Secondary Key = {this.SecondaryKey}, Primary Thumbprint = {this.PrimaryThumbPrint}, Secondary Thumbprint = {this.SecondaryThumbPrint}, ConnectionString = {this.ConnectionString}, ConnState = {this.ConnectionState}, ActivityTime = {this.LastActivityTime}, LastConnState = {this.LastConnectionStateUpdatedTime}, LastStateUpdatedTime = {this.LastStateUpdatedTime}, MessageCount = {this.MessageCount}, State = {this.State}, SuspensionReason = {this.SuspensionReason}\r\n";
        }

        public string StatusString
        {
            get
            {
                return $"{this.Id} Status = {this.ConnectionState}";
            }
        }
    }

    class MQTT
    {
        private string iotHubConnectionString { get; set; }

        private string protocolGatewayHostName { get; set; }

        IDisplayMQTTMessage messageDisplay;

        public MQTT (string MQTTconnectionString, string protocolGatewayHost,IDisplayMQTTMessage messageDisplay)
        {
            this.registryManager = RegistryManager.CreateFromConnectionString(MQTTconnectionString);
            this.iotHubConnectionString = MQTTconnectionString;
            this.protocolGatewayHostName = protocolGatewayHost;
            this.messageDisplay = messageDisplay;
        }

        private String CreateDeviceConnectionString(Device device)
        {
            StringBuilder deviceConnectionString = new StringBuilder();

            var hostName = String.Empty;
            var tokenArray = iotHubConnectionString.Split(';');
            for (int i = 0; i < tokenArray.Length; i++)
            {
                var keyValueArray = tokenArray[i].Split('=');
                if (keyValueArray[0] == "HostName")
                {
                    hostName = tokenArray[i] + ';';
                    break;
                }
            }

            if (!String.IsNullOrWhiteSpace(hostName))
            {
                deviceConnectionString.Append(hostName);
                deviceConnectionString.AppendFormat("DeviceId={0}", device.Id);

                if (device.Authentication != null)
                {
                    if ((device.Authentication.SymmetricKey != null) && (device.Authentication.SymmetricKey.PrimaryKey != null))
                    {
                        deviceConnectionString.AppendFormat(";SharedAccessKey={0}", device.Authentication.SymmetricKey.PrimaryKey);
                    }
                    else
                    {
                        deviceConnectionString.AppendFormat(";x509=true");
                    }
                }

                if (this.protocolGatewayHostName.Length > 0)
                {
                    deviceConnectionString.AppendFormat(";GatewayHostName=ssl://{0}:8883", this.protocolGatewayHostName);
                }
            }

            return deviceConnectionString.ToString();
        }

        int maxCountOfDevices = 1000;

        private RegistryManager registryManager;

        public async Task<List<DeviceEntity>> GetDevices()
        {
            List<DeviceEntity> listOfDevices = new List<DeviceEntity>();

            try
            {
                DeviceEntity deviceEntity;
                var devices = await registryManager.GetDevicesAsync(maxCountOfDevices);

                if (devices != null)
                {
                    foreach (var device in devices)
                    {
                        deviceEntity = new DeviceEntity()
                        {
                            Id = device.Id,
                            ConnectionState = device.ConnectionState.ToString(),
                            ConnectionString = CreateDeviceConnectionString(device),
                            LastActivityTime = device.LastActivityTime,
                            LastConnectionStateUpdatedTime = device.ConnectionStateUpdatedTime,
                            LastStateUpdatedTime = device.StatusUpdatedTime,
                            MessageCount = device.CloudToDeviceMessageCount,
                            State = device.Status.ToString(),
                            SuspensionReason = device.StatusReason
                        };

                        if (device.Authentication != null)
                        {

                            deviceEntity.PrimaryKey = device.Authentication.SymmetricKey?.PrimaryKey;
                            deviceEntity.SecondaryKey = device.Authentication.SymmetricKey?.SecondaryKey;
                            deviceEntity.PrimaryThumbPrint = device.Authentication.X509Thumbprint?.PrimaryThumbprint;
                            deviceEntity.SecondaryThumbPrint = device.Authentication.X509Thumbprint?.SecondaryThumbprint;
                        }

                        listOfDevices.Add(deviceEntity);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return listOfDevices;
        }


        #region Robot message sending


        public async Task SyncSendToRobot(string MQTTName, byte [] message)
        {
            ServiceClient serviceClient = ServiceClient.CreateFromConnectionString(iotHubConnectionString);

            var serviceMessage = new Microsoft.Azure.Devices.Message(message);
            serviceMessage.Ack = DeliveryAcknowledgement.Full;
            serviceMessage.MessageId = Guid.NewGuid().ToString();

            await serviceClient.SendAsync(MQTTName, serviceMessage);

            await serviceClient.CloseAsync();
        }

        public void TaskSendToRobot(string MQTTName, byte [] message)
        {
            Task programTask;

            programTask = Task.Factory.StartNew(
                async () =>
                {
                    await this.SyncSendToRobot(MQTTName, message);
                }
                );
        }

        #endregion

        #region Robot Message receiving

        public async Task<string> MonitorRobot(string selectedDevice, CancellationToken ct)
        {
            string result = "";

            EventHubClient eventHubClient = null;
            EventHubReceiver eventHubReceiver = null;

            try
            {
                eventHubClient = EventHubClient.CreateFromConnectionString(iotHubConnectionString, "messages/events");
                int eventHubPartitionsCount = eventHubClient.GetRuntimeInformation().PartitionCount;
                string partition = EventHubPartitionKeyResolver.ResolveToPartition(selectedDevice, eventHubPartitionsCount);
                string consumerGroupName = "$Default";
                eventHubReceiver = eventHubClient.GetConsumerGroup(consumerGroupName).CreateReceiver(partition);
                var events = await eventHubReceiver.ReceiveAsync(int.MaxValue, TimeSpan.FromSeconds(20));
                string message = "";

                foreach (var eventData in events)
                {
                    var data = Encoding.UTF8.GetString(eventData.GetBytes());
                    var enqueuedTime = eventData.EnqueuedTimeUtc.ToLocalTime();
                    var connectionDeviceId = eventData.SystemProperties["iothub-connection-device-id"].ToString();

                    if (string.CompareOrdinal(selectedDevice.ToUpper(), connectionDeviceId.ToUpper()) == 0)
                    {
                        message = $"{enqueuedTime}> Device: [{connectionDeviceId}], Data:[{data}]";

                        if (eventData.Properties.Count > 0)
                        {
                            message += "Properties:\r\n";
                            foreach (var property in eventData.Properties)
                            {
                                message += $"'{property.Key}': '{property.Value}'\r\n";
                            }
                        }
                        message += "\r\n";
                    }
                }

                if(message.Length > 0)  
                    messageDisplay.DisplayMessageLine(message);

                while (true)
                {
                    message = "";

                    ct.ThrowIfCancellationRequested();

                    var eventData = await eventHubReceiver.ReceiveAsync(TimeSpan.FromSeconds(1));

                    if (eventData != null)
                    {
                        var data = Encoding.UTF8.GetString(eventData.GetBytes());
                        var enqueuedTime = eventData.EnqueuedTimeUtc.ToLocalTime();

                        // Display only data from the selected device; otherwise, skip.
                        var connectionDeviceId = eventData.SystemProperties["iothub-connection-device-id"].ToString();

                        if (string.CompareOrdinal(selectedDevice, connectionDeviceId) == 0)
                        {
                            message += $"{enqueuedTime}> Device: [{connectionDeviceId}], Data:[{data}]";

                            if (eventData.Properties.Count > 0)
                            {
                                message += "Properties:\r\n";
                                foreach (var property in eventData.Properties)
                                {
                                    message += $"'{property.Key}': '{property.Value}'\r\n";
                                }
                            }
                            message += "\r\n";
                        }
                        if (message.Length > 0)
                            messageDisplay.DisplayMessageLine(message);
                    }
                }
            }
            catch (Exception e)
            {

            }

            return result;
        }

        //private async void MonitorEventHubAsync(DateTime startTime, CancellationToken ct, string consumerGroupName)
        //{
        //    EventHubClient eventHubClient = null;
        //    EventHubReceiver eventHubReceiver = null;

        //    try
        //    {
        //        string selectedDevice = deviceIDsComboBoxForEvent.SelectedItem.ToString();
        //        eventHubClient = EventHubClient.CreateFromConnectionString(activeIoTHubConnectionString, "messages/events");
        //        eventHubTextBox.Text = "Receiving events...\r\n";
        //        eventHubPartitionsCount = eventHubClient.GetRuntimeInformation().PartitionCount;
        //        string partition = EventHubPartitionKeyResolver.ResolveToPartition(selectedDevice, eventHubPartitionsCount);
        //        eventHubReceiver = eventHubClient.GetConsumerGroup(consumerGroupName).CreateReceiver(partition, startTime);

        //        //receive the events from startTime until current time in a single call and process them
        //        var events = await eventHubReceiver.ReceiveAsync(int.MaxValue, TimeSpan.FromSeconds(20));

        //        foreach (var eventData in events)
        //        {
        //            var data = Encoding.UTF8.GetString(eventData.GetBytes());
        //            var enqueuedTime = eventData.EnqueuedTimeUtc.ToLocalTime();
        //            var connectionDeviceId = eventData.SystemProperties["iothub-connection-device-id"].ToString();

        //            if (string.CompareOrdinal(selectedDevice.ToUpper(), connectionDeviceId.ToUpper()) == 0)
        //            {
        //                eventHubTextBox.Text += $"{enqueuedTime}> Device: [{connectionDeviceId}], Data:[{data}]";

        //                if (eventData.Properties.Count > 0)
        //                {
        //                    eventHubTextBox.Text += "Properties:\r\n";
        //                    foreach (var property in eventData.Properties)
        //                    {
        //                        eventHubTextBox.Text += $"'{property.Key}': '{property.Value}'\r\n";
        //                    }
        //                }
        //                eventHubTextBox.Text += "\r\n";

        //                // scroll text box to last line by moving caret to the end of the text
        //                eventHubTextBox.SelectionStart = eventHubTextBox.Text.Length - 1;
        //                eventHubTextBox.SelectionLength = 0;
        //                eventHubTextBox.ScrollToCaret();
        //            }
        //        }

        //        //having already received past events, monitor current events in a loop
        //        while (true)
        //        {
        //            ct.ThrowIfCancellationRequested();

        //            var eventData = await eventHubReceiver.ReceiveAsync(TimeSpan.FromSeconds(1));

        //            if (eventData != null)
        //            {
        //                var data = Encoding.UTF8.GetString(eventData.GetBytes());
        //                var enqueuedTime = eventData.EnqueuedTimeUtc.ToLocalTime();

        //                // Display only data from the selected device; otherwise, skip.
        //                var connectionDeviceId = eventData.SystemProperties["iothub-connection-device-id"].ToString();

        //                if (string.CompareOrdinal(selectedDevice, connectionDeviceId) == 0)
        //                {
        //                    eventHubTextBox.Text += $"{enqueuedTime}> Device: [{connectionDeviceId}], Data:[{data}]";

        //                    if (eventData.Properties.Count > 0)
        //                    {
        //                        eventHubTextBox.Text += "Properties:\r\n";
        //                        foreach (var property in eventData.Properties)
        //                        {
        //                            eventHubTextBox.Text += $"'{property.Key}': '{property.Value}'\r\n";
        //                        }
        //                    }
        //                    eventHubTextBox.Text += "\r\n";
        //                }

        //                // scroll text box to last line by moving caret to the end of the text
        //                eventHubTextBox.SelectionStart = eventHubTextBox.Text.Length - 1;
        //                eventHubTextBox.SelectionLength = 0;
        //                eventHubTextBox.ScrollToCaret();
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        if (ct.IsCancellationRequested)
        //        {
        //            eventHubTextBox.Text += $"Stopped Monitoring events. {ex.Message}\r\n";
        //        }
        //        else
        //        {
        //            using (new CenterDialog(this))
        //            {
        //                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        //            }
        //            eventHubTextBox.Text += $"Stopped Monitoring events. {ex.Message}\r\n";
        //        }
        //        if (eventHubReceiver != null)
        //        {
        //            eventHubReceiver.Close();
        //        }
        //        if (eventHubClient != null)
        //        {
        //            eventHubClient.Close();
        //        }
        //        dataMonitorButton.Enabled = true;
        //        deviceIDsComboBoxForEvent.Enabled = true;
        //        cancelMonitoringButton.Enabled = false;
        //    }
        //}



        #endregion

    }
}
