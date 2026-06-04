using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Timers;
using MIM.Models;
using MIM.Services;

#if WINDOWS
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
#endif

namespace MIM
{
    public class MainPageViewModel : INotifyPropertyChanged
    {
        private readonly IGamepadService _gamepadService;
        private readonly System.Timers.Timer _sendTimer;
        private bool _isSending;

        private double _motorSpeedFL;
        private double _motorSpeedFR;
        private double _motorSpeedRL;
        private double _motorSpeedRR;
        private string _videoStreamStatus;
        private string _selectedDevice;
        private bool _isConnected;
        private Color _connectionStatusColor;
        private string _connectionStatusText;
        private string _manualCommand;

        private double _leftStickX;
        private double _leftStickY;
        private double _rightStickX;
        private double _rightStickY;
        private string _gamepadStatus;
        private bool _isGamepadConnected;

#if WINDOWS
        private StreamSocket _socket;
        private DataWriter _writer;
        private DataReader _reader;
        private const string EspIp = "172.20.10.10";   // ZMIEŃ NA IP TWOJEGO ESP
        private const string EspPort = "8080";
#endif

        public event PropertyChangedEventHandler PropertyChanged;

        public MainPageViewModel()
        {
            _gamepadService = new WindowsGamepadService();
            _gamepadService.GamepadStateChanged += OnGamepadStateChanged;
            _gamepadService.Start();

            _sendTimer = new System.Timers.Timer(100);
            _sendTimer.AutoReset = true;
            _sendTimer.Elapsed += SendTimerElapsed;
            _sendTimer.Start();

            BluetoothDevices = new ObservableCollection<string>();

            VideoStreamStatus = "Oczekiwanie na strumień wideo... (00:00:00)";
            ConnectionStatusColor = Colors.Red;
            ConnectionStatusText = "Rozłączono";
            GamepadStatus = "Pad: brak";
            IsGamepadConnected = false;

            MotorSpeedFL = 0;
            MotorSpeedFR = 0;
            MotorSpeedRL = 0;
            MotorSpeedRR = 0;

            ScanCommand = new Command(ExecuteScan);
            ConnectCommand = new Command(ExecuteConnect, () => !IsConnected);
            DisconnectCommand = new Command(ExecuteDisconnect, () => IsConnected);
        }

        public string ManualCommand
        {
            get => _manualCommand;
            set { _manualCommand = value; OnPropertyChanged(); }
        }

        public double LeftStickX
        {
            get => _leftStickX;
            set { _leftStickX = value; OnPropertyChanged(); }
        }

        public double LeftStickY
        {
            get => _leftStickY;
            set { _leftStickY = value; OnPropertyChanged(); }
        }

        public double RightStickX
        {
            get => _rightStickX;
            set { _rightStickX = value; OnPropertyChanged(); }
        }

        public double RightStickY
        {
            get => _rightStickY;
            set { _rightStickY = value; OnPropertyChanged(); }
        }

        public string GamepadStatus
        {
            get => _gamepadStatus;
            set { _gamepadStatus = value; OnPropertyChanged(); }
        }

        public bool IsGamepadConnected
        {
            get => _isGamepadConnected;
            set { _isGamepadConnected = value; OnPropertyChanged(); }
        }

        public bool IsNotConnected => !IsConnected;

        public double MotorSpeedFL
        {
            get => _motorSpeedFL;
            set { _motorSpeedFL = value; OnPropertyChanged(); }
        }

        public double MotorSpeedFR
        {
            get => _motorSpeedFR;
            set { _motorSpeedFR = value; OnPropertyChanged(); }
        }

        public double MotorSpeedRL
        {
            get => _motorSpeedRL;
            set { _motorSpeedRL = value; OnPropertyChanged(); }
        }

        public double MotorSpeedRR
        {
            get => _motorSpeedRR;
            set { _motorSpeedRR = value; OnPropertyChanged(); }
        }

