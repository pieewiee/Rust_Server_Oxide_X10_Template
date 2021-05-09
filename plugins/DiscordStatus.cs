using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Ext.Discord;
using Oxide.Ext.Discord.Attributes;
using Oxide.Ext.Discord.DiscordObjects;
using Steamworks;
using static Oxide.Ext.Discord.DiscordObjects.Embed;
using Random = Oxide.Core.Random;

namespace Oxide.Plugins
{
    [Info("Discord Status", "Gonzi", "3.0.0")]
    [Description("Shows server information as a discord bot status")]

    public class DiscordStatus : CovalencePlugin
    {
        private string seperatorText = string.Join("-", new string[25 + 1]);
        private bool enableChatSeparators;

        #region Fields

        [DiscordClient]
        private DiscordClient Client;

        [PluginReference]
        private Plugin DiscordAuth;

        Configuration config;
        private int statusIndex = -1;
        private string[] StatusTypes = new string[]
        {
            "Game",
            "Stream",
            "Listen",
            "Watch"
        };

        #endregion

        #region Config
        class Configuration
        {
            [JsonProperty(PropertyName = "Discord Bot Token")]
            public string BotToken = string.Empty;

            [JsonProperty(PropertyName = "Prefix")]
            public string Prefix = "!";

            [JsonProperty(PropertyName = "Discord Group Id needed for Commands (null to disable)")]
            public string GroupId = null;

            [JsonProperty(PropertyName = "Update Interval (Seconds)")]
            public int UpdateInterval = 5;

            [JsonProperty(PropertyName = "Randomize Status")]
            public bool Randomize = false;

            [JsonProperty(PropertyName = "Status Type (Game/Stream/Listen/Watch)")]
            public string StatusType = "Game";

