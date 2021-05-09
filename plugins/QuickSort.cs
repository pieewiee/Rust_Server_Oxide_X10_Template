using Newtonsoft.Json;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using System;
using UnityEngine;


namespace Oxide.Plugins
{
    [Info("Quick Sort", "MON@H", "1.5.0")]
    [Description("Adds a GUI that allows players to quickly sort items into containers")]
    public class QuickSort : CovalencePlugin
    {
        #region Initialization
        private static readonly Dictionary<ulong, string> guiInfo = new Dictionary<ulong, string>();

        private const string permAutoLootAll = "quicksort.autolootall";
        private const string permLootAll = "quicksort.lootall";
        private const string permUse = "quicksort.use";

        private void Init()
        {
            LoadData();
            permission.RegisterPermission(permAutoLootAll, this);
            permission.RegisterPermission(permLootAll, this);
            permission.RegisterPermission(permUse, this);
            foreach (var command in configData.chatS.commands)
                AddCovalenceCommand(command, nameof(CmdQuickSort));
        }

        private void OnServerInitialized()
        {
            UpdateConfig();
        }

        private void UpdateConfig()
        {
            if (configData.chatS.commands.Length == 0)
                configData.chatS.commands = new[] { "qs" };
            SaveConfig();
        }

        private void OnServerSave() => timer.Once(UnityEngine.Random.Range(0f, 60f), SaveData);

        #endregion Initialization

        #region Configuration

        private ConfigData configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Use permissions")]
            public bool usePermission = true;

            [JsonProperty(PropertyName = "Global settings")]
            public GlobalSettings globalS = new GlobalSettings();

            [JsonProperty(PropertyName = "Custom UI Settings")]
            public UiSettings customSettings = new UiSettings();

            [JsonProperty(PropertyName = "Chat settings")]
            public ChatSettings chatS = new ChatSettings();

            public class GlobalSettings
            {
                [JsonProperty(PropertyName = "Allows admins to use Quick Sort without permission")]
                public bool adminsAllowed = true;

                [JsonProperty(PropertyName = "Default enabled")]
                public bool defaultEnabled = true;

                [JsonProperty(PropertyName = "Default UI style (center, lite, right, custom)")]
                public string defaultUiStyle = "right";

                [JsonProperty(PropertyName = "Loot all delay in seconds (0 to disable)")]
                public int lootAllDelay = 0;

                [JsonProperty(PropertyName = "Enable/Disable loot all on the sleepers")]
                public bool lootSleepers = false;

                [JsonProperty(PropertyName = "Default enabled container types")]
                public GlobalSettings.containerTypesEnabled containers = new GlobalSettings.containerTypesEnabled();
                public class containerTypesEnabled
                {
                    public bool belt = false;
                    public bool main = true;
                    public bool wear = false;

                }

                [JsonProperty(PropertyName = "Excluded containers")]
                public string[] containersExcluded = new[]
                {
                    "autoturret_deployed",
                    "bandit_shopkeeper",
                    "bigwheelbettingterminal",
                    "dropbox.deployed",
                    "flameturret.deployed",
                    "guntrap.deployed",
                    "npcvendingmachine",
                    "npcvendingmachine",
                    "npcvendingmachine_attire",
                    "npcvendingmachine_building",
                    "npcvendingmachine_building",
                    "npcvendingmachine_building_hapis",
                    "npcvendingmachine_buyres_hapis",
                    "npcvendingmachine_components",
                    "npcvendingmachine_food_hapis",
                    "npcvendingmachine_hapis_hapis",
                    "npcvendingmachine_resources",
                    "npcvendingmachine_tools",
                    "npcvendingmachine_weapons",
                    "npcvendingmachine_weapons_hapis",
                    "sam_site_turret_deployed",
                    "sam_static",
                    "scientist_turret_any",
                    "scientist_turret_lr300",
                    "shopkeeper_vm_invis",
                    "shopkeeper_vm_invis",
                    "vending_mapmarker",
                    "vendingmachine.deployed",
                    "wall.frame.shopfront",
                    "wall.frame.shopfront.metal",
                    "wall.frame.shopfront.metal.static",
                };

                [JsonProperty(PropertyName = "Auto loot all enabled by default?")]
                public bool autoLootAll = false;
            }

            public class UiSettings
            {
                public string AnchorsMin = "0.5 1.0";
                public string AnchorsMax = "0.5 1.0";
                public string OffsetsMin = "192 -137";
                public string OffsetsMax = "573 0";
                public string Color = "0.5 0.5 0.5 0.33";
                public string ButtonsColor = "0.75 0.43 0.18 0.8";
                public string LootAllColor = "0.41 0.50 0.25 0.8";
                public string TextColor = "0.77 0.92 0.67 0.8";
                public int TextSize = 16;
                public int CategoriesTextSize = 14;
            }

