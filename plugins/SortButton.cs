using Newtonsoft.Json;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;
using Oxide.Game.Rust.Cui;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("Sort Button", "MON@H", "1.0.1")]
    [Description("Adds a sort button to storage boxes, allowing you to sort items by name or category")]
    public class SortButton : CovalencePlugin
    {
        [PluginReference] private readonly Plugin Backpacks;

        #region variables

        private const string PERMISSION_USE = "sortbutton.use";
        private const string IdSortButton = "sbSortbutton";
        private const string IdOrderButton = "sbOrderbutton";
        private Hash<ulong, ItemContainer> BackpackContainers = new Hash<ulong, ItemContainer>();

        #endregion variables

        #region Initialization

        private void Init()
        {
            LoadData();
            permission.RegisterPermission(PERMISSION_USE, this);
            foreach (var command in configData.chatS.commands)
                AddCovalenceCommand(command, nameof(CmdSortButton));
        }

        private void OnServerInitialized()
        {
            UpdateConfig();
        }

        private void UpdateConfig()
        {
            if (configData.chatS.commands.Length == 0)
                configData.chatS.commands = new[] { "sb" };
            SaveConfig();
        }

        private void OnServerSave() => timer.Once(UnityEngine.Random.Range(0f, 60f), SaveData);

        #endregion Initialization

        #region Configuration

        private ConfigData configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Global settings")]
            public GlobalSettings globalS = new GlobalSettings();

            [JsonProperty(PropertyName = "Containers Settings")]
            public ContainersSettings containersS = new ContainersSettings();

            [JsonProperty(PropertyName = "Chat settings")]
            public ChatSettings chatS = new ChatSettings();

            public class GlobalSettings
            {
                [JsonProperty(PropertyName = "Use permissions")]
                public bool usePermission = true;

                [JsonProperty(PropertyName = "Allows admins to use Sort Button without permission")]
                public bool adminsAllowed = true;

                [JsonProperty(PropertyName = "Default enabled")]
                public bool defaultEnabled = true;

                [JsonProperty(PropertyName = "Default sort type (C/N)")]
                public string defaultSortType = "C";
            }

            public class ContainersSettings
            {
                public class ContainerConfig
                {
                    public string OrderButtonAnchorsMin;
                    public string OrderButtonAnchorsMax;
                    public string OrderButtonOffsetsMin;
                    public string OrderButtonOffsetsMax;
                    public string SortButtonAnchorsMin;
                    public string SortButtonAnchorsMax;
                    public string SortButtonOffsetsMin;
                    public string SortButtonOffsetsMax;
                }

                [JsonProperty(PropertyName = "Containers configuration")]
                public Dictionary<string, ContainerConfig> containersConfig = new Dictionary<string, ContainerConfig>()
                {
                    ["backpack"] = new ContainerConfig
                    {
                        OrderButtonAnchorsMin = "0.5 0.0",
                        OrderButtonAnchorsMax = "0.5 0.0",
                        OrderButtonOffsetsMin = "478 546",
                        OrderButtonOffsetsMax = "494 567",
                        SortButtonAnchorsMin = "0.5 0.0",
                        SortButtonAnchorsMax = "0.5 0.0",
                        SortButtonOffsetsMin = "494 546",
                        SortButtonOffsetsMax = "573 567"
                    },
                    ["box.wooden.large"] = new ContainerConfig
                    {
                        OrderButtonAnchorsMin = "0.5 0.0",
                        OrderButtonAnchorsMax = "0.5 0.0",
                        OrderButtonOffsetsMin = "478 422",
                        OrderButtonOffsetsMax = "494 443",
                        SortButtonAnchorsMin = "0.5 0.0",
                        SortButtonAnchorsMax = "0.5 0.0",
                        SortButtonOffsetsMin = "494 422",
                        SortButtonOffsetsMax = "573 443"
                    },
                    ["coffinstorage"] = new ContainerConfig
                    {
                        OrderButtonAnchorsMin = "0.5 0.0",
                        OrderButtonAnchorsMax = "0.5 0.0",
                        OrderButtonOffsetsMin = "478 546",
                        OrderButtonOffsetsMax = "494 567",
                        SortButtonAnchorsMin = "0.5 0.0",
                        SortButtonAnchorsMax = "0.5 0.0",
                        SortButtonOffsetsMin = "494 546",
                        SortButtonOffsetsMax = "573 567"
                    },
                    ["cupboard.tool.deployed"] = new ContainerConfig
                    {
                        OrderButtonAnchorsMin = "0.5 0.0",
                        OrderButtonAnchorsMax = "0.5 0.0",
                        OrderButtonOffsetsMin = "478 560",
                        OrderButtonOffsetsMax = "494 581",
                        SortButtonAnchorsMin = "0.5 0.0",
                        SortButtonAnchorsMax = "0.5 0.0",
                        SortButtonOffsetsMin = "494 560",
                        SortButtonOffsetsMax = "573 581"
                    },
                    ["dropbox.deployed"] = new ContainerConfig
                    {
                        OrderButtonAnchorsMin = "0.5 0.0",
                        OrderButtonAnchorsMax = "0.5 0.0",
                        OrderButtonOffsetsMin = "478 236",
                        OrderButtonOffsetsMax = "494 257",
                        SortButtonAnchorsMin = "0.5 0.0",
                        SortButtonAnchorsMax = "0.5 0.0",
                        SortButtonOffsetsMin = "494 236",
                        SortButtonOffsetsMax = "573 257"
                    },
                    ["fridge.deployed"] = new ContainerConfig
                    {
                        OrderButtonAnchorsMin = "0.5 0.0",
                        OrderButtonAnchorsMax = "0.5 0.0",
                        OrderButtonOffsetsMin = "478 422",
                        OrderButtonOffsetsMax = "494 443",
                        SortButtonAnchorsMin = "0.5 0.0",
                        SortButtonAnchorsMax = "0.5 0.0",
                        SortButtonOffsetsMin = "494 422",
                        SortButtonOffsetsMax = "573 443"
                    },
                    ["small_stash_deployed"] = new ContainerConfig
                    {
                        OrderButtonAnchorsMin = "0.5 0.0",
                        OrderButtonAnchorsMax = "0.5 0.0",
                        OrderButtonOffsetsMin = "478 174",
                        OrderButtonOffsetsMax = "494 195",
                        SortButtonAnchorsMin = "0.5 0.0",
                        SortButtonAnchorsMax = "0.5 0.0",
                        SortButtonOffsetsMin = "494 174",
                        SortButtonOffsetsMax = "573 195"
                    },
                    ["woodbox_deployed"] = new ContainerConfig
                    {
                        OrderButtonAnchorsMin = "0.5 0.0",
                        OrderButtonAnchorsMax = "0.5 0.0",
                        OrderButtonOffsetsMin = "478 236",
                        OrderButtonOffsetsMax = "494 257",
                        SortButtonAnchorsMin = "0.5 0.0",
                        SortButtonAnchorsMax = "0.5 0.0",
                        SortButtonOffsetsMin = "494 236",
                        SortButtonOffsetsMax = "573 257"
                    }
                };
            }

            public class ChatSettings
            {
                [JsonProperty(PropertyName = "Chat command")]
                public string[] commands = new[] { "sb", "sortbutton" };

                [JsonProperty(PropertyName = "Chat prefix")]
                public string prefix = "<color=#00FFFF>[Sort Button]</color>: ";

                [JsonProperty(PropertyName = "Chat steamID icon")]
                public ulong steamIDIcon = 0;
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                configData = Config.ReadObject<ConfigData>();
                if (configData == null)
                    LoadDefaultConfig();
            }
            catch
            {
                PrintError("The configuration file is corrupted");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            configData = new ConfigData();
        }

        protected override void SaveConfig() => Config.WriteObject(configData);

        #endregion Configuration

        #region DataFile

        private StoredData storedData;

        private class StoredData
        {
            public readonly Dictionary<ulong, PlayerData> playerData = new Dictionary<ulong, PlayerData>();

            public class PlayerData
            {
                public bool enabled;
                public string sortType;
            }
        }

        private StoredData.PlayerData GetPlayerData(ulong playerID)
        {
            StoredData.PlayerData playerData;
            if (!storedData.playerData.TryGetValue(playerID, out playerData))
            {
                return null;
            }

            return playerData;
        }

        private void LoadData()
        {
            try
            {
                storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            }
            catch
            {
                storedData = null;
            }
            finally
            {
                if (storedData == null)
                {
                    ClearData();
                }
            }
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);

        private void ClearData()
        {
            storedData = new StoredData();
            SaveData();
        }

        private void OnNewSave(string filename)
        {
            SaveData();
        }

        #endregion DataFile

        #region Localization

        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["SortButtonText"] = "Sort",

                ["NotAllowed"] = "You do not have permission to use this command",
                ["Enabled"] = "<color=#228B22>Enabled</color>",
                ["Disabled"] = "<color=#B22222>Disabled</color>",
                ["SortButton"] = "Sort Button is now {0}",
                ["SortType"] = "Sort Type is now {0}",
                ["C"] = "<color=#D2691E>Category</color>",
                ["N"] = "<color=#00BFFF>Name</color>",
                ["Help"] = "List Commands:\n" +
                "<color=#FFFF00>/{0}</color> - Enable/Disable Sort Button.\n" +
                "<color=#FFFF00>/{0} <sort | type></color> - change sort type.",
            }, this);
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["SortButtonText"] = "Сортировать",

                ["NotAllowed"] = "У вас нет разрешения на использование этой команды",
                ["Enabled"] = "<color=#228B22>Включена</color>",
                ["Disabled"] = "<color=#B22222>Отключена</color>",
                ["SortButton"] = "Кнопка сортировки теперь {0}",
                ["SortType"] = "Тип сортировки теперь {0}",
                ["C"] = "<color=#D2691E>Категория</color>",
                ["N"] = "<color=#00BFFF>Имя</color>",
                ["Help"] = "Список команд:\n" +
                "<color=#FFFF00>/{0}</color> - Включить/Отключить кнопку сортировки.\n" +
                "<color=#FFFF00>/{0} <sort | type></color> - изменить тип сортировки.",
            }, this, "ru");
        }

        #endregion Localization
        
        #region Commands

        private void CmdSortButton(IPlayer player, string command, string[] args)
        {
            if (!UserHasPerm(player.Object as BasePlayer, PERMISSION_USE))
            {
                Print(player, Lang("NotAllowed", player.Id));
                return;
            }

            var playerData = GetPlayerData(ulong.Parse(player.Id));
            if (playerData == null)
            {
                playerData = new StoredData.PlayerData
                {
                    enabled = configData.globalS.defaultEnabled,
                    sortType = configData.globalS.defaultSortType,
                };
                storedData.playerData.Add(ulong.Parse(player.Id), playerData);
            }

            if (args == null || args.Length == 0)
            {
                playerData.enabled = !playerData.enabled;
                Print(player, Lang("SortButton", player.Id, playerData.enabled ? Lang("Enabled", player.Id) : Lang("Disabled", player.Id)));
                return;
            }

            switch (args[0].ToLower())
            {
                case "sort":
                case "type":                    
                    playerData.sortType = (playerData.sortType == "C") ? "N" : "C";
                    Print(player, Lang("SortType", player.Id, Lang(playerData.sortType, player.Id)));
                    return;
            }
            Print(player, Lang("Help", player.Id, configData.chatS.commands[0]));
        }

        [Command("sortbutton.order")]
        private void Command_SortType(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer == null) return;
            BasePlayer player = (iplayer.Object as BasePlayer);
            if (player == null) return;

            var playerData = GetPlayerData(player.userID);
            if (playerData == null)
            {
                playerData = new StoredData.PlayerData
                {
                    enabled = configData.globalS.defaultEnabled,
                    sortType = configData.globalS.defaultSortType,
                };
                storedData.playerData.Add(player.userID, playerData);
            }
            playerData.sortType = (playerData.sortType == "C") ? "N" : "C";

            RecreateSortButton(player);
        }

        [Command("sortbutton.sort")]
        private void Command_Sort(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer == null) return;        
                BasePlayer player = (iplayer.Object as BasePlayer);
                if (player == null) return;

                var enabled = configData.globalS.defaultEnabled;
                var sortType = configData.globalS.defaultSortType;
                var playerData = GetPlayerData(player.userID);
                if (playerData != null)
                {
                    enabled = playerData.enabled;
                    sortType = playerData.sortType;
                }
                if (!enabled) return;

                StorageContainer lootSource = player?.inventory?.loot?.entitySource as StorageContainer;
                var BackpackContainer = BackpackContainers[player.userID];
                ItemContainer inventory = (BackpackContainer == null) ? lootSource?.inventory : BackpackContainer;
                if (inventory == null) return;

                // sort by name//category, category//name
                if (sortType == "N")
                    inventory.itemList = inventory.itemList.OrderBy(x => x.info.displayName.translated).ThenBy(x => x.info.category.ToString()).ToList();
                else if (sortType == "C")
                    inventory.itemList = inventory.itemList.OrderBy(x => x.info.category.ToString()).ThenBy(x => x.info.displayName.translated).ToList();

                ItemContainer container = new ItemContainer();
                container.ServerInitialize(null, inventory.capacity);
                if ((int)container.uid == 0)
                    container.GiveUID();
                container.playerOwner = player;

                // move and stack items to temporary container
                while (inventory.itemList.Count > 0)
                {
                    inventory.itemList[0].MoveToContainer(container);
                }

                // move back to source container
                while (container.itemList.Count > 0)
                {
                    container.itemList[0].MoveToContainer(inventory);
                }

                inventory.MarkDirty();
        }

        #endregion Commands

        #region Helpers

        private void CreateButtonUI(BasePlayer player, ConfigData.ContainersSettings.ContainerConfig button)
        {
            var enabled = configData.globalS.defaultEnabled;
            var sortType = configData.globalS.defaultSortType;
            var playerData = GetPlayerData(player.userID);
            if (playerData != null)
            {
                enabled = playerData.enabled;
                sortType = playerData.sortType;
            }
            if (!enabled) return;

            string OrderButtonColor = sortType == "N" ? "0.26 0.58 0.80 0.8" : "0.75 0.43 0.18 0.8";
            CuiElementContainer elements = new CuiElementContainer {
                {
                    new CuiButton {
                        RectTransform = {
                            AnchorMin = button.OrderButtonAnchorsMin,
                            AnchorMax = button.OrderButtonAnchorsMax,
                            OffsetMin = button.OrderButtonOffsetsMin,
                            OffsetMax = button.OrderButtonOffsetsMax
                        },
                        Button = {
                            Command = "sortbutton.order",
                            Color = OrderButtonColor
                        },
                        Text = {
                            Align = TextAnchor.MiddleCenter,
                            Text = sortType,
                            Color = "0.77 0.92 0.67 0.8",
                            FontSize = 12
                        }
                    },
                    "Overlay",
                    IdOrderButton
                },
                {
                    new CuiButton {
                        RectTransform = {
                            AnchorMin = button.SortButtonAnchorsMin,
                            AnchorMax = button.SortButtonAnchorsMax,
                            OffsetMin = button.SortButtonOffsetsMin,
                            OffsetMax = button.SortButtonOffsetsMax
                        },
                        Button = {
                            Command = "sortbutton.sort",
                            Color = "0.41 0.50 0.25 0.8"
                        },
                        Text = {
                            Align = TextAnchor.MiddleCenter,
                            Text = lang.GetMessage("SortButtonText", this, player.UserIDString),
                            Color = "0.77 0.92 0.67 0.8",
                            FontSize = 12
                        }
                    },
                    "Overlay",
                    IdSortButton
                }
            };

            CuiHelper.AddUi(player, elements);
        }

        private void RecreateSortButton(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, IdSortButton);
            CuiHelper.DestroyUi(player, IdOrderButton);

            if (BackpackContainers.ContainsKey(player.userID))
            {
                CreateButtonUI(player, configData.containersS.containersConfig["backpack"]);
            }
            else
            {
                StorageContainer storage = player.inventory.loot?.entitySource as StorageContainer;
                OnLootEntity(player, storage);
            }
        }

        private void OnLootEntity(BasePlayer player, StorageContainer storage)
        {
            if (!UserHasPerm(player, PERMISSION_USE))
            {
                return;
            }
            if (!BackpackContainers.ContainsKey(player.userID) && (storage.inventory == null || storage.OwnerID == 0))
            {
                return;                    
            }

            var enabled = configData.globalS.defaultEnabled;
            var playerData = GetPlayerData(player.userID);
            if (playerData != null) enabled = playerData.enabled;
            if (!enabled) return;

            BasePlayer owner = null;
            if (BackpackContainers.ContainsKey(player.userID))
            {
                owner = BasePlayer.FindByID(player.userID);
            }
            else
            {
                owner = BasePlayer.FindByID(storage.OwnerID);
            }

            if (owner == null)
                return;

            if (owner.userID != player.userID && (owner.currentTeam != player.currentTeam && owner.currentTeam != 0))
                return;

            if (configData.containersS.containersConfig.ContainsKey(storage.ShortPrefabName) || BackpackContainers.ContainsKey(player.userID))
            {
                var dropBox = storage as DropBox;
                if (dropBox != null)
                {
                    if (dropBox.PlayerBehind(player))
                    {
                        if (BackpackContainers.ContainsKey(player.userID))
                        {
                            CreateButtonUI(player, configData.containersS.containersConfig["backpack"]);                            
                        }
                        else
                        {
                            CreateButtonUI(player, configData.containersS.containersConfig[storage.ShortPrefabName]);
                        }
                    }
                }
                else
                {
                    if (BackpackContainers.ContainsKey(player.userID))
                    {
                        CreateButtonUI(player, configData.containersS.containersConfig["backpack"]);                            
                    }
                    else
                    {
                        CreateButtonUI(player, configData.containersS.containersConfig[storage.ShortPrefabName]);
                    }
                }
            }
        }

        private bool UserHasPerm(BasePlayer player, string perm)
        {
            if (player != null)
            {
                if (!configData.globalS.usePermission)
                {
                    return true;
                }
                else if (configData.globalS.usePermission && permission.UserHasPermission(player.UserIDString, perm))
                {
                    return true;
                }
                else if (configData.globalS.adminsAllowed && player.IsAdmin)
                {
                    return true;
                }
            }
            return false;
        }

        private void Print(IPlayer player, string message)
        {
            var text = string.IsNullOrEmpty(configData.chatS.prefix) ? string.Empty : $"{configData.chatS.prefix}{message}";
#if RUST
            (player.Object as BasePlayer).SendConsoleCommand("chat.add", 2, configData.chatS.steamIDIcon, text);
            return;
#endif
            player.Message(text);
        }

        #endregion Helpers

        #region Hooks

        private void OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity)
        {
            CuiHelper.DestroyUi(player, IdSortButton);
            CuiHelper.DestroyUi(player, IdOrderButton);
            if (BackpackContainers.ContainsKey(player.userID))
            {
                foreach (var container in BackpackContainers)
                {
                    if (container.Key == player.userID)
                    {
                        BackpackContainers.Remove(container.Key);
                        break;                            
                    }
                }
            }
        }

        private void OnBackpackOpened(BasePlayer player, ulong backpackOwnerID, ItemContainer backpackContainer)
        {
            try
            {
                if (UserHasPerm(player, PERMISSION_USE))
                {
                    if (backpackOwnerID > 0 && backpackContainer != null)
                    {                    
                        BackpackContainers.Add(backpackOwnerID, backpackContainer);
                        CreateButtonUI(player, configData.containersS.containersConfig["backpack"]);
                    }
                }
            }
            catch(System.Exception ex)
            {
                PrintError($"OnBackpackOpened threw exception\n:{ex}");
                throw;
            }
        }

        private void OnBackpackClosed(BasePlayer player, ulong backpackOwnerID, ItemContainer backpackContainer)
        {
            try
            {
                CuiHelper.DestroyUi(player, IdSortButton);
                CuiHelper.DestroyUi(player, IdOrderButton);
                if (BackpackContainers.ContainsKey(backpackOwnerID))
                {
                    foreach (var container in BackpackContainers)
                    {
                        if (container.Key == backpackOwnerID)
                        {
                            BackpackContainers.Remove(container.Key);
                            break;                            
                        }
                    }
                }
            }
            catch(System.Exception ex)
            {
                PrintError($"OnBackpackClosed threw exception\n:{ex}");
                throw;
            }
        }
        
        #endregion Hooks
    }
}