using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Ext.Discord;
using Oxide.Ext.Discord.Attributes;
using Oxide.Ext.Discord.DiscordEvents;
using Oxide.Ext.Discord.DiscordObjects;
using DiscordUser = Oxide.Ext.Discord.DiscordObjects.User;
using Random = Oxide.Core.Random;

 namespace Oxide.Plugins
{
    [Info("Discord Core", "MJSU", "0.17.0")]
    [Description("Creates a link between a player and discord")]
    internal class DiscordCore : CovalencePlugin
    {
        #region Class Fields
        [PluginReference] private Plugin DiscordAuth, DiscordConnect;
        [DiscordClient] private DiscordClient _client;
        
        private StoredData _storedData; //Plugin Data
        private PluginConfig _pluginConfig; //Plugin Config
        
        private const string AccentColor = "de8732";
        private const string UsePermission = "discordcore.use";
        private const string PluginsPermission = "discordcore.plugins";

        private readonly Hash<string, DiscordCommand> _discordCommands = new Hash<string, DiscordCommand>();
        private readonly Hash<string, List<ChannelSubscription>> _channelSubscriptions = new Hash<string, List<ChannelSubscription>>();

        private readonly List<DiscordActivation> _pendingDiscordActivations = new List<DiscordActivation>();
        private readonly Hash<string, InGameActivation> _pendingInGameActivation = new Hash<string, InGameActivation>();

        private readonly List<Plugin> _hookPlugins = new List<Plugin>();
        private readonly Hash<string, string> _playerDmChannel = new Hash<string, string>();
        private readonly Hash<string, Channel> _channelCache = new Hash<string, Channel>();

        private DiscordUser _bot;
        private Guild _discordServer;
        
        private readonly List<GuildMember> _chunkUsers = new List<GuildMember>();
        private Timer _chunkLoading;

        private enum ConnectionStateEnum : byte { Disconnected, Connecting, Connected }
        private ConnectionStateEnum _connectionState = ConnectionStateEnum.Disconnected;
        private DateTime _lastUpdate;
        private bool _initialized;
        private Timer _connectionTimeout;
        private int _guildCount;
        private bool _useExternal;
        private DiscordSettings _discordSettings;
        #endregion

        #region Setup & Loading
        private void Init()
        {
            _storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);

            permission.RegisterPermission(UsePermission, this);
            permission.RegisterPermission(PluginsPermission, this);
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Loading Default Config");
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Config.Settings.DefaultValueHandling = DefaultValueHandling.Populate;
            _pluginConfig = Config.ReadObject<PluginConfig>();
            Config.WriteObject(_pluginConfig);
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [LangKeys.NoPermission] = "You do not have permission to use this command",
                [LangKeys.ChatFormat] = $"[#BEBEBE][[#{AccentColor}]{Title}[/#]] {{0}}[/#]",
                [LangKeys.UnknownCommand] = "Unknown Command",
                [LangKeys.DiscordLeave] = "You have removed your discord bot connection",
                [LangKeys.DiscordLeaveFailed] = "You are not subscribed to discord and were not removed",
                [LangKeys.JoinAlreadySignedUp] = $"You're already signed up for discord. If you want to remove yourself from discord type [#{AccentColor}]/{{0}} leave[/#]",
                [LangKeys.JoinInvalidSyntax] = $"Invalid Syntax. Ex. [#{AccentColor}]/{{0}} join name#1234[/#] to sign up for discord",
                [LangKeys.JoinUnableToFindUser] = $"Unable to find user '{{0}}' in the {{1}} discord. Have you joined the {{1}} discord server @ [#{AccentColor}]discord.gg/{{2}}[/#]?",
                [LangKeys.JoinReceivedPm] = $"You have received a PM from the {{0}}. Please respond to the bot with your code to complete the discord activation.\n[#{AccentColor}]{{1}}[/#]",
                [LangKeys.JoinPleaseEnterCode] = "Please enter your code here to complete activation.",
                [LangKeys.DiscordJoinInvalidSyntax] = $"Invalid syntax. Type [#{AccentColor}]/{{0}} code 123456[/#] where 123456 is the code you got from discord",
                [LangKeys.DiscordJoinNoPendingActivations] = "You either don't have a pending activation or your code is invalid",
                [LangKeys.DiscordJoinSuccessfullyRegistered] = "You have successfully registered your discord with the server",
                [LangKeys.DiscordCommandsPreText] = "Available Commands:",
                [LangKeys.DiscordCommandsBotChannelAllowed] = "(Bot Channel Allowed)",
                [LangKeys.DiscordPlugins] = "List of Discord enabled plugins:\n",
                [LangKeys.DiscordJoinWrongChannel] = "Please use /{0} in this private message and not to any discord server channels",
                [LangKeys.DiscordCoreLinkingNotEnabled] = "DiscordCore linking is not enabled. Please use the external link system.",
                [LangKeys.DiscordNotAllowedBotChannel] = "This command is not allowed in the bot channel and can only be used here.",
                [LangKeys.DiscordMustSignUpBeforeCommands] = "You need to sign up for discord before you can start using commands. To begin type /{0}",
                [LangKeys.DiscordPlayerIsServerConsole] = "You have linked yourself as server console. This is not allowed. Please unlink and link with your steam account.",
                [LangKeys.DiscordMustSignUpBeforeBotCommands] = "You need to sign up for discord before you can use this command in the bot channel. To begin type /{0}",
                [LangKeys.DiscordClientNotConnected] = "The discord client is not connected. Please contact an admin.",
                [LangKeys.GameJoinCodeNotMatch] = "Your activation code does not match! Please reply with only the code.",
                [LangKeys.DiscordCompleteActivation] = "To complete your discord activation in the server chat please type /{0} code {1}",
                [LangKeys.ConsolePlayerNotSupported] = "You cannot use this command from the server console.",
                [LangKeys.DiscordAuthNotLinked] = "You're not linked with DiscordAuth please authenticate and try again",
                [LangKeys.DiscordConnectNotLinked] = "You're not linked with DiscordConnect please authenticate and try again",
                [LangKeys.DiscordHooksNotLinked] = "You're not linked with this servers discord authentication please authenticate and try again",
                [LangKeys.LinkNotification] = "Player {0}({1}) has linked with discord {2}({3})",
                [LangKeys.RejoinNotification] = "Player {0}({1}) has rejoined and was linked with discord {2}({3})",
                [LangKeys.UnlinkNotification] = "Player {0}({1}) has unlinked their discord {2}({3})",

                [LangKeys.DiscordUserJoin] = "If you would like to link your discord account with the {0} server respond to this message with /{1}. " +
                                      "This provides you with a bot which gives you access to commands while not on the server. Once you sign up type /{2} to learn more",
                [LangKeys.NotLinked] = "You're not linked with the external link system. Please link before using this command.\n",
                [LangKeys.HelpNotLinked] = "Discord: Allows communication between a server and discord.\n" +
                               "Type /{0} - (In a private message to the bot) - to start linking your server player and discord together\n" +
                               "Type /{1} - to see this message again",
                [LangKeys.HelpLinked] = "Discord: Allows communication between a server and discord.\n" +
                                  "Type /{0} - (In a private message to the bot) - to see the list of available commands\n" +
                                  "Type /{1} - (In a private message to the bot) - to unlink your discord from the game server\n" +
                                  "Type /{2} - to see this message again",