            public class ChatSettings
            {
                [JsonProperty(PropertyName = "Chat command")]
                public string[] commands = new[] { "qs", "quicksort" };

                [JsonProperty(PropertyName = "Chat prefix")]
                public string prefix = "<color=#00FFFF>[Quick Sort]</color>: ";

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
                public bool autoLootAll;
                public string uiStyle;
                public ConfigData.GlobalSettings.containerTypesEnabled containers;
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
                ["Deposit"] = "Deposit",
                ["DepositAll"] = "All",
                ["DepositAmmo"] = "Ammo",
                ["DepositAttire"] = "Attire",
                ["DepositConstruction"] = "Construction",
                ["DepositExisting"] = "Existing",
                ["DepositFood"] = "Food",
                ["DepositItems"] = "Deployables",
                ["DepositMedical"] = "Medical",
                ["DepositResources"] = "Resources",
                ["DepositTools"] = "Tools",
                ["DepositTraps"] = "Traps",
                ["DepositWeapons"] = "Weapons",
                ["DepositComponents"] = "Components",
                ["DepositMisc"] = "Misc",
                ["LootAll"] = "Loot All",

                ["NotAllowed"] = "You do not have permission to use this command",
                ["Enabled"] = "<color=#228B22>Enabled</color>",
                ["Disabled"] = "<color=#B22222>Disabled</color>",
                ["SyntaxError"] = "Syntax error, type '<color=#FFFF00>/{0} <help | h></color>' to view help",
                ["QuickSort"] = "Quick Sort GUI is now {0}",
                ["Style"] = "Quick Sort GUI style is now {0}",
                ["AutoLootAll"] = "Automated looting is now {0}",
                ["ContainerType"] = "Quick Sort for container type {0} is now {1}",
                ["Help"] = "List Commands:\n" +
                "<color=#FFFF00>/{0}</color> - Enable/Disable GUI.\n" +
                "<color=#FFFF00>/{0} auto - Enable/Disable automated looting.\n" +
                "<color=#FFFF00>/{0} style \"center/lite/right/custom\" - change GUI style.\n" +
                "<color=#FFFF00>/{0} conatiner \"main/wear/belt\" - add/remove container type from the sort.",
            }, this);
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Deposit"] = "Положить",
                ["DepositAll"] = "Всё",
                ["DepositAmmo"] = "Патроны",
                ["DepositAttire"] = "Одежда",
                ["DepositConstruction"] = "Конструкции",
                ["DepositExisting"] = "Существующие",
                ["DepositFood"] = "Еда",
                ["DepositItems"] = "Развертываемые",
                ["DepositMedical"] = "Медикаменты",
                ["DepositResources"] = "Ресурсы",
                ["DepositTools"] = "Инструменты",
                ["DepositTraps"] = "Ловушки",
                ["DepositWeapons"] = "Оружие",
                ["DepositComponents"] = "Компоненты",
                ["DepositMisc"] = "Разное",
                ["LootAll"] = "Забрать всё",

