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

            //Console.WriteLine("Measure PNG decoding");
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GdiBench.mountain-png.png");
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();

            //MeasureOperation(delegate(object input, Stopwatch sw)
            //{
            //    var readStream = new MemoryStream((byte[])input);
            //    sw.Start();
            //    using (var bit = System.Drawing.Bitmap.FromStream(readStream, false, true))
            //    {
            //        sw.Stop();
            //    }
                
            //}, bytes);

            Console.WriteLine("Measure DrawImage");
            MeasureOperation(delegate(object input, Stopwatch sw)
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
                    sw.Start();
                    
                    g.DrawImage(bit, new Point[] { new Point(0, 0), new Point(500, 0), new Point(0, 500) },
                        new Rectangle(0, 0, bit.Width, bit.Height), GraphicsUnit.Pixel, attrs);
                    sw.Stop();
                }

            }, bytes);
            Console.ReadKey();
        }

        static void MeasureOperation(Action<object, Stopwatch> op, object input)
        {
            var throwaway = TimeOperation(op, 1, input);
            Console.WriteLine("Throwaway run " + throwaway.First() + "ms");
            foreach(var threads in new int[]{2, 4,8,16,32,64, 1}){
                Stopwatch real = new Stopwatch();
                real.Start();
                var set = TimeOperation(op, threads, input);
                real.Stop();
                Console.WriteLine(threads + " parallel threads: Real:" + real.ElapsedMilliseconds +
                    " Active:" + set.Sum() +  " Avg:" + set.Average() + " Min:" + set.Min() + " Max: " + set.Max());

                real.Restart();
                var serial = new ConcurrentStack<long>();
                for (var i = 0; i < threads; i++)
                {
                    var per = new Stopwatch();
                    op.Invoke(input, per);
                    serial.Push(per.ElapsedMilliseconds);
                }
                real.Stop();

                Console.WriteLine(threads + " serialized runs: Real:" + real.ElapsedMilliseconds +
                    " Active:" + serial.Sum() + " Avg:" + serial.Average() + " Min:" + serial.Min() + " Max: " + serial.Max());

                Console.WriteLine("Parallel is ms slower than serial: " + (set.Average() - serial.Average()).ToString());
            }

        }

        static List<long> TimeOperation(Action<object, Stopwatch> op, int threads, object input)
        {
            if (threads == 1)
            {
                var sw = new Stopwatch();
                op.Invoke(input, sw);
                return new List<long>(new long[] {sw.ElapsedMilliseconds });
            }
            else
            {
                var times = new ConcurrentStack<long>();
                Parallel.For(0, threads, new Action<int>(delegate(int index)
                {
                    GC.Collect();
                    var sw = new Stopwatch();
                    op.Invoke(input, sw);
                    times.Push(sw.ElapsedMilliseconds);
                }));
                return times.ToList();
            }
        }
    }
}
