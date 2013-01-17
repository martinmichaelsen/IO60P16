﻿using System;
using System.Threading;
using Gadgeteer.Interfaces;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using GTM = Gadgeteer.Modules;
using GTI = Gadgeteer.Interfaces;

#if !USE_DAISYLINK_SOFTWARE_I2C
using GHI.OSHW.Hardware;
#endif

namespace Gadgeteer.Modules.IanLee.IO60P16
{
    /// <summary>
    /// A IO60P16 module for Microsoft .NET Gadgeteer
    /// </summary>
    public class IO60P16Module : GTM.Module
    {
#if HARDWARE_I2C
        private readonly GTI.I2CBus _i2c;
#else
    #if USE_DAISYLINK_SOFTWARE_I2C
        private readonly GTI.SoftwareI2C _i2c;
    #else
        private static SoftwareI2CBus _i2c;
        private static SoftwareI2CBus.I2CDevice _i2cDevice;
    #endif
#endif
        /// <summary>
        /// Address of the device.
        /// </summary>
        private const byte DEV_ADDR = 0x20;

        // Registers
        public const byte INPUT_PORT_0_REGISTER = 0x00;
        public const byte OUTPUT_PORT_0_REGISTER = 0x08;
        public const byte INTERRUPT_STATUS_PORT_0_REGISTER = 0x10;
        public const byte PORT_SELECT_REGISTER = 0x18;
        public const byte INTERRUPT_MASK_PORT_REGISTER = 0x19;
        public const byte INVERSION_REGISTER = 0x1b;
        public const byte PORT_DIRECTION_REGISTER = 0x1c;
        public const byte COMMAND_REGISTER = 0x30;

        private readonly InterruptInput _interrupt;
        public delegate void InterruptEventHandler(object sender, InterruptEventArgs args);
        public event InterruptEventHandler Interrupt;

        private readonly Object _lock = new Object();

        // Note: A constructor summary is auto-generated by the doc builder.
        /// <summary></summary>
        /// <param name="socketNumber">The socket that this module is plugged in to.</param>
        public IO60P16Module(int socketNumber)
        {
            var socket = Socket.GetSocket(socketNumber, true, this, null);
#if HARDWARE_I2C
            var types = new[] {'I'};
#else
            var types = new[] { 'X', 'Y' };
#endif
            socket.EnsureTypeIsSupported(types, this);

#if HARDWARE_I2C
            _i2c = new GTI.I2CBus(socket, DEV_ADDR, 100, this);
#else
    #if USE_DAISYLINK_SOFTWARE_I2C
            _i2c = new GTI.SoftwareI2C(socket, Socket.Pin.Five, Socket.Pin.Four, this);
    #else
            _i2c = new SoftwareI2CBus(socket.CpuPins[4], socket.CpuPins[5]);
            _i2cDevice = _i2c.CreateI2CDevice(DEV_ADDR, 100);
    #endif
#endif

            // Send a Power On Reset (POR) command.
            Reset();

            // Start receiving interrupts.
            _interrupt = new InterruptInput(socket, Socket.Pin.Three, GlitchFilterMode.Off, Interfaces.ResistorMode.Disabled, InterruptMode.RisingEdge, null);
            _interrupt.Interrupt += OnInterrupt;
        }

        /// <summary>
        /// Executes anytime the interrupt pin goes high.
        /// </summary>
        private void OnInterrupt(InterruptInput sender, bool value)
        {
            // Make sure we actually have some subscribers.
            if (Interrupt == null) return;

            var intTime = DateTime.Now;

            // Get the current pin state of all input pins on all ports.
            var pinState = ReadRegister(INPUT_PORT_0_REGISTER, 8);

            // Get the interrupt status of all pins on all ports.
            var status = ReadRegister(INTERRUPT_STATUS_PORT_0_REGISTER, 8);

            // Loop through the enabled ports and find which pin(s) threw the event.
            for (byte port = 0; port < 8; port++)
            {
                // Get the interrupt status of all pins on the port.
                var intMask = status[port];
                if (intMask == 0) continue;         // This port didn't trigger the event.  Move on...

                // Raise events for each of the pins that triggered the interrupt.
                for (byte pin = 0; pin < 8; pin++)
                {
                    if ((intMask & (1 << pin)) > 0)
                    {
                        Interrupt(this, new InterruptEventArgs((IOPin)((port << 4) + pin)
                                                              ,(pinState[port] & (1 << pin)) == 1
                                                              ,intTime));
                    }
                }
            }
        }

