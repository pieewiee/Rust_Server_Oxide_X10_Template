namespace uMod.Plugins
{
    [Info("Auto Save", "CastBlacKing", "1.0.0")]
    [Description("Auto saves the server every X seconds")]
    public class AutoSave : Plugin
    {
        private DefaultConfig config;

        [Config, Toml]
        private class DefaultConfig
        {
            public bool Enabled = true;
            public int Interval = 300;
        }

        private void OnServerInitialized(DefaultConfig defaultConfig)
        {
            config = defaultConfig;

            timer.Every(config.Interval, () =>
            {
                Server.Save();
            });
        }
    }
}