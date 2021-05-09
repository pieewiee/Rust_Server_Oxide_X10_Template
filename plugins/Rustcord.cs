using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Globalization;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;
using Oxide.Ext.Discord;
using Oxide.Ext.Discord.Attributes;
using Oxide.Ext.Discord.DiscordObjects;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Rustcord", "Kirollos & OuTSMoKE", "2.5.2")]
    [Description("Complete game server monitoring through discord.")]
    internal class Rustcord : RustPlugin
    {
        [PluginReference] Plugin PrivateMessages, BetterChatMute, Clans, AdminChat, DiscordAuth, AdminHammer, AdminRadar, Vanish, RaidableBases;
        [DiscordClient] private DiscordClient _client;

        private Settings _settings;
        private int? _channelCount;

        private Presence DiscordPresence = new Presence()
        {
            Game = new Ext.Discord.DiscordObjects.Game()
            {
                Type = ActivityType.Game,
                Name = "Rustcord Initializing..."
            }
        };
        private Timer StatusTimer = null;

        private class Settings
        {
            public string Apikey { get; set; }
            public bool AutoReloadPlugin { get; set; }
            public int AutoReloadTime { get; set; }
            public bool EnableBotStatus { get; set; }
            public ulong GameChatIconSteamID { get; set; }
            public string GameChatTag { get; set; }
            public string GameChatTagColor { get; set; }
            public string GameChatNameColor { get; set; }
            public string GameChatTextColor { get; set; }
            public bool LogChat { get; set; }
            public bool LogJoinQuits { get; set; }
            public bool LogDeaths { get; set; }
            public bool LogVehicleSpawns { get; set; }
            public bool LogCrateDrops { get; set; }
            public bool LogUserGroups { get; set; }
            public bool LogPermissions { get; set; }
            public bool LogKickBans { get; set; }
            public bool LogNameChanges { get; set; }
            public bool LogServerCommands { get; set; }
            public bool LogServerMessages { get; set; }
            public bool LogF7Reports { get; set; }
            public bool LogTeams { get; set; }
            public bool LogRCON { get; set; }
            public bool LogPluginAdminHammer { get; set; }
            public bool LogPluginAdminRadar { get; set; }
            public bool LogPluginBetterChatMute { get; set; }
            public bool LogPluginClans { get; set; }
            public bool LogPluginDiscordAuth { get; set; }
            public bool LogPluginPrivateMessages { get; set; }
            public bool LogPluginRaidableBases { get; set; }
            public bool LogPluginSignArtist { get; set; }
            public bool LogPluginVanish { get; set; }
            public bool EnableCustomLogging { get; set; }
            public List<string> CustomLoggingKeywords;
            public string ReportCommand { get; set; }
            private string _Botid { get; set; }
            public string Botid(string id = null)
            {
                if (id != null)
                {
                    _Botid = id;
                }
                return _Botid;
            }
            public string Commandprefix { get; set; }
            public List<Channel> Channels { get; set; }
            public Dictionary<string, List<string>> Commandroles { get; set; }

            public class Channel
            {
                public string Channelid { get; set; }
                public List<string> perms { get; set; }
            }
            public List<string> FilterWords;
            public string FilteredWord;
            public List<string> LogExcludeGroups;
            public List<string> LogExcludePerms;
        }

        enum CacheType
        {
            OnPlayerChat = 0,
            OnPlayerConnected = 1,
            OnPlayerDisconnected = 2,
            OnPlayerJoin = 3
        }

        Dictionary<CacheType, Dictionary<BasePlayer, Dictionary<string, string>>> cache = new Dictionary<CacheType, Dictionary<BasePlayer, Dictionary<string, string>>>();
        private string rbdiff;

        Dictionary<string, string> GetPlayerCache(BasePlayer player, string message, CacheType type)
        {
            switch (type)
            {
                case CacheType.OnPlayerChat:
                    {
                        Dictionary<string, string> dict;
                        if (!cache[CacheType.OnPlayerChat].TryGetValue(player, out dict))
                        {
                            cache[CacheType.OnPlayerChat].Add(player, dict = new Dictionary<string, string>
                            {
                                ["playername"] = player.displayName,
                                ["message"] = message,
                                ["playersteamid"] = player.UserIDString
                            });
                        }

                        dict["playername"] = player.displayName;
                        dict["message"] = message;
                        dict["playersteamid"] = player.UserIDString;
                        return dict;
                    }
                case CacheType.OnPlayerConnected:
                    {
                        Dictionary<string, string> dict;
                        if (!cache[CacheType.OnPlayerConnected].TryGetValue(player, out dict))
                        {
                            cache[CacheType.OnPlayerConnected].Add(player, dict = new Dictionary<string, string>
                            {
                                ["playername"] = player.displayName,
                                ["playerip"] = message.Substring(0, message.IndexOf(":")),
                                ["playersteamid"] = player.UserIDString
                            });
                        }

                        dict["playername"] = player.displayName;
                        dict["playerip"] = message.Substring(0, message.IndexOf(":"));
                        dict["playersteamid"] = player.UserIDString;
                        return dict;
                    }
                case CacheType.OnPlayerJoin:
                    {
                        Dictionary<string, string> dict;
                        if (!cache[CacheType.OnPlayerJoin].TryGetValue(player, out dict))
                        {
                            cache[CacheType.OnPlayerDisconnected].Add(player, dict = new Dictionary<string, string>
                            {
                                ["playername"] = player.displayName
                            });
                        }

                        dict["playername"] = player.displayName;
                        return dict;
                    }
                case CacheType.OnPlayerDisconnected:
                default:
                    {
                        Dictionary<string, string> dict;
                        if (!cache[CacheType.OnPlayerDisconnected].TryGetValue(player, out dict))
                        {
                            cache[CacheType.OnPlayerDisconnected].Add(player, dict = new Dictionary<string, string>
                            {
                                ["playername"] = player.displayName,
                                ["reason"] = message,
                            });
                        }

                        dict["playername"] = player.displayName;
                        dict["reason"] = message;
                        return dict;
                    }
            }
        }

        //CONFIG FILE
        private Settings GetDefaultSettings()
        {
            return new Settings
            {
                Apikey = "BotToken",
                AutoReloadPlugin = false,
                AutoReloadTime = 901,
                Commandprefix = "!",
                GameChatIconSteamID = 76561199066612103,
                GameChatTag = "[RUSTCORD]",
                GameChatTagColor = "#7289DA",
                GameChatNameColor = "#55aaff",
                GameChatTextColor = "#ffffff",
                ReportCommand = "report",
                LogChat = true,
                LogJoinQuits = true,
                LogDeaths = false,
                LogVehicleSpawns = false,
                LogCrateDrops = false,
                LogUserGroups = false,
                LogPermissions = false,
                LogKickBans = true,
                LogNameChanges = false,
                LogServerCommands = false,
                LogServerMessages = false,
                LogF7Reports = false,
                LogTeams = false,
                LogRCON = false,
                LogPluginAdminHammer = false,
                LogPluginAdminRadar = false,
                LogPluginBetterChatMute = false,
                LogPluginClans = false,
                LogPluginDiscordAuth = false,
                LogPluginPrivateMessages = false,
                LogPluginRaidableBases = false,
                LogPluginSignArtist = false,
                LogPluginVanish = false,
                EnableCustomLogging = false,
                EnableBotStatus = false,
                CustomLoggingKeywords = new List<string>() {
                    "Keyword 1",
                    "Keyword 2"
                },
                Channels = new List<Settings.Channel>
                    {
                        new Settings.Channel
                            {
                                Channelid = string.Empty,
                                perms = new List<string>
                                {
                                    "cmd_allow",
                                    "cmd_players",
                                    "cmd_kick",
                                    "cmd_com",
                                    "cmd_mute",
                                    "cmd_unmute",
                                    "msg_join",
                                    "msg_joinlog",
                                    "msg_quit",
                                    "death_pvp",
                                    "msg_chat",
                                    "msg_teamchat",
                                    "game_report",
                                    "game_bug",
                                    "msg_serverinit"
                                }
                            }
                    },
                Commandroles = new Dictionary<string, List<string>>
                    {
                        {
                            "command", new List<string>()
                                {
                                    "rolename1",
                                    "rolename2"
                                }
                        }
                    },
                FilterWords = new List<string>
                    {
                        "badword1",
                        "badword2"
                    },
                FilteredWord = "<censored>",
                LogExcludeGroups = new List<string>
                    {
                        "default"
                    },
                LogExcludePerms = new List<string>
                    {
                        "example.permission"
                    }
            };
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Attempting to create default config...");
            Config.Clear();
            Config.WriteObject(GetDefaultSettings(), true);
            Config.Save();
        }
        //END CONFIG FILE


        private void OnServerInitialized()
        {
            var reloadtime = _settings.AutoReloadTime;

            permission.RegisterPermission("rustcord.hidejoinquit", this);
            permission.RegisterPermission("rustcord.hidechat", this);

            if (_settings.AutoReloadPlugin && _settings.AutoReloadTime > 59)
            {
                timer.Every(reloadtime, () => Reload());
            }

            if (_client != null)
            {
                for (int i = 0; i < _channelCount; i++)
                {
                    if (_settings.Channels[i].perms.Contains("msg_serverinit"))
                    {
                        GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                        {
                            chan.CreateMessage(_client, Translate("RUST_OnInitMsg"));
                        });
                    }
                }
            }
        }
        void OnServerShutdown()
        {

            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("msg_serverinit"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnServerShutdown"));
                    });
                }
            }
        }

        void SubscribeHooks()
        {
            if (_settings.LogChat) Subscribe(nameof(OnPlayerChat));
            if (_settings.LogJoinQuits)
            {
                Subscribe(nameof(OnPlayerConnected));
                Subscribe(nameof(OnPlayerDisconnected));
            }
            if (_settings.LogDeaths) Subscribe(nameof(OnDeathNotice));
            if (_settings.LogVehicleSpawns) Subscribe(nameof(OnEntitySpawned));
            if (_settings.LogCrateDrops)
            {
                Subscribe(nameof(OnCrateDropped));
                Subscribe(nameof(OnSupplyDropLanded));
            }
            if (_settings.LogUserGroups)
            {
                Subscribe(nameof(OnGroupCreated));
                Subscribe(nameof(OnGroupDeleted));
                Subscribe(nameof(OnUserGroupAdded));
                Subscribe(nameof(OnUserGroupRemoved));
            }
            if (_settings.LogPermissions)
            {
                Subscribe(nameof(OnUserPermissionGranted));
                Subscribe(nameof(OnGroupPermissionGranted));
                Subscribe(nameof(OnUserPermissionRevoked));
                Subscribe(nameof(OnGroupPermissionRevoked));
            }
            if (_settings.LogKickBans)
            {
                Subscribe(nameof(OnUserKicked));
                Subscribe(nameof(OnUserBanned));
                Subscribe(nameof(OnUserUnbanned));
            }
            if (_settings.LogNameChanges) Subscribe(nameof(OnUserNameUpdated));
            if (_settings.LogServerMessages) Subscribe(nameof(OnServerMessage));
            if (_settings.LogServerCommands) Subscribe(nameof(OnServerCommand));
            if (_settings.LogF7Reports) Subscribe(nameof(OnPlayerReported));
            if (_settings.LogTeams)
            {
                Subscribe(nameof(OnTeamCreated));
                Subscribe(nameof(OnTeamAcceptInvite));
                Subscribe(nameof(OnTeamLeave));
                Subscribe(nameof(OnTeamKick));
                Subscribe(nameof(OnTeamDisbanded));
            }
            if (_settings.LogRCON)
            {
                Subscribe(nameof(OnRconConnection));
            }
            if (_settings.LogPluginAdminHammer)
            {
                Subscribe(nameof(OnAdminHammerEnabled));
                Subscribe(nameof(OnAdminHammerDisabled));
            }
            if (_settings.LogPluginAdminRadar)
            {
                Subscribe(nameof(OnRadarActivated));
                Subscribe(nameof(OnRadarDeactivated));
            }
            if (_settings.LogPluginBetterChatMute)
            {
                Subscribe(nameof(OnBetterChatMuted));
                Subscribe(nameof(OnBetterChatTimeMuted));
                Subscribe(nameof(OnBetterChatUnmuted));
                Subscribe(nameof(OnBetterChatMuteExpired));
            }
            if (_settings.LogPluginClans)
            {
                Subscribe(nameof(OnClanCreate));
                Subscribe(nameof(OnClanDestroy));
                Subscribe(nameof(OnClanChat));
            }
            if (_settings.LogPluginPrivateMessages) Subscribe(nameof(OnPMProcessed));
            if (_settings.LogPluginRaidableBases)
            {
                Subscribe(nameof(OnRaidableBaseStarted));
                Subscribe(nameof(OnRaidableBaseEnded));
            }
            if (_settings.LogPluginSignArtist) Subscribe(nameof(OnImagePost));
            if (_settings.LogPluginDiscordAuth)
            {
                Subscribe(nameof(OnAuthenticate));
                Subscribe(nameof(OnDeauthenticate));
            }
            if (_settings.LogPluginVanish)
            {
                Subscribe(nameof(OnVanishDisappear));
                Subscribe(nameof(OnVanishReappear));
            }
        }
        private void Init()
        {
            cache[CacheType.OnPlayerChat] = new Dictionary<BasePlayer, Dictionary<string, string>>();
            cache[CacheType.OnPlayerConnected] = new Dictionary<BasePlayer, Dictionary<string, string>>();
            cache[CacheType.OnPlayerDisconnected] = new Dictionary<BasePlayer, Dictionary<string, string>>();
            cache[CacheType.OnPlayerJoin] = new Dictionary<BasePlayer, Dictionary<string, string>>();
            UnsubscribeHooks();
        }

        void UnsubscribeHooks()
        {
            Unsubscribe(nameof(OnPlayerChat));
            Unsubscribe(nameof(OnPlayerConnected));
            Unsubscribe(nameof(OnPlayerDisconnected));
            Unsubscribe(nameof(OnDeathNotice));
            Unsubscribe(nameof(OnEntitySpawned));
            Unsubscribe(nameof(OnCrateDropped));
            Unsubscribe(nameof(OnSupplyDropLanded));
            Unsubscribe(nameof(OnGroupCreated));
            Unsubscribe(nameof(OnGroupDeleted));
            Unsubscribe(nameof(OnUserGroupAdded));
            Unsubscribe(nameof(OnUserGroupRemoved));
            Unsubscribe(nameof(OnUserPermissionGranted));
            Unsubscribe(nameof(OnGroupPermissionGranted));
            Unsubscribe(nameof(OnUserPermissionRevoked));
            Unsubscribe(nameof(OnGroupPermissionRevoked));
            Unsubscribe(nameof(OnUserKicked));
            Unsubscribe(nameof(OnUserBanned));
            Unsubscribe(nameof(OnUserUnbanned));
            Unsubscribe(nameof(OnUserNameUpdated));
            Unsubscribe(nameof(OnServerMessage));
            Unsubscribe(nameof(OnServerCommand));
            Unsubscribe(nameof(OnPlayerReported));
            Unsubscribe(nameof(OnTeamCreated));
            Unsubscribe(nameof(OnTeamAcceptInvite));
            Unsubscribe(nameof(OnTeamLeave));
            Unsubscribe(nameof(OnTeamKick));
            Unsubscribe(nameof(OnTeamDisbanded));
            Unsubscribe(nameof(OnRconConnection));
            Unsubscribe(nameof(OnAdminHammerEnabled));
            Unsubscribe(nameof(OnAdminHammerDisabled));
            Unsubscribe(nameof(OnRadarActivated));
            Unsubscribe(nameof(OnRadarDeactivated));
            Unsubscribe(nameof(OnBetterChatTimeMuted));
            Unsubscribe(nameof(OnBetterChatMuted));
            Unsubscribe(nameof(OnBetterChatTimeMuted));
            Unsubscribe(nameof(OnBetterChatUnmuted));
            Unsubscribe(nameof(OnBetterChatMuteExpired));
            Unsubscribe(nameof(OnClanCreate));
            Unsubscribe(nameof(OnClanDestroy));
            Unsubscribe(nameof(OnClanChat));
            Unsubscribe(nameof(OnPMProcessed));
            Unsubscribe(nameof(OnImagePost));
            Unsubscribe(nameof(OnAuthenticate));
            Unsubscribe(nameof(OnDeauthenticate));
            Unsubscribe(nameof(OnRaidableBaseStarted));
            Unsubscribe(nameof(OnRaidableBaseEnded));
        }

        private void Loaded()
        {
            _settings = Config.ReadObject<Settings>();

            // Make sure objects are not taken off the config, otherwise some parts of code will release NRE.
            if (_settings.Channels == null)
                _settings.Channels = new List<Settings.Channel>();
            if (_settings.Commandroles == null)
                _settings.Commandroles = new Dictionary<string, List<string>>();
            if (_settings.FilterWords == null)
                _settings.FilterWords = new List<string>();
            if (_settings.LogExcludeGroups == null)
                _settings.LogExcludeGroups = new List<string>();
            if (_settings.LogExcludePerms == null)
                _settings.LogExcludePerms = new List<string>();
            if (_settings.CustomLoggingKeywords == null)
                _settings.CustomLoggingKeywords = new List<string>();
            if (_settings.ReportCommand == null)
                _settings.ReportCommand = "report";
            if (string.IsNullOrEmpty(_settings.GameChatTag))
                _settings.GameChatTag = "[Rustcord]";
            if (string.IsNullOrEmpty(_settings.GameChatTagColor))
                _settings.GameChatTagColor = "#7289DA";
            if (string.IsNullOrEmpty(_settings.GameChatNameColor))
                _settings.GameChatNameColor = "#55aaff";
            if (string.IsNullOrEmpty(_settings.GameChatTextColor))
                _settings.GameChatTextColor = "#ffffff";
            if (_settings.GameChatIconSteamID.Equals(0uL))
                _settings.GameChatIconSteamID = 76561199066612103;

            Config.WriteObject(_settings, true);
            // ------------------------------------------------------------------------

            if (string.IsNullOrEmpty(_settings.Apikey) || _settings.Apikey == null || _settings.Apikey == "BotToken")
            {
                PrintError("API key is empty or invalid!");
                return;
            }
            bool flag = true;
            try
            {
                Oxide.Ext.Discord.Discord.CreateClient(this, _settings.Apikey);
            }
            catch (Exception e)
            {
                flag = false;
                PrintError($"Rustcord failed to create client! Exception message: {e.Message}");
            }

            if (flag)
            {
                cmd.AddChatCommand(_settings.ReportCommand, this, "cmdReport");
                cmd.AddChatCommand("bug", this, "cmdBug");
                SubscribeHooks();
            }
            if (_settings.EnableCustomLogging)
                UnityEngine.Application.logMessageReceived += ConsoleLog;
        }

        private void Reload()
        {
            rust.RunServerCommand("oxide.reload Rustcord");
        }

        void Discord_Ready(Oxide.Ext.Discord.DiscordEvents.Ready rdy)
        {
            _settings.Botid(rdy.User.id);
            SubscribeHooks();
            _channelCount = _settings?.Channels.Count;
            if (_settings.EnableBotStatus)
            {
                NextFrame(() =>
                {
                    if (StatusTimer != null && !StatusTimer.Destroyed)
                    {
                        StatusTimer.Destroy();
                    }
                    StatusTimer = timer.Every(6f, () =>
                    {
                        var text = new Dictionary<string, string>
                        {
                            ["players"] = Convert.ToString(BasePlayer.activePlayerList.Count),
                            ["maxplayers"] = Convert.ToString(ConVar.Server.maxplayers),
                            ["sleepers"] = Convert.ToString(BasePlayer.sleepingPlayerList.Count)
                        };
                        var msg = Translate("Discord_Status", text);
                        DiscordPresence.Game.Name = string.IsNullOrEmpty(msg) ? "Rustcord initializing...." : msg;
                        _client.UpdateStatus(DiscordPresence);
                    });
                });
            }
        }

        void Discord_GuildCreate(Guild newguild)
        {

            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("msg_plugininit"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, c => {
                        c.CreateMessage(_client, "Rustcord Initialized!");
                    }, newguild.id);
                }
            }
        }

        private void Unload()
        {
            if (StatusTimer != null && !StatusTimer.Destroyed)
            {
                StatusTimer.Destroy();
            }
            if (_settings.EnableCustomLogging)
                UnityEngine.Application.logMessageReceived -= ConsoleLog;
            Discord.CloseClient(_client);
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                { "Discord_PlayersResponse", ":mag_right: Connected Players [{count}/{maxplayers}]: {playerslist}" },
                { "Discord_Status", "{players}/{maxplayers} Online, {sleepers} Sleepers." },
                { "RUST_OnInitMsg", ":vertical_traffic_light: Server is back online! Players may now re-join. :vertical_traffic_light:" },
                { "RUST_OnServerShutdown", ":vertical_traffic_light: Server shutting down. :vertical_traffic_light:" },
                { "RUST_OnPlayerGesture", ":speech_left: {playername}: {gesture}"},
                { "RUST_OnPlayerChat", ":speech_left: {playername}: {message}"},
                { "RUST_OnPlayerTeamChat", ":speech_left: {playername}: {message}"},
                { "RUST_OnPlayerJoin", ":white_check_mark: {playername} has connected!" },
                { "RUST_OnPlayerJoinAdminLog", ":clipboard: {playername} has connected! (IP: {playerip}    SteamID: {playersteamid})" },
                { "RUST_OnPlayerQuit", ":x: {playername} has disconnected! ({reason})" },
                { "RUST_OnPlayerReport", ":warning: {playername}: {message}"},
                { "RUST_OnPlayerBug", ":beetle: {playername}: {message}"},
                { "RUST_OnPlaneSpawn", ":airplane: Cargo Plane has spawned."},
                { "RUST_OnBradleySpawn", ":trolleybus: Bradley APC has spawned."},
                { "RUST_OnShipSpawn", ":ship: Cargo Ship has spawned."},
                { "RUST_OnSupplyDrop", ":airplane: A supply drop has landed."},
                { "RUST_OnHeliSpawn", ":helicopter: Patrol Helicopter has spawned."},
                { "RUST_OnChinookSpawn", ":helicopter: Chinook Helicopter has spawned."},
                { "RUST_OnCrateDropped", ":helicopter: A Chinook has delivered a crate."},
                { "RUST_OnTeamAcceptInvite", ":family_mwgb: {playername} joined {teamleader}'s team. ({teamid})"},
                { "RUST_OnTeamCreated", ":family_mwgb: {playername} created a new team. ({teamid})"},
                { "RUST_OnTeamKicked", ":family_mwgb: {teamleader} kicked {playername} from their team. ({teamid})"},
                { "RUST_OnTeamLeave", ":family_mwgb: {playername} left {teamleader}'s team ({teamid})."},
                { "RUST_OnTeamDisbanded", ":family_mwgb: {teamleader}'s team has been disbanded. ({teamid})"},
                { "RUST_OnGroupCreated", ":desktop: Group {groupname} has been created."},
                { "RUST_OnGroupDeleted", ":desktop: Group {groupname} has been deleted."},
                { "RUST_OnUserGroupAdded", ":desktop: {playername} ({steamid}) has been added to group: {group}."},
                { "RUST_OnUserGroupRemoved", ":desktop: {playername} ({steamid}) has been removed from group: {group}."},
                { "RUST_OnUserPermissionGranted", ":desktop: {playername} ({steamid}) has been granted permission: {permission}."},
                { "RUST_OnGroupPermissionGranted", ":desktop: Group {group} has been granted permission: {permission}."},
                { "RUST_OnUserPermissionRevoked", ":desktop: {playername} ({steamid}) has been revoked permission: {permission}."},
                { "RUST_OnGroupPermissionRevoked", ":desktop: Group {group} has been revoked permission: {permission}."},
                { "RUST_OnPlayerKicked", ":desktop: {playername} has been kicked for: {reason}"},
                { "RUST_OnPlayerBanned", ":desktop: {playername} ({steamid}/{ip}) has been banned for: {reason}"}, //only works with vanilla/native system atm
				{ "RUST_OnPlayerUnBanned", ":desktop: {playername} ({steamid}/{ip}) has been unbanned."}, //only works with vanilla/native system atm
				{ "RUST_OnPlayerNameChange", ":desktop: {oldname} ({steamid}) is now playing as {newname}."},
                { "RUST_OnPlayerReported", ":desktop: {reporter} reported {targetplayer} ({targetsteamid}) to Facepunch for {reason}. Message: {message}"},
                { "RUST_OnF1ItemSpawn", ":desktop: {name}: {givemessage}."},
                { "RUST_OnNoteUpdate", ":desktop: [NOTES] {playername}: {notemessage}."},
                { "RUST_OnRCONConnected", ":desktop: [RCON] New connection from: {ip}."},
                { "PLUGIN_AdminHammer_Enabled", ":hammer: {player} has enabled Admin Hammer."},
                { "PLUGIN_AdminHammer_Disabled", ":hammer: {player} has disabled Admin Hammer."},
                { "PLUGIN_AdminRadar_Enabled", ":satellite: {player} has enabled Admin Radar."},
                { "PLUGIN_AdminRadar_Disabled", ":satellite: {player} has disabled Admin Radar."},
                { "PLUGIN_BetterChatMute_Mute", "[MUTE] :zipper_mouth: {muter} has permanently muted {target}. Reason: {reason}"},
                { "PLUGIN_BetterChatMute_UnMute", "[MUTE] :loudspeaker: {unmuter} has unmuted {target}."},
                { "PLUGIN_BetterChatMute_TimedMute", "[MUTE] :hourglass_flowing_sand: {muter} has been temporarily muted {target} for {time}. Reason: {reason}"},
                { "PLUGIN_BetterChatMute_MuteExpire", "[MUTE] :hourglass: {target}'s temporary mute has expired."},
                { "PLUGIN_Clans_Chat", ":speech_left: [CLANS] {playername}: {message}"},
                { "PLUGIN_Clans_CreateClan", ":family_mwgb: Clan [{clan}] has been created."},
                { "PLUGIN_Clans_DisbandClan", ":family_mwgb: Clan [{clan}] has been disbanded."},
                { "PLUGIN_Clans_MemberJoin", ":family_mwgb: {playername} ({steamid}) joined clan: [{clan}]."},
                { "PLUGIN_Clans_MemberLeave", ":family_mwgb: {playername} left clan: [{clan}]."},
                { "PLUGIN_DiscordAuth_Auth", ":lock: {gamename} has linked to Discord account {discordname}."},
                { "PLUGIN_DiscordAuth_Deauth", ":unlock: {gamename} has been unlinked from Discord account {discordname}."},
                { "PLUGIN_PrivateMessages_PM", "[PM] {sender}  :incoming_envelope: {target}: {message}"},
                { "PLUGIN_RaidableBases_Started", ":house: {difficulty} Raidable Base has spawned at {position}."},
                { "PLUGIN_RaidableBases_Ended", ":house: {difficulty} Raidable Base at {position} has ended."},
                { "PLUGIN_SignArtist", "{player} posted an image to a sign.\nPosition: ({position})"},
                { "PLUGIN_Vanish_Disappear", ":ghost: {player} has vanished." },
                { "PLUGIN_Vanish_Reappear", ":ghost: {player} has reappeared." }
            }, this);
        }
        private void OnPlayerChat(BasePlayer player, string message, ConVar.Chat.ChatChannel channel)
        {
            if (_client == null) return;
            if (player == null || message == null) return;
            if (permission.UserHasPermission(player.UserIDString, "rustcord.hidechat")) return;
            if (BetterChatMute?.Call<bool>("API_IsMuted", player.IPlayer) ?? false) return;
            if (_settings.FilterWords != null && _settings.FilterWords.Count > 0)
            {
                for (int i = _settings.FilterWords.Count - 1; i >= 0; i--)
                {
                    while (message.Contains(" " + _settings.FilterWords[i] + " ") || message.Contains(_settings.FilterWords[i]))
                        message = message.Replace(_settings.FilterWords[i], _settings.FilteredWord ?? "");
                }
            }

            var text = GetPlayerCache(player, message, CacheType.OnPlayerChat);

            for (int i = 0; i < _settings.Channels.Count; i++)
            {
                if (_settings.Channels[i].perms.Contains(channel == ConVar.Chat.ChatChannel.Team ? "msg_teamchat" : "msg_chat"))
                {
                    if (!(player.IsValid())) continue;

                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate(channel == ConVar.Chat.ChatChannel.Team ? "RUST_OnPlayerTeamChat" : "RUST_OnPlayerChat", text));

                    });
                }
            }

            text.Clear();
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (_client == null) return;
            if (player == null) return;

            var text = GetPlayerCache(player, player.net.connection.ipaddress, CacheType.OnPlayerConnected);

            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("msg_joinlog"))
                {
                    if (!player.IsValid()) return;

                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        // Admin
                        chan.CreateMessage(_client, Translate("RUST_OnPlayerJoinAdminLog", text));
                    });
                }
            }

            if (permission.UserHasPermission(player.UserIDString, "rustcord.hidejoinquit")) return;

            text = GetPlayerCache(player, null, CacheType.OnPlayerJoin);

            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("msg_join"))
                {
                    if (!player.IsValid()) return;

                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnPlayerJoin", text));
                    });
                }
            }
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (_client == null) return;
            if (player == null || string.IsNullOrEmpty(reason)) return;
            if (permission.UserHasPermission(player.UserIDString, "rustcord.hidejoinquit"))
                return;

            var text = GetPlayerCache(player, reason, CacheType.OnPlayerDisconnected);

            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("msg_quit"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnPlayerQuit", text));
                    });
                }
            }

            cache[CacheType.OnPlayerChat].Remove(player);
            cache[CacheType.OnPlayerConnected].Remove(player);
            cache[CacheType.OnPlayerDisconnected].Remove(player);
            cache[CacheType.OnPlayerJoin].Remove(player);
        }

        private void OnDeathNotice(Dictionary<string, object> data, string message)
        {
            if (_client == null) return;
            if (data["VictimEntityType"] == null || data["KillerEntityType"] == null) return;
            int victimType = (int)data["VictimEntityType"];
            int killerType = (int)data["KillerEntityType"];

            var _DeathNotes = plugins.Find("DeathNotes");

            if (_DeathNotes != null)
                if ((victimType == 5 && (killerType == 5 || killerType == 6 || killerType == 7 || killerType == 8 || killerType == 9 || killerType == 10 || killerType == 11 || killerType == 12 || killerType == 14 || killerType == 15)))
                {
                    message = (string)_DeathNotes.Call("StripRichText", message);
                    for (int i = 0; i < _channelCount; i++)
                    {
                        if (_settings.Channels[i].perms.Contains("death_pvp"))
                        {
                            GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                            {
                                chan.CreateMessage(_client, ":skull_crossbones: " + message);
                            });
                        }
                    }

                }
                else if ((victimType == 2 && killerType == 5) || (victimType == 5 && killerType == 2))
                {
                    message = (string)_DeathNotes.Call("StripRichText", message);

                    for (int i = 0; i < _channelCount; i++)
                    {
                        if (_settings.Channels[i].perms.Contains("death_animal"))
                        {
                            GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                            {
                                chan.CreateMessage(_client, ":skull_crossbones: " + message);
                            });
                        }
                    }

                }
                else if ((victimType == 5 && (killerType == 0 || killerType == 1)) || ((victimType == 0 || victimType == 1) && (killerType == 5)))
                {
                    message = (string)_DeathNotes.Call("StripRichText", message);

                    for (int i = 0; i < _channelCount; i++)
                    {
                        if (_settings.Channels[i].perms.Contains("death_vehicle"))
                        {
                            GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                            {
                                chan.CreateMessage(_client, ":skull_crossbones: " + message);
                            });
                        }
                    }
                }
                else if ((victimType == 5 && (killerType == 3 || killerType == 4)) || ((victimType == 3 || victimType == 4) && (killerType == 5)))
                {
                    message = (string)_DeathNotes.Call("StripRichText", message);

                    for (int i = 0; i < _channelCount; i++)
                    {
                        if (_settings.Channels[i].perms.Contains("death_npc"))
                        {
                            GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                            {
                                chan.CreateMessage(_client, ":skull_crossbones: " + message);
                            });
                        }
                    }

                }
        }

        // TEMP fix until extension calls the hooks in NextFrame
        private void Discord_MessageCreate(Message message)
        {
            NextFrame(() => Discord_MessageCreate_Ex(message));
        }

        private void Discord_MessageCreate_Ex(Message message)
        {
            if ((message.content?.Length ?? 0) == 0) return;
            Settings.Channel channelidx = FindChannelById(message.channel_id);
            if (channelidx == null)
                return;

            if (message.author.id == _settings.Botid()) return;
            if (message.content[0] == _settings.Commandprefix[0])
            {
                if (!channelidx.perms.Contains("cmd_allow"))
                    return;
                string cmd;
                string msg;
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

                msg = message.content.Remove(0, 1 + cmd.Length).Trim();
                cmd = cmd.Trim();
                cmd = cmd.ToLower();

                if (!channelidx.perms.Contains("cmd_" + cmd))
                    return;
                if (!_settings.Commandroles.ContainsKey(cmd))
                {
                    DiscordToGameCmd(cmd, msg, message.author, message.channel_id);
                    return;
                }
                var roles = _settings.Commandroles[cmd];
                if (roles.Count == 0)
                {
                    DiscordToGameCmd(cmd, msg, message.author, message.channel_id);
                    return;
                }

                foreach (var roleid in message.member.roles)
                {
                    var rolename = GetRoleNameById(roleid);
                    if (roles.Contains(rolename))
                    {
                        DiscordToGameCmd(cmd, msg, message.author, message.channel_id);
                        break;
                    }
                }
            }
            else
            {
                var chattag = _settings.GameChatTag;
                var chattagcolor = _settings.GameChatTagColor;
                var chatnamecolor = _settings.GameChatNameColor;
                var chattextcolor = _settings.GameChatTextColor;
                if (!channelidx.perms.Contains("msg_chat")) return;
                string nickname = message.member?.nick ?? "";
                if (nickname.Length == 0)
                    nickname = message.author.username;
                //PrintToChat("<color=" + chattagcolor + ">" + chattag + "</color> " + "<color=" + chatnamecolor + ">" + nickname + ":</color> " + "<color=" + chattextcolor + ">" + message.content + "</color>");
                string text = $"<color={chattagcolor}>{chattag}</color> <color={chatnamecolor}>{nickname}:</color> <color={chattextcolor}>{message.content}</color>";
                foreach (var player in BasePlayer.activePlayerList) Player.Message(player, text, _settings.GameChatIconSteamID);
                Puts("[DISCORD] " + nickname + ": " + message.content);
            }
        }

        private void DiscordToGameCmd(string command, string param, User author, string channelid)
        {
            switch (command)
            {
                case "players":
                    {
                        string listStr = string.Empty;
                        var pList = BasePlayer.activePlayerList;
                        int i = 0;
                        foreach (var player in pList)
                        {
                            listStr += player.displayName + "[" + i++ + "]";
                            if (i != pList.Count)
                                listStr += ", ";

                            if (i % 25 == 0 || i == pList.Count)
                            {
                                var text = new Dictionary<string, string>
                                {
                                    ["count"] = Convert.ToString(BasePlayer.activePlayerList.Count),
                                    ["maxplayers"] = Convert.ToString(ConVar.Server.maxplayers),
                                    ["playerslist"] = listStr
                                };
                                GetChannel(_client, channelid, chan =>
                                {
                                    // Connected Players [{count}/{maxplayers}]: {playerslist}
                                    chan.CreateMessage(_client, Translate("Discord_PlayersResponse", text));
                                });
                                text.Clear();
                                listStr = string.Empty;
                            }
                        }

                        break;
                    }
                case "kick":
                    {
                        if (String.IsNullOrEmpty(param))
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !kick <steam id> <reason>");
                            });
                            return;
                        }
                        string[] _param = param.Split(' ');
                        if (_param.Length < 2)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !kick <steam id> <reason>");
                            });
                            return;
                        }
                        BasePlayer plr = BasePlayer.Find(_param[0]);
                        if (plr == null)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Error: player not found");
                            });
                            return;
                        }
                        plr.Kick(param.Remove(0, _param[0].Length + 1));
                        GetChannel(_client, channelid, chan =>
                        {
                            chan.CreateMessage(_client, "Success: Kick command executed!");
                        });
                        break;
                    }
                case "timeban":
                    {
                        if (string.IsNullOrEmpty(param))
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !timeban <steamid> <name> <duration> <reason>");
                            });
                            return;
                        }
                        string[] _param = param.Split(' ');
                        if (_param.Length < 3)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !timeban <steamid> <name> <duration> <reason>");
                            });
                            return;
                        }
                        var plr = covalence.Players.FindPlayer(_param[0]);
                        if (plr == null)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Error: player not found");
                            });
                            return;
                        }
                        string[] args = new string[4];
                        args[0] = _param[0]; // id
                        args[1] = _param[1]; // name
                        args[2] = "\""; // reason
                        for (int i = 3; i < _param.Length; i++)
                        {
                            args[2] += _param[i];
                            if (i != _param.Length - 1)
                                args[2] += " ";
                        }
                        args[2] += "\"";
                        args[3] = _param[2];
                        this.Server.Command("banid", args);
                        GetChannel(_client, channelid, chan =>
                        {
                            chan.CreateMessage(_client, "Success: Ban command executed!");
                        });
                        break;
                    }
                case "ban":
                    {
                        if (string.IsNullOrEmpty(param))
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !ban <name/id> <reason>");
                            });
                            return;
                        }
                        string[] _param = param.Split(' ');
                        if (_param.Length < 2)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !ban <name/id> <reason>");
                            });
                            return;
                        }
                        var plr = covalence.Players.FindPlayer(_param[0]);
                        if (plr == null)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Error: player not found");
                            });
                            return;
                        }
                        plr.Ban(param.Remove(0, _param[0].Length + 1));
                        GetChannel(_client, channelid, chan =>
                        {
                            chan.CreateMessage(_client, "Success: Ban command executed!");
                        });
                        break;
                    }
                case "unban":
                    {
                        if (string.IsNullOrEmpty(param))
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !unban <name/id>");
                            });
                            return;
                        }
                        string[] _param = param.Split(' ');
                        var plr = covalence.Players.FindPlayer(_param[0]);
                        if (plr == null)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Error: player not found");
                            });
                            return;
                        }
                        plr.Unban();
                        GetChannel(_client, channelid, chan =>
                        {
                            chan.CreateMessage(_client, "Success: Unban command executed!");
                        });
                        break;
                    }
                case "com":
                    {
                        if (String.IsNullOrEmpty(param))
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !com <command>");
                            });
                            return;
                        }
                        string[] _param = param.Split(' ');
                        if (_param.Length > 1)
                        {
                            string[] args = new string[_param.Length - 1];
                            Array.Copy(_param, 1, args, 0, args.Length);
                            this.Server.Command(_param[0], args);
                        }
                        else
                        {
                            this.Server.Command(param);
                        }
                        GetChannel(_client, channelid, chan =>
                        {
                            chan.CreateMessage(_client, "Success: Console command executed!");
                        });
                        break;
                    }
                case "mute":
                    {
                        if (BetterChatMute == null)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "This command requires the Better Chat Mute plugin.");
                                return;
                            });
                        }
                        if (string.IsNullOrEmpty(param))
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !mute <playername/steamid> <time (optional)> <reason (optional)>");
                            });
                            return;
                        }
                        string[] _param = param.Split(' ');
                        if (_param.Length >= 1)
                        {
                            this.Server.Command($"mute {string.Join(" ", _param)}");
                            return;
                        }
                        GetChannel(_client, channelid, chan =>
                        {
                            chan.CreateMessage(_client, "Success: Mute command executed!");
                        });
                        break;
                    }
                case "unmute":
                    {
                        if (BetterChatMute == null)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "This command requires the Better Chat Mute plugin.");
                                return;
                            });
                        }
                        if (String.IsNullOrEmpty(param))
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !unmute <playername/steamid>");
                            });
                            return;
                        }
                        string[] _param = param.Split(' ');
                        if (_param.Length > 1)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !unmute <playername/steamid>");
                            });
                            return;
                        }
                        if (_param.Length == 1)
                        {
                            this.Server.Command($"unmute {string.Join(" ", _param)}");
                            return;
                        }
                        break;
                    }
            }

        }

        //GAME COMMANDS

        // /report [message]
        void cmdReport(BasePlayer player, string command, string[] args)
        {
            if (args.Length < 1)
            {
                SendReply(player, "Syntax: /report [message]");
                return;
            }

            string message = "";
            foreach (string s in args)
                message += (s + " ");

            var dict = new Dictionary<string, string>
            {
                { "playername", player.displayName },
                { "message", message }
            };

            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("game_report"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnPlayerReport", dict));
                    });
                }
            }

            SendReply(player, "Your report has been submitted to Discord.");

        }

        void cmdBug(BasePlayer player, string command, string[] args)
        {
            if (args.Length < 1)
            {
                SendReply(player, "Syntax: /bug [message]");
                return;
            }

            string message = "";
            foreach (string s in args)
                message += (s + " ");

            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("game_bug"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnPlayerBug", new Dictionary<string, string>
                        {
                            { "playername", player.displayName },
                            { "message", message }
                        }));
                    });
                }
            }

            SendReply(player, "Your bug report has been submitted to Discord.");

        }
        //NPC VEHICLE SPAWN LOGGING

        private void OnEntitySpawned(BaseEntity Entity)
        {
            if (Entity == null) return;
            if (Entity is BaseHelicopter)
            {
                for (int i = 0; i < _channelCount; i++)
                {
                    if (_settings.Channels[i].perms.Contains("msg_helispawn"))
                    {
                        GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                        {
                            chan.CreateMessage(_client, Translate("RUST_OnHeliSpawn"));
                        });
                    }
                }
            }
            if (Entity is CargoPlane)
            {
                for (int i = 0; i < _channelCount; i++)
                {
                    if (_settings.Channels[i].perms.Contains("msg_planespawn"))
                    {
                        GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                        {
                            chan.CreateMessage(_client, Translate("RUST_OnPlaneSpawn"));
                        });
                    }
                }
            }
            if (Entity is CargoShip)
            {
                for (int i = 0; i < _channelCount; i++)
                {
                    if (_settings.Channels[i].perms.Contains("msg_shipspawn"))
                    {
                        GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                        {
                            chan.CreateMessage(_client, Translate("RUST_OnShipSpawn"));
                        });
                    }
                }

            }
            if (Entity is CH47Helicopter)
            {
                for (int i = 0; i < _channelCount; i++)
                {
                    if (_settings.Channels[i].perms.Contains("msg_chinookspawn"))
                    {
                        GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                        {
                            chan.CreateMessage(_client, Translate("RUST_OnChinookSpawn"));
                        });
                    }
                }
            }
            if (Entity is BradleyAPC)
            {
                for (int i = 0; i < _channelCount; i++)
                {
                    if (_settings.Channels[i].perms.Contains("msg_bradleyspawn"))
                    {
                        GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                        {
                            chan.CreateMessage(_client, Translate("RUST_OnBradleySpawn"));
                        });
                    }
                }
            }

        }

        private string Translate(string msg, Dictionary<string, string> parameters = null)
        {
            if (string.IsNullOrEmpty(msg))
                return string.Empty;

            msg = lang.GetMessage(msg, this);

            if (parameters != null)
            {
                foreach (var lekey in parameters)
                {
                    if (msg.Contains("{" + lekey.Key + "}"))
                        msg = msg.Replace("{" + lekey.Key + "}", lekey.Value);
                }
            }

            return msg;
        }

        private Settings.Channel FindChannelById(string id)
        {
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].Channelid == id)
                    return _settings.Channels[i];
            }

            return null;
        }

        private void GetChannel(DiscordClient c, string chan_id, Action<Channel> cb, string guildid = null)
        {
            //Guild g = guildid == null ? c.DiscordServers.FirstOrDefault(x => x.channels.FirstOrDefault(y => y.id == chan_id) != null) : c.GetGuild(guildid);
            Guild g = null;
            Channel foundchan = null;
            if (!string.IsNullOrEmpty(guildid))
                g = c.GetGuild(guildid);
            else
                foreach (var G in c.DiscordServers)
                {
                    if (g != null)
                        break;
                    foreach (var C in G.channels)
                    {
                        if (C.id == chan_id)
                        { g = G; foundchan = C; break; }
                    }
                }
            if (g == null)
            {
                PrintWarning($"Rustcord failed to fetch channel! (chan_id={chan_id}). Guild is invalid.");
                return;
            }
            if (g.unavailable ?? false == true)
            {
                PrintWarning($"Rustcord failed to fetch channel! (chan_id={chan_id}). Guild is possibly invalid or not available yet.");
                return;
            }
            //Channel foundchan = g?.channels?.FirstOrDefault(z => z.id == chan_id);
            if (foundchan == null)
            {
                if (guildid != null) return; // Ignore printing error
                PrintWarning($"Rustcord failed to fetch channel! (chan_id={chan_id}).");
                return;
            }
            if (foundchan.id != chan_id) return;
            cb?.Invoke(foundchan);
        }

        private string GetRoleNameById(string id)
        {
            //var role = _client.DiscordServers.FirstOrDefault(x => x.roles.FirstOrDefault(y => y.id == id) != null)?.roles.FirstOrDefault(z => z.id == id);
            //return role?.name ?? "";
            foreach (var r in _client.DiscordServer.roles)
            {
                if (r.id == id)
                    return r.name;
            }
            return String.Empty;
        }

        private IPlayer FindPlayer(string nameorId)
        {
            foreach (var player in covalence.Players.Connected)
            {
                if (player.Id == nameorId)
                    return player;

                if (player.Name == nameorId)
                    return player;
            }

            return null;
        }

        private User FindUserByID(string Id)
        {
            var user = _client.DiscordServers.FirstOrDefault(x => x.members.FirstOrDefault(y => y.user.id == Id) != null)?.members.FirstOrDefault(z => z.user.id == Id).user;
            return user;
        }

        private BasePlayer FindPlayerByID(string Id)
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player.UserIDString == Id)
                    return player;
            }

            return null;
        }

        //CONSOLE LOGGING 

        void OnCrateDropped(HackableLockedCrate crate)
        {
            var dict = new Dictionary<string, string> { };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_cratedrop"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnCrateDropped", dict));
                    });
                }
            }
        }

        void OnSupplyDropLanded(SupplyDrop entity)
        {
            var dict = new Dictionary<string, string> { };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_supplydrop"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnSupplyDrop", dict));
                    });
                }
            }
        }

        void OnGroupCreated(string name)
        {
            var dict = new Dictionary<string, string>
            {
                            { "groupname", name }
            };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_groups"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnGroupCreated", dict));
                    });
                }
            }
        }

        void OnGroupDeleted(string name)
        {
            var dict = new Dictionary<string, string>
            {
                { "groupname", name }
            };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_groups"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnGroupDeleted", dict));
                    });
                }
            }
        }



        void OnUserGroupAdded(string id, string groupName)
        {
            if (_settings.LogExcludeGroups.Contains(groupName))
            {
                return;
            }
            var player = covalence.Players.FindPlayerById(id);
            if (player == null) return;
            var dict = new Dictionary<string, string>
            {
                { "playername", player.Name },
                { "steamid", id },
                { "group", groupName }
            };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_groups"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnUserGroupAdded", dict));
                    });
                }
            }
        }

        void OnUserGroupRemoved(string id, string groupName)
        {
            if (_settings.LogExcludeGroups.Contains(groupName)) return;
            var player = covalence.Players.FindPlayerById(id);
            if (player == null) return;
            var dict = new Dictionary<string, string>
            {
                            { "playername", player.Name },
                            { "steamid", id },
                            { "group", groupName }
            };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_groups"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnUserGroupRemoved", dict));
                    });
                }
            }
        }

        void OnUserPermissionGranted(string id, string permName)
        {
            if (_settings.LogExcludePerms.Contains(permName)) return;
            var player = covalence.Players.FindPlayerById(id);
            if (player == null) return;
            var dict = new Dictionary<string, string>
            {
                            { "playername", player.Name },
                            { "steamid", id },
                            { "permission", permName }
            };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_perms"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnUserPermissionGranted", dict));
                    });
                }
            }
        }

        void OnGroupPermissionGranted(string name, string perm)
        {
            if (_settings.LogExcludePerms.Contains(perm)) return;
            var dict = new Dictionary<string, string>
            {
                            { "group", name },
                            { "permission", perm }
            };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_perms"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnGroupPermissionGranted", dict));
                    });
                }
            }
        }

        void OnUserPermissionRevoked(string id, string permName)
        {
            if (_settings.LogExcludePerms.Contains(permName)) return;
            var player = covalence.Players.FindPlayerById(id);
            if (player == null) return;
            var dict = new Dictionary<string, string>
            {
                            { "playername", player.Name },
                            { "steamid", id },
                            { "permission", permName }
            };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_perms"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnUserPermissionRevoked", dict));
                    });
                }
            }
        }

        void OnGroupPermissionRevoked(string name, string perm)
        {
            if (_settings.LogExcludePerms.Contains(perm)) return;
            var dict = new Dictionary<string, string>
            {
                        { "group", name },
                        { "permission", perm }
            };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_perms"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnGroupPermissionRevoked", dict));
                    });
                }
            }
        }

        void OnUserKicked(IPlayer player, string reason)
        {
            var dict = new Dictionary<string, string>
            {
                            { "playername", player.Name },
                            { "reason", reason }
            };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_kicks"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnPlayerKicked", dict));
                    });
                }
            }
        }

        void OnUserBanned(string name, string bannedId, string address, string reason)
        {
            var dict = new Dictionary<string, string>
            {
                        { "playername", name },
                        { "steamid", bannedId },
                        { "ip", address },
                        { "reason", reason }
            };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_bans"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnPlayerBanned", dict));
                    });
                }
            }

        }

        private void OnUserUnbanned(string name, string id, string ip)
        {
            var dict = new Dictionary<string, string>
                        {
                            { "playername", name },
                            { "steamid", id },
                            { "ip", ip }
                        };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_bans"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnPlayerUnBanned", dict));
                    });
                }
            }
        }

        void OnUserNameUpdated(string id, string oldName, string newName) //TESTING FUNCTION
        {
            if ((oldName == newName) || (oldName == "Unnamed")) return;
            var dict = new Dictionary<string, string>
                        {
                            { "oldname", oldName },
                            { "newname", newName },
                            { "steamid", id }
                        };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_namechange"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnPlayerNameChange", dict));
                    });
                }
            }
        }

        private object OnServerMessage(string message, string name)
        {
            if (message.Contains("gave") && name == "SERVER")
            {
                var dict = new Dictionary<string, string>
                        {
                                { "name", name },
                                { "givemessage", message }
                        };
                for (int i = 0; i < _channelCount; i++)
                {
                    if (_settings.Channels[i].perms.Contains("log_admingive"))
                    {
                        GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                        {
                            chan.CreateMessage(_client, Translate("RUST_OnF1ItemSpawn", dict));
                        });
                    }
                }
            }

            return null;
        }

        private void OnServerCommand(ConsoleSystem.Arg arg)
        {
            var player1 = arg.Player();
            var emote = arg.GetString(0);

            if (arg.cmd.Name == "gesture")
            {
                if (_emotes.ContainsKey(emote))
                {
                    var emoji = _emotes[emote];
                    var dict = new Dictionary<string, string>
                        {
                                    {"playername", player1.displayName },
                                    {"gesture", emoji }
                        };
                    for (int i = 0; i < _channelCount; i++)
                    {
                        if (_settings.Channels[i].perms.Contains("msg_gestures"))
                        {
                            GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                            {
                                chan.CreateMessage(_client, Translate("RUST_OnPlayerGesture", dict));
                            });
                        }
                    }
                }
            }
            if (arg.cmd.FullName == "note.update")
            {
                BasePlayer player = arg.Connection.player as BasePlayer;
                if (player == null)
                    return;
                var notemsg = arg.GetString(1, string.Empty);
                var dict = new Dictionary<string, string>
                        {
                                { "playername", player.displayName },
                                { "notemessage", notemsg }
                        };
                for (int i = 0; i < _channelCount; i++)
                {
                    if (_settings.Channels[i].perms.Contains("log_itemnote"))
                    {
                        GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                        {
                            chan.CreateMessage(_client, Translate("RUST_OnNoteUpdate", dict));
                        });
                    }
                }
            }
        }
        private readonly Dictionary<string, string> _emotes = new Dictionary<string, string>
        {
            ["wave"] = ":wave:",
            ["shrug"] = ":shrug:",
            ["victory"] = ":trophy:",
            ["thumbsup"] = ":thumbsup:",
            ["chicken"] = ":chicken:",
            ["hurry"] = ":runner:",
            ["whoa"] = ":flag_white:"
        };
        void OnPlayerReported(BasePlayer reporter, string targetName, string targetId, string subject, string message, string type)
        {
            var dict = new Dictionary<string, string>
             {
                            { "reporter", reporter.displayName },
                            { "targetplayer", targetName },
                            { "targetsteamid", targetId },
                            { "reason", subject },
                            { "message", message }
            };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_f7reports"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnPlayerReported", dict));
                    });
                }
            }
        }
        /*void OnTeamCreate(BasePlayer player)
        {
            var dict = new Dictionary<string, string>
            {
                { "playername", player.displayName }
            };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_teams"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnTeamCreated", dict));
                    });
                }
            }

        }*/
        void OnTeamCreated(BasePlayer player, RelationshipManager.PlayerTeam team)
        {
            var dict = new Dictionary<string, string>
            {
                { "playername", player.displayName },
                { "teamid", team.teamID.ToString() }
            };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_teams"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnTeamCreated", dict));
                    });
                }
            }
        }
        void OnTeamAcceptInvite(RelationshipManager.PlayerTeam team, BasePlayer player)
        {
            var dict = new Dictionary<string, string>
            {
                 { "playername", player.displayName },
                 { "teamleader", team.GetLeader().displayName },
                 { "teamid", team.teamID.ToString() }
            };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_teams"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnTeamAcceptInvite", dict));
                    });
                }
            }
        }
        void OnTeamLeave(RelationshipManager.PlayerTeam team, BasePlayer player)
        {
            if (player == team.GetLeader()) return;
            if ((team == null)||(player == null)) return;
            var dict = new Dictionary<string, string>
            {
                            { "playername", player.displayName },
                            { "teamleader", team.GetLeader().displayName },
                            { "teamid", team.teamID.ToString() }
            };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_teams"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnTeamLeave", dict));
                    });
                }
            }
        }
        void OnTeamKick(RelationshipManager.PlayerTeam team, BasePlayer player, ulong target)
        {
            Puts($"{player?.displayName} | {player?.currentTeam} | {team} | {team?.GetLeader()?.displayName} | {team?.teamID.ToString()}");
            var targetplayer = FindPlayerByID(target.ToString());
            var dict = new Dictionary<string, string>
                        {
                            { "playername", targetplayer.displayName },
                            { "teamleader", team.GetLeader().displayName },
                            { "teamid", team.teamID.ToString() }
                        };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_teams"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnTeamKicked", dict));
                    });
                }
            }
        }
        void OnTeamDisbanded(RelationshipManager.PlayerTeam team)
        {
            var dict = new Dictionary<string, string>
            {
                            { "teamleader", team.GetLeader().displayName },
                            { "teamid", team.teamID.ToString() }
            };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_teams"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnTeamDisbanded", dict));
                    });
                }
            }
        }

        private void ConsoleLog(string condition, string stackTrace, LogType type)
        {
            if (!string.IsNullOrEmpty(condition) && _settings.CustomLoggingKeywords.Any(condition.Contains))
            {
                for (int i = 0; i < _channelCount; i++)
                {
                    if (_settings.Channels[i].perms.Contains("log_console"))
                    {
                        GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                        {
                            chan.CreateMessage(_client, "[Log] " + condition);
                        });
                    }
                }
            }
        }

        //RCON SUPPORT
        private void OnRconConnection(IPAddress ip)
        {
            var dict = new Dictionary<string, string>
                        {
                            { "ip", ip.ToString() }
                        };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("log_rcon"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnRCONConnected", dict));
                    });
                }
            }
        }


        //           ======================================================================
        //           ||                      EXTERNAL PLUGINS SUPPORT                    ||
        //           ======================================================================

        //Admin Hammer
        void OnAdminHammerEnabled(BasePlayer player)
        {
            var dict = new Dictionary<string, string>
                        {
                            { "player", player.displayName }
                        };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_adminhammer"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("PLUGIN_AdminHammer_Enabled", dict));
                    });
                }
            }
        }
        void OnAdminHammerDisabled(BasePlayer player)
        {
            var dict = new Dictionary<string, string>
                        {
                            { "player", player.displayName }
                        };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_adminhammer"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("PLUGIN_AdminHammer_Disabled", dict));
                    });
                }
            }
        }

        //Admin Radar
        void OnRadarActivated(BasePlayer player)
        {
            var dict = new Dictionary<string, string>
                        {
                            { "player", player.displayName }
                        };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_adminradar"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("PLUGIN_AdminRadar_Enabled", dict));
                    });
                }
            }
        }
        void OnRadarDeactivated(BasePlayer player)
        {
            var dict = new Dictionary<string, string>
                        {
                            { "player", player.displayName }
                        };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_adminradar"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("PLUGIN_AdminRadar_Disabled", dict));
                    });
                }
            }
        }

        //Better Chat Mute

        private void OnBetterChatMuted(IPlayer target, IPlayer player, string reason)
        {
            var dict = new Dictionary<string, string>
                        {
                            { "target", target.Name },
                            { "reason", reason },
                            { "muter", player.Name }
                        };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_betterchatmute"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("PLUGIN_BetterChatMute_Mute", dict));
                    });
                }
            }
        }

        private void OnBetterChatTimeMuted(IPlayer target, IPlayer player, TimeSpan time, string reason)
        {
            var dict = new Dictionary<string, string>
                        {
                            { "target", target.Name },
                            { "reason", reason },
                            { "muter", player.Name },
                            { "time", FormatTime((TimeSpan) time) }
                        };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_betterchatmute"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("PLUGIN_BetterChatMute_TimedMute", dict));
                    });
                }
            }
        }

        private void OnBetterChatUnmuted(IPlayer target, IPlayer player)
        {
            var dict = new Dictionary<string, string>
                        {
                            { "target", target.Name },
                            { "unmuter", player.Name }
                        };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_betterchatmute"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("PLUGIN_BetterChatMute_UnMute", dict));
                    });
                }
            }
        }

        private void OnBetterChatMuteExpired(IPlayer target)
        {
            var dict = new Dictionary<string, string>
                        {
                            { "target", target.Name }
                        };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_betterchatmute"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("PLUGIN_BetterChatMute_MuteExpire", dict));
                    });
                }
            }
        }

        private static string FormatTime(TimeSpan time)
        {
            var values = new List<string>();

            if (time.Days != 0)
                values.Add($"{time.Days} day(s)");

            if (time.Hours != 0)
                values.Add($"{time.Hours} hour(s)");

            if (time.Minutes != 0)
                values.Add($"{time.Minutes} minute(s)");

            if (time.Seconds != 0)
                values.Add($"{time.Seconds} second(s)");

            return values.ToSentence();
        }

        //Clans
        void OnClanCreate(string tag, string ownerID)
        {
            var dict = new Dictionary<string, string>
                        {
                            { "clan", tag }
                        };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_clans"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("PLUGIN_Clans_CreateClan", dict));
                    });
                }
            }
        }
        void OnClanDestroy(string tag)
        {
            var dict = new Dictionary<string, string>
                        {
                            { "clan", tag }
                        };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_clans"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("PLUGIN_Clans_DisbandClan", dict));
                    });
                }
            }
        }

        void OnClanChat(IPlayer player, string message)
        {
            var dict = new Dictionary<string, string>
                        {
                            { "playername", player.Name },
                            { "message", message }
                        };
            if (player.Name == null || message == null) return;
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_clanchat"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("PLUGIN_Clans_Chat", dict));
                    });
                }
            }
        }

        // Vanish
        void OnVanishDisappear(BasePlayer player)
        {
            var dict = new Dictionary<string, string>
                        {
                            { "player", player.displayName }
                        };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_vanish"))
                {

                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("PLUGIN_Vanish_Disappear", dict));
                    });
                }
            }
        }
        void OnVanishReappear(BasePlayer player)
        {
            var dict = new Dictionary<string, string>
                        {
                            { "player", player.displayName }
                        };
            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_vanish"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("PLUGIN_Vanish_Reappear", dict));
                    });
                }
            }
        }


        //PrivateMessages
        [HookMethod("OnPMProcessed")]
        void OnPMProcessed(IPlayer sender, IPlayer target, string message)
        {
            var dict = new Dictionary<string, string>
                        {
                            { "sender", sender.Name },
                            { "target", target.Name },
                            { "message", message }
                        };

            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_privatemessages"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("PLUGIN_PrivateMessages_PM", dict));
                    });
                }
            }
        }

        //RaidableBases
        void OnRaidableBaseStarted(Vector3 pos, int difficulty)
        {
            string rbdiff = string.Empty;
            if (difficulty == 0) rbdiff = "Easy";
            if (difficulty == 1) rbdiff = "Medium";
            if (difficulty == 2) rbdiff = "Hard";

            var dict = new Dictionary<string, string>
                        {
                            { "position", pos.ToString() },
                            { "difficulty", rbdiff }
                        };

            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_raidablebases"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("PLUGIN_RaidableBases_Started", dict));
                    });
                }
            }
        }
        void OnRaidableBaseEnded(Vector3 pos, int difficulty)
        {
            string rbdiff = string.Empty;
            if (difficulty == 0) rbdiff = "Easy";
            if (difficulty == 1) rbdiff = "Medium";
            if (difficulty == 2) rbdiff = "Hard";

            var dict = new Dictionary<string, string>
                        {
                            { "position", pos.ToString() },
                            { "difficulty", rbdiff }
                        };

            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_raidablebases"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("PLUGIN_RaidableBases_Ended", dict));
                    });
                }
            }
        }


        //SignArtist
        private void OnImagePost(BasePlayer player, string image)
        {
            var dict = new Dictionary<string, string>
                        {
                            { "player", player.displayName },
                            { "position", $"{player.transform.position.x} {player.transform.position.y} {player.transform.position.z}" }
                        };

            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_signartist"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, SignArtistEmbed(Translate("PLUGIN_SignArtist", dict), image));
                    });
                }
            }
        }

        private Embed SignArtistEmbed(string text, string image)
        {
            Embed embed = new Embed
            {
                title = text,
                color = 52326,
                image = new Embed.Image
                {
                    url = image
                }
            };

            return embed;
        }
        //DiscordAuth
        private void OnAuthenticate(string steamId, string discordId) => ProcessDiscordAuth("PLUGIN_DiscordAuth_Auth", steamId, discordId);

        private void OnDeauthenticate(string steamId, string discordId) => ProcessDiscordAuth("PLUGIN_DiscordAuth_Deauth", steamId, discordId);

        private void ProcessDiscordAuth(string key, string steamId, string discordId)
        {
            var player = covalence.Players.FindPlayerById(steamId);
            var user = FindUserByID(discordId);

            if (player == null || user == null)
                return;

            for (int i = 0; i < _channelCount; i++)
            {
                if (_settings.Channels[i].perms.Contains("plugin_discordauth"))
                {
                    GetChannel(_client, _settings.Channels[i].Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate(key, new Dictionary<string, string>
                        {
                            { "gamename", player.Name },
                            { "discordname", user.username + "#" + user.discriminator }
                        }));
                    });
                }
            }
        }
    }
}