using System.Linq;
using NUnit.Framework;

namespace Aegis.Tests
{
    public class PointParserTests
    {
        [Test]
        public void SingleObject_IsOnePoint()
        {
            PointParseResult result = PointParser.Parse("{\"label\":\"gate-3\",\"latitude\":1.35,\"longitude\":103.82,\"value\":64}");

            Assert.That(result.Ok, Is.True);
            Assert.That(result.Points, Has.Count.EqualTo(1));
            Assert.That(result.Points[0].Label, Is.EqualTo("gate-3"));
            Assert.That(result.Points[0].Value, Is.EqualTo(64f));
        }

        [Test]
        public void RawArray_IsManyPoints()
        {
            PointParseResult result = PointParser.Parse("[{\"label\":\"a\"},{\"label\":\"b\"}]");

            Assert.That(result.Points.Select(p => p.Label), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void WrapperObject_IsManyPoints()
        {
            PointParseResult result = PointParser.Parse("{\"points\": [{\"label\":\"a\"},{\"label\":\"b\"}]}");

            Assert.That(result.Points, Has.Count.EqualTo(2));
        }

        [Test]
        public void SingleObject_WhoseLabelIsPoints_IsNotTreatedAsWrapper()
        {
            PointParseResult result = PointParser.Parse("{\"label\":\"points\",\"place\":\"Tokyo\"}");

            Assert.That(result.Points, Has.Count.EqualTo(1));
            Assert.That(result.Points[0].Label, Is.EqualTo("points"));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not json")]
        [TestCase("{\"label\":")]
        public void UnreadableBody_IsFatal(string body)
        {
            PointParseResult result = PointParser.Parse(body);

            Assert.That(result.Ok, Is.False);
            Assert.That(result.FatalError, Is.Not.Empty);
            Assert.That(result.Points, Is.Empty);
        }

        [Test]
        public void OutOfRangeCoordinates_RejectOnlyThatPoint()
        {
            PointParseResult result = PointParser.Parse("[{\"label\":\"ok\"},{\"label\":\"bad\",\"latitude\":91}]");

            Assert.That(result.Ok, Is.True);
            Assert.That(result.Points.Single().Label, Is.EqualTo("ok"));
            Assert.That(result.Errors.Single(), Does.StartWith("points[1]: latitude"));
        }

        [Test]
        public void NegativeValue_IsRejected()
        {
            PointParseResult result = PointParser.Parse("{\"label\":\"x\",\"value\":-5}");

            Assert.That(result.Points, Is.Empty);
            Assert.That(result.Rejected, Is.EqualTo(1));
        }

        [Test]
        public void EmptyObject_IsRejected()
        {
            PointParseResult result = PointParser.Parse("{}");

            Assert.That(result.Points, Is.Empty);
            Assert.That(result.Errors.Single(), Does.Contain("needs at least"));
        }

        [Test]
        public void TooManyPoints_IsFatal()
        {
            PointParseResult result = PointParser.Parse("[{\"label\":\"a\"},{\"label\":\"b\"},{\"label\":\"c\"}]", maxPoints: 2);

            Assert.That(result.Ok, Is.False);
            Assert.That(result.FatalError, Does.Contain("limit is 2"));
        }

        [Test]
        public void Text_IsTrimmedStrippedAndCapped()
        {
            string longLabel = new string('x', PointParser.MaxTextLength + 50);
            PointParseResult result = PointParser.Parse("[{\"label\":\"  a\\u0007b  \"},{\"label\":\"" + longLabel + "\"}]");

            Assert.That(result.Points[0].Label, Is.EqualTo("ab"));
            Assert.That(result.Points[1].Label.Length, Is.EqualTo(PointParser.MaxTextLength));
        }

        [Test]
        public void Fingerprint_ChangesWithValue_NotWithInstance()
        {
            DataPoint a = PointParser.Parse("{\"id\":\"1\",\"label\":\"x\",\"value\":3}").Points[0];
            DataPoint same = PointParser.Parse("{\"id\":\"1\",\"label\":\"x\",\"value\":3}").Points[0];
            DataPoint changed = PointParser.Parse("{\"id\":\"1\",\"label\":\"x\",\"value\":4}").Points[0];

            Assert.That(PointParser.Fingerprint(a), Is.EqualTo(PointParser.Fingerprint(same)));
            Assert.That(PointParser.Fingerprint(a), Is.Not.EqualTo(PointParser.Fingerprint(changed)));
        }
    }
}
