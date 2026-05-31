using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
#if WINDOWS
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
#endif
using System.IO;

namespace MIM
{
    public class MainPageViewModel : INotifyPropertyChanged
    {
        // --- Pola prywatne ---
        private double _motorSpeedFL;
        private double _motorSpeedFR;
        private double _motorSpeedRL;
        private double _motorSpeedRR;
        private string _videoStreamStatus;
        private string _selectedDevice;
        private bool _isConnected;
        private Color _connectionStatusColor;
        private string _connectionStatusText;
#if WINDOWS
        private StreamSocket _socket;
        private DataWriter _writer;
        private DataReader _reader;
#endif
        private string _manualCommand;
        public string ManualCommand
        {
            get => _manualCommand;
            set { _manualCommand = value; OnPropertyChanged(); }
        }

        // Komenda wywoływana przez przycisk "Wyślij"
        public ICommand SendManualCommand => new Command(async () =>
        {
            if (!string.IsNullOrWhiteSpace(ManualCommand))
            {
                await SendRoverCommand(ManualCommand);
                // Opcjonalnie: czyść pole po wysłaniu
                // ManualCommand = string.Empty; 
            }
        });
        public bool IsNotConnected => !IsConnected;
        // --- Właściwości dla silników ---
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

        // --- Właściwości wideo ---
        public string VideoStreamStatus
        {
            get => _videoStreamStatus;
            set { _videoStreamStatus = value; OnPropertyChanged(); }
        }

        // --- Właściwości Bluetooth ---
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
                OnPropertyChanged(nameof(IsNotConnected)); // <--- DODAJ TĘ LINIJKĘ
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

        // --- Komendy ---
        public ICommand ScanCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }

        public MainPageViewModel()
        {
            BluetoothDevices = new ObservableCollection<string>();

            // Inicjalizacja domyślnych wartości
            VideoStreamStatus = "Oczekiwanie na strumień wideo... (00:00:00)";
            ConnectionStatusColor = Colors.Red;
            ConnectionStatusText = "Rozłączono";

            // Przykładowe wartości prędkości silników
            MotorSpeedFL = 12.5;
            MotorSpeedFR = 12.5;
            MotorSpeedRL = 13.0;
            MotorSpeedRR = 13.0;

            // Inicjalizacja komend
            ScanCommand = new Command(ExecuteScan);
            ConnectCommand = new Command(ExecuteConnect, () => !IsConnected && !string.IsNullOrEmpty(SelectedDevice));
            DisconnectCommand = new Command(ExecuteDisconnect, () => IsConnected);
        }

        private async void ExecuteScan()
        {
            BluetoothDevices.Clear();
            ConnectionStatusText = "Próba dostępu do sprzętu...";

#if WINDOWS
            try
            {
                ConnectionStatusText = "Pobieranie sparowanych urządzeń...";

                // Selektor szukający urządzeń Bluetooth, które są SPAROWANE
                // Używamy GUID dla usług RFCOMM (Serial Port Profile - SPP), z których korzysta HC-05
                string selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);

                var devices = await DeviceInformation.FindAllAsync(selector);

                if (devices.Count == 0)
                {
                    ConnectionStatusText = "Nie znaleziono sparowanych urządzeń.";
                    return;
                }

                foreach (var device in devices)
                {
                    // HC-05 czasami ma nazwę, a czasami tylko ID
                    string name = !string.IsNullOrEmpty(device.Name) ? device.Name : device.Id;

                    if (!BluetoothDevices.Contains(name))
                    {
                        BluetoothDevices.Add(name);
                        System.Diagnostics.Debug.WriteLine($"---> ZNALEZIONO SPAROWANE: {name}");
                    }
                }

                ConnectionStatusText = $"Znaleziono {BluetoothDevices.Count} sparowanych.";
                ConnectionStatusColor = Colors.Green;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BŁĄD: {ex.Message}");
                ConnectionStatusText = "Błąd dostępu do listy urządzeń.";
            }
#endif
        }

        private async void ExecuteConnect()
        {
            if (string.IsNullOrEmpty(SelectedDevice)) return;

#if WINDOWS
            try
            {
                ConnectionStatusText = "Łączenie...";

                var devices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
                var deviceInfo = devices.FirstOrDefault(d => d.Name == SelectedDevice);

                if (deviceInfo == null) return;

                var bluetoothDevice = await BluetoothDevice.FromIdAsync(deviceInfo.Id);
                var rfcommServices = await bluetoothDevice.GetRfcommServicesAsync();

                if (rfcommServices.Services.Count > 0)
                {
                    var service = rfcommServices.Services[0];
                    _socket = new StreamSocket();

                    await _socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName);

                    _writer = new DataWriter(_socket.OutputStream);
                    _reader = new DataReader(_socket.InputStream);

                    IsConnected = true;
                    ConnectionStatusText = "POŁĄCZONO";
                    ConnectionStatusColor = Colors.Green;
                }
            }
            catch (Exception ex)
            {
                ConnectionStatusText = "Błąd połączenia";
                System.Diagnostics.Debug.WriteLine($"Błąd: {ex.Message}");
            }
#else
    // Opcjonalny komunikat dla wersji Android/iOS
    await App.Current.MainPage.DisplayAlert("Info", "Bluetooth Classic w tej wersji obsługuje tylko Windows Desktop.", "OK");
#endif
        }

        private void ExecuteDisconnect()
        {
            IsConnected = false;
            SelectedDevice = null;
            ConnectionStatusText = "Rozłączono";
            ConnectionStatusColor = Colors.Red;
            VideoStreamStatus = "Utracono połączenie wideo.";
        }

        // --- INotifyPropertyChanged ---
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public async Task SendRoverCommand(string command)
        {
#if WINDOWS
            if (_writer == null || !IsConnected)
            {
                System.Diagnostics.Debug.WriteLine("Błąd: Nie połączono z łazikiem.");
                return;
            }

            try
            {
                _writer.WriteString(command);

                // StoreAsync faktycznie wysyła dane z bufora do urządzenia
                await _writer.StoreAsync();
                // FlushAsync upewnia się, że strumień jest czysty
                await _writer.FlushAsync();

                System.Diagnostics.Debug.WriteLine($"Wysłano: {command}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd wysyłania: {ex.Message}");
                ExecuteDisconnect(); // Rozłącz przy błędzie transmisji
            }
#endif
        }
    }
}
