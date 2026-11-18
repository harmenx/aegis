using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Aegis.Tests
{
    public class PointsFeedTests
    {
        [Test]
        public void RewrittenFile_OnlyDrawsNewPoints()
        {
            List<DataPoint> first = PointParser.Parse("[{\"id\":\"1\",\"label\":\"a\"},{\"id\":\"2\",\"label\":\"b\"}]").Points;
            List<DataPoint> second = PointParser.Parse("[{\"id\":\"1\",\"label\":\"a\"},{\"id\":\"2\",\"label\":\"b\"},{\"id\":\"3\",\"label\":\"c\"}]").Points;

            List<DataPoint> drawnFirst = PointsFeed.NewPoints(null, first, out HashSet<string> afterFirst);
            List<DataPoint> drawnSecond = PointsFeed.NewPoints(afterFirst, second, out HashSet<string> afterSecond);

            Assert.That(drawnFirst, Has.Count.EqualTo(2));
            Assert.That(drawnSecond.Single().Id, Is.EqualTo("3"));
            Assert.That(afterSecond, Has.Count.EqualTo(3));
        }

        [Test]
        public void RemovedPoints_AreForgotten()
        {
            List<DataPoint> full = PointParser.Parse("[{\"id\":\"1\",\"label\":\"a\"},{\"id\":\"2\",\"label\":\"b\"}]").Points;
            List<DataPoint> shrunk = PointParser.Parse("[{\"id\":\"2\",\"label\":\"b\"}]").Points;

            PointsFeed.NewPoints(null, full, out HashSet<string> afterFull);
            PointsFeed.NewPoints(afterFull, shrunk, out HashSet<string> afterShrunk);

            Assert.That(afterShrunk, Has.Count.EqualTo(1));
        }

        [Test]
        public void DuplicateRows_InOneFile_DrawOnce()
        {
            List<DataPoint> points = PointParser.Parse("[{\"id\":\"1\",\"label\":\"a\"},{\"id\":\"1\",\"label\":\"a\"}]").Points;

            Assert.That(PointsFeed.NewPoints(null, points, out _), Has.Count.EqualTo(1));
        }

        [Test]
        public void JsonLines_SplitAcrossReads()
        {
            var reader = new JsonLinesReader();
            var lines = new List<string>();
            byte[] bytes = Encoding.UTF8.GetBytes("{\"label\":\"a\"}\n{\"lab");
            byte[] rest = Encoding.UTF8.GetBytes("el\":\"b\"}\r\n\n");

            reader.Feed(bytes, bytes.Length, lines);
            Assert.That(lines, Is.EqualTo(new[] { "{\"label\":\"a\"}" }));

            reader.Feed(rest, rest.Length, lines);
            Assert.That(lines, Is.EqualTo(new[] { "{\"label\":\"a\"}", "{\"label\":\"b\"}" }));
            Assert.That(reader.Offset, Is.EqualTo(bytes.Length + rest.Length));
        }

        [Test]
        public void JsonLines_MultiByteCharacterSplitAcrossReads()
        {
            var reader = new JsonLinesReader();
            var lines = new List<string>();
            byte[] bytes = Encoding.UTF8.GetBytes("{\"place\":\"Zürich\"}\n");
            int split = System.Array.IndexOf(bytes, (byte)0xC3) + 1;

            reader.Feed(bytes.Take(split).ToArray(), split, lines);
            reader.Feed(bytes.Skip(split).ToArray(), bytes.Length - split, lines);

            Assert.That(lines.Single(), Is.EqualTo("{\"place\":\"Zürich\"}"));
        }

        [Test]
        public void JsonLines_OversizedLineIsDropped()
        {
            var reader = new JsonLinesReader(maxLineChars: 16);
            var lines = new List<string>();
            byte[] bytes = Encoding.UTF8.GetBytes(new string('x', 40) + "\n{\"a\":1}\n");

            reader.Feed(bytes, bytes.Length, lines);

            Assert.That(lines, Is.EqualTo(new[] { "{\"a\":1}" }));
            Assert.That(reader.DroppedLines, Is.EqualTo(1));
        }
    }
}
