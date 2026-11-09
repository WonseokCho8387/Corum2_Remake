using System;
using System.Collections.Generic;
using Corum2.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Corum2.Unity
{
    /// <summary>
    /// Input System -> logical actions (appendix 3.2 item 1). Every press/release is recorded from the action callback
    /// with its own event time, so presses shorter than a render frame are not lost. Events wait in the
    /// InputTransport until the next fixed tick drains them.
    /// Input System update mode is the project default (dynamic update): events processed in a frame reach the next
    /// FixedUpdate, and the delay is logged per event as "latency".
    /// </summary>
    public sealed class InputCollector : IDisposable
    {
        readonly InputTransport _transport;
        readonly DirectionEdgeTracker _dir = new DirectionEdgeTracker();
        readonly List<InputEvent> _scratch = new List<InputEvent>();
        readonly InputActionMap _map;
        readonly InputAction _move;
        readonly float _deadzone;
        Vector2 _stick;
        string _lastDevice = "none";

        public InputCollector(InputProfileConfig profile, InputTransport transport)
        {
            _transport = transport;
            _deadzone = profile.stickDeadzone;
            _map = new InputActionMap("Corum2");

            _move = _map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            foreach (var path in profile.moveStickPaths) _move.AddBinding(path);
            foreach (var composite in profile.moveKeyComposites)
            {
                var keys = composite.Split(',');
                if (keys.Length != 4) continue;
                _move.AddCompositeBinding("2DVector")
                    .With("Up", keys[0].Trim())
                    .With("Down", keys[1].Trim())
                    .With("Left", keys[2].Trim())
                    .With("Right", keys[3].Trim());
            }
            _move.performed += OnMove;
            _move.canceled += OnMove;

            foreach (var b in profile.buttons)
            {
                var logical = b.action;
                var a = _map.AddAction(logical.ToString(), InputActionType.Button);
                foreach (var path in b.paths) a.AddBinding(path);
                a.performed += ctx => OnButton(ctx, logical, InputEdge.Press);
                a.canceled += ctx => OnButton(ctx, logical, InputEdge.Release);
            }
            _map.Enable();
        }

        public Dir8 CurrentDir => _dir.Current;

        void OnMove(InputAction.CallbackContext ctx)
        {
            _stick = ctx.ReadValue<Vector2>();
            _lastDevice = ctx.control != null ? ctx.control.device.name : _lastDevice;
            var d = Dir8Util.FromStick(_stick.x, _stick.y, _deadzone);
            EmitDir(d, ctx.time, _lastDevice, ctx.control != null ? ctx.control.path : "move");
        }

        void OnButton(InputAction.CallbackContext ctx, LogicalAction action, InputEdge edge)
        {
            _lastDevice = ctx.control != null ? ctx.control.device.name : _lastDevice;
            _transport.Enqueue(new InputEvent(_transport.NextEventId(), action, edge, ctx.time, _lastDevice, ctx.control != null ? ctx.control.path : action.ToString()));
        }

        void EmitDir(Dir8 d, double time, string device, string control)
        {
            _scratch.Clear();
            _dir.Update(d, time, device, control, _transport.NextEventId, _scratch);
            foreach (var e in _scratch) _transport.Enqueue(e);
        }

        // ---------------------------------------------------------------- scripted input (evidence runs)

        public void InjectDir(Dir8 d)
        {
            _lastDevice = "script";
            _stick = new Vector2(Dir8Util.Horizontal(d), Dir8Util.Vertical(d));
            EmitDir(d, Time.realtimeSinceStartupAsDouble, "script", "script/dir");
        }

        public void InjectButton(LogicalAction a, InputEdge edge)
        {
            _lastDevice = "script";
            _transport.Enqueue(new InputEvent(_transport.NextEventId(), a, edge, Time.realtimeSinceStartupAsDouble, "script", "script/" + a));
        }

        public void Fill(TickInput tick)
        {
            _transport.Drain(tick);
            tick.Dir = _dir.Current;
            tick.StickX = _stick.x;
            tick.StickY = _stick.y;
            tick.Device = _lastDevice;
            tick.RealTime = Time.realtimeSinceStartupAsDouble;
        }

        public void Dispose()
        {
            _map.Disable();
            _map.Dispose();
        }
    }
}