                ["NotAllowed"] = "У вас нет разрешения на использование этой команды",
                ["Enabled"] = "<color=#228B22>Включена</color>",
                ["Disabled"] = "<color=#B22222>Отключена</color>",
                ["SyntaxError"] = "Синтаксическая ошибка, напишите '<color=#FFFF00>/{0} <help | h></color>' чтобы отобразить подсказки",
                ["QuickSort"] = "GUI быстрой сортировки теперь {0}",
                ["Style"] = "Стиль GUI быстрой сортировки теперь {0}",
                ["AutoLootAll"] = "Забирать всё автоматически теперь {0}",
                ["ContainerType"] = "Быстрая сортировка для типа контейнера {0} теперь {1}",
                ["Help"] = "Список команд:\n" +
                "<color=#FFFF00>/{0}</color> - Включить/Отключить GUI быстрой сортировки.\n" +
                "<color=#FFFF00>/{0} auto</color> - Включить/Отключить забирать всё автоматически.\n" +
                "<color=#FFFF00>/{0} style <center/lite/right/custom></color> - изменить стиль GUI быстрой сортировки.\n" +
                "<color=#FFFF00>/{0} conatiner <main/wear/belt></color> - добавить/удалить тип контейнера для сортировки.",
            }, this, "ru");
        }

        #endregion Localization

        #region Game Hooks

        private void OnLootPlayer(BasePlayer player)
        {
            if (UserHasPerm(player, permUse))
            {
                UserInterface(player);
            }
        }

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            var check = entity.GetComponent<DestroyOnGroundMissing>();
            if (check != null && check.enabled == false)
            {
                return;
            }
            else if (IsContainerExcluded(entity))
            {
                return;
            }
            else if (UserHasPerm(player, permAutoLootAll))
            {

                var playerData = GetPlayerData(player.userID);
                var autoLootAll = configData.globalS.autoLootAll;
                if (playerData != null) autoLootAll = playerData.autoLootAll;
                if (autoLootAll)
                {
                    timer.Once(configData.globalS.lootAllDelay, () =>
                    {
                        List<ItemContainer> containers = GetLootedInventory(player);

                        if (containers != null)
                        {
                            foreach (var c in containers)
                            {
                                if (c.HasFlag(ItemContainer.Flag.NoItemInput))
                                {
                                    AutoLoot(player);
                                    if (c.IsEmpty())
                                    {
                                        player.EndLooting();
                                        return;
                                    }
                                }
                            }
                        }
                    });
                }
            }
            if (UserHasPerm(player, permUse))
            {
                UserInterface(player);
            }
        }

        private void OnPlayerLootEnd(PlayerLoot inventory)
        {
            BasePlayer player = inventory.GetComponent<BasePlayer>();

            if (player != null)
            {
                DestroyUi(player);
            }
        }

        void OnPlayerTick(BasePlayer player)
        {
            if (player.IsConnected && player.IsSleeping() && guiInfo.ContainsKey(player.userID))
            {
                DestroyUi(player);
            }
        }

        void OnBackpackOpened(BasePlayer player, ulong backpackOwnerID, ItemContainer backpackContainer)
        {
            if (UserHasPerm(player, permUse))
            {
                UserInterface(player);
            }
        }

        void OnBackpackClosed(BasePlayer player, ulong backpackOwnerID, ItemContainer backpackContainer)
        {
            if (player != null)
            {
                DestroyUi(player);
            }
        }

        #endregion Game Hooks

        #region Commands

        private void CmdQuickSort(IPlayer player, string command, string[] args)
        {
            if (configData.usePermission && !permission.UserHasPermission(player.Id, permUse))
            {
                if (!configData.globalS.adminsAllowed || !player.IsAdmin)
                {
                    Print(player, Lang("NotAllowed", player.Id));
                    return;
                }
            }

            var playerData = GetPlayerData(ulong.Parse(player.Id));
            if (playerData == null)
            {
                playerData = new StoredData.PlayerData
                {
                    enabled = configData.globalS.defaultEnabled,
                    autoLootAll = configData.globalS.autoLootAll,
                    uiStyle = configData.globalS.defaultUiStyle,
                    containers = configData.globalS.containers,
                };
                storedData.playerData.Add(ulong.Parse(player.Id), playerData);
            }

            if (args == null || args.Length == 0)
            {
                playerData.enabled = !playerData.enabled;
                Print(player, Lang("QuickSort", player.Id, playerData.enabled ? Lang("Enabled", player.Id) : Lang("Disabled", player.Id)));
                return;
            }

            switch (args[0].ToLower())
            {
                case "h":
                case "help":
                    var firstCmd = configData.chatS.commands[0];
                    Print(player, Lang("Help", player.Id, firstCmd));
                    return;
                case "auto":
                    playerData.autoLootAll = !playerData.autoLootAll;
                    Print(player, Lang("AutoLootAll", player.Id, playerData.autoLootAll ? Lang("Enabled", player.Id) : Lang("Disabled", player.Id)));
                    return;
                case "style":
                    {
                        if (args.Length > 1)
                        {
                            switch (args[1].ToLower())
                            {
                                case "center":
                                case "lite":
                                case "right":
                                case "custom":
                                    {
                                        playerData.uiStyle = args[1].ToLower();
                                        Print(player, Lang("Style", player.Id, args[1].ToLower()));
                                        return;
                                    }
                            }
                        }
                        break;
                    }
                case "container":
                    {
                        if (args.Length > 1)
                        {
                            switch (args[1].ToLower())
                            {
                                case "main":
                                    {
                                        var flag = false;
                                        if (args.Length > 2 && bool.TryParse(args[2], out flag))
                                            playerData.containers.main = flag;
                                        else
                                            playerData.containers.main = !playerData.containers.main;
                                        Print(player, Lang("ContainerType", player.Id, "main", playerData.containers.main ? Lang("Enabled", player.Id) : Lang("Disabled", player.Id)));
                                        return;
                                    }
                                case "wear":
                                    {
                                        var flag = false;
                                        if (args.Length > 2 && bool.TryParse(args[2], out flag))
                                            playerData.containers.wear = flag;
                                        else
                                            playerData.containers.wear = !playerData.containers.wear;
                                        Print(player, Lang("ContainerType", player.Id, "wear", playerData.containers.wear ? Lang("Enabled", player.Id) : Lang("Disabled", player.Id)));
                                        return;
                                    }
                                case "belt":
                                    {
                                        var flag = false;
                                        if (args.Length > 2 && bool.TryParse(args[2], out flag))
                                            playerData.containers.belt = flag;
                                        else
                                            playerData.containers.belt = !playerData.containers.belt;
                                        Print(player, Lang("ContainerType", player.Id, "belt", playerData.containers.belt ? Lang("Enabled", player.Id) : Lang("Disabled", player.Id)));
                                        return;
                                    }
                            }
                        }
                        break;
                    }
            }
            Print(player, Lang("SyntaxError", player.Id, configData.chatS.commands[0]));
        }

        [Command("quicksortgui")]
        private void SortCommand(IPlayer player, string command, string[] args)
        {
            if (UserHasPerm((player.Object as BasePlayer), permUse))
            {
                try
                {
                    SortItems(player.Object as BasePlayer, args);
                }
                catch { }
            }
        }

        [Command("quicksortgui.lootall")]
        private void LootAllCommand(IPlayer player, string command, string[] args)
        {
            if (UserHasPerm((player.Object as BasePlayer), permLootAll))
            {
                timer.Once(configData.globalS.lootAllDelay, () => AutoLoot(player.Object as BasePlayer));
            }
        }

        [Command("quicksortgui.lootdelay")]
        private void LootDelayCommand(IPlayer player, string command, string[] args)
        {
            if (player.IsAdmin)
            {
                int x;
                if (int.TryParse(args[0], out x))
                {
                    configData.globalS.lootAllDelay = x;
                    SaveConfig();
                }
            }
        }

        #endregion Commands

        #region Loot Handling

        private void AutoLoot(BasePlayer player)
        {
            List<ItemContainer> containers = GetLootedInventory(player);
            ItemContainer playerMain = player.inventory.containerMain;

            if (containers != null && playerMain != null && (containers[0].playerOwner == null || configData.globalS.lootSleepers))
            {
                List<Item> itemsSelected = new List<Item>();
                foreach (var c in containers)
                {
                    itemsSelected.AddRange(CloneItemList(c.itemList));
                }
                itemsSelected.Sort((item1, item2) => item2.info.itemid.CompareTo(item1.info.itemid));
                MoveItems(itemsSelected, playerMain);
            }
        }

        private void SortItems(BasePlayer player, string[] args)
        {
            if (player == null) return;
            var type = GetPlayerData(ulong.Parse(player.UserIDString))?.containers;
            ItemContainer container = GetLootedInventory(player)[0];
            ItemContainer playerMain = player.inventory?.containerMain;
            ItemContainer playerWear = player.inventory?.containerWear;
            ItemContainer playerBelt = player.inventory?.containerBelt;

            if (container != null && playerMain != null)
            {
                List<Item> itemsSelected = new List<Item>();

                if (args.Length == 1)
                {
                    if (string.IsNullOrEmpty(args[0])) return;
                    if (args[0].Equals("existing"))
                    {
                        if (configData.globalS.containers.main && (type == null || type.main))
                            itemsSelected.AddRange(GetExistingItems(playerMain, container));
                        if (playerWear != null && configData.globalS.containers.wear && type != null && type.wear)
                            itemsSelected.AddRange(GetExistingItems(playerWear, container));
                        if (playerBelt != null && configData.globalS.containers.belt && type != null && type.belt)
                            itemsSelected.AddRange(GetExistingItems(playerBelt, container));
                    }
                    else
                    {
                        ItemCategory category = StringToItemCategory(args[0]);
                        if (configData.globalS.containers.main && (type == null || type.main))
                            itemsSelected.AddRange(GetItemsOfType(playerMain, category));
                        if (playerWear != null && configData.globalS.containers.wear && type != null && type.wear)
                            itemsSelected.AddRange(GetItemsOfType(playerWear, category));
                        if (playerBelt != null && configData.globalS.containers.belt && type != null && type.belt)
                            itemsSelected.AddRange(GetItemsOfType(playerBelt, category));
                    }
                }
                else
                {
                    if (configData.globalS.containers.main && (type == null || type.main))
                        itemsSelected.AddRange(CloneItemList(playerMain.itemList));
                    if (playerWear != null && configData.globalS.containers.wear && type != null && type.wear)
                        itemsSelected.AddRange(CloneItemList(playerWear.itemList));
                    if (playerBelt != null && configData.globalS.containers.belt && type != null && type.belt)
                        itemsSelected.AddRange(CloneItemList(playerBelt.itemList));
                }

                IEnumerable<Item> uselessItems = GetUselessItems(itemsSelected, container);

                foreach (Item item in uselessItems)
                {
                    itemsSelected.Remove(item);
                }

                itemsSelected.Sort((item1, item2) => item2.info.itemid.CompareTo(item1.info.itemid));
                MoveItems(itemsSelected, container);
            }
        }

        #endregion Loot Handling

        #region Helpers

        private IEnumerable<Item> GetUselessItems(IEnumerable<Item> items, ItemContainer container)
        {
            BaseOven furnace = container.entityOwner?.GetComponent<BaseOven>();
            List<Item> uselessItems = new List<Item>();

            if (furnace != null)
            {
                foreach (Item item in items)
                {
                    ItemModCookable cookable = item.info.GetComponent<ItemModCookable>();

                    if (cookable == null || cookable.lowTemp > furnace.cookingTemperature || cookable.highTemp < furnace.cookingTemperature)
                    {
                        uselessItems.Add(item);
                    }
                }
            }

            return uselessItems;
        }

        private List<Item> CloneItemList(IEnumerable<Item> list)
        {
            List<Item> clone = new List<Item>();

            foreach (Item item in list)
            {
                clone.Add(item);
            }

            return clone;
        }

        private List<Item> GetExistingItems(ItemContainer primary, ItemContainer secondary)
        {
            List<Item> existingItems = new List<Item>();

            if (primary != null && secondary != null)
            {
                foreach (Item t in primary.itemList)
                {
                    foreach (Item t1 in secondary.itemList)
                    {
                        if (t.info.itemid != t1.info.itemid)
                        {
                            continue;
                        }

                        existingItems.Add(t);
                        break;
                    }
                }
            }

            return existingItems;
        }

        private List<Item> GetItemsOfType(ItemContainer container, ItemCategory category)
        {
            List<Item> items = new List<Item>();

            foreach (Item item in container.itemList)
            {
                if (item.info.category == category)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        private List<ItemContainer> GetLootedInventory(BasePlayer player)
        {
            PlayerLoot playerLoot = player.inventory.loot;
            return playerLoot != null && playerLoot.IsLooting() ? playerLoot.containers : null;
        }

        private void MoveItems(IEnumerable<Item> items, ItemContainer to)
        {
            foreach (Item item in items)
            {
                item.MoveToContainer(to);
            }
        }

        private ItemCategory StringToItemCategory(string categoryName)
        {
            string[] categoryNames = Enum.GetNames(typeof(ItemCategory));

            for (int i = 0; i < categoryNames.Length; i++)
            {
                if (categoryName.ToLower().Equals(categoryNames[i].ToLower()))
                {
                    return (ItemCategory)i;
                }
            }

            return (ItemCategory)categoryNames.Length;
        }

        private bool UserHasPerm(BasePlayer player, string perm)
        {
            if (player != null)
            {
                if (!configData.usePermission)
                {
                    return true;
                }
                else if (configData.usePermission && permission.UserHasPermission(player.UserIDString, perm))
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

        private bool IsContainerExcluded(BaseEntity entity)
        {
            if (entity != null)
            {
                if ((entity is VendingMachine) || (entity is ShopFront) || (entity is BigWheelBettingTerminal))
                {
                    return true;
                }
                else if (!configData.globalS.containersExcluded.IsNullOrEmpty() && configData.globalS.containersExcluded.Contains(entity.ShortPrefabName))
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

        #region User Interface

        private void UserInterface(BasePlayer player)
        {
            var playerData = GetPlayerData(player.userID);
            var Enabled = configData.globalS.defaultEnabled;
            if (playerData != null) Enabled = playerData.enabled;
            if (!Enabled) return;

            DestroyUi(player);
            guiInfo[player.userID] = CuiHelper.GetGuid();
            player.inventory.loot.gameObject.AddComponent<UIDestroyer>();

            var UiStyle = configData.globalS.defaultUiStyle;
            if (playerData != null) UiStyle = playerData.uiStyle;
            switch (UiStyle)
            {
                case "center":
                    UiCenter(player);
                    break;
                case "lite":
                    UiLite(player);
                    break;
                case "right":
                    UiRight(player);
                    break;
                case "custom":
                    UiCustom(player);
                    break;
            }
        }

        #region UI Custom

        private void UiCustom(BasePlayer player)
        {
            CuiElementContainer elements = new CuiElementContainer();
            var cfg = configData.customSettings;

            string panel = elements.Add(new CuiPanel
            {
                Image = { Color = cfg.Color },
                RectTransform = {
                    AnchorMin = cfg.AnchorsMin,
                    AnchorMax = cfg.AnchorsMax,
                    OffsetMin = cfg.OffsetsMin,
                    OffsetMax = cfg.OffsetsMax
                }
            }, "Hud.Menu", guiInfo[player.userID]);
            //left
            elements.Add(new CuiLabel
            {
                Text = { Text = Lang("Deposit", player.UserIDString), FontSize = cfg.TextSize, Align = TextAnchor.MiddleCenter, Color = cfg.TextColor },
                RectTransform = { AnchorMin = "0.02 0.8", AnchorMax = "0.35 1" }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui existing", Color = cfg.ButtonsColor },
                RectTransform = { AnchorMin = "0.02 0.6", AnchorMax = "0.35 0.8" },
                Text = { Text = Lang("DepositExisting", player.UserIDString), FontSize = cfg.TextSize, Align = TextAnchor.MiddleCenter, Color = cfg.TextColor }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui", Color = cfg.ButtonsColor },
                RectTransform = { AnchorMin = "0.02 0.35", AnchorMax = "0.35 0.55" },
                Text = { Text = Lang("DepositAll", player.UserIDString), FontSize = cfg.TextSize, Align = TextAnchor.MiddleCenter, Color = cfg.TextColor }
            }, panel);
            if (UserHasPerm(player, permLootAll))
            {
                elements.Add(new CuiButton
                {
                    Button = { Command = "quicksortgui.lootall", Color = cfg.LootAllColor },
                    RectTransform = { AnchorMin = "0.02 0.05", AnchorMax = "0.35 0.3" },
                    Text = { Text = Lang("LootAll", player.UserIDString), FontSize = cfg.TextSize, Align = TextAnchor.MiddleCenter, Color = cfg.TextColor }
                }, panel);
            }
            //center
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui weapon", Color = cfg.ButtonsColor },
                RectTransform = { AnchorMin = "0.37 0.818", AnchorMax = "0.65 0.949" },
                Text = { Text = Lang("DepositWeapons", player.UserIDString), FontSize = cfg.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = cfg.TextColor }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui ammunition", Color = cfg.ButtonsColor },
                RectTransform = { AnchorMin = "0.37 0.664", AnchorMax = "0.65 0.796" },
                Text = { Text = Lang("DepositAmmo", player.UserIDString), FontSize = cfg.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = cfg.TextColor }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui medical", Color = cfg.ButtonsColor },
                RectTransform = { AnchorMin = "0.37 0.511", AnchorMax = "0.65 0.642" },
                Text = { Text = Lang("DepositMedical", player.UserIDString), FontSize = cfg.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = cfg.TextColor }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui attire", Color = cfg.ButtonsColor },
                RectTransform = { AnchorMin = "0.37 0.358", AnchorMax = "0.65 0.489" },
                Text = { Text = Lang("DepositAttire", player.UserIDString), FontSize = cfg.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = cfg.TextColor }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui resources", Color = cfg.ButtonsColor },
                RectTransform = { AnchorMin = "0.37 0.204", AnchorMax = "0.65 0.336" },
                Text = { Text = Lang("DepositResources", player.UserIDString), FontSize = cfg.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = cfg.TextColor }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui component", Color = cfg.ButtonsColor },
                RectTransform = { AnchorMin = "0.37 0.051", AnchorMax = "0.65 0.182" },
                Text = { Text = Lang("DepositComponents", player.UserIDString), FontSize = cfg.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = cfg.TextColor }
            }, panel);
            //right
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui construction", Color = cfg.ButtonsColor },
                RectTransform = { AnchorMin = "0.67 0.818", AnchorMax = "0.98 0.949" },
                Text = { Text = Lang("DepositConstruction", player.UserIDString), FontSize = cfg.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = cfg.TextColor }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui items", Color = cfg.ButtonsColor },
                RectTransform = { AnchorMin = "0.67 0.664", AnchorMax = "0.98 0.796" },
                Text = { Text = Lang("DepositItems", player.UserIDString), FontSize = cfg.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = cfg.TextColor }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui tool", Color = cfg.ButtonsColor },
                RectTransform = { AnchorMin = "0.67 0.511", AnchorMax = "0.98 0.642" },
                Text = { Text = Lang("DepositTools", player.UserIDString), FontSize = cfg.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = cfg.TextColor }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui food", Color = cfg.ButtonsColor },
                RectTransform = { AnchorMin = "0.67 0.358", AnchorMax = "0.98 0.489" },
                Text = { Text = Lang("DepositFood", player.UserIDString), FontSize = cfg.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = cfg.TextColor }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui traps", Color = cfg.ButtonsColor },
                RectTransform = { AnchorMin = "0.67 0.204", AnchorMax = "0.98 0.336" },
                Text = { Text = Lang("DepositTraps", player.UserIDString), FontSize = cfg.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = cfg.TextColor }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui misc", Color = cfg.ButtonsColor },
                RectTransform = { AnchorMin = "0.67 0.051", AnchorMax = "0.98 0.182" },
                Text = { Text = Lang("DepositMisc", player.UserIDString), FontSize = cfg.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = cfg.TextColor }
            }, panel);

            CuiHelper.AddUi(player, elements);
        }

        #endregion UI Custom

        #region UI Center

        private void UiCenter(BasePlayer player)
        {
            CuiElementContainer elements = new CuiElementContainer();

            string panel = elements.Add(new CuiPanel
            {
                Image = { Color = "0.5 0.5 0.5 0.33" },
                RectTransform = {
                    AnchorMin = "0.5 0.0",
                    AnchorMax = "0.5 0.0",
                    OffsetMin = "-198 472",
                    OffsetMax = "182 626"
                }
            }, "Hud.Menu", guiInfo[player.userID]);
            //left
            elements.Add(new CuiLabel
            {
                Text = { Text = Lang("Deposit"), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8"},
                RectTransform = { AnchorMin = "0.02 0.8", AnchorMax = "0.35 1" }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui existing", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.02 0.6", AnchorMax = "0.35 0.8" },
                Text = { Text = Lang("DepositExisting"), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8"}
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.02 0.35", AnchorMax = "0.35 0.55" },
                Text = { Text = Lang("DepositAll"), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8"}
            }, panel);
            if (UserHasPerm(player, permLootAll))
            {
                elements.Add(new CuiButton
                {
                    Button = { Command = "quicksortgui.lootall", Color = "0.41 0.50 0.25 0.8" },
                    RectTransform = { AnchorMin = "0.02 0.05", AnchorMax = "0.35 0.3" },
                    Text = { Text = Lang("LootAll"), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8"}
                }, panel);
            }
            //center
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui weapon", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.818", AnchorMax = "0.65 0.949" },
                Text = { Text = Lang("DepositWeapons", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8"}
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui ammunition", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.664", AnchorMax = "0.65 0.796" },
                Text = { Text = Lang("DepositAmmo", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8"}
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui medical", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.511", AnchorMax = "0.65 0.642" },
                Text = { Text = Lang("DepositMedical", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8"}
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui attire", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.358", AnchorMax = "0.65 0.489" },
                Text = { Text = Lang("DepositAttire", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8"}
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui resources", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.204", AnchorMax = "0.65 0.336" },
                Text = { Text = Lang("DepositResources", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8"}
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui component", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.051", AnchorMax = "0.65 0.182" },
                Text = { Text = Lang("DepositComponents", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8"}
            }, panel);
            //right
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui construction", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.818", AnchorMax = "0.98 0.949" },
                Text = { Text = Lang("DepositConstruction", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8"}
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui items", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.664", AnchorMax = "0.98 0.796" },
                Text = { Text = Lang("DepositItems", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8"}
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui tool", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.511", AnchorMax = "0.98 0.642" },
                Text = { Text = Lang("DepositTools", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8"}
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui food", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.358", AnchorMax = "0.98 0.489" },
                Text = { Text = Lang("DepositFood", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8"}
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui traps", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.204", AnchorMax = "0.98 0.336" },
                Text = { Text = Lang("DepositTraps", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8"}
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui misc", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.051", AnchorMax = "0.98 0.182" },
                Text = { Text = Lang("DepositMisc", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8"}
            }, panel);

            CuiHelper.AddUi(player, elements);
        }

        #endregion UI Center

        #region UI Lite

        private void UiLite(BasePlayer player)
        {
            CuiElementContainer elements = new CuiElementContainer();

            string panel = elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = {
                    AnchorMin = "0.5 0.0",
                    AnchorMax = "0.5 0.0",
                    OffsetMin = "-56 340",
                    OffsetMax = "179 359"
                }
            }, "Hud.Menu", guiInfo[player.userID]);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui existing", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.44 1" },
                Text = { Text = Lang("DepositExisting", player.UserIDString), FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.46 0", AnchorMax = "0.60 1" },
                Text = { Text = Lang("DepositAll", player.UserIDString), FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);
            if (UserHasPerm(player, permLootAll))
            {
                elements.Add(new CuiButton
                {
                    Button = { Command = "quicksortgui.lootall", Color = "0.41 0.50 0.25 0.8" },
                    RectTransform = { AnchorMin = "0.62 0", AnchorMax = "1 1" },
                    Text = { Text = Lang("LootAll", player.UserIDString), FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
                }, panel);
            }

            CuiHelper.AddUi(player, elements);
        }

        #endregion UI Lite

        #region UI Right

        private void UiRight(BasePlayer player)
        {
            CuiElementContainer elements = new CuiElementContainer();

            string panel = elements.Add(new CuiPanel
            {
                Image = { Color = "0.5 0.5 0.5 0.33" },
                RectTransform = {                    
                    AnchorMin = "0.5 1.0",
                    AnchorMax = "0.5 1.0",
                    OffsetMin = "192 -137",
                    OffsetMax = "573 0"
                }
            }, "Hud.Menu", guiInfo[player.userID]);
            //left
            elements.Add(new CuiLabel
            {
                Text = { Text = Lang("Deposit", player.UserIDString), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" },
                RectTransform = { AnchorMin = "0.02 0.8", AnchorMax = "0.35 1" }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui existing", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.02 0.6", AnchorMax = "0.35 0.8" },
                Text = { Text = Lang("DepositExisting", player.UserIDString), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.02 0.35", AnchorMax = "0.35 0.55" },
                Text = { Text = Lang("DepositAll", player.UserIDString), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);
            if (UserHasPerm(player, permLootAll))
            {
                elements.Add(new CuiButton
                {
                    Button = { Command = "quicksortgui.lootall", Color = "0.41 0.50 0.25 0.8" },
                    RectTransform = { AnchorMin = "0.02 0.05", AnchorMax = "0.35 0.3" },
                    Text = { Text = Lang("LootAll", player.UserIDString), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
                }, panel);
            }
            //center
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui weapon", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.818", AnchorMax = "0.65 0.949" },
                Text = { Text = Lang("DepositWeapons", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui ammunition", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.664", AnchorMax = "0.65 0.796" },
                Text = { Text = Lang("DepositAmmo", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui medical", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.511", AnchorMax = "0.65 0.642" },
                Text = { Text = Lang("DepositMedical", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui attire", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.358", AnchorMax = "0.65 0.489" },
                Text = { Text = Lang("DepositAttire", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui resources", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.204", AnchorMax = "0.65 0.336" },
                Text = { Text = Lang("DepositResources", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui component", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.051", AnchorMax = "0.65 0.182" },
                Text = { Text = Lang("DepositComponents", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);
            //right
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui construction", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.818", AnchorMax = "0.98 0.949" },
                Text = { Text = Lang("DepositConstruction", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui items", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.664", AnchorMax = "0.98 0.796" },
                Text = { Text = Lang("DepositItems", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui tool", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.511", AnchorMax = "0.98 0.642" },
                Text = { Text = Lang("DepositTools", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui food", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.358", AnchorMax = "0.98 0.489" },
                Text = { Text = Lang("DepositFood", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui traps", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.204", AnchorMax = "0.98 0.336" },
                Text = { Text = Lang("DepositTraps", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui misc", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.051", AnchorMax = "0.98 0.182" },
                Text = { Text = Lang("DepositMisc", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            CuiHelper.AddUi(player, elements);
        }

        #endregion UI Right

        #region Cleanup

        private static void DestroyUi(BasePlayer player)
        {
            string gui;
            if (guiInfo.TryGetValue(player.userID, out gui))
            {
                CuiHelper.DestroyUi(player, gui);
                guiInfo.Remove(player.userID);
            }
        }

        private class UIDestroyer : MonoBehaviour
        {
            private void PlayerStoppedLooting(BasePlayer player)
            {
                DestroyUi(player);
                Destroy(this);
            }
        }

        private void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                DestroyUi(player);
            }
            SaveData();
        }

        #endregion Cleanup

        #endregion User Interface
    }
}
