using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Midi;
using Windows.UI.Xaml.Controls;
using Windows.UI.Core;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Popups;

namespace RolandGP8
{
    public class MIDI
    {
        //public CoreDispatcher coreDispatcher;
        public MidiDeviceWatcher midiOutputDeviceWatcher;
        public MidiDeviceWatcher midiInputDeviceWatcher;
        public IMidiOutPort midiOutPort;
        public MidiInPort midiInPort;
        public Boolean MessageReceived { get; set; }
        public byte MidiOutPortChannel { get; set; }
        public byte MidiInPortChannel { get; set; }
        public byte[] rawData;
        public Int32 MidiOutPortSelectedIndex { get; set; }
        public Int32 MidiInPortSelectedIndex { get; set; }
        public MainPage mainPage { get; set; }

        // Constructor using a combobox for full device watch:
        public MIDI(MainPage mainPage, ComboBox OutputDeviceSelector, ComboBox InputDeviceSelector, CoreDispatcher coreDispatcher, byte MidiOutPortChannel, byte MidiInPortChannel, String deviceName = null)
        {
            //coreDispatcher.ProcessEvents(CoreProcessEventsOption.ProcessAllIfPresent);
            this.mainPage = mainPage;
            MessageReceived = false;
            midiOutputDeviceWatcher = new MidiDeviceWatcher(MidiOutPort.GetDeviceSelector(), OutputDeviceSelector, coreDispatcher);
            midiInputDeviceWatcher = new MidiDeviceWatcher(MidiInPort.GetDeviceSelector(), InputDeviceSelector, coreDispatcher);
            midiOutputDeviceWatcher.StartWatcher();
            midiInputDeviceWatcher.StartWatcher();
            this.MidiOutPortChannel = MidiOutPortChannel;
            this.MidiInPortChannel = MidiInPortChannel;
            Init(deviceName);
            //Init("MIDI");
        }

        // Simpleconstructor that takes the name of the device:
        public MIDI(String deviceName)
        {
            Init(deviceName);
        }

        ~MIDI()
        {
            try
            {
                midiOutputDeviceWatcher.StopWatcher();
                midiInputDeviceWatcher.StopWatcher();
                midiOutPort.Dispose();
                midiInPort.MessageReceived -= MidiInPort_MessageReceived;
                midiInPort.Dispose();
                midiOutPort = null;
                midiInPort = null;
            } catch { }
        }

        private async void Init(String deviceName)
        {
            DeviceInformationCollection midiOutputDevices = await DeviceInformation.FindAllAsync(MidiOutPort.GetDeviceSelector());
            DeviceInformationCollection midiInputDevices = await DeviceInformation.FindAllAsync(MidiInPort.GetDeviceSelector());
            DeviceInformation midiOutDevInfo = null;
            DeviceInformation midiInDevInfo = null;

            foreach (DeviceInformation device in midiOutputDevices)
            {
                if (device.Name.Contains(deviceName) && !device.Name.Contains("CTRL"))
                {
                    midiOutDevInfo = device;
                    break;
                }
            }

            if (midiOutDevInfo != null)
            {
                midiOutPort = await MidiOutPort.FromIdAsync(midiOutDevInfo.Id);
            }

            foreach (DeviceInformation device in midiInputDevices)
            {
                if (device.Name.Contains(deviceName) && !device.Name.Contains("CTRL"))
                {
                    midiInDevInfo = device;
                    break;
                }
            }

            //if (midiInDevInfo != null)
            //{
            //    foreach (DeviceInformation device in midiInputDevices)
            //    {
            //        if (device.Name.Contains(deviceName) && !device.Name.Contains("CTRL"))
            //        {
            //            midiInDevInfo = device;
            //            break;
            //        }
            //    }
            //}

            if (midiInDevInfo != null)
            {
                midiInPort = await MidiInPort.FromIdAsync(midiInDevInfo.Id);
            }

            if (midiOutPort == null)
            {
                System.Diagnostics.Debug.WriteLine("Unable to create MidiOutPort from output device");
            }

            if (midiInPort == null)
            {
                System.Diagnostics.Debug.WriteLine("Unable to create MidiInPort from output device");
            }
        }

