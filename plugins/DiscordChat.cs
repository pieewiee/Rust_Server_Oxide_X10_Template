using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Ext.Discord.DiscordObjects;
using DiscordUser = Oxide.Ext.Discord.DiscordObjects.User;

#if RUST
using ConVar;
#endif

namespace Oxide.Plugins
{
    [Info("Discord Chat", "MJSU", "1.0.8")]
    [Description("Allows chatting through discord")]
    internal class DiscordChat : CovalencePlugin
    {
        #region Class Fields

        [PluginReference] private Plugin AdminChat, AdminDeepCover, BetterChat, BetterChatMute, DiscordCore, Clans, ChatTranslator, TranslationAPI;

        private StoredData _storedData; //Plugin Data
        private PluginConfig _pluginConfig;
        
        private DiscordUser _bot;

        private const string GetMessagesKey = "DiscordChat_ChatMessages";
        private bool _init;

        private StringBuilder _discordChat = new StringBuilder();
        private Timer _sendTimer;
        
        private Timer _joinLeaveTimer;
        private StringBuilder _joinLeaveMessage = new StringBuilder();
        
#if RUST
        private StringBuilder _teamChat = new StringBuilder();
        private Timer _teamTimer;
#endif

        private enum MessageSource : byte
        {
            Server,
            Discord
        }

        #endregion

        #region Setup & Loading
        private void Init()
        {
            _storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
        }
        
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [LangKeys.NotLinked] = "You have not yet linked your discord to the rust server. Please respond to this message with /join to begin the link.",
                [LangKeys.Joined] = "({0:HH:mm}) {1} has joined.",
                [LangKeys.Disconnected] = "({0:HH:mm}) {1} has disconnected. Reason: {2}",
                [LangKeys.ChatMessage] = "({0:HH:mm}) {1}: {2}",
                [LangKeys.BetterChatMessage] = "({0:HH:mm}) {1}",
                [LangKeys.TeamChatMessage] = "({0:HH:mm}) {1}: {2}",
                [LangKeys.BetterChatTeamMessage] = "({0:HH:mm}) {1}"
            }, this);
        }
        
        protected override void LoadDefaultConfig()
        {
            PrintWarning("Loading Default Config");
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Config.Settings.DefaultValueHandling = DefaultValueHandling.Populate;
            _pluginConfig = AdditionalConfig(Config.ReadObject<PluginConfig>());
            Config.WriteObject(_pluginConfig);
        }

        private PluginConfig AdditionalConfig(PluginConfig config)
        {
            config.PluginSupport = new PluginSupport
            {
                AdminChat = new AdminChatSettings
                {
                    ChatChannel = config.PluginSupport?.AdminChat?.ChatChannel ?? "admin-chat",
                    AdminChatPrefix = config.PluginSupport?.AdminChat?.AdminChatPrefix ?? "@",
                    ExcludeDefault = config.PluginSupport?.AdminChat?.ExcludeDefault ?? true,
                    Enabled = config.PluginSupport?.AdminChat?.Enabled ?? false
                },
                ChatTranslator = new ChatTranslatorSettings
                {
                    DiscordServerLanguage = config.PluginSupport?.ChatTranslator?.DiscordServerLanguage ?? lang.GetServerLanguage()
                }
            };

            config.TextFilter = config.TextFilter ?? new Hash<string, string>
            {
                ["@everyone"] = "everyone",
                ["@here"] = "here"
            };

            config.IgnoredCommands = config.IgnoredCommands ?? new List<string>
            {
                "!rust"
            };

            config.IgnoreUsers = config.IgnoreUsers ?? new List<string>();

            return config;
        }

        private void OnServerInitialized()
        {
            if (DiscordCore == null)
            {
                PrintError("Missing plugin dependency DiscordCore: https://umod.org/plugins/discord-core");
                return;
            }

            if (_pluginConfig.EnableDiscordTag)
            {
                BetterChat?.Call("API_RegisterThirdPartyTitle", this, new Func<IPlayer, string>(GetDiscordTag));
            }
            
            OnDiscordCoreReady();
        }

        private void OnDiscordCoreReady()
        {
            if (!(DiscordCore?.Call<bool>("IsReady") ?? false))
            {
                return;
            }

            if (_pluginConfig.DiscordToServer && !string.IsNullOrEmpty(_pluginConfig.ChatChannel))
            {
                DiscordCore.Call("SubscribeChannel", _pluginConfig.ChatChannel, this, new Func<Message, object>(HandleMessage));
            }
            
            _bot = DiscordCore.Call<DiscordUser>("GetBot");
            if (!_storedData.BotIds.Contains(_bot.id))
            {
                _storedData.BotIds.Add(_bot.id);
                SaveData();
            }

            if (_pluginConfig.PluginSupport.AdminChat.Enabled)
            {
                DiscordCore.Call("SubscribeChannel", _pluginConfig.PluginSupport.AdminChat.ChatChannel, this, new Func<Message, object>(HandleAdminChatMessage));
            }

            _init = true;
        }
        
        private void Unload()
        {
            if (_pluginConfig.DiscordToServer)
            {
                DiscordCore?.Call("UnsubscribeChannel", _pluginConfig.ChatChannel, this);
            }
        }

        #endregion

        #region Oxide Hook


