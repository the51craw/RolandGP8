//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************

//using SDKTemplate;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Midi;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace RolandGP8
{
    /// <summary>
    /// DeviceWatcher class to monitor adding/removing MIDI devices on the fly
    /// </summary>
    public class MidiDeviceWatcher
    {
        internal DeviceWatcher deviceWatcher = null;
        //internal DeviceInformationCollection deviceInformationCollection = null;
        bool enumerationCompleted = false;
        ComboBox portList = null;
        string midiSelector = string.Empty;
        CoreDispatcher coreDispatcher = null;
        public DeviceInformationCollection DeviceInformationCollection { get; set; }

        /// <summary>
        /// Constructor: Initialize and hook up Device Watcher events
        /// </summary>
        /// <param name="midiSelectorString">MIDI Device Selector</param>
        /// <param name="dispatcher">CoreDispatcher instance, to update UI thread</param>
        /// <param name="portListBox">The UI element to update with list of devices</param>
        internal MidiDeviceWatcher(string midiSelectorString, ComboBox portListBox, CoreDispatcher dispatcher)
        {
            this.deviceWatcher = DeviceInformation.CreateWatcher(midiSelectorString);
            this.portList = portListBox;
            this.midiSelector = midiSelectorString;
            this.coreDispatcher = dispatcher;

            this.deviceWatcher.Added += DeviceWatcher_Added;
            this.deviceWatcher.Removed += DeviceWatcher_Removed;
            this.deviceWatcher.Updated += DeviceWatcher_Updated;
            this.deviceWatcher.EnumerationCompleted += DeviceWatcher_EnumerationCompleted;
        }

        /// <summary>
        /// Destructor: Remove Device Watcher events
        /// </summary>
        ~MidiDeviceWatcher()
        {
            this.deviceWatcher.Added -= DeviceWatcher_Added;
            this.deviceWatcher.Removed -= DeviceWatcher_Removed;
            this.deviceWatcher.Updated -= DeviceWatcher_Updated;
            this.deviceWatcher.EnumerationCompleted -= DeviceWatcher_EnumerationCompleted;
        }

        /// <summary>
        /// Start the Device Watcher
        /// </summary>
        internal void StartWatcher()
        {
            if (this.deviceWatcher.Status != DeviceWatcherStatus.Started)
            {
                this.deviceWatcher.Start();
            }
        }

        /// <summary>
        /// Stop the Device Watcher
        /// </summary>
        internal void StopWatcher()
        {
            if (this.deviceWatcher.Status != DeviceWatcherStatus.Stopped)
            {
                this.deviceWatcher.Stop();
            }
        }

        /// <summary>
        /// Get the DeviceInformationCollection
        /// </summary>
        /// <returns></returns>
        internal DeviceInformationCollection GetDeviceInformationCollection()
        {
            return this.DeviceInformationCollection;
        }

        /// <summary>
        /// Add any connected MIDI devices to the list
        /// </summary>
        private async void UpdateComboBox()
        {
            // Get a list of all MIDI devices
            this.DeviceInformationCollection = await DeviceInformation.FindAllAsync(this.midiSelector);

            // If no devices are found, update the ListBox
            if ((this.DeviceInformationCollection == null) || (this.DeviceInformationCollection.Count == 0))
            {
                // Start with a clean list
                this.portList.Items.Clear();

                this.portList.Items.Add("No MIDI ports found");
                this.portList.IsEnabled = false;
            }
            // If devices are found, enumerate them and add them to the list
            else
            {
                // Start with a clean list
                this.portList.Items.Clear();

                foreach (var device in DeviceInformationCollection)
                {
                    this.portList.Items.Add(device.Name);
                }

                for (Int32 i = 0; i < portList.Items.Count; i++)
                {
                    if (((String)portList.Items[i]).Contains("INTEGRA-7")
                        && !((String)portList.Items[i]).Contains("CTRL")
                    //if (((String)portList.Items[i]).Contains("MIDI")
                    )
                    {
                        portList.SelectedIndex = i;
                    }
                }

                this.portList.IsEnabled = true;
            }
        }

        /// <summary>
        /// Update UI on device added
        /// </summary>
        /// <param name="sender">The active DeviceWatcher instance</param>
        /// <param name="args">Event arguments</param>
        private async void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation args)
        {
            // If all devices have been enumerated
            if (this.enumerationCompleted)
            {
                await coreDispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    // Update the device list
                    UpdateComboBox();
                });
            }
        }

        /// <summary>
        /// Update UI on device removed
        /// </summary>
        /// <param name="sender">The active DeviceWatcher instance</param>
        /// <param name="args">Event arguments</param>
        private async void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            // If all devices have been enumerated
            if (this.enumerationCompleted)
            {
                await coreDispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    // Update the device list
                    UpdateComboBox();
                });
            }
        }

        /// <summary>
        /// Update UI on device updated
        /// </summary>
        /// <param name="sender">The active DeviceWatcher instance</param>
        /// <param name="args">Event arguments</param>
        private async void DeviceWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            // If all devices have been enumerated
            if (this.enumerationCompleted)
            {
                await coreDispatcher.RunAsync(CoreDispatcherPriority.High, () =>
                {
                    // Update the device list
                    UpdateComboBox();
                });
            }
        }

        /// <summary>
        /// Update UI on device enumeration completed.
        /// </summary>
        /// <param name="sender">The active DeviceWatcher instance</param>
        /// <param name="args">Event arguments</param>
        private async void DeviceWatcher_EnumerationCompleted(DeviceWatcher sender, object args)
        {
            this.enumerationCompleted = true;
            await coreDispatcher.RunAsync(CoreDispatcherPriority.High, () =>
            {
                // Update the device list
                UpdateComboBox();
            });
        }
    }
}
