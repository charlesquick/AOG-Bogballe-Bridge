using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AOGBogballeBridge
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<string> AvailablePorts { get; }
        public ObservableCollection<Brush> Sections { get; }

        private string? _selectedPort;
        private double _speed;
        private double _rate;
        private double _width;
        private bool _udpConnected;
        private bool _serialConnected;
        private DateTime _lastUdp;
        private byte _currentSectionMask;
        private string _lastPgn = "";
        private string _configText = "";

        // Machine configuration received from AgOpenGPS (PGN 0xEB)
        private int _secNum;
        private double _secWidth; // cm
        private bool _validConfig;
        private int _activeSectionsLast;
        private int _aogVersion;
        private bool _versionWarned;

        // Work switch: spreader Start/Stop state ({R:SprSta:C}) forwarded to the
        // AgOpenGPS master section button as a remote switch (PGN 234)
        private bool _workSwitchEnabled;
        private bool? _spreaderActive;          // null = unknown / no response
        private DateTime _lastSprSta;
        private bool _commandedSpreading;       // last SOrlSE command sent by the Bridge
        private bool _workSwitchReleasePending;
        private string _spreaderStatusText = "";
        private IPEndPoint? _agioEndpoint;
        private readonly List<byte> _serialRx = new List<byte>();

        private UdpClient? _udp;
        private SerialPort? _serial;
        private readonly DispatcherTimer _statusTimer;
        private readonly Dispatcher _dispatcher;

        public ICommand RefreshPortsCommand { get; }

        public string? SelectedPort
        {
            get => _selectedPort;
            set
            {
                _selectedPort = value;
                Properties.Settings.Default.ComPort = value;
                Properties.Settings.Default.Save();
                ConnectSerial();
                OnPropertyChanged(nameof(SelectedPort));
            }
        }

        public double Speed
        {
            get => _speed;
            set
            {
                _speed = value;
                OnPropertyChanged(nameof(Speed));
            }
        }

        public double Rate
        {
            get => _rate;
            set
            {
                _rate = value;
                OnPropertyChanged(nameof(Rate));
            }
        }

        public double Width
        {
            get => _width;
            set
            {
                _width = value;
                OnPropertyChanged(nameof(Width));
            }
        }

        public bool UdpConnected
        {
            get => _udpConnected;
            set
            {
                _udpConnected = value;
                OnPropertyChanged(nameof(UdpConnected));
                OnPropertyChanged(nameof(UdpBrush));
            }
        }

        public bool SerialConnected
        {
            get => _serialConnected;
            set
            {
                _serialConnected = value;
                OnPropertyChanged(nameof(SerialConnected));
                OnPropertyChanged(nameof(SerialBrush));
            }
        }

        public string LastPgn
        {
            get => _lastPgn;
            set
            {
                _lastPgn = value;
                OnPropertyChanged(nameof(LastPgn));
            }
        }

        public string ConfigText
        {
            get => _configText;
            set
            {
                _configText = value;
                OnPropertyChanged(nameof(ConfigText));
            }
        }

        public bool WorkSwitchEnabled
        {
            get => _workSwitchEnabled;
            set
            {
                _workSwitchEnabled = value;
                Properties.Settings.Default.WorkSwitchEnabled = value;
                Properties.Settings.Default.Save();

                _spreaderActive = null;
                _workSwitchReleasePending = false;
                SpreaderStatusText = value ? "Waiting for spreader..." : "Disabled";
                OnPropertyChanged(nameof(WorkSwitchEnabled));
            }
        }

        public string SpreaderStatusText
        {
            get => _spreaderStatusText;
            set
            {
                _spreaderStatusText = value;
                OnPropertyChanged(nameof(SpreaderStatusText));
            }
        }

        public Brush UdpBrush => UdpConnected ? Brushes.LimeGreen : Brushes.Red;
        public Brush SerialBrush => SerialConnected ? Brushes.LimeGreen : Brushes.Red;

        public MainViewModel()
        {
            _dispatcher = Application.Current.Dispatcher;

            AvailablePorts = new ObservableCollection<string>(SerialPort.GetPortNames());
            Sections = new ObservableCollection<Brush>();

            for (int i = 0; i < 8; i++)
                Sections.Add(Brushes.Gray);

            // Initialize display values
            Speed = 0;
            Rate = 0;
            Width = 0;
            _currentSectionMask = 0;
            _activeSectionsLast = 0;

            // Load persisted machine configuration (updated whenever AgOpenGPS
            // broadcasts the section dimensions PGN 0xEB)
            _secNum = Properties.Settings.Default.SecNum;
            _secWidth = Properties.Settings.Default.SecWidth;
            ApplyConfig(showWarning: false);

            WorkSwitchEnabled = Properties.Settings.Default.WorkSwitchEnabled;

            // Load persisted COM port
            SelectedPort = Properties.Settings.Default.ComPort;

            // Setup refresh command
            RefreshPortsCommand = new RelayCommand(RefreshPorts);

            StartUdp();

            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            _statusTimer.Tick += StatusTimer_Tick;
            _statusTimer.Start();
        }

        private void RefreshPorts()
        {
            string? currentPort = SelectedPort;

            AvailablePorts.Clear();

            foreach (string port in SerialPort.GetPortNames())
            {
                AvailablePorts.Add(port);
            }

            // Try to restore previous selection if it still exists
            if (currentPort != null && AvailablePorts.Contains(currentPort))
            {
                SelectedPort = currentPort;
            }
            else
            {
                SelectedPort = null;
            }
        }

        private void StatusTimer_Tick(object? sender, EventArgs e)
        {
            bool connected = (DateTime.Now - _lastUdp).TotalSeconds < 1.5;

            // Comms lost: stop spreading rather than holding the last command
            // (matches the Python default CommsLostBehaviour = 0)
            if (UdpConnected && !connected)
            {
                Speed = 0;
                _currentSectionMask = 0;
                UpdateSections(0);
                SendSpeed();
                SendEnable();
                SendSections();
            }

            UdpConnected = connected;

            PollSpreaderStatus();
        }

        private void StartUdp()
        {
            try
            {
                _udp = new UdpClient();

                _udp.Client.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress,
                    true);

                _udp.Client.Bind(new IPEndPoint(IPAddress.Any, 8888));

                BeginReceive();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR starting UDP: {ex.Message}");
            }
        }

        private void BeginReceive()
        {
            try
            {
                _udp?.BeginReceive(UdpCallback, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in BeginReceive: {ex.Message}");
            }
        }

        private void UdpCallback(IAsyncResult ar)
        {
            IPEndPoint? ep = new IPEndPoint(IPAddress.Any, 0);

            try
            {
                byte[] data = _udp!.EndReceive(ar, ref ep);
                _lastUdp = DateTime.Now;

                // Remember where AgIO sends from (source 0x7F, port 9999) so
                // work switch packets can be returned to it like a module would
                if (data.Length > 2 && data[0] == 0x80 && data[1] == 0x81 && data[2] == 0x7F)
                    _agioEndpoint = ep;

                // Parse on UI thread to ensure property updates work
                _dispatcher.BeginInvoke(new Action(() => ParseAogPacket(data)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in UdpCallback: {ex.Message}");
            }
            finally
            {
                BeginReceive();
            }
        }

        private void ParseAogPacket(byte[] data)
        {
            if (data == null || data.Length < 6)
                return;

            byte pgn = data[3];
            LastPgn = $"0x{pgn:X2}";

            // Speed PGN (Steer data - same as Python)
            if (pgn == 0xFE)
            {
                if (data.Length < 7)
                    return;

                ushort spd = BitConverter.ToUInt16(data, 5);
                Speed = Math.Round(spd * 0.1, 1);
                SendSpeed();
                SendEnable();
                SendSections(); // Always send sections with speed updates
            }
            // Machine Data PGN (has section data in byte 11)
            else if (pgn == 0xEF)
            {
                if (data.Length >= 12)
                {
                    byte sectBits = data[11]; // Byte 11 contains sections 1-8
                    _currentSectionMask = sectBits;
                    UpdateSections(sectBits);
                    SendEnable();
                    SendSections(); // Always send sections when they update
                }
            }
            // Section control
            else if (pgn == 0xEC)
            {
                byte mask = data[5];
                _currentSectionMask = mask;
                UpdateSections(mask);
                SendEnable();
                SendSections(); // Always send sections when they update
            }
            // Section dimensions PGN: machine configuration from AgOpenGPS
            else if (pgn == 0xEB)
            {
                if (data.Length < 38)
                    return;

                double secWidth = BitConverter.ToUInt16(data, 5); // section width in cm
                int secNum = data[37];                            // number of sections

                // Only persist and warn when the configuration actually changes
                if (secNum == _secNum && secWidth == _secWidth)
                    return;

                _secWidth = secWidth;
                _secNum = secNum;

                Properties.Settings.Default.SecNum = _secNum;
                Properties.Settings.Default.SecWidth = _secWidth;
                Properties.Settings.Default.Save();

                Debug.WriteLine($"Section configuration received: {_secNum} sections, each {_secWidth} cm");

                ApplyConfig(showWarning: true);
                SendTotalWidth();
            }
            // AgIO Hello PGN
            else if (pgn == 0xC8)
            {
                _aogVersion = data[5];

                if (_aogVersion < 56 && !_versionWarned)
                {
                    _versionWarned = true;
                    MessageBox.Show(
                        "This version of AgOpenGPS is not supported! Some features may not work as intended.\n" +
                        "Consider upgrading to version 5.7 or newer.\n\n" +
                        $"Detected version: {_aogVersion / 10.0:F1}",
                        "AOG-Bogballe Bridge",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            // Rate + width
            else if (pgn == 0xE7)
            {
                if (data.Length < 9)
                    return;

                ushort rateRaw = BitConverter.ToUInt16(data, 5);
                Rate = Math.Round(rateRaw * 0.01, 0);

                ushort widthRaw = BitConverter.ToUInt16(data, 7);
                Width = Math.Round(widthRaw * 0.01, 1);
            }
        }

        private void ApplyConfig(bool showWarning)
        {
            // Bogballe control supports 2, 4 or 8 sections (expanded to 8 below)
            _validConfig = _secNum > 0 && _secNum <= 8 && _secNum % 2 == 0;
            UpdateSections(_currentSectionMask);

            if (_validConfig)
            {
                Width = Math.Round(_secWidth / 100.0 * _secNum, 1);
                ConfigText = $"{_secNum} sections, each {_secWidth:F0} cm (total {Width:F1} m)";
            }
            else if (_secNum == 0)
            {
                ConfigText = "Waiting for machine configuration from AgOpenGPS...";
            }
            else
            {
                ConfigText = $"Unsupported: {_secNum} sections, each {_secWidth:F0} cm";

                if (showWarning)
                {
                    MessageBox.Show(
                        "Machine configuration not supported.\nPlease use either 2, 4 or 8 sections.\n\n" +
                        $"Current setup: {_secNum} sections, each {_secWidth:F0} cm wide",
                        "AOG-Bogballe Bridge",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        private void UpdateSections(byte mask)
        {
            int shown = _validConfig ? _secNum : 8;
            for (int i = 0; i < 8; i++)
            {
                if (i >= shown)
                {
                    Sections[i] = Brushes.Gray; // not part of the configured machine
                    continue;
                }

                bool active = (mask & (1 << i)) != 0;
                Sections[i] = active ? Brushes.LimeGreen : Brushes.DarkGray;
            }
        }

        private int CountActiveSections()
        {
            int count = 0;
            for (int i = 0; i < _secNum; i++)
            {
                if ((_currentSectionMask & (1 << i)) != 0)
                    count++;
            }
            return count;
        }

        private void ConnectSerial()
        {
            try
            {
                _serial?.Close();

                if (string.IsNullOrWhiteSpace(SelectedPort))
                {
                    SerialConnected = false;
                    return;
                }

                // Write timeout stops a stalled adapter from freezing the UI thread
                _serial = new SerialPort(SelectedPort, 9600) { WriteTimeout = 500 };
                _serial.DataReceived += Serial_DataReceived;
                _serial.Open();
                _serialRx.Clear();
                SerialConnected = true;

                // Force enable/width to be resent to the (re)connected spreader
                _activeSectionsLast = -1;
            }
            catch
            {
                SerialConnected = false;
            }
        }

        private void SendToSpreader(string message)
        {
            if (!SerialConnected || _serial == null)
                return;

            try
            {
                // Frame: {message checksum}
                string checksum = CalculateAsciiChecksum(message);
                string fullMessage = $"{{{message}{checksum}}}";

                byte[] msg = Encoding.ASCII.GetBytes(fullMessage);
                _serial.Write(msg, 0, msg.Length);

                Debug.WriteLine($"Sent: {fullMessage}");
            }
            catch (Exception ex)
            {
                SerialConnected = false;
                Debug.WriteLine($"ERROR sending '{message}': {ex.Message}");
            }
        }

        private void SendSpeed()
        {
            // Format: {S:SpdKmh:0.0:checksum}
            SendToSpreader($"S:SpdKmh:{Speed.ToString("F1", CultureInfo.InvariantCulture)}:");
        }

        private void SendSections()
        {
            if (!_validConfig)
                return;

            // Bogballe always expects 8 section states, so 2- and 4-section
            // configurations are expanded (each section repeated 8/secNum times).
            // Format: {S:SOrlBs:0:0:0:0:0:0:0:0:checksum}
            StringBuilder message = new StringBuilder("S:SOrlBs:");
            int repeats = 8 / _secNum;
            for (int i = 0; i < _secNum; i++)
            {
                char state = (_currentSectionMask & (1 << i)) != 0 ? '1' : '0';
                for (int r = 0; r < repeats; r++)
                {
                    message.Append(state);
                    message.Append(':');
                }
            }

            SendToSpreader(message.ToString());
        }

        private void SendEnable()
        {
            if (!_validConfig)
                return;

            // Only send the enable command when the active section count changes
            int activeSections = CountActiveSections();
            if (activeSections == _activeSectionsLast)
                return;

            _activeSectionsLast = activeSections;
            _commandedSpreading = activeSections > 0;

            // Format: {S:SOrlSE:1:checksum} (1 = spreading enabled, 0 = disabled)
            SendToSpreader(activeSections > 0 ? "S:SOrlSE:1:" : "S:SOrlSE:0:");
            SendActiveWidth();
        }

        private void SendActiveWidth()
        {
            // Format: {S:SOrlWt:1:width_m:checksum}
            double activeWidth = Math.Round(_activeSectionsLast * _secWidth / 100.0, 1);
            SendToSpreader($"S:SOrlWt:1:{activeWidth.ToString("F1", CultureInfo.InvariantCulture)}:");
        }

        private void SendTotalWidth()
        {
            // Format: {S:SprdWt:width_m checksum}
            // Built for protocol completeness but not transmitted, matching the
            // original Python implementation where this write was disabled.
            double totalWidth = Math.Round(_secWidth * _secNum / 100.0, 1);
            Debug.WriteLine($"Total spread width: {{S:SprdWt:{totalWidth.ToString("F1", CultureInfo.InvariantCulture)}}} (not transmitted)");
        }

        private void PollSpreaderStatus()
        {
            if (!_workSwitchEnabled)
                return;

            // Release the remote switch "button" pressed on the previous tick
            if (_workSwitchReleasePending)
            {
                _workSwitchReleasePending = false;
                SendRemoteSwitch(0x00);
            }

            if (_spreaderActive != null && (DateTime.Now - _lastSprSta).TotalSeconds > 2)
            {
                _spreaderActive = null;
                SpreaderStatusText = "No response from spreader";
            }

            // Format: {R:SprSta:checksum}, answered with {W:SprSta:<active>:<trend>:checksum}
            SendToSpreader("R:SprSta:");
        }

        private void Serial_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            var port = (SerialPort)sender;

            try
            {
                byte[] buffer = new byte[port.BytesToRead];
                int count = port.Read(buffer, 0, buffer.Length);

                // Parse on UI thread, alongside the UDP handling
                _dispatcher.BeginInvoke(new Action(() => ProcessSerialBytes(port, buffer, count)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR reading serial: {ex.Message}");
            }
        }

        private void ProcessSerialBytes(SerialPort port, byte[] buffer, int count)
        {
            // Ignore data from a port that has since been replaced
            if (!ReferenceEquals(port, _serial))
                return;

            for (int i = 0; i < count; i++)
                _serialRx.Add(buffer[i]);

            // Extract complete telegrams: {payload checksum}
            while (true)
            {
                int start = _serialRx.IndexOf((byte)'{');
                if (start < 0)
                {
                    _serialRx.Clear();
                    return;
                }

                int end = _serialRx.IndexOf((byte)'}', start + 1);
                if (end < 0)
                {
                    _serialRx.RemoveRange(0, start);
                    if (_serialRx.Count > 256)
                        _serialRx.Clear(); // runaway garbage, resync
                    return;
                }

                // A '{' inside the frame means the earlier telegram was cut short
                int restart = _serialRx.IndexOf((byte)'{', start + 1, end - start - 1);
                if (restart >= 0)
                {
                    _serialRx.RemoveRange(0, restart);
                    continue;
                }

                if (end - start >= 2)
                {
                    string payload = Encoding.ASCII.GetString(_serialRx.GetRange(start + 1, end - start - 2).ToArray());
                    char checksum = (char)_serialRx[end - 1];

                    if (CalculateAsciiChecksum(payload)[0] == checksum)
                        HandleSpreaderTelegram(payload);
                    else
                        Debug.WriteLine($"Bad checksum from spreader: {payload}");
                }

                _serialRx.RemoveRange(0, end + 1);
            }
        }

        private void HandleSpreaderTelegram(string payload)
        {
            // {W:SprSta:<SpreaderActive>:<TrendPosition>:C}
            // SpreaderActive: 0 = not spreading, 1 = spreading
            if (!payload.StartsWith("W:SprSta:", StringComparison.Ordinal))
                return;

            string[] fields = payload.Split(':');
            if (fields.Length < 3 || (fields[2] != "0" && fields[2] != "1"))
                return;

            UpdateSpreaderActive(fields[2] == "1");
        }

        private void UpdateSpreaderActive(bool active)
        {
            _lastSprSta = DateTime.Now;
            bool? previous = _spreaderActive;
            _spreaderActive = active;
            SpreaderStatusText = active ? "Spreading" : "Stopped";

            if (!_workSwitchEnabled || previous == null || previous == active || !UdpConnected)
                return;

            // The Bridge starts/stops the spreader itself (SOrlSE) as AgOpenGPS
            // switches sections, e.g. at headlands. Only forward changes that
            // disagree with that command - those came from the operator pressing
            // Start/Stop on the control box. Forwarding our own stops would
            // switch AgOpenGPS off at every headland.
            if (active == _commandedSpreading)
                return;

            Debug.WriteLine($"Spreader {(active ? "started" : "stopped")} by operator - sending work switch {(active ? "ON" : "OFF")}");
            SendRemoteSwitch(active ? (byte)0x01 : (byte)0x02);
            _workSwitchReleasePending = true;
        }

        private void SendRemoteSwitch(byte main)
        {
            // PGN 234 (0xEA) remote section switches, as sent by a switch box module.
            // AgIO forwards it to AgOpenGPS. Main byte, button mode (bit 2 clear):
            // bit 0 = master Auto ON, bit 1 = master OFF; AgOpenGPS acts when the
            // byte changes, so each press is followed by a release (0x00).
            // All section bytes are 0 so individual sections are left alone.
            IPEndPoint? agio = _agioEndpoint;
            if (_udp == null || agio == null)
                return;

            byte[] pgn = { 0x80, 0x81, 0x7B, 0xEA, 8, main, 0, 0, 0, 0, 0, 0, 0, 0 };

            byte crc = 0;
            for (int i = 2; i < pgn.Length - 1; i++)
                crc += pgn[i];
            pgn[pgn.Length - 1] = crc;

            try
            {
                _udp.Send(pgn, pgn.Length, agio);
                Debug.WriteLine($"Sent PGN 234 main=0x{main:X2} to {agio}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR sending PGN 234: {ex.Message}");
            }
        }

        private string CalculateAsciiChecksum(string data)
        {
            byte cs = 0;
            foreach (char c in data)
            {
                cs ^= (byte)c;
            }

            // Avoid special characters (same as Python logic)
            if (cs == 0x00 || cs == 0x7B || cs == 0x7D)
                cs = 0x55;

            return ((char)cs).ToString();
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    // Simple RelayCommand implementation
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter)
        {
            return _canExecute == null || _canExecute();
        }

        public void Execute(object? parameter)
        {
            _execute();
        }
    }
}