        /// <summary>
        /// Sends a power-on reset to the module.  The equivalent of unplugging the power
        /// and plugging it back in.
        /// </summary>
        public void Reset()
        {
            WriteRegister(COMMAND_REGISTER, 0x07);
        }

        /// <summary>
        /// Restores the module configuration to factory defaults.
        /// </summary>
        public void RestoreFactoryDefaults()
        {
            WriteRegister(COMMAND_REGISTER, 0x02);
        }

        /// <summary>
        /// Saves the current configuration as the default initial settings 
        /// after a power on reset.
        /// </summary>
        public void SaveConfiguration()
        {
            WriteRegister(COMMAND_REGISTER, 0x01);
        }

        /// <summary>
        /// Writes a value to a register.
        /// </summary>
        /// <param name="register">The register to write to.</param>
        /// <param name="value">The value to write.</param>
        public void WriteRegister(byte register, byte value)
        {
            byte[] data = new[] { register, value };
            lock (_lock)
            {
#if HARDWARE_I2C
                _i2c.Write(data, 1000);
#else
  #if USE_DAISYLINK_SOFTWARE_I2C
                _i2c.Write(DEV_ADDR, data);
  #else
                _i2cDevice.Write(data, 0, data.Length);
  #endif
#endif
            }
        }

        /// <summary>
        /// Reads a value from a register.
        /// </summary>
        /// <param name="register">The register to read from.</param>
        /// <returns>The value in the register.</returns>
        public byte[] ReadRegister(byte register, byte length = 1)
        {            
            var writeBuffer = new byte[] { register };
            var data = new byte[length];

            lock (_lock)
            {
#if HARDWARE_I2C
                _i2c.Write(writeBuffer, 20);
                _i2c.Read(data, 20);
#else
  #if USE_DAISYLINK_SOFTWARE_I2C
                // Bring the pointer to the needed address
                _i2c.Write(DEV_ADDR, new byte[] { register });
                // Read the address
                _i2c.Read(DEV_ADDR, data);
  #else
                // Bring the pointer to the needed address
                _i2cDevice.Write(writeBuffer, 0, 1);

                // Read the address
                _i2cDevice.Read(data, 0, data.Length);
  #endif
#endif
            }
            return data;
        }

        /// <summary>
        /// Reads the value of a port.
        /// </summary>
        /// <param name="port">The port to read.</param>
        /// <returns>The value of the port.</returns>
        public byte Read(byte port)
        {
            return ReadRegister((byte) (INPUT_PORT_0_REGISTER + port))[0];
        }

        /// <summary>
        /// Reads the value of a pin.
        /// </summary>
        /// <param name="pin">The pin to read.</param>
        /// <returns>High (true) or low (false) state of the pin.</returns>
        public bool Read(IOPin pin)
        {
            var portVal = Read(GetPortNumber(pin));
            var pinNumber = GetPinNumber(pin);
            return (portVal & (1 << pinNumber)) != 0;
        }

        /// <summary>
        /// Writes a value to the specified port.
        /// </summary>
        /// <param name="port">Port to write to.</param>
        /// <param name="value">Value to write to the port.</param>
        public void Write(byte port, byte value)
        {
            WriteRegister((byte)(OUTPUT_PORT_0_REGISTER + port), value);
        }

        /// <summary>
        /// Writes a value to the specified pin.
        /// </summary>
        /// <param name="pin">Pin to write to.</param>
        /// <param name="state">State value to write to the pin.</param>
        public void Write(IOPin pin, bool state)
        {
            Write(GetPortNumber(pin), GetPinNumber(pin), state);
        }

