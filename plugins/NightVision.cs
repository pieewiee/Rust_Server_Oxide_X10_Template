using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Network;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("NightVision", "Clearshot", "2.2.0")]
    [Description("Allows players to see at night")]
    class NightVision : CovalencePlugin
    {
        private PluginConfig _config;
        private Game.Rust.Libraries.Player _rustPlayer = Interface.Oxide.GetLibrary<Game.Rust.Libraries.Player>("Player");
        private EnvSync _envSync;
        private Dictionary<ulong, NVPlayerData> _playerData = new Dictionary<ulong, NVPlayerData>();
        private DateTime _sunnyDayDate = new DateTime(2024, 1, 25);

        public bool API_envUpdates = true;

        private void SendChatMsg(BasePlayer pl, string msg, string prefix = null) =>
            _rustPlayer.Message(pl, msg, prefix != null ? prefix : lang.GetMessage("ChatPrefix", this, pl.UserIDString), Convert.ToUInt64(_config.chatIconID), Array.Empty<object>());

        private void Init()
        {
            permission.RegisterPermission("nightvision.allowed", this);
            permission.RegisterPermission("nightvision.unlimitednvg", this);
        }

        private void OnServerInitialized()
        {
            _envSync = BaseNetworkable.serverEntities.OfType<EnvSync>().FirstOrDefault();

            timer.Every(5f, () => {
                if (!_envSync.limitNetworking)
                    _envSync.limitNetworking = true;

                List<Connection> subscribers = _envSync.net.group.subscribers;
                if (subscribers != null && subscribers.Count > 0)
                {
                    for (int i = 0; i < subscribers.Count; i++)
                    {
                        Connection connection = subscribers[i];
                        global::BasePlayer basePlayer = connection.player as global::BasePlayer;

                        if (!(basePlayer == null)) {
                            NVPlayerData nvPlayerData = GetNVPlayerData(basePlayer);

                            if (!API_envUpdates && !nvPlayerData.timeLocked) continue;

                            if (Net.sv.write.Start())
                            {
                                connection.validate.entityUpdates = connection.validate.entityUpdates + 1;
                                BaseNetworkable.SaveInfo saveInfo = new global::BaseNetworkable.SaveInfo
                                {
                                    forConnection = connection,
                                    forDisk = false
                                };
                                Net.sv.write.PacketID(Message.Type.Entities);
                                Net.sv.write.UInt32(connection.validate.entityUpdates);
                                using (saveInfo.msg = Facepunch.Pool.Get<ProtoBuf.Entity>())
                                {
                                    _envSync.Save(saveInfo);
                                    if (nvPlayerData.timeLocked)
                                    {
                                        saveInfo.msg.environment.dateTime = _sunnyDayDate.AddHours(nvPlayerData.time).ToBinary();
                                        saveInfo.msg.environment.fog = nvPlayerData.fog;
                                        saveInfo.msg.environment.rain = nvPlayerData.rain;
                                        saveInfo.msg.environment.clouds = 0;
                                    }
                                    if (saveInfo.msg.baseEntity == null)
                                    {
                                        LogError(this + ": ToStream - no BaseEntity!?");
                                    }
                                    if (saveInfo.msg.baseNetworkable == null)
                                    {
                                        LogError(this + ": ToStream - no baseNetworkable!?");
                                    }
                                    saveInfo.msg.ToProto(Net.sv.write);
                                    _envSync.PostSave(saveInfo);
                                    Net.sv.write.Send(new SendInfo(connection));
                                }
                            }
                        }
                    }
                }
            });
        }

        private void OnPlayerDisconnected(BasePlayer pl, string reason)
        {
            if (pl != null && _playerData.ContainsKey(pl.userID))
                _playerData.Remove(pl.userID);
        }

        private void Unload()
        {
            if (_envSync != null)
                _envSync.limitNetworking = false;
        }

        [Command("nightvision", "nv", "unlimitednvg", "unvg")]
        private void NightVisionCommand(IPlayer player, string command, string[] args)
        {
            if (player == null) return;
            BasePlayer pl = (BasePlayer)player.Object;
            if (pl == null) return;

            if (args.Length != 0 && args[0] == "help")
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(lang.GetMessage("HelpTitle", this, pl.UserIDString));
                sb.AppendLine(lang.GetMessage("Help1", this, pl.UserIDString));

                if (permission.UserHasPermission(pl.UserIDString, "nightvision.unlimitednvg"))
                    sb.AppendLine(lang.GetMessage("Help2", this, pl.UserIDString));

                SendChatMsg(pl, sb.ToString(), "");
                return;
            }

            switch(command)
            {
                case "nightvision":
                case "nv":
                    if (!permission.UserHasPermission(pl.UserIDString, "nightvision.allowed"))
                    {
                        SendChatMsg(pl, lang.GetMessage("NoPerms", this, pl.UserIDString));
                        return;
                    }

                    NVPlayerData nvpd = GetNVPlayerData(pl);
                    nvpd.timeLocked = !nvpd.timeLocked;
                    SendChatMsg(pl, lang.GetMessage(nvpd.timeLocked ? "TimeLocked" : "TimeUnlocked", this, pl.UserIDString));
                    break;
                case "unlimitednvg":
                case "unvg":
                    if (!permission.UserHasPermission(pl.UserIDString, "nightvision.unlimitednvg"))
                    {
                        SendChatMsg(pl, lang.GetMessage("NoPerms", this, pl.UserIDString));
                        return;
                    }

                    List<Item> unvgInv = pl.inventory.containerWear.itemList.FindAll((Item x) => x.info.name == "hat.nvg.item");
                    if (unvgInv.Count > 0)
                    {
                        foreach(Item i in unvgInv)
                        {
                            if (i.condition == 1 && i.amount == 0)
                            {
                                i.SwitchOnOff(false);
                                i.Remove();
                            }
                        }

                        pl.inventory.containerWear.capacity = 7;
                        SendChatMsg(pl, lang.GetMessage("RemoveUNVG", this, pl.UserIDString));
                    }
                    else
                    {
                        var item = ItemManager.CreateByName("nightvisiongoggles", 1, 0UL);
                        if (item != null)
                        {
                            item.OnVirginSpawn();
                            item.SwitchOnOff(true);
                            item.condition = 1;
                            item.amount = 0;
                            pl.inventory.containerWear.capacity = 8;
                            item.MoveToContainer(pl.inventory.containerWear, 7);
                            SendChatMsg(pl, lang.GetMessage("EquipUNVG", this, pl.UserIDString));
                        }
                    }
                    break;
            }
        }

        private object CanWearItem(PlayerInventory inventory, Item item, int targetSlot)
        {
            if (item == null || inventory == null) return null;
            if (item.info.name == "hat.nvg.item" && item.condition == 1 && item.amount == 0) return null;

            NextTick(() =>
            {
                if (inventory != null && inventory.containerMain != null)
                {
                    foreach (Item i in inventory.containerMain.itemList.FindAll((Item x) => x.info.name == "hat.nvg.item"))
                    {
                        if (i != null && i.condition == 1 && i.amount == 0)
                        {
                            i.SwitchOnOff(false);
                            i.Remove();
                            inventory.containerWear.capacity = 7;
                        }
                    }
                }
                if (inventory != null && inventory.containerBelt != null)
                {
                    foreach (Item i in inventory.containerBelt.itemList.FindAll((Item x) => x.info.name == "hat.nvg.item"))
                    {
                        if (i != null && i.condition == 1 && i.amount == 0)
                        {
                            i.SwitchOnOff(false);
                            i.Remove();
                            inventory.containerWear.capacity = 7;
                        }
                    }
                }
                if (inventory != null && inventory.containerWear != null)
                {
                    foreach (Item i in inventory.containerWear.itemList.FindAll((Item x) => x.info.name == "hat.nvg.item"))
                    {
                        if (i != null && i.condition == 1 && i.amount == 0)
                        {
                            i.SwitchOnOff(false);
                            i.Remove();
                            inventory.containerWear.capacity = 7;
                        }
                    }
                }
            });
            return null;
        }

        private void OnItemDropped(Item item, BaseEntity entity)
        {
            if (item != null && item.info.name == "hat.nvg.item" && item.condition == 1 && item.amount == 0)
            {
                item.Remove();
            }
        }

        private NVPlayerData GetNVPlayerData(BasePlayer pl)
        {
            _playerData[pl.userID] = _playerData.ContainsKey(pl.userID) ? _playerData[pl.userID] : new NVPlayerData();
            if (_playerData[pl.userID].timeLocked && !permission.UserHasPermission(pl.UserIDString, "nightvision.allowed"))
            {
                _playerData[pl.userID].timeLocked = false;
            }
            return _playerData[pl.userID];
        }

        #region Plugin-API

        [HookMethod("LockPlayerTime")]
        void LockPlayerTime_PluginAPI(BasePlayer player, float time, float fog, float rain)
        {
            var data = GetNVPlayerData(player);
            data.timeLocked = true;
            data.time = time;
            data.fog = fog;
            data.rain = rain;
        }

        [HookMethod("UnlockPlayerTime")]
        void UnlockPlayerTime_PluginAPI(BasePlayer player)
        {
            var data = GetNVPlayerData(player);
            data.timeLocked = false;
        }

        [HookMethod("IsPlayerTimeLocked")]
        bool IsPlayerTimeLocked_PluginAPI(BasePlayer player)
        {
            var data = GetNVPlayerData(player);
            return data.timeLocked;
        }

        [HookMethod("BlockEnvUpdates")]
        void BlockEnvUpdates_PluginAPI(bool envUpdates)
        {
            API_envUpdates = !envUpdates;
        }

        #endregion

        #region Config
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["ChatPrefix"] = "<color=#00ff00>[Night Vision]</color>",
                ["NoPerms"] = "You do not have permission to use this command!",
                ["TimeLocked"] = "Time locked to day",
                ["TimeUnlocked"] = "Time unlocked",
                ["HelpTitle"] = "<size=16><color=#00ff00>Night Vision</color> Help</size>\n",
                ["Help1"] = "<color=#00ff00>/nightvision (/nv)</color> - Toggle time lock night vision",
                ["Help2"] = "<color=#00ff00>/unlimitednvg (/unvg)</color> - Equip/remove unlimited night vision goggles",
                ["EquipUNVG"] = "Equipped unlimited night vision goggles",
                ["RemoveUNVG"] = "Removed unlimited night vision goggles"
            }, this);
        }

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(GetDefaultConfig(), true);
        }

        private PluginConfig GetDefaultConfig()
        {
            return new PluginConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<PluginConfig>();
            Config.WriteObject(_config, true);
        }

        private class PluginConfig
        {
            public string chatIconID = "0";
        }
        #endregion

        private class NVPlayerData
        {
            public bool timeLocked = false;
            public float time = 12;
            public float rain = 0;
            public float fog = 0;
        }
    }
}
