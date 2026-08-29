using System;
using SpinMotion;
using UnityEngine;

public class VirtualJoystickCarControls : MonoBehaviour
{
    [SerializeField] private Joystick thrustJoystick;
    [SerializeField] private Joystick steerJoystick;

    private void Update()
    {
        var vertical = thrustJoystick.Vertical;
        var horizontal = steerJoystick.Horizontal;
        
        MobileInputManager.SetAxis("Horizontal", horizontal);
        MobileInputManager.SetAxis("Vertical", vertical);
    }
}
