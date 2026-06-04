namespace MIM.Models
{
    public class GamepadState
    {
        public bool IsConnected { get; set; }
        public string DeviceName { get; set; } = "Brak";
        public double LeftStickX { get; set; }
        public double LeftStickY { get; set; }
        public double RightStickX { get; set; }
        public double RightStickY { get; set; }
    }
}