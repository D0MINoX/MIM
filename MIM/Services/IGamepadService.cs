using MIM.Models;

namespace MIM.Services
{
    public interface IGamepadService
    {
        event EventHandler<GamepadState> GamepadStateChanged;

        GamepadState CurrentState { get; }

        void Start();
        void Stop();
    }
}