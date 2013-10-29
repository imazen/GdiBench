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
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GdiBench.mountain-jpg.jpg");
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();

            Console.WriteLine("Measure Bitmap.FromStream (jpeg)");
            MeasureOperation(delegate(object input, TimeSegment time)
            {
                var readStream = new MemoryStream((byte[])input);

                time.MarkStart();
                using (var bit = System.Drawing.Bitmap.FromStream(readStream, false, true))
                {
                    time.MarkStop();
                    var test = bit.Width;
                }

            }, bytes);

            Console.WriteLine("Measure Bitmap.Save (jpeg)");
            MeasureOperation(delegate(object input, TimeSegment time)
            {
                var readStream = new MemoryStream((byte[])input);

                
                using (var bit = System.Drawing.Bitmap.FromStream(readStream, false, true))
                using (EncoderParameters p = new EncoderParameters(1))
                using (var ep = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)90)){
                    var outStream = new MemoryStream(readStream.Capacity);
                    p.Param[0] = ep;
                    time.MarkStart();
                    bit.Save(outStream, GetImageCodeInfo("image/jpeg"), p);
                    time.MarkStop();
                }

            }, bytes);


            Console.WriteLine("Measure DrawImage");
            MeasureOperation(delegate(object input, TimeSegment time)
            {
                var readStream = new MemoryStream((byte[])input);

                using (var bit = System.Drawing.Bitmap.FromStream(readStream, false, true))
                using (var canvas = new Bitmap(500, 500))
                using (var g = Graphics.FromImage(canvas))
                using (var attrs = new ImageAttributes() {})
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
                }

            }, bytes);
            Console.ReadKey();
        }

        static void MeasureOperation(Action<object, TimeSegment> op, object input)
        {
            //Throwaway run for warmup
            var throwaway = TimeOperation(op, 1, input);
            Console.WriteLine("Throwaway run " + throwaway.First().Milliseconds + "ms");

            foreach(var threads in new int[]{4,8,32,2}){

                //Time in parallel
                var wallClock = Stopwatch.StartNew();
                var parallel = TimeOperation(op, threads, input);
                wallClock.Stop();

                //Convert to durations and deduplicate for a total
                var parallelDurations = parallel.ConvertAll<double>((s) => s.Milliseconds);
                var deduped = DeduplicateTime(parallel);

                Console.WriteLine(threads + " parallel threads: Wall time:" + wallClock.ElapsedMilliseconds +
                    " Active:" + Math.Round(deduped) + " Avg  time:" + Math.Round(deduped / threads) + " Min:" + parallelDurations.Min() + " Max: " + parallelDurations.Max());

                
                var serial = new ConcurrentStack<long>();
                wallClock.Restart();
                for (var i = 0; i < threads; i++)
                {
                    var per = new TimeSegment();
                    op.Invoke(input, per);
                    serial.Push((long)per.Milliseconds);
                }
                wallClock.Stop();

                Console.WriteLine(threads + " serialized runs: Wall time:" + wallClock.ElapsedMilliseconds +
                    " Active:" + serial.Sum() + " Avg:" + Math.Round(serial.Average()) + " Min:" + serial.Min() + " Max: " + serial.Max());


                Console.WriteLine("Parallel averages " + Math.Round((deduped / threads) - serial.Average()) + "ms slower per run (" + Math.Round(deduped - serial.Sum()) + " total) on " + threads + " threads");
                Console.WriteLine();
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

        static List<TimeSegment> TimeOperation(Action<object, TimeSegment> op, int threads, object input)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (threads == 1)
            {
                var time = new TimeSegment();
                op.Invoke(input, time);
                return new List<TimeSegment>(new TimeSegment[] { time });
            }
            else
            {
                var times = new ConcurrentStack<TimeSegment>();
                Parallel.For(0, threads, new Action<int>(delegate(int index)
                {
                    
                    var time = new TimeSegment();
                    op.Invoke(input, time);
                    times.Push(time);
                }));
                return times.ToList();
            }
        }
    }
}