        /// <summary>
        /// Write a value to a single pin of the specified port.
        /// </summary>
        /// <param name="port">Port to write to.</param>
        /// <param name="pin">Pin to write to.</param>
        /// <param name="state">Value to write to the pin.</param>
        public void Write(byte port, byte pin, bool state)
        {
            lock (_lock)
            {
                // Read port
                var b = Read(port);
                // Config pin
                if (state)
                {
                    b |= (byte) (1 << (pin));
                }
                else
                {
                    b &= (byte) ~(1 << (pin));
                }
                // Apply
                Write(port, b);
            }
        }

        /// <summary>
        /// Write to an array of pins simultaneously.
        /// </summary>
        /// <param name="pins">An array of OutputPort objects.</param>
        /// <param name="state">Array of state values for each pin.</param>
        public void Write(OutputPort[] pins, bool[] state)
        {
            
        }

        /// <summary>
        /// Creates a PWM object.
        /// </summary>
        /// <param name="pin">The PWM pin.</param>
        /// <param name="period">The period length.</param>
        /// <param name="pulseWidth">The pulse width.</param>
        /// <returns>A PWM object.</returns>
        public PWM CreatePwm(PwmPin pin, uint period, uint pulseWidth, PWM.ScaleFactor scale, bool invertOutput)
        {
            return new PWM(this, pin, period, pulseWidth, scale, invertOutput);
        }

        /// <summary>
        /// Creates an OutputPort object.
        /// </summary>
        /// <param name="pin">The pin to make an output port.</param>
        /// <param name="initialState">The initial state of the pin.</param>
        /// <returns>An OutputPort object.</returns>
        public IO60P16.OutputPort CreateOutputPort(IOPin pin, bool initialState)
        {
            return new IO60P16.OutputPort(this, pin, initialState);
        }

        /// <summary>
        /// Creates an InputPort object.
        /// </summary>
        /// <param name="pin">The pin to make an input port.</param>
        /// <returns>An InputPort object.</returns>
        public IO60P16.InputPort CreateInputPort(IOPin pin)
        {
            return new IO60P16.InputPort(this, pin);
        }

        /// <summary>
        /// Creates an InterruptPort object.
        /// </summary>
        /// <param name="pin">The pin to make an interrupt port.</param>
        /// <returns>An InterruptPort object.</returns>
        public IO60P16.InterruptPort CreateInterruptPort(IOPin pin)
        {
            return new IO60P16.InterruptPort(this, pin);
        }

        /// <summary>
        /// Sets all the pins on a port to the same resistor mode.
        /// </summary>
        /// <param name="port">The port number to set.</param>
        /// <param name="resistorMode">The resistor mode assign to the port.</param>
        /// <param name="pinMask">The pins to assign to the specified resistor mode. </param>
        public void SetResistorMode(byte port, ResistorMode resistorMode, byte pinMask = 0xff)
        {
            lock (_lock)
            {
                WriteRegister(PORT_SELECT_REGISTER, port);
                WriteRegister((byte) resistorMode, pinMask);
            }
        }

        /// <summary>
        /// Sets all the pins on a port to the same resistor mode.
        /// </summary>
        /// <param name="port">The port number to set.</param>
        /// <param name="pin">The pin to set.</param>
        /// <param name="resistorMode">The resistor mode assign to the port.</param>
        public void SetResistorMode(byte port, byte pin, ResistorMode resistorMode)
        {
            lock (_lock)
            {
                WriteRegister(PORT_SELECT_REGISTER, port);
                var b = ReadRegister((byte) resistorMode)[0]; // Read the current values for the resistor mode.
                b |= (byte) (1 << (pin)); // Config pin
                WriteRegister((byte) resistorMode, b); // Apply
            }
        }

