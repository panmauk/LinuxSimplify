using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace LinuxSimplify.Services
{
    /// <summary>
    /// Sound exists only to confirm irreversible actions.
    /// If a sound can be removed without confusion, it should be removed.
    /// </summary>
    public static class SoundHelper
    {
        public static bool Enabled { get; set; } = true;
        private const int RATE = 22050;

        /// <summary>
        /// Slide complete — short metallic snap.
        /// Like a vice grip opening. Physical, muted. ~80ms.
        /// </summary>
        public static void PlayUnlock()
        {
            if (!Enabled) return;
            Task.Run(() =>
            {
                int samples = (int)(0.08 * RATE);
                var data = new short[samples];
                var rng = new Random(42);

                for (int i = 0; i < samples; i++)
                {
                    double t = (double)i / RATE;
                    double env = Math.Exp(-i / (RATE * 0.012));
                    double noise = (rng.NextDouble() * 2 - 1);
                    double metal1 = Math.Sin(2 * Math.PI * 3800 * t) * 0.3;
                    double metal2 = Math.Sin(2 * Math.PI * 2200 * t) * 0.2;
                    double body = Math.Sin(2 * Math.PI * 800 * t) * Math.Exp(-i / (RATE * 0.006)) * 0.15;
                    double sample = (noise * 0.35 + metal1 + metal2 + body) * env;
                    data[i] = (short)(sample * 4000);
                }
                PlayRaw(data);
            });
        }

        private static void PlayRaw(short[] data)
        {
            try
            {
                using (var ms = new MemoryStream())
                using (var bw = new BinaryWriter(ms))
                {
                    int dataSize = data.Length * 2;
                    bw.Write(new char[] { 'R', 'I', 'F', 'F' });
                    bw.Write(36 + dataSize);
                    bw.Write(new char[] { 'W', 'A', 'V', 'E' });
                    bw.Write(new char[] { 'f', 'm', 't', ' ' });
                    bw.Write(16);
                    bw.Write((short)1);
                    bw.Write((short)1);
                    bw.Write(RATE);
                    bw.Write(RATE * 2);
                    bw.Write((short)2);
                    bw.Write((short)16);
                    bw.Write(new char[] { 'd', 'a', 't', 'a' });
                    bw.Write(dataSize);
                    foreach (var s in data) bw.Write(s);
                    bw.Flush();
                    ms.Position = 0;
                    using (var player = new SoundPlayer(ms))
                        player.PlaySync();
                }
            }
            catch { }
        }
    }
}
