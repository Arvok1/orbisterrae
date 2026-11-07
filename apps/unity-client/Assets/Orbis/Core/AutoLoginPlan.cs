namespace Orbis.Core
{
    /// <summary>Credentials staged for an automatic first login, or nothing.</summary>
    public readonly struct StagedCredentials
    {
        public string Username { get; }
        public string Password { get; }
        /// <summary>True when the credentials came from the command line rather than saved preferences.</summary>
        public bool FromCommandLine { get; }

        public bool HasCredentials => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrEmpty(Password);

        public StagedCredentials(string username, string password, bool fromCommandLine = false)
        {
            Username = username ?? string.Empty;
            Password = password ?? string.Empty;
            FromCommandLine = fromCommandLine;
        }
    }

    /// <summary>Pure decision of whether the client should log in automatically on start.</summary>
    public static class AutoLoginPlan
    {
        /// <summary>
        /// Command line credentials win, then credentials remembered by the auto-login toggle.
        /// A restored session already drives its own reconnection and is never replaced.
        /// </summary>
        public static StagedCredentials Choose(
            ClientRuntimeConfig config,
            bool hasRestoredSession,
            string rememberedUsername,
            string rememberedPassword,
            bool rememberEnabled)
        {
            if (hasRestoredSession || config == null) return default;
            if (config.HasTestCredentials)
            {
                return new StagedCredentials(config.TestUsername, config.TestPassword, true);
            }

            if (!rememberEnabled) return default;
            var remembered = new StagedCredentials(rememberedUsername, rememberedPassword);
            return remembered.HasCredentials ? remembered : default;
        }
    }
}
