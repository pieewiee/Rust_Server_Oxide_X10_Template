using System;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("AutoBroadcast", "Wulf/lukespragg", "1.0.8", ResourceId = 684)]
    [Description("Sends randomly configured chat messages every X amount of seconds")]

    class AutoBroadcast : CovalencePlugin
    {
        #region Initialization

        bool random;

        int interval;
        int nextKey;

        protected override void LoadDefaultConfig()
        {
            // Options
            Config["Randomize Messages (true/false)"] = random = GetConfig("Randomize Messages (true/false)", false);

            // Settings
            Config["Broadcast Interval (Seconds)"] = interval = GetConfig("Broadcast Interval (Seconds)", 300);

            SaveConfig();
        }

        void OnServerInitialized()
        {
            LoadDefaultConfig();

            if (lang.GetLanguages(this).Length == 0 || lang.GetMessages(lang.GetServerLanguage(), this)?.Count == 0)
            {
                lang.RegisterMessages(new Dictionary<string, string>
                {
                    ["ExampleMessage"] = "This is an example. Change it, remove it, translate it, whatever!",
                    ["AnotherExample"] = "This is another example, notice the comma at the end of the line above..."
                }, this, lang.GetServerLanguage());
            }
            else
            {
                foreach (var language in lang.GetLanguages(this))
                {
                    var messages = new Dictionary<string, string>();
                    foreach (var message in lang.GetMessages(language, this)) messages.Add(message.Key, message.Value);
                    lang.RegisterMessages(messages, this, language);
                }
            }

            Broadcast();
        }

        #endregion

        #region Broadcasting

        void Broadcast()
        {
            if (lang.GetLanguages(this) == null || lang.GetMessages(lang.GetServerLanguage(), this).Count == 0) return;

            timer.Every(interval, () =>
            {
                if (players.Connected.Count() <= 0) return;

                Dictionary<string, string> messages = null;
                foreach (var player in players.Connected)
                {
                    messages = lang.GetMessages(lang.GetLanguage(player.Id), this) ?? lang.GetMessages(lang.GetServerLanguage(), this);

                    if (messages == null || messages.Count == 0)
                    {
                        LogWarning($"No messages found for {player.Name} in {lang.GetLanguage(player.Id)} or {lang.GetServerLanguage()}");
                        continue;
                    }

                    var message = random ? messages.ElementAt(new Random().Next(0, messages.Count - 1)) : messages.ElementAt(nextKey);
                    if (message.Key != null) player.Message(Lang(message.Key, player.Id));
                }
                nextKey = nextKey + 1 == messages.Count ? 0 : nextKey + 1; // TODO: Don't assume that all languages have same count
            });
        }

        #endregion

        #region Helpers

        T GetConfig<T>(string name, T value) => Config[name] == null ? value : (T)Convert.ChangeType(Config[name], typeof(T));

        string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        #endregion
    }
}