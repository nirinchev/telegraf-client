using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace StatsdClient
{
    public interface IAllowsSampleRate { }
    

    
    public interface IAllowsInteger { }
    public interface IAllowsString { }

    public class Statsd : IStatsd
    {
        private readonly object _commandCollectionLock = new object();

        private StopwatchFactory StopwatchFactory { get; set; }
        private IStatsdUDP Udp { get; set; }
        private SamplerFunc SamplerFunc { get; set; }

        private readonly string _prefix;

        public List<string> Commands { get; private set; }

        public class Counting : IAllowsSampleRate, IAllowsInteger { }
        public class Timing : IAllowsSampleRate, IAllowsInteger { }
        
        public class Histogram : IAllowsInteger { }
        public class Meter : IAllowsInteger { }
        public class Set : IAllowsString { }

        private readonly Dictionary<Type, string> _commandToUnit = new Dictionary<Type, string>
                                                                       {
                                                                           {typeof (Counting), "c"},
                                                                           {typeof (Timing), "ms"},
                                                                           
                                                                           {typeof (Histogram), "h"},
                                                                           {typeof (Meter), "m"},
                                                                           {typeof (Set), "s"}
                                                                       };

        public Statsd(IStatsdUDP udp, SamplerFunc samplerFunc, StopwatchFactory stopwatchFactory, string prefix)
        {
            Commands = new List<string>();
            StopwatchFactory = stopwatchFactory;
            Udp = udp;
            SamplerFunc = samplerFunc;
            _prefix = prefix;
        }

        public Statsd(IStatsdUDP udp, SamplerFunc samplerFunc, StopwatchFactory stopwatchFactory)
            : this(udp, samplerFunc, stopwatchFactory, string.Empty) { }



		public Statsd(IStatsdUDP udp, SamplerFunc samplerFunc)
			: this(udp, samplerFunc, () =>
			{
				var watch = Stopwatch.StartNew();
				return (() => (int)watch.ElapsedMilliseconds);
			}) { }


        public Statsd(IStatsdUDP udp, string prefix)
            : this(udp, SamplerDefault.ShouldSend, () =>
            {
	            var watch = Stopwatch.StartNew();
	            return (() => (int)watch.ElapsedMilliseconds);
            }, prefix) { }

        public Statsd(IStatsdUDP udp)
            : this(udp, "") { }


        public void Send<TCommandType>(string name, int value) where TCommandType : IAllowsInteger
        {
            Commands = new List<string> { GetCommand(name, value.ToString(CultureInfo.InvariantCulture), _commandToUnit[typeof(TCommandType)], 1) };
            Send();
        }
        public void SendGauge(string name, double value) 
        {
            Commands = new List<string> { GetCommand(name, String.Format(CultureInfo.InvariantCulture,"{0:F15}", value), "g", 1) };
            Send();
        }

        public void SendGauge(string name, double value, bool isDeltaValue) 
        {
          if (isDeltaValue)
          {
              // Sending delta values to StatsD requires a value modifier sign (+ or -) which we append 
              // using this custom format with a different formatting rule for negative/positive and zero values
              // https://msdn.microsoft.com/en-us/library/0c899ak8.aspx#SectionSeparator
              const string deltaValueStringFormat = "{0:+#.###;-#.###;+0}";
              Commands = new List<string> {
                GetCommand(name, string.Format(CultureInfo.InvariantCulture, 
                deltaValueStringFormat, 
                value), 
                 "g", 1)
              };
              Send();
          }
          else
          {
              SendGauge(name, value);
          }
        }

        public void SendSet(string name, string value) 
        {
            Commands = new List<string> { GetCommand(name, value.ToString(CultureInfo.InvariantCulture), "s", 1) };
            Send();
        }

        public void Add<TCommandType>(string name, int value) where TCommandType : IAllowsInteger
        {
            ThreadSafeAddCommand(GetCommand(name, value.ToString(CultureInfo.InvariantCulture), _commandToUnit[typeof (TCommandType)], 1));
        }

        public void AddGauge(string name, double value) 
        {
            ThreadSafeAddCommand(GetCommand(name, String.Format(CultureInfo.InvariantCulture,"{0:F15}", value), "g", 1));
        }

        public void Send<TCommandType>(string name, int value, double sampleRate) where TCommandType : IAllowsInteger, IAllowsSampleRate
        {
            if (SamplerFunc(sampleRate))
            {
                Commands = new List<string> { GetCommand(name, value.ToString(CultureInfo.InvariantCulture), _commandToUnit[typeof(TCommandType)], sampleRate) };
                Send();
            }
        }

        public void Add<TCommandType>(string name, int value, double sampleRate) where TCommandType : IAllowsInteger, IAllowsSampleRate
        {
            if (SamplerFunc(sampleRate))
            {
                Commands.Add(GetCommand(name, value.ToString(CultureInfo.InvariantCulture), _commandToUnit[typeof(TCommandType)], sampleRate));
            }
        }

        private void ThreadSafeAddCommand(string command)
        {
            lock (_commandCollectionLock)
            {
                Commands.Add(command);
            }
        }

        public void Send()
        {
            try
            {
                Udp.Send(string.Join("\n", Commands.ToArray()));
                Commands = new List<string>();
            }
            catch(Exception e)
            {
                Debug.WriteLine(e.Message);
            }
        }

        private string GetCommand(string name, string value, string unit, double sampleRate)
        {
            var format = sampleRate.Equals(1) ? "{0}:{1}|{2}" : "{0}:{1}|{2}|@{3}";
            return string.Format(CultureInfo.InvariantCulture, format, _prefix + name, value, unit, sampleRate);
        }

        public void Add(Action actionToTime, string statName, double sampleRate=1)
        {
			if (!SamplerFunc(sampleRate))
			{
				actionToTime();
				return;
			}
            var stopwatch = StopwatchFactory();

            try
            {
                actionToTime();
            }
            finally
            {
				
				Add<Timing>(statName, stopwatch());
                
            }
        }

        public void Send(Action actionToTime, string statName, double sampleRate=1)
        {
			if (!SamplerFunc(sampleRate))
			{
				actionToTime();
				return;
			}
            var stopwatch = StopwatchFactory();

            try
            {
                
                actionToTime();
            }
            finally
            {
				Send<Timing>(statName, stopwatch());
            }
        }
    }
}
