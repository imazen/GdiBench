using ImageResizer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace GdiBench
{


    class Program
    {
        static void Main(string[] args)
        {
            Stopwatch decode = new Stopwatch();

            //Load jpeg into reusable byte array
            //var stream = new MemoryStream(File.ReadAllBytes("large.jpg"));
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GdiBench.mountain-jpg.jpg");
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();

            //Create PNG version via IR
            MemoryStream pngStream = new MemoryStream();
            ImageBuilder.Current.Build(new MemoryStream(bytes), pngStream, new Instructions("format=png"));
            var pngBytes = ImageResizer.ExtensionMethods.StreamExtensions.CopyToBytes(pngStream, true);

            var endecodingBenchmarks = new List<Tuple<string, Action>>();

            //Since we're doing so many decoding tests, make a template
            Func<byte[],Func<Stream,Image>,Action> CreateDecodeTest = (byte[] inputBytes, Func<Stream, Image> decoder) => {
                return () => {
                    var bytesRead = new ConcurrentStack<long>();
                    MeasureOperation(delegate(object input, TimeSegment time)
                    {
                        var readStream = new InstrumentedMemoryStream((byte[])input);
                        time.MarkStart();
                        using (var bit = decoder(readStream))
                        {
                            time.MarkStop();
                            bytesRead.Push(readStream.BytesRead);
                            var test = bit.Width;
                        }

                    }, inputBytes);
                    Console.WriteLine("Average bytes read per thread: " + bytesRead.Average().ToString());
                };
            };

            endecodingBenchmarks.Add(new Tuple<string, Action>("Measure new Bitmap(stream,useIcm=true) (jpeg)",
                CreateDecodeTest(bytes, (s) => new Bitmap(s,true))
            ));

            endecodingBenchmarks.Add(new Tuple<string, Action>("Measure new Bitmap(stream,useIcm=true) (png)",
                CreateDecodeTest(pngBytes, (s) => new Bitmap(s, true))
            ));

            endecodingBenchmarks.Add(new Tuple<string, Action>("Measure Bitmap.FromStream(readStream,true,true) (jpeg)",
                CreateDecodeTest(bytes, (s) => Bitmap.FromStream(s, true, true))
            ));


            endecodingBenchmarks.Add(new Tuple<string, Action>("Measure Bitmap.Save (jpeg)",
               () => {
                   MeasureOperation(delegate(object input, TimeSegment time)
                   {
                       var readStream = new MemoryStream((byte[])input);
                       using (var bit = System.Drawing.Bitmap.FromStream(readStream, false, true))
                       using (EncoderParameters p = new EncoderParameters(1))
                       using (var ep = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)90))
                       {
                           var outStream = new MemoryStream(readStream.Capacity);
                           p.Param[0] = ep;
                           time.MarkStart();
                           bit.Save(outStream, GetImageCodeInfo("image/jpeg"), p);
                           time.MarkStop();
                       }
                   }, bytes);
               }
           ));


            var resizeBenchmarks = new List<Tuple<string, Action>>();
            resizeBenchmarks.Add(new Tuple<string, Action>("Measure DrawImage -> 500x500",
               () =>
               {
                   MeasureOperation(delegate(object input, TimeSegment time)
                   {
                       var readStream = new InstrumentedMemoryStream((byte[])input);
                       readStream.BytesRead = 0;

                       using (var bit = new Bitmap(readStream, true))
                       {
                           readStream.SleepMsPerReadCall = 50;
                           readStream.BytesRead = 0;

                           using (var canvas = new Bitmap(500, 500))
                           using (var g = Graphics.FromImage(canvas))
                           using (var attrs = new ImageAttributes() { })
                           {
                               attrs.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                               g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                               g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                               g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                               g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                               g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;


                               time.MarkStart();

                               g.DrawImage(bit, new Point[] { new Point(0, 0), new Point(500, 0), new Point(0, 500) },
                                   new Rectangle(0, 0, bit.Width, bit.Height), GraphicsUnit.Pixel, attrs);
                               time.MarkStop();
                               Debug.Assert(readStream.BytesRead == 0);
                           }
                       }

                   }, bytes);
               }));

            resizeBenchmarks.Add(new Tuple<string, Action>("Measure ImageResizer jpg->500px->jpg",
               () =>
               {
                   MeasureOperation(delegate(object input, TimeSegment time)
                    {
                        var readStream = new MemoryStream((byte[])input);
                        var outStream = new MemoryStream(readStream.Capacity);
                        time.MarkStart();
                        ImageBuilder.Current.Build(readStream, outStream, new Instructions("width=500&height=500&mode=stretch&format=jpg"));
                        time.MarkStop();

                    }, bytes);
               }));

            var all = new List<Tuple<string, Action>>();
            all.AddRange(endecodingBenchmarks);
            all.AddRange(resizeBenchmarks);

            List<Tuple<string, Action>> toRun = null;

            Console.WriteLine("(a) to run all, (d) to run decode/encode tests, (r) to run resize tests, (i) for individual");
            var key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.A) toRun = all;
            else if (key == ConsoleKey.E) toRun = endecodingBenchmarks;
            else if (key == ConsoleKey.R) toRun = resizeBenchmarks;
            else
            {   
                //Ask which ones to run
                toRun = new List<Tuple<string, Action>>();
                foreach (var t in all)
                {
                    Console.WriteLine("(y) to schedule: " + t.Item1);
                    if (Console.ReadKey(true).Key == ConsoleKey.Y)
                        toRun.Add(t);
                }
                Console.WriteLine();
                Console.WriteLine();
            }

            //Run selected benchmarks
            foreach (var t in toRun)
            {
                Console.WriteLine(t.Item1);
                t.Item2();
                Console.WriteLine();
                Console.WriteLine();
            }
            Console.WriteLine("Finished. Press any key to exit.");
            Console.ReadKey();
        }

        static void MeasureOperation(Action<object, TimeSegment> op, object input)
        {
            //Throwaway run for warmup
            var throwaway = TimeOperation(op, 1,1, input);
            Console.WriteLine("Throwaway run {0}ms", throwaway.First().Milliseconds);

            foreach(var threads in new int[]{4,8,32,64,2}){

                //Time in parallel
                var wallClock = Stopwatch.StartNew();
                var parallel = TimeOperation(op, threads, 1, input);
                wallClock.Stop();

                //Convert to durations and deduplicate for a total
                var parallelDurations = parallel.ConvertAll<double>((s) => s.Milliseconds);
                var deduped = DeduplicateTime(parallel);

                Console.WriteLine("{0} threads; Wall time:{1} Active:{2} Wall avg:{3} Avg:{4} Min:{5} Max:{6}",
                    threads, wallClock.ElapsedMilliseconds, Math.Round(deduped,1),  Math.Round(deduped / threads,1), Math.Round(parallelDurations.Average(),1), parallelDurations.Min(),parallelDurations.Max());

                //Time in serial
                wallClock.Restart();
                var serial = TimeOperation(op, 1, threads, input).ConvertAll<double>((s) => s.Milliseconds);
                wallClock.Stop();

                Console.WriteLine("{0} in sequence; Wall time:{1} Active:{2} Avg:{3} Min:{4} Max:{5}",
                    threads, wallClock.ElapsedMilliseconds, serial.Sum(), Math.Round(serial.Average(),1), serial.Min(), serial.Max());


                var slowerPerRun = Math.Round((deduped / (double)threads) - serial.Average());
                var slowerTotal = Math.Round(deduped - serial.Sum());
                Console.WriteLine("Parallel averages {0}ms slower per run ({1} total) on {2} threads", slowerPerRun,slowerTotal,threads);
            }

        }

        static double DeduplicateTime(List<TimeSegment> times)
        {
            times = new List<TimeSegment>(times);
            times.Add(new TimeSegment() { StartTicks = 0, StopTicks = 0 }); //Add accumulator seed
            times.Sort((a, b) => a.StartTicks.CompareTo(b.StartTicks));

            var result = times.Aggregate(delegate(TimeSegment acc, TimeSegment elem)
            {
                
                //Eliminate overlapping time
                if (acc.StopTicks > elem.StartTicks) {
                    elem.StartTicks = Math.Min(elem.StopTicks, acc.StopTicks);
                }
                //Aggregate non-redundant time and store last stop position
                return new TimeSegment(){ 
                    StartTicks=(acc.StartTicks + elem.StopTicks - elem.StartTicks), 
                    StopTicks = Math.Max(acc.StopTicks,elem.StopTicks)};
            });
            return ((double)result.StartTicks * 1000.0f / Stopwatch.Frequency);
        }

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

        static ImageCodecInfo GetImageCodeInfo(string mimeType) {
            ImageCodecInfo[] info = ImageCodecInfo.GetImageEncoders();
            foreach (ImageCodecInfo ici in info)
                if (ici.MimeType.Equals(mimeType, StringComparison.OrdinalIgnoreCase)) return ici;
            return null;
        }

        static List<TimeSegment> TimeOperation(Action<object, TimeSegment> op, int threads, int batches, object input)
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
