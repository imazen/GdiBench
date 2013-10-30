using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GdiBench
{

    public class TimeSegment
    {
        public long StartTicks { get; set; }
        public long StopTicks { get; set; }
        public void MarkStart()
        {
            StartTicks = Stopwatch.GetTimestamp();
        }
        public void MarkStop()
        {
            StopTicks = Stopwatch.GetTimestamp();
        }

        public double Milliseconds
        {
            get
            {
                return ((StopTicks - StartTicks) * 1000) / Stopwatch.Frequency;
            }
        }
    }

    public class Timing
    {
        public static void MeasureOperation(Action<object, TimeSegment> op, object input)
        {
            //Throwaway run for warmup
            var throwaway = TimeOperation(op, 1, 1, input);
            Console.WriteLine("Throwaway run {0}ms", throwaway.First().Milliseconds);

            foreach (var threads in new int[] { 4, 8, 32, 64, 2 })
            {

                //Time in parallel
                var wallClock = Stopwatch.StartNew();
                var parallel = TimeOperation(op, threads, 1, input);
                wallClock.Stop();

                //Convert to durations and deduplicate for a total
                var parallelDurations = parallel.ConvertAll<double>((s) => s.Milliseconds);
                var deduped = DeduplicateTime(parallel);

                Console.WriteLine("{0} threads; Wall time:{1} Active:{2} Wall avg:{3} Avg:{4} Min:{5} Max:{6}",
                    threads, wallClock.ElapsedMilliseconds, Math.Round(deduped, 1), Math.Round(deduped / threads, 1), Math.Round(parallelDurations.Average(), 1), parallelDurations.Min(), parallelDurations.Max());

                //Time in serial
                wallClock.Restart();
                var serial = TimeOperation(op, 1, threads, input).ConvertAll<double>((s) => s.Milliseconds);
                wallClock.Stop();

                Console.WriteLine("{0} in sequence; Wall time:{1} Active:{2} Avg:{3} Min:{4} Max:{5}",
                    threads, wallClock.ElapsedMilliseconds, serial.Sum(), Math.Round(serial.Average(), 1), serial.Min(), serial.Max());


                var slowerPerRun = Math.Round((deduped / (double)threads) - serial.Average());
                var slowerTotal = Math.Round(deduped - serial.Sum());
                Console.WriteLine("Parallel averages {0}ms slower per run ({1} total) on {2} threads", slowerPerRun, slowerTotal, threads);
            }

        }

        public static double DeduplicateTime(List<TimeSegment> times)
        {
            times = new List<TimeSegment>(times);
            times.Add(new TimeSegment() { StartTicks = 0, StopTicks = 0 }); //Add accumulator seed
            times.Sort((a, b) => a.StartTicks.CompareTo(b.StartTicks));

            var result = times.Aggregate(delegate(TimeSegment acc, TimeSegment elem)
            {

                //Eliminate overlapping time
                if (acc.StopTicks > elem.StartTicks)
                {
                    elem.StartTicks = Math.Min(elem.StopTicks, acc.StopTicks);
                }
                //Aggregate non-redundant time and store last stop position
                return new TimeSegment()
                {
                    StartTicks = (acc.StartTicks + elem.StopTicks - elem.StartTicks),
                    StopTicks = Math.Max(acc.StopTicks, elem.StopTicks)
                };
            });
            return ((double)result.StartTicks * 1000.0f / Stopwatch.Frequency);
        }



        public static List<TimeSegment> TimeOperation(Action<object, TimeSegment> op, int threads, int batches, object input)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var times = new ConcurrentStack<TimeSegment>();
            for (var i = 0; i < batches; i++)
            {
                if (threads == 1)
                {
                    var time = new TimeSegment();
                    op.Invoke(input, time);
                    times.Push(time);
                }
                else
                {
                    Parallel.For(0, threads, new Action<int>(delegate(int index)
                    {
                        var time = new TimeSegment();
                        op.Invoke(input, time);
                        times.Push(time);
                    }));
                }
            }
            return times.ToList();

        }
    }
}