        public string VideoStreamStatus
        {
            get => _videoStreamStatus;
            set { _videoStreamStatus = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> BluetoothDevices { get; set; }

        public string SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                _selectedDevice = value;
                OnPropertyChanged();
                ((Command)ConnectCommand).ChangeCanExecute();
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotConnected));
                ((Command)ConnectCommand).ChangeCanExecute();
                ((Command)DisconnectCommand).ChangeCanExecute();
            }
        }

        public Color ConnectionStatusColor
        {
            get => _connectionStatusColor;
            set { _connectionStatusColor = value; OnPropertyChanged(); }
        }

        public string ConnectionStatusText
        {
            get => _connectionStatusText;
            set { _connectionStatusText = value; OnPropertyChanged(); }
        }

        public ICommand ScanCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }

        public ICommand SendManualCommand => new Command(async () =>
        {
            if (!string.IsNullOrWhiteSpace(ManualCommand))
            {
                string[] parts = ManualCommand.Split(',');
                if (parts.Length == 4 &&
                    byte.TryParse(parts[0], out byte m1) &&
                    byte.TryParse(parts[1], out byte m2) &&
                    byte.TryParse(parts[2], out byte m3) &&
                    byte.TryParse(parts[3], out byte m4))
                {
                    await SendRawBytes(new[] { m1, m2, m3, m4 });
                }
            }
        });

        private void OnGamepadStateChanged(object sender, GamepadState state)
        {
            IsGamepadConnected = state.IsConnected;
            GamepadStatus = state.IsConnected ? $"Pad: {state.DeviceName}" : "Pad: brak";

            LeftStickX = state.LeftStickX;
            LeftStickY = state.LeftStickY;
            RightStickX = state.RightStickX;
            RightStickY = state.RightStickY;

            MotorSpeedFL = ScaleStickToSpeedDisplay(LeftStickY);
            MotorSpeedRL = ScaleStickToSpeedDisplay(LeftStickY);
            MotorSpeedFR = ScaleStickToSpeedDisplay(RightStickY);
            MotorSpeedRR = ScaleStickToSpeedDisplay(RightStickY);
        }

        private async void SendTimerElapsed(object sender, ElapsedEventArgs e)
        {
#if WINDOWS
            if (!IsConnected || _isSending || !IsGamepadConnected)
                return;

            _isSending = true;

            try
            {
                byte speedFL = ScaleStickToSpeed(LeftStickY);
                byte speedRL = ScaleStickToSpeed(LeftStickY);
                byte speedFR = ScaleStickToSpeed(RightStickY);
                byte speedRR = ScaleStickToSpeed(RightStickY);

                bool dirFL = LeftStickY >= 0;
                bool dirRL = LeftStickY >= 0;
                bool dirFR = RightStickY >= 0;
                bool dirRR = RightStickY >= 0;

                byte[] packet = new byte[]
                {
                    BuildMotorFrame(0, dirFL, speedFL), // FL
                    BuildMotorFrame(1, dirFR, speedFR), // FR
                    BuildMotorFrame(2, dirRL, speedRL), // RL
                    BuildMotorFrame(3, dirRR, speedRR)  // RR
                };

                await SendRawBytes(packet);
            }
            finally
            {
                _isSending = false;
            }
#endif
        }

        private void ExecuteScan()
        {
            BluetoothDevices.Clear();
            ConnectionStatusText = "Tryb TCP/Wi‑Fi: skan Bluetooth nie jest używany.";
            ConnectionStatusColor = Colors.Orange;
        }

        private async void ExecuteConnect()
        {
#if WINDOWS
            try
            {
                ConnectionStatusText = "Łączenie z ESP po TCP...";
                ConnectionStatusColor = Colors.Orange;

                _socket = new StreamSocket();
                await _socket.ConnectAsync(new HostName(EspIp), EspPort);

                _writer = new DataWriter(_socket.OutputStream);
                _reader = new DataReader(_socket.InputStream);

                IsConnected = true;
                ConnectionStatusText = $"POŁĄCZONO Z ESP ({EspIp}:{EspPort})";
                ConnectionStatusColor = Colors.Green;
            }
            catch (Exception ex)
            {
                ConnectionStatusText = $"Błąd połączenia: {ex.Message}";
                ConnectionStatusColor = Colors.Red;
                ExecuteDisconnect();
            }
#else
            ConnectionStatusText = "TCP w tej konfiguracji działa tylko na Windows.";
            ConnectionStatusColor = Colors.Red;
#endif
        }

        private void ExecuteDisconnect()
        {
#if WINDOWS
            try
            {
                _writer?.DetachStream();
                _writer?.Dispose();
                _reader?.DetachStream();
                _reader?.Dispose();
                _socket?.Dispose();
            }
            catch
            {
            }

            _writer = null;
            _reader = null;
            _socket = null;
#endif

            IsConnected = false;
            SelectedDevice = null;
            ConnectionStatusText = "Rozłączono";
            ConnectionStatusColor = Colors.Red;
            VideoStreamStatus = "Utracono połączenie wideo.";
        }

        private byte BuildMotorFrame(byte motorId, bool forward, byte speed)
        {
            motorId = (byte)(motorId & 0b00000011);
            byte direction = (byte)(forward ? 1 : 0);
            speed = (byte)(speed & 0b00011111);

            return (byte)((motorId << 6) | (direction << 5) | speed);
        }

        private byte ScaleStickToSpeed(double value)
        {
            double absValue = Math.Abs(value);

            if (absValue < 0.05)
                return 0;

            if (absValue > 1.0)
                absValue = 1.0;

            return (byte)Math.Round(absValue * 31.0);
        }

        private double ScaleStickToSpeedDisplay(double value)
        {
            return ScaleStickToSpeed(value);
        }

        public async Task SendRawBytes(byte[] values)
        {
#if WINDOWS
            if (_socket == null || _writer == null || !IsConnected || values == null || values.Length == 0)
                return;

            try
            {
                _writer.WriteBytes(values);
                await _writer.StoreAsync();
                await _writer.FlushAsync();
            }
            catch
            {
                ExecuteDisconnect();
            }
#endif
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}