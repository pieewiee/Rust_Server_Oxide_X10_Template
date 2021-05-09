using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins {
    [Info("Active Sort", "Egor Blagov", "1.0.4")]
    [Description("Sorts furnace and refinery on click")]
    class ActiveSort : RustPlugin {
        private const string permUse = "activesort.use";

        #region TRANSLATION TABLES
        Dictionary<string, string> furnaceItems = new Dictionary<string, string> {
            ["sulfur.ore"] = "sulfur",
            ["metal.ore"] = "metal.fragments",
            ["hq.metal.ore"] = "metal.refined"
        };

        Dictionary<string, string> refineryItems = new Dictionary<string, string> {
            ["crude.oil"] = "lowgradefuel"
        };
        #endregion

        enum FurnaceType {
            Furnace,
            Refinery
        }
        #region CONFIG
        class PluginConfig {
            public bool ShowUI = true;
            public Vector2 ButtonPositionOffset = new Vector2(0, 0);
            public Vector2 ButtonSize = new Vector2(115, 30);
            public string ButtonColorHex = "#6F8344";
            public string ButtonCaptionColorHex = "#A5BA7A";
            public int ButtonCaptionFontSize = 16;
            public bool ButtonCaptionIsBold = false;
        }
        private PluginConfig config;
        #endregion
        #region L10N
        private string l10n(string key, string UserIDString) {
            return lang.GetMessage(key, this, UserIDString);
        }
        protected override void LoadDefaultMessages() {
            lang.RegisterMessages(new Dictionary<string, string> {
                ["BUTTON_CAPTION"] = "Sort"
            }, this);
            lang.RegisterMessages(new Dictionary<string, string> {
                ["BUTTON_CAPTION"] = "Сортировать"
            }, this, "ru");
        }
        #endregion
        #region HOOKS
        private void Init() {
            permission.RegisterPermission(permUse, this);
            config = Config.ReadObject<PluginConfig>();
            Config.WriteObject(config);
            if (!config.ShowUI) {
                Unsubscribe(nameof(OnPlayerLootEnd));
                Unsubscribe(nameof(OnLootEntity));
            }
        }

        protected override void LoadDefaultConfig() {
            Config.WriteObject(new PluginConfig(), true);
        }

        private void Unload() {
            foreach (var comp in UnityEngine.Object.FindObjectsOfType<ActiveSortUI>()) {
                UnityEngine.Object.Destroy(comp);
            }
        }

        private void OnLootEntity(BasePlayer player, BaseEntity entity) {
            if (CanSort(player)) {
                if (player.GetComponent<ActiveSortUI>() == null) {
                    var ui = player.gameObject.AddComponent<ActiveSortUI>();
                    ui.Init(this);
                }
            }
        }

        private void OnPlayerLootEnd(PlayerLoot inventory) {
            ActiveSortUI ui = inventory?.GetComponent<ActiveSortUI>();

            if (ui != null) {
                UnityEngine.Object.Destroy(ui);
            }
        }
        #endregion
        #region API
        private void SortLoot(BasePlayer player) {
            if (!CanSort(player)) {
                return;
            }

            if (player.inventory.loot.containers == null || player.inventory.loot.containers.Count == 0) {
                return;
            }

            var container = player.inventory.loot.containers[0];
            var furnace = player.inventory.loot.entitySource.GetComponent<BaseOven>();
            var type = FurnaceType.Furnace;
            if (furnace.name.Contains("refinery")) {
                type = FurnaceType.Refinery;
            }
            var allowedItems = type == FurnaceType.Furnace ? furnaceItems : refineryItems;

            foreach (var it in container.itemList.ToList()) {
                if (it.info.shortname != "wood" && !allowedItems.ContainsKey(it.info.shortname)) {
                    ReturnToPlayer(player, it);
                }
            }

            Dictionary<string, Item> items = CloneAndPackItems(container);
            ClearContainer(container);

            int spaceLeft = container.capacity;
            PutToContainer(items, "wood", container, player, ref spaceLeft, true);
            PutToContainer(items, "charcoal", container, player, ref spaceLeft, true);
            FilterWhitelist(items, player, container, type);
            FilterOnlyNotProcessed(items, player, container, type);

            while (true) {
                int toSortKinds = items.Keys.Where(shortname => allowedItems.ContainsKey(shortname)).Count();
                if (toSortKinds == 0) {
                    if (items.Count > 0) {
                        items.Keys.ToList().ForEach(shortname => {
                            ReturnToPlayer(player, items[shortname]);
                        });
                    }
                    break;
                }

                if (toSortKinds * 2 > spaceLeft) {
                    string toCancel = items.Keys.ToList()[0];
                    ReturnToPlayer(player, items[toCancel]);
                    if (items.ContainsKey(allowedItems[toCancel])) {
                        ReturnToPlayer(player, items[allowedItems[toCancel]]);
                    }

                    items.Remove(toCancel);
                    items.Remove(allowedItems[toCancel]);
                    continue;
                }

                int cellForEach = spaceLeft / toSortKinds;
                int cellAdditional = spaceLeft % toSortKinds;

                Dictionary<string, int> cellCountByName = new Dictionary<string, int>();
                foreach (var shortname in items.Keys) {
                    if (allowedItems.ContainsKey(shortname)) {
                        cellCountByName[shortname] = cellForEach;
                        if (cellAdditional > 0) {
                            cellCountByName[shortname]++;
                            cellAdditional--;
                        }
                    }
                }

                foreach (var shortname in cellCountByName.Keys) {
                    int cellAmount = items[shortname].amount / (cellCountByName[shortname] - 1);
                    if (cellAmount > 0) {
                        for (int i = 0; i < cellCountByName[shortname] - 2; i++) {
                            Item entry = TakeStack(items[shortname], cellAmount);
                            entry.MoveToContainer(container, -1, false);
                        }
                    }
                    Item lastPart = TakeStack(items[shortname]);
                    if (lastPart == null) {
                        lastPart = items[shortname];
                    } else {
                        ReturnToPlayer(player, items[shortname]);
                    }

                    lastPart.MoveToContainer(container, -1, false);
                    if (items.ContainsKey(allowedItems[shortname])) {
                        Item processedToContainer = TakeStack(items[allowedItems[shortname]]);
                        if (processedToContainer == null) {
                            processedToContainer = items[allowedItems[shortname]];
                        } else {
                            ReturnToPlayer(player, items[allowedItems[shortname]]);
                        }

                        processedToContainer.MoveToContainer(container, -1, true);
                    }

                    items.Remove(shortname);
                    items.Remove(allowedItems[shortname]);
                }
            }

            float longestCookingTime = 0.0f;

            foreach (var it in container.itemList) {
                var cookable = it.info.GetComponent<ItemModCookable>();
                if (cookable != null) {
                    float cookingTime = cookable.cookTime * it.amount;
                    if (cookingTime > longestCookingTime) {
                        longestCookingTime = cookingTime;
                    }
                }
            }

            float fuelAmount = furnace.fuelType.GetComponent<ItemModBurnable>().fuelAmount;
            int neededFuel = Mathf.CeilToInt(longestCookingTime * (furnace.cookingTemperature / 200.0f) / fuelAmount);

            foreach (var it in container.itemList) {
                if (it.info.shortname == "wood") {
                    if (neededFuel == 0) {
                        ReturnToPlayer(player, it);
                    } else if (it.amount > neededFuel) {
                        var unneded = it.SplitItem(it.amount - neededFuel);
                        ReturnToPlayer(player, unneded);
                    }
                    break;
                }
            }
        }

        private bool CanSort(BasePlayer player) {
            if (player == null) {
                return false;
            }

            if (!permission.UserHasPermission(player.UserIDString, permUse)) {
                return false;
            }
            var furnace = player.inventory?.loot?.entitySource?.GetComponent<BaseOven>();
            return furnace != null && (furnace.name.Contains("furnace") || furnace.name.Contains("refinery"));
        }
        #endregion

        private void PutToContainer(Dictionary<string, Item> items, string shortname, ItemContainer container, BasePlayer player, ref int spaceLeft, bool reserve = false) {
            if (items.ContainsKey(shortname)) {
                var stackToContainer = TakeStack(items[shortname]);
                if (stackToContainer != null) {
                    stackToContainer.MoveToContainer(container);
                    ReturnToPlayer(player, items[shortname]);
                } else {
                    items[shortname].MoveToContainer(container);
                    items.Remove(shortname);
                }
            }

            if (items.ContainsKey(shortname) || reserve) {
                spaceLeft--;
            }
        }

        private Item TakeStack(Item item, int targetCount = -1) {
            int count = item.info.stackable;
            if (targetCount != -1) {
                count = Math.Min(item.info.stackable, targetCount);
            }
            if (item.amount > count) {
                return item.SplitItem(count);
            }

            return null;
        }

        private void FilterOnlyNotProcessed(Dictionary<string, Item> items, BasePlayer player, ItemContainer container, FurnaceType type) {
            var allowedItems = type == FurnaceType.Furnace ? furnaceItems : refineryItems;
            foreach (var shortname in items.Keys.ToList()) {
                if (allowedItems.ContainsValue(shortname) && !items.ContainsKey(allowedItems.FirstOrDefault(x => x.Value == shortname).Key)) {
                    ReturnToPlayer(player, items[shortname]);
                    items.Remove(shortname);
                }
            }
        }

        private void FilterWhitelist(Dictionary<string, Item> items, BasePlayer player, ItemContainer container, FurnaceType type) {
            var allowedItems = type == FurnaceType.Furnace ? furnaceItems : refineryItems;
            foreach (var shortname in items.Keys.ToList()) {
                if (!allowedItems.ContainsKey(shortname) && !allowedItems.ContainsValue(shortname)) {
                    ReturnToPlayer(player, items[shortname]);
                    items.Remove(shortname);
                }
            }
        }

        private void ReturnToPlayer(BasePlayer player, Item item) {
            while (item != null) {
                var nextToGive = TakeStack(item);
                if (nextToGive == null) {
                    nextToGive = item;
                    item = null;
                }

                player.GiveItem(nextToGive);
            }
        }

        private Dictionary<string, Item> CloneAndPackItems(ItemContainer container) {
            var items = new Dictionary<string, Item>();
            foreach (var it in container.itemList) {
                if (items.ContainsKey(it.info.shortname)) {
                    items[it.info.shortname].amount += it.amount;
                } else {
                    items[it.info.shortname] = ItemManager.Create(it.info, it.amount, it.skin);
                }
            }

            return items;
        }

        private void ClearContainer(ItemContainer container) {
            while (container.itemList.Count > 0) {
                var item = container.itemList[0];
                item.RemoveFromContainer();
                item.Remove(0f);
            }
        }
        #region GUI HANDLERS
        [ConsoleCommand("activesort.sort")]
        private void HandlerSortLoot(ConsoleSystem.Arg arg) {
            if (arg.Player() == null) {
                return;
            }

            if (arg.Player().GetComponent<ActiveSortUI>() == null) {
                return;
            }

            SortLoot(arg.Player());
        }
        #endregion
        #region GUI COMPONENT
        class ActiveSortUI : MonoBehaviour {
            private const string baseName = "activesort.sort_button";
            private Vector2 buttonBasePosition = new Vector2(365, 85);
            private BasePlayer player;
            private ActiveSort plugin;

            public void Init(ActiveSort plugin) {
                this.plugin = plugin;
                RenderUI();
            }

            private void RenderUI() {
                List<CuiElement> elements = new List<CuiElement>();
                var button = new CuiElement {
                    Parent = "Overlay",
                    Name = baseName
                };
                button.Components.Add(new CuiButtonComponent {
                    Color = ParseColor(plugin.config.ButtonColorHex),
                    Command = "activesort.sort"
                });
                button.Components.Add(new CuiRectTransformComponent {
                    AnchorMin = "0.5 0.0",
                    AnchorMax = "0.5 0.0",
                    OffsetMax = $"{buttonBasePosition.x + plugin.config.ButtonPositionOffset.x + plugin.config.ButtonSize.x / 2} " +
                    $"{buttonBasePosition.y + plugin.config.ButtonPositionOffset.y + plugin.config.ButtonSize.y / 2}",
                    OffsetMin = $"{buttonBasePosition.x + plugin.config.ButtonPositionOffset.x - plugin.config.ButtonSize.x / 2} " +
                    $"{buttonBasePosition.y + plugin.config.ButtonPositionOffset.y - plugin.config.ButtonSize.y / 2}"
                });
                elements.Add(button);

                var caption = new CuiElement {
                    Parent = baseName,
                    Name = $"{baseName}.caption"
                };
                caption.Components.Add(new CuiTextComponent {
                    Align = TextAnchor.MiddleCenter,
                    Color = ParseColor(plugin.config.ButtonCaptionColorHex),
                    Font = plugin.config.ButtonCaptionIsBold ? "RobotoCondensed-Bold.ttf" : "robotocondensed-regular.ttf",
                    Text = plugin.l10n("BUTTON_CAPTION", player.UserIDString),
                    FontSize = plugin.config.ButtonCaptionFontSize
                });
                caption.Components.Add(new CuiRectTransformComponent {
                    AnchorMax = "1 1",
                    AnchorMin = "0 0"
                });
                elements.Add(caption);

                CuiHelper.AddUi(player, elements);
            }

            private string ParseColor(string hexColor) {
                Color c = new Color();
                ColorUtility.TryParseHtmlString(hexColor, out c);
                return $"{c.r} {c.g} {c.b} {c.a}";
            }

            void Awake() {
                player = GetComponent<BasePlayer>();
            }

            void OnDestroy() {
                CuiHelper.DestroyUi(player, baseName);
            }
        }
        #endregion
    }
}