        /// <summary>
        /// Sets the resistor mode on a pin.
        /// </summary>
        /// <param name="pin">The pin.</param>
        /// <param name="resistorMode">The resistor mode to assign to the pin.</param>
        public void SetResistorMode(IOPin pin, ResistorMode resistorMode)
        {
            SetResistorMode(GetPortNumber(pin), GetPinNumber(pin), resistorMode);
        }

        /// <summary>
        /// Gets the current resistor/drive mode of a pin.
        /// </summary>
        /// <param name="pin">The pin to inspect.</param>
        /// <returns>ResistorMode value.</returns>
        public ResistorMode GetResistorMode(IOPin pin)
        {
            lock (_lock)
            {
                var portNum = GetPortNumber(pin);
                var pinNum = GetPinNumber(pin);
                var mask = (byte) (1 << pinNum);

                WriteRegister(PORT_SELECT_REGISTER, portNum); // Move to the port.

                // We have to check all of the drive mode registers until we find a match...
                for (byte reg = 0x1d; reg <= 0x23; reg++)
                {
                    var r = ReadRegister(reg)[0];
                    if ((r & mask) > 0) return (ResistorMode) reg;
                }
            }
            // This should never occur...
            throw new UnknownTypeException();
        }   

        /// <summary>
        /// Sets the direction of a single pin.
        /// </summary>
        public void SetDirection(IOPin pin, PinDirection direction)
        {
            lock (_lock)
            {
                var port = GetPortNumber(pin);
                var pinNum = GetPinNumber(pin);

                // Get current direction values for all pins on the port.
                WriteRegister(PORT_SELECT_REGISTER, port);
                var d = ReadRegister(PORT_DIRECTION_REGISTER)[0];

                // Update just the direction of our pin.
                if (direction == PinDirection.Input)
                {
                    d |= (byte) (1 << (pinNum));
                }
                else
                {
                    d &= (byte) ~(1 << (pinNum));
                }
                WriteRegister(PORT_DIRECTION_REGISTER, d);
            }
        }
        
        /// <summary>
        /// Gets the port number from a given pin ID.
        /// </summary>
        /// <param name="pinId">An IOPin.</param>
        /// <returns>Port number of the given pin.</returns>
        internal static byte GetPortNumber(IOPin pinId)
        {
            return (byte)((byte)(pinId) >> 4);
        }

        /// <summary>
        /// Gets the pin number from a given pin ID.
        /// </summary>
        /// <param name="pin">An IOPin.</param>
        /// <returns>Pin number of the given pin.</returns>
        internal static byte GetPinNumber(IOPin pin)
        {
            return (byte)((byte)(pin) & 0x0f);
        }

        /// <summary>
        /// Sets the IO direction for all pins on a port at once.
        /// </summary>
        /// <param name="port">The port number to update.</param>
        /// <param name="direction">Directions of each pin on the port.  Each bit represents a pin.  0 = output, 1 = input.</param>
        public void SetDirection(byte port, byte direction)
        {
            lock (_lock)
            {
                WriteRegister(PORT_SELECT_REGISTER, port);          // Select port
                WriteRegister(PORT_DIRECTION_REGISTER, direction);  // write to register
            }
        }

        /// <summary>
        /// Gets a mask of the pins on a port that have interrupts enabled.
        /// </summary>
        /// <param name="port">The port to inspect.</param>
        /// <returns>Pin mask indicating those that have interrupts enabled.</returns>
        public byte GetInterruptsEnabled(byte port)
        {
            lock (_lock)
            {
                WriteRegister(PORT_SELECT_REGISTER, port); // Select port
                return ReadRegister(INTERRUPT_MASK_PORT_REGISTER)[0];
            }
        }

        /// <summary>
        /// Enables interrupts for all pins on a port according to a mask.
        /// </summary>
        /// <param name="port">The port to update.</param>
        /// <param name="enableMask">Mask that specifies the port enable setting for each pin.  1 = enabled, 0 = disabled.</param>
        public void SetInterruptEnable(byte port, byte enableMask)
        {
            lock (_lock)
            {
                WriteRegister(PORT_SELECT_REGISTER, port);                  // Select port
                WriteRegister(INTERRUPT_MASK_PORT_REGISTER, enableMask);    // Enable interrupt for select pins on the port.                    
            }
        }       

