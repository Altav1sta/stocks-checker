using System;

namespace WebApp.Models
{
    public class SendingSignalEventArgs : EventArgs
    {
        public string Ticker { get; set; }

        public double Value { get; set; }
    }
}
