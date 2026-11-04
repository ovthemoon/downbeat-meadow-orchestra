using System;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class MediaFileCheckTests
    {
        static byte[] ValidWav(int samples = 400) => WavEncoder.EncodePcm16Mono(new float[samples], 44100);

        static string CheckWav(byte[] bytes) => MediaFileCheck.CheckWav(bytes.Length, (offset, count) => Slice(bytes, offset, count));

        static string CheckImage(string name, byte[] bytes) => MediaFileCheck.CheckImage(name, bytes.Length, (offset, count) => Slice(bytes, offset, count));

        static byte[] Slice(byte[] bytes, long offset, int count)
        {
            var available = (int)Math.Max(0, Math.Min(count, bytes.Length - offset));
            var slice = new byte[available];
            Array.Copy(bytes, offset, slice, 0, available);
            return slice;
        }

        static byte[] Png(int bodyBytes = 200)
        {
            var bytes = new byte[8 + bodyBytes + 12];
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
            new byte[] { 0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 }.CopyTo(bytes, bytes.Length - 12);
            return bytes;
        }

        static byte[] Jpeg(int bodyBytes = 200)
        {
            var bytes = new byte[3 + bodyBytes + 2];
            new byte[] { 0xFF, 0xD8, 0xFF }.CopyTo(bytes, 0);
            bytes[bytes.Length - 2] = 0xFF;
            bytes[bytes.Length - 1] = 0xD9;
            return bytes;
        }

        [Test]
        public void Wav_AcceptsAWellFormed16BitFile()
        {
            Assert.IsNull(CheckWav(ValidWav()));
        }

        [Test]
        public void Wav_AcceptsTheShippedSounds()
        {
            var bytes = System.IO.File.ReadAllBytes(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(TestContent.RepoContentPath), "audio", "harp_A4.wav"));

            Assert.IsNull(CheckWav(bytes));
        }

        [Test]
        public void Wav_RejectsAnEmptyFile()
        {
            StringAssert.Contains("empty", CheckWav(new byte[0]));
        }

        [Test]
        public void Wav_RejectsSomethingThatIsNotAWav()
        {
            var notWav = ValidWav();
            notWav[0] = (byte)'X';

            StringAssert.Contains("RIFF", CheckWav(notWav));
        }

        [Test]
        public void Wav_RejectsAHeaderThatEndsBeforeTheSoundStarts()
        {
            var bytes = ValidWav();

            StringAssert.Contains("data", CheckWav(Slice(bytes, 0, 36)));
        }

        [Test]
        public void Wav_RejectsAFileThatWasCutShort()
        {
            var bytes = ValidWav(4000);

            StringAssert.Contains("cut short", CheckWav(Slice(bytes, 0, bytes.Length / 2)));
        }

        [Test]
        public void Wav_RejectsAnUnsupportedFormat()
        {
            var bytes = ValidWav();
            bytes[20] = 0x55; // format tag: not PCM, not float

            StringAssert.Contains("format", CheckWav(bytes));
        }

        [Test]
        public void Wav_RejectsAnImpossibleSampleRate()
        {
            var bytes = ValidWav();
            BitConverter.GetBytes(3).CopyTo(bytes, 24);

            StringAssert.Contains("sample rate", CheckWav(bytes));
        }

        [Test]
        public void Wav_RejectsASoundWithNoSamples()
        {
            StringAssert.Contains("no sound", CheckWav(ValidWav(0)));
        }

        [Test]
        public void Wav_SkipsOtherChunksBeforeTheData()
        {
            var plain = ValidWav(300);
            var list = new byte[] { (byte)'L', (byte)'I', (byte)'S', (byte)'T', 4, 0, 0, 0, 1, 2, 3, 4 };
            var withList = new byte[plain.Length + list.Length];
            Array.Copy(plain, 0, withList, 0, 36);
            list.CopyTo(withList, 36);
            Array.Copy(plain, 36, withList, 36 + list.Length, plain.Length - 36);

            Assert.IsNull(CheckWav(withList));
        }

        [Test]
        public void Image_AcceptsAWholePngAndAWholeJpeg()
        {
            Assert.IsNull(CheckImage("a.png", Png()));
            Assert.IsNull(CheckImage("b.jpg", Jpeg()));
            Assert.IsNull(CheckImage("c.JPEG", Jpeg()));
        }

        [Test]
        public void Image_AcceptsTheShippedPlates()
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(TestContent.RepoContentPath), "img", "meadow");
            foreach (var file in System.IO.Directory.GetFiles(folder))
            {
                Assert.IsNull(CheckImage(file, System.IO.File.ReadAllBytes(file)), file);
            }
        }

        [Test]
        public void Image_RejectsATruncatedPngAndATruncatedJpeg()
        {
            var png = Png();
            var jpeg = Jpeg();

            StringAssert.Contains("cut short", CheckImage("a.png", Slice(png, 0, png.Length - 40)));
            StringAssert.Contains("cut short", CheckImage("b.jpg", Slice(jpeg, 0, jpeg.Length - 40)));
        }

        [Test]
        public void Image_RejectsTheWrongKindOfFile()
        {
            StringAssert.Contains("not a PNG", CheckImage("a.png", Jpeg()));
            StringAssert.Contains("not a JPEG", CheckImage("b.jpg", Png()));
            StringAssert.Contains("empty", CheckImage("c.jpg", new byte[0]));
        }

        [Test]
        public void Image_RejectsAnExtensionItDoesNotKnow()
        {
            StringAssert.Contains("kind of picture", CheckImage("d.gif", Png()));
        }
    }
}
