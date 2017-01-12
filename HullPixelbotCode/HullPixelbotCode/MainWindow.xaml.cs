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

namespace HullPixelbotCode
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

        }

        void populatePorts()
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
        }

        SerialPort outputPort = null;

        void connectSerial()
        {
            string[] portNames = SerialPort.GetPortNames();

            if (portNames.Length == 0)
            {
                MessageBox.Show("No serial ports available", "Send program file");
                return;
            }

            // See if the selected port is still available

            string selectedPort = portsComboBox.SelectedItem as string;

            if (selectedPort == null)
            {
                MessageBox.Show("No serial port selected", "Send program file");
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
                MessageBox.Show("The selected port is no longer available", "Send program file");
                return;
            }

            addTextToMonitor("Connecting to " + selectedPort);

            addLineOfTextToMonitor("");

            if (outputPort != null)
            {
                addLineOfTextToMonitor("Closing existing port");
                outputPort.Close();
            }

            outputPort = new SerialPort(selectedPort, 9600, Parity.None);

            outputPort.DiscardNull = false;

            outputPort.DataReceived += OutputPort_DataReceived;

            outputPort.Open();

            return;
        }

        void disconnectSerial()
        {
            if (outputPort != null)
            {
                addLineOfTextToMonitor("Closing serial port");
                outputPort.Close();
                outputPort = null;
            }
            else
            {
                addLineOfTextToMonitor("No serial port to close");
            }
        }

        void loadFile()
        {
            OpenFileDialog loadDialog = new OpenFileDialog();

            loadDialog.CheckFileExists = true;

            if(loadDialog.ShowDialog()==true)
            {
                codeEditTextBox.Text = File.ReadAllText(loadDialog.FileName);
            }
        }

        void saveFile()
        {
            SaveFileDialog saveDialog = new SaveFileDialog();

            saveDialog.OverwritePrompt = true;

            if (saveDialog.ShowDialog()==true)
            {
                File.WriteAllText(saveDialog.FileName, codeEditTextBox.Text);
            }
        }

        void resetMonitor()
        {
            // Clear the monitor window
            serialDataTextBox.Text = "";
        }

        void addLineOfTextToMonitor(string message)
        {
            serialDataTextBox.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Normal,
                new Action(
                delegate ()
                {
                    serialDataTextBox.Text = serialDataTextBox.Text + System.Environment.NewLine + message;
                }
            ));
        }

        void addTextToMonitor(string message)
        {
            serialDataTextBox.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Normal,
                new Action(
                delegate ()
                {
                    serialDataTextBox.Text = serialDataTextBox.Text + message;
                }
            ));
        }

        const int TERMINATOR = 0x00;

        void sendFile()
        {
            // See of we have a port we can use

            if(outputPort == null)
            {
                MessageBox.Show("No serial port connected. Select a port and press the Connect button", "Program transfer");
                return;
            }

            // Got a working port - try to send the file

            outputPort.Write("\rRM\r");

            byte checksum = 0;

            char lastCh = ' ';

            foreach(char ch in codeEditTextBox.Text)
            {
                if (ch == '\n')
                {
                    // ignore linefeeds - only using CR
                    continue;
                }
                outputPort.Write(ch.ToString());

                checksum += (byte)ch;
                lastCh = ch;
            }

            byte[] oneByte = new byte[1];

            if (lastCh != '\r')
            {
                // Add a terminator
                oneByte[0] = (byte) '\r'; // terminator 
                outputPort.Write(oneByte, 0, 1);
                checksum += (byte)'\r';
            }

            // write the terminator

            oneByte[0] = TERMINATOR; // terminator 

            outputPort.Write( oneByte, 0, 1);

            oneByte[0] = checksum; // checksum

            outputPort.Write(oneByte, 0, 1);
        }

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
            addTextToMonitor(outputPort.ReadExisting());
        }

        private void loadButton_Click(object sender, RoutedEventArgs e)
        {
            loadFile();
        }

        private void saveButton_Click(object sender, RoutedEventArgs e)
        {
            saveFile();
        }

        private void sendButton_Click(object sender, RoutedEventArgs e)
        {
            sendFile();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            populatePorts();
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
            resetMonitor();
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
    }
}