        /// <summary>
        /// Enable interrupts for a pin.
        /// </summary>
        /// <param name="pin">The pin to enable interrupts on.</param>
        /// <param name="enable">Enable (true) or disable (false)?</param>
        public void SetInterruptEnable(IOPin pin, bool enable)
        {
            var pinNum = GetPinNumber(pin);
            var port = GetPortNumber(pin);

            lock (_lock)
            {
                WriteRegister(PORT_SELECT_REGISTER, port); // Select port
                var b = ReadRegister(INTERRUPT_MASK_PORT_REGISTER)[0]; // Read the current values of the port.

                // Update just the interrupt enable of our pin.
                if (!enable) // A 1 status on this register means "disabled".
                {
                    b |= (byte) (1 << (pinNum));
                }
                else
                {
                    b &= (byte) ~(1 << (pinNum));
                }
                WriteRegister(INTERRUPT_MASK_PORT_REGISTER, b);
            }
        }

        /// <summary>
        /// Inverts the input logic of an input port.
        /// </summary>
        /// <param name="port">The port to update.</param>
        /// <param name="enableMask">Mask specifying the pins to enable inversion.</param>
        public void EnableInputLogicInversion(byte port, byte enableMask)
        {
            lock (_lock)
            {
                WriteRegister(PORT_SELECT_REGISTER, port);      // Select port
                WriteRegister(INVERSION_REGISTER, enableMask);  // Set the intersion flags.
            }
        }

        /// <summary>
        /// Inverts the input logic of an input pin.
        /// </summary>
        /// <param name="pin">The pin to update.</param>
        /// <param name="enable">Enable (true) or disable (false) inversion of the logic input.</param>
        public void EnableInputLogicInversion(IOPin pin, bool enable)
        {
            lock (_lock)
            {
                var pinNum = GetPinNumber(pin);
                var port = GetPortNumber(pin);

                WriteRegister(PORT_SELECT_REGISTER, port);  // Select port
                var b = ReadRegister(INVERSION_REGISTER)[0];   // Read the current values of the port.

                // Update just the interrupt enable of our pin.
                if (enable)
                {
                    b |= (byte) (1 << (pinNum));
                }
                else
                {
                    b &= (byte) ~(1 << (pinNum));
                }
                WriteRegister(INVERSION_REGISTER, b);
            }
        }
    }

    /// <summary>
    /// Event arguments that are passed during an Interrupt event.
    /// </summary>
    public class InterruptEventArgs : EventArgs
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="pinId">The pin that raised the event.</param>
        /// <param name="pinState">The state value of the pin at the time the event was raised.</param>
        /// <param name="timestamp">The date/time that the event was raised.</param>
        public InterruptEventArgs(IOPin pinId, bool pinState, DateTime timestamp)
        {
            PinId = pinId;
            Timestamp = timestamp;
            PinState = pinState;
        }

        /// <summary>
        /// The pin that raised the event.
        /// </summary>
        public IOPin PinId { get; private set; }

        /// <summary>
        /// The state value of the pin at the time the event was raised.
        /// </summary>
        public bool PinState { get; private set; }

