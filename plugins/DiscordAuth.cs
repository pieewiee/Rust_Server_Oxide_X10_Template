using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Ext.Discord;
using Oxide.Ext.Discord.Attributes;
using Oxide.Ext.Discord.DiscordObjects;

namespace Oxide.Plugins
{
    [Info("Discord Auth", "Tricky", "1.1.3")]
    [Description("Allows players to connect their discord account with steam")]

    public class DiscordAuth : CovalencePlugin
    {
        #region Defined
        [DiscordClient]
        private DiscordClient Client;
        #endregion

        #region Config
        Configuration config;

        class Configuration
        {
            [JsonProperty(PropertyName = "Settings")]
            public Settings Info = new Settings();

            [JsonProperty(PropertyName = "Authentication Code")]
            public AuthCode Code = new AuthCode();

            public class Settings
            {
                [JsonProperty(PropertyName = "Bot Token")]
                public string BotToken = string.Empty;

                [JsonProperty(PropertyName = "Oxide Group")]
                public string Group = "authenticated";

                [JsonProperty(PropertyName = "Enable Logging")]
                public bool Log = false;

                [JsonProperty(PropertyName = "Auth Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public string[] AuthCommands = new string[] { "auth", "authenticate" };

                [JsonProperty(PropertyName = "Deauth Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public string[] DeauthCommands = new string[] { "deauth", "deauthenticate" };

                [JsonProperty(PropertyName = "Discord Roles to Assign", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public List<string> Roles = new List<string>()
                {
                    "Authenticated"
                };

                [JsonProperty(PropertyName = "Revoke Oxide Group on Discord Leave")]
                public bool RemovefromGroup = true;

                [JsonProperty(PropertyName = "Deauthenticate on Discord Leave")]
                public bool Deauthenticate = false;

                [JsonProperty(PropertyName = "Chat Prefix")]
                public string ChatPrefix = "[#1874CD](Auth)[/#]:";
            }

            public class AuthCode
            {
                [JsonProperty(PropertyName = "Code Lifetime (minutes)")]
                public int CodeLifetime = 60;

                [JsonProperty(PropertyName = "Code Length")]
                public int CodeLength = 5;

                [JsonProperty(PropertyName = "Lowercase")]
                public bool Lowercase = false;
            }
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

        #region Classes and Stored Data
        private enum MemberRole { Add, Remove }
        private const string authPerm = "discordauth.auth";
        private const string deauthPerm = "discordauth.deauth";
        private Data data;

        private class Data
        {
            public Dictionary<string, string> Players = new Dictionary<string, string>();
        }

        private Dictionary<string, string> Codes = new Dictionary<string, string>();
        #endregion

        #region Lang
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Code Generation"] = "Here is your code: [#1874CD]{0}[/#]\nJoin our [#EE3B3B]Discord[/#] and PM the code to the Discord Bot",
                ["Code Expired"] = "Your code has [#EE3B3B]Expired![/#]",
                ["Authenticated"] = "Thank you for authenticating your account",
                ["Game-Deauthenticated"] = "Successfully deauthenticated your account",
                ["Discord-Deauthenticated"] = "You have been deauthenticated from {0}",
                ["Already Authenticated"] = "You have already [#1874CD]authenticated[/#] your account, no need to do it again",
                ["Not Authenticated"] = "You are not authenticated",
                ["Group Revoked"] = "Your '{0}' Group has been revoked! Join the server back to achieve it",
                ["Group Granted"] = "Granted '{0}' Group and the discord roles for joining {1} back",
                ["Unable to find code"] = "Sorry, we couldn't find your code, please try to authenticate again, If you haven't generated a code, please type /auth in-game",
                ["No Permission"] = "You dont have permission to use this command"
            }, this);
        }
        #endregion

        #region Commands
        private void AuthCommand(IPlayer player, string command, string[] args)
        {
            // No Permission
            if (!player.HasPermission(authPerm))
            {
                Message(player, "No Permission");
                return;
            }

            // Already authenticated-check
            if (data.Players.ContainsKey(player.Id))
            {
                Message(player, "Already Authenticated");
                return;
            }

            // Sends the code if already exist to prevent duplication
            if (Codes.ContainsKey(player.Id))
            {
                Message(player, "Code Generation", Codes[player.Id]);
                return;
            }

            // Adds a random code and send it to the player if doesn't already exist
            var code = GenerateCode(config.Code.CodeLength, config.Code.Lowercase);
            Codes.Add(player.Id, code);
            Message(player, "Code Generation", code);

            // Code Expiration Function
            timer.In(config.Code.CodeLifetime * 60, () =>
            {
                if (Codes.ContainsKey(player.Id))
                {
                    Codes.Remove(player.Id);
                    if (player != null) Message(player, "Code Expired");
                }
            });
        }

        private void DeauthCommand(IPlayer player, string command, string[] args)
        {
            // No Permission
            if (!player.HasPermission(deauthPerm))
            {
                Message(player, "No Permission");
                return;
            }

            if (!data.Players.ContainsKey(player.Id))
            {
                Message(player, "Not Authenticated");
                return;
            }

            Oxide.Ext.Discord.DiscordObjects.User.GetUser(Client, data.Players[player.Id], user => HandleRoles(user, MemberRole.Remove));
            Deauthenticate(player.Id, data.Players[player.Id]);
            Message(player, "Game-Deauthenticated");
        }
        #endregion

        #region Oxide Hooks
        private void Init()
        {
            data = Interface.Oxide.DataFileSystem.ReadObject<Data>(Name);

            CreateClient();
            AddCovalenceCommand(config.Info.AuthCommands, nameof(AuthCommand));
            AddCovalenceCommand(config.Info.DeauthCommands, nameof(DeauthCommand));
            permission.CreateGroup(config.Info.Group, config.Info.Group, 0);
            permission.RegisterPermission(authPerm, this);
            permission.RegisterPermission(deauthPerm, this);

            if (!config.Info.Deauthenticate && !config.Info.RemovefromGroup)
                Unsubscribe(nameof(Discord_MemberRemoved));

            if (!config.Info.RemovefromGroup)
                Unsubscribe(nameof(Discord_MemberAdded));

            if (!config.Info.Log)
                Unsubscribe(nameof(DiscordSocket_Initalized));
        }

        private void Unload()
        {
            CloseClient();
            SaveData();
        }

        private void OnServerSave() => SaveData();
        #endregion

        #region Discord Hooks
        // Called when a message is created on the Discord server
        private void Discord_MessageCreate(Message message)
        {
            // Bot-check
            if (message.author.bot == true)
                return;

            Channel.GetChannel(Client, message.channel_id, dm =>
            {
                // DM-check
                if (dm.type != ChannelType.DM)
                    return;

                // Length-check
                if (message.content.Length != config.Code.CodeLength)
                    return;

                // No code found
                if (!Codes.ContainsValue(message.content))
                {
                    dm.CreateMessage(Client, GetEmbed(Formatter.ToPlaintext(Lang("Unable to find code")), 16098851));
                    return;
                }

                // Already authenticated-check
                if (data.Players.ContainsValue(message.author.id))
                {
                    dm.CreateMessage(Client, GetEmbed(Formatter.ToPlaintext(Lang("Already Authenticated")), 4886754));
                    return;
                }

                Codes.Keys.ToList().ForEach(playerId =>
                {
                    if (Codes[playerId] != message.content)
                        return;

                    Authenticate(playerId, message.author.id);
                });

                HandleRoles(message.author, MemberRole.Add);
                dm.CreateMessage(Client, GetEmbed(Formatter.ToPlaintext(Lang("Authenticated")), 11523722));
            });
        }

        // Called when a member leaves the Discord server
        private void Discord_MemberRemoved(GuildMember member)
        {
            var steamId = API_GetSteam(member.user.id);

            // No user found
            if (steamId == null)
                return;

            if (config.Info.Deauthenticate)
            {
                Deauthenticate(steamId, member.user.id);
                member.user.CreateDM(Client, dm => dm.CreateMessage(Client, GetEmbed(Formatter.ToPlaintext(Lang("Discord-Deauthenticated", steamId, Client.DiscordServer.name)), 9905970)));
                return;
            }

            if (config.Info.RemovefromGroup)
            {
                permission.RemoveUserGroup(steamId, config.Info.Group);
                member.user.CreateDM(Client, dm => dm.CreateMessage(Client, GetEmbed(Formatter.ToPlaintext(Lang("Group Revoked", steamId, config.Info.Group)), 16098851)));
            }
        }

        // Called when a user joins the discord server
        private void Discord_MemberAdded(GuildMember member)
        {
            var steamId = API_GetSteam(member.user.id);

            // No user found
            if (steamId == null)
                return;

            HandleRoles(member.user, MemberRole.Add);
            permission.AddUserGroup(steamId, config.Info.Group);
            member.user.CreateDM(Client, dm => dm.CreateMessage(Client, GetEmbed(Formatter.ToPlaintext(Lang("Group Granted", steamId, config.Info.Group, Client.DiscordServer.name)), 4886754)));
        }

        // Called when the client is created, and the plugin can use it
        private void DiscordSocket_Initalized(DiscordClient client) => Puts("Discord Initalized!");
        #endregion

        #region Core
        private void Authenticate(string steamId, string discordId)
        {
            if (Interface.Oxide.CallHook("OnAuthenticate", steamId, discordId) != null)
                return;

            data.Players.Add(steamId, discordId);
            permission.AddUserGroup(steamId, config.Info.Group);
        }

        private void Deauthenticate(string steamId, string discordId)
        {
            if (Interface.Oxide.CallHook("OnDeauthenticate", steamId, discordId) != null)
                return;

            data.Players.Remove(steamId);
            permission.RemoveUserGroup(steamId, config.Info.Group);
        }

        private void HandleRoles(Oxide.Ext.Discord.DiscordObjects.User user, MemberRole memberRole)
        {
            config.Info.Roles.ForEach(roleName =>
            {
                var role = GetRoleByName(roleName);
                if (role == null)
                {
                    PrintError($"Unable to find '{roleName}'");
                    return;
                }

                switch (memberRole)
                {
                    case MemberRole.Add:
                        Client.DiscordServer.AddGuildMemberRole(Client, user, role);
                        break;
                    case MemberRole.Remove:
                        Client.DiscordServer.RemoveGuildMemberRole(Client, user, role);
                        break;
                }
            });
        }
        #endregion

        #region API
        //private bool API_Authenticate(string steamId, string discordId, bool addtoGroup = true, bool callHook = true)
        //{
        //    if (!data.Players.ContainsKey(steamId) || !data.Players.ContainsValue(discordId))
        //    {
        //        if(addtoGroup)
        //            permission.AddUserGroup(steamId, config.Info.Group);

        //        if(callHook)
        //            Interface.Oxide.CallHook("OnAuthenticate", steamId, discordId);

        //        data.Players.Add(steamId, discordId);
        //        return true;
        //    }

        //    return false;
        //}

        //private bool API_Deauthenticate(string Id, bool removefromGroup = true, bool callHook = true)
        //{
        //    // If Id is steamId
        //    if (data.Players.ContainsKey(Id))
        //    {
        //        if (removefromGroup)
        //            permission.RemoveUserGroup(Id, config.Info.Group);

        //        if (callHook)
        //            Interface.Oxide.CallHook("OnDeauthenticate", Id, data.Players[Id]);

        //        data.Players.Remove(Id);
        //        return true;
        //    }

        //    // If Id is discordId
        //    if (data.Players.ContainsValue(Id))
        //    {
        //        foreach (var steamid in data.Players.Keys)
        //        {
        //            if (data.Players[steamid] == Id)
        //            {
        //                if (removefromGroup)
        //                    permission.RemoveUserGroup(steamid, config.Info.Group);

        //                if (callHook)
        //                    Interface.Oxide.CallHook("OnDeauthenticate", steamid, Id);

        //                data.Players.Remove(steamid);
        //                return true;
        //            }
        //        }
        //    }

        //    return false;
        //}

        private bool API_IsAuthenticated(string Id)
        {
            if (data.Players.ContainsKey(Id))
                return true;

            if (data.Players.ContainsValue(Id))
                return true;

            return false;
        }

        private string API_GetSteam(string Id)
        {
            if (data.Players.ContainsValue(Id))
            {
                foreach (var steamid in data.Players.Keys)
                {
                    if (data.Players[steamid] == Id)
                        return steamid;
                }
            }

            return null;
        }

        private string API_GetDiscord(string Id)
        {
            if (data.Players.ContainsKey(Id))
            {
                return data.Players[Id];
            }

            return null;
        }

        private int API_GetAuthCount() => data.Players.Count;

        private List<string> API_GetSteamList() => data.Players.Keys.ToList();

        private List<string> API_GetDiscordList() => data.Players.Values.ToList();
        #endregion

        #region Helpers
        private void CreateClient()
        {
            if (config.Info.BotToken != string.Empty)
                Discord.CreateClient(this, config.Info.BotToken);
        }

        private void CloseClient() => Discord.CloseClient(Client);

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, data);

        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        private void Message(IPlayer player, string key, params object[] args) => player.Reply($"{config.Info.ChatPrefix} {Lang(key, player.Id, args)}");

        private string GenerateCode(int size, bool lowerCase)
        {
            var builder = new StringBuilder();
            var random = new System.Random();
            char ch;

            for (int i = 0; i < size; i++)
            {
                ch = Convert.ToChar(Convert.ToInt32(Math.Floor(26 * random.NextDouble() + 65)));
                builder.Append(ch);
            }

            if (lowerCase)
                return builder.ToString().ToLower();

            return builder.ToString();
        }

        private Embed GetEmbed(string text, int color)
        {
            var embed = new Embed
            {
                description = text,
                color = color
            };

            return embed;
        }

        private Role GetRoleByName(string roleName)
        {
            foreach (var role in Client.DiscordServer.roles)
            {
                if (role.name == roleName)
                    return role;
            }

            return null;
        }
        #endregion
    }
}
