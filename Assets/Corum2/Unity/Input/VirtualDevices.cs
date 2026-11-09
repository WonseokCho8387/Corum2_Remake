using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Corum2.Unity
{
    /// <summary>
    /// Evidence-run input at the Input System device level: a virtual keyboard and gamepad receive queued state events,
    /// so the same bindings and action callbacks as real hardware run. This is NOT physical hardware input; device
    /// names start with "Corum2Virtual" so logs can tell them apart from a real "Keyboard" or pad.
    /// </summary>
    public sealed class VirtualDevices : IDisposable
    {
        public const string KeyboardName = "Corum2VirtualKeyboard";
        public const string GamepadName = "Corum2VirtualGamepad";

        readonly Keyboard _kb;
        readonly Gamepad _pad;
        readonly HashSet<Key> _keys = new HashSet<Key>();
        GamepadState _padState;

        public VirtualDevices()
        {
            _kb = InputSystem.AddDevice<Keyboard>(KeyboardName);
            _pad = InputSystem.AddDevice<Gamepad>(GamepadName);
            _padState = new GamepadState();
        }

        public void Key(string keyName, bool down)
        {
            var key = (Key)Enum.Parse(typeof(Key), keyName, true);
            if (down) _keys.Add(key);
            else _keys.Remove(key);
            var arr = new Key[_keys.Count];
            _keys.CopyTo(arr);
            InputSystem.QueueStateEvent(_kb, new KeyboardState(arr));
        }

        public void Stick(float x, float y)
        {
            _padState.leftStick = new Vector2(x, y);
            InputSystem.QueueStateEvent(_pad, _padState);
        }

        public void Button(string name, bool down)
        {
            var b = (GamepadButton)Enum.Parse(typeof(GamepadButton), name, true);
            _padState = _padState.WithButton(b, down);
            InputSystem.QueueStateEvent(_pad, _padState);
        }

        public void Dispose()
        {
            if (_kb != null && _kb.added) InputSystem.RemoveDevice(_kb);
            if (_pad != null && _pad.added) InputSystem.RemoveDevice(_pad);
        }
    }
}
