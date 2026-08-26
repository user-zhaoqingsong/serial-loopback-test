using System;

namespace RsLoopTest
{
    internal sealed class LoopSnapshot
    {
        public bool IsRunning { get; set; }
        public LoopTestMode Mode { get; set; }
        public long ASent { get; set; }
        public long AReceivedOk { get; set; }
        public long AReceivedError { get; set; }
        public long BReceivedOk { get; set; }
        public long BReceivedError { get; set; }
        public long BSent { get; set; }
        public long TotalBytes { get; set; }
        public long CrcErrors { get; set; }
        public long HeaderErrors { get; set; }
        public long ResynchronizedBytes { get; set; }
        public long LostFrames { get; set; }
        public long DuplicateFrames { get; set; }
        public long OutOfOrderFrames { get; set; }
        public long ErrorBytes { get; set; }
        public long ErrorBits { get; set; }
        public int InFlightFrames { get; set; }
        public int WindowSize { get; set; }
        public TimeSpan Elapsed { get; set; }
        public double LastRoundTripMilliseconds { get; set; }
        public double AverageRoundTripMilliseconds { get; set; }
        public string StopReason { get; set; }
    }
}
