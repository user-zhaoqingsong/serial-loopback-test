using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using RsLoopTest;

internal static class SerialHardwareProbe
{
    private static int Main(string[] args)
    {
        if (args.Length < 5)
        {
            Console.Error.WriteLine("Usage: probe PORT_A PORT_B SECONDS PAYLOAD_LENGTH RATE[,RATE...]");
            return 2;
        }

        string portA = args[0];
        string portB = args[1];
        int seconds = int.Parse(args[2], CultureInfo.InvariantCulture);
        int payloadLength = int.Parse(args[3], CultureInfo.InvariantCulture);
        int requestedWindow = args.Length > 5
            ? int.Parse(args[5], CultureInfo.InvariantCulture) : 0;
        string[] rateValues = args[4].Split(',');
        Console.WriteLine("baud,payload,seconds,window,sent,ok,a_error,b_error,lost,duplicate,out_of_order,crc,error_bytes,error_bits,avg_rtt_ms,max_sampled_rtt_ms,in_flight");

        foreach (string rateValue in rateValues)
        {
            int rate = int.Parse(rateValue, CultureInfo.InvariantCulture);
            SerialLoopController controller = new SerialLoopController();
            List<string> errors = new List<string>();
            controller.LogAvailable += delegate(string message, bool isError)
            {
                if (isError && errors.Count < 5) errors.Add(message);
            };
            controller.TestStopped += delegate(string reason)
            {
                if (errors.Count < 5) errors.Add("STOP: " + reason);
            };

            try
            {
                TransportSettings endpointA = TransportSettings.Parse(
                    TransportKind.Serial, portA, string.Empty);
                TransportSettings endpointB = TransportSettings.Parse(
                    TransportKind.Serial, portB, string.Empty);
                LoopDataOptions options = new LoopDataOptions
                {
                    Pattern = PayloadPattern.Prbs31,
                    FrameLength = payloadLength,
                    DataSeed = 0x12345678u,
                    InFlightWindow = requestedWindow
                };

                controller.Start(endpointA, endpointB, rate, options, 100,
                    LoopTestMode.DualPortRelay);
                DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
                double maximumSampledRtt = 0.0;
                while (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(20);
                    LoopSnapshot sample = controller.GetSnapshot();
                    maximumSampledRtt = Math.Max(maximumSampledRtt,
                        sample.LastRoundTripMilliseconds);
                    if (!sample.IsRunning) break;
                }

                LoopSnapshot snapshot = controller.GetSnapshot();
                Console.WriteLine(string.Join(",", new string[]
                {
                    rate.ToString(CultureInfo.InvariantCulture),
                    payloadLength.ToString(CultureInfo.InvariantCulture),
                    seconds.ToString(CultureInfo.InvariantCulture),
                    snapshot.WindowSize.ToString(CultureInfo.InvariantCulture),
                    snapshot.ASent.ToString(CultureInfo.InvariantCulture),
                    snapshot.AReceivedOk.ToString(CultureInfo.InvariantCulture),
                    snapshot.AReceivedError.ToString(CultureInfo.InvariantCulture),
                    snapshot.BReceivedError.ToString(CultureInfo.InvariantCulture),
                    snapshot.LostFrames.ToString(CultureInfo.InvariantCulture),
                    snapshot.DuplicateFrames.ToString(CultureInfo.InvariantCulture),
                    snapshot.OutOfOrderFrames.ToString(CultureInfo.InvariantCulture),
                    snapshot.CrcErrors.ToString(CultureInfo.InvariantCulture),
                    snapshot.ErrorBytes.ToString(CultureInfo.InvariantCulture),
                    snapshot.ErrorBits.ToString(CultureInfo.InvariantCulture),
                    snapshot.AverageRoundTripMilliseconds.ToString("0.000", CultureInfo.InvariantCulture),
                    maximumSampledRtt.ToString("0.000", CultureInfo.InvariantCulture),
                    snapshot.InFlightFrames.ToString(CultureInfo.InvariantCulture)
                }));
                foreach (string error in errors)
                    Console.Error.WriteLine(rate + ": " + error);
                if (!snapshot.IsRunning) Thread.Sleep(300);
                foreach (string error in errors)
                {
                    if (error.StartsWith("STOP:", StringComparison.Ordinal))
                        Console.Error.WriteLine(rate + ": " + error);
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(rate + ",ERROR," + exception.Message.Replace(',', ';'));
            }
            finally
            {
                controller.Stop("用户停止");
                controller.Dispose();
            }
            Thread.Sleep(200);
        }
        return 0;
    }
}