        /// <summary>
        /// The date/time that the event was raised.
        /// </summary>
        public DateTime Timestamp { get; private set; }
    }

    /// <summary>
    /// Identifies a pin's IO direction.
    /// </summary>
    public enum PinDirection : byte
    {
        Output = 0,
        Input = 1
    }

    /// <summary>
    /// IO pin drive modes.
    /// </summary>
    public enum ResistorMode : byte
    {
        /// <summary>
        /// This mode is the opposite of the Pull-down mode. In this mode, HIGH output is driven strong and LOW output 
        /// is through an internal pull-down resistor of approximately 5.6K. 
        /// This mode may be used as an input, for example with a switch connected to GND. 
        /// Once the pull-up resistor is enabled, the state of the pin is read using the input register. 
        /// This mode may also be used as output.
        /// </summary>
        ResistivePullUp = 0x1D,
        /// <summary>
        /// In this mode, HIGH output is driven strong, and LOW output is through an internal pull-down resistor of approximately 5.6K. 
        /// This mode may be used as an input, for example with a switch connected to 3.3V. This mode may also be used as output.
        /// </summary>
        ResistivePullDown = 0x1E,   
        /// <summary>
        /// In this mode, the HIGH output is driven with a slow strong drive. The LOW output is high impedence.
        /// </summary>
        OpenDrainHigh = 0x1F,       
        /// <summary>
        /// In this mode, the LOW output is driven with a slow strong drive and HIGH output is high impedence.
        /// This mode is suitable for I2C bus where external pull-up resistors are used.
        /// </summary>
        OpenDrainLow = 0x20,        
        /// <summary>
        /// Strong High, Strong Low, FastOutput Mode.
        /// Use the Strong mode if your pin is an output driving a load. 
        /// The pin has a low impedance connection to GND and 3.3V when driven high and low. 
        /// Do not use the Strong mode if the pin is an input.
        /// </summary>
        StrongDrive = 0x21,         
        /// <summary>
        /// Strong High, Strong Low, Slow Output Mode.
        /// This mode is similar to the Strong mode, but the slope of the output is slightly controlled 
        /// so that high harmonics are not present when the output switches.
        /// </summary>
        SlowStrongDrive = 0x22,     
        /// <summary>
        /// High Z mode.
        /// This mode is normally used when the pin will be driven high and low externally of the module.
        /// </summary>
        HighImpedence = 0x23        
    }

    public enum PwmPin : byte
    {
        Pwm0 = 0x60,
        Pwm1,
        Pwm2,
        Pwm3,
        Pwm4,
        Pwm5,
        Pwm6,
        Pwm7,
        Pwm8 = 0x70,
        Pwm9,
        Pwm10,
        Pwm11,
        Pwm12,
        Pwm13,
        Pwm14,
        Pwm15
    }

    public enum IOPin : byte
    {
        Port0_Pin0 = 0x00,
        Port0_Pin1,
        Port0_Pin2,
        Port0_Pin3,
        Port0_Pin4,
        Port0_Pin5,
        Port0_Pin6,
        Port0_Pin7,
        Port1_Pin0 = 0x10,
        Port1_Pin1,
        Port1_Pin2,
        Port1_Pin3,
        Port1_Pin4,
        Port1_Pin5,
        Port1_Pin6,
        Port1_Pin7,
        Port2_Pin0 = 0x20,
        Port2_Pin1,
        Port2_Pin2,
        Port2_Pin3,
        Port3_Pin0 = 0x30,
        Port3_Pin1,
        Port3_Pin2,
        Port3_Pin3,
        Port3_Pin4,
        Port3_Pin5,
        Port3_Pin6,
        Port3_Pin7,
        Port4_Pin0 = 0x40,
        Port4_Pin1,
        Port4_Pin2,
        Port4_Pin3,
        Port4_Pin4,
        Port4_Pin5,
        Port4_Pin6,
        Port4_Pin7,
        Port5_Pin0 = 0x50,
        Port5_Pin1,
        Port5_Pin2,
        Port5_Pin3,
        Port5_Pin4,
        Port5_Pin5,
        Port5_Pin6,
        Port5_Pin7,
        Port6_Pwm0 = 0x60,
        Port6_Pwm1,
        Port6_Pwm2,
        Port6_Pwm3,
        Port6_Pwm4,
        Port6_Pwm5,
        Port6_Pwm6,
        Port6_Pwm7,
        Port7_Pwm8 = 0x70,
        Port7_Pwm9,
        Port7_Pwm10,
        Port7_Pwm11,
        Port7_Pwm12,
        Port7_Pwm13,
        Port7_Pwm14,
        Port7_Pwm15
    }
}
