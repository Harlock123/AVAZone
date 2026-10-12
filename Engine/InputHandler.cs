using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace AVAZone.Engine;

public class InputHandler : IDisposable
{
    private readonly HashSet<Key> _pressedKeys = new();
    private readonly ControllerManager _controller = new();

    // Threshold for analog stick to register as direction
    private const float StickThreshold = 0.3f;
    // Threshold for trigger to register as pressed
    private const float TriggerThreshold = 0.3f;

    // Mouse state
    private bool _leftMousePressed;
    private bool _rightMousePressed;
    private bool _middleMousePressed;
    private double _mouseWheelDelta;

    public void OnKeyDown(Key key)
    {
        _pressedKeys.Add(key);
    }

    public void OnKeyUp(Key key)
    {
        _pressedKeys.Remove(key);
    }

    public bool IsKeyPressed(Key key)
    {
        return _pressedKeys.Contains(key);
    }

    public void OnMouseDown(MouseButton button)
    {
        if (button == MouseButton.Left)
            _leftMousePressed = true;
        else if (button == MouseButton.Right)
            _rightMousePressed = true;
        else if (button == MouseButton.Middle)
            _middleMousePressed = true;
    }

    public void OnMouseUp(MouseButton button)
    {
        if (button == MouseButton.Left)
            _leftMousePressed = false;
        else if (button == MouseButton.Right)
            _rightMousePressed = false;
        else if (button == MouseButton.Middle)
            _middleMousePressed = false;
    }

    public void OnMouseWheel(double delta)
    {
        _mouseWheelDelta += delta;
    }

    public void Update()
    {
        _controller.Update();
    }

    public void PostUpdate()
    {
        // Reset mouse wheel delta after each frame
        _mouseWheelDelta = 0;
    }

    public bool IsLeftMousePressed => _leftMousePressed;
    public bool IsRightMousePressed => _rightMousePressed;
    public double MouseWheelDelta => _mouseWheelDelta;

    public bool IsControllerConnected => _controller.IsConnected;

    // Controller-only menu inputs, for initials entry (the keyboard is handled key by key there).
    public bool PadUp => _controller.DPadUp || _controller.LeftStickY < -0.5f;
    public bool PadDown => _controller.DPadDown || _controller.LeftStickY > 0.5f;
    public bool PadLeft => _controller.DPadLeft || _controller.LeftStickX < -0.5f;
    public bool PadRight => _controller.DPadRight || _controller.LeftStickX > 0.5f;
    public bool PadAccept => _controller.ButtonX || _controller.ButtonB || _controller.ButtonA;
    public bool PadDone => _controller.ButtonStart;

    /// <summary>Any key, mouse button, wheel turn or controller input at all this frame.</summary>
    public bool IsAnyInput =>
        _pressedKeys.Count > 0 ||
        _leftMousePressed || _rightMousePressed || _middleMousePressed || _mouseWheelDelta != 0 ||
        Math.Abs(LeftTread) > 0.01 || Math.Abs(RightTread) > 0.01 || IsFiring ||
        IsPausePressed || IsBackPressed;

    /// <summary>
    /// The two treads, each -1 (full reverse) to 1 (full ahead), as the cabinet's two sticks set them.
    /// From the keyboard, forward/back drive both and left/right turn by running them opposite ways. A
    /// gamepad drives the same way with its left stick, or like the cabinet if the right stick is used:
    /// left stick for the left tread, right stick for the right.
    /// </summary>
    public double LeftTread => Treads().Left;
    public double RightTread => Treads().Right;

    private (double Left, double Right) Treads()
    {
        if (Math.Abs(_controller.RightStickY) > StickThreshold)
            return (-_controller.LeftStickY, -_controller.RightStickY);

        double forward = 0, turn = 0;
        if (IsKeyPressed(Key.W) || IsKeyPressed(Key.Up) || _controller.DPadUp) forward += 1;
        if (IsKeyPressed(Key.S) || IsKeyPressed(Key.Down) || _controller.DPadDown) forward -= 1;
        if (IsKeyPressed(Key.D) || IsKeyPressed(Key.Right) || _controller.DPadRight) turn += 1;
        if (IsKeyPressed(Key.A) || IsKeyPressed(Key.Left) || _controller.DPadLeft) turn -= 1;
        if (Math.Abs(_controller.LeftStickY) > StickThreshold * 0.5) forward -= _controller.LeftStickY; // stick up is negative
        if (Math.Abs(_controller.LeftStickX) > StickThreshold * 0.5) turn += _controller.LeftStickX;
        if (_rightMousePressed) forward += 1;
        forward = Math.Clamp(forward, -1, 1);
        turn = Math.Clamp(turn, -1, 1);
        return (Math.Clamp(forward + turn, -1, 1), Math.Clamp(forward - turn, -1, 1));
    }

    /// <summary>The cannon's fire button.</summary>
    public bool IsFiring =>
        IsKeyPressed(Key.Space) ||
        IsKeyPressed(Key.LeftCtrl) || IsKeyPressed(Key.RightCtrl) ||
        _controller.ButtonA || _controller.ButtonB || _controller.ButtonX ||
        _controller.RightBumper || _controller.LeftBumper ||
        _controller.RightTrigger > TriggerThreshold || _controller.LeftTrigger > TriggerThreshold ||
        _leftMousePressed;

    public bool IsPausePressed =>
        _controller.ButtonStart;

    public bool IsBackPressed =>
        _controller.ButtonBack;

    public void Dispose()
    {
        _controller.Dispose();
    }
}
