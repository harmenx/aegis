using NUnit.Framework;

namespace Aegis.Tests
{
    public class TextFormatTests
    {
        [TestCase(0, "0")]
        [TestCase(950, "950")]
        [TestCase(25099, "25,099")]
        [TestCase(533951000, "533.9M")]
        [TestCase(1999999, "1.9M")]
        [TestCase(250400, "250.4K")]
        [TestCase(1240000000, "1.24B")]
        [TestCase(-96420000, "−96.4M")]
        public void Compact(double value, string expected)
        {
            Assert.That(TextFormat.Compact(value), Is.EqualTo(expected));
        }

        [Test]
        public void Compact_NonFinite_IsDash()
        {
            Assert.That(TextFormat.Compact(double.NaN), Is.EqualTo("—"));
        }

        [TestCase("abc", 5, "abc")]
        [TestCase("abcdef", 4, "abc…")]
        [TestCase(null, 4, "")]
        public void Ellipsize(string value, int max, string expected)
        {
            Assert.That(TextFormat.Ellipsize(value, max), Is.EqualTo(expected));
        }

        [Test]
        public void EscapeRichText_NeutralisesTags()
        {
            string escaped = TextFormat.EscapeRichText("<color=red>x</color>");

            Assert.That(escaped, Does.Not.Contain("<"));
            Assert.That(escaped, Does.Contain("color=red>x"));
        }

        [Test]
        public void StreamEntry_DoesNotCarryTagsFromData()
        {
            var gitEvent = new GitEvent { Actor = "<b>evil</b>", Repo = "<size=200>big", Category = "<mark>", City = "<i>" };

            string entry = DashboardController.Format(gitEvent);

            Assert.That(entry, Does.Not.Contain("<b>"));
            Assert.That(entry, Does.Not.Contain("<size"));
            Assert.That(entry, Does.Not.Contain("<i>"));
        }

        [TestCase("acme/widgets", "widgets")]
        [TestCase("widgets", "widgets")]
        [TestCase("", "")]
        public void ShortRepo(string repo, string expected)
        {
            Assert.That(TextFormat.ShortRepo(repo), Is.EqualTo(expected));
        }
    }
}
