using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;

namespace Aegis.Tests
{
    public class AegisConfigTests
    {
        [Test]
        public void Defaults_AreLocalOnly()
        {
            var config = new AegisConfig();

            Assert.That(config.port, Is.EqualTo(AegisConfig.DefaultPort));
            Assert.That(config.bindAddress, Is.EqualTo("127.0.0.1"));
            Assert.That(config.Demo, Is.EqualTo(DemoMode.Auto));
        }

        [Test]
        public void File_OverridesDefaults()
        {
            AegisConfig config = AegisConfig.FromJson("{\"port\":9000,\"demo\":\"off\"}", out string error);

            Assert.That(error, Is.Null);
            Assert.That(config.port, Is.EqualTo(9000));
            Assert.That(config.Demo, Is.EqualTo(DemoMode.Off));
            Assert.That(config.listen, Is.True);
        }

        [Test]
        public void BrokenFile_FallsBackToDefaults()
        {
            AegisConfig config = AegisConfig.FromJson("{port:", out string error);

            Assert.That(error, Is.Not.Null);
            Assert.That(config.port, Is.EqualTo(AegisConfig.DefaultPort));
        }

        [Test]
        public void CommandLine_BeatsEnvironment()
        {
            var config = new AegisConfig();
            var environment = new Hashtable { { "AEGIS_PORT", "9100" }, { "AEGIS_DEMO", "on" } };
            var args = new[] { "Aegis", "-aegis-port", "9200" };
            var warnings = new List<string>();

            config.ApplyOverrides(environment, args, warnings);

            Assert.That(config.port, Is.EqualTo(9200));
            Assert.That(config.Demo, Is.EqualTo(DemoMode.On));
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void BadValues_AreWarnedAndReplaced()
        {
            var config = new AegisConfig { port = 70000, demo = "sometimes" };
            var warnings = new List<string>();

            config.ApplyOverrides(new Hashtable { { "AEGIS_LISTEN", "maybe" } }, null, warnings);
            config.Validate(warnings);

            Assert.That(config.port, Is.EqualTo(AegisConfig.DefaultPort));
            Assert.That(config.Demo, Is.EqualTo(DemoMode.Auto));
            Assert.That(config.listen, Is.True);
            Assert.That(warnings, Has.Count.EqualTo(3));
        }

        [Test]
        public void PublicBind_IsWarned()
        {
            var config = new AegisConfig { bindAddress = "0.0.0.0" };
            var warnings = new List<string>();

            config.Validate(warnings);

            Assert.That(warnings, Has.Some.Contains("no authentication"));
        }
    }
}
