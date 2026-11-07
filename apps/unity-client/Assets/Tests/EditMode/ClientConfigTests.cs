using NUnit.Framework;
using Orbis.Core;

namespace Orbis.Tests.EditMode
{
    public sealed class ClientConfigTests
    {
        static ClientRuntimeConfig Defaults() => ClientRuntimeConfig.CreateDefaults("C:/Orbis");

        [Test]
        public void CommandLineCredentialsAreParsed()
        {
            var config = ClientRuntimeConfig.Parse(Defaults(), new[] { "--user=admin", "--password=secret" });
            Assert.That(config.TestUsername, Is.EqualTo("admin"));
            Assert.That(config.TestPassword, Is.EqualTo("secret"));
            Assert.That(config.HasTestCredentials, Is.True);
        }

        [Test]
        public void WithoutCredentialsDefaultsStayEmpty()
        {
            var config = ClientRuntimeConfig.Parse(Defaults(), new[] { "--user=admin" });
            Assert.That(config.TestUsername, Is.EqualTo("admin"));
            Assert.That(config.HasTestCredentials, Is.False);
        }

        [Test]
        public void CredentialsFlowThroughNestedParsing()
        {
            var parsed = ClientRuntimeConfig.Parse(Defaults(), new[] { "--user=admin", "--password=secret" });
            var reparsed = ClientRuntimeConfig.Parse(parsed, new[] { "--locale=en" });
            Assert.That(reparsed.HasTestCredentials, Is.True);
            Assert.That(reparsed.Locale, Is.EqualTo("en"));
        }

        [Test]
        public void CommandLineCredentialsWinOverRememberedOnes()
        {
            var config = ClientRuntimeConfig.Parse(Defaults(), new[] { "--user=admin", "--password=secret" });
            var staged = AutoLoginPlan.Choose(config, false, "player", "other", true);
            Assert.That(staged.HasCredentials, Is.True);
            Assert.That(staged.Username, Is.EqualTo("admin"));
            Assert.That(staged.Password, Is.EqualTo("secret"));
            Assert.That(staged.FromCommandLine, Is.True);
        }

        [Test]
        public void RememberedCredentialsAreUsedWhenToggleEnabled()
        {
            var staged = AutoLoginPlan.Choose(Defaults(), false, "player", "hunter", true);
            Assert.That(staged.FromCommandLine, Is.False);
            Assert.That(staged.Username, Is.EqualTo("player"));
            Assert.That(staged.Password, Is.EqualTo("hunter"));
        }

        [Test]
        public void ToggleOffOrIncompleteStorageMeansNoAutomaticLogin()
        {
            Assert.That(AutoLoginPlan.Choose(Defaults(), false, "player", "hunter", false).HasCredentials, Is.False);
            Assert.That(AutoLoginPlan.Choose(Defaults(), false, "player", "", true).HasCredentials, Is.False);
            Assert.That(AutoLoginPlan.Choose(Defaults(), false, "", "hunter", true).HasCredentials, Is.False);
            Assert.That(AutoLoginPlan.Choose(null, false, "player", "hunter", true).HasCredentials, Is.False);
        }

        [Test]
        public void ARestoredSessionNeverBecomesALogin()
        {
            var config = ClientRuntimeConfig.Parse(Defaults(), new[] { "--user=admin", "--password=secret" });
            Assert.That(AutoLoginPlan.Choose(config, true, "player", "hunter", true).HasCredentials, Is.False);
        }
    }
}