            [JsonProperty(PropertyName = "Status", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Status = new List<string>
            {
                "{players.online} / {server.maxplayers} Online!",
                "{server.entities} Entities",
                "{players.sleepers} Sleepers!",
                "{players.authenticated} Linked Account(s)"
            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) throw new Exception();
            }
            catch
            {
                Config.WriteObject(config, false, $"{Interface.Oxide.ConfigDirectory}/{Name}.jsonError");
                PrintError("The configuration file contains an error and has been replaced with a default config.\n" +
                           "The error configuration file was saved in the .jsonError extension");
                LoadDefaultConfig();
            }

            SaveConfig();
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void SaveConfig() => Config.WriteObject(config);

        #endregion

        #region Lang
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Title"] = "Players List",
                ["Players"] = "Online Players [{0}/{1}] 🎆\n {2}",
                ["IPAddress"] = "steam://connect/{0}:{1}"

            }, this, "en");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Title"] = "플레이어 목록",
                ["Players"] = "접속중인 플레이어 [{0}/{1}] 🎆\n {2}",
                ["IPAddress"] = "steam://connect/{0}:{1}"
            }, this, "kr");
        }

        private string Lang(string key, params object[] args)
        {
            return string.Format(lang.GetMessage(key, this), args);
        }

        #endregion

        #region Discord
        public Embed ServerStats(string content)
        {
            Embed embed = new Embed
            {
                title = Lang("Title", ConVar.Server.hostname),
                description = content,
                thumbnail = new Embed.Thumbnail
                {
                    url = $"{ConVar.Server.headerimage}"
                },
                footer = new Embed.Footer
                {
                    text = $"Gonzi V{Version}",
                    icon_url = "https://cdn.discordapp.com/avatars/321373026488811520/08f996472c573473e7e30574e0e28da0.png?size=1024"
                },

                color = 15158332
            };
            return embed;
        }
        void Discord_MessageCreate(Message message)
        {
            if (message.author.bot == true) return;


            if (message.content[0] == config.Prefix[0])
            {

                string cmd;
                try
                {
                    cmd = message.content.Split(' ')[0].ToLower();
                    if (string.IsNullOrEmpty(cmd.Trim()))
                        cmd = message.content.Trim().ToLower();
                }
                catch
                {
                    cmd = message.content.Trim().ToLower();
                }

                cmd = cmd.Remove(0, 1);

                cmd = cmd.Trim();
                cmd = cmd.ToLower();

                DiscordCMD(cmd, message);
            }
        }

        private void DiscordCMD(string command, Message message)
        {
            if (config.GroupId != null && !message.member.roles.Contains(config.GroupId)) return;

            switch (command)
            {
                case "players":
                    {
                        string maxplayers = Convert.ToString(ConVar.Server.maxplayers);
                        string onlineplayers = Convert.ToString(BasePlayer.activePlayerList.Count);
                        string list = string.Empty;
                        var playerList = BasePlayer.activePlayerList;
                        foreach (var player in playerList)
                        {
                            list += $"[{player.displayName}](https://steamcommunity.com/profiles/{player.UserIDString}/) \n";
                        }

                        Channel.GetChannel(Client, message.channel_id, channel =>
                        {
                            channel.CreateMessage(Client, ServerStats(Lang("Players", BasePlayer.activePlayerList.Count, ConVar.Server.maxplayers, list)));
                        });
                        break;
                    }
                case "ip":
                    {
                        Channel.GetChannel(Client, message.channel_id, channel =>
                        {
                            webrequest.Enqueue("http://icanhazip.com", "", (code, response) =>
                            {
                                string ip = response.Trim();
                                channel.CreateMessage(Client, Lang("IPAddress", ip, ConVar.Server.port));
                            }, this);
                        });
                    }
                    break;
            }
        }

        #endregion

        #region Oxide Hooks
        private void OnServerInitialized()
        {
            lang.SetServerLanguage("en");

            if (config.BotToken == string.Empty)
                return;

            Discord.CreateClient(this, config.BotToken);

            timer.Every(config.UpdateInterval, () => UpdateStatus());

            timer.Every(901f, () =>
            {
                Reload();
            });
        }
        private void Unload() => Discord.CloseClient(Client);

        private void Reload() => ConsoleSystem.Run(ConsoleSystem.Option.Unrestricted, ($"oxide.reload DiscordStatus"));

        #endregion

        #region Discord Hooks

        #endregion

        #region Status Update
        private void UpdateStatus()
        {
            try
            {
                if (config.Status.Count == 0)
                    return;

                var index = GetStatusIndex();

                Client.UpdateStatus(new Presence()
                {
                    Game = new Ext.Discord.DiscordObjects.Game()
                    {
                        Name = Format(config.Status[index]),
                        Type = GetStatusType()
                    }
                });

                statusIndex = index;
            }
            catch (Exception err)
            {
                LogToFile("DiscordStatus", $"{err}", this);
            }
        }
        #endregion

        #region Helper Methods
        private int GetStatusIndex()
        {
            if (!config.Randomize)
                return (statusIndex + 1) % config.Status.Count;

            var index = 0;
            do index = Random.Range(0, config.Status.Count - 1);
            while (index == statusIndex);

            return index;
        }

        private ActivityType GetStatusType()
        {
            if (!StatusTypes.Contains(config.StatusType))
                PrintError($"Unknown Status Type '{config.StatusType}'");

            switch (config.StatusType)
            {
                case "Game":
                    return ActivityType.Game;
                case "Stream":
                    return ActivityType.Streaming;
                case "Listen":
                    return ActivityType.Listening;
                case "Watch":
                    return ActivityType.Watching;
                default:
                    return default(ActivityType);
            }
        }

        private string Format(string message)
        {
            message = message
                .Replace("{guild.name}", Client?.DiscordServer?.name ?? "{unknown}")
                .Replace("{members.total}", Client?.DiscordServer?.member_count.ToString() ?? "{unknown}")
                .Replace("{channels.total}", Client?.DiscordServer?.channels?.Count.ToString() ?? "{unknown}")
                .Replace("{server.hostname}", server.Name)
                .Replace("{server.maxplayers}", server.MaxPlayers.ToString())
                .Replace("{players.online}", players.Connected.Count().ToString())
                .Replace("{players.authenticated}", DiscordAuth != null ? GetAuthCount().ToString() : "{unknown}");

#if RUST
        message = message
            .Replace("{server.ip}", ConVar.Server.ip)
            .Replace("{server.port}", ConVar.Server.port.ToString())
            .Replace("{server.entities}", BaseNetworkable.serverEntities.Count.ToString())
            .Replace("{server.worldsize}", ConVar.Server.worldsize.ToString())
            .Replace("{server.seed}", ConVar.Server.seed.ToString())
            .Replace("{server.fps}", Performance.current.frameRate.ToString())
            .Replace("{server.avgfps}", Convert.ToInt32(Performance.current.frameRateAverage).ToString())
            .Replace("{players.queued}", ConVar.Admin.ServerInfo().Queued.ToString())
            .Replace("{players.joining}", ConVar.Admin.ServerInfo().Joining.ToString())
            .Replace("{players.sleepers}", BasePlayer.sleepingPlayerList.Count.ToString())
            .Replace("{players.total}", (players.Connected.Count() + BasePlayer.sleepingPlayerList.Count).ToString());
#endif

            return message;
        }

        private int GetAuthCount() => (int)DiscordAuth.Call("API_GetAuthCount");

        #endregion
    }
}
