using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Common.Exceptions;
using System.Threading;

namespace HullPixelbotCode
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDisplayMQTTMessage
    {
        public MainWindow()
        {
            InitializeComponent();

        }


        #region Serial Monitor display
        void resetSerialMonitor()
        {
            // Clear the monitor window
            serialDataTextBox.Text = "";
        }

        void addLineOfTextToSerialMonitor(string message)
        {
            serialDataTextBox.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Normal,
                new Action(
                delegate ()
                {
                    serialDataTextBox.Text = serialDataTextBox.Text + System.Environment.NewLine + message;
                    serialDataTextBox.ScrollToEnd();
                }
            ));
        }

        void addTextToSerialMonitor(string message)
        {
            serialDataTextBox.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Normal,
                new Action(
                delegate ()
                {
                    serialDataTextBox.Text = serialDataTextBox.Text + message;
                    serialDataTextBox.ScrollToEnd();
                }
            ));
        }

        #endregion


        #region MQTT Monitor display
        void resetMQTTMonitor()
        {
            // Clear the monitor window
            MQTTDataTextBox.Text = "";
        }

        void addLineOfTextToMQTTMonitor(string message)
        {
            MQTTDataTextBox.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Normal,
                new Action(
                delegate ()
                {
                    MQTTDataTextBox.Text = MQTTDataTextBox.Text + System.Environment.NewLine + message;
                    // scroll text box to last line by moving caret to the end of the text
                    MQTTDataTextBox.ScrollToEnd();
                }
            ));
        }

        void addTextToMQTTMonitor(string message)
        {
            MQTTDataTextBox.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Normal,
                new Action(
                delegate ()
                {
                    serialDataTextBox.Text = MQTTDataTextBox.Text + message;
                }
            ));
        }

        public void DisplayMessageLine(string message)
        {
            addLineOfTextToMQTTMonitor(message);
        }


        #endregion


        #region Code Assembly 

        void addStringToByteList(string input, List<byte> output)
        {
            foreach (char ch in input)
                output.Add((byte)ch);
        }

        /// <summary>
        /// Takes the program source and converts it into a packet for delivery to the robot.
        /// Adds the download command at the start of the packet
        /// Makes sure that each statement is separated by a return character, calculates and adds the checksum and adds the terminator
        /// </summary>
        /// <param name="code">lines of program source</param>
        /// <returns>string to be sent to the robot</returns>
        byte [] assembleProgram(string code)
        {
            List<byte> output = new List<byte>();

            addStringToByteList("\rRM\r", output);

            char lastCh = ' ';

            foreach (char ch in code)
            {
                if (ch == '\n')
                {
                    // ignore linefeeds - only using CR
                    continue;
                }

                output.Add((byte)ch);

                lastCh = ch;
            }

            if (lastCh != '\r')
            {
                // Add a terminator on the last line
                output.Add((byte)'\r');
            }

            addStringToByteList("RX\r", output);

            return output.ToArray();
        }

        #endregion

        #region MQTT methods

        MQTT mqttconnection;

        void connectMQTT()
        {
            mqttconnection = new MQTT(
                    MQTTconnectionString: "HostName=HullPixelbot.azure-devices.net;SharedAccessKeyName=iothubowner;SharedAccessKey=3l6MBea3c9YyIkWO9JRU6CKGi8DvVI9ILKo79EimgjM=",
                    protocolGatewayHost: "", messageDisplay: this);
        }

        async Task<string> populateRobots()
        {
            MQTTrobotNames.Items.Clear();

            var devices = await mqttconnection.GetDevices();

            foreach (DeviceEntity device in devices)
            {
                if(device.ConnectionState == "Connected")
                    MQTTrobotNames.Items.Add(device);
            }

            return "MQTT robot list complete";
        }

        async Task<string> getMQTTDeviceName()
        {
            string robotName = "";

            DeviceEntity selectedPort = MQTTrobotNames.SelectedItem as DeviceEntity;

            // first see if a robot has been selected

            if (selectedPort == null)
            {
                MessageBox.Show("No robot selected", "Send program file to MQTTT");
                return "";
            }

            // see if the selected robot is online

            var devices = await mqttconnection.GetDevices();

            foreach (DeviceEntity device in devices)
            {
                if (device.Id == selectedPort.Id)
                {
                    if (device.ConnectionState == "Connected")
                    {
                        robotName = device.Id;
                    }
                    break;
                }
            }

            if (robotName == null)
            {
                MessageBox.Show("Selected robot not connected", "Send program file to MQTTT");
                return "";
            }

            return robotName;
        }


        async Task<string> mqttSendProgram(string program)
        {
            string robotName = await getMQTTDeviceName();

            if (robotName == "")
                return "Robot not avaialable";

            // If we get here we have a connected destination and a program to send
            // Send it

            byte[] programBytes = assembleProgram(program);

            await mqttconnection.SyncSendToRobot(robotName, programBytes);

            return "Program sent to MQTT";
        }

        private static CancellationTokenSource ctsForDataMonitoring;

        async Task<string> mqttStartMonitor()
        {
            ctsForDataMonitoring = new CancellationTokenSource();

            string robotName = await getMQTTDeviceName();

            if (robotName == "")
                return "Robot not avaialable";

            string result = "";

            result = await mqttconnection.MonitorRobot(robotName, ctsForDataMonitoring.Token);

            return result;
        }

        #endregion

        #region Serial connection

        string populatePorts()
        {
            // Get the names of the currently active serial ports
            string[] portNames = SerialPort.GetPortNames();

            // Clear the combobox
            portsComboBox.Items.Clear();

            // Do we have any names?
            if(portNames.Length > 0)
            {
                // work through the names and add them to the combo box
                foreach(string port in portNames)
                {
                    portsComboBox.Items.Add(port);
                }

                // Select the first port to get us started
                portsComboBox.SelectedIndex = 0;
            }

            return "Ports loaded";
        }

        SerialPort outputPort = null;

        void connectSerial()
        {
            string[] portNames = SerialPort.GetPortNames();

            if (portNames.Length == 0)
            {
                MessageBox.Show("No serial ports available", "Serial port open");
                return;
            }

            // See if the selected port is still available

            string selectedPort = portsComboBox.SelectedItem as string;

            if (selectedPort == null)
            {
                MessageBox.Show("No serial port selected", "Serial port open");
                return;
            }

            bool foundPort = false;
            selectedPort = selectedPort.ToUpper();

            foreach (string port in portNames)
            {
                if (port.ToUpper() == selectedPort)
                {
                    foundPort = true;
                    break;
                }
            }

            if (!foundPort)
            {
                MessageBox.Show("The selected port is no longer available", "Serial port open");
                return;
            }

            addTextToSerialMonitor("Connecting to " + selectedPort);

            addLineOfTextToSerialMonitor("");

            if (outputPort != null)
            {
                addLineOfTextToSerialMonitor("Closing existing port");
                outputPort.Close();
            }

            outputPort = new SerialPort(selectedPort, 1200, Parity.None);

            outputPort.DiscardNull = false;

            outputPort.DataReceived += OutputPort_DataReceived;

            try
            {
                outputPort.Open();
            }
            catch
            {
                MessageBox.Show("The selected port could not be opened. It may be in use.", "Serial port open");
                outputPort = null;
                return;
            }

            return;
        }

        void disconnectSerial()
        {
            if (outputPort != null)
            {
                addLineOfTextToSerialMonitor("Closing serial port");
                outputPort.Close();
                outputPort = null;
            }
            else
            {
                addLineOfTextToSerialMonitor("No serial port to close");
            }
        }

        #endregion

        #region File input/output

        string loadFile()
        {
            string result = "";

            OpenFileDialog loadDialog = new OpenFileDialog();

            loadDialog.CheckFileExists = true;

            if(loadDialog.ShowDialog()==true)
            {
                result = File.ReadAllText(loadDialog.FileName);
            }
            return result;
        }

        void saveFile(string text)
        {
            SaveFileDialog saveDialog = new SaveFileDialog();

            saveDialog.OverwritePrompt = true;

            if (saveDialog.ShowDialog()==true)
            {
                File.WriteAllText(saveDialog.FileName, text);
            }
        }


        void sendFile(string text)
        {
            // See of we have a port we can use

            if(outputPort == null)
            {
                MessageBox.Show("No serial port connected. Select a port and press the Connect button", "Program transfer");
                return;
            }

            byte[] message = assembleProgram(text);

            // Got a working port - try to send the file

            outputPort.Write(message, 0, message.Length);

            return;
        }

        #endregion


        void sendMessage(string message)
        {
            if (outputPort == null)
            {
                MessageBox.Show("No serial port connected. Select a port and press the Connect button", "Program transfer");
                return;
            }

            outputPort.Write(message + "\r");
        }

        void stopProgram()
        {
            sendMessage("RH");
        }

        void startProgram()
        {
            sendMessage("RS");
        }

        void pauseProgram()
        {
            sendMessage("RP");
        }

        void resumeProgram()
        {
            sendMessage("RS");
        }
        private void OutputPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort outputPort = sender as SerialPort;
            addTextToSerialMonitor(outputPort.ReadExisting());
        }

        private void loadButton_Click(object sender, RoutedEventArgs e)
        {
            codeEditTextBox.Text = loadFile();
        }

        private void saveButton_Click(object sender, RoutedEventArgs e)
        {
            saveFile(codeEditTextBox.Text);
        }

        private void sendButton_Click(object sender, RoutedEventArgs e)
        {
            sendFile(codeEditTextBox.Text);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            connectMQTT();

            addLineOfTextToSerialMonitor(populatePorts());

            string populateMessage = await populateRobots();

            addLineOfTextToSerialMonitor(populateMessage);
        }

        private void refershPortsButton_Click(object sender, RoutedEventArgs e)
        {
            populatePorts();
        }

        private void connectButton_Click(object sender, RoutedEventArgs e)
        {
            connectSerial();
        }

        private void clearButton_Click(object sender, RoutedEventArgs e)
        {
            resetSerialMonitor();
        }

        private void disconnectButton_Click(object sender, RoutedEventArgs e)
        {
            disconnectSerial();
        }

        private void stopButtonClick(object sender, RoutedEventArgs e)
        {
            stopProgram();
        }

        private void startButtonClick(object sender, RoutedEventArgs e)
        {
            startProgram();
        }

        private void pauseButtonClick(object sender, RoutedEventArgs e)
        {
            pauseProgram();
        }

        private void resumeButtonClick(object sender, RoutedEventArgs e)
        {
            resumeProgram();
        }

        private async void mqttSendButton_Click(object sender, RoutedEventArgs e)
        {
            string result = await mqttSendProgram(codeEditTextBox.Text);
            addLineOfTextToSerialMonitor(result);
        }

        private async void MQTTMonitorButtonClick(object sender, RoutedEventArgs e)
        {
            string result = await mqttStartMonitor();
            addLineOfTextToMQTTMonitor(result);
        }

    }
}
