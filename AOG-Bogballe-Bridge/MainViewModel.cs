using System;
using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AOGBogballeBridge
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<string> AvailablePorts { get; }
        public ObservableCollection<Brush> Sections { get; }
        public ObservableCollection<string> ShapefileFields { get; }

        private IReadOnlyList<ShapefileFeature> _shapefileFeatures = Array.Empty<ShapefileFeature>();
        private string? _selectedShapefileField;
        private string _shapefileName = "No shapefile loaded";
        private string _shapefileStatus = "Load an ESRI .shp file to preview its features.";
        private bool _isShapefileGeographic;
        private bool _shapefileLoaded;
        private bool _isLoadingShapefile;
        private bool _gpsConnected;
        private bool _gpsPositionValid;
        private double _gpsLatitude;
        private double _gpsLongitude;
        private int _gpsFixQuality;
        private int _gpsSatellites;
        private string _gpsStatus = "Waiting for PANDA data.";
        private bool _rateZoneValid;
        private string? _currentApplicationRate;
        private string? _lastSentApplicationRate;
        private bool _spreaderEnabledLast = true;

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
        private DateTime _lastGpsPacket;

        private UdpClient? _udp;
        private SerialPort? _serial;
        private readonly DispatcherTimer _statusTimer;
        private readonly Dispatcher _dispatcher;

        public ICommand RefreshPortsCommand { get; }
        public ICommand LoadShapefileCommand { get; }

        public bool GpsConnected
        {
            get => _gpsConnected;
            private set
            {
                _gpsConnected = value;
                OnPropertyChanged(nameof(GpsConnected));
            }
        }

        public bool ShapefileLoaded
        {
            get => _shapefileLoaded;
            private set
            {
                _shapefileLoaded = value;
                OnPropertyChanged(nameof(ShapefileLoaded));
            }
        }

        public bool GpsPositionValid
        {
            get => _gpsPositionValid;
            private set
            {
                _gpsPositionValid = value;
                OnPropertyChanged(nameof(GpsPositionValid));
                OnPropertyChanged(nameof(GpsCoordinateText));
            }
        }

        public double GpsLatitude
        {
            get => _gpsLatitude;
            private set
            {
                _gpsLatitude = value;
                OnPropertyChanged(nameof(GpsLatitude));
                OnPropertyChanged(nameof(GpsCoordinateText));
            }
        }

        public double GpsLongitude
        {
            get => _gpsLongitude;
            private set
            {
                _gpsLongitude = value;
                OnPropertyChanged(nameof(GpsLongitude));
                OnPropertyChanged(nameof(GpsCoordinateText));
            }
        }

        public string GpsCoordinateText => GpsPositionValid
            ? $"Lat: {GpsLatitude:F6}   Lon: {GpsLongitude:F6}"
            : "Position unavailable";

        public int GpsFixQuality
        {
            get => _gpsFixQuality;
            private set
            {
                _gpsFixQuality = value;
                OnPropertyChanged(nameof(GpsFixQuality));
            }
        }

        public int GpsSatellites
        {
            get => _gpsSatellites;
            private set
            {
                _gpsSatellites = value;
                OnPropertyChanged(nameof(GpsSatellites));
            }
        }

        public string GpsStatus
        {
            get => _gpsStatus;
            private set
            {
                _gpsStatus = value;
                OnPropertyChanged(nameof(GpsStatus));
            }
        }

        public IReadOnlyList<ShapefileFeature> ShapefileFeatures
        {
            get => _shapefileFeatures;
            private set
            {
                _shapefileFeatures = value;
                OnPropertyChanged(nameof(ShapefileFeatures));
            }
        }

        public bool IsShapefileGeographic
        {
            get => _isShapefileGeographic;
            private set
            {
                _isShapefileGeographic = value;
                OnPropertyChanged(nameof(IsShapefileGeographic));
            }
        }

        public string? SelectedShapefileField
        {
            get => _selectedShapefileField;
            set
            {
                _selectedShapefileField = value;
                OnPropertyChanged(nameof(SelectedShapefileField));
                if (!_isLoadingShapefile)
                    UpdateApplicationRate();
            }
        }

        public string CurrentApplicationRate
        {
            get => !ShapefileLoaded
                ? "Shapefile rate control off; using normal spreader control."
                : _currentApplicationRate == null ? "No rate selected" : $"Application rate: {_currentApplicationRate}";
            private set
            {
                _currentApplicationRate = value;
                OnPropertyChanged(nameof(CurrentApplicationRate));
                OnPropertyChanged(nameof(TargetApplicationRateDisplay));
            }
        }

        public string TargetApplicationRateDisplay => !ShapefileLoaded
            ? "Off"
            : _currentApplicationRate ?? "—";

        public string ShapefileName
        {
            get => _shapefileName;
            private set
            {
                _shapefileName = value;
                OnPropertyChanged(nameof(ShapefileName));
            }
        }

        public string ShapefileStatus
        {
            get => _shapefileStatus;
            private set
            {
                _shapefileStatus = value;
                OnPropertyChanged(nameof(ShapefileStatus));
            }
        }

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

        public Brush UdpBrush => UdpConnected ? Brushes.LimeGreen : Brushes.Red;
        public Brush SerialBrush => SerialConnected ? Brushes.LimeGreen : Brushes.Red;

        public MainViewModel()
        {
            _dispatcher = Application.Current.Dispatcher;

            AvailablePorts = new ObservableCollection<string>(SerialPort.GetPortNames());
            Sections = new ObservableCollection<Brush>();
            ShapefileFields = new ObservableCollection<string>();

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

            // Load persisted COM port
            SelectedPort = Properties.Settings.Default.ComPort;

            // Setup refresh command
            RefreshPortsCommand = new RelayCommand(RefreshPorts);
            LoadShapefileCommand = new RelayCommand(LoadShapefile);

            StartUdp();

            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            _statusTimer.Tick += StatusTimer_Tick;
            _statusTimer.Start();
        }

        private void LoadShapefile()
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Open shapefile",
                Filter = "ESRI Shapefile (*.shp)|*.shp",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                ShapefileData data = ShapefileReader.Read(dialog.FileName);
                _isLoadingShapefile = true;
                ShapefileFeatures = data.Features;
                IsShapefileGeographic = data.IsGeographic;
                ShapefileFields.Clear();
                foreach (string field in data.Fields)
                    ShapefileFields.Add(field);

                SelectedShapefileField = data.Fields.FirstOrDefault(field =>
                    data.Features.Any(feature =>
                        feature.Attributes.TryGetValue(field, out string? value) &&
                        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)));
                ShapefileName = System.IO.Path.GetFileName(dialog.FileName);
                ShapefileLoaded = true;
                string fieldStatus = data.Fields.Count == 0
                    ? $"{data.Features.Count} features loaded. No matching .dbf file found."
                    : $"{data.Features.Count} features loaded; {data.Fields.Count} DBF fields available.";
                ShapefileStatus = $"{fieldStatus} {data.ProjectionSummary}";
                _isLoadingShapefile = false;
                UpdateApplicationRate();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _isLoadingShapefile = false;
                ShapefileStatus = $"Could not load shapefile: {ex.Message}";
                MessageBox.Show(
                    ShapefileStatus,
                    "AOG Bogballe Bridge",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
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
            bool gpsConnected = _lastGpsPacket != default && (DateTime.UtcNow - _lastGpsPacket).TotalSeconds < 3;
            if (GpsConnected && !gpsConnected)
            {
                GpsPositionValid = false;
                GpsStatus = "PANDA data timed out.";
                UpdateApplicationRate();
            }
            GpsConnected = gpsConnected;

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
        }

        private void ParseGpsPacket(string packet)
        {
            string line = packet.Trim();
            if (!line.StartsWith("$PANDA,", StringComparison.OrdinalIgnoreCase))
                return;

            _lastGpsPacket = DateTime.UtcNow;
            GpsConnected = true;

            string[] fields = line.Split(',');
            if (fields.Length < 16 || !int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fixQuality) ||
                fixQuality is < 0 or > 8)
            {
                GpsPositionValid = false;
                GpsStatus = "Invalid PANDA packet: missing or invalid fix quality.";
                Debug.WriteLine($"Invalid PANDA packet: {line}");
                UpdateApplicationRate();
                return;
            }

            GpsFixQuality = fixQuality;
            if (fixQuality == 0)
            {
                GpsPositionValid = false;
                GpsStatus = "PANDA reports no GPS fix.";
                UpdateApplicationRate();
                return;
            }

            if (!TryParseNmeaCoordinate(fields[2], fields[3], isLatitude: true, out double latitude) ||
                !TryParseNmeaCoordinate(fields[4], fields[5], isLatitude: false, out double longitude))
            {
                GpsPositionValid = false;
                GpsStatus = "Invalid PANDA packet: latitude or longitude is malformed.";
                Debug.WriteLine($"Invalid PANDA coordinates: {line}");
                UpdateApplicationRate();
                return;
            }

            GpsLatitude = latitude;
            GpsLongitude = longitude;
            GpsSatellites = int.TryParse(fields[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int satellites)
                ? Math.Max(0, satellites)
                : 0;
            GpsPositionValid = true;
            GpsStatus = $"Fix quality {fixQuality}; {GpsSatellites} satellites.";
            UpdateApplicationRate();
        }

        private static bool TryParseNmeaCoordinate(string coordinate, string hemisphere, bool isLatitude, out double decimalDegrees)
        {
            decimalDegrees = 0;
            if (!double.TryParse(coordinate, NumberStyles.Float, CultureInfo.InvariantCulture, out double nmeaValue) ||
                !double.IsFinite(nmeaValue) || nmeaValue < 0)
                return false;

            string direction = hemisphere.Trim().ToUpperInvariant();
            if (isLatitude ? direction is not ("N" or "S") : direction is not ("E" or "W"))
                return false;

            double degrees = Math.Floor(nmeaValue / 100);
            double minutes = nmeaValue - degrees * 100;
            double maximumDegrees = isLatitude ? 90 : 180;
            if (minutes >= 60 || degrees > maximumDegrees || (degrees == maximumDegrees && minutes > 0))
                return false;

            decimalDegrees = degrees + minutes / 60;
            if (direction is "S" or "W")
                decimalDegrees = -decimalDegrees;
            return true;
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
            if (data == null || data.Length == 0)
                return;

            string textPacket = Encoding.ASCII.GetString(data).Trim();
            if (textPacket.StartsWith("$PANDA,", StringComparison.OrdinalIgnoreCase))
            {
                ParseGpsPacket(textPacket);
                return;
            }

            _lastUdp = DateTime.Now;
            if (data.Length < 6)
                return;

            byte pgn = data[3];
            LastPgn = $"0x{pgn:X2}";

            // GPS output PGN: longitude and latitude as little-endian doubles.
            if (pgn == 0x64)
            {
                ParseGpsOutPacket(data);
            }
            // Speed PGN (Steer data - same as Python)
            else if (pgn == 0xFE)
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

        private void ParseGpsOutPacket(byte[] data)
        {
            _lastGpsPacket = DateTime.UtcNow;
            GpsConnected = true;

            int payloadLength = data[4];
            if (payloadLength is not (16 or 24) || data.Length != payloadLength + 6)
            {
                GpsPositionValid = false;
                GpsStatus = "Invalid GPSOut packet length.";
                Debug.WriteLine($"Invalid GPSOut packet length: {data.Length} bytes, payload {payloadLength} bytes.");
                UpdateApplicationRate();
                return;
            }

            double longitude = ReadLittleEndianDouble(data, 5);
            double latitude = ReadLittleEndianDouble(data, 13);
            if (!double.IsFinite(longitude) || longitude is < -180 or > 180 ||
                !double.IsFinite(latitude) || latitude is < -90 or > 90)
            {
                GpsPositionValid = false;
                GpsStatus = "Invalid GPSOut coordinates.";
                Debug.WriteLine($"GPSOut coordinates out of range: longitude {longitude}, latitude {latitude}.");
                UpdateApplicationRate();
                return;
            }

            GpsLongitude = longitude;
            GpsLatitude = latitude;
            GpsPositionValid = true;
            UpdateApplicationRate();
            GpsStatus = "GPS position received";
        }

        private void UpdateApplicationRate()
        {
            if (!ShapefileLoaded)
            {
                _rateZoneValid = false;
                CurrentApplicationRate = "Shapefile rate control off; using normal spreader control.";
                return;
            }

            string? rateText = null;
            string status;

            if (!GpsPositionValid)
            {
                status = "No valid GPS fix; spreading disabled.";
            }
            else if (!IsShapefileGeographic)
            {
                status = "Rate lookup requires a geographic shapefile; spreading disabled.";
            }
            else if (string.IsNullOrWhiteSpace(SelectedShapefileField))
            {
                status = "Select a DBF rate field; spreading disabled.";
            }
            else
            {
                Point position = new(GpsLongitude, GpsLatitude);
                ShapefileFeature? matchingFeature = ShapefileFeatures.FirstOrDefault(feature => feature.Contains(position));
                if (matchingFeature == null)
                {
                    status = "Outside rate polygons; spreading disabled.";
                }
                else if (!matchingFeature.Attributes.TryGetValue(SelectedShapefileField, out string? candidate) ||
                         !double.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out double numericRate) ||
                         !double.IsFinite(numericRate) || numericRate < 0)
                {
                    status = $"Rate field '{SelectedShapefileField}' is missing or invalid; spreading disabled.";
                }
                else
                {
                    rateText = candidate.Trim();
                    status = $"Inside rate polygon; application rate {rateText}.";
                }
            }

            bool rateValid = rateText != null;
            bool rateChanged = !string.Equals(_currentApplicationRate, rateText, StringComparison.Ordinal);
            _rateZoneValid = rateValid;
            CurrentApplicationRate = rateText ?? status;
            GpsStatus = status;

            if (rateValid)
            {
                if (rateChanged || _lastSentApplicationRate == null)
                {
                    SendToSpreader($"S:AppRat:{rateText}:C");
                    _lastSentApplicationRate = rateText;
                }
            }
            else
            {
                _lastSentApplicationRate = null;
            }

            SendEnable();
        }

        private static double ReadLittleEndianDouble(byte[] data, int offset) =>
            BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset, sizeof(double))));

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
                _serial.Open();
                SerialConnected = true;
                _lastSentApplicationRate = null;

                // Force enable/width to be resent to the (re)connected spreader
                _activeSectionsLast = -1;
                _spreaderEnabledLast = !_rateZoneValid;
                UpdateApplicationRate();
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
            int activeSections = _validConfig ? CountActiveSections() : 0;
            bool rateAllowsSpreading = !ShapefileLoaded || _rateZoneValid;
            bool shouldEnable = _validConfig && rateAllowsSpreading && activeSections > 0;
            if (activeSections == _activeSectionsLast && shouldEnable == _spreaderEnabledLast)
                return;

            _activeSectionsLast = activeSections;
            _spreaderEnabledLast = shouldEnable;

            // Format: {S:SOrlSE:1:checksum} (1 = spreading enabled, 0 = disabled)
            SendToSpreader(shouldEnable ? "S:SOrlSE:1:" : "S:SOrlSE:0:");
            if (_validConfig)
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