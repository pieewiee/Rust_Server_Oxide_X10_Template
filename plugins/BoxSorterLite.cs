using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Box Sorter Lite", "haggbart", "1.0.9")]
    [Description("Sort your loot in boxes using an intuitive interface.")]
    internal class BoxSorterLite : RustPlugin
    {
        private const string permUse = "boxsorterlite.use";
        private Dictionary<ulong, BoxCategory> _skinbox = new Dictionary<ulong, BoxCategory>();
        private string boxContentCommand;
        private string boxContentName;
        private CuiElementContainer cuiContainer;
        private List<Item> selectedItems;

        private struct BoxCategory
        {
            public string Name { get; set; }
            public HashSet<int> ItemIds { get; set; }
        }

        #region init

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["select"] = "Select category...",
                ["clothing"] = "Clothing",
                ["weapons"] = "Weapons",
                ["ammo"] = "Ammo",
                ["medicine"] = "Medicine",
                ["tools"] = "Tools",
                ["resources"] = "Resources",
                ["refined"] = "Refined",
                ["explosives"] = "Explosives",
                ["components"] = "Components",
                ["electronics"] = "Electronics",
                ["keys"] = "Keys",
                ["building"] = "Building",
                ["food"] = "Food"
            }, this);
        }

        private void OnServerInitialized()
        {
            _skinbox = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, BoxCategory>>("BoxSorterLite");
            if (_skinbox.Count != 12)
                InitSorters();
            permission.RegisterPermission(permUse, this);
        }

        private void InitSorters()
        {
            Puts("Generating default values...");
            var explosives = new HashSet<int>
            {
                -1878475007, // satchel charge
                1248356124, // c4
                -592016202, // explosives
                1840822026, // beancan grenade
                -1841918730, // high velocity rocket
                1638322904, // incendiary rocket
                -742865266, // rocket
                -1321651331, // explosive ammo
                349762871 // he grenade
            };
            var resources = new HashSet<int>(ItemManager.GetItemDefinitions()
                .Where(k => k.category == ItemCategory.Resources).Select(x => x.itemid));
            resources.Remove(1523195708); // targeting computer
            resources.Remove(634478325); // cctv
            var refined = new HashSet<int>
            {
                317398316, // hqm
                69511070, // metal fragments
                -1581843485, // sulfur
                -151838493, // wood
                -2099697608, // stone
                -946369541, // low grade fuel
                -1938052175, // charcoal
                -265876753 // gunpowder
            };
            resources.ExceptWith(refined);
            
            var electronics = new HashSet<int>(ItemManager.GetItemDefinitions()
                .Where(k => k.category == ItemCategory.Electrical).Select(x => x.itemid));
            
            var keys = new HashSet<int>
            {
                -629028935, // electric fuse
                37122747, // green keycard
                -484206264, // blue keycard
                -1880870149 // red keycard  
            };
            var misc = new HashSet<int>(ItemManager.GetItemDefinitions().Where(k => k.category == ItemCategory.Misc)
                .Select(x => x.itemid));
            misc.ExceptWith(keys);
            var itemids = new HashSet<int>(ItemManager.GetItemDefinitions()
                .Where(k => k.category == ItemCategory.Attire).Select(x => x.itemid));
            _skinbox.Add(576569265, new BoxCategory {Name = "clothing", ItemIds = itemids});
            itemids = new HashSet<int>(ItemManager.GetItemDefinitions().Where(k => k.category == ItemCategory.Weapon)
                .Select(x => x.itemid));
            itemids.Remove(1840822026); // remove beancan
            _skinbox.Add(854718942, new BoxCategory {Name = "weapons", ItemIds = itemids});
            itemids = new HashSet<int>(ItemManager.GetItemDefinitions()
                .Where(k => k.category == ItemCategory.Ammunition).Select(x => x.itemid));
            itemids.ExceptWith(explosives);
            _skinbox.Add(813269955, new BoxCategory {Name = "ammo", ItemIds = itemids});
            itemids = new HashSet<int>(ItemManager.GetItemDefinitions().Where(k => k.category == ItemCategory.Medical)
                .Select(x => x.itemid));
            _skinbox.Add(882223700, new BoxCategory {Name = "medicine", ItemIds = itemids});
            itemids = new HashSet<int>(ItemManager.GetItemDefinitions().Where(k => k.category == ItemCategory.Tool)
                .Select(x => x.itemid));
            itemids.ExceptWith(explosives);
            _skinbox.Add(1192724938, new BoxCategory {Name = "tools", ItemIds = itemids});
            _skinbox.Add(809171741, new BoxCategory {Name = "resources", ItemIds = resources});
            _skinbox.Add(1353721544, new BoxCategory {Name = "refined", ItemIds = refined});
            _skinbox.Add(798455489, new BoxCategory {Name = "explosives", ItemIds = explosives});
            itemids = new HashSet<int>(ItemManager.GetItemDefinitions().Where(k => k.category == ItemCategory.Component)
                .Select(x => x.itemid)) {1523195708, 634478325};
            // added targeting computer, cctv
            itemids.Remove(-629028935); // remove electric fuse
            _skinbox.Add(854002617, new BoxCategory {Name = "components", ItemIds = itemids});
            _skinbox.Add(1588282308, new BoxCategory {Name = "electronics", ItemIds = electronics});
            _skinbox.Add(1686299197, new BoxCategory {Name = "keys", ItemIds = keys});
            itemids = new HashSet<int>(ItemManager.GetItemDefinitions()
                .Where(k => k.category == ItemCategory.Construction).Select(x => x.itemid));
            itemids.UnionWith(new HashSet<int>(ItemManager.GetItemDefinitions()
                .Where(k => k.category == ItemCategory.Items).Select(x => x.itemid)));
            itemids.UnionWith(new HashSet<int>(ItemManager.GetItemDefinitions()
                .Where(k => k.category == ItemCategory.Traps).Select(x => x.itemid)));
            itemids.UnionWith(misc);
            _skinbox.Add(813563521, new BoxCategory {Name = "building", ItemIds = itemids});
            Interface.Oxide.DataFileSystem.WriteObject("BoxSorterLite", _skinbox);
        }

        #endregion init

        #region hooks

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (!player.IsBuildingAuthed() || !(entity is BoxStorage) ||
                !permission.UserHasPermission(player.UserIDString, permUse) || entity.prefabID == 1560881570) return;
            CreateBoxUI(player, entity);
        }

        private void OnPlayerLootEnd(PlayerLoot inventory)
        {
            var player = inventory.GetComponent<BasePlayer>();
            CuiHelper.DestroyUi(player, "BoxUIHeader");
            CuiHelper.DestroyUi(player, "BoxUIContent");
            CuiHelper.DestroyUi(player, "BoxUISort");
        }

        #endregion hooks

        #region CUI

        private void CreateBoxUI(BasePlayer player, BaseEntity entity, int header = 0)
        {
            if (header == 0)
                AddHeaderUI(player);
            cuiContainer = ContainerOffset("BoxUIContent", "0.65 0.65 0.65 0.06", "0.5 0", "0.5 0", "192.5 16",
                "423 75.9");
            if (_skinbox.ContainsKey(entity.skinID) || entity.prefabID == 1844023509)
                AddSelectedUI(player, entity);
            else
                AddCategoryUI(player, cuiContainer);
        }

        private void AddHeaderUI(BasePlayer player)
        {
            cuiContainer = ContainerOffset("BoxUIHeader", "0.86 0.86 0.86 0.2", "0.5 0", "0.5 0", "192.5 78.5",
                "423 98");
            Label(ref cuiContainer, "BoxUIHeader", "<b>BOX SORTING</b>", "0.91 0.87 0.83 1.0", 13, "0.051 0", "1 0.95",
                0f, TextAnchor.MiddleLeft);
            CuiHelper.DestroyUi(player, "BoxUIHeader");
            CuiHelper.AddUi(player, cuiContainer);
        }

        private readonly StringBuilder xY1 = new StringBuilder();
        private readonly StringBuilder xY2 = new StringBuilder();
        private readonly StringBuilder cD = new StringBuilder();

        private void AddCategoryUI(BasePlayer player, CuiElementContainer container)
        {
            Label(ref container, "BoxUIContent", lang.GetMessage("select", this, player.UserIDString),
                "0.91 0.87 0.83 1.0", 10, "0.02 0.76", "1 0.96", 0f, TextAnchor.UpperLeft);

            const float x20 = 0.246f;
            const float x10 = 1 - 4.0f * x20;
            const float yspace = 0.25f;

            float x1 = x10;
            float x2 = x20;
            var y2 = 0.75f;
            float y1 = y2 - 0.2f;
            var count = 0;
            foreach (var box in _skinbox)
            {
                xY1.Append(x1);
                xY1.Append(" ");
                xY1.Append(y1);
                xY2.Append(x2);
                xY2.Append(" ");
                xY2.Append(y2);
                cD.Append("boxsorter.select ");
                cD.Append(box.Key);
                Button(ref container, "BoxUIContent", "0.65 0.65 0.65 0.06",
                    lang.GetMessage(box.Value.Name, this, player.UserIDString), "0.77 0.77 0.77 1", 9, xY1.ToString(),
                    xY2.ToString(), cD.ToString());
                xY1.Length = 0;
                xY2.Length = 0;
                cD.Length = 0;
                count++;
                x1 += x20;
                x2 += x20;

                if (count % 4 != 0) continue;
                y1 -= yspace;
                y2 -= yspace;
                x1 = x10;
                x2 = x20;
            }

            CuiHelper.DestroyUi(player, "BoxUIContent");
            CuiHelper.AddUi(player, container);
        }

        private void AddSelectedUI(BasePlayer player, BaseEntity entity)
        {
            if (entity.prefabID == 1844023509) // refigerator
            {
                boxContentName = "food";
                boxContentCommand = string.Empty;
            }
            else
            {
                boxContentName = _skinbox[entity.skinID].Name;
                boxContentCommand = "boxsorter.select 969292267";
            }

            Button(ref cuiContainer, "BoxUIContent", "0.65 0.65 0.65 0.12",
                lang.GetMessage(boxContentName, this, player.UserIDString), "0.77 0.77 0.77 1", 20, "0 0", "1 1",
                boxContentCommand);
            CuiHelper.DestroyUi(player, "BoxUIContent");
            CuiHelper.AddUi(player, cuiContainer);
            cuiContainer = ContainerOffset("BoxUISort", "0.75 0.75 0.75 0", "0.5 0", "0.5 0", "75 341", "179 359");
            Button(ref cuiContainer, "BoxUISort", "0.75 0.75 0.75 0.1", "<b>──></b>", "0.91 0.87 0.83 0.8", 11, "0 0",
                "0.48 1", "boxsorter.insert");
            Button(ref cuiContainer, "BoxUISort", "0.415 0.5 0.258 0.4", "<b>Sort</b>", "0.607 0.705 0.431", 11,
                "0.52 0", "1 1", "boxsorter.sort");
            CuiHelper.DestroyUi(player, "BoxUISort");
            CuiHelper.AddUi(player, cuiContainer);
        }

        [ConsoleCommand("boxsorter.sort")]
        private void CmdSort(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection.player as BasePlayer;
            if (player == null) return;
            BaseEntity entity = player.inventory.loot.entitySource;
            if (entity == null || !player.IsBuildingAuthed()) return;
            ItemContainer inventory = player.inventory.loot.entitySource.GetComponent<StorageContainer>()?.inventory;
            if (inventory == null) return;
            selectedItems = inventory.itemList.ToList();
            while (inventory.itemList.Count > 0)
                inventory.itemList[0].RemoveFromContainer();
            selectedItems.Sort((x, y) => x.info.itemid.CompareTo(y.info.itemid));
            if (_skinbox.ContainsKey(entity.skinID))
                foreach (Item item in selectedItems)
                    if (!_skinbox[entity.skinID].ItemIds.Contains(item.info.itemid) &&
                        !player.inventory.containerMain.IsFull())
                        item.MoveToContainer(player.inventory.containerMain);
                    else
                        item.MoveToContainer(inventory);
            else if (entity.prefabID == 1844023509)
                foreach (Item item in selectedItems)
                    item.MoveToContainer(inventory);
        }

        [ConsoleCommand("boxsorter.insert")]
        private void CmdInsert(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection.player as BasePlayer;
            if (player == null) return;
            BaseEntity entity = player.inventory.loot.entitySource;
            if (entity == null || !player.IsBuildingAuthed()) return;
            ItemContainer inventory = player.inventory.loot.entitySource.GetComponent<StorageContainer>()?.inventory;
            if (inventory == null) return;
            selectedItems = new List<Item>();
            if (_skinbox.ContainsKey(entity.skinID))
                selectedItems = player.inventory.containerMain.itemList
                    .Where(x => _skinbox[entity.skinID].ItemIds.Contains(x.info.itemid)).ToList();

            else if (entity.prefabID == 1844023509)
                selectedItems = player.inventory.containerMain.itemList.Where(x => x.info.category == ItemCategory.Food)
                    .ToList();
            foreach (Item item in selectedItems)
            {
                if (!item.CanMoveTo(inventory)) break;
                item.MoveToContainer(inventory);
            }
        }

        [ConsoleCommand("boxsorter.select")]
        private void CmdSelect(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection.player as BasePlayer;
            if (player == null) return;
            BaseEntity entity = player.inventory.loot.entitySource;
            if (entity == null || !player.IsBuildingAuthed()) return;
            ItemContainer inventory = player.inventory.loot.entitySource.GetComponent<StorageContainer>()?.inventory;
            if (inventory == null) return;
            uint skinid;
            uint.TryParse(arg.Args[0], out skinid);
            entity.skinID = skinid;
            entity.SendNetworkUpdate();
            CuiHelper.DestroyUi(player, "BoxUISort");
            CreateBoxUI(player, entity, 1);
        }

        #endregion CUI

        #region CUI Helper 

        //================================= [ Хомячок ] =======================================

        private static CuiElementContainer ContainerOffset(string panelName, string color, string aMin, string aMax,
            string offSetMin = "0 0", string offSetMax = "0 0", float fadein = 0f, bool useCursor = false,
            string parent = "Overlay")
        {
            var newElement = new CuiElementContainer
            {
                {
                    new CuiPanel
                    {
                        Image = {Color = color, FadeIn = fadein},
                        RectTransform =
                            {AnchorMin = aMin, AnchorMax = aMax, OffsetMin = offSetMin, OffsetMax = offSetMax},
                        CursorEnabled = useCursor
                    },
                    new CuiElement().Parent = parent,
                    panelName
                }
            };
            return newElement;
        }

        private void Panel(ref CuiElementContainer container, string panel, string color, string aMin, string aMax,
            float fadein = 0f, bool cursor = false)
        {
            container.Add(new CuiPanel
                {
                    Image = {Color = color, FadeIn = fadein},
                    RectTransform = {AnchorMin = aMin, AnchorMax = aMax},
                    CursorEnabled = cursor
                },
                panel);
        }

        private static void Label(ref CuiElementContainer container, string panel, string text, string color,
            int size, string aMin, string aMax, float fadein = 0f, TextAnchor align = TextAnchor.MiddleCenter)
        {
            container.Add(new CuiLabel
                {
                    Text =
                    {
                        FontSize = size,
                        Align = align,
                        Text = text,
                        Color = color,
                        Font = "robotocondensed-regular.ttf",
                        FadeIn = fadein
                    },
                    RectTransform = {AnchorMin = aMin, AnchorMax = aMax}
                },
                panel);
        }

        private static void Button(ref CuiElementContainer container, string panel, string color, string text,
            string color1, int size, string aMin, string aMax, string command,
            TextAnchor align = TextAnchor.MiddleCenter)
        {
            container.Add(new CuiButton
                {
                    Button = {Color = color, Command = command, FadeIn = 0f},
                    RectTransform = {AnchorMin = aMin, AnchorMax = aMax},
                    Text =
                    {
                        Text = text,
                        FontSize = size,
                        Align = align,
                        Color = color1,
                        Font = "robotocondensed-regular.ttf"
                    }
                },
                panel);
        }
        #endregion CUI Helper
    }
}