#if RUST
        private void OnPlayerChat(BasePlayer rustPlayer, string message, Chat.ChatChannel chatChannel)
        {
            IPlayer player = rustPlayer.IPlayer;
            int channel = (int) chatChannel;

#else
        private void OnUserChat(IPlayer player, string message)
        {
            int channel = 0;
#endif
            if (!_init)
            {
                return;
            }
            
            if (BetterChatMute?.Call<bool>("API_IsMuted", player) ?? false)
            {
                return;
            }

            message = FilterText(message);
            
            if (HandlePluginSupport(player, message, MessageSource.Server, channel))
            {
                return;
            }

            HandleChatMessage(player, message, channel);
        }

        private void HandleChatMessage(IPlayer player, string message, int channel)
        {
            
#if RUST
            if (channel == (int)Chat.ChatChannel.Team)
            {
                SendPlayerMessageToTeamChannel(player, message);
            }
            else
            {
                if (!_pluginConfig.ServerToDiscord)
                {
                    return;
                }
                
                SendPlayerMessageToDefaultChannel(player, message);
            }
#else
            if (!_pluginConfig.ServerToDiscord)
            {
                return;
            }      
      
            SendPlayerMessageToDefaultChannel(player, message);
#endif
        }
        
        private void OnUserConnected(IPlayer player)
        {
            if (!_init)
            {
                return;
            }

            SendJoinMessage(player);
        }

        private void OnUserDisconnected(IPlayer player, string reason)
        {
            if (!_init)
            {
                return;
            }

            SendLeaveMessage(player, reason);
        }

        #endregion

        #region Discord Chat Handling

        private void OnChannelSubscribed(Channel channel, Plugin plugin)
        {
            if (this != plugin)
            {
                return;
            }

            DiscordCore.Call("GetChannelMessages", channel.id, GetMessagesKey);
        }

        private void OnGetChannelMessages(List<Message> messages, string responseKey)
        {
            if (responseKey != GetMessagesKey)
            {
                return;
            }

            if (_pluginConfig.UseBotMessageDisplay)
            {
                int i = 0;
                foreach (Message message in messages.Where(m => !ShouldIgnoreUser(m.author) && !_storedData.BotIds.Contains(m.author.id)))
                {
                    timer.In(i, () =>
                    {
                        DiscordCore.Call("DeleteMessage", message);
                    });
                    i++;
                }
            }
        }

        private object HandleMessage(Message message)
        {
            if (!_pluginConfig.DiscordToServer)
            {
                return null;
            }

            if (ShouldIgnoreUser(message.author))
            {
                return null;
            }
            
            if (_pluginConfig.IgnoredCommands.Any(c => message.content.StartsWith(c, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            if (message.author.id == _bot.id)
            {
                return null;
            }
            
            string playerId = DiscordCore.Call<string>("GetSteamIdFromDiscordId", message.author.id);
            if (playerId == null)
            {
                SendMessageToUser(message.author.id, Lang(LangKeys.NotLinked));
            }
            else
            {
                IPlayer player = covalence.Players.FindPlayer(playerId);
                if (player == null)
                {
                    PrintError($"{playerId} is linked in DiscordCore but does not exist in covalence player list");
                    return null;
                }
                
                //Attempt to fix random mass disconnect issues
                NextTick(() =>
                {
                    BroadcastChat(player, FilterText(message.content));
                }); 
            }

            if (_pluginConfig.UseBotMessageDisplay)
            {
                timer.In(.25f, () => { DiscordCore.Call("DeleteMessage", message); });
            }

            return null;
        }

        private object HandleAdminChatMessage(Message message)
        {
            if (_pluginConfig.IgnoredCommands.Any(c => message.content.StartsWith(c, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
            
            string playerId = DiscordCore.Call<string>("GetSteamIdFromDiscordId", message.author.id);
            if (playerId == null)
            {
                SendMessageToUser(message.author.id, Lang(LangKeys.NotLinked));
            }
            else
            {
                IPlayer player = covalence.Players.FindPlayer(playerId);
                HandleAdminChat(player, message.content, MessageSource.Discord);
            }

            timer.In(.25f, () => { DiscordCore.Call("DeleteMessage", message); });

            return null;
        }

        private void BroadcastChat(IPlayer player, string message)
        {
            if (IsInAdminDeepCover(player))
            {
                return;
            }

            if (_pluginConfig.UseBotMessageDisplay)
            {
                if (ChatTranslator != null && ChatTranslator.IsLoaded)
                {
                    HandleTranslate(message, _pluginConfig.PluginSupport.ChatTranslator.DiscordServerLanguage, lang.GetLanguage(player.Id),
                        translatedText =>
                        {
                            SendPlayerMessageToDefaultChannel(player, translatedText);
                        });
                }
                else
                {
                    SendPlayerMessageToDefaultChannel(player, message);
                }
            }
            
            bool playerReturn = false;
#if RUST
            //Let other chat plugins process first
            if (player.Object != null)
            {
                Unsubscribe(nameof(OnPlayerChat));
                playerReturn = Interface.Call(nameof(OnPlayerChat), player.Object, message, Chat.ChatChannel.Global) != null;
                Subscribe(nameof(OnPlayerChat));
            }
#endif
            
            //Let other chat plugins process first
            Unsubscribe("OnUserChat");
            bool userReturn = Interface.Call("OnUserChat", player, message) != null;
            Subscribe("OnUserChat");
            
            if (playerReturn || userReturn)
            {
                return;
            }

            if (BetterChat != null)
            {
                string betterMessage = GetBetterChatMessage(player, message);
                foreach (IPlayer connected in players.Connected)
                {
                    connected.Message(betterMessage);
                }
                
                Puts(GetBetterChatConsoleMessage(player, message));
                return;
            }

            message = $"{player.Name}: {message}";
            foreach (IPlayer connected in players.Connected)
            {
                connected.Message(message);
            }
            Puts(message);
        }

        #endregion

        #region BetterChat Tag

        private string GetDiscordTag(IPlayer player)
        {
            if (player.IsConnected)
            {
                return null;
            }

            return $"[#{_pluginConfig.DiscordTagColor}][{_pluginConfig.DiscordTagText}][/#]";
        }
        #endregion

        #region Plugin Support

        private bool HandlePluginSupport(IPlayer player, string message, MessageSource type, int channel = 0)
        {
            if (_pluginConfig.PluginSupport.AdminChat.Enabled)
            {
                string adminChatPrefix = _pluginConfig.PluginSupport.AdminChat.AdminChatPrefix;
                if (message.StartsWith(adminChatPrefix))
                {
                    HandleAdminChat(player, message.Substring(adminChatPrefix.Length), type);
                    return _pluginConfig.PluginSupport.AdminChat.ExcludeDefault;
                }

                if (IsInAdminChat(player))
                {
                    HandleAdminChat(player, message, type);
                    return _pluginConfig.PluginSupport.AdminChat.ExcludeDefault;
                }
            }

            if (ChatTranslator != null  && ChatTranslator.IsLoaded)
            {
                HandleTranslate(message, _pluginConfig.PluginSupport.ChatTranslator.DiscordServerLanguage, lang.GetLanguage(player.Id), translatedMessage =>
                {
                    HandleChatMessage(player, translatedMessage, channel);
                });
                return true;
            }

            if (IsInAdminDeepCover(player) && channel != 1)
            {
                return true;
            }
            
            return false;
        }

        private void HandleTranslate(string message, string to, string from, Action<string> callback)
        {
            TranslationAPI.Call("Translate", message, to, from, callback);
        }

        private bool IsInAdminChat(IPlayer player)
        {
            return AdminChat?.Call<bool>("HasAdminChatEnabled", player) ?? false;
        }

        private void HandleAdminChat(IPlayer player, string message, MessageSource source)
        {
            if (source == MessageSource.Server)
            {
                SendChatToChannel(_pluginConfig.PluginSupport.AdminChat.ChatChannel, message);
            }
            else
            {
                AdminChat.Call("SendAdminMessage", player, message);
            }
        }
        #endregion

        #region Helpers
        private DateTime GetServerTime()
        {
            return DateTime.Now + TimeSpan.FromHours(_pluginConfig.ServerTimeOffset);
        }

        private string FilterText(string message)
        {
            foreach (KeyValuePair<string, string> replacement in _pluginConfig.TextFilter)
            {
                message = message.Replace(replacement.Key, replacement.Value);
            }

            return message;
        }

        private void SendMessageToUser(string id, string message)
        {
            DiscordCore.Call("SendMessageToUser", id, message);
        }

        private void SendPlayerMessageToDefaultChannel(IPlayer player, string message)
        {
            if (string.IsNullOrEmpty(_pluginConfig.ChatChannel))
            {
                return;
            }
            
            if (_sendTimer == null)
            {
                _sendTimer = timer.In(1f, () =>
                {
                    SendChatToChannel(_pluginConfig.ChatChannel, _discordChat.ToString());
                    _discordChat = new StringBuilder();
                    _sendTimer = null;
                });
            }
            
            if (BetterChat != null)
            {
                message = GetBetterChatConsoleMessage(player, message);
                _discordChat.AppendLine(Lang(LangKeys.BetterChatMessage, player, GetServerTime(), message));
            }
            else
            {
                _discordChat.AppendLine(Lang(LangKeys.ChatMessage, player, GetServerTime(), GetPlayerName(player), message));
            }
        }
        
#if RUST
        private void SendPlayerMessageToTeamChannel(IPlayer player, string message)
        {
            if (string.IsNullOrEmpty(_pluginConfig.TeamChannel))
            {
                return;
            }
            
            if (_teamTimer == null)
            {
                _teamTimer = timer.In(1f, () =>
                {
                    SendChatToChannel(_pluginConfig.TeamChannel, _teamChat.ToString());
                    _teamChat = new StringBuilder();
                    _teamTimer = null;
                });
            }

            if (BetterChat != null)
            {
                message = GetBetterChatConsoleMessage(player, message);
                _teamChat.AppendLine(Lang(LangKeys.BetterChatTeamMessage, player, GetServerTime(), message));
            }
            else
            {
                _teamChat.AppendLine(Lang(LangKeys.TeamChatMessage, player, GetServerTime(), GetPlayerName(player), message));
            }
        }
#endif

        private void SendJoinMessage(IPlayer player)
        {
            if (_joinLeaveTimer == null)
            {
                _joinLeaveTimer = timer.In(1f, () =>
                {
                    SendChatToChannel(_pluginConfig.JoinLeaveChannel, _joinLeaveMessage.ToString());
                    _joinLeaveMessage = new StringBuilder();
                    _joinLeaveTimer = null;
                });
            }

            _joinLeaveMessage.AppendLine(Lang(LangKeys.Joined, player, GetServerTime(), GetPlayerName(player)));
        }
        
        private void SendLeaveMessage(IPlayer player, string reason)
        {
            if (_joinLeaveTimer == null)
            {
                _joinLeaveTimer = timer.In(1f, () =>
                {
                    SendChatToChannel(_pluginConfig.JoinLeaveChannel, _joinLeaveMessage.ToString());
                    _joinLeaveMessage = new StringBuilder();
                    _joinLeaveTimer = null;
                });
            }

            _joinLeaveMessage.AppendLine(Lang(LangKeys.Disconnected, player, GetServerTime(), GetPlayerName(player), reason));
        }

        private void SendChatToChannel(string channel, string message)
        {
            if (string.IsNullOrEmpty(channel))
            {
                return;
            }
            
            DiscordCore.Call("SendMessageToChannel", channel, message);
        }

        private string GetPlayerName(IPlayer player)
        {
            string clanTag = Clans?.Call<string>("GetClanOf", player);
            if (!string.IsNullOrEmpty(clanTag))
            {
                clanTag = $"[{clanTag}] ";
            }

            return $"{clanTag}{player.Name}";
        }

        public bool ShouldIgnoreUser(DiscordUser user)
        {
            return _pluginConfig.IgnoreUsers.Contains(user.id);
        }

        private bool IsAdminDeepCoverLoaded() => AdminDeepCover != null && AdminDeepCover.IsLoaded;

        private bool IsInAdminDeepCover(IPlayer player) => IsAdminDeepCoverLoaded() && player.Object != null && AdminDeepCover.Call<bool>("API_IsDeepCovered", player.Object);

        private string GetBetterChatConsoleMessage(IPlayer player, string message)
        {
            return BetterChat.Call<string>("API_GetFormattedMessage", player, message, true);
        }
        
        private string GetBetterChatMessage(IPlayer player, string message)
        {
            return BetterChat.Call<string>("API_GetFormattedMessage", player, message, false);
        }

        public void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _storedData);
        
        private string Lang(string key, IPlayer player = null, params object[] args) => string.Format(lang.GetMessage(key, this, player?.Id), args);

        #endregion

        #region Classes
        private class PluginConfig
        {
            [DefaultValue("Chat")]
            [JsonProperty("Chat Channel name or id")]
            public string ChatChannel { get; set; }

#if RUST
            [DefaultValue("Team")]
            [JsonProperty("Team Channel name or id")]
            public string TeamChannel { get; set; }    
#endif
            
            [DefaultValue("Chat")]
            [JsonProperty("Join / Leave Channel name or id")]
            public string JoinLeaveChannel { get; set; }
            
            [DefaultValue(0f)]
            [JsonProperty("Server Time Offset (Hours)")]
            public float ServerTimeOffset { get; set; }
            
            [DefaultValue(true)]
            [JsonProperty("Add discord tag to chat")]
            public bool EnableDiscordTag { get; set; }
            
            [DefaultValue(true)]
            [JsonProperty("Use bot to display message")]
            public bool UseBotMessageDisplay { get; set; }
            
            [DefaultValue(true)]
            [JsonProperty("Send messages from server to discord")]
            public bool ServerToDiscord { get; set; }
            
            [DefaultValue(true)]
            [JsonProperty("Send messages from discord to server")]
            public bool DiscordToServer { get; set; }
            
            [DefaultValue("Discord")]
            [JsonProperty("Discord tag text")]
            public string DiscordTagText { get; set; }
            
            [DefaultValue("5f79d6")]
            [JsonProperty("Discord tag color")]
            public string DiscordTagColor { get; set; }

            [JsonProperty("Ignore messages from users in this list (Discord ID)")]
            public List<string> IgnoreUsers { get; set; }
            
            [JsonProperty("Text Filter")]
            public Hash<string, string> TextFilter { get; set; }
            
            [JsonProperty("Ignored Commands")]
            public List<string> IgnoredCommands { get; set; }
            
            [JsonProperty("Plugin Support")]
            public PluginSupport PluginSupport { get; set; }
        }

        private class StoredData
        {
            public List<string> BotIds = new List<string>();
        }

        private class PluginSupport
        {
            [JsonProperty("AdminChat Settings")]

            public AdminChatSettings AdminChat { get; set; }
            
            [JsonProperty("ChatTranslator Settings")]

            public ChatTranslatorSettings ChatTranslator { get; set; }
        }

        private class AdminChatSettings
        {
            [JsonProperty("Exclude from chat channel")]
            public bool ExcludeDefault { get; set; }

            [JsonProperty("Admin chat channel")]
            public string ChatChannel { get; set; }

            [JsonProperty("Admin Chat Prefix")]
            public string AdminChatPrefix { get; set; }

            [JsonProperty("Enable AdminChat Plugin Support")]
            public bool Enabled { get; set; }
        }
        
        private class ChatTranslatorSettings
        {
            [JsonProperty("Discord Server Chat Language")]
            public string DiscordServerLanguage { get; set;}
        }

        private static class LangKeys
        {
            public const string NotLinked = "NotLinked";
            public const string Joined = "Joined";
            public const string Disconnected = "Disconnected";
            public const string ChatMessage = "ChatMessage";
            public const string TeamChatMessage = "TeamChatMessage";
            public const string BetterChatMessage = "BetterChatMessage";
            public const string BetterChatTeamMessage = "BetterChatTeamMessage";
        }

        #endregion
    }
}