                [LangKeys.HelpText] = $"Allows players to link their player and discord accounts together. Players must first join the {{0}} Discord @ [#{AccentColor}]discord.gg/{{1}}[/#]\n" +
                                      $"Type [#{AccentColor}]/{{2}} {{3}} discordusername#discorduserid[/#] to start the activation of discord\n" +
                                      $"Type [#{AccentColor}]/{{2}} {{4}}[/#] to remove yourself from discord\n" +
                                      $"Type [#{AccentColor}]/{{2}}[/#] to see this message again"
            }, this);
        }
        
        private void OnServerInitialized()
        {
            _discordSettings = new DiscordSettings
            {
                ApiToken = _pluginConfig.DiscordApiKey,
                Debugging = _pluginConfig.ExtensionDebugging
            };
            
            if (_pluginConfig.UseDiscordAuth && DiscordAuth == null)
            {
                PrintWarning("Use DiscordAuth enabled but DiscordAuth not found.");
                _pluginConfig.UseDiscordAuth = false;
            }
            
            if (_pluginConfig.UseDiscordConnect && DiscordConnect == null)
            {
                PrintWarning("Use DiscordConnect enabled but DiscordConnect not found.");
                _pluginConfig.UseDiscordConnect = false;
            }
            
            if (_pluginConfig.UseDiscordAuth || _pluginConfig.UseDiscordConnect || _pluginConfig.UseHooks)
            {
                _useExternal = true;
                _pluginConfig.EnableLinking = false;
            }
            
            if (_pluginConfig.EnableLinking && !_useExternal)
            {
                if (!string.IsNullOrEmpty(_pluginConfig.GameChatCommand))
                {
                    AddCovalenceCommand(_pluginConfig.GameChatCommand, nameof(DiscordChatCommand));
                }
                
                RegisterCommand(_pluginConfig.LeaveCommand, this, HandleLeave, "To unlink your player account from discord");
            }

            if (!string.IsNullOrEmpty(_pluginConfig.HelpCommand))
            {
                RegisterCommand(_pluginConfig.HelpCommand, this, HandleHelp, "To view the discord bot help text", null, true);
            }

            if (!string.IsNullOrEmpty(_pluginConfig.CommandsCommand))
            {
                RegisterCommand(_pluginConfig.CommandsCommand, this, HandleCommands, "To view the list of available bot commands");
            }

            if (!string.IsNullOrEmpty(_pluginConfig.PluginsCommand))
            {
                RegisterCommand(_pluginConfig.PluginsCommand, this, HandlePlugins, "To see a list of discord enabled plugins", PluginsPermission);
            }

            ConnectClient();
            
            _lastUpdate = DateTime.Now;
            timer.Every(60, () =>
            {
                double duration = (DateTime.Now - _lastUpdate).TotalSeconds;
                if (_connectionState != ConnectionStateEnum.Connecting && duration > _pluginConfig.HeartbeatTimeoutDuration)
                {
                    PrintWarning($"Heartbeat timed out ({duration}s). Reconnecting");
                    CloseClient();
                    ConnectClient();
                }
            });
        }

        private void Unload()
        {
            CloseClient();
        }

        public void ConnectClient()
        {
            if (string.IsNullOrEmpty(_pluginConfig.DiscordApiKey))
            {
                PrintWarning("Please enter your discord bot API key and reload the plugin");
                return;
            }
            
            _lastUpdate = DateTime.Now;
            _connectionState = ConnectionStateEnum.Connecting;
            
            timer.In(1f, () =>
            {
                Discord.CreateClient(this, _discordSettings); // Create a new DiscordClient

                timer.In(60f, () =>
                {
                    if (_connectionState == ConnectionStateEnum.Connecting)
                    {
                        _connectionState = ConnectionStateEnum.Disconnected;
                    }
                });
            });
        }

        public void CloseClient()
        {
            _connectionState = ConnectionStateEnum.Disconnected;
            Interface.Oxide.CallHook("OnDiscordCoreClose");
            Discord.CloseClient(_client);
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            foreach (KeyValuePair<string, DiscordCommand> commands in _discordCommands.Where(dc => dc.Value.PluginName == plugin.Name).ToList())
            {
                UnregisterCommand(commands.Key, plugin);
            }

            foreach (KeyValuePair<string, List<ChannelSubscription>> channelSubscription in _channelSubscriptions)
            {
                if (channelSubscription.Value.Any(cs => cs.PluginName == plugin.Name))
                {
                    UnsubscribeChannel(channelSubscription.Key, plugin);
                }
            }

            if (_hookPlugins.Contains(plugin))
            {
                _hookPlugins.Remove(plugin);
                _client.Plugins.Remove(plugin);
            }
        }
        #endregion

        #region Chat Commands
        private void DiscordChatCommand(IPlayer player, string cmd, string[] args)
        {
            if (player.Id == "server_console")
            {
                Chat(player, Lang(LangKeys.NoPermission, player));
                return;
            }
            
            if (!player.HasPermission(UsePermission) && !player.IsAdmin)
            {
                Chat(player, Lang(LangKeys.NoPermission, player));
                return;
            }

            if (args.Length == 0)
            {
                DisplayHelp(player);
                return;
            }

            string option = args[0].ToLower();
            if (option == _pluginConfig.InGameJoinCommand.ToLower())
            {
                HandleJoin(player, args);
            }
            else if (option == _pluginConfig.InGameLeaveCommand.ToLower())
            {
                if (_storedData.PlayerDiscordInfo.ContainsKey(player.Id))
                {
                    HandleLeave(player, false);
                    Chat(player, Lang(LangKeys.DiscordLeave, player));
                }
                else
                {
                    Chat(player, Lang(LangKeys.DiscordLeaveFailed, player));
                }
            }
            else if (option == "code")
            {
                HandleDiscordJoinCode(player, args);
            }
            else
            {
                DisplayHelp(player);
            }
        }

        public void DisplayHelp(IPlayer player)
        {
            Chat(player, Lang(LangKeys.HelpText, player, GetDiscordName(), _pluginConfig.JoinCode, _pluginConfig.GameChatCommand, _pluginConfig.InGameJoinCommand, _pluginConfig.InGameLeaveCommand));
        }

        public void HandleJoin(IPlayer player, string[] args)
        {
            if (_storedData.PlayerDiscordInfo.ContainsKey(player.Id))
            {
                Chat(player, Lang(LangKeys.JoinAlreadySignedUp, player, _pluginConfig.GameChatCommand));
                return;
            }

            if (args.Length < 2)
            {
                Chat(player, Lang(LangKeys.JoinInvalidSyntax, player, _pluginConfig.GameChatCommand));
                return;
            }

            if (_client == null)
            {
                Chat(player, Lang(LangKeys.DiscordClientNotConnected, player));
                return;
            }

            DiscordUser user = GetUserByUsername(args[1]);
            if (user == null)
            {
                Chat(player, Lang(LangKeys.JoinUnableToFindUser, player, args[1], GetDiscordName(), _pluginConfig.JoinCode));
                return;
            }

            if (_storedData.PlayerDiscordInfo.Values.Any(di => di.DiscordId == user.id))
            {
                Chat(player, Lang(LangKeys.JoinAlreadySignedUp, player, _pluginConfig.GameChatCommand));
                return;
            }

            string code = GenerateCode();
            _pendingInGameActivation[user.id] = new InGameActivation
            {
                Code = code,
                PlayerId = player.Id
            };

            Chat(player, Lang(LangKeys.JoinReceivedPm, player, _bot.username, code));
            CreateChannelActivation(player, user, code, Lang(LangKeys.JoinPleaseEnterCode, player));
        }

        public void HandleDiscordJoinCode(IPlayer player, string[] args)
        {
            if (_storedData.PlayerDiscordInfo.ContainsKey(player.Id))
            {
                Chat(player, Lang(LangKeys.JoinAlreadySignedUp, player, _pluginConfig.GameChatCommand));
                return;
            }

            if (args.Length < 2)
            {
                Chat(player, Lang(LangKeys.DiscordJoinInvalidSyntax, player, _pluginConfig.GameChatCommand));
                return;
            }

            DiscordActivation act = _pendingDiscordActivations.FirstOrDefault(a => a.Code == args[1]);
            if (act == null)
            {
                Chat(player, Lang(LangKeys.DiscordJoinNoPendingActivations, player));
                return;
            }

            _storedData.PlayerDiscordInfo[player.Id] = new DiscordInfo
            {
                PlayerId = player.Id,
                DiscordId = act.DiscordId,
            };

            string name = _discordServer.members.FirstOrDefault(m => m.user.id == act.DiscordId)?.user?.username;

            SaveData();
            SendMessageToUser(player.Id, Lang(LangKeys.DiscordJoinSuccessfullyRegistered, player));
            Chat(player, Lang(LangKeys.DiscordJoinSuccessfullyRegistered, player));
            SendMessageToChannel(_pluginConfig.LinkUnlinkAnnouncementChannel, Lang(LangKeys.LinkNotification, null, player.Name, player.Id, name, act.DiscordId));
            Interface.Call("OnDiscordCoreJoin", player, act.DiscordId);
        }
        #endregion

        #region Internal Discord Commands
        public object HandleHelp(IPlayer player, string channelId, string cmd, string[] args)
        {
            string message;
            if (player == null)
            {
                if (_pluginConfig.UseDiscordAuth)
                {
                    message = Lang(LangKeys.DiscordAuthNotLinked);
                }
                else if (_pluginConfig.UseDiscordConnect)
                {
                    message = Lang(LangKeys.DiscordConnectNotLinked);
                }
                else if (_pluginConfig.UseHooks)
                {
                    message = Lang(LangKeys.DiscordHooksNotLinked);
                }
                else
                {
                    message = Lang(LangKeys.HelpNotLinked, null, _pluginConfig.DiscordJoinCommand, _pluginConfig.HelpCommand);
                }
            }
            else
            {
                message = Lang(LangKeys.HelpLinked, player, _pluginConfig.CommandsCommand, _pluginConfig.LeaveCommand, _pluginConfig.HelpCommand);
            }
            
            SendMessageToChannel(channelId, message);
            return null;
        }

        public object HandleCommands(IPlayer player, string channelId, string cmd, string[] args)
        {
            string commands = $"Discord: {Lang(LangKeys.DiscordCommandsPreText, player)}\n";
            foreach (KeyValuePair<string, DiscordCommand> command in _discordCommands)
            {
                if (!string.IsNullOrEmpty(command.Value.Permission) && !player.HasPermission(command.Value.Permission))
                {
                    continue;
                }

                string commandText = $"/{command.Key}";
                string botChannel = $" {(_pluginConfig.EnableBotChannel && command.Value.AllowInBotChannel ? Lang(LangKeys.DiscordCommandsBotChannelAllowed, player) : "")}";
                string helpText = lang.GetMessage(command.Value.HelpText, command.Value.Plugin, player.Id);

                commands += $"{commandText}{botChannel} - {helpText}\n";
            }

            SendMessageToChannel(channelId, commands);
            return null;
        }

        public object HandleLeave(IPlayer player, string channelId, string cmd, string[] args)
        {
            SendMessageToChannel(channelId, "Discord: " + Lang(LangKeys.DiscordLeave, player));
            HandleLeave(player, false);
            return null;
        }

        public object HandlePlugins(IPlayer player, string channelId, string cmd, string[] args)
        {
            string pluginList = _discordCommands.Select(dc => dc.Value.PluginName).Distinct().Aggregate("Discord: " + Lang(LangKeys.DiscordPlugins, player), (current, plugin) => current + $"{plugin}\n");
            SendMessageToChannel(channelId, pluginList);
            return null;
        }

        #endregion

        #region Discord Hooks
        private void Discord_Ready(Ready ready)
        {
            try
            {
                _guildCount = ready.Guilds.Count;
                if (_guildCount > 1)
                {
                    if (string.IsNullOrEmpty(_pluginConfig.BotGuildId))
                    {
                        PrintError("Bot Guild Id is blank and the bot is in multiple guilds. " +
                                   "Please set the bot guild id to the discord server you wish it to work for.");
                        return;
                    }

                    if (ready.Guilds.All(g => g.id != _pluginConfig.BotGuildId))
                    {
                        PrintError("Failed to find a matching guild for the Bot Guild Id. " +
                                   "Please make sure your guild Id is correct and the bot is in that discord server.");
                        return;
                    }
                }

                _bot = ready.User;
                Puts($"Connected to bot: {_bot.username}");

                _connectionTimeout = timer.In(60f, () =>
                {
                    CloseClient();
                    ConnectClient();
                });
            }
            catch (Exception ex)
            {
                _connectionState = ConnectionStateEnum.Disconnected;
                PrintError($"Failed to load DiscordCore: {ex}");
            }
        }
        
        private void Discord_GuildCreate(Guild guild)
        {
            if (_connectionState != ConnectionStateEnum.Connecting)
            {
                return;
            }

            if (_guildCount != 1 && guild.id != _pluginConfig.BotGuildId)
            {
                return;
            }

            GuildConnected(guild);
        }

        private void GuildConnected(Guild guild)
        {
            try
            {
                _connectionTimeout?.Destroy();
                _lastUpdate = DateTime.Now;
                _discordServer = guild;
                Puts($"Discord connected to server: {GetDiscordName()}");

                _connectionState = ConnectionStateEnum.Connected;

                if (!_initialized)
                {
                    _initialized = true;
                }

                if (_pluginConfig.EnableBotChannel)
                {
                    if (_discordServer.channels.All(c => c.name != _pluginConfig.BotChannel && c.id != _pluginConfig.BotChannel))
                    {
                        PrintWarning($"Bot channel is enabled but there is no channel found with name or id '{_pluginConfig.BotChannel}' on the discord server");
                    }
                }
                
                Puts($"{_discordServer.members.Count} Guild Discord Members Loaded");

                if (_discordServer.members.All(m => m.user.bot ?? false))
                {
                    PrintError($"You need to enable \"Presence Intent\" & \"Server Members Intent\" for {_bot.username} @ https://discord.com/developers/applications\n" +
                               "DiscordCore will not function correctly until that is fixed. Once updated please reload DiscordCore.");
                    return;
                }

                _client.RequestGuildMembers(guild.id);
            }
            catch (Exception ex)
            {
                _connectionState = ConnectionStateEnum.Disconnected;
                PrintError($"Failed to connect to guild: {ex}");
            }
        }

        private void Discord_GuildMembersChunk(GuildMembersChunk chunk)
        {
            _chunkLoading?.Destroy();
            _chunkLoading = timer.In(1.5f, FinishChunk);
            
            _chunkUsers.AddRange(chunk.members);
            _lastUpdate = DateTime.Now;
        }

        private void FinishChunk()
        {
            _lastUpdate = DateTime.Now;
            int added = 0;
            foreach (GuildMember member in _chunkUsers)
            {
                if (_discordServer.members.All(m => m.user.id != member.user.id))
                {
                    added++;
                    _discordServer.members.Add(member);
                }
            }
            
            Puts($"{added} Chunk Discord Members Loaded");
            Puts($"{_discordServer.members.Count} Total Discord Members Loaded");
            
            timer.In(5f, () =>
            {
                foreach (KeyValuePair<string, DiscordInfo> info in _storedData.PlayerDiscordInfo.ToList())
                {
                    if (_discordServer.members.All(m => m.user.id != info.Value.DiscordId))
                    {
                        IPlayer player = players.FindPlayerById(info.Key);
                        if (player != null)
                        {
                            HandleLeave(player, true);
                            Puts($"Player ({info.Value.PlayerId}) no longer in discord. Removing them from the data file.");
                        }
                    }
                }

                foreach (GuildMember member in _discordServer.members)
                {
                    DiscordInfo existing = _storedData.LeftPlayerInfo[member.user.id];
                    if (existing != null)
                    {
                        HandleRejoin(member.user.id);
                    }
                }
            });
            
            //Fixes issue in discord extension
            NextTick(() =>
            {
                Interface.Call("OnDiscordCoreReady");
                Puts($"Discord Core Ready");
            });
        }

        private void DiscordSocket_HeartbeatSent()
        {
            _lastUpdate = DateTime.Now;
        }

        private void Discord_MessageCreate(Message message)
        {
            if (!_initialized)
            {
                return;
            }

            if (message.author.username == _bot.username)
            {
                Interface.Oxide.CallHook("OnDiscordBotMessage", message);
                return;
            }
            
            bool botChannel = false;
            string content = message.content;
            Channel channel = _discordServer.channels.FirstOrDefault(c => c.id == message.channel_id);
            if (channel != null)
            {
                bool isBotChannel = channel.name.ToLower().Equals(_pluginConfig.BotChannel.ToLower()) || channel.id == _pluginConfig.BotChannel;
                bool shouldReturn = true;
                if (!string.IsNullOrEmpty(_pluginConfig.DiscordJoinCommand) && content.ToLower().Equals("/" + _pluginConfig.DiscordJoinCommand))
                {
                    if (_useExternal)
                    {
                        message.author.CreateDM(_client, pmChannel =>
                        {
                            pmChannel.CreateMessage(_client, Lang(LangKeys.DiscordCoreLinkingNotEnabled, null, _pluginConfig.DiscordJoinCommand));
                        });
                    }
                    else if (_pluginConfig.EnableJoinWrongChannelNonBotChannel && !isBotChannel || _pluginConfig.EnableJoinWrongChannelBotChannel && isBotChannel)
                    {
                        message.author.CreateDM(_client, pmChannel =>
                        {
                            pmChannel.CreateMessage(_client, Lang(LangKeys.DiscordJoinWrongChannel, null, _pluginConfig.DiscordJoinCommand));
                        });
                    }

                    NextTick(() =>
                    {
                        message.DeleteMessage(_client);
                    });
                }
                else if (_pluginConfig.EnableBotChannel 
                         && content.StartsWith("/") 
                         && isBotChannel)
                {
                    string cmd = content.TrimStart('/').Split(' ')[0];
                    DiscordCommand command = _discordCommands[cmd];
                    if (command != null)
                    {
                        if (command.AllowInBotChannel)
                        {
                            botChannel = true;
                        }
                        else
                        {
                            NextTick(() =>
                            {
                                message.DeleteMessage(_client);
                            });
                            message.author.CreateDM(_client, pmChannel =>
                            {
                                pmChannel.CreateMessage(_client, Lang(LangKeys.DiscordNotAllowedBotChannel));
                            });
                        }
                    }
                    else
                    {
                        message.Reply(_client, Lang(LangKeys.UnknownCommand));
                    }
                }
                else if (_channelSubscriptions.ContainsKey(channel.id))
                {
                    object output = null;
                    foreach (ChannelSubscription subscription in _channelSubscriptions[channel.id])
                    {
                        try
                        {
                            output = subscription.Method.Invoke(message);
                        }
                        catch (Exception ex)
                        {
                            PrintError($"Error invoking method on plugin {subscription.PluginName} on subscribed channel method {subscription.Method.Method.Name}:\n{ex}");
                        }
                    }
                    
                    if (output != null)
                    {
                        shouldReturn = false;
                    }
                }
                
                if (shouldReturn && !botChannel)
                {
                    return;
                }
            }
            
            InGameActivation inGameActivation = _pendingInGameActivation[message.author.id];
            if (inGameActivation != null)
            {
                HandleGameServerJoin(message, inGameActivation);
                return;
            }
            
            if (content.Equals($"/{_pluginConfig.DiscordJoinCommand}", StringComparison.InvariantCultureIgnoreCase))
            {
                if (_useExternal)
                {
                    SendMessageToUser(message.author.id, Lang(LangKeys.DiscordCoreLinkingNotEnabled, null, _pluginConfig.DiscordJoinCommand));
                    return;
                }
                
                HandleDiscordJoin(message);
                return;
            }

            //Puts($"MessageCreate: {content}");

            string id = GetDiscordId(message.author.id);
            if (string.IsNullOrEmpty(id) && !botChannel)
            {
                if (!content.StartsWith("/"))
                {
                    return;
                }
                
                if (!_pluginConfig.EnableLinking && !_useExternal)
                {
                    return;
                }
                
                message.author.CreateDM(_client, dmChannel =>
                {
                    if (_useExternal)
                    {
                        dmChannel.CreateMessage(_client, Lang(LangKeys.NotLinked, null, _pluginConfig.DiscordJoinCommand));
                        return;
                    }

                    dmChannel.CreateMessage(_client, Lang(LangKeys.DiscordMustSignUpBeforeCommands, null, _pluginConfig.DiscordJoinCommand));
                });
                return;
            }

            try
            {
                string playerId = GetPlayerId(id);
                if (playerId == "server_console")
                {
                    SendMessageToUser(message.author.id, Lang(LangKeys.DiscordPlayerIsServerConsole));
                    return;
                }
                
                IPlayer player = players.FindPlayerById(playerId);
                
                if (content.StartsWith("/"))
                {
                    string[] args;
                    string command;
                    
                    ParseCommand(message.content.TrimStart('/'), out command, out args);
                    DiscordCommand discord = _discordCommands[command];
                    if (discord != null)
                    {
                        if (!string.IsNullOrEmpty(discord.Permission))
                        {
                            if (player == null)
                            {
                                SendMessageToUser(message.author.id, Lang(LangKeys.DiscordMustSignUpBeforeBotCommands, null, _pluginConfig.DiscordJoinCommand));
                                return;
                            }

                            if (!player.HasPermission(discord.Permission))
                            {
                                SendMessageToUser(player, Lang(LangKeys.NoPermission, player));
                                return;
                            }
                        }
                        
                        discord.Method(player, message.channel_id, command, args);
                    }
                    else
                    {
                        SendMessageToUser(message.author.id, Lang(LangKeys.UnknownCommand, player));
                    }
                }
                else
                {
                    Interface.Call("OnDiscordChat", player, message.content);
                }
            }
            catch (Exception ex)
            {
                PrintError($"Error in message create:\n{ex}");
            }
        }

        private void Discord_MemberAdded(GuildMember member)
        {
            HandleRejoin(member.user.id);
            
            if (!_pluginConfig.EnabledJoinNotifications)
            {
                return;
            }

            member.user.CreateDM(_client, channel =>
            {
                channel.CreateMessage(_client, Lang(LangKeys.DiscordUserJoin, null, GetDiscordName(), _pluginConfig.DiscordJoinCommand, _pluginConfig.HelpCommand));
            });
        }
        
        private void Discord_MemberRemoved(GuildMember member)
        {
            if (member?.user == null)
            {
                return;
            }
            
            DiscordInfo info = _storedData.GetInfoByDiscordId(member.user.id);
            if (info == null)
            {
                return;
            }

            IPlayer player = players.FindPlayerById(info.PlayerId);
            if (player == null)
            {
                return;
            }

            HandleLeave(player, true);
        }

        private void Discord_ChannelDelete(Channel channel)
        {
            // Delete all subscriptions to this channel for all plugins
            _channelSubscriptions.Remove(channel.id);

            foreach (KeyValuePair<string, Channel> cacheChannel in _channelCache.ToList())
            {
                if (channel.id == cacheChannel.Value.id)
                {
                    _channelCache.Remove(cacheChannel.Key);
                }
            }
            
            //TODO: Remove once fix is implemented in Discord Extension
            _discordServer.channels.RemoveAll(c => c.id == channel.id);
        }

        private void Discord_UnhandledEvent(JObject messageObject)
        {
            if (_connectionState == ConnectionStateEnum.Connected)
            {
                PrintError("Discord Exception had an unhandled event. Discord Core will now try to reconnect");
                timer.In(5f, () =>
                {
                    CloseClient();
                    ConnectClient();
                });
            }
        }

        private void DiscordSocket_WebSocketErrored(Exception exception, string message)
        {
            if (_connectionState == ConnectionStateEnum.Connected)
            {
                PrintError("Discord Exception had a websocket error. Discord Core will now try to reconnect");
                timer.In(5f, () =>
                {
                    CloseClient();
                    ConnectClient();
                });
            }
        }

        private void DiscordSocket_WebSocketClosed(string reason, int code, bool clean)
        {
            if (_connectionState == ConnectionStateEnum.Connected)
            {
                PrintError("Discord Exception closed the websocket. Discord Core will now try to reconnect");
                timer.In(5f, () =>
                {
                    CloseClient();
                    ConnectClient();
                });
            }
        }
        #endregion

        #region Joining
        public void HandleGameServerJoin(Message message, InGameActivation activation)
        {
            if (activation.Code != message.content)
            {
                message.Reply(_client, Lang(LangKeys.GameJoinCodeNotMatch), false);
                return;
            }

            _pendingInGameActivation.Remove(message.author.id);
            _storedData.PlayerDiscordInfo[activation.PlayerId] = new DiscordInfo
            {
                DiscordId = message.author.id,
                PlayerId = activation.PlayerId,
            };

            IPlayer player = covalence.Players.FindPlayer(activation.PlayerId);
            SaveData();
            Chat(player, Lang(LangKeys.DiscordJoinSuccessfullyRegistered, player));
            message.Reply(_client, $"Discord: {Lang(LangKeys.DiscordJoinSuccessfullyRegistered, player)}", false);
            string name = _discordServer.members.FirstOrDefault(m => m.user.id == message.author.id)?.nick;
            SendMessageToChannel(_pluginConfig.LinkUnlinkAnnouncementChannel, Lang(LangKeys.LinkNotification, null, player.Name, player.Id, name, message.author.id));
            Interface.Call("OnDiscordCoreJoin", player);
        }

        public void HandleDiscordJoin(Message message)
        {
            if (_storedData.PlayerDiscordInfo.Any(di => di.Value.DiscordId == message.author.id))
            {
                message.Reply(_client, Formatter.ToPlaintext(Lang(LangKeys.JoinAlreadySignedUp, null, _pluginConfig.DiscordJoinCommand)), false);
                return;
            }

            string code = GenerateCode();
            _pendingDiscordActivations.Add(new DiscordActivation
            {
                Code = code,
                DiscordId = message.author.id,
                ChannelId = message.channel_id,
            });

            message.Reply(_client, Lang(LangKeys.DiscordCompleteActivation, null, _pluginConfig.GameChatCommand, code), false);
        }

        public DiscordUser GetUserByUsername(string userName)
        {
            if (!IsDiscordCoreOnline())
            {
                return null;
            }
            
            return _discordServer.members.FirstOrDefault(m => $"{m?.user?.username?.ToLower()}#{m?.user?.discriminator}" == userName.ToLower())?.user;
        }

        public void CreateChannelActivation(IPlayer player, DiscordUser user, string code, string message)
        {
            user.CreateDM(_client, channel =>
            {
                channel.CreateMessage(_client, message);
                _pendingInGameActivation[user.id] = new InGameActivation
                {
                    PlayerId = player.Id,
                    Code = code,
                    ChannelId = channel.id,
                    DisplayName = player.Name
                };
            });
        }
        #endregion

        #region Leaving

        public void HandleLeave(IPlayer player, bool backup)
        {
            string discordId = GetDiscordId(player.Id);
            Interface.Call("OnDiscordCoreLeave", player, discordId);
            
            string name = _discordServer.members.FirstOrDefault(m => m.user.id == discordId)?.nick;
            SendMessageToChannel(_pluginConfig.LinkUnlinkAnnouncementChannel, Lang(LangKeys.UnlinkNotification, null, player.Name, player.Id, name, discordId));

            if (backup)
            {
                _storedData.LeftPlayerInfo[discordId] = _storedData.PlayerDiscordInfo[player.Id];
            }

            _storedData.PlayerDiscordInfo.Remove(player.Id);
            SaveData();
        }

        public void HandleRejoin(string discordId)
        {
            if (!_pluginConfig.EnableLinking)
            {
                return;
            }
        
            DiscordInfo existing = _storedData.LeftPlayerInfo[discordId];
            if (existing == null)
            {
                return;
            }

            _storedData.PlayerDiscordInfo[existing.PlayerId] = existing;
            _storedData.LeftPlayerInfo.Remove(discordId);

            IPlayer player = players.FindPlayerById(existing.PlayerId);
            
            string name = _discordServer.members.FirstOrDefault(m => m.user.id == discordId)?.nick;
            SendMessageToChannel(_pluginConfig.LinkUnlinkAnnouncementChannel, Lang(LangKeys.RejoinNotification, null, player.Name, player.Id, name, discordId));

            Interface.Call("OnDiscordCoreJoin", existing.PlayerId, existing.DiscordId);
            SaveData();
        }

        #endregion

        #region API

        #region Discord Server
        [HookMethod("IsReady")]
        public bool IsReady()
        {
            return _client?.DiscordServer != null;
        }

        [HookMethod("GetClient")]
        public DiscordClient GetClient()
        {
            return _client;
        }

        [HookMethod("GetBot")]
        public DiscordUser GetBot()
        {
            return _bot;
        }
        
        [HookMethod("GetServer")]
        public Guild GetServer()
        {
            return _discordServer;
        }

        [HookMethod("GetDiscordName")]
        public string GetDiscordName()
        {
            if (!string.IsNullOrEmpty(_pluginConfig.ServerNameOverride))
            {
                return _pluginConfig.ServerNameOverride;
            }
            
            return _discordServer?.name ?? "Not Connected";
        }

        [HookMethod("GetDiscordJoinCode")]
        public string GetDiscordJoinCode()
        {
            return _pluginConfig.JoinCode;
        }

        [HookMethod("UpdatePresence")]
        public void UpdatePresence(Presence presence)
        {
            try
            {
                _client?.UpdateStatus(presence);
            }
            catch
            {
                // ignored
            }
        }

        [HookMethod("RegisterPluginForExtensionHooks")]
        public void RegisterPluginForExtensionHooks(Plugin plugin)
        {
            if (!_client.Plugins.Contains(plugin))
            {
                _client.Plugins.Add(plugin);
            }

            if (!_hookPlugins.Contains(plugin))
            {
                _hookPlugins.Add(plugin);
            }
        }
        #endregion

        #region Send Message

        #region Channel
        [HookMethod("SendMessageToChannel")]
        public void SendMessageToChannel(string channelNameOrId, string message)
        {
            Channel channel = GetChannel(channelNameOrId);

            channel?.CreateMessage(_client,  StripRustTags(Formatter.ToPlaintext(message)));
        }
        
        [HookMethod("SendMessageToChannel")]
        public void SendMessageToChannel(string channelNameOrId, Embed message)
        {
            Channel channel = GetChannel(channelNameOrId);

            channel?.CreateMessage(_client, message);
        }
        
        [HookMethod("SendMessageToChannel")]
        public void SendMessageToChannel(string channelNameOrId, Message message)
        {
            Channel channel = GetChannel(channelNameOrId);

            channel?.CreateMessage(_client, message);
        }
        #endregion

        #region User
        [HookMethod("SendMessageToUser")]
        public void SendMessageToUser(string id, string message)
        {
            GetPlayerDmChannel(id, channel => { channel.CreateMessage(_client, StripRustTags(Formatter.ToPlaintext(message))); });
        }

        [HookMethod("SendMessageToUser")]
        public void SendMessageToUser(IPlayer player, string message)
        {
            SendMessageToUser(player.Id, message);
        }
        
        [HookMethod("SendMessageToUser")]
        public void SendMessageUser(string id, Embed message)
        {
            GetPlayerDmChannel(id, channel => { channel.CreateMessage(_client, message); });
        }
        
        [HookMethod("SendMessageToUser")]
        public void SendMessageUser(string id, Message message)
        {
            GetPlayerDmChannel(id, channel => { channel.CreateMessage(_client, message); });
        }
        
        [HookMethod("SendMessageToUser")]
        public void SendMessageUser(IPlayer player, Message message)
        {
            SendMessageUser(player.Id, message);
        }

        [HookMethod("UpdateUserNick")]
        public void UpdateUserNick(string id, string newNick)
        {
            if (!IsDiscordCoreOnline())
            {
                return;
            }
            
            string discordId = GetDiscordId(id);
            if (string.IsNullOrEmpty(discordId))
            {
                return;
            }

            GuildMember member = GetGuildMember(id);
            if (member == null || member.nick == newNick)
            {
                return;
            }

            if (_discordServer?.owner_id == discordId)
            {
                return;
            }

            _discordServer?.ModifyUsersNick(_client, discordId, newNick);
        }

        [HookMethod("UpdateUserNick")]
        public void UpdateUserNick(IPlayer player, string newNick)
        {
            UpdateUserNick(player.Id, newNick);
        }
        #endregion
        #endregion

        #region Message
        [HookMethod("DeleteMessage")]
        public void DeleteMessage(Message message)
        {
            message.DeleteMessage(_client);
        }
        #endregion

        #region User
        [HookMethod("GetAllUsers")]
        public List<string> GetAllUsers()
        {
            if (_pluginConfig.UseDiscordAuth)
            {
                return DiscordAuth?.Call("API_GetSteamList") as List<string> ?? new List<string>();
            }

            if (_pluginConfig.UseDiscordConnect)
            {
                throw new NotSupportedException("DiscordConnect does not support GetAllUsers()");
            }

            if (_pluginConfig.UseHooks)
            {
                return Interface.Call("Discord_GetAllSteamUsers") as List<string> ?? new List<string>();
            }

            return _storedData.PlayerDiscordInfo.Keys.ToList();
        }
        
        [HookMethod("GetAllDiscordUsers")]
        public List<string> GetAllDiscordUsers()
        {
            if (_pluginConfig.UseDiscordAuth)
            {
                return DiscordAuth.Call("API_GetDiscordList") as List<string>;
            }
            
            if (_pluginConfig.UseDiscordConnect)
            {
                throw new NotSupportedException("DiscordConnect does not support GetAllDiscordUsers()");
            }
            
            if (_pluginConfig.UseHooks)
            {
                return Interface.Call("Discord_GetAllDiscordUsers") as List<string> ?? new List<string>();
            }
            
            return _storedData.PlayerDiscordInfo.Values.Select(d => d.DiscordId).ToList();
        }

        [HookMethod("GetGuildMember")]
        public GuildMember GetGuildMember(string id)
        {
            if (!IsDiscordCoreOnline())
            {
                return null;
            }
            
            string discordId = GetDiscordId(id);
            return _discordServer.members.FirstOrDefault(m => discordId == m.user.id);
        }

        [HookMethod("GetLinkedPlayers")]
        public List<DiscordUser> GetLinkedPlayers()
        {
            if (!IsDiscordCoreOnline())
            {
                return new List<DiscordUser>();
            }
            
            List<string> linked = GetAllDiscordUsers();
            
            return _discordServer.members
                .Where(m => linked.Contains(m.user.id))
                .Select(m => m.user)
                .ToList();
        }

        [HookMethod("GetGuildAllUsers")]
        public List<DiscordUser> GetGuildAllUsers()
        {
            if (!IsDiscordCoreOnline())
            {
                return new List<DiscordUser>();
            }
            
            return _discordServer.members.Select(u => u.user).ToList();
        }

        [HookMethod("GetUserDiscordInfo")]
        public JObject GetUserDiscordInfo(object user)
        {
            DiscordInfo info = null;
            if (user is string)
            {
                string playerId = GetPlayerId((string) user);
                if (_useExternal)
                {
                    if (!string.IsNullOrEmpty(playerId))
                    {
                        string discordId = GetDiscordId(playerId);
                        info = new DiscordInfo
                        {
                            DiscordId = discordId,
                            PlayerId = playerId
                        };
                    }
                }
                else
                {
                    info = _storedData.PlayerDiscordInfo[(string)user] ?? _storedData.GetInfoByDiscordId((string)user);
                }
            }
            else if (user is IPlayer)
            {
                IPlayer player = (IPlayer) user;
                string discordId = GetDiscordId(player.Id);

                info = new DiscordInfo
                {
                    DiscordId = discordId,
                    PlayerId = player.Id
                };
            }

            return info != null ? JObject.FromObject(info) : null;
        }

        [HookMethod("GetUsersDiscordInfo")]
        public List<JObject> GetUsersDiscordInfo(List<object> users)
        {
            return users.Select(GetUserDiscordInfo).Where(i => i != null).ToList();
        }

        [HookMethod("GetSteamIdFromDiscordId")]
        public string GetSteamIdFromDiscordId(string discordId)
        {
            return GetPlayerId(discordId);
        }

        [HookMethod("GetDiscordIdFromSteamId")]
        public string GetDiscordIdFromSteamId(string steamId)
        {
            return GetDiscordId(steamId);
        }

        [HookMethod("KickDiscordUser")]
        public void KickDiscordUser(string id)
        {
            if (!IsDiscordCoreOnline())
            {
                return;
            }
            
            string discordId = GetDiscordId(id);
            _discordServer.RemoveGuildMember(_client, discordId);
        }

        [HookMethod("BanDiscordUser")]
        public void BanDiscordUser(string id, int? deleteMessageDays)
        {
            if (!IsDiscordCoreOnline())
            {
                return;
            }
            
            string discordId = GetDiscordId(id);
            _discordServer.CreateGuildBan(_client, discordId, deleteMessageDays);
        }
        #endregion

        #region Discord Chat Commands
        [HookMethod("RegisterCommand")]
        public void RegisterCommand(string command, Plugin plugin, Func<IPlayer, string, string, string[], object> method, string helpText, string permission = null, bool allowInBotChannel = false)
        {
            command = command.TrimStart('/');
            if (_discordCommands.ContainsKey(command) && _discordCommands[command].PluginName != plugin.Name)
            {
                PrintWarning($"Discord Commands already contains command: {command}. Previously registered to {_discordCommands[command].PluginName}");
            }

            if (plugin == null)
            {
                PrintWarning($"Cannot register command: {command} with a null plugin!");
                return;
            }

            //Puts($"Registered Command: /{command} for plugin: {pluginName}");

            _discordCommands[command] = new DiscordCommand
            {
                PluginName = plugin.Name,
                Plugin = plugin,
                Method = method,
                HelpText = helpText,
                Permission = permission,
                AllowInBotChannel = allowInBotChannel
            };
        }

        [HookMethod("UnregisterCommand")]
        public void UnregisterCommand(string command, Plugin plugin)
        {
            command = command.TrimStart('/');
            if (!_discordCommands.ContainsKey(command))
            {
                PrintWarning($"Command: {command} could not be unregistered because it was not found");
                return;
            }

            if (_discordCommands[command]?.PluginName != plugin.Name)
            {
                PrintWarning("Cannot unregister commands which don't belong to your plugin\n" +
                             $"Command: {command} CommandPlugin:{_discordCommands[command]?.PluginName} Unregistering Plugin: {plugin.Title}");
                return;
            }

            _discordCommands.Remove(command);
        }
        #endregion

        #region Channel
        [HookMethod("GetAllChannels")]
        public List<Channel> GetAllChannels()
        {
            if (!IsDiscordCoreOnline())
            {
                return new List<Channel>();
            }
            
            return _discordServer.channels.ToList();
        }
        
        [HookMethod("GetAllDms")]
        public List<Channel> GetAllDms()
        {
            return _client.DMs.ToList();
        }

        [HookMethod("GetChannel")]
        public Channel GetChannel(string nameOrId)
        {
            Channel channel = _channelCache[nameOrId];
            if (channel != null)
            {
                return channel;
            }
            
            if (!IsDiscordCoreOnline())
            {
                return null;
            }
            
            channel = _discordServer?.channels.ToList().FirstOrDefault(c => c.id == nameOrId || string.Equals(c.name, nameOrId, StringComparison.InvariantCultureIgnoreCase));
            if (channel != null)
            {
                _channelCache[nameOrId] = channel;
                return channel;
            }

            channel = _client?.DMs.FirstOrDefault(d => d.id == nameOrId);
            _channelCache[nameOrId] = channel;
            return channel;
        }

        [HookMethod("GetChannelMessages")]
        public void GetChannelMessages(string nameOrId, string responseKey)
        {
            GetChannel(nameOrId)?.GetChannelMessages(_client, messages =>
            {
                Interface.Call("OnGetChannelMessages", messages, responseKey);
            });
        }

        [HookMethod("SubscribeChannel")]
        public void SubscribeChannel(string channelNameOrId, Plugin plugin, Func<Message, object> method)
        {
            if (_connectionState != ConnectionStateEnum.Connected)
            {
                PrintError("Trying to subscribe to channel while bot is not in connect state");
                return;
            }

            Channel channel = GetChannel(channelNameOrId);
            if (channel == null)
            {
                PrintError($"Channel not found in guild: {channelNameOrId}");
                return;
            }

            if (!_channelSubscriptions.ContainsKey(channel.id))
            {
                _channelSubscriptions[channel.id] = new List<ChannelSubscription>();
            }

            List<ChannelSubscription> subscriptions = _channelSubscriptions[channel.id];
            if (subscriptions.Any(s => s.PluginName == plugin.Name))
            {
                LogWarning($"The plugin {plugin.Title} already has a subscription to channel {channelNameOrId}");
                return;
            }

            subscriptions.Add(new ChannelSubscription
            {
                PluginName = plugin.Name,
                Method = method
            });

            Interface.Call("OnChannelSubscribed", channel, plugin);
        }

        [HookMethod("UnsubscribeChannel")]
        public void UnsubscribeChannel(string channelNameOrId, Plugin plugin)
        {
            if (_connectionState != ConnectionStateEnum.Connected)
            {
                PrintError("Trying to unsubscribe to channel while bot is not in connect state");
                return;
            }

            Channel channel = GetChannel(channelNameOrId);
            if (channel == null)
            {
                PrintError($"Channel not found in guild: {channelNameOrId}");
                return;
            }

            List<ChannelSubscription> subscriptions = _channelSubscriptions[channel.id];

            subscriptions?.RemoveAll(s => s.PluginName == plugin.Name);
            Interface.Call("OnChannelUnsubscribed", channel, plugin);
        }

        [HookMethod("ChannelBulkMessageDelete")]
        public void ChannelBulkMessageDelete(string channelNameOrId, string[] messageIds)
        {
            Channel channel = GetChannel(channelNameOrId);
            channel.BulkDeleteMessages(_client, messageIds);
        }
        #endregion

        #region Roles
        [HookMethod("GetRoles")]
        public List<Role> GetRoles()
        {
            if (!IsDiscordCoreOnline())
            {
                return new List<Role>();
            }
            
            return _discordServer.roles;
        }
        
        [HookMethod("GetRole")]
        public Role GetRole(string nameOrId)
        {
            if (!IsDiscordCoreOnline())
            {
                return null;
            }
            
            return _discordServer.roles.FirstOrDefault(r => r.id == nameOrId || string.Equals(r.name, nameOrId, StringComparison.OrdinalIgnoreCase));
        }

        [HookMethod("UserHasRole")]
        public bool UserHasRole(string userId, string nameOrId)
        {
            GuildMember member = GetGuildMember(userId);
            if (member == null)
            {
                return false;
            }

            Role role = GetRole(nameOrId);
            if (role == null)
            {
                return false;
            }
            
            return member.roles.Contains(role.id);
        }

        [HookMethod("AddRoleToUser")]
        public void AddRoleToUser(string userId, string roleId)
        {
            if (!IsDiscordCoreOnline())
            {
                return;
            }
            
            Role role = GetRole(roleId);
            if (role == null)
            {
                PrintWarning($"Tried to add a role to player that doesn't exist: '{roleId}'");
                return;
            }

            _discordServer.AddGuildMemberRole(_client, userId, role.id);
        }

        [HookMethod("RemoveRoleFromUser")]
        public void RemoveRoleFromUser(string userId, string roleId)
        {
            if (!IsDiscordCoreOnline())
            {
                return;
            }
            
            Role role = GetRole(roleId);
            if (role == null)
            {
                PrintWarning($"Tried to remove a role from a player that doesn't exist: '{roleId}'");
                return;
            }

            _discordServer.RemoveGuildMemberRole(_client, userId, role.id);
        }

        [HookMethod("CreateGuildRole")]
        public void CreateGuildRole(Role role)
        {
            if (!IsDiscordCoreOnline())
            {
                return;
            }
            
            _discordServer.CreateGuildRole(_client, role);
        }

        [HookMethod("DeleteGuildRole")]
        public void DeleteGuildRole(string roleId)
        {
            if (!IsDiscordCoreOnline())
            {
                return;
            }
            
            _discordServer.DeleteGuildRole(_client, roleId);
        }

        [HookMethod("DeleteGuildRole")]
        public void DeleteGuildRole(Role role)
        {
            DeleteGuildRole(role.id);
        }
        #endregion
        #endregion

        #region Method Handling
        /// <summary>
        /// Parses the specified command into uMod command format
        /// Sourced from RustCore.cs of OxideMod (https://github.com/theumod/uMod.Rust/blob/oxide/src/RustCore.cs)
        /// </summary>
        /// <param name="argstr"></param>
        /// <param name="command"></param>
        /// <param name="args"></param>
        public void ParseCommand(string argstr, out string command, out string[] args)
        {
            List<string> argList = new List<string>();
            StringBuilder sb = new StringBuilder();
            bool inLongArg = false;

            foreach (char c in argstr)
            {
                if (c == '"')
                {
                    if (inLongArg)
                    {
                        string arg = sb.ToString().Trim();
                        if (!string.IsNullOrEmpty(arg))
                            argList.Add(arg);
                        sb = new StringBuilder();
                        inLongArg = false;
                    }
                    else
                        inLongArg = true;
                }
                else if (char.IsWhiteSpace(c) && !inLongArg)
                {
                    string arg = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(arg))
                        argList.Add(arg);
                    sb = new StringBuilder();
                }
                else
                    sb.Append(c);
            }

            if (sb.Length > 0)
            {
                string arg = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(arg))
                    argList.Add(arg);
            }

            if (argList.Count == 0)
            {
                command = null;
                args = null;
                return;
            }

            command = argList[0].ToLower();
            argList.RemoveAt(0);
            args = argList.ToArray();
        }

        #endregion Method Handling

        #region Rust Tag Handling
        private readonly List<Regex> _regexTags = new List<Regex>
        {
            new Regex("<color=.+?>", RegexOptions.Compiled),
            new Regex("<size=.+?>", RegexOptions.Compiled)
        };

        private readonly List<string> _tags = new List<string>
        {
            "</color>",
            "</size>",
            "<i>",
            "</i>",
            "<b>",
            "</b>"
        };

        public string StripRustTags(string original)
        {
            if (string.IsNullOrEmpty(original))
            {
                return string.Empty;
            }

            foreach (string tag in _tags)
            {
                original = original.Replace(tag, "");
            }

            foreach (Regex regexTag in _regexTags)
            {
                original = regexTag.Replace(original, "");
            }

            return original;
        }
        #endregion

        #region Discord Helpers
        public string GetDiscordId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }
            
            if (_useExternal)
            {
                string discordId = GetExternalDiscord(id);
                if (!string.IsNullOrEmpty(discordId))
                {
                    return discordId;
                }

                discordId = GetExternalGame(id);
                if (!string.IsNullOrEmpty(discordId))
                {
                    return id;
                }

                return null;
            }
            
            DiscordInfo info = _storedData.PlayerDiscordInfo[id] ?? _storedData.GetInfoByDiscordId(id);
            
            return info?.DiscordId;
        }

        public string GetPlayerId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }
            
            if (_useExternal)
            {
                string discordId = GetExternalDiscord(id);
                if (!string.IsNullOrEmpty(discordId))
                {
                    return id;
                }

                discordId = GetExternalGame(id);
                if (!string.IsNullOrEmpty(discordId))
                {
                    return discordId;
                }

                return null;
            }

            DiscordInfo info = _storedData.PlayerDiscordInfo[id] ?? _storedData.GetInfoByDiscordId(id);

            return info?.PlayerId;
        }

        public string GetExternalDiscord(string id)
        {
            if (_pluginConfig.UseDiscordAuth)
            {
                return DiscordAuth?.Call("API_GetDiscord", id) as string;
            }

            if (_pluginConfig.UseDiscordConnect)
            {
                return DiscordConnect.Call("GetDiscordOf", id) as string;
            }
            
            return Interface.Call("Discord_GetDiscordBySteam", id) as string;
        }

        public string GetExternalGame(string id)
        {
            if (_pluginConfig.UseDiscordAuth)
            {
                return DiscordAuth?.Call("API_GetSteam", id) as string;
            }

            if (_pluginConfig.UseDiscordConnect)
            {
                return DiscordConnect.Call("GetGameOf", id) as string;
            }
            
            return Interface.Call("Discord_GetSteamByDiscord", id) as string;
        }

        public void GetPlayerDmChannel(string id, Action<Channel> callback)
        {
            string discordId = GetDiscordId(id);
            if (string.IsNullOrEmpty(discordId))
            {
                return;
            }
            
            string channelId = _playerDmChannel[discordId];
            if (!string.IsNullOrEmpty(channelId))
            {
                Channel.GetChannel(_client, channelId, channel =>
                {
                    if (channel != null)
                    {
                        callback.Invoke(channel);
                        return;
                    }

                    _playerDmChannel.Remove(discordId);
                    GetPlayerDmChannel(discordId, callback);
                });

                return;
            }

            Channel dmChannel = _client.DMs.FirstOrDefault(d => d.recipients.Any(r => r.id == discordId));
            if (dmChannel != null)
            {
                _playerDmChannel[discordId] = dmChannel.id;
                callback.Invoke(dmChannel);
                return;
            }
            
            DiscordUser.GetUser(_client, discordId, user =>
            {
                user?.CreateDM(_client, (newChannel) =>
                {
                    _playerDmChannel[discordId] = newChannel.id;
                    callback.Invoke(newChannel); ;
                });
            });
        }

        private bool IsDiscordCoreOnline() => _initialized && _discordServer != null;
        #endregion
        
        #region Helper Methods
        public string GenerateCode()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _pluginConfig.LinkCodeLength; i++)
            {
                sb.Append(_pluginConfig.LinkCodeCharacters[Random.Range(0, _pluginConfig.LinkCodeCharacters.Length - 1)]);
            }

            return sb.ToString();
        }
        
        public void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _storedData);
        
        public void Chat(IPlayer player, string format) => player.Reply(Lang(LangKeys.ChatFormat, player, format));

        public string Lang(string key, IPlayer player = null, params object[] args)
        {
            try
            {
                return string.Format(lang.GetMessage(key, this, player?.Id), args);
            }
            catch(Exception ex)
            {
                PrintError($"Lang Key '{key}' threw exception\n:{ex}");
                throw;
            }
        }

        #endregion

        #region Classes
        public class PluginConfig
        {
            [DefaultValue("")]
            [JsonProperty(PropertyName = "Discord API Key")]
            public string DiscordApiKey { get; set; }
            
            [DefaultValue(false)]
            [JsonProperty(PropertyName = "Enable Discord Extension Debugging")]
            public bool ExtensionDebugging { get; set; }

            [DefaultValue("")]
            [JsonProperty(PropertyName = "Bot Guild ID (Can be left blank if bot in only 1 guild)")]
            public string BotGuildId { get; set; }

            [DefaultValue("")]
            [JsonProperty(PropertyName = "Discord Join Code")]
            public string JoinCode { get; set; }
            
            [DefaultValue("")]
            [JsonProperty(PropertyName = "Discord Server Name Override")]
            public string ServerNameOverride { get; set; }

            [DefaultValue(true)]
            [JsonProperty(PropertyName = "Enable Commands In Bot Channel")]
            public bool EnableBotChannel { get; set; }

            [DefaultValue("bot")]
            [JsonProperty(PropertyName = "Bot Channel Name or Id")]
            public string BotChannel { get; set; }
            
            [DefaultValue("")]
            [JsonProperty(PropertyName = "Link / Unlink Announcement Channel Name or Id")]
            public string LinkUnlinkAnnouncementChannel { get; set; }

            [DefaultValue("dc")]
            [JsonProperty(PropertyName = "In Game Chat Command")]
            public string GameChatCommand { get; set; }
            
            [DefaultValue("join")]
            [JsonProperty(PropertyName = "In Game Join Command")]
            public string InGameJoinCommand { get; set; }
            
            [DefaultValue("leave")]
            [JsonProperty(PropertyName = "In Game Leave Command")]
            public string InGameLeaveCommand { get; set; }

            [DefaultValue("join")]
            [JsonProperty(PropertyName = "Discord Bot Join Command")]
            public string DiscordJoinCommand { get; set; }

            [DefaultValue("plugins")]
            [JsonProperty(PropertyName = "Discord Bot Plugins Command")]
            public string PluginsCommand { get; set; }

            [DefaultValue("help")]
            [JsonProperty(PropertyName = "Discord Bot Help Command")]
            public string HelpCommand { get; set; }

            [DefaultValue("leave")]
            [JsonProperty(PropertyName = "Discord Bot Leave Command")]
            public string LeaveCommand { get; set; }

            [DefaultValue("commands")]
            [JsonProperty(PropertyName = "Discord Bot Commands Command")]
            public string CommandsCommand { get; set; }
            
            [DefaultValue(true)]
            [JsonProperty(PropertyName = "Enable Discord Server Join Notification")]
            public bool EnabledJoinNotifications { get; set; }
            
            [DefaultValue(true)]
            [JsonProperty(PropertyName = "Enable Linking Discord To Rust")]
            public bool EnableLinking { get; set; }
            
            [DefaultValue(false)]
            [JsonProperty(PropertyName = "Use DiscordAuth")]
            public bool UseDiscordAuth { get; set; }
            
            [DefaultValue(false)]
            [JsonProperty(PropertyName = "Use DiscordConnect")]
            public bool UseDiscordConnect { get; set; }
            
            [DefaultValue(false)]
            [JsonProperty(PropertyName = "Use External Hooks")]
            public bool UseHooks { get; set; }
            
            [DefaultValue("0123456789")]
            [JsonProperty(PropertyName = "Discord Linking Code Characters")]
            public string LinkCodeCharacters { get; set; }
            
            [DefaultValue(6)]
            [JsonProperty(PropertyName = "Discord Linking Code Length")]
            public int LinkCodeLength { get; set; }
            
            [DefaultValue(120)]
            [JsonProperty(PropertyName = "Time until heartbeat is considered to have timed out.")]
            public int HeartbeatTimeoutDuration { get; set; }
            
            [DefaultValue(true)]
            [JsonProperty(PropertyName = "Enable join command not being used in DM message (Non Bot Channel)")]
            public bool EnableJoinWrongChannelNonBotChannel { get; set; }
            
            [DefaultValue(true)]
            [JsonProperty(PropertyName = "Enable join command not being used in DM message (Bot Channel)")]
            public bool EnableJoinWrongChannelBotChannel { get; set; }
        }

        public class StoredData
        {
            public Hash<string, DiscordInfo> PlayerDiscordInfo = new Hash<string, DiscordInfo>();
            public Hash<string, DiscordInfo> LeftPlayerInfo = new Hash<string, DiscordInfo>();

            public DiscordInfo GetInfoByDiscordId(string id)
            {
                return PlayerDiscordInfo.Values.FirstOrDefault(info => info.DiscordId == id);
            }
        }

        public class DiscordInfo
        {
            public string DiscordId { get; set; }
            public string PlayerId { get; set; }
        }

        public class InGameActivation
        {
            public string Code { get; set; }
            public string PlayerId { get; set; }
            public string ChannelId { get; set; }
            public string DisplayName { get; set; }
        }

        public class DiscordActivation
        {
            public string Code { get; set; }
            public string DiscordId { get; set; }
            public string ChannelId { get; set; }
        }

        public class DiscordCommand
        {
            public string PluginName { get; set; }
            public Plugin Plugin { get; set; }
            public Func<IPlayer, string, string, string[], object> Method { get; set; }
            public string HelpText { get; set; }
            public string Permission { get; set; }
            public bool AllowInBotChannel { get; set; }
        }

        public class ChannelSubscription
        {
            public string PluginName { get; set; }
            public Func<Message, object> Method { get; set; }
        }

        public static class LangKeys
        {
            public const string NoPermission = nameof(NoPermission);
            public const string ChatFormat = nameof(ChatFormat)+"V1";
            public const string UnknownCommand = nameof(UnknownCommand);
            public const string DiscordLeave = nameof(DiscordLeave);
            public const string DiscordLeaveFailed = nameof(DiscordLeaveFailed);
            public const string JoinAlreadySignedUp = nameof(JoinAlreadySignedUp)+"V1";
            public const string JoinInvalidSyntax = nameof(JoinInvalidSyntax)+"V1";
            public const string JoinUnableToFindUser = nameof(JoinUnableToFindUser)+"V2";
            public const string JoinReceivedPm = nameof(JoinReceivedPm)+"V2";
            public const string JoinPleaseEnterCode = nameof(JoinPleaseEnterCode);
            public const string DiscordJoinInvalidSyntax = nameof(DiscordJoinInvalidSyntax)+"V1";
            public const string DiscordJoinNoPendingActivations = nameof(DiscordJoinNoPendingActivations);
            public const string DiscordJoinSuccessfullyRegistered = nameof(DiscordJoinSuccessfullyRegistered);
            public const string DiscordCommandsPreText = nameof(DiscordCommandsPreText)+"V1";
            public const string DiscordCommandsBotChannelAllowed = nameof(DiscordCommandsBotChannelAllowed);
            public const string DiscordPlugins =nameof(DiscordPlugins)+"V1";
            public const string DiscordJoinWrongChannel = nameof(DiscordJoinWrongChannel)+"V1";
            public const string DiscordCoreLinkingNotEnabled = nameof(DiscordCoreLinkingNotEnabled);
            public const string DiscordNotAllowedBotChannel = nameof(DiscordNotAllowedBotChannel);
            public const string DiscordMustSignUpBeforeCommands = nameof(DiscordMustSignUpBeforeCommands)+"V1";
            public const string DiscordPlayerIsServerConsole = nameof(DiscordPlayerIsServerConsole);
            public const string DiscordMustSignUpBeforeBotCommands = nameof(DiscordMustSignUpBeforeBotCommands);
            public const string DiscordClientNotConnected =nameof(DiscordClientNotConnected);
            public const string GameJoinCodeNotMatch = nameof(GameJoinCodeNotMatch);
            public const string DiscordCompleteActivation = nameof(DiscordCompleteActivation)+"V2";
            public const string ConsolePlayerNotSupported = nameof(ConsolePlayerNotSupported);
            public const string DiscordUserJoin = nameof(DiscordUserJoin)+"V1";
            public const string NotLinked = nameof(NotLinked);
            public const string HelpNotLinked =nameof(HelpNotLinked)+"V1";
            public const string HelpLinked =nameof(HelpLinked)+"V1";
            public const string HelpText = nameof(HelpText)+"V3";
            public const string DiscordAuthNotLinked = nameof(DiscordAuthNotLinked);
            public const string DiscordConnectNotLinked = nameof(DiscordConnectNotLinked);
            public const string DiscordHooksNotLinked = nameof(DiscordHooksNotLinked);
            public const string LinkNotification = nameof(LinkNotification);
            public const string RejoinNotification = nameof(RejoinNotification);
            public const string UnlinkNotification = nameof(UnlinkNotification);
        }
        #endregion
    }
}
