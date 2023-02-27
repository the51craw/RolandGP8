using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Midi;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using System.Threading;
using Windows.Storage;
using Windows.UI.Popups;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace RolandGP8
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        ApplicationDataContainer localSettings = null;
        String midiDevice;
        Int32 midiDeviceId;
        byte deviceNumber = 0x0f;
        MIDI midi = null;
        Boolean initDone = false;
        DispatcherTimer timer = null;
        byte delay = 0;
        Int32 requestSent = -1;

        // Effect switches are stored in low nibbles of address 0x00 an 0x01.
        // They are assembled into variable effect and stores nibble at address
        // 0x01 as low nibble and nibble at address 0x00 as high nibble.
        // That gives us the effects in correct order starting with dynamic 
        // filter at bit 0 (lsb) and chorus at bit 8 (msb).
        byte effects = 0x00;
        byte[] rawData = null;
        Boolean dataReceived = false;
        Boolean handleControlEvents = true;
        Boolean previousHandleControlEvents = true;
        Boolean midiInPortRegistered = false;

        ComboBox midiOutPortComboBox = new ComboBox();

        public MainPage()
        {
            this.InitializeComponent();
            Init();
            //if (midiOutPortComboBox.Items.Count > 0)
            //{
            //    midiOutPortComboBox.SelectedIndex = 0;
            //}
            delay = 20;
        }

        private async void Init()
        {
            //ListMidiDevices();
            List<String> devices = await MIDI.GetIODeviceNames();
            if (devices.Count() < 1)
            {
                MessageDialog warning = new MessageDialog("It seems like you do not have any MIDI device capable of both MIDI input and MIDI output.\r\n" + 
                    "Please make sure you have connected your GP-8, input and output, via a MIDI device.\r\n\r\nApplication will now close.");
                warning.Title = "Roland GP-8 editor by MrMartin";
                warning.Commands.Add(new UICommand { Label = "Close app", Id = 0 });
                await warning.ShowAsync();
                App.Current.Exit();
            }
            else
            {
                foreach (String name in devices)
                {
                    cbMidiDevice.Items.Add(name);
                }
                localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values.ContainsKey("DeviceNumber"))
                {
                    deviceNumber = ((byte)localSettings.Values["DeviceNumber"]);
                }
                cbDevice.SelectedIndex = deviceNumber;
                //localSettings.Values.Remove("MidiDevice");
                if (localSettings.Values.ContainsKey("MidiDevice"))
                {
                    midiDevice = ((String)localSettings.Values["MidiDevice"]);
                    if (cbMidiDevice.Items.Contains(midiDevice))
                    {
                        InitMidi(midiDevice);
                        cbMidiDevice.SelectedItem = midiDevice;
                        midiDeviceId = cbMidiDevice.SelectedIndex;
                    }
                    else
                    {
                        localSettings.DeleteContainer("MidiDevice");
                        midiDeviceId = 0;
                    }
                }
                if (midiDeviceId > -1 && cbMidiDevice.Items.Count() > 1)
                {
                    MessageDialog warning = new MessageDialog("Please select your MIDI device:");
                    warning.Title = "Roland GP-8";
                    Int32 i = 0;
                    foreach (String name in devices)
                    {
                        if (i < 3)
                        {
                            try
                            {
                                warning.Commands.Add(new UICommand { Label = name, Id = i++ });
                            }
                            catch { }
                        }
                    }
                    var response = await warning.ShowAsync();
                    midiDeviceId = (Int32)response.Id;
                    cbMidiDevice.SelectedIndex = midiDeviceId;
                    InitMidi(devices[(Int32)response.Id]);
                    localSettings.Values["MidiDevice"] = devices[(Int32)response.Id];
                }
                else if(midiDeviceId == 0 && cbMidiDevice.Items.Count == 1)
                {
                    cbMidiDevice.SelectedIndex = midiDeviceId;
                    InitMidi(devices[midiDeviceId]);
                    localSettings.Values["MidiDevice"] = devices[midiDeviceId];
                }
                timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(1);
                timer.Tick += Timer_Tick;
                timer.Start();
            }
        }

        private void InitMidi(String deviceName = null)
        {
            midi = new MIDI(this, midiOutPortComboBox, cbMidiDevice, Dispatcher, (byte)midiOutPortComboBox.SelectedIndex, (byte)cbMidiDevice.SelectedIndex, deviceName);
        }

        ////////////////////////////////////////////////////////////////////////////////////////////
        // Event handlers
        ////////////////////////////////////////////////////////////////////////////////////////////

        private async void Timer_Tick(object sender, object e)
        {
            if (!midiInPortRegistered)
            {
                if (midi.midiInPort != null)
                {
                    midi.midiInPort.MessageReceived += MidiInPort_MessageReceived;
                    midiInPortRegistered = true;
                }
            }
            else if (!initDone)
            {
                if (midiInPortRegistered)
                {
                    handleControlEvents = false;
                    cbGroupA.IsChecked = true;
                    cbGroupB.IsChecked = false;
                    cbBank.SelectedIndex = 0;
                    cbPatch.SelectedIndex = 0;
                    cbPatchBankAndGroup.SelectedIndex = 0;
                    cbMidiDevice.SelectedIndex = midiDeviceId;
                    handleControlEvents = true;
                    initDone = true;
                }
            }
            else
            {
                if (dataReceived)
                {
                    previousHandleControlEvents = handleControlEvents;
                    handleControlEvents = false;
                    UpdateControls();
                    handleControlEvents = previousHandleControlEvents;
                    dataReceived = false;
                }
            }
            if (delay > 0)
            {
                if (delay == 11)
                {
                    cbPatch.SelectedIndex = 0;
                    cbBank.SelectedIndex = 0;
                    cbGroupA.IsChecked = true;
                    cbGroupB.IsChecked = false;
                    cbPatchBankAndGroup.SelectedIndex = 0;
                    SetPatch();
                }
                if (delay == 1)
                {
                    ReadGP8();
                }
                delay--;
            }
            if (requestSent == 0)
            {
                requestSent = -1;
                MessageDialog warning = new MessageDialog("A request was sent to your GP-8, but no response was received!\r\n" +
                    "Please make sure you have connected your GP-8, input and output, via a MIDI device.\r\n" +
                    "Also verify that MIDI channel of the GP-8 is the same as Device number of the app (lower right corner)\r\n" +
                    "If not, either change MIDI on the GP-8 or Device number in the app.\r\n" +
                    " Then press \'Read\'.");
                warning.Title = "Roland GP-8 editor by MrMartin";
                warning.Commands.Add(new UICommand { Label = "Ok", Id = 0 });
                await warning.ShowAsync();
                //ReadGP8();
            }
            if (requestSent > 0)
            {
                requestSent--;
            }
        }

        public void MidiInPort_MessageReceived(Windows.Devices.Midi.MidiInPort sender, Windows.Devices.Midi.MidiMessageReceivedEventArgs args)
        {
            requestSent = -1;
            IMidiMessage receivedMidiMessage = args.Message;
            rawData = receivedMidiMessage.RawData.ToArray();
            dataReceived = true;
        }

        private void cbDynamicFilter_Click(object sender, RoutedEventArgs e)
        {
            if (handleControlEvents)
            {
                midi.SendSystemExclusive(EffectOnOff(cbDynamicFilter, 1));
            }
        }

        private void slDynamicFilterSens_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x02 };
                byte[] data = new byte[] { (byte)slDynamicFilterSens.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbDynamicFilterSens.Text = "Sens: " + slDynamicFilterSens.Value.ToString();
        }

        private void slDynamicFilterCutoffFreq_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x03 };
                byte[] data = new byte[] { (byte)slDynamicFilterCutoffFreq.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbDynamicFilterCutoffFreq.Text = "Cutoff freq: " + slDynamicFilterCutoffFreq.Value.ToString();
        }

        private void slDynamicFilterQ_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x04 };
                byte[] data = new byte[] { (byte)slDynamicFilterQ.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbDynamicFilterQ.Text = "Q: " + slDynamicFilterQ.Value.ToString();
        }

        private void cbDynamicFilterUp_Checked(object sender, RoutedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x05 };
                byte[] data = new byte[] { 0x64 };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
        }

        private void cbDynamicFilterDown_Checked(object sender, RoutedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x05 };
                byte[] data = new byte[] { 0x00 };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
        }

        private void cbCompressor_Click(object sender, RoutedEventArgs e)
        {
            if (handleControlEvents)
            {
                midi.SendSystemExclusive(EffectOnOff(cbCompressor, 2));
            }
        }

        private void slCompressorAttack_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x06 };
                byte[] data = new byte[] { (byte)slCompressorAttack.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbCompressorAttack.Text = "Attack: " + slCompressorAttack.Value.ToString();
        }

        private void slCompressorSustain_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x07 };
                byte[] data = new byte[] { (byte)slCompressorSustain.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbCompressorSustain.Text = "Sustain: " + slCompressorSustain.Value.ToString();
        }

        private void cbOverdrive_Click(object sender, RoutedEventArgs e)
        {
            if (handleControlEvents)
            {
                midi.SendSystemExclusive(EffectOnOff(cbOverdrive, 3));
            }
        }

        private void cbOverdriveTurbo_Click(object sender, RoutedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x0a };
                byte[] data = new byte[] { (byte)((Boolean)cbOverdriveTurbo.IsChecked ? 0x64 : 0x00) };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
        }

        private void slOverdriveTone_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x08 };
                byte[] data = new byte[] { (byte)slOverdriveTone.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbOverdriveTone.Text = "Tone: " + slOverdriveTone.Value.ToString();
        }

        private void slOverdriveDrive_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x9 };
                byte[] data = new byte[] { (byte)slOverdriveDrive.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbOverdriveDrive.Text = "Drive: " + slOverdriveDrive.Value.ToString();
        }

        private void cbDistortion_Click(object sender, RoutedEventArgs e)
        {
            if (handleControlEvents)
            {
                midi.SendSystemExclusive(EffectOnOff(cbDistortion, 4));
            }
        }

        private void slDistortionTone_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x0b };
                byte[] data = new byte[] { (byte)slDistortionTone.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbDistortionTone.Text = "Tone: " + slDistortionTone.Value.ToString();
        }

        private void slDistortionDist_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x0c };
                byte[] data = new byte[] { (byte)slDistortionDist.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbDistortionDist.Text = "Distortion: " + slDistortionDist.Value.ToString();
        }

        private void cbPhaser_Click(object sender, RoutedEventArgs e)
        {
            if (handleControlEvents)
            {
                midi.SendSystemExclusive(EffectOnOff(cbPhaser, 5));
            }
        }

        private void slPhaserRate_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x0d };
                byte[] data = new byte[] { (byte)slPhaserRate.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbPhaserRate.Text = "Rate: " + slPhaserRate.Value.ToString();
        }

        private void slPhaserDepth_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x0e };
                byte[] data = new byte[] { (byte)slPhaserDepth.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbPhaserDepth.Text = "Depth: " + slPhaserDepth.Value.ToString();
        }

        private void slPhaserResonance_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x0f };
                byte[] data = new byte[] { (byte)slPhaserResonance.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbPhaserResonance.Text = "Resonance: " + slPhaserResonance.Value.ToString();
        }

        private void cbEqualizer_Click(object sender, RoutedEventArgs e)
        {
            if (handleControlEvents)
            {
                midi.SendSystemExclusive(EffectOnOff(cbEqualizer, 6));
            }
        }

        private void slEqualizerHiLevel_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x10 };
                byte[] data = new byte[] { (byte)slEqualizerHiLevel.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbEqualizerHiLevel.Text = "Hi level: " + (slEqualizerHiLevel.Value - 50).ToString();
        }

        private void slEqualizerMidLevel_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x11 };
                byte[] data = new byte[] { (byte)slEqualizerMidLevel.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbEqualizerMidLevel.Text = "Mid level: " + (slEqualizerMidLevel.Value - 50).ToString();
        }

        private void slEqualizerLowLevel_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x12 };
                byte[] data = new byte[] { (byte)slEqualizerLowLevel.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbEqualizerLowLevel.Text = "Low level: " + (slEqualizerLowLevel.Value - 50).ToString();
        }

        private void slEqualizerOutLevel_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x13 };
                byte[] data = new byte[] { (byte)slEqualizerOutLevel.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbEqualizerOutLevel.Text = "Out level: " + slEqualizerOutLevel.Value.ToString();
        }

        private void cbDigitalDelay_Click(object sender, RoutedEventArgs e)
        {
            if (handleControlEvents)
            {
                midi.SendSystemExclusive(EffectOnOff(cbDigitalDelay, 7));
            }
        }

        private void slDigitalDelayELevel_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x14 };
                byte[] data = new byte[] { (byte)slDigitalDelayELevel.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbDigitalDelayELevel.Text = "Effect level: " + slDigitalDelayELevel.Value.ToString();
        }

        private void slDigitalDelayFeedback_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x17 };
                byte[] data = new byte[] { (byte)slDigitalDelayFeedback.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbDigitalDelayFeedback.Text = "Feedback: " + slDigitalDelayFeedback.Value.ToString();
        }

        private void slDigitalDelayDTime_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x15 };
                byte[] data = new byte[] { (byte)(slDigitalDelayDTime.Value / 0x80) };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);

                address = new byte[] { 0x00, 0x16 };
                data = new byte[] { (byte)(slDigitalDelayDTime.Value % 0x80) };
                message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbDigitalDelayDTime.Text = "Delay time: " + slDigitalDelayDTime.Value.ToString() + "ms";
        }

        private void cbDigitalChorus_Click(object sender, RoutedEventArgs e)
        {
            if (handleControlEvents)
            {
                midi.SendSystemExclusive(EffectOnOff(cbDigitalChorus, 8));
            }
        }

        private void slDigitalChorusRate_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x18 };
                byte[] data = new byte[] { (byte)slDigitalChorusRate.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbDigitalChorusRate.Text = "Rate: " + slDigitalChorusRate.Value.ToString();
        }

        private void slDigitalChorusDepth_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x19 };
                byte[] data = new byte[] { (byte)slDigitalChorusDepth.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbDigitalChorusDepth.Text = "Depth: " + slDigitalChorusDepth.Value.ToString();
        }

        private void slDigitalChorusELevel_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x1a };
                byte[] data = new byte[] { (byte)slDigitalChorusELevel.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbDigitalChorusELevel.Text = "Effect level: " + slDigitalChorusELevel.Value.ToString();
        }

        private void slDigitalChorusPreDelay_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x1b };
                byte[] data = new byte[] { (byte)slDigitalChorusPreDelay.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbDigitalChorusPreDelay.Text = "Pre delay: " + slDigitalChorusPreDelay.Value.ToString();
        }

        private void slDigitalChorusFeedback_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x1c };
                byte[] data = new byte[] { (byte)slDigitalChorusFeedback.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbDigitalChorusFeedback.Text = "Feedback: " + slDigitalChorusFeedback.Value.ToString();
        }

        private void slCommonMasterVolume_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x1d };
                byte[] data = new byte[] { (byte)slCommonMasterVolume.Value };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
            tbCommonMasterVolume.Text = "Master volume: " + slCommonMasterVolume.Value.ToString();
        }

        private void cbCommonEV5Parameter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x1e };
                byte[] data = new byte[] { (byte)cbCommonEV5Parameter.SelectedIndex };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
        }

        private void btnRead_Click(object sender, RoutedEventArgs e)
        {
            ReadGP8();
        }

        private void cbCommonExtControlOut1_Click(object sender, RoutedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x1f };
                byte[] data = new byte[] { (byte)((Boolean)cbCommonExtControlOut1.IsChecked ? 0x64 : 0x00) };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
        }

        private void cbCommonExtControlOut2_Click(object sender, RoutedEventArgs e)
        {
            if (handleControlEvents)
            {
                byte[] address = new byte[] { 0x00, 0x20 };
                byte[] data = new byte[] { (byte)((Boolean)cbCommonExtControlOut2.IsChecked ? 0x64 : 0x00) };
                byte[] message = midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
                midi.SendSystemExclusive(message);
            }
        }

        private void cbGroupA_Checked(object sender, RoutedEventArgs e)
        {
            if (handleControlEvents)
            {
                SetPatchBankAndGroupSelector();
                SetPatch();
            }
        }

        private void cbGroupB_Checked(object sender, RoutedEventArgs e)
        {
            if (handleControlEvents)
            {
                SetPatchBankAndGroupSelector();
                SetPatch();
            }
        }

        private void cbBank_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                SetPatchBankAndGroupSelector();
                SetPatch();
            }
        }

        private void cbPatch_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                SetPatchBankAndGroupSelector();
                SetPatch();
            }
        }

        private void cbPatchBankAndGroup_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (handleControlEvents)
            {
                Int32 group = cbPatchBankAndGroup.SelectedIndex / 64;
                Int32 bank = (cbPatchBankAndGroup.SelectedIndex % 64) / 8;
                Int32 patch = (cbPatchBankAndGroup.SelectedIndex % 64) % 8;
                previousHandleControlEvents = handleControlEvents;
                handleControlEvents = false;
                cbGroupA.IsChecked = group == 0;
                cbGroupB.IsChecked = group == 1;
                cbBank.SelectedIndex = bank;
                handleControlEvents = previousHandleControlEvents;
                cbPatch.SelectedIndex = patch;
            }
        }

        private void cbDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            deviceNumber = (byte)cbDevice.SelectedIndex;
            localSettings.Values["DeviceNumber"] = deviceNumber;
        }

        private void cbMidiDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (initDone && cbMidiDevice.SelectedIndex > -1 && cbMidiDevice.SelectedIndex < cbMidiDevice.Items.Count())
            {
                midiDevice = (String)cbMidiDevice.Items[cbMidiDevice.SelectedIndex];
                localSettings.Values["MidiDevice"] = MIDI.TrimDeviceName(midiDevice);
                midi.InputDeviceChanged(cbMidiDevice);
                //InitMidi(midiDevice);
            }
        }

        private void Grid_KeyUp(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.F1)
            {
                ShowHelp();
            }
        }

        private async void ShowHelp()
        {
            MessageDialog warning = new MessageDialog("Assuming you have read your GP-8 manual, you do not need much more help since all is pretty obvious.\r\n" +
                "All eight processing devices in your GP-8 corresponds to a row in the app window, with the on/off switch in the leftmost column.\r\n" +
                "The rows differ in height because they contain different number of parameters.\r\n" +
                "Last row contains patch switch controls and MIDI settings. There is one dropbox to select group, bank and patch together, and three dropboxes to set group, bank and patch separately.\r\n" +
                "To the right you have the MIDI settings.\r\n" +
                "There is no save button because the GP-8 does not accept a save command over MIDI. Instead, you have to do like this, if you want to save:\r\n" +
                "1) Before you start editing, use the app to select the patch you want to edit.\r\n" +
                "2) Press the \'Edit\' button on your GP-8 to enter edit mode.\r\n" +
                "3) Use the app to edit the patch to your liking.\r\n" +
                "4) Press the \'Save\' button on your GP-8 to save the new settings.\r\n" +
                "Also note that the GP-8 does not reveal its patch selection, so the app resets your GP-8 to Group A, Bank 1, Patch 1 at startup.");
            warning.Title = "Help for the Roland GP-8 app";
            await warning.ShowAsync();
        }

        ////////////////////////////////////////////////////////////////////////////////////////////
        // Helpers
        ////////////////////////////////////////////////////////////////////////////////////////////

        private byte[] EffectOnOff(CheckBox cb, Int32 effect)
        {
            byte[] address;
            byte[] data;

            if ((Boolean)cb.IsChecked)
            {
                effects |= (byte)(0x01 << effect - 1);
            }
            else
            {
                effects &= (byte)(0xfe << effect - 1);
            }
            if (effect < 5)
            {
                address = new byte[] { 0x00, 0x01 };
                data = new byte[] { (byte)(effects & 0x0f) };
            }
            else
            {
                address = new byte[] { 0x00, 0x00 };
                data = new byte[] { (byte)((effects >> 4) & 0x0f) };
            }
            return midi.SystemExclusiveDT1Message(0x01, 0x41, deviceNumber, address, data);
        }

        private void ReadGP8()
        {
            byte[] address = new byte[] { 0x00, 0x00 };
            byte[] length = new byte[] { 0x00, 0x01 };
            byte[] message = midi.SystemExclusiveRQ1Message(0x01, 0x41, deviceNumber, address, length);
            midi.SendSystemExclusive(message);
            requestSent = 20;
        }

        private void UpdateControls()
        {
            byte addressOffset = 7;
            try
            {
                effects = (byte)(((rawData[addressOffset] << 4) & 0xf0) | (rawData[addressOffset + 0x01] & 0x0f));
                cbDynamicFilter.IsChecked = (effects & 0x01) == 0x01;
                cbCompressor.IsChecked = (effects & 0x02) == 0x02;
                cbOverdrive.IsChecked = (effects & 0x04) == 0x04;
                cbDistortion.IsChecked = (effects & 0x08) == 0x08;
                cbPhaser.IsChecked = (effects & 0x10) == 0x10;
                cbEqualizer.IsChecked = (effects & 0x20) == 0x20;
                cbDigitalDelay.IsChecked = (effects & 0x40) == 0x40;
                cbDigitalChorus.IsChecked = (effects & 0x80) == 0x80;
                slDynamicFilterSens.Value = rawData[addressOffset + 0x02];
                slDynamicFilterCutoffFreq.Value = rawData[addressOffset + 0x03];
                slDynamicFilterQ.Value = rawData[addressOffset + 0x04];
                cbDynamicFilterUp.IsChecked = rawData[addressOffset + 0x05] == 0x64;
                cbDynamicFilterDown.IsChecked = rawData[addressOffset + 0x05] == 0x00;
                slCompressorAttack.Value = rawData[addressOffset + 0x06];
                slCompressorSustain.Value = rawData[addressOffset + 0x07];
                cbOverdriveTurbo.IsChecked = rawData[addressOffset + 0x0a] == 0x64;
                slOverdriveTone.Value = rawData[addressOffset + 0x08];
                slOverdriveDrive.Value = rawData[addressOffset + 0x09];
                slDistortionTone.Value = rawData[addressOffset + 0x0b];
                slDistortionDist.Value = rawData[addressOffset + 0x0c];
                slPhaserRate.Value = rawData[addressOffset + 0x0d];
                slPhaserDepth.Value = rawData[addressOffset + 0x0e];
                slPhaserResonance.Value = rawData[addressOffset + 0x0f];
                slEqualizerHiLevel.Value = rawData[addressOffset + 0x10];
                slEqualizerMidLevel.Value = rawData[addressOffset + 0x11];
                slEqualizerLowLevel.Value = rawData[addressOffset + 0x12];
                slEqualizerOutLevel.Value = rawData[addressOffset + 0x13];
                slDigitalDelayELevel.Value = rawData[addressOffset + 0x14];
                slDigitalDelayFeedback.Value = rawData[addressOffset + 0x17];
                slDigitalDelayDTime.Value = rawData[addressOffset + 0x15] / 0x80 + rawData[addressOffset + 0x16] % 0x80;
                slDigitalChorusRate.Value = rawData[addressOffset + 0x18];
                slDigitalChorusDepth.Value = rawData[addressOffset + 0x19];
                slDigitalChorusELevel.Value = rawData[addressOffset + 0x1a];
                slDigitalChorusPreDelay.Value = rawData[addressOffset + 0x1b];
                slDigitalChorusFeedback.Value = rawData[addressOffset + 0x1c];
                slCommonMasterVolume.Value = rawData[addressOffset + 0x1d];
                cbCommonEV5Parameter.SelectedIndex = rawData[addressOffset + 0x1e];
                cbCommonExtControlOut1.IsChecked = rawData[addressOffset + 0x1f] == 0x64;
                cbCommonExtControlOut2.IsChecked = rawData[addressOffset + 0x20] == 0x64;

                String name = "";
                for (Int32 i = 40; i < 57; i++)
                {
                    name += (char)rawData[i];
                }
                tbName.Text = name;
            }
            catch { }
        }

        private void SetPatch()
        {
            byte patch = (byte)(
                cbPatch.SelectedIndex +
                cbBank.SelectedIndex * 8 +
                ((Boolean)cbGroupB.IsChecked ? 64 : 0));
            midi.ProgramChange(deviceNumber, 0xff, 0xff, (byte)(patch + 1));
            delay = 10;
        }

        private void SetPatchBankAndGroupSelector()
        {
            Int32 group = (Boolean)cbGroupA.IsChecked ? 0: 64;
            Int32 bank = cbBank.SelectedIndex;
            Int32 patch = cbPatch.SelectedIndex;
            previousHandleControlEvents = handleControlEvents;
            handleControlEvents = false;
            cbPatchBankAndGroup.SelectedIndex = group + bank * 8 + patch;
            handleControlEvents = previousHandleControlEvents;
        }
    }
}