        // Returns a list of all names of MIDI devices that has both input and output.
        // All decorations like '2 - ' and ' [3]' are removed since they differ between
        // input and output device.
        public static async Task<List<String>> GetIODeviceNames()
        {
            List<String> outputDevices = new List<String>();
            List<String> inputDevices = new List<String>();
            DeviceInformationCollection midiOutputDevices = await DeviceInformation.FindAllAsync(MidiOutPort.GetDeviceSelector());
            DeviceInformationCollection midiInputDevices = await DeviceInformation.FindAllAsync(MidiInPort.GetDeviceSelector());

            foreach (DeviceInformation device in midiOutputDevices)
            {
                outputDevices.Add(TrimDeviceName(device.Name));
            }

            foreach (DeviceInformation device in midiInputDevices)
            {
                String name = TrimDeviceName(device.Name);
                if (outputDevices.Contains(name))
                {
                    inputDevices.Add(name);
                }
            }

            return inputDevices;
        }

        public static String TrimDeviceName(String name)
        {
            return name.Trim('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ' ', '-', '[', ']');
        }

        public async void OutputDeviceChanged(ComboBox DeviceSelector)
        {
            try
            {
                if (!String.IsNullOrEmpty((String)DeviceSelector.SelectedValue))
                {
                    var midiOutDeviceInformationCollection = midiOutputDeviceWatcher.DeviceInformationCollection;

                    if (midiOutDeviceInformationCollection == null)
                    {
                        return;
                    }

                    DeviceInformation midiOutDevInfo = midiOutDeviceInformationCollection[DeviceSelector.SelectedIndex];

                    if (midiOutDevInfo == null)
                    {
                        return;
                    }

                    midiOutPort = await MidiOutPort.FromIdAsync(midiOutDevInfo.Id);

                    if (midiOutPort == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Unable to create MidiOutPort from output device");
                        return;
                    }
                }
            }
            catch { }
        }

        public async void InputDeviceChanged(ComboBox DeviceSelector)
        {
            try
            {
                if (!String.IsNullOrEmpty((String)DeviceSelector.SelectedValue))
                {
                    var midiInDeviceInformationCollection = midiInputDeviceWatcher.DeviceInformationCollection;

                    if (midiInDeviceInformationCollection == null)
                    {
                        return;
                    }

                    DeviceInformation midiInDevInfo = midiInDeviceInformationCollection[DeviceSelector.SelectedIndex];

                    if (midiInDevInfo == null)
                    {
                        return;
                    }

                    midiInPort.MessageReceived -= mainPage.MidiInPort_MessageReceived;
                    midiInPort = await MidiInPort.FromIdAsync(midiInDevInfo.Id);

                    if (midiInPort == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Unable to create MidiInPort from input device");
                        return;
                    }
                    midiInPort.MessageReceived += mainPage.MidiInPort_MessageReceived;

                    // Out port must follow (We can add an argument controlling this behaviour.):
                    List<String> outputDevices = new List<String>();
                    DeviceInformationCollection midiOutputDevices = await DeviceInformation.FindAllAsync(MidiOutPort.GetDeviceSelector());

                    foreach (DeviceInformation device in midiOutputDevices)
                    {
                        if (device.Name.Contains(TrimDeviceName(midiInDevInfo.Name)))
                        {
                            midiOutPort = await MidiOutPort.FromIdAsync(device.Id);
                        }
                    }
                }
            }
            catch { }
        }

        public void NoteOn(byte currentChannel, byte noteNumber, byte velocity)
        {
            if (midiOutPort != null)
            {
                IMidiMessage midiMessageToSend = new MidiNoteOnMessage(currentChannel, noteNumber, velocity);
                midiOutPort.SendMessage(midiMessageToSend);
            }
        }

        public void NoteOff(byte currentChannel, byte noteNumber)
        {
            if (midiOutPort != null)
            {
                IMidiMessage midiMessageToSend = new MidiNoteOnMessage(currentChannel, noteNumber, 0);
                midiOutPort.SendMessage(midiMessageToSend);
            }
        }

        public void SendControlChange(byte channel, byte controller, byte value)
        {
            if (midiOutPort != null)
            {
                IMidiMessage midiMessageToSend = new MidiControlChangeMessage(channel, controller, value);
                midiOutPort.SendMessage(midiMessageToSend);
            }
        }

        public void SetVolume(byte currentChannel, byte volume)
        {
            if (midiOutPort != null)
            {
                IMidiMessage midiMessageToSend = new MidiControlChangeMessage(currentChannel, 0x07, volume);
                midiOutPort.SendMessage(midiMessageToSend);
            }
        }

        public void ProgramChange(byte currentChannel, String smsb, String slsb, String spc)
        {
            try
            {
                MidiControlChangeMessage controlChangeMsb = new MidiControlChangeMessage(currentChannel, 0x00, (byte)(UInt16.Parse(smsb)));
                MidiControlChangeMessage controlChangeLsb = new MidiControlChangeMessage(currentChannel, 0x20, (byte)(UInt16.Parse(slsb)));
                MidiProgramChangeMessage programChange = new MidiProgramChangeMessage(currentChannel, (byte)(UInt16.Parse(spc) - 1));
                midiOutPort.SendMessage(controlChangeMsb);
                midiOutPort.SendMessage(controlChangeLsb);
                midiOutPort.SendMessage(programChange);
            }
            catch { }
        }

        public void ProgramChange(byte currentChannel, byte msb, byte lsb, byte pc)
        {
            try
            {
                MidiProgramChangeMessage programChange = new MidiProgramChangeMessage(currentChannel, (byte)(pc - 1));
                if (msb != 0xff)
                {
                    MidiControlChangeMessage controlChangeMsb = new MidiControlChangeMessage(currentChannel, 0x00, msb);
                    MidiControlChangeMessage controlChangeLsb = new MidiControlChangeMessage(currentChannel, 0x20, lsb);
                    midiOutPort.SendMessage(controlChangeMsb);
                    midiOutPort.SendMessage(controlChangeLsb);
                }
                midiOutPort.SendMessage(programChange);
            }
            catch { }
        }

        public async void SendSystemExclusive(byte[] bytes)
        {
            if (midiOutPort == null)
            {
                MessageDialog warning = new MessageDialog("It seems like you do not have a GP-8 connected via MIDI output.\r\n" +
                    "Please make sure you have connected your GP-8, input and output, via a MIDI device.\r\n" +
                    "Restart the application when you have connected our GP-8.\r\n" +
                    "\r\nApplication will now close.");
                warning.Title = "Roland GP-8 editor by MrMartin";
                warning.Commands.Add(new UICommand { Label = "Close app", Id = 0 });
                await warning.ShowAsync();
                App.Current.Exit();
            }
            else
            {
                IBuffer buffer = bytes.AsBuffer();
                //midiOutPort.SendBuffer(buffer);
                MidiSystemExclusiveMessage midiMessageToSend = new MidiSystemExclusiveMessage(buffer);
                midiOutPort.SendMessage(midiMessageToSend);
            }
        }

        public void MidiInPort_MessageReceived(MidiInPort sender, MidiMessageReceivedEventArgs args)
        {
            //mainPage.MidiInPort_MessageReceived(sender, args);
            IMidiMessage receivedMidiMessage = args.Message;
            rawData = receivedMidiMessage.RawData.ToArray();
            MessageReceived = true;
        }

        public byte[] SystemExclusiveRQ1Message(byte Device, byte Company, byte Id, byte[] Address, byte[] Length)
        {
            byte[] result = null;
            switch (Device)
            {
                case 0:
                    // Roland INTEGRA-7
                    result = new byte[17];
                    result[0] = 0xf0; // Start of exclusive message
                    result[1] = 0x41; // Roland
                    result[2] = Id;   // Device Id is 17 according to settings in INTEGRA-7 (Menu -> System -> MIDI, 1 = 0x00 ... 17 = 0x10)
                    result[3] = 0x00;
                    result[4] = 0x00;
                    result[5] = 0x64; // INTEGRA-7
                    result[6] = 0x11; // Command (RQ1)
                    result[7] = Address[0];
                    result[8] = Address[1];
                    result[9] = Address[2];
                    result[10] = Address[3];
                    result[11] = Length[0];
                    result[12] = Length[1];
                    result[13] = Length[2];
                    result[14] = Length[3];
                    result[15] = 0x00; // Filled out by CheckSum but present here to avoid confusion about index 15 missing.
                    result[16] = 0xf7; // End of sysex
                    break;
                case 1:
                    // Roland GP-8:
                    result = new byte[11];
                    result[0] = 0xf0; // Start of exclusive message
                    result[1] = 0x41; // Roland
                    result[2] = Id;   // Device Id is 0 - 0f set in GP-8
                    result[3] = 0x13; // Model is GP-8 (0x13)
                    result[4] = 0x11; // Command (RQ1)
                    result[5] = Address[0];
                    result[6] = Address[1];
                    result[7] = Length[0];
                    result[8] = Length[1];
                    result[9] = 0x00; // Filled out by CheckSum but present here to avoid confusion about index 15 missing.
                    result[10] = 0xf7; // End of sysex
                    break;
                case 2:
                    // Roland R-8M: (not fixed! copy of case 0!)
                    result = new byte[17];
                    result[0] = 0xf0; // Start of exclusive message
                    result[1] = 0x41; // Roland
                    result[2] = Id;   // Device Id is 17 according to settings in INTEGRA-7 (Menu -> System -> MIDI, 1 = 0x00 ... 17 = 0x10)
                    result[3] = 0x00;
                    result[4] = 0x00;
                    result[5] = 0x64; // INTEGRA-7
                    result[6] = 0x11; // Command (RQ1)
                    result[7] = Address[0];
                    result[8] = Address[1];
                    result[9] = Address[2];
                    result[10] = Address[3];
                    result[11] = Length[0];
                    result[12] = Length[1];
                    result[13] = Length[2];
                    result[14] = Length[3];
                    result[15] = 0x00; // Filled out by CheckSum but present here to avoid confusion about index 15 missing.
                    result[16] = 0xf7; // End of sysex
                    break;
            }
            CheckSum(7, ref result);
            return (result);
        }

        public byte[] SystemExclusiveDT1Message(byte Device, byte Company, byte Id, byte[] Address, byte[] DataToTransmit)
        {
            byte[] result = null;
            Int32 length = 0;
            switch (Device)
            {
                case 0:
                    // Roland INTEGRA-7
                    length = 13 + DataToTransmit.Length;
                    result = new byte[length];
                    result[0] = 0xf0; // Start of exclusive message
                    result[1] = 0x41; // Roland
                    result[2] = 0x10; // Device Id is 17 according to settings in INTEGRA-7 (Menu -> System -> MIDI, 1 = 0x00 ... 17 = 0x10)
                    result[3] = 0x00;
                    result[4] = 0x00;
                    result[5] = 0x64; // INTEGRA-7
                    result[6] = 0x12; // Command (DT1)
                    result[7] = Address[0];
                    result[8] = Address[1];
                    result[9] = Address[2];
                    result[10] = Address[3];
                    for (Int32 i = 0; i < DataToTransmit.Length; i++)
                    {
                        result[i + 11] = DataToTransmit[i];
                    }
                    result[12 + DataToTransmit.Length] = 0xf7; // End of sysex
                    break;
                case 1:
                    // Roland GP-8:
                    length = 9 + DataToTransmit.Length;
                    result = new byte[length];
                    result[0] = 0xf0; // Start of exclusive message
                    result[1] = 0x41; // Roland
                    result[2] = Id;   // Device Id is 0 - 0f set in GP-8
                    result[3] = 0x13; // Model is GP-8 (0x13)
                    result[4] = 0x12; // Command (DT1)
                    result[5] = Address[0];
                    result[6] = Address[1];
                    for (Int32 i = 0; i < DataToTransmit.Length; i++)
                    {
                        result[i + 7] = DataToTransmit[i];
                    }
                    result[8 + DataToTransmit.Length] = 0xf7; // End of sysex
                    break;
            }
            CheckSum(5, ref result);
            return (result);
        }

        public void CheckSum(byte start, ref byte[] bytes)
        {
            byte chksum = 0;
            for (Int32 i = start; i < bytes.Length - 2; i++)
            {
                chksum += bytes[i];
            }
            bytes[bytes.Length - 2] = (byte)((0x80 - (chksum & 0x7f)) & 0x7f);
        }
    }
}
