using System;
using System.Threading;
using System.Xml.Serialization;

using NationalInstruments.DAQmx;

using DAQ.Environment;
using DAQ.HAL;
using ScanMaster.Acquire.Plugin;

namespace ScanMaster.Acquire.Plugins
{
	/// <summary>
	/// A plugin to scan an analog output on an E-series board.
	/// </summary>
	[Serializable]
	public class DAQMxAnalogOutputPlugin : ScanOutputPlugin
	{
		
		[NonSerialized]
		private double scanParameter = 0;

		[NonSerialized]
		private Task outputTask;
		[NonSerialized]
		private AnalogSingleChannelWriter writer;

		protected override void InitialiseSettings()
		{
			settings["channel"] = "laser";
			settings["rampToZero"] = true;
            settings["flyback"] = "normal";
			settings["rampSteps"] = 50;
			settings["rampDelay"] = 20;
            settings["overshootVoltage"] = 0.0;
           
		}


		public override void AcquisitionStarting()
		{
			// initialise the output hardware
			outputTask = new Task("analog output");
			if (!Environs.Debug)
			{
				AnalogOutputChannel oc = 
					(AnalogOutputChannel) Environs.Hardware.AnalogOutputChannels[(string)settings["channel"]];
				oc.AddToTask(outputTask, oc.RangeLow, oc.RangeHigh);
				writer = new AnalogSingleChannelWriter(outputTask.Stream);
				writer.WriteSingleSample(true, 0);
			}
			scanParameter = 0;
			//go gently to the correct start position
            if ((string)settings["scanMode"] == "up" || (string)settings["scanMode"] == "updown")
            {
                if ((string)settings["flyback"] == "overshoot") rampOutputToVoltage((double)settings["overshootVoltage"]);
                rampOutputToVoltage((double)settings["start"]);                
            }
            if ((string)settings["scanMode"] == "down" || (string)settings["scanMode"] == "downup")
            {
                if ((string)settings["flyback"] == "overshoot") rampOutputToVoltage((double)settings["overshootVoltage"]);
                rampOutputToVoltage((double)settings["end"]);
            }
		}

		public override void ScanStarting()
		{
			//do nothing
		}

		public override void ScanFinished()
		{
            if ((string)settings["scanMode"] == "up")
            {
                if ((string)settings["flyback"] == "overshoot") rampOutputToVoltage((double)settings["overshootVoltage"]);
                rampOutputToVoltage((double)settings["start"]);
            }
            if ((string)settings["scanMode"] == "down")
            {
                if ((string)settings["flyback"] == "overshoot") rampOutputToVoltage((double)settings["overshootVoltage"]);
                rampOutputToVoltage((double)settings["end"]);
            }
			// all other cases, do nothing
		}

		public override void AcquisitionFinished()
		{
			rampOutputToVoltage(0);
			outputTask.Dispose();
		}
		
		[XmlIgnore]
		public override double ScanParameter
		{
			set
			{
                scanParameter = value;
                if (!Environs.Debug) writer.WriteSingleSample(true, value);
			}
			get { return scanParameter; }
		}

		private void rampOutputToVoltage(double voltage)
		{
			for (int i = 1 ; i <= (int)settings["rampSteps"] ; i++ ) 
			{
				if (!Environs.Debug) writer.WriteSingleSample(true,
										 scanParameter - (i*(scanParameter - voltage) / (int)settings["rampSteps"]));
				Thread.Sleep( (int)settings["rampDelay"] );
			}
			scanParameter = voltage;
		}

        
		
	}
}
