﻿using System;
using System.Collections.Generic;
using System.Text;
using DAQ.Environment;
using DAQ.HAL;
using ScanMaster.Acquire.Plugin;
using System.Xml.Serialization;

namespace ScanMaster.Acquire.Plugins
{
    /// <summary>
    /// A switch output plugin that switches the rf frequency between two values. On the off shots, the frequency is set to the "start" value.
    /// </summary>
    [Serializable]
    public class SynthFrequencySwitchPlugin : SwitchOutputPlugin
    {
        [NonSerialized]
        private bool state;

        [NonSerialized]
        Synth synth;

        protected override void InitialiseSettings()
        {
        }

        public override void AcquisitionStarting()
        {
            synth = (Synth)Environs.Hardware.GPIBInstruments[(string)Config.outputPlugin.Settings["synth"]];
        }

        public override void ScanStarting()
        {
        }

        public override void ScanFinished()
        {
        }

        public override void AcquisitionFinished()
        {
        }

        [XmlIgnore]
        public override bool State
        {
            set
            {
                state = value;
                if (state)
                {
                    
                }
                else
                {
                    synth.Frequency = (double)Config.outputPlugin.Settings["start"];
                }

            }
            get { return state; }
        }
    }
}
