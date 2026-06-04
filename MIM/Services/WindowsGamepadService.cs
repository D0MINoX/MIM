using Microsoft.Maui.Dispatching;
using MIM.Models;

#if WINDOWS
using Windows.Gaming.Input;
#endif

namespace MIM.Services
{
    public class WindowsGamepadService : IGamepadService
    {
        public event EventHandler<GamepadState> GamepadStateChanged;

        public GamepadState CurrentState { get; private set; } = new();

#if WINDOWS
        private Gamepad _gamepad;
        private RawGameController _rawController;
        private IDispatcherTimer _timer;
#endif

        public void Start()
        {
#if WINDOWS
            Gamepad.GamepadAdded += OnGamepadAdded;
            Gamepad.GamepadRemoved += OnGamepadRemoved;

            RawGameController.RawGameControllerAdded += OnRawControllerAdded;
            RawGameController.RawGameControllerRemoved += OnRawControllerRemoved;

            foreach (var g in Gamepad.Gamepads)
            {
                _gamepad = g;
                CurrentState.IsConnected = true;
                CurrentState.DeviceName = "Gamepad";
                break;
            }

            if (_gamepad == null)
            {
                foreach (var raw in RawGameController.RawGameControllers)
                {
                    _rawController = raw;
                    CurrentState.IsConnected = true;
                    CurrentState.DeviceName = string.IsNullOrWhiteSpace(raw.DisplayName)
                        ? $"Raw Controller VID:{raw.HardwareVendorId} PID:{raw.HardwareProductId}"
                        : $"{raw.DisplayName} VID:{raw.HardwareVendorId} PID:{raw.HardwareProductId}";
                    break;
                }
            }

            _timer = Application.Current.Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(50);
            _timer.Tick += (s, e) => Poll();
            _timer.Start();

            RaiseStateChanged();
#endif
        }

        public void Stop()
        {
#if WINDOWS
            if (_timer != null)
            {
                _timer.Stop();
                _timer = null;
            }

            Gamepad.GamepadAdded -= OnGamepadAdded;
            Gamepad.GamepadRemoved -= OnGamepadRemoved;
            RawGameController.RawGameControllerAdded -= OnRawControllerAdded;
            RawGameController.RawGameControllerRemoved -= OnRawControllerRemoved;

            _gamepad = null;
            _rawController = null;
#endif

            CurrentState = new GamepadState
            {
                IsConnected = false,
                DeviceName = "Brak"
            };

            RaiseStateChanged();
        }

#if WINDOWS
        private void OnGamepadAdded(object sender, Gamepad gamepad)
        {
            _gamepad = gamepad;
            _rawController = null;

            CurrentState.IsConnected = true;
            CurrentState.DeviceName = "Gamepad";
            RaiseStateChanged();
        }

        private void OnGamepadRemoved(object sender, Gamepad gamepad)
        {
            if (_gamepad == gamepad)
            {
                _gamepad = null;

                CurrentState = new GamepadState
                {
                    IsConnected = false,
                    DeviceName = "Brak"
                };

                RaiseStateChanged();
            }
        }

        private void OnRawControllerAdded(object sender, RawGameController controller)
        {
            if (_gamepad != null)
                return;

            _rawController = controller;

            CurrentState.IsConnected = true;
            CurrentState.DeviceName = string.IsNullOrWhiteSpace(controller.DisplayName)
                ? $"Raw Controller VID:{controller.HardwareVendorId} PID:{controller.HardwareProductId}"
                : $"{controller.DisplayName} VID:{controller.HardwareVendorId} PID:{controller.HardwareProductId}";

            RaiseStateChanged();
        }

        private void OnRawControllerRemoved(object sender, RawGameController controller)
        {
            if (_rawController == controller)
            {
                _rawController = null;

                CurrentState = new GamepadState
                {
                    IsConnected = false,
                    DeviceName = "Brak"
                };

                RaiseStateChanged();
            }
        }

        private void Poll()
        {
            try
            {
                if (_gamepad != null)
                {
                    var reading = _gamepad.GetCurrentReading();

                    CurrentState.IsConnected = true;
                    CurrentState.DeviceName = "Gamepad";
                    CurrentState.LeftStickX = ScaleTo100(reading.LeftThumbstickX);
                    CurrentState.LeftStickY = ScaleTo100(reading.LeftThumbstickY);
                    CurrentState.RightStickX = ScaleTo100(reading.RightThumbstickX);
                    CurrentState.RightStickY = ScaleTo100(reading.RightThumbstickY);

                    RaiseStateChanged();
                    return;
                }

                if (_rawController != null)
                {
                    bool[] buttons = new bool[_rawController.ButtonCount];
                    GameControllerSwitchPosition[] switches = new GameControllerSwitchPosition[_rawController.SwitchCount];
                    double[] axes = new double[_rawController.AxisCount];

                    _rawController.GetCurrentReading(buttons, switches, axes);

                    CurrentState.IsConnected = true;
                    CurrentState.DeviceName = string.IsNullOrWhiteSpace(_rawController.DisplayName)
                        ? $"Raw Controller VID:{_rawController.HardwareVendorId} PID:{_rawController.HardwareProductId}"
                        : $"{_rawController.DisplayName} VID:{_rawController.HardwareVendorId} PID:{_rawController.HardwareProductId}";

                    MapRawAxes(axes);

                    RaiseStateChanged();
                    return;
                }
            }
            catch
            {
                CurrentState = new GamepadState
                {
                    IsConnected = false,
                    DeviceName = "Błąd odczytu pada"
                };

                RaiseStateChanged();
            }
        }

        private void MapRawAxes(double[] axes)
        {
            CurrentState.LeftStickX = 0;
            CurrentState.LeftStickY = 0;
            CurrentState.RightStickX = 0;
            CurrentState.RightStickY = 0;

            if (axes == null || axes.Length < 4)
                return;

            CurrentState.LeftStickX = NormalizeRawAxis(axes[0]);
            CurrentState.LeftStickY = NormalizeRawAxis(axes[1], invert: true);
            CurrentState.RightStickX = NormalizeRawAxis(axes[2]);
            CurrentState.RightStickY = NormalizeRawAxis(axes[3], invert: true);
        }

        private int NormalizeRawAxis(double value, bool invert = false)
        {
            double normalized = (value * 2.0) - 1.0;
            normalized = Math.Max(-1.0, Math.Min(1.0, normalized));

            if (invert)
                normalized *= -1.0;

            return ScaleTo100(normalized);
        }

        private int ScaleTo100(double value, double deadzone = 0.08)
        {
            if (Math.Abs(value) < deadzone)
                return 0;

            value = Math.Max(-1.0, Math.Min(1.0, value));
            return (int)Math.Round(value * 100.0, 0);
        }
#endif

        private void RaiseStateChanged()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                GamepadStateChanged?.Invoke(this, CurrentState);
            });
        }
    }
}