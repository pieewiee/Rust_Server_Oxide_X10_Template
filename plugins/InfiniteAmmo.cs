using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Infinite Ammo", "collect_vood & Mughisi", "1.3.0")]
    [Description("Allows permission based Infinite Ammo")]
    public class InfiniteAmmo : CovalencePlugin
    {
        #region Fields

        private readonly string _usePermission = "infiniteammo.use";

        #endregion

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>()
            {
                { "Disabled", "You no longer have infinite ammo!" },
                { "Enabled", "You now have infinite ammo!" },
                { "NotAllowed", "You are not allowed to use this command." },
            }, this);
        }

        #endregion

        #region Configuration

        private ConfigurationFile _configuration;

        public class ConfigurationFile
        {
            [JsonProperty(PropertyName = "Ammo Toggle Command")]
            public string AmmoToggleCommand = "toggleammo";     
            
            [JsonProperty(PropertyName = "Chat Prefix")]
            public string ChatPrefix = "Infinite Ammo";    
            
            [JsonProperty(PropertyName = "Chat Prefix Color")]
            public string ChatPrefixColor = "#008800";            
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            _configuration = new ConfigurationFile();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _configuration = Config.ReadObject<ConfigurationFile>();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_configuration);

        #endregion

        #region Data

        private StoredData _storedData;

        public class StoredData
        {
            [JsonProperty(PropertyName = "Active Infinite Ammo Player UserIds", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ulong> ActiveUsers = new List<ulong>();
        }
        
        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, _storedData);
        }

        private void Unload()
        {
            SaveData();
        }

        #endregion

        #region Hooks

        private void Init()
        {
            _storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);

            permission.RegisterPermission(_usePermission, this);

            AddCovalenceCommand(_configuration.AmmoToggleCommand, nameof(CommandToggleAmmo));
        }

        private void OnWeaponFired(BaseProjectile projectile, BasePlayer player)
        {
            if (!IsInfiniteAmmo(player.userID))
            {
                return;
            }

            var heldEntity = projectile.GetItem();
            heldEntity.condition = heldEntity.info.condition.max;

            if (projectile.primaryMagazine.contents > 0)
            {
                return;
            }

            projectile.primaryMagazine.contents = projectile.primaryMagazine.capacity;
            projectile.SendNetworkUpdateImmediate();
        }

        private void OnRocketLaunched(BasePlayer player)
        {
            if (!IsInfiniteAmmo(player.userID))
            {
                return;
            }

            var heldEntity = player.GetActiveItem();
            if (heldEntity == null)
            {
                return;
            }

            heldEntity.condition = heldEntity.info.condition.max;

            var weapon = heldEntity.GetHeldEntity() as BaseProjectile;
            if (weapon == null)
            {
                return;
            }

            if (weapon.primaryMagazine.contents > 0)
            {
                return;
            }

            weapon.primaryMagazine.contents = weapon.primaryMagazine.capacity;
            weapon.SendNetworkUpdateImmediate();
        }

        private void OnMeleeThrown(BasePlayer player, Item item)
        {
            if (!IsInfiniteAmmo(player.userID))
            {
                return;
            }

            var newMelee = ItemManager.CreateByItemID(item.info.itemid, item.amount, item.skin);
            newMelee._condition = item._condition;

            player.GiveItem(newMelee, BaseEntity.GiveItemReason.PickedUp);
        }

        private void OnServerSave()
        {
            SaveData();
        }

        #endregion

        #region Commands

        private void CommandToggleAmmo(IPlayer iPlayer, string cmd, string[] args)
        {
            var bPlayer = iPlayer.Object as BasePlayer;

            if (bPlayer == null)
            {
                return;
            }

            if (!CanUseInfiniteAmmo(bPlayer))
            {
                SendMessage(bPlayer, GetMessage("NotAllowed", bPlayer));
                return;
            }

            if (!IsInfiniteAmmo(bPlayer.userID))
            {
                _storedData.ActiveUsers.Add(bPlayer.userID);
                SendMessage(bPlayer, GetMessage("Enabled", bPlayer));
            }
            else
            {
                _storedData.ActiveUsers.Remove(bPlayer.userID);
                SendMessage(bPlayer, GetMessage("Disabled", bPlayer));
            }
        }

        #endregion

        #region Methods

        private void SendMessage(BasePlayer player, string message)
        {
            player.ChatMessage($"<color={_configuration.ChatPrefixColor}>{_configuration.ChatPrefix}</color>: {message}");
        }

        private bool IsInfiniteAmmo(ulong userId) => _storedData.ActiveUsers.Contains(userId);

        private bool CanUseInfiniteAmmo(BasePlayer player)
            => (player.IsAdmin || permission.UserHasPermission(player.UserIDString, _usePermission));

        private string GetMessage(string key, BasePlayer player, params string[] args) 
            => String.Format(lang.GetMessage(key, this, player.UserIDString), args);

        #endregion
    }
}