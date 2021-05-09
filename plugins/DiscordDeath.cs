using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Rust;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Discord Death", "MJSU", "2.0.0")]
    [Description("Displays deaths to a discord channel")]
    internal class DiscordDeath : CovalencePlugin
    {
        #region Class Fields
        [PluginReference] private Plugin DeathNotes, PlaceholderAPI;

        private PluginConfig _pluginConfig;

        private const string DefaultUrl = "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks";
        private const string EmptyField = "\u200b";

        private enum DebugEnum
        {
            Message,
            None,
            Error,
            Warning,
            Info
        }
        
        public enum CombatEntityType
        {
            Helicopter = 0,
            Bradley = 1,
            Animal = 2,
            Murderer = 3,
            Scientist = 4,
            Scarecrow = 16,
            Player = 5,
            Trap = 6,
            Turret = 7,
            Barricade = 8,
            ExternalWall = 9,
            HeatSource = 10,
            Fire = 11,
            Lock = 12,
            ScientistSentry = 13,
            Other = 14,
            None = 15
        }

        private Action<IPlayer, StringBuilder, bool> _replacer;

        private DeathData _deathData;
        private DiscordMessage _message;
        #endregion

        #region Setup & Loading
        private void Init()
        {
            Unsubscribe(nameof(OnDeathNotice));
        }
        
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [LangKeys.UnknownOwner] = "Unknown Owner",
            }, this);
        }
        
        private void OnServerInitialized()
        {
            if (DeathNotes == null || !DeathNotes.IsLoaded)
            {
                PrintError("Missing plugin dependency DeathNotes: https://umod.org/plugins/death-notes");
                return;
            }
            
            if (DeathNotes.Version < new VersionNumber(6, 3, 0))
            {
                PrintError("DeathNotes plugin must be version 6.3.0 or higher");
                return;
            }

            if (!IsPlaceholderApiLoaded())
            {
                PrintError("Missing plugin dependency PlaceholderAPI: https://umod.org/plugins/placeholder-api");
                return;
            }
            
            if(PlaceholderAPI.Version < new VersionNumber(2, 2, 0))
            {
                PrintError("Placeholder API plugin must be version 2.2.0 or higher");
                return;
            }

            _message = ParseMessage(_pluginConfig.DeathMessage);

            if (!string.IsNullOrEmpty(_pluginConfig.WipeWebhook) && _pluginConfig.WipeWebhook != DefaultUrl)
            {
                Subscribe(nameof(OnDeathNotice));
            }
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
            config.DisplayOptions = config.DisplayOptions ?? new Hash<string, List<string>>
            {
                ["Player"] = new List<string>
                {
                    "*"
                },
                ["Animal"] = new List<string>
                {
                    "Player"
                }
            };

            config.DeathMessage = config.DeathMessage ?? new DiscordMessageConfig
            {
                Content = "",
                Embed = new EmbedConfig
                {
                    Color = "#ff6961",
                    Title = "{server.name}",
                    Enabled = true,
                    Url = string.Empty,
                    Image = string.Empty,
                    Thumbnail = string.Empty,
                    Description = ":crossed_swords: {discorddeath.message} :crossed_swords:",
                    Footer = new FooterConfig
                    {
                        IconUrl = string.Empty,
                        Text = string.Empty,
                        Enabled = true
                    },
                    Fields = new List<FieldConfig>
                    {
                        new FieldConfig
                        {
                            Title = "Victim Type",
                            Value = "{discorddeath.victim.type}",
                            Inline = true
                        },
                        new FieldConfig
                        {
                            Title = "Victim",
                            Value = "{discorddeath.victim.name}({discorddeath.victim.id})",
                            Inline = true
                        },      
                        new FieldConfig
                        {
                            Title = "Body Part",
                            Value = "{discorddeath.victim.bodypart}",
                            Inline = true
                        },
                        new FieldConfig
                        {
                            Title = "Attacker Type",
                            Value = "{discorddeath.killer.type}",
                            Inline = true
                        },
                        new FieldConfig
                        {
                            Title = "Attacker",
                            Value = "{discorddeath.killer.name} ({discorddeath.killer.id})",
                            Inline = true
                        }, 
                        new FieldConfig
                        {
                            Title = "Attacker Health",
                            Value = "{discorddeath.killer.health:0.0} HP",
                            Inline = true
                        },
                        new FieldConfig
                        {
                            Title = "Weapon",
                            Value = "{discorddeath.killer.weapon} ({discorddeath.killer.attachments})",
                            Inline = true
                        },  
                        new FieldConfig
                        {
                            Title = "Distance",
                            Value = "{discorddeath.killer.distance:0.0} Meters",
                            Inline = true
                        },  
                        new FieldConfig
                        {
                            Title = "Entity Owner",
                            Value = "{discorddeath.killer.owner}",
                            Inline = true
                        },  
                    }
                }
            };

            return config;
        }
        #endregion

        #region Death Hook
        private void OnDeathNotice(Dictionary<string, object> data, string message)
        {
            string victim = data["VictimEntityType"].ToString();
            List<string> attackerTypes;
            if (!_pluginConfig.DisplayOptions.TryGetValue(victim, out attackerTypes))
            {
                return;
            }

            string attacker = data["KillerEntityType"].ToString();
            if (!attackerTypes.Contains("*", StringComparer.InvariantCultureIgnoreCase) && !attackerTypes.Contains(attacker))
            {
                return;
            }

            DeathNotesData notesData = new DeathNotesData(data);
            _deathData = CreateDeathData(notesData, message);

            SendDiscordMessage(_pluginConfig.WipeWebhook, _message);
        }
        #endregion

        #region DeathNote
        public DeathData CreateDeathData(DeathNotesData data, string message)
        {
            DeathData death = new DeathData
            {
                VictimType = data.VictimEntityType.ToString(),
                VictimName = GetEntityName(data.VictimEntity, data.VictimEntityType),
                Message = message
            };

            if (data.VictimEntity is BasePlayer && !data.VictimEntity.IsNpc)
            {
                death.VictimId = ((BasePlayer) data.VictimEntity).UserIDString;
            }
            else
            {
                death.VictimId = data.VictimEntity.net.ID.ToString();
            }

            if (data.KillerEntityType != CombatEntityType.None)
            {
                death.KillerType = data.KillerEntityType.ToString();
                death.KillerName = GetEntityName(data.KillerEntity, data.KillerEntityType);
                death.BodyPart = GetBodyPart(data.HitInfo);

                if (data.KillerEntity is BasePlayer && !data.KillerEntity.IsNpc)
                {
                    death.KilledId = ((BasePlayer) data.KillerEntity).UserIDString;
                }
                else
                {
                    death.KilledId = data.KillerEntity.net.ID.ToString();
                }
                
                if (data.KillerEntity != null)
                {
                    death.Distance = data.KillerEntity.Distance(data.VictimEntity);
                    death.KillerHealth = data.KillerEntity.Health();
                    
                    if (death.KillerType.Equals("Player", StringComparison.OrdinalIgnoreCase))
                    {
                        death.Weapon = GetWeaponName(data.HitInfo);
                        death.Attachments = GetAttachments(data.HitInfo);
                    }
                    else if(death.KillerType.Equals("Turret", StringComparison.OrdinalIgnoreCase)
                            || death.KillerType.Equals("Lock", StringComparison.OrdinalIgnoreCase)
                            || death.KillerType.Equals("Trap", StringComparison.OrdinalIgnoreCase))
                    {
                        death.Owner = covalence.Players.FindPlayerById(data.KillerEntity.OwnerID.ToString())?.Name ?? Lang(LangKeys.UnknownOwner);
                    }
                }
            }

            death.EnsureNotEmpty();
            
            return death;
        }
        
        public string GetEntityName(BaseEntity entity, object type)
        {
            return DeathNotes?.Call<string>("GetCustomizedEntityName", entity, type);
        }        
        
        public string GetEntityName(HitInfo info)
        {
            return DeathNotes?.Call<string>("GetCustomizedBodypartName", info);
        }    
        
        public string GetWeaponName(HitInfo info)
        {
            return DeathNotes?.Call<string>("GetCustomizedWeaponName", info);
        }        
        
        public string GetBodyPart(HitInfo info)
        {
            return DeathNotes?.Call<string>("GetCustomizedBodypartName", info);
        }
        
        public string GetAttachments(HitInfo info)
        {
            List<string> attachments = DeathNotes?.Call<List<string>>("GetCustomizedAttachmentNames", info);
            if (attachments == null)
            {
                return string.Empty;
            }

            return string.Join(", ", attachments);
        }
        #endregion

        #region PlaceholderAPI
        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin?.Name == "PlaceholderAPI")
            {
                _replacer = null;
            }
        }
        
        private void OnPlaceholderAPIReady()
        {
            RegisterPlaceholder("discorddeath.victim.type", (player, s) => _deathData.VictimType, "Displays the victim entity type");
            RegisterPlaceholder("discorddeath.victim.name", (player, s) => _deathData.VictimName, "Displays the victim entity name");
            RegisterPlaceholder("discorddeath.victim.id", (player, s) => _deathData.VictimId, "Displays the victim steam if player else entity id");
            RegisterPlaceholder("discorddeath.victim.bodypart", (player, s) => _deathData.BodyPart, "Displays the victim body part");
            RegisterPlaceholder("discorddeath.killer.type", (player, s) => _deathData.KillerType, "Displays the killer entity type");
            RegisterPlaceholder("discorddeath.killer.id", (player, s) => _deathData.KilledId, "Displays the killer steam if player else entity id");
            RegisterPlaceholder("discorddeath.killer.name", (player, s) => _deathData.KillerName, "Displays the killer entity name");
            RegisterPlaceholder("discorddeath.killer.weapon", (player, s) => _deathData.Weapon, "Displays the killer's weapon");
            RegisterPlaceholder("discorddeath.killer.attachments", (player, s) => _deathData.Attachments, "Displays the killer's attachments");
            RegisterPlaceholder("discorddeath.killer.health", (player, s) => _deathData.KillerHealth, "Displays the killer's health");
            RegisterPlaceholder("discorddeath.killer.owner", (player, s) => _deathData.Owner, "Displays the Owners name of the entity that did the killing");
            RegisterPlaceholder("discorddeath.killer.distance", (player, s) => _deathData.Distance, "Displays the distance to the victim the attacker was");
            RegisterPlaceholder("discorddeath.message", (player, s) => Formatter.ToPlaintext(StripRustTags(_deathData.Message)), "Displays the default death message");
        }

        private void RegisterPlaceholder(string key, Func<IPlayer, string, object> action, string description = null)
        {
            if (IsPlaceholderApiLoaded())
            {
                PlaceholderAPI.Call("AddPlaceholder", this, key, action, description);
            }
        }

        private Action<IPlayer, StringBuilder, bool> GetReplacer()
        {
            if (!IsPlaceholderApiLoaded())
            {
                return _replacer;
            }
            
            return _replacer ?? (_replacer = PlaceholderAPI.Call<Action<IPlayer, StringBuilder, bool>>("GetProcessPlaceholders", 1));
        }

        private bool IsPlaceholderApiLoaded() => PlaceholderAPI != null && PlaceholderAPI.IsLoaded;
        #endregion

        #region Rust Tag Stripping
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

        private string StripRustTags(string original)
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

        #region Helpers
        private void Debug(DebugEnum level, string message)
        {
            if (level <= _pluginConfig.DebugLevel)
            {
                Puts($"{level}: {message}");
            }
        }
        
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
        private class PluginConfig
        {
            [DefaultValue(DefaultUrl)]
            [JsonProperty(PropertyName = "Webhook url")]
            public string WipeWebhook { get; set; }

            [JsonProperty("Display Options (Victim -> Attackers)")]
            public Hash<string, List<string>> DisplayOptions { get; set; }

            [JsonProperty(PropertyName = "Death Message")]
            public DiscordMessageConfig DeathMessage { get; set; }
            
            [JsonConverter(typeof(StringEnumConverter))]
            [DefaultValue(DebugEnum.None)]
            [JsonProperty(PropertyName = "Debug Level (None, Error, Warning, Info)")]
            public DebugEnum DebugLevel { get; set; }
        }

        public class DeathNotesData
        {
            public CombatEntityType VictimEntityType { get; set; }
            public BaseEntity VictimEntity { get; set; }
            
            public CombatEntityType KillerEntityType { get; set; }
            public BaseEntity KillerEntity { get; set; }
            public DamageType DamageType { get; set; }
            
            public HitInfo HitInfo { get; set; }
            
            public DeathNotesData(Dictionary<string,object> data)
            {
                if (data.ContainsKey("VictimEntityType"))
                {
                    VictimEntityType = (CombatEntityType)(int)data["VictimEntityType"];
                }
                
                if (data.ContainsKey("VictimEntity"))
                {
                    VictimEntity = data["VictimEntity"] as BaseEntity;
                }
                
                if (data.ContainsKey("KillerEntityType"))
                {
                    KillerEntityType = (CombatEntityType)(int)data["KillerEntityType"];
                }
                
                if (data.ContainsKey("KillerEntity"))
                {
                    KillerEntity = data["KillerEntity"] as BaseEntity;
                }
                
                if (data.ContainsKey("DamageType"))
                {
                    DamageType = (DamageType)data["DamageType"];
                }
                
                if (data.ContainsKey("HitInfo"))
                {
                    HitInfo = data["HitInfo"] as HitInfo;
                }
            }
        }
        
        public class DeathData
        {
            public string VictimName { get; set; }
            public string VictimId { get; set; }
            public string VictimType { get; set; }
            public string KillerName { get; set; }
            public string KilledId { get; set; }
            public string KillerType { get; set; }
            public float KillerHealth { get; set; }
            public string Weapon { get; set; }
            public string BodyPart { get; set; }
            public string Attachments { get; set; }
            public float Distance { get; set; }
            public string Owner { get; set; }
            public string Message { get; set; }

            public void EnsureNotEmpty()
            {
                if (string.IsNullOrEmpty(VictimName))
                {
                    VictimName = EmptyField;
                }
                
                if (string.IsNullOrEmpty(VictimId))
                {
                    VictimId = EmptyField;
                }
                
                if (string.IsNullOrEmpty(VictimType))
                {
                    VictimType = EmptyField;
                }

                if (string.IsNullOrEmpty(KillerName))
                {
                    KillerName = EmptyField;
                }
                
                if (string.IsNullOrEmpty(KilledId))
                {
                    KilledId = EmptyField;
                }
                
                if (string.IsNullOrEmpty(KillerType))
                {
                    KillerType = EmptyField;
                }
                
                if (string.IsNullOrEmpty(Weapon))
                {
                    Weapon = EmptyField;
                }

                if (string.IsNullOrEmpty(BodyPart))
                {
                    BodyPart = EmptyField;
                }
                
                if (string.IsNullOrEmpty(Owner))
                {
                    Owner = EmptyField;
                }
                
                if (string.IsNullOrEmpty(Message))
                {
                    Message = EmptyField;
                }
            }
        }
        
        private static class LangKeys
        {
            public const string UnknownOwner = nameof(UnknownOwner);
        }
        #endregion

        #region Discord Embed
        #region Send Embed Methods
        /// <summary>
        /// Headers when sending an embedded message
        /// </summary>
        private readonly Dictionary<string, string> _headers = new Dictionary<string, string>()
        {
            {"Content-Type", "application/json"}
        };

        /// <summary>
        /// Sends the DiscordMessage to the specified webhook url
        /// </summary>
        /// <param name="url">Webhook url</param>
        /// <param name="message">Message being sent</param>
        private void SendDiscordMessage(string url, DiscordMessage message)
        {
            StringBuilder json = message.ToJson();
            if (_pluginConfig.DebugLevel >= DebugEnum.Info)
            {
                Debug(DebugEnum.Info, $"SendDiscordMessage - ToJson \n{json}");
            }

            if (IsPlaceholderApiLoaded())
            {
                GetReplacer()?.Invoke(null, json, false);
            }
            
            if (_pluginConfig.DebugLevel >= DebugEnum.Info)
            {
                Debug(DebugEnum.Info, $"SendDiscordMessage - ParseFields \n{json}");
            }

            webrequest.Enqueue(url, json.ToString(), SendDiscordMessageCallback, this, RequestMethod.POST, _headers);
        }

        /// <summary>
        /// Callback when sending the embed if any errors occured
        /// </summary>
        /// <param name="code">HTTP response code</param>
        /// <param name="message">Response message</param>
        private void SendDiscordMessageCallback(int code, string message)
        {
            if (code != 204)
            {
                PrintError(message);
            }
        }
        #endregion

        #region Helper Methods
        private const string OwnerIcon = "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/avatars/47/47db946f27bc76d930ac82f1656f7a10707bb67d_full.jpg";

        private void AddPluginInfoFooter(Embed embed)
        {
            embed.AddFooter($"{Title} V{Version} by {Author}", OwnerIcon);
        }

        private string GetPositionField(Vector3 pos)
        {
            return $"{pos.x:0.00} {pos.y:0.00} {pos.z:0.00}";
        }
        #endregion

        #region Embed Classes
        private class DiscordMessage
        {
            /// <summary>
            /// The name of the user sending the message changing this will change the webhook bots name
            /// </summary>
            [JsonProperty("username")]
            private string Username { get; set; }

            /// <summary>
            /// The avatar url of the user sending the message changing this will change the webhook bots avatar
            /// </summary>
            [JsonProperty("avatar_url")]
            private string AvatarUrl { get; set; }

            /// <summary>
            /// String only content to be sent
            /// </summary>
            [JsonProperty("content")]
            private string Content { get; set; }

            /// <summary>
            /// Embeds to be sent
            /// </summary>
            [JsonProperty("embeds")]
            private List<Embed> Embeds { get; }

            public DiscordMessage(string username = null, string avatarUrl = null)
            {
                Username = username;
                AvatarUrl = avatarUrl;
                Embeds = new List<Embed>();
            }

            public DiscordMessage(string content, string username = null, string avatarUrl = null)
            {
                Content = content;
                Username = username;
                AvatarUrl = avatarUrl;
                Embeds = new List<Embed>();
            }

            public DiscordMessage(Embed embed, string username = null, string avatarUrl = null)
            {
                Embeds = new List<Embed> {embed};
                Username = username;
                AvatarUrl = avatarUrl;
            }

            /// <summary>
            /// Adds a new embed to the list of embed to send
            /// </summary>
            /// <param name="embed">Embed to add</param>
            /// <returns>This</returns>
            /// <exception cref="IndexOutOfRangeException">Thrown if more than 10 embeds are added in a send as that is the discord limit</exception>
            public DiscordMessage AddEmbed(Embed embed)
            {
                if (Embeds.Count >= 10)
                {
                    throw new IndexOutOfRangeException("Only 10 embed are allowed per message");
                }

                Embeds.Add(embed);
                return this;
            }

            /// <summary>
            /// Adds string content to the message
            /// </summary>
            /// <param name="content"></param>
            /// <returns></returns>
            public DiscordMessage AddContent(string content)
            {
                Content = content;
                return this;
            }

            /// <summary>
            /// Changes the username and avatar image for the bot sending the message
            /// </summary>
            /// <param name="username">username to change</param>
            /// <param name="avatarUrl">avatar img url to change</param>
            /// <returns>This</returns>
            public DiscordMessage AddSender(string username, string avatarUrl)
            {
                Username = username;
                AvatarUrl = avatarUrl;
                return this;
            }

            /// <summary>
            /// Returns message as JSON to be sent in the web request
            /// </summary>
            /// <returns></returns>
            public StringBuilder ToJson() => new StringBuilder(JsonConvert.SerializeObject(this, Formatting.None,
                new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore}));
        }

        private class Embed
        {
            /// <summary>
            /// Color of the left side bar of the embed message
            /// </summary>
            [JsonProperty("color")]
            private int Color { get; set; }

            /// <summary>
            /// Fields to be added to the embed message
            /// </summary>
            [JsonProperty("fields")]
            private List<Field> Fields { get; } = new List<Field>();

            /// <summary>
            /// Title of the embed message
            /// </summary>
            [JsonProperty("title")]
            private string Title { get; set; }

            /// <summary>
            /// Description of the embed message
            /// </summary>
            [JsonProperty("description")]
            private string Description { get; set; }

            /// <summary>
            /// Description of the embed message
            /// </summary>
            [JsonProperty("url")]
            private string Url { get; set; }

            /// <summary>
            /// Image to added to the embed message. Appears at the bottom of the message above the footer
            /// </summary>
            [JsonProperty("image")]
            private Image Image { get; set; }

            /// <summary>
            /// Thumbnail image added to the embed message. Appears in the top right corner
            /// </summary>
            [JsonProperty("thumbnail")]
            private Image Thumbnail { get; set; }

            /// <summary>
            /// Video to add to the embed message
            /// </summary>
            [JsonProperty("video")]
            private Video Video { get; set; }

            /// <summary>
            /// Author to add to the embed message. Appears above the title.
            /// </summary>
            [JsonProperty("author")]
            private AuthorInfo Author { get; set; }

            /// <summary>
            /// Footer to add to the embed message. Appears below all content.
            /// </summary>
            [JsonProperty("footer")]
            private Footer Footer { get; set; }

            /// <summary>
            /// Adds a title to the embed message
            /// </summary>
            /// <param name="title">Title to add</param>
            /// <returns>This</returns>
            public Embed AddTitle(string title)
            {
                Title = title;
                return this;
            }

            /// <summary>
            /// Adds a description to the embed message
            /// </summary>
            /// <param name="description">description to add</param>
            /// <returns>This</returns>
            public Embed AddDescription(string description)
            {
                Description = description;
                return this;
            }

            /// <summary>
            /// Adds a url to the embed message
            /// </summary>
            /// <param name="url"></param>
            /// <returns>This</returns>
            public Embed AddUrl(string url)
            {
                Url = url;
                return this;
            }

            /// <summary>
            /// Adds an author to the embed message. The author will appear above the title
            /// </summary>
            /// <param name="name">Name of the author</param>
            /// <param name="iconUrl">Icon Url to use for the author</param>
            /// <param name="url">Url to go to when the authors name is clicked on</param>
            /// <param name="proxyIconUrl">Backup icon url. Can be left null if you only have one icon url</param>
            /// <returns>This</returns>
            public Embed AddAuthor(string name, string iconUrl = null, string url = null, string proxyIconUrl = null)
            {
                Author = new AuthorInfo(name, iconUrl, url, proxyIconUrl);
                return this;
            }

            /// <summary>
            /// Adds a footer to the embed message
            /// </summary>
            /// <param name="text">Text to be added to the footer</param>
            /// <param name="iconUrl">Icon url to add in the footer. Appears to the left of the text</param>
            /// <param name="proxyIconUrl">Backup icon url. Can be left null if you only have one icon url</param>
            /// <returns>This</returns>
            public Embed AddFooter(string text, string iconUrl = null, string proxyIconUrl = null)
            {
                Footer = new Footer(text, iconUrl, proxyIconUrl);

                return this;
            }

            /// <summary>
            /// Adds an int based color to the embed. Color appears as a bar on the left side of the message
            /// </summary>
            /// <param name="color"></param>
            /// <returns></returns>
            public Embed AddColor(int color)
            {
                if (color < 0x0 || color > 0xFFFFFF)
                {
                    throw new Exception($"Color '{color}' is outside the valid color range");
                }

                Color = color;
                return this;
            }

            /// <summary>
            /// Adds a hex based color. Color appears as a bar on the left side of the message
            /// </summary>
            /// <param name="color">Color in string hex format</param>
            /// <returns>This</returns>
            /// <exception cref="Exception">Exception thrown if color is outside of range</exception>
            public Embed AddColor(string color)
            {
                int parsedColor = int.Parse(color.TrimStart('#'), NumberStyles.AllowHexSpecifier);
                if (parsedColor < 0x0 || parsedColor > 0xFFFFFF)
                {
                    throw new Exception($"Color '{color}' is outside the valid color range");
                }

                Color = parsedColor;
                return this;
            }

            /// <summary>
            /// Adds a RGB based color. Color appears as a bar on the left side of the message
            /// </summary>
            /// <param name="red">Red value between 0 - 255</param>
            /// <param name="green">Green value between 0 - 255</param>
            /// <param name="blue">Blue value between 0 - 255</param>
            /// <returns>This</returns>
            /// <exception cref="Exception">Thrown if red, green, or blue is outside of range</exception>
            public Embed AddColor(int red, int green, int blue)
            {
                if (red < 0 || red > 255 || green < 0 || green > 255 || blue < 0 || blue > 255)
                {
                    throw new Exception($"Color Red:{red} Green:{green} Blue:{blue} is outside the valid color range. Must be between 0 - 255");
                }

                Color = red * 65536 + green * 256 + blue;
                ;
                return this;
            }

            /// <summary>
            /// Adds a blank field.
            /// If inline it will add a blank column.
            /// If not inline will add a blank row
            /// </summary>
            /// <param name="inline">If the field is inline</param>
            /// <returns>This</returns>
            public Embed AddBlankField(bool inline)
            {
                Fields.Add(new Field("\u200b", "\u200b", inline));
                return this;
            }

            /// <summary>
            /// Adds a new field with the name as the title and value as the value.
            /// If inline will add a new column. If row will add in a new row.
            /// </summary>
            /// <param name="name"></param>
            /// <param name="value"></param>
            /// <param name="inline"></param>
            /// <returns></returns>
            public Embed AddField(string name, string value, bool inline)
            {
                Fields.Add(new Field(name, value, inline));
                return this;
            }

            /// <summary>
            /// Adds an image to the embed. The url should point to the url of the image.
            /// If using attachment image you can make the url: "attachment://{image name}.{image extension}
            /// </summary>
            /// <param name="url">Url for the image</param>
            /// <param name="width">width of the image</param>
            /// <param name="height">height of the image</param>
            /// <param name="proxyUrl">Backup url for the image</param>
            /// <returns></returns>
            public Embed AddImage(string url, int? width = null, int? height = null, string proxyUrl = null)
            {
                Image = new Image(url, width, height, proxyUrl);
                return this;
            }

            /// <summary>
            /// Adds a thumbnail in the top right corner of the embed
            /// If using attachment image you can make the url: "attachment://{image name}.{image extension}
            /// </summary>
            /// <param name="url">Url for the image</param>
            /// <param name="width">width of the image</param>
            /// <param name="height">height of the image</param>
            /// <param name="proxyUrl">Backup url for the image</param>
            /// <returns></returns>
            public Embed AddThumbnail(string url, int? width = null, int? height = null, string proxyUrl = null)
            {
                Thumbnail = new Image(url, width, height, proxyUrl);
                return this;
            }

            /// <summary>
            /// Adds a video to the embed
            /// </summary>
            /// <param name="url">Url for the video</param>
            /// <param name="width">Width of the video</param>
            /// <param name="height">Height of the video</param>
            /// <returns></returns>
            public Embed AddVideo(string url, int? width = null, int? height = null)
            {
                Video = new Video(url, width, height);
                return this;
            }
        }

        /// <summary>
        /// Field for and embed message
        /// </summary>
        private class Field
        {
            /// <summary>
            /// Name of the field
            /// </summary>
            [JsonProperty("name")]
            private string Name { get; }

            /// <summary>
            /// Value for the field
            /// </summary>
            [JsonProperty("value")]
            private string Value { get; }

            /// <summary>
            /// If the field should be in the same row or a new row
            /// </summary>
            [JsonProperty("inline")]
            private bool Inline { get; }

            public Field(string name, string value, bool inline)
            {
                Name = name;
                Value = value;
                Inline = inline;
            }
        }

        /// <summary>
        /// Image for an embed message
        /// </summary>
        private class Image
        {
            /// <summary>
            /// Url for the image
            /// </summary>
            [JsonProperty("url")]
            private string Url { get; }

            /// <summary>
            /// Width for the image
            /// </summary>
            [JsonProperty("width")]
            private int? Width { get; }

            /// <summary>
            /// Height for the image
            /// </summary>
            [JsonProperty("height")]
            private int? Height { get; }

            /// <summary>
            /// Proxy url for the image
            /// </summary>
            [JsonProperty("proxyURL")]
            private string ProxyUrl { get; }

            public Image(string url, int? width, int? height, string proxyUrl)
            {
                Url = url;
                Width = width;
                Height = height;
                ProxyUrl = proxyUrl;
            }
        }

        /// <summary>
        /// Video for an embed message
        /// </summary>
        private class Video
        {
            /// <summary>
            /// Url to the video
            /// </summary>
            [JsonProperty("url")]
            private string Url { get; }

            /// <summary>
            /// Width of the video
            /// </summary>
            [JsonProperty("width")]
            private int? Width { get; }

            /// <summary>
            /// Height of the video
            /// </summary>
            [JsonProperty("height")]
            private int? Height { get; }

            public Video(string url, int? width, int? height)
            {
                Url = url;
                Width = width;
                Height = height;
            }
        }

        /// <summary>
        /// Author of an embed message
        /// </summary>
        private class AuthorInfo
        {
            /// <summary>
            /// Name of the author
            /// </summary>
            [JsonProperty("name")]
            private string Name { get; }

            /// <summary>
            /// Url to go to when clicking on the authors name
            /// </summary>
            [JsonProperty("url")]
            private string Url { get; }

            /// <summary>
            /// Icon url for the author
            /// </summary>
            [JsonProperty("icon_url")]
            private string IconUrl { get; }

            /// <summary>
            /// Proxy icon url for the author
            /// </summary>
            [JsonProperty("proxy_icon_url")]
            private string ProxyIconUrl { get; }

            public AuthorInfo(string name, string iconUrl, string url, string proxyIconUrl)
            {
                Name = name;
                Url = url;
                IconUrl = iconUrl;
                ProxyIconUrl = proxyIconUrl;
            }
        }

        /// <summary>
        /// Footer for an embed message
        /// </summary>
        private class Footer
        {
            /// <summary>
            /// Text for the footer
            /// </summary>
            [JsonProperty("text")]
            private string Text { get; }

            /// <summary>
            /// Icon url for the footer
            /// </summary>
            [JsonProperty("icon_url")]
            private string IconUrl { get; }

            /// <summary>
            /// Proxy icon url for the footer
            /// </summary>
            [JsonProperty("proxy_icon_url")]
            private string ProxyIconUrl { get; }

            public Footer(string text, string iconUrl, string proxyIconUrl)
            {
                Text = text;
                IconUrl = iconUrl;
                ProxyIconUrl = proxyIconUrl;
            }
        }
        #endregion

        #region Attachment Classes
        /// <summary>
        /// Enum for attachment content type
        /// </summary>
        private enum AttachmentContentType
        {
            Png,
            Jpg
        }

        private class Attachment
        {
            /// <summary>
            /// Attachment data
            /// </summary>
            public byte[] Data { get; }

            /// <summary>
            /// File name for the attachment.
            /// Used in the url field of an image
            /// </summary>
            public string Filename { get; }

            /// <summary>
            /// Content type for the attachment
            /// https://developer.mozilla.org/en-US/docs/Web/HTTP/Basics_of_HTTP/MIME_types
            /// </summary>
            public string ContentType { get; }

            public Attachment(byte[] data, string filename, AttachmentContentType contentType)
            {
                Data = data;
                Filename = filename;

                switch (contentType)
                {
                    case AttachmentContentType.Jpg:
                        ContentType = "image/jpeg";
                        break;

                    case AttachmentContentType.Png:
                        ContentType = "image/png";
                        break;
                }
            }

            public Attachment(byte[] data, string filename, string contentType)
            {
                Data = data;
                Filename = filename;
                ContentType = contentType;
            }
        }
        #endregion

        #region Config Classes
        private class DiscordMessageConfig
        {
            public string Content { get; set; }
            public EmbedConfig Embed { get; set; }
        }

        private class EmbedConfig
        {
            [JsonProperty("Enabled")]
            public bool Enabled { get; set; }

            [JsonProperty("Title")]
            public string Title { get; set; }

            [JsonProperty("Description")]
            public string Description { get; set; }

            [JsonProperty("Url")]
            public string Url { get; set; }

            [JsonProperty("Embed Color")]
            public string Color { get; set; }

            [JsonProperty("Image Url")]
            public string Image { get; set; }

            [JsonProperty("Thumbnail Url")]
            public string Thumbnail { get; set; }

            [JsonProperty("Fields")]
            public List<FieldConfig> Fields { get; set; }

            [JsonProperty("Footer")]
            public FooterConfig Footer { get; set; }
        }

        private class FieldConfig
        {
            [JsonProperty("Title")]
            public string Title { get; set; }

            [JsonProperty("Value")]
            public string Value { get; set; }

            [JsonProperty("Inline")]
            public bool Inline { get; set; }
        }

        private class FooterConfig
        {
            [JsonProperty("Icon Url")]
            public string IconUrl { get; set; }

            [JsonProperty("Text")]
            public string Text { get; set; }

            [JsonProperty("Enabled")]
            public bool Enabled { get; set; }
        }
        #endregion

        #region Config Methods
        private DiscordMessage ParseMessage(DiscordMessageConfig config)
        {
            DiscordMessage message = new DiscordMessage();

            if (!string.IsNullOrEmpty(config.Content))
            {
                message.AddContent(config.Content);
            }

            if (config.Embed != null && config.Embed.Enabled)
            {
                Embed embed = new Embed();
                if (!string.IsNullOrEmpty(config.Embed.Title))
                {
                    embed.AddTitle(config.Embed.Title);
                }

                if (!string.IsNullOrEmpty(config.Embed.Description))
                {
                    embed.AddDescription(config.Embed.Description);
                }

                if (!string.IsNullOrEmpty(config.Embed.Url))
                {
                    embed.AddUrl(config.Embed.Url);
                }

                if (!string.IsNullOrEmpty(config.Embed.Color))
                {
                    embed.AddColor(config.Embed.Color);
                }

                if (!string.IsNullOrEmpty(config.Embed.Image))
                {
                    embed.AddImage(config.Embed.Image);
                }

                if (!string.IsNullOrEmpty(config.Embed.Thumbnail))
                {
                    embed.AddThumbnail(config.Embed.Thumbnail);
                }

                foreach (FieldConfig field in config.Embed.Fields)
                {
                    string value = field.Value;
                    if (string.IsNullOrEmpty(value))
                    {
                        PrintWarning($"Field: {field.Title} was skipped because the value was null or empty.");
                        continue;
                    }

                    embed.AddField(field.Title, value, field.Inline);
                }

                if (config.Embed.Footer != null && config.Embed.Footer.Enabled)
                {
                    if (string.IsNullOrEmpty(config.Embed.Footer.Text) &&
                        string.IsNullOrEmpty(config.Embed.Footer.IconUrl))
                    {
                        AddPluginInfoFooter(embed);
                    }
                    else
                    {
                        embed.AddFooter(config.Embed.Footer.Text, config.Embed.Footer.IconUrl);
                    }
                }

                message.AddEmbed(embed);
            }

            return message;
        }
        #endregion
        #endregion
    }
}