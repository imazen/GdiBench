using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace GdiBench
{
    public class InstrumetedMemoryStream : MemoryStream 
    {

        public InstrumetedMemoryStream(byte[] buffer) : base(buffer) { }

        public long BytesRead { get; set; }

        public int? SleepMsPerReadCall { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (SleepMsPerReadCall != null && SleepMsPerReadCall.Value > 0)
            {
                Thread.Sleep(SleepMsPerReadCall.Value);
            }
            int read= base.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override int ReadByte()
        {
            if (SleepMsPerReadCall != null && SleepMsPerReadCall.Value > 0)
            {
                Thread.Sleep(SleepMsPerReadCall.Value);
            }
            BytesRead++;
            return base.ReadByte();
        }
    }
}
