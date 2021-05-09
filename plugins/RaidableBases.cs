#define USE_HTN_HOOK
//#define DEBUG_DRAWINGS

using Facepunch;
using Network;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using Oxide.Game.Rust.Cui;
using Oxide.Game.Rust.Libraries;
using Rust;
using Rust.Ai;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using static NPCPlayerApex;

namespace Oxide.Plugins
{
    [Info("Raidable Bases", "nivex", "2.0.4")]
    [Description("Create fully automated raidable bases with npcs.")]
    class RaidableBases : RustPlugin
    {
        [PluginReference]
        private Plugin DangerousTreasures, Vanish, LustyMap, ZoneManager, Economics, ServerRewards, Map, GUIAnnouncements, CopyPaste, Friends, Clans, Kits, TruePVE, Spawns, NightLantern, Wizardry, NextGenPVE, Imperium, Backpacks;

        protected static SingletonBackbone Backbone { get; set; }
        protected RotationCycle Cycle { get; set; } = new RotationCycle();
        public Dictionary<int, List<BaseEntity>> Bases { get; } = new Dictionary<int, List<BaseEntity>>();
        public Dictionary<int, RaidableBase> Raids { get; } = new Dictionary<int, RaidableBase>();
        public Dictionary<BaseEntity, RaidableBase> RaidEntities { get; } = new Dictionary<BaseEntity, RaidableBase>();
        public Dictionary<int, RaidableBase> Indices { get; set; } = new Dictionary<int, RaidableBase>();
        public Dictionary<ulong, RaidableBase> Npcs { get; set; } = new Dictionary<ulong, RaidableBase>();
        protected Dictionary<ulong, DelaySettings> PvpDelay { get; } = new Dictionary<ulong, DelaySettings>();
        private Dictionary<string, SkinInfo> Skins { get; } = new Dictionary<string, SkinInfo>();
        private Dictionary<MonumentInfo, float> monuments { get; set; } = new Dictionary<MonumentInfo, float>();
        private Dictionary<Vector3, ZoneInfo> managedZones { get; set; } = new Dictionary<Vector3, ZoneInfo>();
        private Dictionary<int, MapInfo> mapMarkers { get; set; } = new Dictionary<int, MapInfo>();
        private Dictionary<int, string> lustyMarkers { get; set; } = new Dictionary<int, string>();
        protected Dictionary<RaidableType, RaidableSpawns> raidSpawns { get; set; } = new Dictionary<RaidableType, RaidableSpawns>();
        public StoredData storedData { get; set; } = new StoredData();
        protected Coroutine despawnCoroutine { get; set; }
        protected Coroutine maintainCoroutine { get; set; }
        protected Coroutine scheduleCoroutine { get; set; }
        protected Coroutine gridCoroutine { get; set; }
        private Stopwatch gridStopwatch { get; } = new Stopwatch();
        private StringBuilder _sb { get; } = new StringBuilder();
        private bool wiped { get; set; }
        private float lastSpawnRequestTime { get; set; }
        private float gridTime { get; set; }
        private bool IsUnloading { get; set; }
        private int _maxOnce { get; set; }
        private bool bypassRestarting { get; set; }
        private bool debugMode { get; set; }
        private static BuildingTables Buildings { get; set; } = new BuildingTables();
        private bool maintainEnabled { get; set; }
        private bool scheduleEnabled { get; set; }
        private static Dictionary<string, ItemDefinition> shortnameDefs { get; set; } = new Dictionary<string, ItemDefinition>();
        private static List<string> shortnames { get; set; } = new List<string>();
        private static Dictionary<Vector3, float> LoadingTimes { get; set; } = new Dictionary<Vector3, float>();
        private List<string> Helms { get; set; } = new List<string> { "hat.wolf", "attire.hide.helterneck", "hat.beenie", "hat.boonie", "bucket.helmet", "burlap.headwrap", "hat.candle", "hat.cap", "clatter.helmet", "coffeecan.helmet", "deer.skull.mask", "heavy.plate.helmet", "hat.miner", "partyhat", "riot.helmet", "wood.armor.helmet", "mask.balaclava", "mask.bandana", "metal.facemask", "nightvisiongoggles", "hat.dragonmask", "hat.ratmask", "attire.nesthat" };
        private List<string> Boots { get; set; } = new List<string> { "boots.frog", "shoes.boots", "burlap.shoes", "attire.hide.boots" };
        private List<string> Gloves { get; set; } = new List<string> { "burlap.gloves.new", "burlap.gloves", "roadsign.gloves", "tactical.gloves" };
        private List<string> Vests { get; set; } = new List<string> { "bone.armor.suit", "heavy.plate.jacket", "jacket.snow", "jacket", "wood.armor.jacket", "attire.hide.poncho", "metal.plate.torso", "roadsign.jacket" };
        private List<string> Legs { get; set; } = new List<string> { "burlap.trousers", "heavy.plate.pants", "attire.hide.pants", "pants.shorts", "wood.armor.pants", "pants" };
        private List<string> Shirts { get; set; } = new List<string> { "hoodie", "burlap.shirt", "shirt.collared", "attire.hide.vest", "shirt.tanktop", "tshirt.long", "tshirt" };
        private List<string> Other { get; set; } = new List<string> { "movembermoustachecard", "movembermoustache", "sunglasses02black", "sunglasses02camo", "sunglasses02red", "sunglasses03black", "sunglasses03chrome", "sunglasses03gold", "sunglasses", "twitchsunglasses", "gloweyes", "attire.bunnyears" };
        private List<uint> Furnaces { get; set; } = new List<uint> { 1374462671, 2931042549, 1402456403 };
        private List<uint> BBQs { get; set; } = new List<uint> { 2409469892, 128449714 };
        private List<uint> Lanterns { get; set; } = new List<uint> { 3887352222, 1889323056, 630866573, 4027991414, 1392608348 };
        private List<uint> Refineries { get; set; } = new List<uint> { 1057236622, 919097516 };

        //campfire: 4160694184, campfire_static: 1339281147, cursedcauldron.deployed: 1348425051, fireplace.deployed: 110576239, hobobarrel_static: 754638672, skull_fire_pit: 1906669538

        private struct Constants
        {
            public const float RADIUS = 25f;
            public const float CELL_SIZE = 12.5f;
            public const float INSTRUCTION_TIME = 0.02f;
            public const uint LARGE_WOODEN_BOX = 2206646561;
            public const uint SMALL_WOODEN_BOX = 1560881570;
            public const uint COFFIN_STORAGE = 4080262419;
            public const uint ID_CODELOCK = 3518824735;
            public const uint ID_EXPLOSIONMARKER = 4060989661;
            public const uint ID_FIREBALLSMALL = 2086405370;
            public const uint ID_FIREBALL = 3369311876;
            public const uint ID_HIGHEXTERNALWOODENWALL = 1745077396;
            public const uint ID_HIGHEXTERNALSTONEWALL = 1585379529;
            public const uint ID_LADDER = 2150203378;
            public const uint ID_MURDERER = 3879041546;
            public const uint ID_RADIUSMARKER = 2849728229;
            public const uint ID_SPHERE = 3211242734;
            public const uint ID_SCIENTIST = 4223875851;
            public const uint ID_VENDINGMARKER = 3459945130;
            public const uint ID_VENDINGMACHINE = 186002280;
        }
        
        public class ZoneInfo
        {
            public Vector3 Position;
            public Vector3 Size;
            public float Distance;
            public OBB OBB;
        }

        public class SkinInfo
        {
            public List<ulong> skins = new List<ulong>();
            public List<ulong> allSkins = new List<ulong>();
        }

        public class BaseProfile
        {
            public List<TreasureItem> BaseLootList { get; set; } = new List<TreasureItem>();

            public BuildingOptions Options { get; set; } = new BuildingOptions();

            public BaseProfile()
            {

            }

            public BaseProfile(BuildingOptions options)
            {
                Options = options;
            }

            public static BaseProfile Clone(BaseProfile profile)
            {
                return profile.MemberwiseClone() as BaseProfile;
            }
        }

        private class BuildingTables
        {
            public List<TreasureItem> Normal { get; set; } = new List<TreasureItem>();
            public List<TreasureItem> Default { get; set; } = new List<TreasureItem>();
            public Dictionary<DayOfWeek, List<TreasureItem>> Weekday { get; set; } = new Dictionary<DayOfWeek, List<TreasureItem>>();
            public Dictionary<string, BaseProfile> Profiles { get; set; } = new Dictionary<string, BaseProfile>();

            public bool IsValid(string fileName)
            {
                if (string.IsNullOrEmpty(fileName))
                {
                    return false;
                }

                foreach (var profile in Profiles.Values)
                {
                    if (profile.Options.AdditionalBases.ContainsKey(fileName))
                    {
                        return true;
                    }
                }

                return Profiles.ContainsKey(fileName);
            }
        }

        public class DelaySettings
        {
            public RaidableBase RaidableBase;
            public Timer Timer;
            public bool AllowPVP;
        }

        private enum SpawnResult
        {
            Failure,
            Transfer,
            Success,
            Skipped
        }

        public enum RaidableType
        {
            None,
            Manual,
            Scheduled,
            Maintained,
            Grid
        }

        public enum RaidableMode
        {
            Disabled = -1,
            Normal = 0,
            Random = 9999
        }

        public class ResourcePath
        {
            public List<uint> Blocks { get; }
            public List<uint> TrueDamage { get; }
            public string ExplosionMarker { get; }
            public string CodeLock { get; }
            public string FireballSmall { get; }
            public string Fireball { get; }
            public string HighExternalWoodenWall { get; }
            public string HighExternalStoneWall { get; }
            public string Ladder { get; }
            public string Murderer { get; }
            public string RadiusMarker { get; }
            public string Sphere { get; }
            public string Scientist { get; }
            public string VendingMarker { get; }

            public ResourcePath()
            {
                Blocks = new List<uint> { 803699375, 2194854973, 919059809, 3531096400, 310235277, 2326657495, 3234260181, 72949757, 1745077396, 1585379529, 995542180, 372561515 };
                TrueDamage = new List<uint> { 976279966, 3824663394, 1202834203, 4254045167, 1745077396, 1585379529 };
                CodeLock = StringPool.Get(Constants.ID_CODELOCK);
                ExplosionMarker = StringPool.Get(Constants.ID_EXPLOSIONMARKER);
                Fireball = StringPool.Get(Constants.ID_FIREBALL);
                FireballSmall = StringPool.Get(Constants.ID_FIREBALLSMALL);
                HighExternalStoneWall = StringPool.Get(Constants.ID_HIGHEXTERNALSTONEWALL);
                HighExternalWoodenWall = StringPool.Get(Constants.ID_HIGHEXTERNALWOODENWALL);
                Ladder = StringPool.Get(Constants.ID_LADDER);
                Murderer = StringPool.Get(Constants.ID_MURDERER);
                RadiusMarker = StringPool.Get(Constants.ID_RADIUSMARKER);
                Scientist = StringPool.Get(Constants.ID_SCIENTIST);
                Sphere = StringPool.Get(Constants.ID_SPHERE);
                VendingMarker = StringPool.Get(Constants.ID_VENDINGMARKER);
            }
        }

        public class SingletonBackbone : SingletonComponent<SingletonBackbone>
        {
            public RaidableBases Plugin { get; private set; }
            public ResourcePath Path { get; private set; }
            public Oxide.Core.Libraries.Lang lang => Plugin.lang;
            private StringBuilder sb => Plugin._sb;
            public StoredData Data => Plugin.storedData;
            public Dictionary<ulong, FinalDestination> Destinations { get; set; }

            public float OceanLevel { get; set; }

            public SingletonBackbone(RaidableBases plugin)
            {
                Plugin = plugin;
                Path = new ResourcePath();
                Destinations = new Dictionary<ulong, FinalDestination>();
                OceanLevel = WaterSystem.OceanLevel;
                InvokeRepeating(CheckOceanLevel, 60f, 60f);
            }

            public void Destroy()
            {
                Path = null;
                Plugin = null;
                DestroyImmediate(Instance);
            }

            private void CheckOceanLevel()
            {
                if (OceanLevel != WaterSystem.OceanLevel)
                {
                    OceanLevel = WaterSystem.OceanLevel;

                    RaidableSpawns rs;
                    if (!Plugin.raidSpawns.TryGetValue(RaidableType.Grid, out rs))
                    {
                        return;
                    }

                    rs.TryAddRange(CacheType.Submerged);
                }
            }

            public void InitializeSkins()
            {
                foreach (var def in ItemManager.GetItemDefinitions())
                {
                    if (!shortnames.Contains(def.shortname)) shortnames.Add(def.shortname);
                    var imd = def.GetComponent<ItemModDeployable>();
                    if (imd == null || shortnameDefs.ContainsKey(imd.entityPrefab.resourcePath)) continue;
                    shortnameDefs.Add(imd.entityPrefab.resourcePath, def);
                }
            }

            public void Message(BasePlayer player, string key, params object[] args)
            {
                if (player.IsValid())
                {
                    Plugin.Player.Message(player, GetMessage(key, player.UserIDString, args), _config.Settings.ChatID);
                }
            }

            public string GetMessage(string key, string id = null, params object[] args)
            {
                sb.Length = 0;

                if (_config.EventMessages.Prefix && id != null && id != "server_console" && !key.EndsWith("Flag"))
                {
                    sb.Append(lang.GetMessage("Prefix", Plugin, id));
                }

                sb.Append(id == "server_console" || id == null ? RemoveFormatting(lang.GetMessage(key, Plugin, id)) : lang.GetMessage(key, Plugin, id));

                return args.Length > 0 ? string.Format(sb.ToString(), args) : sb.ToString();
            }

            public string RemoveFormatting(string source) => source.Contains(">") ? Regex.Replace(source, "<.*?>", string.Empty) : source;

            public Timer Timer(float seconds, Action action) => Plugin.timer.Once(seconds, action);
        }

        public class Elevation
        {
            public float Min { get; set; }
            public float Max { get; set; }
        }

        public class RaidableSpawnLocation
        {
            public List<Vector3> Surroundings = new List<Vector3>();
            public Elevation Elevation = new Elevation();
            public Vector3 Location = Vector3.zero;
            public float WaterHeight;
            public float TerrainHeight;
            public float SpawnHeight;
            public float Radius;
            public bool AutoHeight;
        }

        public enum CacheType
        {
            Delete,
            Generic,
            Temporary,
            Privilege,
            Submerged,
        }

        public class RaidableSpawns
        {
            public readonly HashSet<RaidableSpawnLocation> Spawns = new HashSet<RaidableSpawnLocation>();
            private readonly Dictionary<CacheType, HashSet<RaidableSpawnLocation>> Cached = new Dictionary<CacheType, HashSet<RaidableSpawnLocation>>();

            public bool IsCustomSpawn { get; set; }

            public int Count
            {
                get
                {
                    return Spawns.Count;
                }
            }

            public void Add(RaidableSpawnLocation rsl, CacheType cacheType)
            {
                switch (cacheType)
                {
                    case CacheType.Submerged:
                        rsl.WaterHeight = TerrainMeta.WaterMap.GetHeight(rsl.Location);
                        rsl.Surroundings.Clear();

                        if (Backbone.Plugin.IsSubmerged(rsl))
                        {
                            return;
                        }
                        break;
                    case CacheType.Generic:
                        if (Backbone.Plugin.EventTerritory(rsl.Location))
                        {
                            return;
                        }
                        break;
                    case CacheType.Temporary:
                        string message;
                        if (!Backbone.Plugin.IsAreaSafe(ref rsl.Location, Constants.CELL_SIZE, Layers.Mask.Player_Server | Layers.Mask.Construction | Layers.Mask.Deployed, out cacheType, out message))
                        {
                            return;
                        }
                        break;
                    case CacheType.Privilege:
                        if (HasBuildingPrivilege(rsl.Location, _config.Settings.Management.CupboardDetectionRadius) != Vector3.zero)
                        {
                            return;
                        }
                        break;
                }

                Spawns.Add(rsl);
            }

            public void TryAddRange(CacheType cacheType = CacheType.Generic, bool forced = false)
            {
                HashSet<RaidableSpawnLocation> cache = Get(cacheType);

                foreach (var rsl in cache)
                {
                    if (forced)
                    {
                        Spawns.Add(rsl);
                    }
                    else Add(rsl, cacheType);
                }

                cache.RemoveWhere(rsl => Spawns.Contains(rsl));
            }

            public HashSet<RaidableSpawnLocation> Active => Spawns;

            public HashSet<RaidableSpawnLocation> Inactive(CacheType cacheType) => Get(cacheType);

            public void Check()
            {
                if (Spawns.Count == 0)
                {
                    TryAddRange();
                }
            }

            public RaidableSpawnLocation GetRandom()
            {
                var rsl = Spawns.ElementAt(UnityEngine.Random.Range(0, Spawns.Count));

                Remove(rsl, CacheType.Generic);

                return rsl;
            }

            public HashSet<RaidableSpawnLocation> Get(CacheType cacheType)
            {
                HashSet<RaidableSpawnLocation> cache;
                if (!Cached.TryGetValue(cacheType, out cache))
                {
                    Cached[cacheType] = cache = new HashSet<RaidableSpawnLocation>();
                }

                return cache;
            }

            public void AddNear(Vector3 target, float radius, CacheType cacheType)
            {
                HashSet<RaidableSpawnLocation> cache = Get(cacheType);

                AddNear(cache, target, radius);
            }

            public void AddNear(HashSet<RaidableSpawnLocation> cache, Vector3 target, float radius)
            {
                foreach (var b in cache)
                {
                    if (InRange(target, b.Location, radius))
                    {
                        Spawns.Add(b);
                    }
                }

                cache.RemoveWhere(x => Spawns.Contains(x));
            }

            public void Remove(RaidableSpawnLocation a, CacheType cacheType)
            {
                Get(cacheType).Add(a);
                Spawns.Remove(a);
            }

            public float RemoveNear(Vector3 target, float radius, CacheType cacheType, RaidableType type)
            {
                if (cacheType == CacheType.Generic)
                {
                    float r = GetDistance(type);

                    if (r > radius)
                    {
                        radius = r;
                    }
                }

                var cache = Get(cacheType);

                foreach (var b in Spawns)
                {
                    if (InRange(target, b.Location, radius))
                    {
                        cache.Add(b);
                    }
                }

                Spawns.RemoveWhere(x => cache.Contains(x));
                return radius;
            }

            public RaidableSpawns(HashSet<RaidableSpawnLocation> spawns)
            {
                Spawns = spawns;
                IsCustomSpawn = true;
            }

            public RaidableSpawns()
            {

            }
        }

        private class MapInfo
        {
            public string Url;
            public string IconName;
            public Vector3 Position;
        }

        public class PlayerInfo
        {
            public int TotalRaids { get; set; }
            public int Raids { get; set; }
            public PlayerInfo() { }
        }

        public class RotationCycle
        {
            private Dictionary<RaidableMode, List<string>> _buildings = new Dictionary<RaidableMode, List<string>>();

            public void Add(RaidableType type, RaidableMode mode, string key)
            {
                if (!_config.Settings.Management.RequireAllSpawned || type == RaidableType.Grid || type == RaidableType.Manual)
                {
                    return;
                }

                List<string> keyList;
                if (!_buildings.TryGetValue(mode, out keyList))
                {
                    _buildings[mode] = keyList = new List<string>();
                }

                if (!keyList.Contains(key))
                {
                    keyList.Add(key);
                }
            }

            public bool CanSpawn(RaidableType type, RaidableMode mode, string key)
            {
                if (mode == RaidableMode.Disabled)
                {
                    return false;
                }

                if (!_config.Settings.Management.RequireAllSpawned || mode == RaidableMode.Random || type == RaidableType.Grid || type == RaidableType.Manual)
                {
                    return true;
                }

                List<string> keyList;
                if (!_buildings.TryGetValue(mode, out keyList))
                {
                    return true;
                }

                return TryClear(type, mode, keyList) || !keyList.Contains(key);
            }

            public bool TryClear(RaidableType type, RaidableMode mode, List<string> keyList)
            {
                foreach (var profile in Buildings.Profiles)
                {
                    if (MustExclude(type, profile.Value.Options.AllowPVP))
                    {
                        continue;
                    }

                    if (!keyList.Contains(profile.Key) && FileExists(profile.Key))
                    {
                        return false;
                    }

                    foreach (var kvp in profile.Value.Options.AdditionalBases)
                    {
                        if (!keyList.Contains(kvp.Key) && FileExists(kvp.Key))
                        {
                            return false;
                        }
                    }
                }

                keyList.Clear();
                return true;
            }
        }

        public class StoredData
        {
            public Dictionary<string, PlayerInfo> Players { get; set; } = new Dictionary<string, PlayerInfo>();
            public Dictionary<string, UI.Info> UI { get; set; } = new Dictionary<string, UI.Info>();
            public string RaidTime { get; set; } = DateTime.MinValue.ToString();
            public int TotalEvents { get; set; }
            public StoredData() { }
        }

        private class PlayWithFire : FacepunchBehaviour
        {
            private FireBall fireball { get; set; }
            private BaseEntity target { get; set; }
            private bool fireFlung { get; set; }
            private Coroutine mcCoroutine { get; set; }

            public BaseEntity Target
            {
                get
                {
                    return target;
                }
                set
                {
                    target = value;
                    enabled = true;
                }
            }

            private void Awake()
            {
                fireball = GetComponent<FireBall>();
                enabled = false;
            }

            private void FixedUpdate()
            {
                if (!IsValid(target) || target.Health() <= 0)
                {
                    fireball.Extinguish();
                    Destroy(this);
                    return;
                }

                fireball.transform.RotateAround(target.transform.position, Vector3.up, 5f);
                fireball.transform.hasChanged = true;
            }

            public void FlingFire(BaseEntity attacker)
            {
                if (fireFlung) return;
                fireFlung = true;
                mcCoroutine = StartCoroutine(MakeContact(attacker));
            }

            private IEnumerator MakeContact(BaseEntity attacker)
            {
                float distance = Vector3.Distance(fireball.ServerPosition, attacker.transform.position);

                while (!Backbone.Plugin.IsUnloading && attacker != null && fireball != null && !fireball.IsDestroyed && !InRange(fireball.ServerPosition, attacker.transform.position, 2.5f))
                {
                    fireball.ServerPosition = Vector3.MoveTowards(fireball.ServerPosition, attacker.transform.position, distance * 0.1f);
                    yield return CoroutineEx.waitForSeconds(0.3f);
                }
            }

            private void OnDestroy()
            {
                if (mcCoroutine != null)
                {
                    StopCoroutine(mcCoroutine);
                    mcCoroutine = null;
                }

                Destroy(this);
            }
        }

        public class PlayerInputEx : FacepunchBehaviour
        {
            public BasePlayer player { get; set; }
            private InputState input { get; set; }
            private RaidableBase raid { get; set; }

            public void Setup(BasePlayer player, RaidableBase raid)
            {
                this.player = player;
                this.raid = raid;
                raid.Inputs[player] = this;
                input = player.serverInput;
                InvokeRepeating(Repeater, 0.1f, 0.1f);
            }

            public void Restart()
            {
                CancelInvoke(Repeater);
                InvokeRepeating(Repeater, 0.1f, 0.1f);
            }

            private void Repeater()
            {
                if (raid == null)
                {
                    Destroy(this);
                    return;
                }

                if (!player || !player.IsConnected)
                {
                    raid.TryInvokeResetOwner();
                    Destroy(this);
                    return;
                }

                TryPlaceLadder(player, raid);
            }

            public bool TryPlaceLadder(BasePlayer player, RaidableBase raid)
            {
                if (player.svActiveItemID == 0)
                {
                    return false;
                }

                var input = player.serverInput;

                if (!input.WasJustReleased(BUTTON.FIRE_PRIMARY) && !input.IsDown(BUTTON.FIRE_PRIMARY))
                {
                    return false;
                }

                Item item = player.GetActiveItem();

                if (item?.info.shortname != "ladder.wooden.wall")
                {
                    return false;
                }

                if (raid.Options.RequiresCupboardAccessLadders && !player.CanBuild())
                {
                    Backbone.Message(player, "Ladders Require Building Privilege!");
                    return false;
                }

                RaycastHit hit;
                if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 4f, Layers.Mask.Construction, QueryTriggerInteraction.Ignore))
                {
                    return false;
                }

                var block = hit.GetEntity();

                if (!block.IsValid() || block.OwnerID != 0 || !Backbone.Path.Blocks.Contains(block.prefabID)) // walls and foundations
                {
                    return false;
                }

                int amount = item.amount;
                var action = new Action(() =>
                {
                    if (raid == null || item == null || item.amount != amount || IsLadderNear(hit.point))
                    {
                        return;
                    }

                    var rot = Quaternion.LookRotation(hit.normal, Vector3.up);
                    var e = GameManager.server.CreateEntity(Backbone.Path.Ladder, hit.point, rot, true);

                    if (e == null)
                    {
                        return;
                    }

                    e.gameObject.SendMessage("SetDeployedBy", player, SendMessageOptions.DontRequireReceiver);
                    e.OwnerID = 0;
                    e.Spawn();
                    item.UseItem(1);

                    var planner = item.GetHeldEntity() as Planner;

                    if (planner != null)
                    {
                        var deployable = planner?.GetDeployable();

                        if (deployable != null && deployable.setSocketParent && block.SupportsChildDeployables())
                        {
                            e.SetParent(block, true, false);
                        }
                    }

                    raid.Entities.Add(e);
                    raid.BuiltList.Add(e.net.ID);
                });

                player.Invoke(action, 0.1f);
                return true;
            }

            public static bool IsLadderNear(Vector3 target)
            {
                var list = Pool.GetList<BaseLadder>();

                Vis.Entities(target, 0.3f, list, Layers.Mask.Deployed, QueryTriggerInteraction.Ignore);

                bool result = list.Count > 0;

                Pool.FreeList(ref list);

                return result;
            }

            private void OnDestroy()
            {
                CancelInvoke();
                raid?.Inputs?.Remove(player);
                Destroy(this);
            }
        }

        public class FinalDestination : FacepunchBehaviour
        {
            public NPCPlayerApex npc;
            private List<Vector3> positions;
            public AttackOperator.AttackType attackType;
            private NpcSettings settings;
            private bool isRanged;
            private BasePlayer target;
            private ulong userID;
            public bool stationary;
            private Vector3 home;
            private float maxRoamRange;

            private void OnDestroy()
            {
                Backbone?.Destinations?.Remove(userID);
                CancelInvoke();
                Destroy(this);
            }
            
            public void Set(NPCPlayerApex npc, List<Vector3> positions, NpcSettings settings, Vector3 home, float maxRoamRange)
            {
                this.npc = npc;
                this.positions = positions;
                this.settings = settings;
                this.home = home;
                this.maxRoamRange = maxRoamRange;
                attackType = IsMelee(npc) ? AttackOperator.AttackType.CloseRange : AttackOperator.AttackType.LongRange;
                isRanged = attackType != AttackOperator.AttackType.CloseRange;
                Backbone.Destinations[userID = npc.userID] = this;
                if (!(stationary = positions == null))
                {
                    InvokeRepeating(Go, 0f, 7.5f);
                }
                else InvokeRepeating(Attack, 0f, 1f);
            }

            public void Attack(BasePlayer player, bool converge = true)
            {
                if (target == player)
                {
                    return;
                }

                if (!IsInvoking(Attack))
                {
                    InvokeRepeating(Attack, 0f, 1f);
                }

                if (npc.Stats.AggressionRange < 150f)
                {
                    npc.Stats.AggressionRange += 150f;
                    npc.Stats.DeaggroRange += 100f;
                }

                npc.AttackTarget = player;
                npc.lastAttacker = player;
                npc.AiContext.LastAttacker = player;
                target = player;

                if (converge)
                {
                    Converge(player);
                }
            }

            private void Attack()
            {
                if (stationary)
                {
                    StationaryAttack();
                    return;
                }

                if (npc.AttackTarget == null || !(npc.AttackTarget is BasePlayer))
                {
                    return;
                }

                var attacker = npc.AttackTarget as BasePlayer;

                if (attacker.IsDead() || !InRange(attacker.transform.position, npc.transform.position, maxRoamRange * 2f))
                {
                    Forget();
                    CancelInvoke(Attack);
                    InvokeRepeating(Go, 0f, 7.5f);
                    return;
                }

                if (!npc.GetMounted())
                {
                    npc.NeverMove = false;
                    npc.IsStopped = false;
                    npc.RandomMove();
                }

                if (attacker.IsVisible(npc.eyes.position, attacker.eyes.position))
                {
                    HumanAttackOperator.AttackEnemy(npc.AiContext, attackType);
                }
            }

            private void StationaryAttack()
            {
                npc.SetAimDirection(new Vector3(npc.transform.forward.x, 0, npc.transform.forward.z));

                if (npc.AttackTarget == null || !(npc.AttackTarget is BasePlayer))
                {
                    return;
                }

                var attacker = npc.AttackTarget as BasePlayer;

                if (attacker.IsDead())
                {
                    Forget();
                    return;
                }

                if (attacker.IsVisible(npc.eyes.position, attacker.eyes.position))
                {
                    npc.SetAimDirection((attacker.transform.position - npc.transform.position).normalized);
                    HumanAttackOperator.AttackEnemy(npc.AiContext, attackType);
                }
            }

            private void Forget()
            {
                npc.Stats.AggressionRange = settings.AggressionRange;
                npc.Stats.DeaggroRange = settings.AggressionRange * 1.125f;
                npc.lastDealtDamageTime = Time.time;
                npc.AttackTarget = null;
                npc.lastAttacker = null;
                npc.lastAttackedTime = Time.time;
                npc.LastAttackedDir = Vector3.zero;
                npc.SetFact(Facts.HasEnemy, 0, true, true);
                npc.SetFact(Facts.EnemyRange, 3, true, true);
                npc.SetFact(Facts.AfraidRange, 1, true, true);
                npc.SetFact(Facts.IsAggro, 0, true, true);
                npc.SetFact(Facts.IsAfraid, 0, true, true);
                npc.SetPlayerFlag(BasePlayer.PlayerFlags.Relaxed, true);
                //npc.AiContext.ChairTarget = null;
                //npc.AiContext.Chairs
                //HumanNavigateToOperator.NavigateToMountableLoc(npc.AiContext, HumanNavigateToOperator.OperatorType.MountableChair);
            }

            public void Warp()
            {
                var position = positions.GetRandom();

                npc.Pause();
                npc.ServerPosition = position;
                npc.GetNavAgent.Warp(position);
                npc.stuckDuration = 0f;
                npc.IsStuck = false;
                npc.Resume();
            }

            private void Go()
            {
                if (npc.GetMounted())
                {
                    return;
                }

                if (npc.IsHeadUnderwater())
                {
                    npc.Kill();
                    Destroy(this);
                    return;
                }

                if (npc.AttackTarget == null)
                {
                    npc.NeverMove = true;

                    if (npc.IsStuck)
                    {
                        Warp();
                    }

                    var position = positions.GetRandom();

                    if (npc.GetNavAgent == null || !npc.GetNavAgent.isOnNavMesh)
                    {
                        npc.finalDestination = position;
                    }
                    else npc.GetNavAgent.SetDestination(position);

                    npc.IsStopped = false;
                    npc.Destination = position;
                }
            }

            private void Converge(BasePlayer player)
            {
                foreach (var fd in Backbone.Destinations.Values)
                {
                    if (fd != this && fd.npc.IsValid() && fd.npc.AttackTarget == null && fd.npc.IsAlive() && fd.npc.Distance(npc) < 25f)
                    {
                        if (fd.attackType != attackType) continue;
                        fd.npc.SetFact(Facts.AllyAttackedRecently, 1, true, true);
                        fd.npc.SetFact(Facts.AttackedRecently, 1, true, true);
                        fd.Attack(player, false);
                        fd.Attack();
                    }
                }
            }

            private bool IsMelee(BasePlayer player)
            {
                var attackEntity = player.GetHeldEntity() as AttackEntity;

                if (attackEntity == null)
                {
                    return false;
                }

                return attackEntity is BaseMelee;
            }

            public bool NpcCanRoam(Vector3 destination) => InRange(home, destination, maxRoamRange);
        }

        public class BlockProperties
        {
            public OBB obb;
            public Vector3 position;
            public uint prefabID;
        }

        public class BackpackData
        {
            public DroppedItemContainer backpack;
            public BasePlayer player;
            public ulong userID;
        }

        public class RaidableBase : FacepunchBehaviour
        {
            private const float Radius = Constants.RADIUS;
            private List<BaseMountable> mountables { get; set; } = Pool.GetList<BaseMountable>();
            public Hash<uint, float> conditions { get; set; } = Pool.Get<Hash<uint, float>>();
            public List<StorageContainer> _containers { get; set; } = Pool.GetList<StorageContainer>();
            public List<StorageContainer> _allcontainers { get; set; } = Pool.GetList<StorageContainer>();
            public Dictionary<BasePlayer, PlayerInputEx> Inputs { get; set; } = Pool.Get<Dictionary<BasePlayer, PlayerInputEx>>();
            public List<NPCPlayerApex> npcs { get; set; } = Pool.GetList<NPCPlayerApex>();
            public Dictionary<uint, BasePlayer> records { get; set; } = Pool.Get<Dictionary<uint, BasePlayer>>();
            public Dictionary<ulong, BasePlayer> raiders { get; set; } = Pool.Get<Dictionary<ulong, BasePlayer>>();
            public List<BasePlayer> friends { get; set; } = Pool.GetList<BasePlayer>();
            public List<BasePlayer> intruders { get; set; } = Pool.GetList<BasePlayer>();
            public Dictionary<uint, BackpackData> corpses { get; set; } = Pool.Get<Dictionary<uint, BackpackData>>();
            private Dictionary<FireBall, PlayWithFire> fireballs { get; set; } = Pool.Get<Dictionary<FireBall, PlayWithFire>>();
            private List<Vector3> foundations { get; set; } = Pool.GetList<Vector3>();
            private List<BlockProperties> blockProperties { get; set; } = Pool.GetList<BlockProperties>();
            private List<SphereEntity> spheres { get; set; } = Pool.GetList<SphereEntity>();
            private List<BaseEntity> lights { get; set; } = Pool.GetList<BaseEntity>();
            private List<BaseOven> ovens { get; set; } = Pool.GetList<BaseOven>();
            public List<AutoTurret> turrets { get; set; } = Pool.GetList<AutoTurret>();
            private List<Door> doors { get; set; } = Pool.GetList<Door>();
            private List<CustomDoorManipulator> doorControllers { get; set; } = Pool.GetList<CustomDoorManipulator>();
            public Dictionary<string, float> lastActive { get; set; } = Pool.Get<Dictionary<string, float>>();
            public List<string> ids { get; set; } = Pool.GetList<string>();
            private List<Locker> lockers { get; set; } = Pool.GetList<Locker>();
            private Dictionary<string, ulong> skins { get; set; } = Pool.Get<Dictionary<string, ulong>>();
            private Dictionary<uint, ulong> skinIds { get; set; } = Pool.Get<Dictionary<uint, ulong>>();
            private Dictionary<TriggerBase, BaseEntity> triggers { get; set; } = Pool.Get<Dictionary<TriggerBase, BaseEntity>>();

            public BuildingPrivlidge priv { get; set; }
            private Dictionary<string, List<string>> npcKits { get; set; }
            private MapMarkerExplosion explosionMarker { get; set; }
            private MapMarkerGenericRadius genericMarker { get; set; }
            private VendingMachineMapMarker vendingMarker { get; set; }
            private Coroutine setupRoutine { get; set; } = null;
            private bool IsInvokingCanFinish { get; set; }
            public bool IsDespawning { get; set; }
            public Vector3 PastedLocation { get; set; }
            public Vector3 Location { get; set; }
            public string BaseName { get; set; }
            public int BaseIndex { get; set; } = -1;
            public uint BuildingID { get; set; }
            public uint NetworkID { get; set; } = uint.MaxValue;
            public Color NoneColor { get; set; }
            public BasePlayer owner { get; set; }
            public bool ownerFlag { get; set; }
            public string ID { get; set; } = "0";
            public ulong ownerId { get; set; }
            public float spawnTime { get; set; }
            public float despawnTime { get; set; }
            public bool AllowPVP { get; set; }
            public BuildingOptions Options { get; set; }
            public bool IsAuthed { get; set; }
            public bool IsOpened { get; set; } = true;
            public bool IsUnloading { get; set; }
            public int uid { get; set; }
            public int npcMaxAmount { get; set; }
            public RaidableType Type { get; set; }
            public string DifficultyMode { get; set; }
            public bool IsLooted => CanUndo();
            public bool IsLoading => setupRoutine != null;
            private bool markerCreated { get; set; }
            private bool lightsOn { get; set; }
            public bool killed { get; set; }
            private int itemAmountSpawned { get; set; }
            private bool privSpawned { get; set; }
            public string markerName { get; set; }
            public string NoMode { get; set; }
            public bool isAuthorized { get; set; }
            public bool IsEngaged { get; set; }
            private ItemDefinition lowgradefuel { get; set; } = ItemManager.FindItemDefinition("lowgradefuel");
            public List<BaseEntity> Entities { get; set; } = new List<BaseEntity>();
            public List<uint> BuiltList { get; set; } = new List<uint>();
            public RaidableSpawns rs { get; set; }
            public float RemoveNearDistance { get; set; }
            public bool Damaged { get; set; }

            private void ResetToPool<T>(ICollection<T> collection)
            {
                collection.Clear();

                Pool.Free(ref collection);
            }

            public void FreePool()
            {
                ResetToPool(conditions);
                ResetToPool(Inputs);
                ResetToPool(records);
                ResetToPool(raiders);
                ResetToPool(corpses);
                ResetToPool(fireballs);
                ResetToPool(lastActive);
                ResetToPool(blockProperties);
                ResetToPool(mountables);
                ResetToPool(_containers);
                ResetToPool(_allcontainers);
                ResetToPool(npcs);
                ResetToPool(friends);
                ResetToPool(intruders);
                ResetToPool(foundations);
                ResetToPool(spheres);
                ResetToPool(lights);
                ResetToPool(ovens);
                ResetToPool(turrets);
                ResetToPool(doors);
                ResetToPool(doorControllers);
                ResetToPool(ids);
                ResetToPool(lockers);
                ResetToPool(skins);
                ResetToPool(skinIds);
                ResetToPool(triggers);
            }

            private void Awake()
            {
                markerName = _config.Settings.Markers.MarkerName;
                spawnTime = Time.realtimeSinceStartup;
            }

            private void OnDestroy()
            {
                Despawn();
                Destroy(gameObject);
                Destroy(this);
            }

            private void OnTriggerEnter(Collider col)
            {
                var player = col?.ToBaseEntity() as BasePlayer;
                var m = col?.ToBaseEntity() as BaseMountable;
                var targets = new List<BasePlayer>();

                if (player.IsValid())
                {
                    m = player.GetMounted();

                    targets.Add(player);
                }

                if (m.IsValid() && TryRemoveMountable(m, targets = GetMountedPlayers(m)))
                {
                    return;
                }

                targets.RemoveAll(target => target.IsNpc || intruders.Contains(target));

                foreach (var target in targets)
                {
                    if (IsLoading && !CanBypass(target))
                    {
                        RemovePlayer(target);
                        continue;
                    }

                    if (!target.IsConnected && Time.time - target.sleepStartTime < 2f)
                    {
                        continue;
                    }

                    OnEnterRaid(target);
                }
            }

            //public bool JustRespawned(BasePlayer player)
            //{
            //    return (uint)Epoch.Current - player.lifeStory?.timeBorn <= 2f;
            //}

            public void OnEnterRaid(BasePlayer p)
            {
                if (!IsValid(p) || p.IsNpc)
                {
                    return;
                }

                if (Type != RaidableType.None)
                {
                    if (IsBanned(p) || Teleported(p)) // || JustRespawned(p))
                    {
                        if (RemovePlayer(p))
                        {
                            return;
                        }
                    }
                }

                if (!intruders.Contains(p))
                {
                    intruders.Add(p);
                }

                Protector();

                if (!intruders.Contains(p))
                {
                    return;
                }

                PlayerInputEx component;
                if (Inputs.TryGetValue(p, out component))
                {
                    Destroy(component);
                }

                if (_config.Settings.Management.AllowLadders)
                {
                    p.gameObject.AddComponent<PlayerInputEx>().Setup(p, this);
                }

                StopUsingWand(p);

                if (_config.EventMessages.AnnounceEnterExit)
                {
                    Backbone.Message(p, AllowPVP ? "OnPlayerEntered" : "OnPlayerEnteredPVE");
                }

                UI.UpdateStatusUI(p);
                Interface.CallHook("OnPlayerEnteredRaidableBase", p, Location, AllowPVP);

                if (_config.Settings.Management.PVPDelay > 0)
                {
                    Interface.CallHook("OnPlayerPvpDelayEntry", p);
                }
            }

            private void OnTriggerExit(Collider col)
            {
                var player = col?.ToBaseEntity() as BasePlayer;

                /*if (player.IsValid() && IsUnderTerrain(player.transform.position))
                {
                    return;
                }*/

var m = col?.ToBaseEntity() as BaseMountable;
                var players = new List<BasePlayer>();

                if (m.IsValid())
                {
                    players = GetMountedPlayers(m);
                }
                else if (player.IsValid() && !player.IsNpc)
                {
                    players.Add(player);
                }

                if (players.Count == 0)
                {
                    return;
                }

                foreach (var p in players)
                {
                    OnPlayerExit(p, p.IsDead());
                }
            }

            public void OnPlayerExit(BasePlayer p, bool skipDelay = true)
            {
                UI.DestroyStatusUI(p);

                PlayerInputEx component;
                if (Inputs.TryGetValue(p, out component))
                {
                    Destroy(component);
                }

                if (!intruders.Contains(p))
                {
                    return;
                }

                intruders.Remove(p);
                Interface.CallHook("OnPlayerExitedRaidableBase", p, Location, AllowPVP);

                if (_config.Settings.Management.PVPDelay > 0)
                {
                    if (skipDelay || !Backbone.Plugin.IsPVE() || !AllowPVP)
                    {
                        goto enterExit;
                    }

                    if (_config.EventMessages.AnnounceEnterExit)
                    {
                        string arg = Backbone.GetMessage("PVPFlag", p.UserIDString).Replace("[", string.Empty).Replace("] ", string.Empty);
                        Backbone.Message(p, "DoomAndGloom", arg, _config.Settings.Management.PVPDelay);
                    }

                    ulong id = p.userID;
                    DelaySettings ds;
                    if (!Backbone.Plugin.PvpDelay.TryGetValue(id, out ds))
                    {
                        Backbone.Plugin.PvpDelay[id] = ds = new DelaySettings
                        {
                            Timer = Backbone.Timer(_config.Settings.Management.PVPDelay, () =>
                            {
                                Interface.CallHook("OnPlayerPvpDelayExpired", p);
                                Backbone.Plugin.PvpDelay.Remove(id);
                            }),
                            AllowPVP = AllowPVP,
                            RaidableBase = this
                        };

                        return;
                    }

                    ds.Timer.Reset();

                    return;
                }

                enterExit:
                if (_config.EventMessages.AnnounceEnterExit)
                {
                    Backbone.Message(p, AllowPVP ? "OnPlayerExit" : "OnPlayerExitPVE");
                }
            }

            private bool IsBanned(BasePlayer p)
            {
                if (Backbone.Plugin.HasPermission(p, banPermission))
                {
                    if (CanMessage(p))
                    {
                        Backbone.Message(p, "Banned");
                    }

                    return true;
                }

                return false;
            }

            private bool Teleported(BasePlayer p)
            {
                if (!_config.Settings.Management.AllowTeleport && p.IsConnected && !CanBypass(p))
                {
                    if (NearFoundation(p.transform.position)) // if (InRange(p.transform.position, Location, Options.ProtectionRadius * 0.9f, false))
                    {
                        if (CanMessage(p))
                        {
                            Backbone.Message(p, "CannotTeleport");
                        }

                        return true;
                    }
                }

                return false;
            }

            private bool IsHogging(BasePlayer player)
            {
                if (!_config.Settings.Management.PreventHogging || !player.IsValid() || CanBypass(player))
                {
                    return false;
                }

                foreach (var raid in Backbone.Plugin.Raids.Values)
                {
                    if (raid.IsOpened && raid.BaseIndex != BaseIndex && raid.Any(player.userID, false))
                    {
                        if (CanMessage(player))
                        {
                            Backbone.Message(player, "HoggingFinishYourRaid", PositionToGrid(raid.Location));
                        }

                        return true;
                    }
                }

                if (Backbone.Plugin.HasPermission(player, bypassBlockPermission))
                {
                    return false;
                }

                return false;
            }

            private void CheckCorpses()
            {
                var keys = Pool.GetList<uint>();

                foreach (var data in corpses)
                {
                    if (EjectCorpse(data.Key, data.Value))
                    {
                        keys.Add(data.Key);
                    }
                }

                foreach (uint key in keys)
                {
                    corpses.Remove(key);
                }

                Pool.FreeList(ref keys);
            }

            private void Protector()
            {
                if (corpses.Count > 0)
                {
                    CheckCorpses();
                }

                if (Type == RaidableType.None || intruders.Count == 0)
                {
                    return;
                }

                var targets = new List<BasePlayer>(intruders);

                foreach (var target in targets)
                {
                    if (target == null || target == owner || friends.Contains(target) || CanBypass(target))
                    {
                        continue;
                    }

                    if (CanEject(target) || _config.Settings.Management.EjectSleepers && Type != RaidableType.None && target.IsSleeping())
                    {
                        intruders.Remove(target);
                        RemovePlayer(target);
                    }
                    else if (ownerId.IsSteamId())
                    {
                        friends.Add(target);
                    }
                }
            }

            public void DestroyUI()
            {
                foreach (var player in intruders)
                {
                    UI.DestroyStatusUI(player);
                }
            }

            public static void Unload()
            {
                foreach (var raid in Backbone.Plugin.Raids.Values)
                {
                    if (raid.setupRoutine != null)
                    {
                        raid.StopCoroutine(raid.setupRoutine);
                    }

                    raid.IsUnloading = true;
                    raid.CancelInvoke(raid.Lights);
                }
            }

            public void Despawn()
            {
                IsOpened = false;

                if (killed)
                {
                    return;
                }

                killed = true;

                Interface.CallHook("OnRaidableBaseDespawn", Location, spawnTime, ID);

                SetNoDrops();
                CancelInvoke();
                DestroyFire();
                DestroyInputs();
                RemoveSpheres();
                KillNpc();
                StopAllCoroutines();
                RemoveMapMarkers();
                DestroyUI();

                if (!IsUnloading)
                {
                    //ServerMgr.Instance.StartCoroutine(Backbone.Plugin.UndoRoutine_OLD(BaseIndex, Location, Entities.ToList()));
                    Backbone.Plugin.UndoPaste(BaseIndex, Location, Options.ProtectionRadius, Entities.ToList());
                }

                Backbone.Plugin.Raids.Remove(uid);

                if (Backbone.Plugin.Raids.Count == 0)
                {
                    if (IsUnloading)
                    {
                        UnsetStatics();
                    }
                    else Backbone.Plugin.UnsubscribeHooks();
                }

                if (rs != null)
                {
                    rs.AddNear(Location, RemoveNearDistance, CacheType.Generic);
                }

                Interface.CallHook("OnRaidableBaseEnded", Location, 0);
                Locations.Remove(PastedLocation);
                FreePool();
                Destroy(gameObject);
                Destroy(this);
            }

            public bool AddLooter(BasePlayer looter)
            {
                if (!IsAlly(looter) || looter.IsFlying || Backbone.Plugin.IsInvisible(looter))
                {
                    return false;
                }

                UpdateStatus(looter);

                if (!raiders.ContainsKey(looter.userID))
                {
                    raiders.Add(looter.userID, looter);
                    return true;
                }

                return false;
            }

            private void FillAmmoTurret(AutoTurret turret)
            {
                if (isAuthorized || IsUnloading || turret.IsDestroyed)
                {
                    return;
                }

                foreach (var id in turret.authorizedPlayers)
                {
                    if (id.userid.IsSteamId())
                    {
                        isAuthorized = true;
                        return;
                    }
                }

                var attachedWeapon = turret.GetAttachedWeapon();

                if (attachedWeapon == null)
                {
                    return;
                }

                int p = Math.Max(_config.Weapons.Ammo.AutoTurret, attachedWeapon.primaryMagazine.capacity);
                turret.inventory.AddItem(attachedWeapon.primaryMagazine.ammoType, p, 0uL);
                attachedWeapon.primaryMagazine.contents = attachedWeapon.primaryMagazine.capacity;
                attachedWeapon.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                turret.Invoke(turret.UpdateTotalAmmo, 0.25f);
            }

            private void FillAmmoGunTrap(GunTrap gt)
            {
                if (IsUnloading || gt.IsDestroyed)
                {
                    return;
                }

                if (gt.ammoType == null)
                {
                    gt.ammoType = ItemManager.FindItemDefinition("ammo.handmade.shell");
                }

                var ammo = gt.inventory.GetSlot(0);

                if (ammo == null)
                {
                    gt.inventory.AddItem(gt.ammoType, _config.Weapons.Ammo.GunTrap);
                }
                else ammo.amount = _config.Weapons.Ammo.GunTrap;
            }

            private void FillAmmoFogMachine(FogMachine fm)
            {
                if (lowgradefuel == null || IsUnloading || fm.IsDestroyed)
                {
                    return;
                }

                fm.inventory.AddItem(lowgradefuel, _config.Weapons.Ammo.FogMachine);
            }

            private void FillAmmoFlameTurret(FlameTurret ft)
            {
                if (lowgradefuel == null || IsUnloading || ft.IsDestroyed)
                {
                    return;
                }

                ft.inventory.AddItem(lowgradefuel, _config.Weapons.Ammo.FlameTurret);
            }

            private void FillAmmoSamSite(SamSite ss)
            {
                if (IsUnloading || ss.IsDestroyed)
                {
                    return;
                }

                if (!ss.HasAmmo())
                {
                    Item item = ItemManager.Create(ss.ammoType, _config.Weapons.Ammo.SamSite);

                    if (!item.MoveToContainer(ss.inventory))
                    {
                        item.Remove();
                    }
                    else ss.ammoItem = item;
                }
                else if (ss.ammoItem != null && ss.ammoItem.amount < _config.Weapons.Ammo.SamSite)
                {
                    ss.ammoItem.amount = _config.Weapons.Ammo.SamSite;
                }
            }

            private void OnWeaponItemPreRemove(Item item)
            {
                if (isAuthorized || IsUnloading)
                {
                    return;
                }

                if (priv != null && !priv.IsDestroyed)
                {
                    foreach (var id in priv.authorizedPlayers)
                    {
                        if (id.userid.IsSteamId())
                        {
                            isAuthorized = true;
                            return;
                        }
                    }
                }

                var weapon = item.parent?.entityOwner;

                if (weapon is AutoTurret)
                {
                    weapon.Invoke(() => FillAmmoTurret(weapon as AutoTurret), 0.1f);
                }
                else if (weapon is GunTrap)
                {
                    weapon.Invoke(() => FillAmmoGunTrap(weapon as GunTrap), 0.1f);
                }
                else if (weapon is SamSite)
                {
                    weapon.Invoke(() => FillAmmoSamSite(weapon as SamSite), 0.1f);
                }
            }

            private void OnItemAddedRemoved(Item item, bool bAdded)
            {
                if (!bAdded)
                {
                    StartTryToEnd();
                }
            }

            public void StartTryToEnd()
            {
                if (!IsInvokingCanFinish && !IsLoading)
                {
                    IsInvokingCanFinish = true;
                    InvokeRepeating(TryToEnd, 0f, 1f);
                }
            }

            public void TryToEnd()
            {
                if (IsOpened && IsLooted && !IsLoading)
                {
                    CancelInvoke(TryToEnd);
                    AwardRaiders();
                    Undo();
                }
            }

            public void AwardRaiders()
            {
                var players = new List<BasePlayer>();
                var sb = new StringBuilder();

                foreach (var raider in raiders)
                {
                    var player = raider.Value;

                    if (player == null || player.IsFlying || !IsPlayerActive(player.userID))
                    {
                        continue;
                    }

                    if (_config.Settings.RemoveAdminRaiders && player.IsAdmin && Type != RaidableType.None)
                    {
                        continue;
                    }

                    sb.Append(player.displayName).Append(", ");
                    players.Add(player);
                }

                if (players.Count == 0)
                {
                    return;
                }

                if (Options.Levels.Level2)
                {
                    SpawnNpcs();
                }

                HandleAwards(players);

                sb.Length -= 2;
                string thieves = sb.ToString();
                string posStr = FormatGridReference(Location);

                Puts(Backbone.GetMessage("Thief", null, posStr, thieves));

                if (_config.EventMessages.AnnounceThief)
                {
                    foreach (var target in BasePlayer.activePlayerList)
                    {
                        Backbone.Message(target, "Thief", posStr, thieves);
                    }
                }

                Backbone.Plugin.SaveData();
            }

            private void HandleAwards(List<BasePlayer> players)
            {
                foreach (var raider in players)
                {
                    if (_config.RankedLadder.Enabled)
                    {
                        PlayerInfo playerInfo;
                        if (!Backbone.Data.Players.TryGetValue(raider.UserIDString, out playerInfo))
                        {
                            Backbone.Data.Players[raider.UserIDString] = playerInfo = new PlayerInfo();
                        }

                        playerInfo.TotalRaids++;
                        playerInfo.Raids++;
                    }

                    if (Options.Rewards.Money > 0 && Backbone.Plugin.Economics != null && Backbone.Plugin.Economics.IsLoaded)
                    {
                        double money = _config.Settings.Management.DivideRewards ? Options.Rewards.Money / players.Count : Options.Rewards.Money;
                        Backbone.Plugin.Economics?.Call("Deposit", raider.UserIDString, money);
                        Backbone.Message(raider, "EconomicsDeposit", money);
                    }

                    if (Options.Rewards.Points > 0 && Backbone.Plugin.ServerRewards != null && Backbone.Plugin.ServerRewards.IsLoaded)
                    {
                        int points = _config.Settings.Management.DivideRewards ? Options.Rewards.Points / players.Count : Options.Rewards.Points;
                        Backbone.Plugin.ServerRewards?.Call("AddPoints", raider.userID, points);
                        Backbone.Message(raider, "ServerRewardPoints", points);
                    }
                }
            }

            private List<string> messagesSent = new List<string>();



            public bool CanMessage(BasePlayer player)
            {
                if (player == null || messagesSent.Contains(player.UserIDString))
                {
                    return false;
                }

                string uid = player.UserIDString;

                messagesSent.Add(uid);
                Backbone.Timer(10f, () => messagesSent.Remove(uid));

                return true;
            }

            private bool CanBypass(BasePlayer player)
            {
                return Backbone.Plugin.HasPermission(player, canBypassPermission) || player.IsFlying;
            }

            public string Mode()
            {
                if (owner.IsValid())
                {
                    return string.Format("{0} {1}", owner.displayName, DifficultyMode.SentenceCase());
                }

                return DifficultyMode.SentenceCase();
            }

            private bool IsPlayerActive(ulong playerId)
            {
                if (_config.Settings.Management.LockTime <= 0f)
                {
                    return true;
                }

                float time;
                if (!lastActive.TryGetValue(playerId.ToString(), out time))
                {
                    return true;
                }

                return Time.realtimeSinceStartup - time <= _config.Settings.Management.LockTime * 60f;
            }

            public void TrySetOwner(BasePlayer attacker, BaseEntity entity, HitInfo hitInfo)
            {
                UpdateStatus(attacker);

                if (!_config.Settings.Management.UseOwners || !IsOpened || ownerId.IsSteamId() || IsOwner(attacker)) // || !InRange(attacker.transform.position, Location, 250f))
                {
                    return;
                }

                if (_config.Settings.Management.BypassUseOwnersForPVP && AllowPVP || _config.Settings.Management.BypassUseOwnersForPVE && !AllowPVP)
                {
                    return;
                }

                if (IsHogging(attacker))
                {
                    NullifyDamage(hitInfo);
                    return;
                }

                if (entity is NPCPlayerApex)
                {
                    SetOwner(attacker);
                    return;
                }

                if (!(entity is BuildingBlock) && !(entity is Door) && !(entity is SimpleBuildingBlock))
                {
                    return;
                }

                if (InRange(attacker.transform.position, Location, Options.ProtectionRadius) || IsLootingWeapon(hitInfo))
                {
                    SetOwner(attacker);
                }
            }

            public void SetOwner(BasePlayer player)
            {
                TryInvokeResetOwner();
                UpdateStatus(player);
                owner = player;
                ownerId = player.userID;
                UpdateMarker();
                ClearEnemies();
            }

            private void ClearEnemies()
            {
                if (raiders.Count == 0)
                {
                    return;
                }

                var list = Pool.GetList<ulong>();

                foreach (var raider in raiders)
                {
                    var target = raider.Value;

                    if (target == null || !target.IsConnected || !IsAlly(target))
                    {
                        list.Add(raider.Key);
                    }
                }

                foreach (var targetId in list)
                {
                    raiders.Remove(targetId);
                }

                Pool.FreeList(ref list);
            }

            public void CheckDespawn()
            {
                if (IsDespawning || _config.Settings.Management.DespawnMinutesInactive <= 0)
                {
                    return;
                }

                if (_config.Settings.Management.Engaged && !IsEngaged)
                {
                    return;
                }

                if (IsInvoking(Despawn))
                {
                    CancelInvoke(Despawn);
                }

                if (!_config.Settings.Management.DespawnMinutesInactiveReset && despawnTime != 0)
                {
                    return;
                }

                float time = _config.Settings.Management.DespawnMinutesInactive * 60f;
                despawnTime = Time.realtimeSinceStartup + time;
                Invoke(Despawn, time);
            }

            public bool EndWhenCupboardIsDestroyed()
            {
                if (_config.Settings.Management.EndWhenCupboardIsDestroyed && privSpawned)
                {
                    return priv == null || priv.IsDestroyed;
                }

                return false;
            }

            public bool CanUndo()
            {
                if (IsLoading)
                {
                    return false;
                }

                if (EndWhenCupboardIsDestroyed())
                {
                    return true;
                }

                if (_config.Settings.Management.RequireCupboardLooted && privSpawned)
                {
                    if (priv.IsValid() && !priv.IsDestroyed && !priv.inventory.IsEmpty())
                    {
                        return false;
                    }
                }

                foreach (string value in _config.Settings.Management.Inherit)
                {
                    foreach (var container in _allcontainers)
                    {
                        if (container == null || container.IsDestroyed || !container.ShortPrefabName.Contains(value, CompareOptions.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (!container.inventory.IsEmpty())
                        {
                            return false;
                        }
                    }
                }

                return true;
            }

            private bool CanPlayerBeLooted()
            {
                if (!_config.Settings.Management.PlayersLootableInPVE && !AllowPVP || !_config.Settings.Management.PlayersLootableInPVP && AllowPVP)
                {
                    return false;
                }

                return true;
            }

            private bool CanBeLooted(BasePlayer player, BaseEntity e)
            {
                if (IsProtectedWeapon(e))
                {
                    return _config.Settings.Management.LootableTraps;
                }

                if (e is NPCPlayerCorpse)
                {
                    return true;
                }

                if (e is LootableCorpse)
                {
                    if (CanBypass(player))
                    {
                        return true;
                    }

                    var corpse = e as LootableCorpse;

                    if (!corpse.playerSteamID.IsSteamId() || corpse.playerSteamID == player.userID || corpse.playerName == player.displayName)
                    {
                        return true;
                    }

                    return CanPlayerBeLooted();
                }
                else if (e is DroppedItemContainer)
                {
                    if (CanBypass(player))
                    {
                        return true;
                    }

                    var container = e as DroppedItemContainer;

                    if (!container.playerSteamID.IsSteamId() || container.playerSteamID == player.userID || container.playerName == player.displayName)
                    {
                        return true;
                    }

                    return CanPlayerBeLooted();
                }

                return true;
            }

            public static bool IsProtectedWeapon(BaseEntity e)
            {
                return e is GunTrap || e is FlameTurret || e is FogMachine || e is SamSite || e is AutoTurret;
            }

            public void OnLootEntityInternal(BasePlayer player, BaseEntity e)
            {
                UpdateStatus(player);

                if (e.OwnerID == player.userID || e is BaseMountable)
                {
                    return;
                }

                if (_config.Settings.Management.BlacklistedPickupItems.Any(value => !string.IsNullOrEmpty(value) && e.ShortPrefabName.Contains(value)))
                {
                    player.Invoke(player.EndLooting, 0.1f);
                    return;
                }

                if (e.HasParent() && e.GetParentEntity() is BaseMountable)
                {
                    return;
                }

                if (!CanBeLooted(player, e))
                {
                    player.Invoke(player.EndLooting, 0.1f);
                    return;
                }

                if (e is LootableCorpse || e is DroppedItemContainer)
                {
                    return;
                }

                if (player.GetMounted())
                {
                    Backbone.Message(player, "CannotBeMounted");
                    player.Invoke(player.EndLooting, 0.1f);
                    return;
                }

                if (Options.RequiresCupboardAccess && !player.CanBuild()) //player.IsBuildingBlocked())
                {
                    Backbone.Message(player, "MustBeAuthorized");
                    player.Invoke(player.EndLooting, 0.1f);
                    return;
                }

                if (!IsAlly(player))
                {
                    Backbone.Message(player, "OwnerLocked");
                    player.Invoke(player.EndLooting, 0.1f);
                    return;
                }

                if (raiders.Count > 0 && Type != RaidableType.None)
                {
                    CheckDespawn();
                }

                AddLooter(player);

                if (IsBox(e, true) || e is BuildingPrivlidge)
                {
                    StartTryToEnd();
                }
            }

            private void TryStartPlayingWithFire()
            {
                if (Options.Levels.Level1.Amount > 0)
                {
                    InvokeRepeating(StartPlayingWithFire, 2f, 2f);
                }
            }

            private void StartPlayingWithFire()
            {
                if (npcs.Count == 0)
                {
                    return;
                }

                var dict = Pool.Get<Dictionary<FireBall, PlayWithFire>>();

                foreach (var entry in fireballs)
                {
                    dict.Add(entry.Key, entry.Value);
                }

                foreach (var entry in dict)
                {
                    if (entry.Key == null || entry.Key.IsDestroyed)
                    {
                        Destroy(entry.Value);
                        fireballs.Remove(entry.Key);
                    }
                }

                dict.Clear();
                Pool.Free(ref dict);

                if (fireballs.Count >= Options.Levels.Level1.Amount || UnityEngine.Random.value > Options.Levels.Level1.Chance)
                {
                    return;
                }

                var npc = npcs.GetRandom();

                if (!IsValid(npc))
                {
                    return;
                }

                var fireball = GameManager.server.CreateEntity(Backbone.Path.FireballSmall, npc.transform.position + new Vector3(0f, 3f, 0f), Quaternion.identity, true) as FireBall;

                if (fireball == null)
                {
                    return;
                }

                var rb = fireball.GetComponent<Rigidbody>();

                if (rb != null)
                {
                    rb.isKinematic = false;
                    rb.useGravity = false;
                    rb.drag = 0f;
                }

                fireball.lifeTimeMax = 15f;
                fireball.lifeTimeMin = 15f;
                fireball.canMerge = false;
                fireball.Spawn();
                fireball.CancelInvoke(fireball.TryToSpread);

                var component = fireball.gameObject.AddComponent<PlayWithFire>();

                component.Target = npc;

                fireballs.Add(fireball, component);
            }

            private void SetNoDrops()
            {
                foreach (var container in _allcontainers)
                {
                    if (!container.IsValid()) continue;
                    container.dropChance = 0f;
                }
            }

            public void DestroyInputs()
            {
                if (Inputs.Count > 0)
                {
                    foreach (var input in Inputs.ToList())
                    {
                        Destroy(input.Value);
                    }

                    Inputs.Clear();
                }
            }

            public void DestroyFire()
            {
                if (fireballs.Count == 0)
                {
                    return;
                }

                foreach (var entry in fireballs)
                {
                    Destroy(entry.Value);

                    if (entry.Key == null)
                    {
                        continue;
                    }

                    entry.Key.Extinguish();
                }

                fireballs.Clear();
            }

            public void SetEntities(int baseIndex, List<BaseEntity> entities)
            {
                if (!IsLoading)
                {
                    Entities = entities;
                    BaseIndex = baseIndex;
                    setupRoutine = StartCoroutine(EntitySetup());
                }
            }

            private Vector3 GetCenterFromMultiplePoints()
            {
                if (foundations.Count <= 1)
                {
                    return PastedLocation;
                }

                float x = 0f;
                float z = 0f;

                foreach (var position in foundations)
                {
                    x += position.x;
                    z += position.z;
                }

                var vector = new Vector3(x / foundations.Count, 0f, z / foundations.Count);

                vector.y = GetSpawnHeight(vector);

                return vector;
            }

            private void CreateSpheres()
            {
                if (Options.SphereAmount <= 0 || Options.Silent)
                {
                    return;
                }

                for (int i = 0; i < Options.SphereAmount; i++)
                {
                    var sphere = GameManager.server.CreateEntity(Backbone.Path.Sphere, Location, default(Quaternion), true) as SphereEntity;

                    if (sphere == null)
                    {
                        break;
                    }

                    sphere.currentRadius = 1f;
                    sphere.Spawn();
                    sphere.LerpRadiusTo(Options.ProtectionRadius * 2f, Options.ProtectionRadius * 0.75f);
                    spheres.Add(sphere);
                }
            }

            private void CreateZoneWalls()
            {
                if (!Options.ArenaWalls.Enabled)
                {
                    return;
                }

                var center = new Vector3(Location.x, Location.y, Location.z);
                string prefab = Options.ArenaWalls.Ice ? StringPool.Get(921229511) : Options.ArenaWalls.Stone ? Backbone.Path.HighExternalStoneWall : Backbone.Path.HighExternalWoodenWall;
                float maxHeight = -200f;
                float minHeight = 200f;
                int raycasts = Mathf.CeilToInt(360 / Options.ArenaWalls.Radius * 0.1375f);

                foreach (var position in GetCircumferencePositions(center, Options.ArenaWalls.Radius, raycasts, false, 1f))
                {
                    float y = GetSpawnHeight(position, false);
                    maxHeight = Mathf.Max(y, maxHeight, TerrainMeta.WaterMap.GetHeight(position));
                    minHeight = Mathf.Min(y, minHeight);
                    center.y = minHeight;
                }

                float gap = prefab == Backbone.Path.HighExternalStoneWall ? 0.3f : 0.5f;
                int stacks = Mathf.CeilToInt((maxHeight - minHeight) / 6f) + Options.ArenaWalls.Stacks;
                float next = 360 / Options.ArenaWalls.Radius - gap;
                float j = Options.ArenaWalls.Stacks * 6f + 6f;
                float groundHeight;
                BaseEntity e;

                for (int i = 0; i < stacks; i++)
                {
                    foreach (var position in GetCircumferencePositions(center, Options.ArenaWalls.Radius, next, false, center.y))
                    {
                        if (Location.y - position.y > 48f)
                        {
                            continue;
                        }

                        groundHeight = TerrainMeta.HeightMap.GetHeight(new Vector3(position.x, position.y + 6f, position.z));

                        if (groundHeight > position.y + 9f)
                        {
                            continue;
                        }

                        if (Options.ArenaWalls.LeastAmount)
                        {
                            float h = TerrainMeta.HeightMap.GetHeight(position);

                            if (position.y - groundHeight > j && position.y < h)
                            {
                                continue;
                            }
                        }

                        e = GameManager.server.CreateEntity(prefab, position, default(Quaternion), false);

                        if (e == null)
                        {
                            return;
                        }

                        e.OwnerID = 0;
                        e.transform.LookAt(center, Vector3.up);

                        if (Options.ArenaWalls.UseUFOWalls)
                        {
                            e.transform.Rotate(-67.5f, 0f, 0f);
                        }

                        e.enableSaving = false;
                        e.Spawn();
                        e.gameObject.SetActive(true);

                        if (CanSetupEntity(e))
                        {
                            Entities.Add(e);
                            Backbone.Plugin.RaidEntities[e] = this;
                        }

                        if (stacks == i - 1)
                        {
                            RaycastHit hit;
                            if (Physics.Raycast(new Vector3(position.x, position.y + 6f, position.z), Vector3.down, out hit, 12f, Layers.Mask.World) && hit.collider.name.Contains("rock_"))
                            {
                                stacks++;
                            }
                        }
                    }

                    center.y += 6f;
                }
            }

            private void KillTrees()
            {
                BaseEntity e;
                int hits = Physics.OverlapSphereNonAlloc(Location, Constants.RADIUS, Vis.colBuffer, Layers.Mask.Tree, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < hits; i++)
                {
                    e = Vis.colBuffer[i].ToBaseEntity();

                    if (e != null && !e.IsDestroyed)
                    {
                        e.Kill();
                    }

                    Vis.colBuffer[i] = null;
                }
            }

            private IEnumerator EntitySetup()
            {
                SetupCollider();

                var list = new List<BaseEntity>(Entities);
                var _instruction = ConVar.FPS.limit > 80 ? CoroutineEx.waitForSeconds(Constants.INSTRUCTION_TIME) : null;

                foreach (var e in list)
                {
                    if (!CanSetupEntity(e))
                    {
                        yield return _instruction;
                        continue;
                    }

                    Backbone.Plugin.RaidEntities[e] = this;

                    if (e.net.ID < NetworkID)
                    {
                        NetworkID = e.net.ID;
                    }

                    e.OwnerID = 0;

                    if (!Options.AllowPickup && e is BaseCombatEntity)
                    {
                        SetupPickup(e as BaseCombatEntity);
                    }

                    if (e is IOEntity)
                    {
                        if (e is ContainerIOEntity)
                        {
                            SetupIO(e as ContainerIOEntity);
                        }

                        if (e is AutoTurret)
                        {
                            SetupTurret(e as AutoTurret);
                        }
                        else if (e is Igniter)
                        {
                            SetupIgniter(e as Igniter);
                        }
                        else if (e is SamSite)
                        {
                            SetupSamSite(e as SamSite);
                        }
                        else if (e is TeslaCoil)
                        {
                            SetupTeslaCoil(e as TeslaCoil);
                        }
                        else if (e is SearchLight)
                        {
                            SetupSearchLight(e as SearchLight);
                        }
                        else if (e is CustomDoorManipulator)
                        {
                            doorControllers.Add(e as CustomDoorManipulator);
                        }
                        else if (e is HBHFSensor)
                        {
                            SetupHBHFSensor(e as HBHFSensor);
                        }
                    }
                    else if (e is StorageContainer)
                    {
                        SetupContainer(e as StorageContainer);

                        if (e is BaseOven)
                        {
                            ovens.Add(e as BaseOven);
                        }
                        else if (e is FogMachine)
                        {
                            SetupFogMachine(e as FogMachine);
                        }
                        else if (e is FlameTurret)
                        {
                            SetupFlameTurret(e as FlameTurret);
                        }
                        else if (e is VendingMachine)
                        {
                            SetupVendingMachine(e as VendingMachine);
                        }
                        else if (e is BuildingPrivlidge)
                        {
                            SetupBuildingPriviledge(e as BuildingPrivlidge);
                        }
                        else if (_config.Settings.Management.Lockers && e is Locker)
                        {
                            lockers.Add(e as Locker);
                        }
                        else if (e is GunTrap)
                        {
                            SetupGunTrap(e as GunTrap);
                        }
                    }
                    else if (e is BuildingBlock)
                    {
                        SetupBuildingBlock(e as BuildingBlock);
                    }
                    else if (e is Door)
                    {
                        doors.Add(e as Door);
                    }
                    else if (e is BaseLock)
                    {
                        SetupLock(e);
                    }
                    else if (e is SleepingBag)
                    {
                        SetupSleepingBag(e as SleepingBag);
                    }
                    else if (e is BaseMountable)
                    {
                        SetupMountable(e as BaseMountable);
                    }

                    if (e is DecayEntity)
                    {
                        SetupDecayEntity(e as DecayEntity);
                    }

                    SetupSkin(e);

                    yield return _instruction;
                }

                yield return CoroutineEx.waitForSeconds(2f);

                SetupCollider();
                SetupLoot();
                Subscribe();
                CreateGenericMarker();
                UpdateMarker();
                EjectSleepers();
                KillTrees();
                CreateZoneWalls();
                CreateSpheres();
                SetupLights();
                SetupDoorControllers();
                SetupDoors();
                CheckDespawn();
                SetupContainers();
                MakeAnnouncements();
                InvokeRepeating(Protector, 1f, 1f);

                setupRoutine = null;
                Backbone.Plugin.ResetLoadingType(Type);
                Interface.CallHook("OnRaidableBaseStarted", Location, 0, LoadingTimes.ContainsKey(PastedLocation) ? Time.time - LoadingTimes[PastedLocation] : 0f);
                LoadingTimes.Remove(PastedLocation);
            }

            private void SetupLights()
            {
                if (Backbone.Plugin.NightLantern == null)
                {
                    if (_config.Settings.Management.Lights)
                    {
                        InvokeRepeating(Lights, 1f, 1f);
                    }
                    else if (_config.Settings.Management.AlwaysLights)
                    {
                        Lights();
                    }
                }
            }

            private void SetupCollider()
            {
                transform.position = Location = GetCenterFromMultiplePoints();

                var collider = gameObject.GetComponent<SphereCollider>() ?? gameObject.AddComponent<SphereCollider>();
                collider.radius = Options.ProtectionRadius;
                collider.isTrigger = true;
                collider.center = Vector3.zero;
                gameObject.layer = (int)Layer.Trigger;
            }

            private void PopulateLoot(bool unique) // rewrite this.
            {
                if (unique)
                {
                    if (!_config.Treasure.UniqueBaseLoot && BaseLoot.Count > 0)
                    {
                        AddToLoot(BaseLoot);
                    }

                    if (!_config.Treasure.UniqueDifficultyLoot && DifficultyLoot.Count > 0)
                    {
                        AddToLoot(DifficultyLoot);
                    }

                    if (!_config.Treasure.UniqueDefaultLoot && DefaultLoot.Count > 0)
                    {
                        AddToLoot(DefaultLoot);
                    }
                }
                else
                {
                    if (BaseLoot.Count > 0)
                    {
                        AddToLoot(BaseLoot);
                    }

                    if (DifficultyLoot.Count > 0)
                    {
                        AddToLoot(DifficultyLoot);
                    }

                    if (DefaultLoot.Count > 0)
                    {
                        AddToLoot(DefaultLoot);
                    }
                }
            }

            private void SetupLoot()
            {
                _containers.RemoveAll(x => !IsValid(x));

                if (_containers.Count == 0)
                {
                    Puts(Backbone.GetMessage("NoContainersFound", null, BaseName, PositionToGrid(Location)));
                    return;
                }

                CheckExpansionSettings();

                int treasureAmount = Options.MinTreasure > 0 ? UnityEngine.Random.Range(Options.MinTreasure, Options.MaxTreasure) : Options.MaxTreasure;

                if (Options.SkipTreasureLoot || treasureAmount <= 0)
                {
                    return;
                }

                var containers = Pool.GetList<StorageContainer>();

                foreach (var container in _containers)
                {
                    if (IsBox(container, false))
                    {
                        containers.Add(container);
                    }
                }

                if (Options.IgnoreContainedLoot)
                {
                    containers.RemoveAll(x => !x.inventory.IsEmpty());
                    lockers.RemoveAll(x => !x.inventory.IsEmpty());
                }

                if (containers.Count == 0)
                {
                    Pool.FreeList(ref containers);
                    Puts(Backbone.GetMessage("NoBoxesFound", null, BaseName, PositionToGrid(Location)));
                    return;
                }

                var baseLoot = new List<TreasureItem>();

                foreach (var profile in Buildings.Profiles)
                {
                    if (profile.Key == BaseName || profile.Value.Options.AdditionalBases.ContainsKey(BaseName))
                    {
                        baseLoot = new List<TreasureItem>(profile.Value.BaseLootList);
                        break;
                    }
                }

                TakeLootFrom(baseLoot, BaseLoot);

                BaseLootPermanent = BaseLoot.ToList();

                if (BaseLoot.Count < treasureAmount && Buildings.Normal.Count > 0)
                {
                    TakeLootFrom(new List<TreasureItem>(Buildings.Normal), DifficultyLoot);
                }

                if (BaseLoot.Count + DifficultyLoot.Count < treasureAmount)
                {
                    TakeLootFrom(TreasureLoot, DefaultLoot);
                }

                PopulateLoot(true);

                if (Options.AllowDuplicates)
                {
                    if (Loot.Count > 0 && Loot.Count < treasureAmount)
                    {
                        do
                        {
                            Loot.Add(Loot.GetRandom());
                        } while (Loot.Count < treasureAmount);
                    }
                }

                PopulateLoot(false);

                if (Loot.Count == 0)
                {
                    Pool.FreeList(ref containers);
                    Puts(Backbone.GetMessage("NoConfiguredLoot"));
                    return;
                }

                if (!Options.AllowDuplicates)
                {
                    var newLoot = new List<TreasureItem>();
                    var shortnames = new List<string>();

                    foreach (var ti in Loot)
                    {
                        if (ti.modified || !shortnames.Contains(ti.shortname) || IsPriority(ti))
                        {
                            newLoot.Add(ti);
                            shortnames.Add(ti.shortname);
                        }
                    }

                    Loot = newLoot;
                }

                Shuffle(Loot);

                if (Loot.Count > treasureAmount)
                {
                    int index = Loot.Count;

                    while (Loot.Count > treasureAmount && --index >= 0)
                    {
                        if (IsPriority(Loot[index]))
                        {
                            continue;
                        }

                        Loot.RemoveAt(index);
                    }
                }

                if (Options.DivideLoot)
                {
                    DivideLoot(containers);
                }
                else
                {
                    SpawnLoot(containers, treasureAmount);
                }

                if (itemAmountSpawned == 0)
                {
                    Puts(Backbone.GetMessage("NoLootSpawned"));
                }

                Pool.FreeList(ref containers);
            }

            private void SetupContainers()
            {
                foreach (var container in _containers)
                {
                    container.inventory.onItemAddedRemoved += new Action<Item, bool>(OnItemAddedRemoved);
                    if (container.prefabID != Constants.LARGE_WOODEN_BOX) continue;
                    container.SendNetworkUpdate();
                }
            }

            private bool IsPriority(TreasureItem a)
            {
                if (Options.Prioritize)
                {
                    foreach (var b in BaseLootPermanent)
                    {
                        if (a.shortname == b.shortname)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            private void SetupPickup(BaseCombatEntity e)
            {
                e.pickup.enabled = false;
            }

            private void AddContainer(StorageContainer container)
            {
                if (!Entities.Contains(container))
                {
                    Entities.Add(container);
                }

                if (!_allcontainers.Contains(container))
                {
                    _allcontainers.Add(container);
                }

                if (!_containers.Contains(container) && (IsBox(container, false) || container is BuildingPrivlidge))
                {
                    _containers.Add(container);
                }
            }

            private void SetupContainer(StorageContainer container)
            {
                if (container.inventory == null)
                {
                    container.inventory = new ItemContainer();
                    container.inventory.ServerInitialize(null, 30);
                    container.inventory.GiveUID();
                    container.inventory.entityOwner = container;
                }

                if (Options.LockBoxes && IsBox(container, false))
                {
                    CreateLock(container);
                }

                if (Options.EmptyAll && Type != RaidableType.None)
                {
                    container.inventory.Clear();
                    ItemManager.DoRemoves();
                }

                AddContainer(container);
                SetupBoxSkin(container);

                if (Type == RaidableType.None && container.inventory.itemList.Count > 0)
                {
                    return;
                }

                container.dropChance = 0f;

                if (container is BuildingPrivlidge)
                {
                    container.dropChance = _config.Settings.Management.AllowCupboardLoot ? 1f : 0f;
                }
                else if (!IsProtectedWeapon(container) && !(container is VendingMachine))
                {
                    container.dropChance = 1f;
                }

                if (IsBox(container, true) || container is BuildingPrivlidge)
                {
                    container.inventory.SetFlag(ItemContainer.Flag.NoItemInput, true);
                }
            }

            private void SetupIO(ContainerIOEntity io)
            {
                io.dropChance = IsProtectedWeapon(io) ? 0f : 1f;
                io.inventory.SetFlag(ItemContainer.Flag.NoItemInput, true);
            }

            private void SetupIO(IOEntity io)
            {
                io.SetFlag(BaseEntity.Flags.Reserved8, true, false, true);
            }

            private void SetupLock(BaseEntity e, bool justCreated = false)
            {
                if (!Entities.Contains(e))
                {
                    Entities.Add(e);
                }

                if (Type == RaidableType.None)
                {
                    return;
                }

                if (e is CodeLock)
                {
                    var codeLock = e as CodeLock;

                    if (_config.Settings.Management.RandomCodes || justCreated)
                    {
                        codeLock.code = UnityEngine.Random.Range(1000, 9999).ToString();
                        codeLock.hasCode = true;
                    }

                    codeLock.OwnerID = 0;
                    codeLock.guestCode = string.Empty;
                    codeLock.hasGuestCode = false;
                    codeLock.guestPlayers.Clear();
                    codeLock.whitelistPlayers.Clear();
                    codeLock.SetFlag(BaseEntity.Flags.Locked, true);
                }
                else if (e is KeyLock)
                {
                    var keyLock = e as KeyLock;

                    if (_config.Settings.Management.RandomCodes)
                    {
                        keyLock.keyCode = UnityEngine.Random.Range(1, 100000);
                    }

                    keyLock.OwnerID = 0;
                    keyLock.firstKeyCreated = true;
                    keyLock.SetFlag(BaseEntity.Flags.Locked, true);
                }
            }

            private void SetupVendingMachine(VendingMachine vm)
            {
                if (_config.Settings.Management.AllowBroadcasting)
                {
                    return;
                }

                vm.SetFlag(BaseEntity.Flags.Reserved4, false, false, true);
                vm.UpdateMapMarker();
            }

            private void SetupSearchLight(SearchLight light)
            {
                if (!_config.Settings.Management.Lights && !_config.Settings.Management.AlwaysLights)
                {
                    return;
                }

                lights.Add(light);

                light.enabled = false;
            }

            private void SetupHBHFSensor(HBHFSensor sensor)
            {
                triggers[sensor.myTrigger] = sensor;
            }

            private void SetupBuildingBlock(BuildingBlock block)
            {
                if (!IsValid(block))
                {
                    return;
                }

                if (Options.Tiers.Any())
                {
                    ChangeTier(block);
                }

                block.StopBeingDemolishable();
                block.StopBeingRotatable();

                if (block.prefabID == 3234260181 || block.prefabID == 72949757) // triangle and square foundations
                {
                    foundations.Add(block.transform.position);
                    blockProperties.Add(new BlockProperties { obb = new OBB(block.transform, block.bounds), position = new Vector3(block.transform.position.x, block.transform.position.y + 0.1f, block.transform.position.z), prefabID = block.prefabID });
                }
                else if (block.prefabID == 2925153068 || block.prefabID == 916411076) // triangle and square floor
                {
                    blockProperties.Add(new BlockProperties { obb = new OBB(block.transform, block.bounds), position = new Vector3(block.transform.position.x, block.transform.position.y + 0.1f, block.transform.position.z), prefabID = block.prefabID });
                }
            }

            private void ChangeTier(BuildingBlock block)
            {
                if (Options.Tiers.HQM && block.grade != BuildingGrade.Enum.TopTier)
                {
                    SetGrade(block, BuildingGrade.Enum.TopTier);
                }
                else if (Options.Tiers.Metal && block.grade != BuildingGrade.Enum.Metal)
                {
                    SetGrade(block, BuildingGrade.Enum.Metal);
                }
                else if (Options.Tiers.Stone && block.grade != BuildingGrade.Enum.Stone)
                {
                    SetGrade(block, BuildingGrade.Enum.Stone);
                }
                else if (Options.Tiers.Wooden && block.grade != BuildingGrade.Enum.Wood)
                {
                    SetGrade(block, BuildingGrade.Enum.Wood);
                }
            }

            private void SetGrade(BuildingBlock block, BuildingGrade.Enum grade)
            {
                block.SetGrade(grade);
                block.SetHealthToMax();
                block.SendNetworkUpdate();
                block.UpdateSkin();
            }

            private void SetupTeslaCoil(TeslaCoil tc)
            {
                if (!_config.Weapons.TeslaCoil.RequiresPower)
                {
                    tc.UpdateFromInput(25, 0);
                    tc.SetFlag(IOEntity.Flag_HasPower, true, false, true);
                }

                tc.maxDischargeSelfDamageSeconds = Mathf.Clamp(_config.Weapons.TeslaCoil.MaxDischargeSelfDamageSeconds, 0f, 9999f);
                tc.maxDamageOutput = Mathf.Clamp(_config.Weapons.TeslaCoil.MaxDamageOutput, 0f, 9999f);
            }

            private void SetupIgniter(Igniter igniter)
            {
                igniter.SelfDamagePerIgnite = 0f;
            }

            private void SetupTurret(AutoTurret turret)
            {
                SetupIO(turret as IOEntity);

                if (Type != RaidableType.None)
                {
                    turret.authorizedPlayers.Clear();
                }

                triggers[turret.targetTrigger] = turret;
                turret.InitializeHealth(Options.AutoTurret.Health, Options.AutoTurret.Health);
                turret.sightRange = Options.AutoTurret.SightRange;
                turret.aimCone = Options.AutoTurret.AimCone;
                turrets.Add(turret);

                if (Options.AutoTurret.RemoveWeapon)
                {
                    turret.AttachedWeapon = null;
                    Item slot = turret.inventory.GetSlot(0);

                    if (slot != null && (slot.info.category == ItemCategory.Weapon || slot.info.category == ItemCategory.Fun))
                    {
                        slot.RemoveFromContainer();
                        slot.Remove();
                    }
                }

                if (Options.AutoTurret.Shortnames.Count > 0)
                {
                    if (turret.AttachedWeapon == null)
                    {
                        var shortname = Options.AutoTurret.Shortnames.GetRandom();
                        var itemToCreate = ItemManager.FindItemDefinition(shortname);

                        if (itemToCreate != null)
                        {
                            Item item = ItemManager.Create(itemToCreate, 1, (ulong)itemToCreate.skins.GetRandom().id);

                            if (!item.MoveToContainer(turret.inventory, 0, false))
                            {
                                item.Remove();
                            }
                        }
                    }
                }

                turret.Invoke(turret.UpdateAttachedWeapon, 0.1f);
                turret.Invoke(() => FillAmmoTurret(turret), 0.2f);

                if (Options.AutoTurret.Hostile)
                {
                    turret.SetPeacekeepermode(false);
                }

                if (!Options.AutoTurret.RequiresPower)
                {
                    turret.Invoke(turret.InitiateStartup, 0.3f);
                }

                if (_config.Weapons.InfiniteAmmo.AutoTurret)
                {
                    turret.inventory.onPreItemRemove += new Action<Item>(OnWeaponItemPreRemove);
                }
            }

            private void SetupGunTrap(GunTrap gt)
            {
                if (_config.Weapons.Ammo.GunTrap > 0)
                {
                    FillAmmoGunTrap(gt);
                }

                if (_config.Weapons.InfiniteAmmo.GunTrap)
                {
                    gt.inventory.onPreItemRemove += new Action<Item>(OnWeaponItemPreRemove);
                }

                triggers[gt.trigger] = gt;
            }

            private void SetupFogMachine(FogMachine fm)
            {
                if (_config.Weapons.Ammo.FogMachine > 0)
                {
                    FillAmmoFogMachine(fm);
                }

                if (_config.Weapons.InfiniteAmmo.FogMachine)
                {
                    fm.fuelPerSec = 0f;
                }

                if (_config.Weapons.FogMotion)
                {
                    fm.SetFlag(BaseEntity.Flags.Reserved7, true, false, true);
                }

                if (!_config.Weapons.FogRequiresPower)
                {
                    fm.CancelInvoke(fm.CheckTrigger);
                    fm.SetFlag(BaseEntity.Flags.Reserved6, true, false, true);
                    fm.SetFlag(BaseEntity.Flags.Reserved8, true, false, true);
                    fm.SetFlag(BaseEntity.Flags.On, true, false, true);
                }
            }

            private void SetupFlameTurret(FlameTurret ft)
            {
                triggers[ft.trigger] = ft;
                ft.InitializeHealth(Options.FlameTurretHealth, Options.FlameTurretHealth);

                if (_config.Weapons.Ammo.FlameTurret > 0)
                {
                    FillAmmoFlameTurret(ft);
                }

                if (_config.Weapons.InfiniteAmmo.FlameTurret)
                {
                    ft.fuelPerSec = 0f;
                }
            }

            private void SetupSamSite(SamSite ss)
            {
                if (_config.Weapons.SamSiteRepair > 0f)
                {
                    ss.staticRespawn = true;
                    ss.InvokeRepeating(ss.SelfHeal, _config.Weapons.SamSiteRepair * 60f, _config.Weapons.SamSiteRepair * 60f);
                }

                SetupIO(ss as IOEntity);

                if (_config.Weapons.SamSiteRange > 0f)
                {
                    ss.scanRadius = _config.Weapons.SamSiteRange;
                }

                if (_config.Weapons.Ammo.SamSite > 0)
                {
                    FillAmmoSamSite(ss);
                }

                if (_config.Weapons.InfiniteAmmo.SamSite)
                {
                    ss.inventory.onPreItemRemove += new Action<Item>(OnWeaponItemPreRemove);
                }
            }

            private void SetupDoor(Door door)
            {
                if (Options.DoorLock)
                {
                    CreateLock(door);
                }

                if (!Options.CloseOpenDoors)
                {
                    return;
                }

                door.SetOpen(false, true);
            }

            private void SetupDoors()
            {
                doors.RemoveAll(x => x == null || x.IsDestroyed);

                foreach (var door in doors)
                {
                    SetupDoor(door);
                }

                doors.Clear();
            }

            private void SetupDoorControllers()
            {
                doorControllers.RemoveAll(x => x == null || x.IsDestroyed);

                foreach (var cdm in doorControllers)
                {
                    SetupIO(cdm);

                    if (cdm.IsPaired())
                    {
                        doors.Remove(cdm.targetDoor);
                        continue;
                    }

                    var door = cdm.FindDoor(true);

                    if (door.IsValid())
                    {
                        cdm.SetTargetDoor(door);
                        doors.Remove(door);

                        if (Options.DoorLock)
                        {
                            CreateLock(door);
                        }
                    }
                }

                doorControllers.Clear();
            }

            private void CreateLock(BaseEntity entity)
            {
                if (Type == RaidableType.None || entity == null || entity.IsDestroyed)
                {
                    return;
                }

                var slot = entity.GetSlot(BaseEntity.Slot.Lock) as BaseLock;

                if (slot == null)
                {
                    CreateCodeLock(entity);
                    return;
                }

                var keyLock = slot.GetComponent<KeyLock>();

                if (keyLock.IsValid() && !keyLock.IsDestroyed)
                {
                    keyLock.SetParent(null);
                    keyLock.Kill();
                }

                CreateCodeLock(entity);
            }

            private void CreateCodeLock(BaseEntity entity)
            {
                var codeLock = GameManager.server.CreateEntity(Backbone.Path.CodeLock) as CodeLock;

                if (codeLock == null)
                {
                    return;
                }

                codeLock.gameObject.Identity();
                codeLock.SetParent(entity, entity.GetSlotAnchorName(BaseEntity.Slot.Lock));
                codeLock.Spawn();
                entity.SetSlot(BaseEntity.Slot.Lock, codeLock);

                SetupLock(codeLock, true);
            }

            private void SetupBuildingPriviledge(BuildingPrivlidge priv)
            {
                if (Type != RaidableType.None)
                {
                    priv.authorizedPlayers.Clear();
                    priv.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                }

                if (Options.LockPrivilege)
                {
                    CreateLock(priv);
                }

                this.priv = priv;
                privSpawned = true;
            }

            private void SetupSleepingBag(SleepingBag bag)
            {
                if (Type == RaidableType.None)
                {
                    return;
                }

                bag.deployerUserID = 0uL;
            }

            private void SetupMountable(BaseMountable mountable)
            {
                if (mountable is BaseVehicle)
                {
                    return;
                }

                mountables.Add(mountable);
            }

            private void SetupDecayEntity(DecayEntity decayEntity)
            {
                if (BuildingID == 0)
                {
                    BuildingID = BuildingManager.server.NewBuildingID();
                }

                decayEntity.AttachToBuilding(BuildingID);
                decayEntity.decay = null;
            }

            private void SetupBoxSkin(StorageContainer container)
            {
                if (!IsBox(container, false))
                {
                    return;
                }

                ItemDefinition def;
                if (!shortnameDefs.TryGetValue(container.gameObject.name, out def))
                {
                    return;
                }

                var skinInfo = RaidableBase.GetItemSkins(def);

                if (skinInfo.allSkins.Count == 0)
                {
                    return;
                }

                if (!skinIds.ContainsKey(container.prefabID))
                {
                    if (_config.Skins.Boxes.PresetSkin == 0uL || !skinInfo.allSkins.Contains(_config.Skins.Boxes.PresetSkin))
                    {
                        if (_config.Skins.Boxes.RandomWorkshopSkins)
                        {
                            skinIds[container.prefabID] = skinInfo.allSkins.GetRandom();
                        }
                        else if (_config.Skins.Boxes.RandomSkins)
                        {
                            skinIds[container.prefabID] = skinInfo.skins.GetRandom();
                        }
                        else skinIds[container.prefabID] = container.skinID;
                    }
                    else skinIds[container.prefabID] = _config.Skins.Boxes.PresetSkin;
                }

                if (_config.Skins.Boxes.PresetSkin != 0uL || Options.SetSkins)
                {
                    container.skinID = skinIds[container.prefabID];
                }
                else if (_config.Skins.Boxes.RandomWorkshopSkins)
                {
                    container.skinID = skinInfo.allSkins.GetRandom();
                }
                else if (_config.Skins.Boxes.RandomSkins)
                {
                    container.skinID = skinInfo.skins.GetRandom();
                }
            }

            private void SetupSkin(BaseEntity entity, bool workshop)
            {
                ItemDefinition def;
                if (!shortnameDefs.TryGetValue(entity.gameObject.name, out def))
                {
                    return;
                }

                var skinInfo = GetItemSkins(def);

                if (workshop && skinInfo.allSkins.Count > 0)
                {
                    entity.skinID = skinInfo.allSkins.GetRandom();
                    entity.SendNetworkUpdate();
                }
                else if (skinInfo.skins.Count > 0)
                {
                    entity.skinID = skinInfo.skins.GetRandom();
                    entity.SendNetworkUpdate();
                }
            }

            private void SetupSkin(BaseEntity entity)
            {
                if (!_config.Skins.Deployables.RandomSkins || IsBox(entity, false))
                {
                    return;
                }

                if (_config.Skins.IgnoreSkinned && entity.skinID != 0uL)
                {
                    return;
                }

                if (_config.Skins.Deployables.Everything)
                {
                    SetupSkin(entity, _config.Skins.Deployables.RandomWorkshopSkins);
                    return;
                }

                foreach (string value in _config.Skins.Deployables.Names)
                {
                    if (entity.name.Contains(value))
                    {
                        SetupSkin(entity, _config.Skins.Deployables.RandomWorkshopSkins);
                        return;
                    }
                }
            }

            private void Subscribe()
            {
                if (IsUnloading)
                {
                    return;
                }

                if (Options.EnforceDurability)
                {
                    Subscribe(nameof(OnLoseCondition));
                }

                Subscribe(nameof(CanPickupEntity));

                if (Options.NPC.SpawnAmount < 1)
                {
                    Options.NPC.Enabled = false;
                }

                if (Options.NPC.Enabled)
                {
                    Options.NPC.SpawnAmount = Mathf.Clamp(Options.NPC.SpawnAmount, 0, 25);
                    Options.NPC.SpawnMinAmount = Mathf.Clamp(Options.NPC.SpawnMinAmount, 1, Options.NPC.SpawnAmount);
                    Options.NPC.ScientistHealth = Mathf.Clamp(Options.NPC.ScientistHealth, 100, 5000);
                    Options.NPC.MurdererHealth = Mathf.Clamp(Options.NPC.MurdererHealth, 100, 5000);

                    Subscribe(nameof(OnNpcTarget));
                    Subscribe(nameof(OnEntityEnter));
                    Subscribe(nameof(OnNpcDestinationSet));
                    Subscribe(nameof(OnNpcKits));
                    SetupNpcKits();
                    Invoke(SpawnNpcs, 1f);
                }

                Subscribe(nameof(OnPlayerDropActiveItem));
                Subscribe(nameof(OnPlayerDeath));
                Subscribe(nameof(OnEntityDeath));
                Subscribe(nameof(OnEntityKill));
                Subscribe(nameof(CanBGrade));

                if (!_config.Settings.Management.AllowTeleport)
                {
                    Subscribe(nameof(CanTeleport));
                    Subscribe(nameof(canTeleport));
                }

                if (_config.Settings.Management.BlockRestorePVP && AllowPVP || _config.Settings.Management.BlockRestorePVE && !AllowPVP)
                {
                    Subscribe(nameof(OnRestoreUponDeath));
                }

                if (Options.DropTimeAfterLooting > 0 || _config.UI.Containers)
                {
                    Subscribe(nameof(OnLootEntityEnd));
                }

                if (!_config.Settings.Management.BackpacksOpenPVP || !_config.Settings.Management.BackpacksOpenPVE)
                {
                    Subscribe(nameof(CanOpenBackpack));
                }

                if (_config.Settings.Management.PreventFireFromSpreading)
                {
                    Subscribe(nameof(OnFireBallSpread));
                }

                if (Backbone.Plugin.IsPVE())
                {
                    Subscribe(nameof(CanEntityBeTargeted));
                    Subscribe(nameof(CanEntityTrapTrigger));
                }
                else
                {
                    Subscribe(nameof(OnTrapTrigger));
                    //Subscribe(nameof(CanBeTargeted));
                }

                Subscribe(nameof(CanBePenalized));
                Subscribe(nameof(CanBuild));
                Subscribe(nameof(OnEntityGroundMissing));
                Subscribe(nameof(OnLootEntity));
                Subscribe(nameof(OnEntityBuilt));
                Subscribe(nameof(OnCupboardAuthorize));
                Subscribe(nameof(OnEntityMounted));
            }

            private void Subscribe(string hook) => Backbone.Plugin.Subscribe(hook);

            private void MakeAnnouncements()
            {
                if (Type == RaidableType.None)
                {
                    itemAmountSpawned = 0;

                    foreach (var x in _allcontainers)
                    {
                        if (x == null || x.IsDestroyed)
                        {
                            continue;
                        }

                        itemAmountSpawned += x.inventory.itemList.Count;
                    }
                }

                var posStr = FormatGridReference(Location);

                Puts("{0} @ {1} : {2} items", BaseName, posStr, itemAmountSpawned);

                if (Options.Silent)
                {
                    return;
                }

                foreach (var target in BasePlayer.activePlayerList)
                {
                    float distance = Mathf.Floor((target.transform.position - Location).magnitude);
                    string flag = Backbone.GetMessage(AllowPVP ? "PVPFlag" : "PVEFlag", target.UserIDString).Replace("[", string.Empty).Replace("] ", string.Empty);
                    string api = Backbone.GetMessage("RaidOpenMessage", target.UserIDString, DifficultyMode, posStr, distance, flag);
                    if (Type == RaidableType.None) api = api.Replace(DifficultyMode, NoMode);
                    string message = owner.IsValid() ? string.Format("{0}[Owner: {1}]", api, owner.displayName) : api;

                    if (_config.EventMessages.Opened)
                    {
                        target.SendConsoleCommand("chat.add", 2, _config.Settings.ChatID, message);
                    }

                    if (_config.GUIAnnouncement.Enabled && Backbone.Plugin.GUIAnnouncements != null && Backbone.Plugin.GUIAnnouncements.IsLoaded && distance <= _config.GUIAnnouncement.Distance)
                    {
                        Backbone.Plugin.GUIAnnouncements?.Call("CreateAnnouncement", message, _config.GUIAnnouncement.TintColor, _config.GUIAnnouncement.TextColor, target);
                    }
                }
            }

            private float _lastInvokeUpdate = Time.time;

            private void UpdateStatus(BasePlayer player)
            {
                if (IsOpened)
                {
                    lastActive[player.UserIDString] = Time.realtimeSinceStartup;
                }

                if (ownerId == player.userID && Time.time - _lastInvokeUpdate > 1f)
                {
                    _lastInvokeUpdate = Time.time;
                    TryInvokeResetOwner();
                }
            }

            public void TryInvokeResetOwner()
            {
                if (_config.Settings.Management.LockTime > 0f)
                {
                    if (IsInvoking(ResetOwner)) CancelInvoke(ResetOwner);
                    Invoke(ResetOwner, _config.Settings.Management.LockTime * 60f);
                }
            }

            public void ResetOwner()
            {
                if (!IsOpened || IsPlayerActive(ownerId))
                {
                    TryInvokeResetOwner();
                    return;
                }

                owner = null;
                ownerId = 0;
                friends.Clear();
                UpdateMarker();
            }

            private void Puts(string format, params object[] args)
            {
                Backbone.Plugin.Puts(format, args);
            }

            private bool HasSpace(StorageContainer container, int amount)
            {
                return container.inventory.itemList.Count + amount < container.inventory.capacity;
            }

            private void SpawnLoot(List<StorageContainer> containers, int treasureAmount)
            {
                StorageContainer container = null;

                foreach (var x in containers)
                {
                    if (HasSpace(x, treasureAmount))
                    {
                        container = x;
                        break;
                    }
                }

                if (container == null)
                {
                    container = containers.GetRandom();
                    container.inventory.Clear();
                    ItemManager.DoRemoves();
                }

                SpawnLoot(container, treasureAmount, Loot);
            }

            private void SpawnLoot(StorageContainer container, int total, List<TreasureItem> source)
            {
                if (total > container.inventory.capacity)
                {
                    total = container.inventory.capacity;
                }

                for (int j = 0; j < total; j++)
                {
                    if (Loot.Count == 0 || source.Count == 0)
                    {
                        break;
                    }

                    var lootItem = source.GetRandom();

                    Loot.Remove(lootItem);

                    SpawnItem(lootItem, container);
                }
            }

            private void DivideLoot(List<StorageContainer> containers)
            {
                int index = 0;

                while (Loot.Count > 0 && containers.Count > 0)
                {
                    var container = containers[index];

                    if (!container.inventory.IsFull())
                    {
                        var lootItem = Loot.GetRandom();
                        var result = SpawnItem(lootItem, container);

                        if (result == SpawnResult.Transfer || result == SpawnResult.Failure)
                        {
                            index--;
                        }

                        Loot.Remove(lootItem);
                    }
                    else containers.Remove(container);

                    if (++index >= containers.Count)
                    {
                        index = 0;
                    }
                }
            }

            private void AddToLoot(List<TreasureItem> source)
            {
                foreach (var ti in source)
                {
                    bool isBlueprint = ti.shortname.EndsWith(".bp");
                    string shortname = isBlueprint ? ti.shortname.Replace(".bp", string.Empty) : ti.shortname;
                    bool isModified = false;

                    if (!shortnames.Contains(shortname) && shortname.Contains("_"))
                    {
                        shortname = shortname.Substring(shortname.IndexOf("_") + 1);
                        isModified = true;
                    }

                    ti.definition = ItemManager.FindItemDefinition(shortname);

                    if (ti.definition == null)
                    {
                        Puts("Invalid shortname in config: {0}", ti.shortname);
                        continue;
                    }

                    ti.isBlueprint = isBlueprint;

                    int amount = ti.amount;

                    if (ti.amountMin < ti.amount)
                    {
                        amount = Core.Random.Range(ti.amountMin, ti.amount + 1); // get a random number min (inclusive) and max (exclusive)
                    }

                    if (amount <= 0)
                    {
                        continue; // skip item and pick another since the min amount was 0 while the max amount was greater than 0 and random number equaled 0
                    }

                    if (_config.Treasure.UseStackSizeLimit)
                    {
                        var stacks = GetStacks(amount, ti.definition.stackable);
                        isModified = amount > ti.definition.stackable;

                        foreach (int stack in stacks)
                        {
                            Loot.Add(new TreasureItem
                            {
                                amount = stack,
                                definition = ti.definition,
                                shortname = shortname,
                                skin = ti.skin,
                                modified = isModified,
                                isBlueprint = isBlueprint
                            });
                        }
                    }
                    else
                    {
                        Loot.Add(new TreasureItem
                        {
                            amount = amount,
                            definition = ti.definition,
                            shortname = shortname,
                            skin = ti.skin,
                            isBlueprint = isBlueprint,
                            modified = isModified
                        });
                    }
                }

                source.Clear();
            }

            private List<int> GetStacks(int amount, int maxStack)
            {
                var list = new List<int>();

                while (amount > maxStack)
                {
                    amount -= maxStack;
                    list.Add(maxStack);
                }

                list.Add(amount);

                return list;
            }

            private void TakeLootFrom(List<TreasureItem> source, List<TreasureItem> to)
            {
                if (source.Count == 0)
                {
                    return;
                }

                var from = new List<TreasureItem>(source);

                from.RemoveAll(ti => ti == null || ti.amount <= 0 || ti.amountMin < 0);

                if (from.Count == 0)
                {
                    return;
                }

                Shuffle(from);

                /*if (Options.Prioritize)
                {
                    int difference = Math.Abs(Options.TreasureAmount - Loot.Count);
                    int amount = Math.Min(difference, from.Count);
                    to.AddRange(from.Take(amount));
                }
                else*/
                to.AddRange(from);

                if (Options.Multiplier == 1f)
                {
                    return;
                }

                var m = Mathf.Clamp(Options.Multiplier, 0f, 999f);

                foreach (var ti in to)
                {
                    if (ti.amount == 1)
                    {
                        continue;
                    }

                    ti.amount = Mathf.CeilToInt(ti.amount * m);

                    if (ti.amountMin == 0)
                    {
                        continue;
                    }

                    ti.amountMin = Mathf.CeilToInt(ti.amountMin * m);
                }
            }

            private List<TreasureItem> Loot { get; set; } = new List<TreasureItem>();
            private List<TreasureItem> BaseLoot { get; set; } = new List<TreasureItem>();
            private List<TreasureItem> BaseLootPermanent { get; set; } = new List<TreasureItem>();
            private List<TreasureItem> DifficultyLoot { get; set; } = new List<TreasureItem>();
            private List<TreasureItem> DefaultLoot { get; set; } = new List<TreasureItem>();

            private SpawnResult SpawnItem(TreasureItem lootItem, StorageContainer container)
            {
                int amount = lootItem.amount;

                if (amount <= 0)
                {
                    return SpawnResult.Skipped;
                }

                ItemDefinition def = lootItem.definition;
                ulong skin = 0;

                if (_config.Skins.Loot.RandomSkins && def.shortname != "explosive.satchel" && def.shortname != "grenade.f1")
                {
                    if (!skins.TryGetValue(def.shortname, out skin)) // apply same skin once randomly chosen so items with skins can stack properly
                    {
                        skin = lootItem.skin;
                    }

                    if (skin == 0)
                    {
                        var skinInfo = GetItemSkins(def);

                        if (_config.Skins.Loot.RandomWorkshopSkins && skinInfo.allSkins.Count > 0)
                        {
                            skin = skinInfo.allSkins.GetRandom();
                        }
                        else if (skinInfo.skins.Count > 0)
                        {
                            skin = skinInfo.skins.GetRandom();
                        }

                        if (skin != 0)
                        {
                            skins[def.shortname] = skin;
                        }
                    }
                }

                Item item;

                if (lootItem.isBlueprint)
                {
                    item = ItemManager.CreateByItemID(-996920608, 1, 0);

                    if (item == null)
                    {
                        Puts("-996920608 invalid blueprint ID. Contact author.");
                        return SpawnResult.Skipped;
                    }

                    item.blueprintTarget = def.itemid;
                    item.amount = amount;
                }
                else item = ItemManager.Create(def, amount, skin);

                var e = item.GetHeldEntity();

                if (e.IsValid())
                {
                    e.skinID = skin;
                    e.SendNetworkUpdate();
                }

                if (MoveToCupboard(item) || MoveToBBQ(item) || MoveToOven(item) || MoveToFridge(item) || MoveToLocker(item))
                {
                    itemAmountSpawned++;
                    return SpawnResult.Transfer;
                }
                else if (item.MoveToContainer(container.inventory, -1, false))
                {
                    itemAmountSpawned++;
                    return SpawnResult.Success;
                }

                item.Remove();
                return SpawnResult.Failure;
            }

            private bool MoveToFridge(Item item)
            {
                if (!_config.Settings.Management.Food || _allcontainers.Count == 0 || item.info.category != ItemCategory.Food)
                {
                    return false;
                }

                if (_allcontainers.Count > 1)
                {
                    Shuffle(_allcontainers);
                }

                foreach (var x in _allcontainers)
                {
                    if (x == null || x.IsDestroyed)
                    {
                        continue;
                    }

                    if (x.prefabID == 1844023509 && item.MoveToContainer(x.inventory, -1, true))
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool MoveToBBQ(Item item)
            {
                if (!_config.Settings.Management.Food || ovens.Count == 0 || item.info.category != ItemCategory.Food || !IsCookable(item.info))
                {
                    return false;
                }

                if (ovens.Count > 1)
                {
                    Shuffle(ovens);
                }

                foreach (var oven in ovens)
                {
                    if (oven == null || oven.IsDestroyed)
                    {
                        continue;
                    }

                    if (Backbone.Plugin.BBQs.Contains(oven.prefabID) && item.MoveToContainer(oven.inventory, -1, true))
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool MoveToCupboard(Item item)
            {
                if (!_config.Settings.Management.Cupboard || !privSpawned || item.info.category != ItemCategory.Resources || _config.Treasure.ExcludeFromCupboard.Contains(item.info.shortname))
                {
                    return false;
                }

                if (_config.Settings.Management.Cook && item.info.shortname.EndsWith(".ore") && MoveToOven(item))
                {
                    return true;
                }

                if (priv.IsValid() && !priv.IsDestroyed)
                {
                    return item.MoveToContainer(priv.inventory, -1, true);
                }

                return false;
            }

            private bool IsCookable(ItemDefinition def)
            {
                if (def.shortname.EndsWith(".cooked") || def.shortname.EndsWith(".burned") || def.shortname.EndsWith(".spoiled") || def.shortname == "lowgradefuel")
                {
                    return false;
                }

                return def.GetComponent<ItemModCookable>() || def.shortname == "wood";
            }

            private bool MoveToOven(Item item)
            {
                if (!_config.Settings.Management.Cook || ovens.Count == 0 || !IsCookable(item.info))
                {
                    return false;
                }

                if (ovens.Count > 1)
                {
                    Shuffle(ovens);
                }

                foreach (var oven in ovens)
                {
                    if (oven == null || oven.IsDestroyed || Backbone.Plugin.BBQs.Contains(oven.prefabID))
                    {
                        continue;
                    }

                    if (item.info.shortname.EndsWith(".ore") && !Backbone.Plugin.Furnaces.Contains(oven.prefabID))
                    {
                        continue;
                    }

                    if (item.info.shortname == "lowgradefuel" && !Backbone.Plugin.Lanterns.Contains(oven.prefabID))
                    {
                        continue;
                    }

                    if (item.info.shortname == "crude.oil" && !Backbone.Plugin.Refineries.Contains(oven.prefabID))
                    {
                        continue;
                    }

                    if (item.MoveToContainer(oven.inventory, -1, true))
                    {
                        if (!oven.IsOn())
                        {
                            oven.SetFlag(BaseEntity.Flags.On, true, false, true);
                        }

                        if (!item.HasFlag(global::Item.Flag.OnFire))
                        {
                            item.SetFlag(global::Item.Flag.OnFire, true);
                            item.MarkDirty();
                        }

                        return true;
                    }
                }

                return false;
            }

            private bool IsHealthy(Item item)
            {
                if (item.info.category == ItemCategory.Food || item.info.category == ItemCategory.Medical)
                {
                    if (item.info.shortname.Contains(".spoiled") || item.info.shortname.Contains(".raw") || item.info.shortname.Contains(".burned"))
                    {
                        return false;
                    }

                    return item.info.GetComponent<ItemModConsumable>() != null;
                }

                return false;
            }

            private bool IsRangedWeapon(Item item)
            {
                return item.info.category == ItemCategory.Weapon && item.info.GetComponent<ItemModProjectile>() != null;
            }

            private bool MoveToLocker(Item item)
            {
                if (!_config.Settings.Management.Lockers || lockers.Count == 0)
                {
                    return false;
                }

                if (lockers.Count > 0)
                {
                    Shuffle(lockers);
                }

                foreach (var locker in lockers)
                {
                    if (Backbone.Plugin.Helms.Contains(item.info.shortname))
                    {
                        if (MoveToContainer(locker.inventory, item, 0, 13, 26))
                        {
                            return true;
                        }
                    }
                    else if (Backbone.Plugin.Boots.Contains(item.info.shortname))
                    {
                        if (MoveToContainer(locker.inventory, item, 1, 14, 27))
                        {
                            return true;
                        }
                    }
                    else if (Backbone.Plugin.Gloves.Contains(item.info.shortname))
                    {
                        if (MoveToContainer(locker.inventory, item, 2, 15, 28))
                        {
                            return true;
                        }
                    }
                    else if (Backbone.Plugin.Vests.Contains(item.info.shortname))
                    {
                        if (MoveToContainer(locker.inventory, item, 3, 16, 29))
                        {
                            return true;
                        }
                    }
                    else if (Backbone.Plugin.Legs.Contains(item.info.shortname))
                    {
                        if (MoveToContainer(locker.inventory, item, 4, 17, 30))
                        {
                            return true;
                        }
                    }
                    else if (Backbone.Plugin.Shirts.Contains(item.info.shortname))
                    {
                        if (MoveToContainer(locker.inventory, item, 5, 18, 31))
                        {
                            return true;
                        }
                    }
                    else if (Backbone.Plugin.Other.Contains(item.info.shortname))
                    {
                        if (MoveToContainer(locker.inventory, item, 6, 19, 32))
                        {
                            return true;
                        }
                    }
                    else if (IsRangedWeapon(item))
                    {
                        if (MoveToContainer(locker.inventory, item, 7, 8, 20, 21, 33, 34))
                        {
                            return true;
                        }
                    }
                    else if (item.info.category == ItemCategory.Ammunition)
                    {
                        if (MoveToContainer(locker.inventory, item, 9, 10, 22, 23, 35, 36))
                        {
                            return true;
                        }
                    }
                    else if (IsHealthy(item))
                    {
                        if (MoveToContainer(locker.inventory, item, 11, 12, 24, 25, 37, 38))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            private bool MoveToContainer(ItemContainer container, Item item, params int[] positions)
            {
                foreach (int position in positions)
                {
                    if (item.MoveToContainer(container, position, false))
                    {
                        return true;
                    }
                }

                return false;
            }

            private void CheckExpansionSettings()
            {
                if (!_config.Settings.ExpansionMode || Backbone.Plugin.DangerousTreasures == null)
                {
                    return;
                }

                var boxes = Pool.GetList<StorageContainer>();

                foreach (var x in _containers)
                {
                    if (x.prefabID == 2206646561)
                    {
                        boxes.Add(x);
                    }
                }

                if (boxes.Count > 0)
                {
                    Backbone.Plugin.DangerousTreasures?.Call("API_SetContainer", boxes.GetRandom(), Radius, !Options.NPC.Enabled || Options.NPC.UseExpansionNpcs);
                }

                Pool.FreeList(ref boxes);
            }

            private bool ToggleNpcMinerHat(NPCPlayerApex npc, bool state)
            {
                if (npc == null || npc.inventory == null || npc.IsDead())
                {
                    return false;
                }

                var slot = npc.inventory.FindItemID("hat.miner");

                if (slot == null)
                {
                    return false;
                }

                if (state && slot.contents != null)
                {
                    slot.contents.AddItem(ItemManager.FindItemDefinition("lowgradefuel"), 50);
                }

                slot.SwitchOnOff(state);
                npc.inventory.ServerUpdate(0f);
                return true;
            }

            private void Lights()
            {
                if (lights.Count == 0 && ovens.Count == 0 && npcs.Count == 0)
                {
                    CancelInvoke(Lights);
                    return;
                }

                if (_config.Settings.Management.AlwaysLights || (!lightsOn && !IsDayTime()))
                {
                    lights.RemoveAll(e => e == null || e.IsDestroyed);
                    ovens.RemoveAll(e => e == null || e.IsDestroyed);

                    var list = new List<BaseEntity>(lights);

                    list.AddRange(ovens);

                    foreach (var e in list)
                    {
                        if (e == null || e.IsDestroyed) continue;
                        if (!e.IsOn())
                        {
                            if (e.prefabID == 2931042549)
                            {
                                if ((e as BaseOven).inventory.IsEmpty())
                                {
                                    continue;
                                }
                            }

                            e.SetFlag(BaseEntity.Flags.On, true, false, true);
                        }
                    }

                    foreach (var npc in npcs)
                    {
                        if (npc == null || npc.IsDestroyed) continue;
                        ToggleNpcMinerHat(npc, true);
                    }

                    lightsOn = true;
                }
                else if (lightsOn && IsDayTime())
                {
                    lights.RemoveAll(e => e == null || e.IsDestroyed);
                    ovens.RemoveAll(e => e == null || e.IsDestroyed);

                    var list = new List<BaseEntity>(lights);

                    list.AddRange(ovens);

                    foreach (var e in list)
                    {
                        if (e.prefabID == 2931042549 || e.prefabID == 4160694184 || e.prefabID == 1374462671 || e.prefabID == 2162666837 || Backbone.Plugin.BBQs.Contains(e.prefabID))
                        {
                            continue;
                        }

                        if (e.IsOn())
                        {
                            e.SetFlag(BaseEntity.Flags.On, false);
                        }
                    }

                    foreach (var npc in npcs)
                    {
                        ToggleNpcMinerHat(npc, false);
                    }

                    lightsOn = false;
                }
            }

            public bool IsDayTime() => TOD_Sky.Instance?.Cycle.DateTime.Hour >= 8 && TOD_Sky.Instance?.Cycle.DateTime.Hour < 20;

            public void Undo()
            {
                if (IsOpened)
                {
                    float time = _config.Settings.Management.DespawnMinutes > 0 ? _config.Settings.Management.DespawnMinutes * 60f : 0f;

                    IsDespawning = true;
                    IsOpened = false;
                    CancelInvoke(ResetOwner);

                    if (time > 0f)
                    {
                        despawnTime = Time.realtimeSinceStartup + time;

                        if (_config.EventMessages.ShowWarning)
                        {
                            var grid = FormatGridReference(Location);

                            foreach (var target in BasePlayer.activePlayerList)
                            {
                                var message = Backbone.GetMessage("DestroyingBaseAt", target.UserIDString, grid, _config.Settings.Management.DespawnMinutes);
                                target.SendConsoleCommand("chat.add", 2, _config.Settings.ChatID, message);
                            }
                        }

                        var go = gameObject;

                        Backbone.Timer(time, () => Destroy(go));
                    }
                    else Destroy(gameObject);
                }
            }

            public bool Any(ulong targetId, bool checkFriends = true)
            {
                if (ownerId == targetId)
                {
                    return true;
                }

                foreach (var x in raiders)
                {
                    if (x.Key == targetId)
                    {
                        return true;
                    }
                }

                if (checkFriends)
                {
                    foreach (var x in friends)
                    {
                        if (x?.userID == targetId)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            public static bool IsOwner(BasePlayer player)
            {
                foreach (var raid in Backbone.Plugin.Raids.Values)
                {
                    if (raid.ownerId.IsSteamId() && raid.ownerId == player.userID && raid.IsOpened)
                    {
                        return true;
                    }
                }

                return false;
            }

            public static bool Has(ulong userID)
            {
                return Backbone.Plugin.Npcs.ContainsKey(userID);
            }

            public static bool Has(TriggerBase triggerBase)
            {
                foreach (var raid in Backbone.Plugin.Raids.Values)
                {
                    if (raid.triggers.ContainsKey(triggerBase))
                    {
                        return true;
                    }
                }

                return false;
            }

            public static bool Has(BaseEntity entity)
            {
                return Backbone.Plugin.RaidEntities.ContainsKey(entity);
            }

            public static BaseEntity Get(TriggerBase triggerBase)
            {
                foreach (var raid in Backbone.Plugin.Raids.Values)
                {
                    if (raid.triggers.ContainsKey(triggerBase))
                    {
                        return raid.triggers[triggerBase];
                    }
                }

                return null;
            }


            public static int Get(RaidableType type)
            {
                int amount = 0;

                foreach (var value in Locations.Values)
                {
                    if (value == type)
                    {
                        amount++;
                    }
                }

                return amount;
            }

            public static int Get(RaidableMode mode)
            {
                return Backbone.Plugin.Raids.Values.Count;
            }

            public static RaidableBase Get(ulong userID)
            {
                RaidableBase raid;
                if (Backbone.Plugin.Npcs.TryGetValue(userID, out raid))
                {
                    return raid;
                }

                return null;
            }

            public static RaidableBase Get(Vector3 target, float f = 0f)
            {
                foreach (var raid in Backbone.Plugin.Raids.Values)
                {
                    if (InRange(raid.Location, target, raid.Options.ProtectionRadius + f))
                    {
                        return raid;
                    }
                }

                return null;
            }

            public static RaidableBase Get(int baseIndex)
            {
                if (Backbone.Plugin.Indices.ContainsKey(baseIndex))
                {
                    return Backbone.Plugin.Indices[baseIndex];
                }

                return null;
            }

            public static RaidableBase Get(BasePlayer victim)
            {
                if (victim.IsNpc)
                {
                    return Get(victim.userID);
                }

                DelaySettings ds;
                if (Backbone.Plugin.PvpDelay.TryGetValue(victim.userID, out ds))
                {
                    return ds.RaidableBase;
                }

                return Get(victim.transform.position);
            }

            public static RaidableBase Get(PlayerCorpse corpse)
            {
                if (!corpse.playerSteamID.IsSteamId())
                {
                    return Get(corpse.playerSteamID);
                }

                DelaySettings ds;
                if (Backbone.Plugin.PvpDelay.TryGetValue(corpse.playerSteamID, out ds))
                {
                    return ds.RaidableBase;
                }

                return Get(corpse.transform.position);
            }

            public static RaidableBase Get(BaseEntity entity)
            {
                if (Backbone.Plugin.RaidEntities.ContainsKey(entity))
                {
                    return Backbone.Plugin.RaidEntities[entity];
                }

                return null;
            }

            public static RaidableBase Get(List<BaseEntity> entities)
            {
                foreach (var raid in Backbone.Plugin.Raids.Values)
                {
                    foreach (var e in entities)
                    {
                        if (InRange(raid.PastedLocation, e.transform.position, Radius))
                        {
                            return raid;
                        }
                    }
                }

                return null;
            }

            public static bool IsTooClose(Vector3 target, float radius)
            {
                foreach (var position in Locations.Keys)
                {
                    if (InRange(position, target, radius))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool IsBlacklistedSkin(ItemDefinition def, int num)
            {
                var skinId = ItemDefinition.FindSkin(def.isRedirectOf?.itemid ?? def.itemid, num);
                var dirSkin = def.isRedirectOf == null ? def.skins.FirstOrDefault(x => (ulong)x.id == skinId) : def.isRedirectOf.skins.FirstOrDefault(x => (ulong)x.id == skinId);
                var itemSkin = (dirSkin.id == 0) ? null : (dirSkin.invItem as ItemSkin);

                if (itemSkin?.Redirect != null || def.isRedirectOf != null)
                {
                    return true;
                }

                return false;
            }

            public static SkinInfo GetItemSkins(ItemDefinition def)
            {
                SkinInfo skinInfo;
                if (!Backbone.Plugin.Skins.TryGetValue(def.shortname, out skinInfo))
                {
                    Backbone.Plugin.Skins[def.shortname] = skinInfo = new SkinInfo();

                    foreach (var skin in def.skins)
                    {
                        if (IsBlacklistedSkin(def, skin.id))
                        {
                            continue;
                        }

                        var id = Convert.ToUInt64(skin.id);

                        skinInfo.skins.Add(id);
                        skinInfo.allSkins.Add(id);
                    }

                    if (def.skins2 == null)
                    {
                        return skinInfo;
                    }

                    foreach (var skin in def.skins2)
                    {
                        if (IsBlacklistedSkin(def, (int)skin.WorkshopId))
                        {
                            continue;
                        }

                        if (!skinInfo.allSkins.Contains(skin.WorkshopId))
                        {
                            skinInfo.allSkins.Add(skin.WorkshopId);
                        }
                    }
                }

                return skinInfo;
            }

            private void AuthorizePlayer(NPCPlayerApex npc)
            {
                turrets.RemoveAll(x => !x.IsValid() || x.IsDestroyed);

                foreach (var turret in turrets)
                {
                    turret.authorizedPlayers.Add(new ProtoBuf.PlayerNameID
                    {
                        userid = npc.userID,
                        username = npc.displayName
                    });

                    turret.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                }

                if (priv == null || priv.IsDestroyed)
                {
                    return;
                }

                priv.authorizedPlayers.Add(new ProtoBuf.PlayerNameID
                {
                    userid = npc.userID,
                    username = npc.displayName
                });

                priv.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
            }

            public bool IsInSameClan(string playerId, string targetId)
            {
                if (Backbone.Plugin.Clans == null)
                {
                    return false;
                }

                return Convert.ToBoolean(Backbone.Plugin.Clans?.Call("IsMemberOrAlly", playerId, targetId));
            }

            public bool AreFriends(string playerId, string targetId)
            {
                if (Backbone.Plugin.Friends == null)
                {
                    return false;
                }

                return Convert.ToBoolean(Backbone.Plugin.Friends?.Call("AreFriends", playerId, targetId));
            }

            public bool IsOnSameTeam(ulong playerId, ulong targetId)
            {
                RelationshipManager.PlayerTeam team1;
                if (!RelationshipManager.Instance.playerToTeam.TryGetValue(playerId, out team1))
                {
                    return false;
                }

                RelationshipManager.PlayerTeam team2;
                if (!RelationshipManager.Instance.playerToTeam.TryGetValue(targetId, out team2))
                {
                    return false;
                }

                return team1.teamID == team2.teamID;
            }

            private bool IsOnSameTeam(BasePlayer player, ulong targetId)
            {
                if (player.currentTeam == 0uL)
                {
                    return false;
                }

                return player.Team.members.Contains(targetId);
            }

            public bool IsAlly(ulong playerId, ulong targetId)
            {
                return playerId == targetId || IsOnSameTeam(playerId, targetId) || IsInSameClan(playerId.ToString(), targetId.ToString()) || AreFriends(playerId.ToString(), targetId.ToString());
            }

            public bool IsAlly(BasePlayer player)
            {
                if (!player.IsValid())
                {
                    return false;
                }

                if (!ownerId.IsSteamId() || CanBypass(player) || player.userID == ownerId || friends.Contains(player))
                {
                    return true;
                }

                if (IsOnSameTeam(player.userID, ownerId) || IsInSameClan(player.UserIDString, ownerId.ToString()) || AreFriends(player.UserIDString, ownerId.ToString()))
                {
                    friends.Add(player);
                    return true;
                }

                return false;
            }

            public static void StopUsingWand(BasePlayer player)
            {
                if (!_config.Settings.NoWizardry || Backbone.Plugin.Wizardry == null || !Backbone.Plugin.Wizardry.IsLoaded)
                {
                    return;
                }

                if (player.svActiveItemID == 0)
                {
                    return;
                }

                Item item = player.GetActiveItem();

                if (item?.info.shortname != "knife.bone")
                {
                    return;
                }

                if (!item.MoveToContainer(player.inventory.containerMain))
                {
                    item.DropAndTossUpwards(player.GetDropPosition() + player.transform.forward, 2f);
                    Backbone.Message(player, "TooPowerfulDrop");
                }
                else Backbone.Message(player, "TooPowerful");
            }

            private int targetLayer { get; set; } = ~(Layers.Mask.Invisible | Layers.Mask.Trigger | Layers.Mask.Prevent_Movement | Layers.Mask.Prevent_Building); // credits ZoneManager

            public Vector3 GetEjectLocation(Vector3 a, float distance)
            {
                var position = ((a.XZ3D() - Location.XZ3D()).normalized * (Options.ProtectionRadius + distance)) + Location; // credits ZoneManager
                float y = TerrainMeta.HighestPoint.y + 250f;

                RaycastHit hit;
                if (Physics.Raycast(position + new Vector3(0f, y, 0f), Vector3.down, out hit, Mathf.Infinity, targetLayer, QueryTriggerInteraction.Ignore))
                {
                    position.y = hit.point.y + 0.75f;
                }
                else position.y = Mathf.Max(TerrainMeta.HeightMap.GetHeight(position), TerrainMeta.WaterMap.GetHeight(position)) + 0.75f;

                return position;
            }

            public BackpackData AddCorpse(DroppedItemContainer backpack, BasePlayer player)
            {
                BackpackData data;
                if (!corpses.TryGetValue(backpack.net.ID, out data))
                {
                    corpses[backpack.net.ID] = data = new BackpackData
                    {
                        backpack = backpack,
                        player = player,
                        userID = backpack.playerSteamID
                    };
                }

                return data;
            }

            public bool EjectCorpse(uint key, BackpackData data)
            {
                if (!IsValid(data.backpack))
                {
                    return true;
                }

                if (!ownerId.IsSteamId() || Any(data.userID) || IsAlly(data.player))
                {
                    return false;
                }

                var position = GetEjectLocation(data.backpack.transform.position, 5f);
                float w = TerrainMeta.WaterMap.GetHeight(position);

                if (position.y < w)
                {
                    position.y = w;
                }

                data.backpack.transform.position = position;
                data.backpack.TransformChanged();

                var player = data.player;

                if (player.IsValid() && player.IsConnected)
                {
                    if (_config.Settings.Management.DrawTime <= 0)
                    {
                        Backbone.Message(player, "YourCorpse");
                        return true;
                    }

                    bool isAdmin = player.IsAdmin;
                    string message = Backbone.GetMessage("YourCorpse", player.UserIDString);

                    try
                    {
                        if (!isAdmin)
                        {
                            player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                            player.SendNetworkUpdateImmediate();
                        }

                        player.SendConsoleCommand("ddraw.text", _config.Settings.Management.DrawTime, Color.red, data.backpack.transform.position, message);
                    }
                    catch (Exception ex)
                    {
                        Puts(ex.Message);
                    }
                    finally
                    {
                        if (!isAdmin)
                        {
                            player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                            player.SendNetworkUpdateImmediate();
                        }
                    }
                }

                return true;
            }

            private void EjectSleepers()
            {
                if (!_config.Settings.Management.EjectSleepers || Type == RaidableType.None)
                {
                    return;
                }

                var players = Pool.GetList<BasePlayer>();
                Vis.Entities(Location, Options.ProtectionRadius, players, Layers.Mask.Player_Server, QueryTriggerInteraction.Ignore);

                foreach (var player in players)
                {
                    if (player.IsSleeping() && !player.IsBuildingAuthed())
                    {
                        RemovePlayer(player);
                    }
                }

                Pool.FreeList(ref players);
            }

            public bool RemovePlayer(BasePlayer player)
            {
                if (player.IsNpc || Type == RaidableType.None && !player.IsSleeping())
                {
                    return false;
                }

                if (player.GetMounted())
                {
                    return RemoveMountable(player.GetMounted());
                }

                var position = GetEjectLocation(player.transform.position, 10f);

                if (player.IsFlying)
                {
                    position.y = player.transform.position.y;
                }

                player.EnsureDismounted();
                player.Teleport(position);
                player.SendNetworkUpdateImmediate();
                return true;
            }

            private bool CanEject(List<BasePlayer> players)
            {
                foreach (var player in players)
                {
                    if (CanEject(player))
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool CanEject(BasePlayer target)
            {
                if (target == null || target == owner)
                {
                    return false;
                }

                if (IsBanned(target) || IsHogging(target))
                {
                    return true;
                }
                else if (CanEject() && !IsAlly(target))
                {
                    if (CanMessage(target))
                    {
                        Backbone.Message(target, "OnPlayerEntryRejected");
                    }

                    return true;
                }
                else if (_config.Settings.Management.EjectSleepers && target.IsSleeping() && Type != RaidableType.None)
                {
                    return true;
                }

                return false;
            }

            public bool CanEject()
            {
                if (AllowPVP && Options.EjectLockedPVP && ownerId.IsSteamId())
                {
                    return true;
                }

                if (!AllowPVP && Options.EjectLockedPVE && ownerId.IsSteamId())
                {
                    return true;
                }

                return false;
            }

            public bool IsControlledMount(BaseMountable m)
            {
                if (!_config.Settings.Management.Mounts.ControlledMounts)
                {
                    return false;
                }

                if (m is BaseChair)
                {
                    m.DismountAllPlayers();

                    return true;
                }

                var parentEntity = m.GetParentEntity();

                if (parentEntity == null || parentEntity is RidableHorse)
                {
                    return false;
                }

                if (parentEntity.GetType().Name.Contains("Controller"))
                {
                    m.DismountAllPlayers();

                    return true;
                }

                return false;
            }

            private bool TryRemoveMountable(BaseMountable m, List<BasePlayer> players)
            {
                if (Type == RaidableType.None || m.GetParentEntity() is BaseTrain || IsControlledMount(m))
                {
                    return false;
                }

                if (CanEject(players))
                {
                    return RemoveMountable(m);
                }

                if (_config.Settings.Management.Mounts.Boats && m is BaseBoat)
                {
                    return RemoveMountable(m);
                }
                else if (_config.Settings.Management.Mounts.BasicCars && m is BasicCar)
                {
                    return RemoveMountable(m);
                }
                else if (_config.Settings.Management.Mounts.ModularCars && m is ModularCar)
                {
                    return RemoveMountable(m);
                }
                else if (_config.Settings.Management.Mounts.CH47 && m is CH47Helicopter)
                {
                    return RemoveMountable(m);
                }
                else if (_config.Settings.Management.Mounts.Horses && m is RidableHorse)
                {
                    return RemoveMountable(m);
                }
                else if (_config.Settings.Management.Mounts.Scrap && m is ScrapTransportHelicopter)
                {
                    return RemoveMountable(m);
                }
                else if (_config.Settings.Management.Mounts.MiniCopters && m is MiniCopter && !(m is ScrapTransportHelicopter))
                {
                    return RemoveMountable(m);
                }
                else if (_config.Settings.Management.Mounts.Pianos && m is StaticInstrument)
                {
                    return RemoveMountable(m);
                }

                return false;
            }

            private bool RemoveMountable(BaseMountable m, bool eject = false)
            {
                if (m == null || m.IsDestroyed || m.transform == null)
                {
                    return false;
                }

                if (eject)
                {
                    return EjectMountable(m);
                }

                var vehicle = m.VehicleParent();

                if (vehicle.IsValid() && !vehicle.IsDestroyed && vehicle.transform != null)
                {
                    var e = vehicle.transform.eulerAngles; // credits k1lly0u

                    vehicle.transform.rotation = Quaternion.Euler(e.x, e.y - 180f, e.z);

                    if (vehicle.rigidBody != null)
                    {
                        vehicle.rigidBody.velocity *= -1f;
                    }

                    return true;
                }

                return EjectMountable(m);
            }

            private bool EjectMountable(BaseMountable m)
            {
                var position = ((m.transform.position.XZ3D() - Location.XZ3D()).normalized * (Options.ProtectionRadius + 10f)) + Location;
                var e = m.transform.eulerAngles;

                position.y = m.transform.position.y;

                m.transform.rotation = Quaternion.Euler(e.x, e.y - 180f, e.z);

                var rigidBody = m.GetComponent<Rigidbody>();

                if (rigidBody != null)
                {
                    rigidBody.velocity *= -1f;
                }

                if (m.mountAnchor != null && m.mountAnchor.transform != null)
                {
                    m.transform.position = m.mountAnchor.transform.position = position;
                    m.mountAnchor.Rotate(m.transform.eulerAngles);
                }
                else m.transform.position = position;

                m.TransformChanged();

                return true;
            }

            public bool CanSetupEntity(BaseEntity e)
            {
                BaseEntity.saveList.Remove(e);

                if (e == null || e.IsDestroyed)
                {
                    if (e != null)
                    {
                        e.enableSaving = false;
                    }

                    Entities.Remove(e);
                    return false;
                }

                if (e.net == null)
                {
                    e.net = Net.sv.CreateNetworkable();
                }

                e.enableSaving = false;
                return true;
            }

            public void TryRespawnNpc()
            {
                if ((!IsOpened && !Options.Levels.Level2) || IsInvoking(RespawnNpcNow))
                {
                    return;
                }

                if (Options.RespawnRateMin > 0)
                {
                    Invoke(RespawnNpcNow, UnityEngine.Random.Range(Options.RespawnRateMin, Options.RespawnRateMax));
                }
                else Invoke(RespawnNpcNow, Options.RespawnRateMax);
            }

            public void RespawnNpcNow()
            {
                if (npcs.Count >= npcMaxAmount)
                {
                    return;
                }

                var npc = SpawnNPC(Options.NPC.SpawnScientistsOnly ? false : Options.NPC.SpawnBoth ? UnityEngine.Random.value > 0.5f : Options.NPC.SpawnMurderers);

                if (npc == null || npcs.Count >= npcMaxAmount)
                {
                    return;
                }

                TryRespawnNpc();
            }

            public void SpawnNpcs()
            {
                if (!Options.NPC.Enabled || (Options.NPC.UseExpansionNpcs && _config.Settings.ExpansionMode && Backbone.Plugin.DangerousTreasures != null && Backbone.Plugin.DangerousTreasures.IsLoaded))
                {
                    return;
                }

                if (npcMaxAmount == 0)
                {
                    npcMaxAmount = Options.NPC.SpawnRandomAmount && Options.NPC.SpawnAmount > 1 ? UnityEngine.Random.Range(Options.NPC.SpawnMinAmount, Options.NPC.SpawnAmount) : Options.NPC.SpawnAmount;
                }

                for (int i = 0; i < npcMaxAmount; i++)
                {
                    if (npcs.Count >= npcMaxAmount)
                    {
                        break;
                    }

                    SpawnNPC(!Options.NPC.SpawnScientistsOnly && (Options.NPC.SpawnBoth ? UnityEngine.Random.value >= 0.5f : Options.NPC.SpawnMurderers));
                }

                Invoke(TryStartPlayingWithFire, 5f);
            }

            private bool NearFoundation(Vector3 position)
            {
                foreach (var a in foundations)
                {
                    if (InRange(a, position, 3f))
                    {
                        return true;
                    }
                }

                return false;
            }

            private NavMeshHit _navHit;

            private Vector3 FindPointOnNavmesh(Vector3 target, float radius)
            {
                int tries = 0;

                while (++tries < 100)
                {
                    if (NavMesh.SamplePosition(target, out _navHit, radius, 1))
                    {
                        if (NearFoundation(_navHit.position))
                        {
                            continue;
                        }

                        float y = TerrainMeta.HeightMap.GetHeight(_navHit.position);

                        if (TestInsideRock(_navHit.position) || _navHit.position.y < y)
                        {
                            continue;
                        }

                        if (TerrainMeta.WaterMap.GetHeight(_navHit.position) - y > 1f)
                        {
                            continue;
                        }

                        if ((_navHit.position - Location).magnitude > Mathf.Max(radius * 2f, Options.ProtectionRadius) - 2.5f)
                        {
                            continue;
                        }

                        return _navHit.position;
                    }
                }

                return Vector3.zero;
            }

            private RaycastHit _hit;

            private bool TestInsideRock(Vector3 position)
            {
                bool flag = false;

                Physics.queriesHitBackfaces = true;

                if (Physics.Raycast(position, Vector3.up, out _hit, 20f, Layers.Mask.World, QueryTriggerInteraction.Ignore) && _hit.collider.name.Contains("rock_"))
                {
                    var hits = Physics.RaycastAll(position + new Vector3(0f, 0.1f, 0f), Vector3.down, 50f, Layers.Mask.World | Layers.Mask.Terrain, QueryTriggerInteraction.Ignore);

                    if (hits != null && hits.Length > 0)
                    {
                        flag = hits.Last().collider.name.Contains("rock_");
                    }
                    else flag = _hit.collider.bounds.extents.Max() > 1f;
                }

                Physics.queriesHitBackfaces = false;

                return flag;
            }

            private static NPCPlayerApex InstantiateEntity(Vector3 position, bool murd)
            {
                var prefabName = murd ? Backbone.Path.Murderer : Backbone.Path.Scientist;
                var prefab = GameManager.server.FindPrefab(prefabName);
                var go = Facepunch.Instantiate.GameObject(prefab, position, default(Quaternion));

                go.name = prefabName;
                SceneManager.MoveGameObjectToScene(go, Rust.Server.EntityScene);

                if (go.GetComponent<Spawnable>())
                {
                    Destroy(go.GetComponent<Spawnable>());
                }

                if (!go.activeSelf)
                {
                    go.SetActive(true);
                }

                return go.GetComponent<NPCPlayerApex>();
            }

            private List<Vector3> RandomWanderPositions
            {
                get
                {
                    var list = new List<Vector3>();
                    float maxRoamRange = Options.ArenaWalls.Enabled ? 20f : Mathf.Max(Radius * 2f, Options.ProtectionRadius);

                    for (int i = 0; i < 10; i++)
                    {
                        var vector = FindPointOnNavmesh(GetRandomPoint(maxRoamRange), 15f);

                        if (vector != Vector3.zero)
                        {
                            list.Add(vector);
                        }
                    }

                    return list;
                }
            }

            private Vector3 GetRandomPoint(float radius)
            {
                var vector = Location + UnityEngine.Random.onUnitSphere * radius;

                vector.y = TerrainMeta.HeightMap.GetHeight(vector);

                return vector;
            }

            private NPCPlayerApex SpawnNPC(bool murd)
            {
                var list = RandomWanderPositions;

                if (list.Count == 0)
                    return null;

                var npc = InstantiateEntity(GetRandomPoint(Mathf.Min(Radius, Options.ProtectionRadius) * 0.85f), murd);

                if (npc == null)
                    return null;

                npc.Spawn();

                SetupNpc(npc, murd);

                if (!murd)
                {
                    (npc as Scientist).LootPanelName = npc.displayName;
                    //AttemptMount(npc);
                }

                npc.Invoke(() => UpdateDestination(npc, list), 0.25f);

                return npc;
            }

            private void SetupNpc(NPCPlayerApex npc, bool murd)
            {
                npcs.Add(npc);

                npc.IsInvinsible = false;
                npc.startHealth = murd ? Options.NPC.MurdererHealth : Options.NPC.ScientistHealth;
                npc.InitializeHealth(npc.startHealth, npc.startHealth);
                npc.CommunicationRadius = 0;
                npc.RadioEffect.guid = null;
                npc.displayName = Options.NPC.RandomNames.Count > 0 ? Options.NPC.RandomNames.GetRandom() : RandomUsernames.Get(npc.userID);
                npc.Stats.AggressionRange = Options.NPC.AggressionRange;
                npc.Stats.DeaggroRange = Options.NPC.AggressionRange * 1.125f;
                npc.Stats.MaxRoamRange = Options.ProtectionRadius - 5f;
                npc.NeverMove = true;

                npc.Invoke(() =>
                {
                    if (npc.IsDestroyed)
                    {
                        return;
                    }

                    EquipNpc(npc, murd);
                }, 1f);

                npc.Invoke(() =>
                {
                    if (npc.IsDestroyed)
                    {
                        return;
                    }

                    Item projectileItem = null;

                    foreach (var item in npc.inventory.containerBelt.itemList)
                    {
                        if (item.GetHeldEntity() is BaseProjectile)
                        {
                            projectileItem = item;
                            break;
                        }
                    }

                    if (projectileItem != null)
                    {
                        npc.UpdateActiveItem(projectileItem.uid);
                    }
                    else npc.EquipWeapon();
                }, 2f);

                if (Options.NPC.DespawnInventory)
                {
                    if (murd)
                    {
                        (npc as NPCMurderer).LootSpawnSlots = new LootContainer.LootSpawnSlot[0];
                    }
                    else (npc as Scientist).LootSpawnSlots = new LootContainer.LootSpawnSlot[0];
                }

                AuthorizePlayer(npc);
                Backbone.Plugin.Npcs[npc.userID] = this;
            }

            /*private bool AttemptMount(NPCPlayerApex npc)
            {
                if (!Options.NPC.Mount)
                {
                    return false;
                }

                mountables.RemoveAll(m => m == null || m.IsDestroyed);

                if (mountables.Count > 1)
                {
                    Shuffle(mountables);
                }

                foreach (var m in mountables)
                {
                    if (m.GetMounted() == null)
                    {
                        m.MountPlayer(npc);

                        if (npc.GetMounted())
                        {
                            npc.NavAgent.enabled = false;
                            npc.SetFact(Facts.IsMounted, 1, true, true);
                            npc.CancelInvoke(npc.RadioChatter);

                            return true;
                        }
                    }
                }

                return false;
            }*/

            private Vector3 FindRandomSpot()
            {
                int layers = Layers.Mask.Construction | Layers.Mask.Deployed | Layers.Mask.Player_Server;

                Shuffle(blockProperties);
                var entities = Pool.GetList<BaseEntity>();

                foreach (var block in blockProperties)
                {
                    int walls = 0;
                    bool blocked = false;
                    var destination = block.position + Vector3.up * 1.5f;
                    entities.Clear();
                    Vis.Entities(destination, 2f, entities, layers, QueryTriggerInteraction.Ignore);

                    foreach (var e in entities)
                    {
                        if (e.prefabID == 3234260181 || e.prefabID == 72949757 || e.prefabID == 2925153068 || e.prefabID == 916411076) // triangle/square foundation/floor
                        {
                            continue;
                        }

                        if (e.prefabID == 916411076 || e.prefabID == 3895720527) // floor / roof
                        {
                            if (e.transform.position.y > block.position.y)
                            {
                                blocked = true;
                                break;
                            }
                        }

                        if (e.prefabID == 2194854973)
                        {
                            walls++;
                        }

                        if (e is NPCPlayerApex)
                        {
                            blocked = true;
                            break;
                        }

                        if (e.PrefabName.Contains("/npc/") || e.PrefabName.Contains("/deployable/") && !e.ShortPrefabName.StartsWith("rug."))
                        {
                            if (e.bounds.size.Max() > 1.2f && block.obb.Contains(e.transform.position))
                            {
                                blocked = true;
                                break;
                            }

                            if (GamePhysics.CheckSphere(e.transform.position, 0.2f, Layers.Server.Deployed, QueryTriggerInteraction.Ignore))
                            {
                                blocked = true;
                                break;
                            }
                        }
                    }

                    if (blocked)
                    {
                        continue;
                    }

                    if ((block.prefabID == 3234260181 || block.prefabID == 2925153068) && walls < 3)
                    {
                        //blockProperties.Remove(block);
                        Pool.FreeList(ref entities);
                        return block.position;
                    }

                    if ((block.prefabID == 72949757 || block.prefabID == 916411076) && walls < 4)
                    {
                        //blockProperties.Remove(block);
                        Pool.FreeList(ref entities);
                        return block.position;
                    }
                }

                Pool.FreeList(ref entities);
                return Vector3.zero;
            }

            public void SpawnRandomly()
            {
                var position = FindRandomSpot();

                if (position == Vector3.zero)
                {
                    return;
                }

                var npc = InstantiateEntity(position, false);

                if (npc == null)
                {
                    return;
                }

                npc.Spawn();
                SetupNpc(npc, false);
                npc.Stats.VisionCone = -1f;
                npc.Invoke(() => UpdateDestination(npc, null), 0.25f);
                Subscribe(nameof(OnNpcResume));
                Subscribe(nameof(OnNpcDestinationSet));
            }

            private void SetupNpcKits()
            {
                var murdererKits = new List<string>();
                var scientistKits = new List<string>();

                foreach (string kit in Options.NPC.MurdererKits)
                {
                    if (IsKit(kit))
                    {
                        murdererKits.Add(kit);
                    }
                }

                foreach (string kit in Options.NPC.ScientistKits)
                {
                    if (IsKit(kit))
                    {
                        scientistKits.Add(kit);
                    }
                }

                npcKits = new Dictionary<string, List<string>>
                {
                    { "murderer", murdererKits },
                    { "scientist", scientistKits }
                };
            }

            private bool IsKit(string kit)
            {
                var success = Backbone.Plugin.Kits?.Call("isKit", kit);

                if (success == null || !(success is bool))
                {
                    return false;
                }

                return (bool)success;
            }

            private void EquipNpc(NPCPlayerApex npc, bool murd)
            {
                List<string> kits;
                if (npcKits.TryGetValue(murd ? "murderer" : "scientist", out kits) && kits.Count > 0)
                {
                    npc.inventory.Strip();

                    object success = Backbone.Plugin.Kits?.Call("GiveKit", npc, kits.GetRandom());

                    if (success is bool && (bool)success)
                    {
                        goto done;
                    }
                }

                var items = murd ? Options.NPC.MurdererItems : Options.NPC.ScientistItems;

                if (items.Count == 0)
                {
                    goto done;
                }

                npc.inventory.Strip();

                foreach (string shortname in items)
                {
                    Item item = ItemManager.CreateByName(shortname, 1, 0);

                    if (item == null)
                    {
                        Backbone.Plugin.PrintError("Invalid shortname in config: {0}", shortname);
                        continue;
                    }

                    if (!item.MoveToContainer(npc.inventory.containerWear, -1, false) && !item.MoveToContainer(npc.inventory.containerBelt, -1, false) && !item.MoveToContainer(npc.inventory.containerMain, -1, true))
                    {
                        item.Remove();
                    }
                }

                done:

                foreach (Item item in npc.inventory.AllItems())
                {
                    if (_config.Skins.Npcs)
                    {
                        var skinInfo = GetItemSkins(item.info);

                        if (item.info.category == ItemCategory.Weapon || item.info.category == ItemCategory.Attire)
                        {
                            item.skin = skinInfo.allSkins.Count > 0 ? skinInfo.allSkins.GetRandom() : skinInfo.skins.Count > 0 ? skinInfo.skins.GetRandom() : 0;
                        }
                    }

                    var e = item.GetHeldEntity();

                    if (e.IsValid())
                    {
                        if (item.skin != 0)
                        {
                            e.skinID = item.skin;
                            e.SendNetworkUpdate();
                        }

                        var weapon = e as BaseProjectile;

                        if (weapon.IsValid())
                        {
                            weapon.primaryMagazine.contents = weapon.primaryMagazine.capacity;
                            weapon.SendNetworkUpdateImmediate();
                        }
                    }

                    item.MarkDirty();
                }

                if (!ToggleNpcMinerHat(npc, !IsDayTime()))
                {
                    npc.inventory.ServerUpdate(0f);
                }
            }

            private void UpdateDestination(NPCPlayerApex npc, List<Vector3> list)
            {
                npc.gameObject.AddComponent<FinalDestination>().Set(npc, list, Options.NPC, Location, Options.ProtectionRadius - 5f);
            }

            public static void UpdateAllMarkers()
            {
                foreach (var raid in Backbone.Plugin.Raids.Values)
                {
                    raid.UpdateMarker();
                }
            }

            public void UpdateMarker()
            {
                if (IsLoading)
                {
                    Invoke(UpdateMarker, 1f);
                    return;
                }

                if (genericMarker != null && !genericMarker.IsDestroyed)
                {
                    genericMarker.SendUpdate();
                }

                if (vendingMarker != null && !vendingMarker.IsDestroyed)
                {
                    vendingMarker.transform.position = Location;
                    float seconds = despawnTime - Time.realtimeSinceStartup;
                    string despawnText = _config.Settings.Management.DespawnMinutesInactive > 0 && seconds > 0 ? Math.Floor(TimeSpan.FromSeconds(seconds).TotalMinutes).ToString() : null;
                    string flag = Backbone.GetMessage(AllowPVP ? "PVPFlag" : "PVEFlag");
                    string markerShopName = markerName == _config.Settings.Markers.MarkerName ? string.Format("{0}{1} {2}", flag, Mode(), markerName) : string.Format("{0} {1}", flag, markerName).TrimStart();
                    vendingMarker.markerShopName = string.IsNullOrEmpty(despawnText) ? markerShopName : string.Format("{0} [{1}m]", markerShopName, despawnText);
                    vendingMarker.SendNetworkUpdate();
                }

                if (markerCreated || !IsMarkerAllowed())
                {
                    return;
                }

                if (_config.Settings.Markers.UseExplosionMarker)
                {
                    explosionMarker = GameManager.server.CreateEntity(Backbone.Path.ExplosionMarker, Location) as MapMarkerExplosion;

                    if (explosionMarker != null)
                    {
                        explosionMarker.Spawn();
                        explosionMarker.SendMessage("SetDuration", 60, SendMessageOptions.DontRequireReceiver);
                    }
                }
                else if (_config.Settings.Markers.UseVendingMarker)
                {
                    vendingMarker = GameManager.server.CreateEntity(Backbone.Path.VendingMarker, Location) as VendingMachineMapMarker;

                    if (vendingMarker != null)
                    {
                        string flag = Backbone.GetMessage(AllowPVP ? "PVPFlag" : "PVEFlag");
                        string markerShopName = markerName == _config.Settings.Markers.MarkerName ? string.Format("{0}{1} {2}", flag, Mode(), markerName) : string.Format("{0}{1}", flag, markerName);

                        vendingMarker.enabled = false;
                        vendingMarker.markerShopName = markerShopName;
                        vendingMarker.Spawn();
                    }
                }

                markerCreated = true;
            }

            private void CreateGenericMarker()
            {
                if (_config.Settings.Markers.UseExplosionMarker || _config.Settings.Markers.UseVendingMarker)
                {
                    if (!IsMarkerAllowed())
                    {
                        return;
                    }

                    genericMarker = GameManager.server.CreateEntity(Backbone.Path.RadiusMarker, Location) as MapMarkerGenericRadius;

                    if (genericMarker != null)
                    {
                        genericMarker.alpha = 0.75f;
                        genericMarker.color1 = GetMarkerColor1();
                        genericMarker.color2 = GetMarkerColor2();
                        genericMarker.radius = Mathf.Min(2.5f, _config.Settings.Markers.Radius);
                        genericMarker.Spawn();
                        genericMarker.SendUpdate();
                    }
                }
            }

            private Color GetMarkerColor1()
            {
                if (Type == RaidableType.None)
                {
                    return Color.clear;
                }

                Color color;

                if (ColorUtility.TryParseHtmlString(_config.Settings.Management.Colors1.Normal, out color))
                {
                    return color;
                }

                return Color.green;
            }

            private Color GetMarkerColor2()
            {
                if (Type == RaidableType.None)
                {
                    return NoneColor;
                }

                Color color;

                if (ColorUtility.TryParseHtmlString(_config.Settings.Management.Colors2.Normal, out color))
                {
                    return color;
                }

                return Color.green;
            }

            private bool IsMarkerAllowed()
            {
                if (Options.Silent)
                {
                    return false;
                }

                switch (Type)
                {
                    case RaidableType.Grid:
                    case RaidableType.Manual:
                    case RaidableType.None:
                        {
                            return _config.Settings.Markers.Manual;
                        }
                    case RaidableType.Maintained:
                        {
                            return _config.Settings.Markers.Maintained;
                        }
                    case RaidableType.Scheduled:
                        {
                            return _config.Settings.Markers.Scheduled;
                        }
                }

                return true;
            }

            private void KillNpc()
            {
                var list = new List<NPCPlayerApex>(npcs);

                foreach (var npc in list)
                {
                    if (npc != null && !npc.IsDestroyed)
                    {
                        if (npc.metabolism == null)
                        {
                            npc.metabolism = npc.GetComponent<PlayerMetabolism>();
                        }

                        npc.Kill();
                    }
                }

                npcs.Clear();
                list.Clear();
            }

            private void RemoveSpheres()
            {
                if (spheres.Count > 0)
                {
                    foreach (var sphere in spheres)
                    {
                        if (sphere != null && !sphere.IsDestroyed)
                        {
                            sphere.Kill();
                        }
                    }

                    spheres.Clear();
                }
            }

            public void RemoveMapMarkers()
            {
                Interface.CallHook("RemoveTemporaryLustyMarker", uid);
                Interface.CallHook("RemoveMapPrivatePluginMarker", uid);

                if (explosionMarker != null && !explosionMarker.IsDestroyed)
                {
                    explosionMarker.CancelInvoke(explosionMarker.DelayedDestroy);
                    explosionMarker.Kill();
                }

                if (genericMarker != null && !genericMarker.IsDestroyed)
                {
                    genericMarker.Kill();
                }

                if (vendingMarker != null && !vendingMarker.IsDestroyed)
                {
                    vendingMarker.Kill();
                }
            }
        }

        #region Hooks

        private void UnsubscribeHooks()
        {
            if (IsUnloading)
            {
                return;
            }

            Unsubscribe(nameof(CanBGrade));
            Unsubscribe(nameof(OnRestoreUponDeath));
            Unsubscribe(nameof(OnNpcKits));
            Unsubscribe(nameof(CanTeleport));
            Unsubscribe(nameof(canTeleport));
            Unsubscribe(nameof(CanEntityBeTargeted));
            Unsubscribe(nameof(CanEntityTrapTrigger));
            Unsubscribe(nameof(CanEntityTakeDamage));
            Unsubscribe(nameof(CanOpenBackpack));
            Unsubscribe(nameof(CanBePenalized));

            Unsubscribe(nameof(OnPlayerCommand));
            Unsubscribe(nameof(OnServerCommand));
            Unsubscribe(nameof(OnTrapTrigger));
            //Unsubscribe(nameof(CanBeTargeted));
            Unsubscribe(nameof(OnEntityMounted));
            Unsubscribe(nameof(OnEntityBuilt));
            Unsubscribe(nameof(OnEntityGroundMissing));
            Unsubscribe(nameof(OnEntityKill));
            Unsubscribe(nameof(OnEntityTakeDamage));
            Unsubscribe(nameof(OnLootEntity));
            Unsubscribe(nameof(OnLootEntityEnd));
            Unsubscribe(nameof(OnEntityDeath));
            Unsubscribe(nameof(CanPickupEntity));
            Unsubscribe(nameof(OnPlayerDeath));
            Unsubscribe(nameof(OnPlayerDropActiveItem));
            Unsubscribe(nameof(OnEntityEnter));
            Unsubscribe(nameof(OnNpcTarget));
            Unsubscribe(nameof(OnNpcResume));
            Unsubscribe(nameof(OnNpcDestinationSet));
            Unsubscribe(nameof(OnEntitySpawned));
            Unsubscribe(nameof(OnCupboardAuthorize));
            Unsubscribe(nameof(OnActiveItemChanged));
            Unsubscribe(nameof(OnLoseCondition));
            Unsubscribe(nameof(OnFireBallSpread));
            Unsubscribe(nameof(CanBuild));
        }

        private void OnMapMarkerAdded(BasePlayer player, ProtoBuf.MapNote note)
        {
            if (player.IsValid() && player.IsConnected && note != null && HasPermission(player, mapPermission))
            {
                player.Teleport(new Vector3(note.worldPosition.x, GetSpawnHeight(note.worldPosition), note.worldPosition.z));
            }
        }

        private void OnNewSave(string filename) => wiped = true;

        private void Init()
        {
            IsUnloading = false;
            permission.CreateGroup(rankLadderGroup, rankLadderGroup, 0);
            permission.GrantGroupPermission(rankLadderGroup, rankLadderPermission, this);
            permission.RegisterPermission(adminPermission, this);
            permission.RegisterPermission(rankLadderPermission, this);
            permission.RegisterPermission(drawPermission, this);
            permission.RegisterPermission(mapPermission, this);
            permission.RegisterPermission(canBypassPermission, this);
            permission.RegisterPermission(bypassBlockPermission, this);
            permission.RegisterPermission(banPermission, this);
            permission.RegisterPermission(vipPermission, this);
            lastSpawnRequestTime = Time.realtimeSinceStartup;
            Backbone = new SingletonBackbone(this);
            Unsubscribe(nameof(OnMapMarkerAdded));
            Unsubscribe(nameof(OnPlayerSleepEnded));
            UnsubscribeHooks();
            maintainEnabled = _config.Settings.Maintained.Enabled;
            scheduleEnabled = _config.Settings.Schedule.Enabled;
        }

        private void OnServerInitialized(bool isStartup)
        {
            if (!configLoaded)
            {
                return;
            }

            timer.Repeat(30f, 0, () => RaidableBase.UpdateAllMarkers());

            LoadData();
            SetupMonuments();
            RegisterCommands();
            RemoveAllThirdPartyMarkers();
            CheckForWipe();
            Initialize();
        }

        private void Unload()
        {
            IsUnloading = true;

            if (!configLoaded)
            {
                UnsetStatics();
                return;
            }

            SaveData();
            RaidableBase.Unload();
            StopScheduleCoroutine();
            StopMaintainCoroutine();
            StopGridCoroutine();
            StopDespawnCoroutine();
            DestroyComponents();
            RemoveAllThirdPartyMarkers();

            if (Raids.Count > 0 || Bases.Count > 0)
            {
                DespawnAllBasesNow(false);
                return;
            }

            UnsetStatics();
        }

        private static void UnsetStatics()
        {
            UI.Players.Clear();
            UI.InvokeTimers.Clear();
            LoadingTimes.Clear();
            shortnameDefs.Clear();
            shortnames.Clear();
            Locations.Clear();
            Backbone.Destroy();
            Backbone = null;
            _config = null;
            Buildings.Default.Clear();
            Buildings.Normal.Clear();
            Buildings.Profiles.Clear();
            Buildings.Weekday.Clear();
        }

        private void RegisterCommands()
        {
            AddCovalenceCommand(_config.Settings.EventCommand, nameof(CommandRaidBase));
            AddCovalenceCommand(_config.Settings.HunterCommand, nameof(CommandRaidHunter));
            AddCovalenceCommand(_config.Settings.ConsoleCommand, nameof(CommandRaidBase));
            AddCovalenceCommand("rb.reloadconfig", nameof(CommandReloadConfig));
            AddCovalenceCommand("rb.config", nameof(CommandConfig), "raidablebases.config");
            AddCovalenceCommand("rb.populate", nameof(CommandPopulate), "raidablebases.config");
            AddCovalenceCommand("rb.toggle", nameof(CommandToggle), "raidablebases.config");
        }

        private void CheckForWipe()
        {
            if (!wiped && storedData.Players.Count >= _config.RankedLadder.Amount && BuildingManager.server.buildingDictionary.Count == 0)
            {
                foreach (var pi in storedData.Players.Values)
                {
                    if (pi.Raids > 0)
                    {
                        wiped = true;
                        break;
                    }
                }
            }

            if (wiped)
            {
                var raids = new List<int>();
                var dict = new Dictionary<string, PlayerInfo>(storedData.Players);

                if (storedData.Players.Count > 0)
                {
                    if (AssignTreasureHunters())
                    {
                        foreach (var entry in dict)
                        {
                            if (entry.Value.Raids > 0)
                            {
                                raids.Add(entry.Value.Raids);
                            }

                            storedData.Players[entry.Key].Raids = 0;
                        }
                    }
                }

                if (raids.Count > 0)
                {
                    var average = raids.Average();

                    foreach (var entry in dict)
                    {
                        if (entry.Value.TotalRaids < average)
                        {
                            storedData.Players.Remove(entry.Key);
                        }
                    }
                }

                wiped = false;
                SaveData();
            }
        }

        private void SetupMonuments()
        {
            foreach (var monument in TerrainMeta.Path?.Monuments?.ToArray() ?? UnityEngine.Object.FindObjectsOfType<MonumentInfo>())
            {
                if (string.IsNullOrEmpty(monument.displayPhrase.translated))
                {
                    float size = monument.name.Contains("power_sub") ? 35f : Mathf.Max(monument.Bounds.size.Max(), 75f);
                    monuments[monument] = monument.name.Contains("cave") ? 75f : monument.name.Contains("OilrigAI") ? 150f : size;
                }
                else
                {
                    monuments[monument] = GetMonumentFloat(monument.displayPhrase.translated.TrimEnd());
                }

                monuments[monument] += _config.Settings.Management.MonumentDistance;
            }
        }

        private void BlockZoneManagerZones()
        {
            if (ZoneManager == null || !ZoneManager.IsLoaded)
            {
                return;
            }

            var zoneIds = ZoneManager?.Call("GetZoneIDs") as string[];

            if (zoneIds == null)
            {
                return;
            }

            managedZones.Clear();

            foreach (string zoneId in zoneIds)
            {
                var zoneLoc = ZoneManager.Call("GetZoneLocation", zoneId);

                if (!(zoneLoc is Vector3))
                {
                    continue;
                }

                var position = (Vector3)zoneLoc;

                if (position == Vector3.zero)
                {
                    continue;
                }

                var zoneName = Convert.ToString(ZoneManager.Call("GetZoneName", zoneId));

                if (_config.Settings.Inclusions.Any(zone => zone == zoneId || !string.IsNullOrEmpty(zoneName) && zoneName.Contains(zone, CompareOptions.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var zoneInfo = new ZoneInfo();
                var radius = ZoneManager.Call("GetZoneRadius", zoneId);

                if (radius is float)
                {
                    zoneInfo.Distance = (float)radius + Constants.RADIUS;
                }

                var size = ZoneManager.Call("GetZoneSize", zoneId);

                if (size is Vector3)
                {
                    zoneInfo.Size = (Vector3)size;
                }

                zoneInfo.Position = position;
                zoneInfo.OBB = new OBB(zoneInfo.Position, zoneInfo.Size, Quaternion.identity);
                managedZones[position] = zoneInfo;
            }

            if (managedZones.Count > 0)
            {
                Puts(Backbone.GetMessage("BlockedZones", null, managedZones.Count));
            }
        }

        private void Reinitialize()
        {
            Backbone.Plugin.Skins.Clear();

            if (_config.Settings.TeleportMarker)
            {
                Subscribe(nameof(OnMapMarkerAdded));
            }

            if (_config.UI.Enabled)
            {
                Subscribe(nameof(OnPlayerSleepEnded));
            }
        }

        private object OnRestoreUponDeath(BasePlayer player)
        {
            var raid = RaidableBase.Get(player.transform.position);

            if (raid == null)
            {
                return null;
            }

            return _config.Settings.Management.BlockRestorePVE && !raid.AllowPVP || _config.Settings.Management.BlockRestorePVP && raid.AllowPVP ? true : (object)null;
        }

        private object OnNpcKits(ulong targetId)
        {
            var raid = RaidableBase.Get(targetId);

            if (raid == null || !raid.Options.NPC.DespawnInventory)
            {
                return null;
            }

            return true;
        }

        private object CanBGrade(BasePlayer player, int playerGrade, BuildingBlock block, Planner planner)
        {

            if (player.IsValid() && (EventTerritory(player.transform.position) || PvpDelay.ContainsKey(player.userID)))
            {
                return 0;
            }

            return null;
        }

        private object canTeleport(BasePlayer player)
        {
            return CanTeleport(player);
        }

        private object CanTeleport(BasePlayer player)
        {
            return !player.IsFlying && (EventTerritory(player.transform.position) || PvpDelay.ContainsKey(player.userID)) ? Backbone.GetMessage("CannotTeleport", player.UserIDString) : null;
        }

        private void OnEntityMounted(BaseMountable m, BasePlayer player)
        {
            if (player.IsNpc || m.GetParentEntity() is BaseTrain || m.OwnerID == 1337420)
            {
                return;
            }

            var raid = RaidableBase.Get(player.transform.position);

            if (raid == null)
            {
                return;
            }

            if (raid.IsControlledMount(m) || raid.intruders.Contains(player) || raid.raiders.ContainsKey(player.userID))
            {
                return;
            }

            m.DismountAllPlayers();
            raid.RemovePlayer(player);
        }

        private BasePlayer GetOwnerPlayer(Item item)
        {
            if (item.parentItem == null)
            {
                return item.GetOwnerPlayer();
            }

            return item.parentItem.GetOwnerPlayer();
        }

        private object OnLoseCondition(Item item, float amount)
        {
            var player = GetOwnerPlayer(item);

            if (!IsValid(player) || player.IsAdmin)
            {
                return null;
            }

            var raid = RaidableBase.Get(player.transform.position);

            if (raid == null || !raid.Options.EnforceDurability)
            {
                return null;
            }

            uint uid = item.uid;
            float condition;
            if (!raid.conditions.TryGetValue(uid, out condition))
            {
                raid.conditions[uid] = condition = item.condition;
            }

            NextTick(() =>
            {
                if (raid == null)
                {
                    return;
                }

                if (!IsValid(item))
                {
                    raid.conditions.Remove(uid);
                    return;
                }

                item.condition = condition - amount;

                if (item.condition <= 0f && item.condition < condition)
                {
                    item.OnBroken();
                    raid.conditions.Remove(uid);
                }
                else raid.conditions[uid] = item.condition;
            });

            return true;
        }

        private void OnEntityBuilt(Planner planner, GameObject go)
        {
            var e = go.ToBaseEntity();

            if (e == null)
            {
                return;
            }

            var raid = RaidableBase.Get(e.transform.position);

            if (raid == null)
            {
                return;
            }

            var player = planner.GetOwnerPlayer();

            if (player == null)
            {
                return;
            }

            if (!raid.intruders.Contains(player))
            {
                if (raid.CanMessage(player))
                {
                    Backbone.Message(player, "TooCloseToABuilding");
                }

                e.Invoke(e.KillMessage, 0.1f);
                return;
            }

            if (!raid.Options.AllowBuildingPriviledges && e is BuildingPrivlidge)
            {
                var item = player.inventory.FindItemID("cupboard.tool");

                if (item != null)
                {
                    item.amount++;
                    item.MarkDirty();
                }
                else player.GiveItem(ItemManager.CreateByName("cupboard.tool"));

                e.Invoke(e.KillMessage, 0.1f);
                return;
            }

            var decayEntity = e as DecayEntity;

            if (decayEntity.IsValid())
            {
                if (e.prefabID == 3234260181 || e.prefabID == 72949757)
                {
                    if (decayEntity.buildingID == raid.BuildingID)
                    {
                        if (raid.CanMessage(player))
                        {
                            Backbone.Message(player, "TooCloseToABuilding");
                        }

                        e.Invoke(e.KillMessage, 0.1f);
                        return;
                    }
                }
            }

            AddEntity(e, raid);
        }

        private void AddEntity(BaseEntity e, RaidableBase raid)
        {
            raid.BuiltList.Add(e.net.ID);

            RaidEntities[e] = raid;

            if (e.name.Contains("assets/prefabs/deployable/"))
            {
                if (_config.Settings.Management.DoNotDestroyDeployables)
                {
                    UnityEngine.Object.Destroy(e.GetComponent<DestroyOnGroundMissing>());
                    UnityEngine.Object.Destroy(e.GetComponent<GroundWatch>());
                }
                else raid.Entities.Add(e);
            }
            else if (!_config.Settings.Management.DoNotDestroyStructures)
            {
                raid.Entities.Add(e);
            }
        }

        private object OnEntityEnter(TriggerBase trigger, BasePlayer player) // TargetTrigger, PlayerDetectionTrigger // Prevent npcs from triggering HBHFSensor, GunTrap, FlameTurret, AutoTurret
        {
            if (RaidableBase.Has(player.userID))
            {
                if (RaidableBase.Has(trigger))
                {
                    return false;
                }

                var raid = RaidableBase.Get(player.userID);

                if (raid.Options.NPC.IgnoreTrapsTurrets)
                {
                    return false;
                }
            }

            return _config.Settings.Management.IgnoreFlying && player.IsFlying ? false : (object)null;
        }

#if USE_HTN_HOOK
        private object OnNpcTarget(NPCPlayerApex npc, BaseEntity entity)
        {
            return entity != null && npc != null && entity.IsNpc && RaidableBase.Has(npc.userID) ? true : (object)null;
        }

        private object OnNpcTarget(BaseEntity entity, NPCPlayerApex npc)
        {
            return entity != null && npc != null && entity.IsNpc && RaidableBase.Has(npc.userID) ? true : (object)null;
        }
#else
        private object OnNpcTarget(NPCPlayerApex npc, NPCPlayerApex npc2)
        {
            return npc != null && RaidableBase.Has(npc.userID) ? true : (object)null;
        }

        private object OnNpcTarget(BaseNpc entity, NPCPlayerApex npc)
        {
            return npc != null && RaidableBase.Has(npc.userID) ? true : (object)null;
        }

        private object OnNpcTarget(NPCPlayerApex npc, BaseNpc entity)
        {
            return npc != null && RaidableBase.Has(npc.userID) ? true : (object)null;
        }
#endif

        private object OnNpcDestinationSet(NPCPlayerApex npc, Vector3 newDestination)
        {
            if (npc == null || !npc.GetNavAgent.isOnNavMesh)
            {
                return true;
            }

            FinalDestination fd;
            if (!Backbone.Destinations.TryGetValue(npc.userID, out fd) || fd.NpcCanRoam(newDestination))
            {
                return null;
            }

            return true;
        }

        private object OnNpcResume(NPCPlayerApex npc)
        {
            if (npc == null)
            {
                return null;
            }

            FinalDestination fd;
            if (!Backbone.Destinations.TryGetValue(npc.userID, out fd) || !fd.stationary)
            {
                return null;
            }

            return true;
        }

        private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (player.IsNpc || !EventTerritory(player.transform.position))
            {
                return;
            }

            RaidableBase.StopUsingWand(player);
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            NextTick(() =>
            {
                if (player == null || player.IsDestroyed || player.IsNpc || player.transform == null)
                {
                    return;
                }

                DelaySettings ds;
                if (PvpDelay.TryGetValue(player.userID, out ds))
                {
                    if (ds.Timer != null && !ds.Timer.Destroyed)
                    {
                        ds.Timer.Callback.Invoke();
                        ds.Timer.Destroy();
                    }

                    PvpDelay.Remove(player.userID);
                }

                var raid = RaidableBase.Get(player.transform.position, 5f); // 1.5.1 sleeping bag exploit fix

                if (raid == null || raid.intruders.Contains(player))
                {
                    return;
                }

                if (InRange(player.transform.position, raid.Location, raid.Options.ProtectionRadius))
                {
                    raid.OnEnterRaid(player);
                }
                else raid.RemovePlayer(player);
            });
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo hitInfo)
        {
            var raid = RaidableBase.Get(player);

            if (raid == null)
            {
                return;
            }

            if (player.IsNpc)
            {
                if (!RaidableBase.Has(player.userID))
                {
                    return;
                }

                if (_config.Settings.Management.UseOwners)
                {
                    var attacker = hitInfo?.Initiator as BasePlayer;

                    if (attacker.IsValid() && !attacker.IsNpc)
                    {
                        raid.TrySetOwner(attacker, player, hitInfo);
                        raid.AddLooter(attacker);
                    }
                }

                if (raid.Options.NPC.DespawnInventory)
                {
                    player.inventory.Strip();
                }

                raid.CheckDespawn();
            }
            else
            {
                if (CanDropPlayerBackpack(player, raid))
                {
                    Backpacks?.Call("API_DropBackpack", player);
                }

                raid.OnPlayerExit(player);
            }
        }

        private object OnPlayerDropActiveItem(BasePlayer player, Item item)
        {
            if (EventTerritory(player.transform.position))
            {
                return true;
            }

            return null;
        }

        private object OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            if (player.IsValid() && EventTerritory(player.transform.position))
            {
                command = command.Replace("/", string.Empty);

                foreach (var value in _config.Settings.BlacklistedCommands)
                {
                    if (command.Equals(value.Replace("/", string.Empty), StringComparison.OrdinalIgnoreCase))
                    {
                        Backbone.Message(player, "CommandNotAllowed");
                        return true;
                    }
                }
            }

            return null;
        }

        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player.IsValid() && EventTerritory(player.transform.position))
            {
                string command = arg.cmd.FullName.Replace("/", string.Empty);

                foreach (var value in _config.Settings.BlacklistedCommands)
                {
                    if (command.Equals(value.Replace("/", string.Empty), StringComparison.OrdinalIgnoreCase))
                    {
                        Backbone.Message(player, "CommandNotAllowed");
                        return true;
                    }
                }
            }

            return null;
        }

        private void OnEntityDeath(BuildingPrivlidge priv, HitInfo hitInfo)
        {
            var raid = RaidableBase.Get(priv);

            if (raid == null)
            {
                return;
            }

            if (hitInfo?.Initiator == null && !raid.IsOpened)
            {
                priv.inventory.Clear();
            }

            if (raid.Options.RequiresCupboardAccess)
            {
                OnCupboardAuthorize(priv, null);
            }

            if (raid.IsOpened && raid.EndWhenCupboardIsDestroyed())
            {
                raid.CancelInvoke(raid.TryToEnd);
                raid.AwardRaiders();
                raid.Undo();
            }
        }

        private void OnEntityKill(StorageContainer container)
        {
            if (container is BuildingPrivlidge)
            {
                OnEntityDeath(container as BuildingPrivlidge, null);
            }

            EntityHandler(container, null);
        }

        private void OnEntityDeath(StorageContainer container, HitInfo hitInfo) => EntityHandler(container, hitInfo);

        private void OnEntityDeath(Door door, HitInfo hitInfo) => BlockHandler(door, hitInfo);

        private void OnEntityDeath(BuildingBlock block, HitInfo hitInfo) => BlockHandler(block, hitInfo);

        private void OnEntityDeath(SimpleBuildingBlock block, HitInfo hitInfo) => BlockHandler(block, hitInfo);

        private void BlockHandler(BaseEntity entity, HitInfo hitInfo)
        {
            if (hitInfo == null)
            {
                return;
            }

            var raid = RaidableBase.Get(entity.transform.position);

            if (raid == null)
            {
                return;
            }

            var player = hitInfo.Initiator as BasePlayer;

            if (!player.IsValid())
            {
                return;
            }

            raid.TrySetOwner(player, entity, hitInfo);
            raid.AddLooter(player);
            raid.CheckDespawn();

            if (raid.Damaged || entity is SimpleBuildingBlock)
            {
                return;
            }

            raid.Damaged = true;
        }

        private object OnEntityGroundMissing(StorageContainer container)
        {
            if (IsBox(container, true))
            {
                var raid = RaidableBase.Get(container);

                if (raid != null && raid.Options.Invulnerable)
                {
                    return true;
                }
            }

            EntityHandler(container, null);
            return null;
        }

        private void EntityHandler(StorageContainer container, HitInfo hitInfo)
        {
            var raid = RaidableBase.Get(container);

            if (raid == null || !raid.IsOpened)
            {
                return;
            }

            if (hitInfo?.Initiator == null)
            {
                DropOrRemoveItems(container);
            }
            else if (IsLootingWeapon(hitInfo))
            {
                var player = GetInitiatorPlayer(hitInfo, raid);

                if (player.IsValid())
                {
                    raid.AddLooter(player);
                }
            }

            raid._containers.Remove(container);
            raid.StartTryToEnd();

            if (IsBox(container, true) || container is BuildingPrivlidge)
            {
                UI.UpdateStatusUI(raid);
            }

            foreach (var x in Raids.Values)
            {
                if (x._containers.Count > 0)
                {
                    return;
                }
            }

            Unsubscribe(nameof(OnEntityKill));
            Unsubscribe(nameof(OnEntityGroundMissing));
        }

        private static bool IsLootingWeapon(HitInfo hitInfo)
        {
            if (hitInfo == null || hitInfo.damageTypes == null)
            {
                return false;
            }

            return hitInfo.damageTypes.Has(DamageType.Explosion) || hitInfo.damageTypes.Has(DamageType.Heat) || hitInfo.damageTypes.IsMeleeType();
        }

        private void OnCupboardAuthorize(BuildingPrivlidge priv, BasePlayer player)
        {
            foreach (var raid in Raids.Values)
            {
                if (raid.priv == priv && raid.Options.RequiresCupboardAccess && !raid.IsAuthed)
                {
                    raid.IsAuthed = true;

                    if (raid.Options.RequiresCupboardAccess && _config.EventMessages.AnnounceRaidUnlock)
                    {
                        foreach (var p in BasePlayer.activePlayerList)
                        {
                            Backbone.Message(p, "OnRaidFinished", FormatGridReference(raid.Location));
                        }
                    }

                    break;
                }
            }

            foreach (var raid in Raids.Values)
            {
                if (!raid.IsAuthed)
                {
                    return;
                }
            }

            Unsubscribe(nameof(OnCupboardAuthorize));
        }

        private object CanPickupEntity(BasePlayer player, BaseEntity entity)
        {
            var raid = RaidableBase.Get(entity);

            if (raid == null)
            {
                return null;
            }

            if (player.IsValid())
            {
                raid.AddLooter(player);
            }

            if (_config.Settings.Management.BlacklistedPickupItems.Any(value => !string.IsNullOrEmpty(value) && entity.ShortPrefabName.Contains(value)))
            {
                return false;
            }

            return !raid.Options.AllowPickup && entity.OwnerID == 0 ? false : (object)null;
        }

        private void OnFireBallSpread(FireBall ball, BaseEntity fire)
        {
            if (EventTerritory(fire.transform.position))
            {
                NextTick(() =>
                {
                    if (fire == null || fire.IsDestroyed)
                    {
                        return;
                    }

                    fire.Kill();
                });
            }
        }

        private void OnEntitySpawned(DroppedItemContainer backpack)
        {
            NextTick(() =>
            {
                if (backpack == null || backpack.IsDestroyed || !backpack.playerSteamID.IsSteamId())
                {
                    return;
                }

                DelaySettings ds;
                if (PvpDelay.TryGetValue(backpack.playerSteamID, out ds) && (ds.AllowPVP && _config.Settings.Management.BackpacksPVP || !ds.AllowPVP && _config.Settings.Management.BackpacksPVE))
                {
                    backpack.playerSteamID = 0;
                    return;
                }

                var raid = RaidableBase.Get(backpack.transform.position);

                if (raid == null)
                {
                    return;
                }

                if (raid.AllowPVP && _config.Settings.Management.BackpacksPVP || !raid.AllowPVP && _config.Settings.Management.BackpacksPVE)
                {
                    backpack.playerSteamID = 0;
                }
            });
        }

        private void OnEntitySpawned(BaseLock entity)
        {
            var parent = entity.GetParentEntity();

            foreach (var raid in Raids.Values)
            {
                if (raid.IsLoading)
                {
                    continue;
                }

                foreach (var container in raid._containers)
                {
                    if (parent == container)
                    {
                        entity.Invoke(entity.KillMessage, 0.1f);
                        break;
                    }
                }
            }
        }

        private void OnEntitySpawned(PlayerCorpse corpse)
        {
            if (corpse == null)
            {
                return;
            }

            var raid = RaidableBase.Get(corpse);

            if (raid == null)
            {
                return;
            }

            if (corpse.playerSteamID.IsSteamId())
            {
                var playerSteamID = corpse.playerSteamID;

                if (raid.Options.EjectCorpses)
                {
                    if (corpse.containers == null)
                    {
                        if (_config.Settings.Management.PlayersLootableInPVE && !raid.AllowPVP || _config.Settings.Management.PlayersLootableInPVP && raid.AllowPVP)
                        {
                            corpse.playerSteamID = 0;
                        }

                        return;
                    }

                    var container = ItemContainer.Drop(StringPool.Get(1519640547), corpse.transform.position, Quaternion.identity, corpse.containers);

                    if (!container.IsValid())
                    {
                        if (_config.Settings.Management.PlayersLootableInPVE && !raid.AllowPVP || _config.Settings.Management.PlayersLootableInPVP && raid.AllowPVP)
                        {
                            corpse.playerSteamID = 0;
                        }

                        return;
                    }

                    container.playerName = corpse.playerName;
                    container.playerSteamID = corpse.playerSteamID;

                    for (int i = 0; i < corpse.containers.Length; i++)
                    {
                        corpse.containers[i].Kill();
                    }

                    corpse.containers = null;
                    corpse.Kill();

                    var player = RustCore.FindPlayerById(playerSteamID);
                    var data = raid.AddCorpse(container, player);

                    if (raid.EjectCorpse(container.net.ID, data))
                    {
                        raid.corpses.Remove(container.net.ID);
                    }
                    else Interface.CallHook("OnRaidablePlayerCorpse", player, container);

                    if (_config.Settings.Management.PlayersLootableInPVE && !raid.AllowPVP || _config.Settings.Management.PlayersLootableInPVP && raid.AllowPVP)
                    {
                        container.playerSteamID = 0;
                    }

                    return;
                }

                if (_config.Settings.Management.PlayersLootableInPVE && !raid.AllowPVP || _config.Settings.Management.PlayersLootableInPVP && raid.AllowPVP)
                {
                    corpse.playerSteamID = 0;
                }
            }
            else
            {
                if (raid.Options.NPC.DespawnInventory)
                {
                    corpse.Invoke(corpse.KillMessage, 30f);
                }

                raid.npcs.RemoveAll(npc => npc == null || npc.userID == corpse.playerSteamID);
                Npcs.Remove(corpse.playerSteamID);

                if (raid.Options.RespawnRateMax > 0f)
                {
                    raid.TryRespawnNpc();
                    return;
                }

                if (!AnyNpcs())
                {
                    Unsubscribe(nameof(OnNpcTarget));
                    Unsubscribe(nameof(OnNpcResume));
                    Unsubscribe(nameof(OnNpcDestinationSet));
                }
            }
        }

        private object CanBuild(Planner planner, Construction construction, Construction.Target target)
        {
            var buildPos = target.entity && target.entity.transform && target.socket ? target.GetWorldPosition() : target.position;
            var raid = RaidableBase.Get(buildPos);

            if (raid == null)
            {
                return null;
            }

            if (construction.prefabID == 2150203378)
            {
                if (_config.Settings.Management.AllowLadders)
                {
                    PlayerInputEx input;
                    if (raid.Inputs.TryGetValue(target.player, out input))
                    {
                        input.Restart();
                        input.TryPlaceLadder(target.player, raid);
                    }
                }
                else
                {
                    target.player.ChatMessage(lang.GetMessage("Ladders are blocked!", this, target.player.UserIDString));
                    return false;
                }
            }
            else if (!_config.Settings.Management.AllowBuilding)
            {
                target.player.ChatMessage(lang.GetMessage("Building is blocked!", this, target.player.UserIDString));
                return false;
            }

            return null;
        }

        private void OnLootEntityEnd(BasePlayer player, StorageContainer container)
        {
            if (container?.inventory == null || container.OwnerID.IsSteamId() || IsInvisible(player))
            {
                return;
            }

            var raid = RaidableBase.Get(container);

            if (raid == null)
            {
                return;
            }

            if (IsBox(container, true) || container is BuildingPrivlidge)
            {
                UI.UpdateStatusUI(raid);
            }

            if (raid.Options.DropTimeAfterLooting <= 0 || (raid.Options.DropOnlyBoxesAndPrivileges && !IsBox(container, true) && !(container is BuildingPrivlidge)))
            {
                return;
            }

            if (container.inventory.IsEmpty() && (container.prefabID == Constants.LARGE_WOODEN_BOX || container.prefabID == Constants.SMALL_WOODEN_BOX || container.prefabID == Constants.COFFIN_STORAGE))
            {
                container.Invoke(container.KillMessage, 0.1f);
            }
            else container.Invoke(() => DropOrRemoveItems(container), raid.Options.DropTimeAfterLooting);
        }

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            var raid = RaidableBase.Get(entity.transform.position);

            if (raid == null)
            {
                return;
            }

            raid.OnLootEntityInternal(player, entity);
        }

        private object CanBePenalized(BasePlayer player)
        {
            var raid = RaidableBase.Get(player);

            if (raid == null)
            {
                return null;
            }

            if (raid.Type == RaidableType.None || raid.AllowPVP && !raid.Options.PenalizePVP || !raid.AllowPVP && !raid.Options.PenalizePVE)
            {
                return false;
            }

            return null;
        }

        private void CanOpenBackpack(BasePlayer looter, ulong backpackOwnerID)
        {
            var raid = RaidableBase.Get(looter.transform.position);

            if (raid == null)
            {
                return;
            }

            if (!raid.AllowPVP && !_config.Settings.Management.BackpacksOpenPVE || raid.AllowPVP && !_config.Settings.Management.BackpacksOpenPVP)
            {
                looter.Invoke(looter.EndLooting, 0.1f);
                Player.Message(looter, lang.GetMessage("NotAllowed", this, looter.UserIDString));
            }
        }

        private bool CanDropPlayerBackpack(BasePlayer player, RaidableBase raid)
        {
            DelaySettings ds;
            if (PvpDelay.TryGetValue(player.userID, out ds) && (ds.AllowPVP && _config.Settings.Management.BackpacksPVP || !ds.AllowPVP && _config.Settings.Management.BackpacksPVE))
            {
                return true;
            }

            if (raid == null)
            {
                return false;
            }

            return raid.AllowPVP && _config.Settings.Management.BackpacksPVP || !raid.AllowPVP && _config.Settings.Management.BackpacksPVE;
        }

        /*private object CanBeTargeted(BasePlayer target, MonoBehaviour turret)
        {
            var result = CanEntityBeTargeted(target, turret as BaseEntity);

            if (result is bool)
            {
                return (bool)result ? (object)null : false;
            }

            return null;
        }*/

        private object CanEntityBeTargeted(BasePlayer player, BaseEntity turret)
        {
            if (!IsValid(player) || !turret.IsValid() || IsInvisible(player))
            {
                return null;
            }

            if (RaidableBase.Has(player.userID) || RaidableBase.Has(turret))
            {
                return true;
            }

            return null;
        }

        private object OnTrapTrigger(BaseTrap trap, GameObject go)
        {
            var player = go.GetComponent<BasePlayer>();
            var result = CanEntityTrapTrigger(trap, player);

            if (result is bool)
            {
                return (bool)result ? (object)null : true;
            }

            return null;
        }

        private object CanEntityTrapTrigger(BaseTrap trap, BasePlayer player)
        {
            if (!IsValid(player) || IsInvisible(player))
            {
                return null;
            }

            if (RaidableBase.Has(player.userID))
            {
                return false;
            }

            return EventTerritory(player.transform.position) ? true : (object)null;
        }

        private object CanEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
            if (hitInfo == null || hitInfo.damageTypes == null || !IsValid(entity))
            {
                return null;
            }

            var success = entity is BasePlayer ? HandlePlayerDamage(entity as BasePlayer, hitInfo) : HandleEntityDamage(entity, hitInfo);

            if (success is bool && !(bool)success)
            {
                NullifyDamage(hitInfo);
                return false;
            }

            return success;
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo) => CanEntityTakeDamage(entity, hitInfo);

        private object HandlePlayerDamage(BasePlayer victim, HitInfo hitInfo)
        {
            var raid = RaidableBase.Get(victim);

            if (raid == null || raid.killed)
            {
                return null;
            }

            var attacker = GetInitiatorPlayer(hitInfo, raid);

            if (IsValid(attacker))
            {
                if (PvpDelay.ContainsKey(victim.userID))
                {
                    if (EventTerritory(attacker.transform.position))
                    {
                        return true;
                    }

                    if (_config.Settings.Management.PVPDelayAnywhere && PvpDelay.ContainsKey(attacker.userID))
                    {
                        return true;
                    }
                }

                if (_config.Settings.Management.PVPDelayDamageInside && PvpDelay.ContainsKey(attacker.userID) && InRange(raid.Location, victim.transform.position, raid.Options.ProtectionRadius))
                {
                    return true;
                }

                if (attacker.userID == victim.userID || (_config.TruePVE.ServerWidePVP && !victim.IsNpc && IsPVE()))
                {
                    return true;
                }

                if (victim.IsNpc && !InRange(raid.Location, victim.transform.position, raid.Options.ProtectionRadius))
                {
                    return true;
                }

                if (!attacker.IsNpc && CanBlockOutsideDamage(raid, attacker, raid.Options.BlockOutsideDamageToPlayersInside))
                {
                    return false;
                }

                if (victim.IsNpc && !attacker.IsNpc)
                {
                    if ((_config.Settings.Management.BlockMounts && attacker.GetMounted()) || (raid.ownerId.IsSteamId() && !raid.IsAlly(attacker)))
                    {
                        return false;
                    }

                    FinalDestination fd;
                    if (Backbone.Destinations.TryGetValue(victim.userID, out fd))
                    {
                        var e = attacker.HasParent() ? attacker.GetParentEntity() : null;

                        if (e is ScrapTransportHelicopter || e is HotAirBalloon)
                        {
                            return false;
                        }

                        fd.Attack(attacker);
                    }

                    return true;
                }
                else if (!victim.IsNpc && !attacker.IsNpc)
                {
                    if (!raid.AllowPVP || (!raid.Options.AllowFriendlyFire && raid.IsOnSameTeam(victim.userID, attacker.userID)))
                    {
                        return false;
                    }

                    if (IsPVE())
                    {
                        if (!InRange(attacker.transform.position, raid.Location, raid.Options.ProtectionRadius, false))
                        {
                            return false;
                        }

                        return InRange(victim.transform.position, raid.Location, raid.Options.ProtectionRadius, false);
                    }

                    return true;
                }
                else if (RaidableBase.Has(attacker.userID))
                {
                    if (RaidableBase.Has(victim.userID) || (InRange(attacker.transform.position, raid.Location, raid.Options.ProtectionRadius) && CanBlockOutsideDamage(raid, victim, raid.Options.BlockNpcDamageToPlayersOutside)))
                    {
                        return false;
                    }

                    if (UnityEngine.Random.Range(0, 100) > raid.Options.NPC.Accuracy)
                    {
                        return false;
                    }

                    return true;
                }
            }
            else if (IsTrueDamage(hitInfo.Initiator) && EventTerritory(victim.transform.position))
            {
                if (hitInfo.Initiator is AutoTurret)
                {
                    hitInfo.damageTypes.Scale(DamageType.Bullet, UnityEngine.Random.Range(raid.Options.AutoTurret.Min, raid.Options.AutoTurret.Max));
                }

                return true;
            }
            else if (RaidableBase.Has(victim.userID))
            {
                return hitInfo.Initiator is AutoTurret; // make npc's immune to all damage which isn't from a player or turret
            }

            return null;
        }

        private object HandleEntityDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
            var raid = RaidableBase.Get(entity.transform.position);

            if (raid == null || raid.killed)
            {
                return null;
            }

            if (entity.IsNpc || entity is PlayerCorpse)
            {
                return true;
            }

            if (hitInfo.damageTypes.GetMajorityDamageType() == DamageType.Decay)
            {
                return false;
            }

            if (entity is BaseMountable || entity.name.Contains("modularcar"))
            {
                if (hitInfo.Initiator is SamSite)
                {
                    return _config.Settings.Management.MountDamageFromSamSites;
                }

                if (!_config.Settings.Management.MountDamageFromPlayers && hitInfo.Initiator is BasePlayer)
                {
                    return false;
                }
            }

            if (!RaidableBase.Has(entity) && !raid.BuiltList.Contains(entity.net.ID))
            {
                bool attached = entity is DecayEntity && (entity as DecayEntity).buildingID == raid.BuildingID;

                if (!attached)
                {
                    return null;
                }
            }

            if (hitInfo.Initiator == entity && entity is AutoTurret)
            {
                return false;
            }

            var attacker = GetInitiatorPlayer(hitInfo, raid);

            if (!IsValid(attacker))
            {
                return null;
            }

            if (attacker.IsNpc)
            {
                return true;
            }

            entity.lastAttacker = attacker;
            attacker.lastDealtDamageTime = Time.time;

            if (_config.Settings.Management.BlockMounts && attacker.GetMounted())
            {
                return false;
            }

            if (CanBlockOutsideDamage(raid, attacker, raid.Options.BlockOutsideDamageToBaseInside))
            {
                return false;
            }

            if (raid.ID.IsSteamId() && IsBox(entity, true) && raid.IsAlly(attacker.userID, Convert.ToUInt64(raid.ID)))
            {
                return false;
            }

            if (raid.ownerId.IsSteamId() && !raid.IsAlly(attacker))
            {
                return false;
            }

            if (raid.Options.AutoTurret.AutoAdjust && raid.turrets.Count > 0 && entity is AutoTurret)
            {
                var turret = entity as AutoTurret;

                if (raid.turrets.Contains(turret) && turret.sightRange <= raid.Options.AutoTurret.SightRange)
                {
                    turret.sightRange = raid.Options.AutoTurret.SightRange * 2f;
                }
            }

            if (!raid.Options.ExplosionModifier.Equals(100) && hitInfo.damageTypes.Has(DamageType.Explosion))
            {
                float m = Mathf.Clamp(raid.Options.ExplosionModifier, 0f, 999f);

                hitInfo.damageTypes.Scale(DamageType.Explosion, m.Equals(0f) ? 0f : m / 100f);
            }

            if (raid.BuiltList.Contains(entity.net.ID))
            {
                return true;
            }

            if (raid.Type != RaidableType.None)
            {
                raid.IsEngaged = true;
                raid.CheckDespawn();
            }

            raid.TrySetOwner(attacker, entity, hitInfo);

            if (raid.IsOpened && IsLootingWeapon(hitInfo))
            {
                raid.AddLooter(attacker);
            }

            if (raid.Options.BlocksImmune && entity is BuildingBlock)
            {
                return false;
            }

            if (raid.Options.Invulnerable && IsBox(entity, true))
            {
                return false;
            }

            return true;
        }

        private BasePlayer GetInitiatorPlayer(HitInfo hitInfo, RaidableBase raid)
        {
            if (hitInfo.Initiator is BasePlayer)
            {
                return hitInfo.Initiator as BasePlayer;
            }

            if (hitInfo.Initiator.IsValid() && raid != null && (hitInfo.damageTypes.GetMajorityDamageType() == DamageType.Heat || hitInfo.Initiator is FireBall))
            {
                uint id = hitInfo.Initiator.net.ID;

                foreach (var intruder in raid.intruders)
                {
                    if (intruder.IsValid() && Time.time - intruder.lastDealtDamageTime < 1f && IsUsingProjectile(intruder))
                    {
                        intruder.lastDealtDamageTime = Time.time;
                        raid.records[id] = intruder;
                        return intruder;
                    }
                }

                BasePlayer player;
                if (raid.records.TryGetValue(id, out player) && player.IsValid())
                {
                    player.lastDealtDamageTime = Time.time;
                    return player;
                }
            }

            var combatEntity = hitInfo.HitEntity as BaseCombatEntity;

            if (combatEntity != null)
            {
                return combatEntity.lastAttacker as BasePlayer;
            }

            return null;
        }

        private bool IsUsingProjectile(BasePlayer player)
        {
            if (player == null || player.svActiveItemID == 0)
            {
                return false;
            }

            Item item = player.GetActiveItem();

            if (item == null)
            {
                return false;
            }

            return item.GetHeldEntity() is BaseProjectile;
        }

        #endregion Hooks

        #region Spawn

        private static float GetSpawnHeight(Vector3 target, bool flag = true)
        {
            float y = TerrainMeta.HeightMap.GetHeight(target);
            float w = TerrainMeta.WaterMap.GetHeight(target);
            float p = TerrainMeta.HighestPoint.y + 250f;
            RaycastHit hit;

            // start a water height and cast up P height for world
            if (Physics.Raycast(new Vector3(target.x, w, target.z), Vector3.up, out hit, p, Layers.Mask.World, QueryTriggerInteraction.Ignore))
            {
                y = Mathf.Max(y, hit.point.y);

                // start at hit point height and cast up P height for world
                if (Physics.Raycast(new Vector3(target.x, hit.point.y + 0.5f, target.z), Vector3.up, out hit, p, Layers.Mask.World, QueryTriggerInteraction.Ignore))
                {
                    y = Mathf.Max(y, hit.point.y);
                }
            }

            return flag ? Mathf.Max(y, w) : y;
        }

        /*private static Vector3 GetSpawnHeight(Vector3 pos, float p)
        {
            float h = TerrainMeta.HeightMap.GetHeight(pos);
            pos.y += TerrainMeta.HighestPoint.y;

            RaycastHit hit;
            if (Physics.Raycast(pos, Vector3.down, out hit, pos.y + 1f, Layers.Mask.World, QueryTriggerInteraction.Ignore))
            {
                pos.y = Mathf.Max(hit.point.y, h);
            }
            else pos.y = h;

            return pos;
        }*/

        private bool InDeepWater(Vector3 vector)
        {
            if (_config.Settings.Management.Seabed)
            {
                return false;
            }

            if (TerrainMeta.WaterMap.GetHeight(vector) - TerrainMeta.HeightMap.GetHeight(vector) > 5f)
            {
                return true;
            }

            return false;
        }

        protected void LoadSpawns()
        {
            raidSpawns.Clear();
            raidSpawns.Add(RaidableType.Grid, new RaidableSpawns());

            if (SpawnsFileValid(_config.Settings.Manual.SpawnsFile))
            {
                var spawns = GetSpawnsLocations(_config.Settings.Manual.SpawnsFile);

                if (spawns?.Count > 0)
                {
                    Puts(Backbone.GetMessage("LoadedManual", null, spawns.Count));
                    raidSpawns[RaidableType.Manual] = new RaidableSpawns(spawns);
                }
            }

            if (SpawnsFileValid(_config.Settings.Schedule.SpawnsFile))
            {
                var spawns = GetSpawnsLocations(_config.Settings.Schedule.SpawnsFile);

                if (spawns?.Count > 0)
                {
                    Puts(Backbone.GetMessage("LoadedScheduled", null, spawns.Count));
                    raidSpawns[RaidableType.Scheduled] = new RaidableSpawns(spawns);
                }
            }

            if (SpawnsFileValid(_config.Settings.Maintained.SpawnsFile))
            {
                var spawns = GetSpawnsLocations(_config.Settings.Maintained.SpawnsFile);

                if (spawns?.Count > 0)
                {
                    Puts(Backbone.GetMessage("LoadedMaintained", null, spawns.Count));
                    raidSpawns[RaidableType.Maintained] = new RaidableSpawns(spawns);
                }
            }
        }

        protected void SetupGrid()
        {
            if (raidSpawns.Count >= 5)
            {
                StartAutomation();
                return;
            }

            StopGridCoroutine();

            NextTick(() =>
            {
                gridStopwatch.Start();
                gridTime = Time.realtimeSinceStartup;
                gridCoroutine = ServerMgr.Instance.StartCoroutine(GenerateGrid());
            });
        }

        private static bool ContainsTopology(TerrainTopology.Enum mask, Vector3 position)
        {
            return (TerrainMeta.TopologyMap.GetTopology(position) & (int)mask) != 0;
        }

        private static bool ContainsTopology(TerrainTopology.Enum mask, Vector3 position, float radius)
        {
            return (TerrainMeta.TopologyMap.GetTopology(position, radius) & (int)mask) != 0;
        }

        private bool IsInsideBounds(OBB obb, Vector3 worldPos)
        {
            return obb.ClosestPoint(worldPos) == worldPos;
        }

        private bool IsValidLocation(Vector3 vector, float radius, float md)
        {
            foreach (var zone in managedZones)
            {
                if (zone.Value.Size != Vector3.zero)
                {
                    if (IsInsideBounds(zone.Value.OBB, vector))
                    {
                        draw(vector, "Z");
                        return false;
                    }
                }
                else if (InRange(zone.Key, vector, zone.Value.Distance))
                {
                    draw(vector, "Z");
                    return false;
                }
            }

            if (InDeepWater(vector))
            {
                draw(vector, "D");
                return false;
            }

            if (IsMonumentPosition(vector) || ContainsTopology(TerrainTopology.Enum.Monument, vector, md))
            {
                draw(vector, "M");
                return false;
            }

            if (!_config.Settings.Management.AllowOnBuildingTopology && ContainsTopology(TerrainTopology.Enum.Building, vector, Constants.RADIUS))
            {
                draw(vector, "B");
                return false;
            }

            if (!_config.Settings.Management.AllowOnRivers && ContainsTopology(TerrainTopology.Enum.River | TerrainTopology.Enum.Riverside, vector, Constants.RADIUS))
            {
                draw(vector, "W");
                return false;
            }

            if (!_config.Settings.Management.AllowOnRoads && ContainsTopology(TerrainTopology.Enum.Road | TerrainTopology.Enum.Roadside, vector, Constants.RADIUS))
            {
                draw(vector, "R");
                return false;
            }

            CacheType cacheType;
            string message;
            if (!IsAreaSafe(ref vector, radius, Layers.Mask.World | Layers.Mask.Deployed, out cacheType, out message))
            {
                return false;
            }

            return true;
        }

        private void StopGridCoroutine()
        {
            if (gridCoroutine != null)
            {
                ServerMgr.Instance.StopCoroutine(gridCoroutine);
                gridCoroutine = null;
            }
        }

        private IEnumerator GenerateGrid() // Credits to Jake_Rich for creating this for me!
        {
            var _instruction = ConVar.FPS.limit > 80 ? CoroutineEx.waitForSeconds(Constants.INSTRUCTION_TIME) : null;
            RaidableSpawns rs = raidSpawns[RaidableType.Grid] = new RaidableSpawns();
            int minPos = (int)(World.Size / -2f);
            int maxPos = (int)(World.Size / 2f);
            int checks = 0;
            float md = Constants.RADIUS * 2f + _config.Settings.Management.MonumentDistance;
            float pr = 50f;

            foreach (var profile in Buildings.Profiles.Values)
            {
                if (profile.Options.ProtectionRadius > pr)
                {
                    pr = profile.Options.ProtectionRadius;
                }

                if (profile.Options.ArenaWalls.Radius > pr)
                {
                    pr = profile.Options.ArenaWalls.Radius;
                }
            }

            for (float x = minPos; x < maxPos; x += Constants.CELL_SIZE)
            {
                for (float z = minPos; z < maxPos; z += Constants.CELL_SIZE)
                {
                    var pos = new Vector3(x, 0f, z);
                    
                    pos.y = GetSpawnHeight(pos);

                    if (_config.Settings.Management.Seabed)
                    {
                        var h = TerrainMeta.HeightMap.GetHeight(pos);

                        if (TerrainMeta.WaterMap.GetHeight(pos) > h)
                        {
                            pos.y = h;
                        }
                    }

                    //draw(pos, "+");
                    ExtractLocation(rs, _config.Settings.Management.Elevation, md, pr, pos);

                    if (++checks >= 75)
                    {
                        checks = 0;
                        yield return _instruction;
                    }
                }
            }

            Puts(Backbone.GetMessage("InitializedGrid", null, gridStopwatch.Elapsed.Seconds, gridStopwatch.Elapsed.Milliseconds, World.Size, rs.Count));
            gridCoroutine = null;
            gridStopwatch.Stop();
            gridStopwatch.Reset();
            StartAutomation();
        }

        private void ExtractLocation(RaidableSpawns rs, float max, float md, float pr, Vector3 pos)
        {
            if (IsValidLocation(pos, Constants.CELL_SIZE, md))
            {
                var elevation = GetTerrainElevation(pos);

                if (IsFlatTerrain(pos, elevation, max))
                {
                    var rsl = new RaidableSpawnLocation
                    {
                        Location = pos,
                        Elevation = elevation,
                        WaterHeight = TerrainMeta.WaterMap.GetHeight(pos),
                        TerrainHeight = TerrainMeta.HeightMap.GetHeight(pos),
                        SpawnHeight = GetSpawnHeight(pos, false),
                        Radius = pr,
                        AutoHeight = true
                    };

                    rs.Spawns.Add(rsl);

                    if (IsSubmerged(rsl))
                    {
                        rs.Remove(rsl, CacheType.Submerged);
                    }
                }
                else draw(pos, $"E:{elevation.Max - elevation.Min}");
            }
        }

        private bool IsSubmerged(RaidableSpawnLocation rsl)
        {
            if (rsl.WaterHeight - rsl.TerrainHeight > _config.Settings.Management.WaterDepth)
            {
                if (!_config.Settings.Management.Submerged)
                {
                    return true;
                }

                rsl.Location.y = rsl.WaterHeight;
            }

            if (!_config.Settings.Management.Submerged && _config.Settings.Management.SubmergedAreaCheck && IsSubmerged(rsl, rsl.Radius))
            {
                return true;
            }

            return false;
        }

        private bool IsSubmerged(RaidableSpawnLocation rsl, float radius)
        {
            if (rsl.Surroundings.Count == 0)
            {
                rsl.Surroundings = GetCircumferencePositions(rsl.Location, radius, 90f, false, 1f);
            }

            foreach (var vector in rsl.Surroundings)
            {
                float w = TerrainMeta.WaterMap.GetHeight(vector);
                float h = TerrainMeta.HeightMap.GetHeight(vector);

                if (w - h > _config.Settings.Management.WaterDepth)
                {
                    return true;
                }
            }

            return false;
        }

        private void draw(Vector3 pos, string text)
        {
#if DEBUG_DRAWINGS
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (!player.IsAdmin || !InRange(player.transform.position, pos, 100f))
                    continue;

                player.SendConsoleCommand("ddraw.text", 60f, Color.yellow, pos, text);
            }
#endif
        }

        private bool SpawnsFileValid(string spawnsFile)
        {
            if (Spawns == null || !Spawns.IsLoaded || string.IsNullOrEmpty(spawnsFile))
            {
                return false;
            }

            if (!FileExists($"SpawnsDatabase{Path.DirectorySeparatorChar}{spawnsFile}"))
            {
                return false;
            }

            return Spawns?.Call("GetSpawnsCount", spawnsFile) is int;
        }

        private HashSet<RaidableSpawnLocation> GetSpawnsLocations(string spawnsFile)
        {
            object success = Spawns?.Call("LoadSpawnFile", spawnsFile);

            if (success == null)
            {
                return null;
            }

            var list = (List<Vector3>)success;
            var locations = new HashSet<RaidableSpawnLocation>();

            foreach (var pos in list)
            {
                locations.Add(new RaidableSpawnLocation
                {
                    Location = pos
                });
            }

            return locations;
        }

        private void StartAutomation()
        {
            if (scheduleEnabled)
            {
                if (storedData.RaidTime != DateTime.MinValue.ToString() && GetRaidTime() > _config.Settings.Schedule.IntervalMax) // Allows users to lower max event time
                {
                    storedData.RaidTime = DateTime.MinValue.ToString();
                    SaveData();
                }

                StartScheduleCoroutine();
            }

            StartMaintainCoroutine();
        }

        private static void Shuffle<T>(IList<T> list) // Fisher-Yates shuffle
        {
            int count = list.Count;
            int n = count;
            while (n-- > 0)
            {
                int k = UnityEngine.Random.Range(0, count);
                int j = UnityEngine.Random.Range(0, count);
                T value = list[k];
                list[k] = list[j];
                list[j] = value;
            }
        }

        private static Vector3 HasBuildingPrivilege(Vector3 target, float radius)
        {
            var vector = Vector3.zero;
            var list = Pool.GetList<BuildingPrivlidge>();
            Vis.Entities(target, radius, list);
            foreach (var tc in list)
            {
                if (tc.IsValid() && !RaidableBase.Has(tc))
                {
                    vector = tc.transform.position;
                    break;
                }
            }
            Pool.FreeList(ref list);
            return vector;
        }

        private readonly List<string> assets = new List<string>
        {
            "/props/", "/structures/", "/building/", "train_", "powerline_", "dune", "candy-cane", "assets/content/nature/"
        };

        private readonly Dictionary<Vector3, string> _rocks = new Dictionary<Vector3, string>();

        private float GetRockHeight(Vector3 a)
        {
            RaycastHit hit;
            if (Physics.Raycast(a + new Vector3(0f, 50f, 0f), Vector3.down, out hit, a.y + 51f, Layers.Mask.World, QueryTriggerInteraction.Ignore))
            {
                return Mathf.Abs(hit.point.y - a.y);
            }

            return 0f;
        }

        private bool IsAreaSafe(ref Vector3 position, float radius, int layers, out CacheType cacheType, out string message, RaidableType type = RaidableType.None)
        {
            message = string.Empty;

            var colliders = Pool.GetList<Collider>();

            Vis.Colliders(position, radius, colliders, layers, QueryTriggerInteraction.Ignore);

            int count = colliders.Count;

            cacheType = CacheType.Generic;

            foreach (var collider in colliders)
            {
                if (collider == null || collider.transform == null)
                {
                    count--;
                    continue;
                }

                var e = collider.ToBaseEntity();

                if (IsAsset(collider.name) && e == null)
                {
                    message = $"Blocked by a map prefab {collider.transform.position} {collider.name}";
                    count = int.MaxValue;
                    cacheType = CacheType.Delete;
                    break;
                }

                if (e.IsValid())
                {
                    if (e is BasePlayer)
                    {
                        var player = e as BasePlayer;

                        if (_config.Settings.Management.EjectSleepers && player.IsSleeping())
                        {
                            count--;
                        }
                        else if (player.IsNpc || player.IsFlying)
                        {
                            count--;
                        }
                        else
                        {
                            message = $"A player is too close {e.transform.position}";
                            count = int.MaxValue;
                            cacheType = CacheType.Temporary;
                            break;
                        }
                    }
                    else if (_config.Settings.Schedule.Skip && type == RaidableType.Scheduled && e.OwnerID.IsSteamId())
                    {
                        count--;
                    }

                    else if (_config.Settings.Maintained.Skip && type == RaidableType.Maintained && e.OwnerID.IsSteamId())
                    {
                        count--;
                    }
                    else if (RaidableBase.Has(e))
                    {
                        message = $"Already occupied by a raidable base {e.transform.position}";
                        count = int.MaxValue;
                        cacheType = CacheType.Temporary;
                        break;
                    }
                    else if (e.IsNpc || e is SleepingBag)
                    {
                        count--;
                    }
                    else if (e is BaseOven)
                    {
                        if (e.bounds.size.Max() > 1.6f)
                        {
                            message = $"A oven is too close {e.transform.position}";
                            count = int.MaxValue;
                            cacheType = CacheType.Temporary;
                            break;
                        }
                        else
                        {
                            count--;
                        }
                    }
                    else if (e.OwnerID == 0)
                    {
                        if (e is BuildingBlock)
                        {
                            message = $"A non-player building block is too close {e.transform.position}";
                            count = int.MaxValue;
                            cacheType = CacheType.Temporary;
                            break;
                        }
                        else count--;
                    }
                    else
                    {
                        message = $"Blocked by {e.ShortPrefabName} {e.transform.position}";
                        count = int.MaxValue;
                        cacheType = CacheType.Temporary;
                        break;
                    }
                }
                else if (collider.gameObject.layer == (int)Layer.World)
                {
                    if (collider.name.Contains("rock_"))
                    {
                        float height = GetRockHeight(collider.transform.position);

                        if (height > 2f)
                        {
                            message = $"Rock is too large {collider.transform.position}";
                            //if (!_rocks.ContainsKey(position)) _rocks.Add(position, $"{collider.bounds.size}: {height}");
                            draw(position, $"{collider.name}> {height}");
                            count = int.MaxValue;
                            cacheType = CacheType.Delete;
                            break;
                        }
                        else count--;
                    }
                    else if (!_config.Settings.Management.AllowOnRoads && collider.name.StartsWith("road_"))
                    {
                        message = $"Not allowed on roads {collider.transform.position}";
                        draw(position, "road");
                        count = int.MaxValue;
                        cacheType = CacheType.Delete;
                        break;
                    }
                    else if (collider.name.StartsWith("ice_sheet"))
                    {
                        message = $"Not allowed on ice sheets {collider.transform.position}";
                        draw(position, "ice_sheet");
                        count = int.MaxValue;
                        cacheType = CacheType.Delete;
                        break;
                    }
                    else count--;
                }
                else if (collider.gameObject.layer == (int)Layer.Water)
                {
                    if (!_config.Settings.Management.AllowOnRivers && collider.name.StartsWith("River Mesh"))
                    {
                        message = $"Not allowed on rivers {collider.transform.position}";
                        count = int.MaxValue;
                        draw(position, "river");
                        cacheType = CacheType.Delete;
                        break;
                    }

                    count--;
                }
                else count--;
            }

            Pool.FreeList(ref colliders);

            return count == 0;
        }

        private bool IsAsset(string value)
        {
            foreach (var asset in assets)
            {
                if (value.Contains(asset))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsMonumentPosition(Vector3 target)
        {
            foreach (var monument in monuments)
            {
                if (InRange(monument.Key.transform.position, target, monument.Value))
                {
                    return true;
                }
            }

            return false;
        }

        private void SetAllowPVP(RaidableType type, RaidableBase raid, bool flag)
        {
            if (type == RaidableType.Maintained && _config.Settings.Maintained.Chance > 0)
            {
                raid.AllowPVP = Core.Random.Range(0, 101) <= _config.Settings.Maintained.Chance;
                return;
            }

            if (type == RaidableType.Scheduled && _config.Settings.Schedule.Chance > 0)
            {
                raid.AllowPVP = Core.Random.Range(0, 101) <= _config.Settings.Schedule.Chance;
                return;
            }

            if (type == RaidableType.Maintained && _config.Settings.Maintained.ConvertPVP)
            {
                raid.AllowPVP = false;
            }
            else if (type == RaidableType.Scheduled && _config.Settings.Schedule.ConvertPVP)
            {
                raid.AllowPVP = false;
            }
            else if (type == RaidableType.Manual && _config.Settings.Manual.ConvertPVP)
            {
                raid.AllowPVP = false;
            }
            else if (type == RaidableType.Maintained && _config.Settings.Maintained.ConvertPVE)
            {
                raid.AllowPVP = true;
            }
            else if (type == RaidableType.Scheduled && _config.Settings.Schedule.ConvertPVE)
            {
                raid.AllowPVP = true;
            }
            else if (type == RaidableType.Manual && _config.Settings.Manual.ConvertPVE)
            {
                raid.AllowPVP = true;
            }
            else raid.AllowPVP = flag;
        }

        public bool TryOpenEvent(RaidableType type, Vector3 position, int uid, string BaseName, BaseProfile profile, RaidableMode mode, out RaidableBase raid)
        {
            if (IsUnloading)
            {
                raid = null;
                return false;
            }

            raid = new GameObject().AddComponent<RaidableBase>();

            SetAllowPVP(type, raid, profile.Options.AllowPVP);

            raid.DifficultyMode = Backbone.GetMessage("Normal");
            raid.PastedLocation = position;
            raid.Location = position;
            raid.Options = profile.Options;
            raid.BaseName = BaseName;
            raid.Type = type;
            raid.uid = uid;

            Cycle.Add(type, mode, BaseName);

            if (_config.Settings.NoWizardry && Wizardry != null && Wizardry.IsLoaded)
            {
                Subscribe(nameof(OnActiveItemChanged));
            }

            if (_config.Settings.BlacklistedCommands.Count > 0)
            {
                Subscribe(nameof(OnPlayerCommand));
                Subscribe(nameof(OnServerCommand));
            }

            Subscribe(nameof(OnEntitySpawned));

            if (!IsPVE())
            {
                Subscribe(nameof(OnEntityTakeDamage));
            }

            Subscribe(nameof(CanEntityTakeDamage));

            storedData.TotalEvents++;
            SaveData();

            if (_config.LustyMap.Enabled && LustyMap != null && LustyMap.IsLoaded)
            {
                AddTemporaryLustyMarker(position, uid);
            }

            if (Map)
            {
                AddMapPrivatePluginMarker(position, uid);
            }

            Raids[uid] = raid;
            return true;
        }

        #endregion

        #region Paste

        protected bool IsGridLoading
        {
            get
            {
                return gridCoroutine != null;
            }
        }

        protected bool IsPasteAvailable
        {
            get
            {
                foreach (var raid in Raids.Values)
                {
                    if (raid.IsLoading)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private void PasteBuilding(RaidableType type, Vector3 position, KeyValuePair<string, BaseProfile> profile, RaidableSpawns rs, RaidableMode mode)
        {
            if (Locations.ContainsKey(position) || !IsProfileValid(profile))
            {
                return;
            }

            LoadingTimes[position] = Time.time;

            int uid;

            do
            {
                uid = UnityEngine.Random.Range(1000, 100000);
            } while (Raids.ContainsKey(uid));

            var distance = rs == null ? profile.Value.Options.ProtectionRadius : rs.RemoveNear(position, profile.Value.Options.ProtectionRadius, CacheType.Generic, type);

            var callback = new Action(() =>
            {
                RaidableBase raid;
                if (TryOpenEvent(type, position, uid, profile.Key, profile.Value, mode, out raid))
                {
                    Cycle.Add(type, mode, profile.Key);

                    raid.rs = rs;
                    raid.RemoveNearDistance = distance;
                }
                else
                {
                    ResetLoadingType(type);
                    Locations.Remove(position);

                    if (rs == null)
                    {
                        return;
                    }

                    rs.AddNear(position, distance, CacheType.Generic);
                }
            });

            List<PasteOption> options = profile.Value.Options.PasteOptions;

            foreach (var kvp in profile.Value.Options.AdditionalBases)
            {
                if (kvp.Key.Equals(profile.Key, StringComparison.OrdinalIgnoreCase))
                {
                    options = kvp.Value;
                    break;
                }
            }

            var list = GetListedOptions(options);

            Locations.Add(position, type);

            CopyPaste.Call("TryPasteFromVector3", position, 0f, profile.Key, list.ToArray(), callback);
        }

        protected void ResetLoadingType(RaidableType type)
        {
            if (type == RaidableType.Maintained) IsLoadingMaintainedEvent = false;
            if (type == RaidableType.Scheduled) IsLoadingScheduledEvent = false;
        }

        private static Dictionary<Vector3, RaidableType> Locations = new Dictionary<Vector3, RaidableType>();

        private List<string> GetListedOptions(List<PasteOption> options)
        {
            var list = new List<string>();
            bool flag1 = false, flag2 = false, flag3 = false, flag4 = false;

            for (int i = 0; i < options.Count; i++)
            {
                string key = options[i].Key.ToLower();
                string value = options[i].Value.ToLower();

                if (key == "stability") flag1 = true;
                if (key == "autoheight") flag2 = true;
                if (key == "height") flag3 = true;
                if (key == "entityowner") flag4 = true;

                list.Add(key);
                list.Add(value);
            }

            if (!flag1)
            {
                list.Add("stability");
                list.Add("false");
            }

            if (!flag2)
            {
                list.Add("autoheight");
                list.Add("false");
            }

            if (!flag3)
            {
                list.Add("height");
                list.Add("1.0");
            }

            if (!flag4)
            {
                list.Add("entityowner");
                list.Add("false");
            }

            return list;
        }

        private float DegreeToRadian(float angle)
        {
            return angle.Equals(0f) ? 0f : (float)(Math.PI * angle / 180.0f);
        }

        private void OnPasteFinished(List<BaseEntity> pastedEntities, string fileName)
        {
            if (pastedEntities == null || pastedEntities.Count == 0 || !Buildings.IsValid(fileName))
            {
                return;
            }

            Timer t = null;
            int repeat = 120;

            t = timer.Repeat(1f, 0, () =>
            {
                if (--repeat <= 1)
                {
                    if (IsLoadingMaintainedEvent) ResetLoadingType(RaidableType.Maintained);
                    if (IsLoadingScheduledEvent) ResetLoadingType(RaidableType.Scheduled);
                    return;
                }

                if (IsUnloading)
                {
                    return;
                }

                pastedEntities.RemoveAll(e => e == null || e.IsDestroyed);

                var raid = RaidableBase.Get(pastedEntities);

                if (raid == null)
                {
                    return;
                }

                int baseIndex = 0;

                while (Bases.ContainsKey(baseIndex) || Indices.ContainsKey(baseIndex))
                {
                    baseIndex = UnityEngine.Random.Range(1, 9999999);
                }

                Indices[baseIndex] = raid;
                Bases[baseIndex] = pastedEntities;

                raid.SetEntities(baseIndex, pastedEntities);
                t.Destroy();
            });
        }

        private void UndoPaste(int baseIndex, Vector3 position, float radius, List<BaseEntity> entities)
        {
            float num = 0f;

            if (!_config.Settings.Management.DespawnMounts)
            {
                entities.RemoveAll(e => e is BaseMountable && !e.IsDestroyed && ((e as BaseMountable)?.GetMounted() != null || !InRange(e.transform.position, position, radius)));
            }

            float time = Mathf.Clamp(_config.Settings.Management.InvokeTime, 0.001f, 0.25f);

            for (int i = entities.Count - 1; i >= 0; i--)
            {
                var e = entities[i];

                if (e != null && !e.IsDestroyed)
                {
                    e.Invoke(() =>
                    {
                        if (!e.IsDestroyed)
                        {
                            e.KillMessage();
                        }
                    }, num += time);
                }

                RaidEntities.Remove(e);
            }

            entities.Clear();
            Bases.Remove(baseIndex);
            Indices.Remove(baseIndex);

            if (Bases.Count == 0)
            {
                UnsubscribeHooks();
            }

            Interface.CallHook("OnRaidableBaseDespawned", position);
        }

        private IEnumerator UndoRoutine_OLD(int baseIndex, Vector3 position, List<BaseEntity> entities)
        {
            int total = 0;
            int batchLimit = Mathf.Clamp(_config.Settings.BatchLimit, 1, 15);
            var _instruction = ConVar.FPS.limit > 80 ? CoroutineEx.waitForSeconds(Constants.INSTRUCTION_TIME) : null;

            if (!_config.Settings.Management.DespawnMounts)
            {
                entities.RemoveAll(e => e is BaseMountable);
            }

            for (int i = entities.Count - 1; i >= 0; i--)
            {
                var e = entities[i];

                if (e != null && !e.IsDestroyed)
                {
                    e.Kill();
                }

                RaidEntities.Remove(e);

                if (++total >= batchLimit)
                {
                    total = 0;
                    yield return _instruction;
                }
            }

            entities.Clear();
            Bases.Remove(baseIndex);
            Indices.Remove(baseIndex);

            if (Bases.Count == 0)
            {
                UnsubscribeHooks();
            }

            Interface.CallHook("OnRaidableBaseDespawned", position);
        }

        private static List<Vector3> GetCircumferencePositions(Vector3 center, float radius, float next, bool spawnHeight, float y = 0f)
        {
            var positions = new List<Vector3>();

            if (next < 1f)
            {
                next = 1f;
            }

            float angle = 0f;
            float angleInRadians = 2 * (float)Math.PI;

            while (angle < 360)
            {
                float radian = (angleInRadians / 360) * angle;
                float x = center.x + radius * (float)Math.Cos(radian);
                float z = center.z + radius * (float)Math.Sin(radian);
                var a = new Vector3(x, 0f, z);

                a.y = y == 0f ? spawnHeight ? GetSpawnHeight(a) : TerrainMeta.HeightMap.GetHeight(a) : y;

                if (a.y < -48f)
                {
                    a.y = -48f;
                }

                positions.Add(a);
                angle += next;
            }

            return positions;
        }

        private Elevation GetTerrainElevation(Vector3 center)
        {
            float maxY = -1000;
            float minY = 1000;

            foreach (var position in GetCircumferencePositions(center, 20f, 30f, true, 0f)) // 70 to 30 in 1.5.1
            {
                if (position.y > maxY) maxY = position.y;
                if (position.y < minY) minY = position.y;
            }

            return new Elevation
            {
                Min = minY,
                Max = maxY
            };
        }

        private bool IsFlatTerrain(Vector3 center, Elevation elevation, float value)
        {
            return elevation.Max - elevation.Min <= value && elevation.Max - center.y <= value;
        }

        private float GetMonumentFloat(string monumentName)
        {
            switch (monumentName)
            {
                case "Abandoned Cabins":
                    return 54f;
                case "Abandoned Supermarket":
                    return 50f;
                case "Airfield":
                    return 200f;
                case "Barn":
                case "Large Barn":
                    return 75f;
                case "Fishing Village":
                case "Large Fishing Village":
                    return 50f;
                case "Bandit Camp":
                    return 125f;
                case "Junk Yard":
                    return 125f;
                case "Giant Excavator Pit":
                    return 225f;
                case "Harbor":
                    return 150f;
                case "HQM Quarry":
                    return 37.5f;
                case "Large Oil Rig":
                    return 200f;
                case "Launch Site":
                    return 300f;
                case "Lighthouse":
                    return 48f;
                case "Military Tunnel":
                    return 100f;
                case "Mining Outpost":
                    return 45f;
                case "Oil Rig":
                    return 100f;
                case "Outpost":
                    return 250f;
                case "Oxum's Gas Station":
                    return 65f;
                case "Power Plant":
                    return 140f;
                case "power_sub_small_1":
                case "power_sub_small_2":
                case "power_sub_big_1":
                case "power_sub_big_2":
                    return 30f;
                case "Ranch":
                    return 75f;
                case "Satellite Dish":
                    return 90f;
                case "Sewer Branch":
                    return 100f;
                case "Stone Quarry":
                    return 27.5f;
                case "Sulfur Quarry":
                    return 27.5f;
                case "The Dome":
                    return 70f;
                case "Train Yard":
                    return 150f;
                case "Water Treatment Plant":
                    return 185f;
                case "Water Well":
                    return 24f;
                case "Wild Swamp":
                    return 24f;
            }

            return 100f;
        }

        private Vector3 GetEventPosition(BuildingOptions options, bool checkTerrain, RaidableSpawns rs, RaidableType type, out string message)
        {
            message = string.Empty;

            rs.Check();

            int num1 = 0;
            int attempts = 1000;
            int layers = Layers.Mask.Player_Server | Layers.Mask.Construction | Layers.Mask.Deployed;
            float buildRadius = Mathf.Max(_config.Settings.Management.CupboardDetectionRadius, options.ArenaWalls.Radius, options.ProtectionRadius) + 5f;
            float safeRadius = Mathf.Max(options.ArenaWalls.Radius, options.ProtectionRadius);
            float distance = GetDistance(type);
            CacheType cacheType;

            while (rs.Count > 0 && --attempts > 0)
            {
                var rsl = rs.GetRandom();

                if (type == RaidableType.Maintained && _config.Settings.Maintained.Ignore)
                {
                    return rsl.Location;
                }

                if (type == RaidableType.Scheduled && _config.Settings.Schedule.Ignore)
                {
                    return rsl.Location;
                }

                if (distance > 0 && RaidableBase.IsTooClose(rsl.Location, distance))
                {
                    message = "Too close to another raidable base";
                    continue;
                }

                var vector = HasBuildingPrivilege(rsl.Location, buildRadius);

                if (vector != Vector3.zero)
                {
                    message = $"Blocked by Player Cupboard Detection Radius {vector}";

                    if (rs.IsCustomSpawn) continue;

                    rs.RemoveNear(rsl.Location, buildRadius, CacheType.Privilege, type);

                    continue;
                }

                if (!IsAreaSafe(ref rsl.Location, safeRadius, layers, out cacheType, out message, type))
                {
                    if (rs.IsCustomSpawn) continue;

                    if (cacheType == CacheType.Delete)
                    {
                        rs.Remove(rsl, cacheType);
                    }
                    else rs.RemoveNear(rsl.Location, safeRadius, cacheType, type);

                    continue;
                }

                /*if (!rsl.AutoHeight)
                {
                    return rsl.Location;
                }

                if (!_config.Settings.Management.Submerged || _config.Settings.Management.SubmergedAreaCheck)
                {
                    return new Vector3(rsl.Location.x, Mathf.Max(rsl.Location.y, rsl.SpawnHeight), rsl.Location.z);
                }*/

                return rsl.Location;
            }

            rs.TryAddRange();

            return Vector3.zero;
        }

        private Vector3 SpawnRandomBase(out string message, RaidableType type, RaidableMode mode, string id = null, string baseName = null, bool isAdmin = false)
        {
            lastSpawnRequestTime = Time.realtimeSinceStartup;
            message = string.Empty;

            var profile = GetBuilding(type, mode, baseName);
            bool flag = IsProfileValid(profile);
            bool checkTerrain;
            var rs = GetSpawns(type, out checkTerrain);

            if (flag)
            {
                if (rs != null)
                {
                    var eventPos = GetEventPosition(profile.Value.Options, checkTerrain, rs, type, out message);

                    if (eventPos != Vector3.zero)
                    {
                        PasteBuilding(type, eventPos, profile, rs, mode);
                        return eventPos;
                    }
                }
            }

            if (type == RaidableType.Maintained || type == RaidableType.Scheduled)
            {
                if (!PrintDebugMessage(message) && rs != null)
                {
                    rs.TryAddRange(CacheType.Delete, true);
                    rs.TryAddRange(CacheType.Privilege, true);
                    rs.TryAddRange(CacheType.Temporary, true);
                }

                return Vector3.zero;
            }

            var debug = GetDebugMessage(mode, flag, isAdmin, id, baseName, profile.Value?.Options);

            if (!string.IsNullOrEmpty(message))
            {
                message = debug + ": " + message;
            }
            else message = debug;

            return Vector3.zero;
        }

        private bool PrintDebugMessage(string message)
        {
            if (debugMode && !string.IsNullOrEmpty(message))
            {
                Puts("DEBUG: {0}", message);

                return true;
            }

            return false;
        }

        private string GetDebugMessage(RaidableMode mode, bool flag, bool isAdmin, string id, string baseName, BuildingOptions options)
        {
            if (options != null)
            {
                if (!options.Enabled)
                {
                    return Backbone.GetMessage("Profile Not Enabled", id, baseName);
                }
            }

            if (!flag)
            {
                if (!string.IsNullOrEmpty(baseName))
                {
                    if (!FileExists(baseName))
                    {
                        return Backbone.GetMessage("FileDoesNotExist", id);
                    }
                    else if (!Buildings.Profiles.ContainsKey(baseName))
                    {
                        return Backbone.GetMessage("BuildingNotConfigured", id);
                    }
                }

                if (mode == RaidableMode.Random)
                {
                    return Backbone.GetMessage("NoValidBuildingsConfigured", id);
                }
                else return Backbone.GetMessage("NoValidBuildingsConfigured", id);
            }

            return Backbone.GetMessage("CannotFindPosition", id);
        }

        private RaidableSpawns GetSpawns(RaidableType type, out bool checkTerrain)
        {
            RaidableSpawns spawns;

            switch (type)
            {
                case RaidableType.Maintained:
                    {
                        if (raidSpawns.TryGetValue(RaidableType.Maintained, out spawns))
                        {
                            checkTerrain = false;
                            return spawns;
                        }
                        break;
                    }
                case RaidableType.Manual:
                    {
                        if (raidSpawns.TryGetValue(RaidableType.Manual, out spawns))
                        {
                            checkTerrain = false;
                            return spawns;
                        }
                        break;
                    }
                case RaidableType.Scheduled:
                    {
                        if (raidSpawns.TryGetValue(RaidableType.Scheduled, out spawns))
                        {
                            checkTerrain = false;
                            return spawns;
                        }
                        break;
                    }
            }

            checkTerrain = true;
            return raidSpawns.TryGetValue(RaidableType.Grid, out spawns) ? spawns : null;
        }

        private KeyValuePair<string, BaseProfile> GetBuilding(RaidableType type, RaidableMode mode, string baseName)
        {
            var list = new List<KeyValuePair<string, BaseProfile>>();
            bool isBaseNull = string.IsNullOrEmpty(baseName);

            foreach (var profile in Buildings.Profiles)
            {
                if (MustExclude(type, profile.Value.Options.AllowPVP) || !IsBuildingAllowed(type, mode, RaidableMode.Normal, profile.Value.Options.AllowPVP))
                {
                    continue;
                }

                if (FileExists(profile.Key) && Cycle.CanSpawn(type, mode, profile.Key))
                {
                    if (isBaseNull)
                    {
                        list.Add(profile);
                    }
                    else if (profile.Key.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                    {
                        return profile;
                    }
                }

                foreach (var extra in profile.Value.Options.AdditionalBases)
                {
                    if (!FileExists(extra.Key) || !Cycle.CanSpawn(type, mode, extra.Key))
                    {
                        continue;
                    }

                    var clone = BaseProfile.Clone(profile.Value);
                    var kvp = new KeyValuePair<string, BaseProfile>(extra.Key, clone);

                    kvp.Value.Options.PasteOptions = new List<PasteOption>(extra.Value);

                    if (isBaseNull)
                    {
                        list.Add(kvp);
                    }
                    else if (extra.Key.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                    {
                        return kvp;
                    }
                }
            }

            if (list.Count == 0)
            {
                return default(KeyValuePair<string, BaseProfile>);
            }

            var random = list.GetRandom();

            return random;
        }

        private static bool IsProfileValid(KeyValuePair<string, BaseProfile> profile)
        {
            if (string.IsNullOrEmpty(profile.Key) || profile.Value == null || profile.Value.Options == null)
            {
                return false;
            }

            return true;
        }

        private static bool FileExists(string file)
        {
            if (!file.Contains(Path.DirectorySeparatorChar))
            {
                return Interface.Oxide.DataFileSystem.ExistsDatafile($"copypaste{Path.DirectorySeparatorChar}{file}");
            }

            return Interface.Oxide.DataFileSystem.ExistsDatafile(file);
        }

        private static bool IsBuildingAllowed(RaidableType type, RaidableMode requestedMode, RaidableMode buildingMode, bool allowPVP)
        {
            if (requestedMode != RaidableMode.Random && buildingMode != requestedMode)
            {
                return false;
            }

            return true;
        }

        #endregion

        #region Commands

        private void CommandReloadConfig(IPlayer p, string command, string[] args)
        {
            if (p.IsServer || (p.Object as BasePlayer).IsAdmin)
            {
                if (!IsPasteAvailable)
                {
                    p.Reply(Backbone.GetMessage("PasteOnCooldown", p.Id));
                    return;
                }

                p.Reply(Backbone.GetMessage("ReloadConfig", p.Id));
                LoadConfig();
                maintainEnabled = _config.Settings.Maintained.Enabled;
                scheduleEnabled = _config.Settings.Schedule.Enabled;

                if (maintainCoroutine != null)
                {
                    StopMaintainCoroutine();
                    p.Reply(Backbone.GetMessage("ReloadMaintainCo", p.Id));
                }

                if (scheduleCoroutine != null)
                {
                    StopScheduleCoroutine();
                    p.Reply(Backbone.GetMessage("ReloadScheduleCo", p.Id));
                }

                p.Reply(Backbone.GetMessage("ReloadInit", p.Id));
                Initialize();
            }
        }

        private void Initialize()
        {
            Reinitialize();
            BlockZoneManagerZones();
            LoadSpawns();
            SetupGrid();
            CreateDefaultFiles();
            LoadTables();
            LoadProfiles();
            Backbone.InitializeSkins();
        }

        private void CommandRaidHunter(IPlayer p, string command, string[] args)
        {
            var player = p.Object as BasePlayer;
            bool isAdmin = p.IsServer || player.IsAdmin;
            string arg = args.Length >= 1 ? args[0].ToLower() : string.Empty;

            switch (arg)
            {
                case "version":
                    {
                        p.Reply(Version.ToString());
                        return;
                    }
                case "resettotal":
                    {
                        if (isAdmin)
                        {
                            foreach (var entry in storedData.Players)
                            {
                                entry.Value.TotalRaids = 0;
                            }

                            SaveData();
                        }

                        return;
                    }
                case "resettime":
                    {
                        if (isAdmin)
                        {
                            storedData.RaidTime = DateTime.MinValue.ToString();
                            SaveData();
                        }

                        return;
                    }
                case "wipe":
                    {
                        if (isAdmin)
                        {
                            wiped = true;
                            CheckForWipe();
                        }

                        return;
                    }
                case "ignore_restart":
                    {
                        if (isAdmin)
                        {
                            bypassRestarting = !bypassRestarting;
                            p.Reply($"Bypassing restart check: {bypassRestarting}");
                        }

                        return;
                    }
                case "grid?":
                    {
                        if (player.IsValid())
                        {
                            p.Reply(PositionToGrid(player.transform.position));
                        }

                        return;
                    }
                case "grid":
                    {
                        if (player.IsValid() && (isAdmin || HasPermission(player, drawPermission)))
                        {
                            ShowGrid(player);
                        }

                        return;
                    }
                case "ui":
                    {
                        CommandUI(p, command, args.Skip(1).ToArray());
                        return;
                    }
                case "ladder":
                case "lifetime":
                    {
                        ShowLadder(p, args);
                        return;
                    }
            }

            if (_config.UI.Enabled)
            {
                p.Reply(Backbone.GetMessage("UIHelpText", p.Id, command));
            }

            if (_config.RankedLadder.Enabled)
            {
                p.Reply(Backbone.GetMessage("Wins", p.Id, storedData.Players.ContainsKey(p.Id) ? storedData.Players[p.Id].Raids : 0, _config.Settings.HunterCommand));
            }

            if (Raids.Count == 0 && scheduleEnabled)
            {
                ShowNextScheduledEvent(p);
                return;
            }

            if (!player.IsValid())
            {
                return;
            }

            DrawRaidLocations(player, isAdmin || HasPermission(player, drawPermission));
        }

        protected void DrawRaidLocations(BasePlayer player, bool hasPerm)
        {
            foreach (var raid in Raids.Values)
            {
                if (InRange(raid.Location, player.transform.position, 100f))
                {
                    Player.Message(player, string.Format("{0} @ {1} ({2})", raid.BaseName, raid.Location, PositionToGrid(raid.Location)));
                }
            }

            if (!hasPerm)
            {
                return;
            }

            bool isAdmin = player.IsAdmin;

            try
            {
                if (!isAdmin)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                    player.SendNetworkUpdateImmediate();
                }

                foreach (var raid in Raids.Values)
                {
                    int num = 0;

                    foreach (var t in BasePlayer.activePlayerList)
                    {
                        if (IsValid(t) && t.Distance(raid.Location) <= raid.Options.ProtectionRadius * 3f)
                        {
                            num++;
                        }
                    }

                    int distance = Mathf.CeilToInt(Vector3.Distance(player.transform.position, raid.Location));
                    string message = string.Format(lang.GetMessage("RaidMessage", this, player.UserIDString), distance, num);
                    string flag = Backbone.GetMessage(raid.AllowPVP ? "PVPFlag" : "PVEFlag", player.UserIDString);

                    player.SendConsoleCommand("ddraw.text", 15f, Color.yellow, raid.Location, string.Format("{0} : {1}{2} {3}", raid.BaseName, flag, raid.Mode(), message));

                    foreach (var target in raid.friends)
                    {
                        if (IsValid(target))
                        {
                            player.SendConsoleCommand("ddraw.text", 15f, Color.yellow, target.transform.position, "Ally");
                        }
                    }

                    if (IsValid(raid.owner))
                    {
                        player.SendConsoleCommand("ddraw.text", 15f, Color.yellow, raid.owner.transform.position, "Owner");
                    }
                }
            }
            catch (Exception ex)
            {
                Puts(ex.StackTrace);
                Puts(ex.Message);
            }
            finally
            {
                if (!isAdmin)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                    player.SendNetworkUpdateImmediate();
                }
            }
        }

        protected void ShowNextScheduledEvent(IPlayer p)
        {
            string message;
            double time = GetRaidTime();

            if (BasePlayer.activePlayerList.Count < _config.Settings.Schedule.PlayerLimit)
            {
                message = Backbone.GetMessage("Not Enough Online", p.Id, _config.Settings.Schedule.PlayerLimit);
            }
            else message = FormatTime(time);

            p.Reply(Backbone.GetMessage("Next", p.Id, message));
        }

        protected void ShowLadder(IPlayer p, string[] args)
        {
            if (!_config.RankedLadder.Enabled)
            {
                return;
            }

            if (storedData.Players.Count == 0)
            {
                p.Reply(Backbone.GetMessage("Ladder Insufficient Players", p.Id));
                return;
            }

            if (args.Length == 2 && args[1].ToLower() == "resetme" && storedData.Players.ContainsKey(p.Id))
            {
                storedData.Players[p.Id].Raids = 0;
                return;
            }

            string key = args[0].ToLower();
            var ladder = GetLadder(key);
            int rank = 0;

            ladder.Sort((x, y) => y.Value.CompareTo(x.Value));

            p.Reply(Backbone.GetMessage(key == "ladder" ? "Ladder" : "Ladder Total", p.Id));

            foreach (var kvp in ladder.Take(10))
            {
                NotifyPlayer(p, rank, kvp);
            }

            ladder.Clear();
        }

        protected void ShowGrid(BasePlayer player)
        {
            bool isAdmin = player.IsAdmin;

            try
            {
                if (!isAdmin)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                    player.SendNetworkUpdateImmediate();
                }

                RaidableSpawns rs;
                if (!raidSpawns.TryGetValue(RaidableType.Grid, out rs))
                {
                    return;
                }

                foreach (var rsl in rs.Active)
                {
                    if (InRange(rsl.Location, player.transform.position, 1000f))
                    {
                        player.SendConsoleCommand("ddraw.text", 30f, Color.green, rsl.Location, "X");
                    }
                }

                foreach (CacheType cacheType in Enum.GetValues(typeof(CacheType)))
                {
                    var color = cacheType == CacheType.Generic ? Color.red : cacheType == CacheType.Temporary ? Color.cyan : cacheType == CacheType.Privilege ? Color.yellow : Color.blue;
                    var text = cacheType == CacheType.Generic ? "X" : cacheType == CacheType.Temporary ? "C" : cacheType == CacheType.Privilege ? "TC" : "W";

                    foreach (var rsl in rs.Inactive(cacheType))
                    {
                        if (InRange(rsl.Location, player.transform.position, 1000f))
                        {
                            player.SendConsoleCommand("ddraw.text", 30f, color, rsl.Location, text);
                        }
                    }
                }

                foreach (var entry in _rocks)
                {
                    if (InRange(entry.Key, player.transform.position, 1000f))
                    {
                        player.SendConsoleCommand("ddraw.text", 30f, Color.magenta, entry.Key, $"R:{entry.Value}");
                    }
                }

                foreach (var monument in monuments)
                {
                    string text = monument.Key.displayPhrase.translated;

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        text = GetMonumentName(monument);
                    }

                    player.SendConsoleCommand("ddraw.sphere", 30f, Color.blue, monument.Key.transform.position, monument.Value);
                    player.SendConsoleCommand("ddraw.text", 30f, Color.cyan, monument.Key.transform.position, text);
                }
            }
            catch (Exception ex)
            {
                Puts(ex.StackTrace);
                Puts(ex.Message);
            }
            finally
            {
                if (!isAdmin)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                    player.SendNetworkUpdateImmediate();
                }
            }
        }

        protected List<KeyValuePair<string, int>> GetLadder(string arg)
        {
            var ladder = new List<KeyValuePair<string, int>>();
            bool isLadder = arg.ToLower() == "ladder";

            foreach (var entry in storedData.Players)
            {
                int value = isLadder ? entry.Value.Raids : entry.Value.TotalRaids;

                if (value > 0)
                {
                    ladder.Add(new KeyValuePair<string, int>(entry.Key, value));
                }
            }

            return ladder;
        }

        private void NotifyPlayer(IPlayer p, int rank, KeyValuePair<string, int> kvp)
        {
            string name = covalence.Players.FindPlayerById(kvp.Key)?.Name ?? kvp.Key;
            string value = kvp.Value.ToString("N0");
            string message = lang.GetMessage("NotifyPlayerMessageFormat", this, p.Id);

            message = message.Replace("{rank}", rank.ToString());
            message = message.Replace("{name}", name);
            message = message.Replace("{value}", value);

            p.Reply(message);
        }

        protected string GetMonumentName(KeyValuePair<MonumentInfo, float> monument)
        {
            string text;
            if (monument.Key.name.Contains("Oilrig")) text = "Oil Rig";
            else if (monument.Key.name.Contains("cave")) text = "Cave";
            else if (monument.Key.name.Contains("power_sub")) text = "Power Sub Station";
            else text = "Unknown Monument";
            return text;
        }

        private void CommandRaidBase(IPlayer p, string command, string[] args)
        {
            var player = p.Object as BasePlayer;
            bool isAllowed = p.IsServer || player.IsAdmin || p.HasPermission(adminPermission);

            if (!CanCommandContinue(player, p, args, isAllowed))
            {
                return;
            }

            if (command == _config.Settings.EventCommand) // rbe
            {
                ProcessEventCommand(player, p, args, isAllowed);
            }
            else if (command == _config.Settings.ConsoleCommand) // rbevent
            {
                ProcessConsoleCommand(p, args, isAllowed);
            }
        }

        protected void ProcessEventCommand(BasePlayer player, IPlayer p, string[] args, bool isAllowed) // rbe
        {
            if (!isAllowed || !player.IsValid())
            {
                return;
            }

            string baseName = null;

            if (args.Length > 0)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.IsNullOrEmpty(baseName) && FileExists(args[i])) baseName = args[i];
                }
            }

            var profile = GetBuilding(RaidableType.Manual, RaidableMode.Normal, baseName);

            if (IsProfileValid(profile))
            {
                RaycastHit hit;
                int layers = Layers.Mask.Construction | Layers.Mask.Default | Layers.Mask.Deployed | Layers.Mask.Tree | Layers.Mask.Terrain | Layers.Mask.Water | Layers.Mask.World;
                if (Physics.Raycast(player.eyes.HeadRay(), out hit, isAllowed ? Mathf.Infinity : 100f, layers, QueryTriggerInteraction.Ignore))
                {
                    CacheType cacheType;
                    string message = null;
                    var position = hit.point;
                    int layers2 = Layers.Mask.Player_Server | Layers.Mask.Construction | Layers.Mask.Deployed;
                    var safe = player.IsAdmin || IsAreaSafe(ref position, Mathf.Max(Constants.RADIUS * 2f, profile.Value.Options.ArenaWalls.Radius), layers2, out cacheType, out message, RaidableType.Manual);

                    if (!safe && !player.IsFlying && InRange(player.transform.position, position, 50f, false))
                    {
                        p.Reply(Backbone.GetMessage("PasteIsBlockedStandAway", p.Id));
                        return;
                    }

                    if (safe && (isAllowed || !IsMonumentPosition(position)))
                    {
                        var rs = raidSpawns.FirstOrDefault(x => x.Value.Spawns.Any(y => InRange(y.Location, hit.point, Constants.RADIUS))).Value;
                        PasteBuilding(RaidableType.Manual, hit.point, profile, rs, RaidableMode.Normal);
                        if (player.IsAdmin) player.SendConsoleCommand("ddraw.text", 10f, Color.red, hit.point, "XXX");
                    }
                    else p.Reply(Backbone.GetMessage("PasteIsBlocked", p.Id));

                    if (!string.IsNullOrEmpty(message))
                    {
                        p.Reply(message);
                    }
                }
                else p.Reply(Backbone.GetMessage("LookElsewhere", p.Id));
            }
            else
            {
                if (profile.Value == null)
                {
                    p.Reply(Backbone.GetMessage("BuildingNotConfigured", p.Id));
                }
                else p.Reply(GetDebugMessage(RaidableMode.Normal, false, true, p.Id, profile.Key, profile.Value.Options));
            }
        }

        protected void ProcessConsoleCommand(IPlayer p, string[] args, bool isAdmin) // rbevent
        {
            if (IsGridLoading && !p.IsAdmin)
            {
                int count = raidSpawns.ContainsKey(RaidableType.Grid) ? raidSpawns[RaidableType.Grid].Count : 0;
                p.Reply(Backbone.GetMessage("GridIsLoadingFormatted", p.Id, (Time.realtimeSinceStartup - gridTime).ToString("N02"), count));
            }

            RaidableMode mode = RaidableMode.Random;
            string baseName = null;

            if (args.Length > 0)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.IsNullOrEmpty(baseName) && FileExists(args[i])) baseName = args[i];
                }
            }

            string message;
            var position = SpawnRandomBase(out message, RaidableType.Manual, mode, p.Id, baseName, isAdmin);

            if (position == Vector3.zero)
            {
                p.Reply(message);
            }
            else if (isAdmin && p.IsConnected)
            {
                p.Teleport(position.x, position.y, position.z);
            }
        }

        private bool CanCommandContinue(BasePlayer player, IPlayer p, string[] args, bool isAllowed)
        {
            if (HandledCommandArguments(player, p, isAllowed, args))
            {
                return false;
            }

            if (CopyPaste == null || !CopyPaste.IsLoaded)
            {
                p.Reply(Backbone.GetMessage("LoadCopyPaste", p.Id));
                return false;
            }

            if (!isAllowed && RaidableBase.Get(RaidableType.Manual) >= _config.Settings.Manual.Max)
            {
                p.Reply(Backbone.GetMessage("Max Manual Events", p.Id, _config.Settings.Manual.Max));
                return false;
            }

            if (!IsPasteAvailable && !p.IsAdmin)
            {
                p.Reply(Backbone.GetMessage("PasteOnCooldown", p.Id));
                return false;
            }

            if (!p.IsAdmin && IsSpawnOnCooldown())
            {
                p.Reply(Backbone.GetMessage("SpawnOnCooldown", p.Id));
                return false;
            }

            if (!isAllowed && BaseNetworkable.serverEntities.Count > 300000)
            {
                p.Reply(lang.GetMessage("EntityCountMax", this, p.Id));
                return false;
            }

            return true;
        }

        private bool HandledCommandArguments(BasePlayer player, IPlayer p, bool isAllowed, string[] args)
        {
            if (args.Length == 0)
            {
                return false;
            }

            if (player.IsValid())
            {
                if (args[0].ToLower() == "despawn")
                {
                    if (isAllowed)
                    {
                        bool success = DespawnBase(player);
                        Backbone.Message(player, success ? "DespawnBaseSuccess" : "DespawnBaseNoneAvailable");
                        if (success) Puts(Backbone.GetMessage("DespawnedAt", null, player.displayName, FormatGridReference(player.transform.position)));
                        return true;
                    }
                }

                if (!HasPermission(player, drawPermission) && !isAllowed)
                {
                    return false;
                }

                if (args[0].ToLower() == "draw")
                {
                    bool isAdmin = player.IsAdmin;

                    try
                    {
                        if (!isAdmin)
                        {
                            player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                            player.SendNetworkUpdateImmediate();
                        }

                        foreach (var raid in Raids.Values)
                        {
                            player.SendConsoleCommand("ddraw.sphere", 30f, Color.blue, raid.Location, raid.Options.ProtectionRadius);
                        }
                    }
                    catch (Exception ex)
                    {
                        Puts(ex.StackTrace);
                        Puts(ex.Message);
                    }
                    finally
                    {
                        if (!isAdmin)
                        {
                            player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                            player.SendNetworkUpdateImmediate();
                        }
                    }

                    return true;
                }
            }

            if (!isAllowed)
            {
                return false;
            }

            switch (args[0].ToLower())
            {
                case "debug":
                    {
                        debugMode = !debugMode;
                        p.Reply(string.Format("Debug mode: {0}", debugMode));
                        p.Reply(string.Format("Scheduled Events Running: {0}", scheduleCoroutine != null));
                        p.Reply(string.Format("Maintained Events Running: {0}", maintainCoroutine != null));
                        return true;
                    }
                case "type":
                    {
                        List<string> list;

                        foreach (RaidableType type in Enum.GetValues(typeof(RaidableType)))
                        {
                            list = new List<string>();

                            foreach (var raid in Raids.Values)
                            {
                                if (raid.Type != type)
                                {
                                    continue;
                                }

                                list.Add(PositionToGrid(raid.Location));
                            }

                            if (list.Count == 0) continue;
                            p.Reply(string.Format("{0} : {1} @ {2}", type.ToString(), RaidableBase.Get(type), string.Join(", ", list.ToArray())));
                        }

                        return true;
                    }
                case "mode":
                    {
                        List<string> list;

                        foreach (RaidableMode mode in Enum.GetValues(typeof(RaidableMode)))
                        {
                            if (mode == RaidableMode.Disabled) continue;
                            list = new List<string>();

                            foreach (var raid in Raids.Values)
                            {
                                list.Add(PositionToGrid(raid.Location));
                            }

                            if (list.Count == 0) continue;
                            p.Reply(string.Format("{0} : {1} @ {2}", mode.ToString(), RaidableBase.Get(mode), string.Join(", ", list.ToArray())));
                        }

                        return true;
                    }
                case "resetmarkers":
                    {
                        RemoveAllThirdPartyMarkers();

                        foreach (var raid in Raids.Values)
                        {
                            raid.RemoveMapMarkers();
                            raid.UpdateMarker();
                        }

                        return true;
                    }
                case "randomly":
                    {
                        if (IsValid(player))
                        {
                            var raid = GetNearestBase(player.transform.position);

                            if (raid == null)
                            {
                                return true;
                            }

                            raid.SpawnRandomly();
                            return true;
                        }

                        break;
                    }
                case "despawnall":
                case "despawn_inactive":
                    {
                        if (Raids.Count > 0)
                        {
                            DespawnAllBasesNow(args[0].ToLower() == "despawn_inactive");
                            Puts(Backbone.GetMessage("DespawnedAll", null, player?.displayName ?? p.Id));
                        }

                        return true;
                    }
                case "active":
                    {
                        int count = 0;

                        foreach (var raid in Raids.Values)
                        {
                            if (raid.intruders.Count > 0 || raid.ownerId.IsSteamId())
                            {
                                count++;
                            }
                        }

                        Puts("{0} active raids", count);
                        return true;
                    }
                case "setowner":
                case "lockraid":
                    {
                        if (args.Length >= 2)
                        {
                            var target = RustCore.FindPlayer(args[1]);

                            if (IsValid(target))
                            {
                                var raid = GetNearestBase(target.transform.position);

                                if (raid == null)
                                {
                                    p.Reply(Backbone.GetMessage("TargetTooFar", p.Id));
                                }
                                else
                                {
                                    raid.SetOwner(target);
                                    p.Reply(Backbone.GetMessage("RaidLockedTo", p.Id, target.displayName));
                                }
                            }
                            else p.Reply(Backbone.GetMessage("TargetNotFoundId", p.Id, args[1]));
                        }

                        return true;
                    }
                case "clearowner":
                    {
                        if (!player.IsValid()) return true;

                        var raid = GetNearestBase(player.transform.position);

                        if (raid == null)
                        {
                            p.Reply(Backbone.GetMessage("TooFar", p.Id));
                        }
                        else
                        {
                            raid.owner = null;
                            raid.ownerId = 0;
                            raid.friends.Clear();
                            raid.UpdateMarker();
                            p.Reply(Backbone.GetMessage("RaidOwnerCleared", p.Id));
                        }

                        return true;
                    }
            }

            return false;
        }

        private void CommandToggle(IPlayer p, string command, string[] args)
        {
            if (_config.Settings.Maintained.Enabled)
            {
                maintainEnabled = !maintainEnabled;
                p.Reply($"Toggled maintained events {(maintainEnabled ? "on" : "off")}");
            }

            if (_config.Settings.Schedule.Enabled)
            {
                scheduleEnabled = !scheduleEnabled;
                p.Reply($"Toggled scheduled events {(scheduleEnabled ? "on" : "off")}");
            }
        }

        private void CommandPopulate(IPlayer p, string command, string[] args)
        {
            var lootList = new List<TreasureItem>();

            foreach (var def in ItemManager.GetItemDefinitions())
            {
                lootList.Add(new TreasureItem
                {
                    shortname = def.shortname
                });
            }

            lootList.Sort((x, y) => x.shortname.CompareTo(y.shortname));

            p.Reply("Created Editable_Lists/Normal.json");

            string file = $"{Name}{Path.DirectorySeparatorChar}Editable_Lists{Path.DirectorySeparatorChar}Normal";
            Interface.Oxide.DataFileSystem.WriteObject(file, lootList);

            SaveConfig();
        }

        private void CommandConfig(IPlayer p, string command, string[] args)
        {
            if (!IsValid(args))
            {
                p.Reply(string.Format(lang.GetMessage("ConfigUseFormat", this, p.Id), string.Join("|", arguments.ToArray())));
                return;
            }

            switch (args[0].ToLower())
            {
                case "add":
                    {
                        ConfigAddBase(p, args);
                        return;
                    }
                case "remove":
                case "clean":
                    {
                        ConfigRemoveBase(p, args);
                        return;
                    }
                case "list":
                    {
                        ConfigListBases(p);
                        return;
                    }
            }
        }

        #endregion Commands

        #region Helpers

        private bool HasPermission(BasePlayer player, string perm)
        {
            return permission.UserHasPermission(player.UserIDString, perm);
        }

        private bool HasPVPDelay(ulong playerId)
        {
            return PvpDelay.ContainsKey(playerId);
        }

        private static bool IsBox(BaseEntity entity, bool inherit)
        {
            if (entity.prefabID == Constants.LARGE_WOODEN_BOX || entity.prefabID == Constants.SMALL_WOODEN_BOX || entity.prefabID == Constants.COFFIN_STORAGE)
            {
                return true;
            }

            return inherit && _config.Settings.Management.Inherit.Any(entity.ShortPrefabName.Contains);
        }

        public static float GetDistance(RaidableType type)
        {
            float distance;

            switch (type)
            {
                case RaidableType.Maintained:
                    {
                        distance = _config.Settings.Maintained.Distance;
                        break;
                    }
                case RaidableType.Scheduled:
                    {
                        distance = _config.Settings.Schedule.Distance;
                        break;
                    }
                case RaidableType.Manual:
                    {
                        distance = _config.Settings.Manual.Distance;
                        break;
                    }
                default:
                    {
                        distance = 100f;
                        break;
                    }
            }

            return distance;
        }

        private bool IsPVE() => TruePVE != null || NextGenPVE != null || Imperium != null;

        private bool IsInvisible(BasePlayer player)
        {
            if (!player || Vanish == null || !Vanish.IsLoaded)
            {
                return false;
            }

            var success = Vanish?.Call("IsInvisible", player);

            return success is bool ? (bool)success : false;
        }

        private static void NullifyDamage(HitInfo hitInfo)
        {
            if (hitInfo == null)
            {
                return;
            }

            hitInfo.damageTypes = new DamageTypeList();
            hitInfo.DidHit = false;
            hitInfo.DoHitEffects = false;
            hitInfo.HitEntity = null;
        }

        public static bool MustExclude(RaidableType type, bool allowPVP)
        {
            if (!_config.Settings.Maintained.IncludePVE && type == RaidableType.Maintained && !allowPVP)
            {
                return true;
            }

            if (!_config.Settings.Maintained.IncludePVP && type == RaidableType.Maintained && allowPVP)
            {
                return true;
            }

            if (!_config.Settings.Schedule.IncludePVE && type == RaidableType.Scheduled && !allowPVP)
            {
                return true;
            }

            if (!_config.Settings.Schedule.IncludePVP && type == RaidableType.Scheduled && allowPVP)
            {
                return true;
            }

            return false;
        }

        private static List<BasePlayer> GetMountedPlayers(BaseMountable m)
        {
            var players = new List<BasePlayer>();

            if (m is BaseVehicle)
            {
                var vehicle = m as BaseVehicle;

                foreach (var mp in vehicle.mountPoints)
                {
                    if (mp.mountable.IsValid() && mp.mountable.GetMounted().IsValid())
                    {
                        players.Add(mp.mountable.GetMounted());
                    }
                }
            }
            else if (m.GetMounted().IsValid())
            {
                players.Add(m.GetMounted());
            }

            return players;
        }

        private bool AnyNpcs()
        {
            foreach (var x in Raids.Values)
            {
                if (x.npcs.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void DestroyComponents()
        {
            foreach (var raid in Raids.Values)
            {
                raid.DestroyFire();
                raid.DestroyInputs();
            }
        }

        private void ConfigAddBase(IPlayer p, string[] args)
        {
            if (args.Length < 2)
            {
                p.Reply(lang.GetMessage("ConfigAddBaseSyntax", this, p.Id));
                return;
            }

            _sb.Length = 0;
            var values = new List<string>(args);
            values.RemoveAt(0);
            string value = values[0];

            p.Reply(string.Format(lang.GetMessage("Adding", this, p.Id), string.Join(" ", values.ToArray())));

            BaseProfile profile;
            if (!Buildings.Profiles.TryGetValue(value, out profile))
            {
                Buildings.Profiles[value] = profile = new BaseProfile();
                _sb.AppendLine(string.Format(lang.GetMessage("AddedPrimaryBase", this, p.Id), value));
                profile.Options.AdditionalBases = new Dictionary<string, List<PasteOption>>();
            }

            if (args.Length >= 3)
            {
                values.RemoveAt(0);

                foreach (string ab in values)
                {
                    if (!profile.Options.AdditionalBases.ContainsKey(ab))
                    {
                        profile.Options.AdditionalBases.Add(ab, DefaultPasteOptions);
                        _sb.AppendLine(string.Format(lang.GetMessage("AddedAdditionalBase", this, p.Id), ab));
                    }
                }
            }

            if (_sb.Length > 0)
            {
                p.Reply(_sb.ToString());
                _sb.Length = 0;
                profile.Options.Enabled = true;
                SaveProfile(value, profile.Options);
                Buildings.Profiles[value] = profile;
            }
            else p.Reply(lang.GetMessage("EntryAlreadyExists", this, p.Id));

            values.Clear();
        }

        private void ConfigRemoveBase(IPlayer p, string[] args)
        {
            string arg = args[0].ToLower();

            if (arg == "clean")
            {
                var clean = new List<string>();

                foreach (var x in Buildings.Profiles)
                {
                    foreach (var y in x.Value.Options.AdditionalBases)
                    {
                        if (!FileExists(y.Key))
                        {
                            clean.Add(y.Key);
                        }
                    }
                }

                args = clean.ToArray();
            }

            if (args.Length < 2)
            {
                p.Reply(lang.GetMessage("RemoveSyntax", this, p.Id));
                return;
            }

            int num = 0;
            var profiles = new Dictionary<string, BaseProfile>(Buildings.Profiles);
            var files = string.Join(" ", arg == "remove" ? args.Skip(1) : args);
            files = files.Replace(", ", " ");

            _sb.Length = 0;
            _sb.AppendLine(string.Format(lang.GetMessage("RemovingAllBasesFor", this, p.Id), string.Join(" ", files)));

            foreach (var profile in profiles)
            {
                var list = new List<KeyValuePair<string, List<PasteOption>>>(profile.Value.Options.AdditionalBases);

                foreach (string value in files.Split(' '))
                {
                    foreach (var ab in list)
                    {
                        if (ab.Key == value || profile.Key == value)
                        {
                            _sb.AppendLine(string.Format(lang.GetMessage("RemovedAdditionalBase", this, p.Id), ab.Key, profile.Key));
                            profile.Value.Options.AdditionalBases.Remove(ab.Key);
                            num++;
                            SaveProfile(profile.Key, profile.Value.Options);
                        }
                    }

                    if (profile.Key == value)
                    {
                        _sb.AppendLine(string.Format(lang.GetMessage("RemovedPrimaryBase", this, p.Id), value));
                        Buildings.Profiles.Remove(profile.Key);
                        profile.Value.Options.Enabled = false;
                        num++;
                        SaveProfile(profile.Key, profile.Value.Options);
                    }
                }

                list.Clear();
            }

            _sb.AppendLine(string.Format(lang.GetMessage("RemovedEntries", this, p.Id), num));
            p.Reply(_sb.ToString());
            _sb.Length = 0;
        }

        private void ConfigListBases(IPlayer p)
        {
            _sb.Length = 0;
            _sb.Append(lang.GetMessage("ListingAll", this, p.Id));
            _sb.AppendLine();

            bool validBase = false;

            foreach (var entry in Buildings.Profiles)
            {
                _sb.AppendLine(lang.GetMessage("PrimaryBase", this, p.Id));

                if (FileExists(entry.Key))
                {
                    _sb.AppendLine(entry.Key);
                    validBase = true;
                }
                else _sb.Append(entry.Key).Append(lang.GetMessage("IsProfile", this, p.Id));

                if (entry.Value.Options.AdditionalBases.Count > 0)
                {
                    _sb.AppendLine(lang.GetMessage("AdditionalBase", this, p.Id));

                    foreach (var ab in entry.Value.Options.AdditionalBases)
                    {
                        if (FileExists(ab.Key))
                        {
                            _sb.AppendLine(ab.Key);
                            validBase = true;
                        }
                        else _sb.Append(ab.Key).Append((lang.GetMessage("FileDoesNotExist", this, p.Id)));
                    }
                }
            }

            if (!validBase)
            {
                _sb.AppendLine(lang.GetMessage("NoValidBuildingsConfigured", this, p.Id));
            }

            p.Reply(_sb.ToString());
            _sb.Length = 0;
        }

        private readonly List<string> arguments = new List<string>
        {
            "add", "remove", "list", "clean"
        };

        private static bool IsValid(BaseEntity e)
        {
            if (e == null || e.net == null || e.IsDestroyed || e.transform == null)
            {
                return false;
            }

            return true;
        }

        private bool IsValid(Item item)
        {
            if (item == null || !item.IsValid() || item.isBroken)
            {
                return false;
            }

            return true;
        }

        private bool IsValid(string[] args)
        {
            return args.Length > 0 && arguments.Contains(args[0]);
        }

        private void DropOrRemoveItems(StorageContainer container, bool isOpened = true)
        {
            if (!isOpened || !_config.Settings.Management.DropLootTraps && RaidableBase.IsProtectedWeapon(container) || !_config.Settings.Management.AllowCupboardLoot && container is BuildingPrivlidge)
            {
                container.inventory.Clear();
            }
            else if (container.inventory.itemList.Count > 0)
            {
                var dropPos = container.WorldSpaceBounds().ToBounds().center;

                RaycastHit hit;
                if (Physics.Raycast(container.transform.position, Vector3.up, out hit, 5f, Layers.Mask.World | Layers.Mask.Construction, QueryTriggerInteraction.Ignore))
                {
                    dropPos.y = hit.point.y - 0.3f;
                }

                container.inventory.Drop(StringPool.Get(545786656), dropPos, container.transform.rotation);
            }

            container.Invoke(container.KillMessage, 0.1f);
        }

        private bool IsSpawnOnCooldown()
        {
            if (Time.realtimeSinceStartup - lastSpawnRequestTime < 2f)
            {
                return true;
            }

            lastSpawnRequestTime = Time.realtimeSinceStartup;
            return false;
        }

        protected bool DespawnBase(BasePlayer player)
        {
            var raid = GetNearestBase(player.transform.position);

            if (raid == null)
            {
                return false;
            }

            raid.Despawn();

            return true;
        }

        private RaidableBase GetNearestBase(Vector3 target, float radius = 100f)
        {
            var values = new List<RaidableBase>();

            foreach (var x in Raids.Values)
            {
                if (InRange(x.Location, target, radius))
                {
                    values.Add(x);
                }
            }

            int count = values.Count;

            if (count == 0)
            {
                return null;
            }

            if (count > 1)
            {
                values.Sort((a, b) => (a.Location - target).sqrMagnitude.CompareTo((b.Location - target).sqrMagnitude));
            }

            return values[0];
        }

        private void DespawnAllBasesNow(bool inactiveOnly)
        {
            if (!IsUnloading)
            {
                StartDespawnRoutine(inactiveOnly);
                return;
            }

            if (Interface.Oxide.IsShuttingDown)
            {
                RemoveHeldEntities();
                return;
            }

            StartDespawnInvokes();
            DestroyAll();
        }

        private void RemoveHeldEntities()
        {
            foreach (var entity in RaidEntities.Keys)
            {
                if (entity is IItemContainerEntity)
                {
                    var ice = entity as IItemContainerEntity;

                    foreach (Item item in ice.inventory.itemList)
                    {
                        var e = item.GetHeldEntity();

                        if (e.IsValid())
                        {
                            e.enableSaving = false;
                            BaseEntity.saveList.Remove(e);
                        }
                    }
                }
            }
        }

        private void DestroyAll()
        {
            foreach (var raid in Raids.Values.ToList())
            {
                Interface.CallHook("OnRaidableBaseDespawn", raid.Location, raid.spawnTime, raid.ID);
                Puts(lang.GetMessage("Destroyed Raid", this), $"{PositionToGrid(raid.Location)} {raid.Location}");
                if (raid.IsOpened) raid.AwardRaiders();
                raid.Despawn();
            }
        }

        private void StartDespawnInvokes()
        {
            if (Bases.Count == 0)
            {
                return;
            }

            float num = 0f;
            float time = Mathf.Clamp(_config.Settings.Management.InvokeTime, 0.001f, 0.25f);

            foreach (var entry in Bases)
            {
                if (entry.Value == null || entry.Value.Count == 0)
                {
                    continue;
                }

                foreach (var e in entry.Value)
                {
                    if (e != null && !e.IsDestroyed)
                    {
                        if (e is StorageContainer)
                        {
                            (e as StorageContainer).dropChance = 0f;
                        }
                        else if (e is ContainerIOEntity)
                        {
                            (e as ContainerIOEntity).dropChance = 0f;
                        }

                        e.Invoke(() =>
                        {
                            if (!e.IsDestroyed)
                            {
                                e.KillMessage();
                            }
                        }, num += time);
                    }
                }
            }
        }

        private void StopDespawnCoroutine()
        {
            if (despawnCoroutine != null)
            {
                ServerMgr.Instance.StopCoroutine(despawnCoroutine);
                despawnCoroutine = null;
            }
        }

        private void StartDespawnRoutine(bool inactiveOnly)
        {
            if (Raids.Count == 0)
            {
                return;
            }

            if (despawnCoroutine != null)
            {
                timer.Once(0.1f, () => StartDespawnRoutine(inactiveOnly));
                return;
            }

            despawnCoroutine = ServerMgr.Instance.StartCoroutine(DespawnCoroutine(inactiveOnly));
        }

        private IEnumerator DespawnCoroutine(bool inactiveOnly)
        {
            int count = Raids.Count;

            while (--count >= 0)
            {
                var raid = Raids.ElementAt(count).Value;

                if (inactiveOnly && (raid.intruders.Count > 0 || raid.ownerId.IsSteamId()))
                {
                    continue;
                }

                var baseIndex = raid.BaseIndex;
                var position = raid.Location;
                var uid = raid.uid;

                raid.Despawn();

                yield return new WaitWhile(() => Bases.ContainsKey(baseIndex));
                yield return CoroutineEx.waitForSeconds(0.1f);

                Raids.Remove(uid);

                Interface.CallHook("OnRaidableBaseDespawned", position);
            }

            despawnCoroutine = null;
        }

        private bool IsTrueDamage(BaseEntity entity)
        {
            if (!entity.IsValid())
            {
                return false;
            }

            return entity.skinID == 1587601905 || Backbone.Path.TrueDamage.Contains(entity.prefabID) || RaidableBase.IsProtectedWeapon(entity) || entity is TeslaCoil || entity is FireBall || entity is BaseTrap;
        }

        private Vector3 GetCenterLocation(Vector3 position)
        {
            foreach (var raid in Raids.Values)
            {
                if (InRange(raid.Location, position, raid.Options.ProtectionRadius))
                {
                    return raid.Location;
                }
            }

            return Vector3.zero;
        }

        private bool EventTerritory(Vector3 position)
        {
            foreach (var raid in Raids.Values)
            {
                if (InRange(raid.Location, position, raid.Options.ProtectionRadius))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanBlockOutsideDamage(BasePlayer victim, HitInfo hitInfo)
        {
            if (victim.IsNpc || !hitInfo.Initiator.IsValid() || !hitInfo.Initiator.IsNpc || !(hitInfo.Initiator is NPCPlayerApex))
            {
                return false;
            }

            var npc = hitInfo.Initiator as NPCPlayerApex;
            var raid = RaidableBase.Get(npc.userID);

            if (raid == null || !CanBlockOutsideDamage(raid, npc, raid.Options.BlockOutsideDamageToPlayersInside))
            {
                return false;
            }

            return true;
        }

        private bool CanBlockOutsideDamage(RaidableBase raid, BasePlayer attacker, bool isEnabled)
        {
            if (isEnabled)
            {
                float radius = Mathf.Max(raid.Options.ProtectionRadius, raid.Options.ArenaWalls.Radius, Constants.RADIUS);

                return !InRange(attacker.transform.position, raid.Location, radius, false);
            }

            return false;
        }

        private static bool InRange(Vector3 a, Vector3 b, float distance, bool ex = true)
        {
            if (!ex)
            {
                return (a - b).sqrMagnitude <= distance * distance;
            }

            return (new Vector3(a.x, 0f, a.z) - new Vector3(b.x, 0f, b.z)).sqrMagnitude <= distance * distance;
        }

        private static bool InRange2(Vector3 a, Vector3 b, float distance, bool _2d = true)
        {
            if (_2d)
            {
                return Vector3Ex.SqrMagnitude2D(a - b) <= distance * distance;
            }

            return Vector3.SqrMagnitude(a - b) <= distance * distance;
        }

        private bool AssignTreasureHunters()
        {
            foreach (var target in covalence.Players.All)
            {
                if (target == null || string.IsNullOrEmpty(target.Id))
                    continue;

                if (permission.UserHasPermission(target.Id, rankLadderPermission))
                    permission.RevokeUserPermission(target.Id, rankLadderPermission);

                if (permission.UserHasGroup(target.Id, rankLadderGroup))
                    permission.RemoveUserGroup(target.Id, rankLadderGroup);
            }

            if (!_config.RankedLadder.Enabled)
            {
                return true;
            }

            var ladder = new List<KeyValuePair<string, int>>();

            foreach (var entry in storedData.Players)
            {
                if (entry.Value.Raids > 0)
                {
                    ladder.Add(new KeyValuePair<string, int>(entry.Key, entry.Value.Raids));
                }
            }

            ladder.Sort((x, y) => y.Value.CompareTo(x.Value));

            foreach (var kvp in ladder.Take(_config.RankedLadder.Amount))
            {
                var p = covalence.Players.FindPlayerById(kvp.Key);

                if (p == null)
                {
                    continue;
                }

                permission.GrantUserPermission(p.Id, rankLadderPermission, this);
                permission.AddUserGroup(p.Id, rankLadderGroup);

                string message = Backbone.GetMessage("Log Stolen", null, p.Name, p.Id, kvp.Value);

                LogToFile("treasurehunters", $"{DateTime.Now} : {message}", this, true);
                Puts(Backbone.GetMessage("Log Granted", null, p.Name, p.Id, rankLadderPermission, rankLadderGroup));
            }

            return true;
        }

        private void AddMapPrivatePluginMarker(Vector3 position, int uid)
        {
            if (Map == null || !Map.IsLoaded)
            {
                return;
            }

            mapMarkers[uid] = new MapInfo { IconName = _config.LustyMap.IconName, Position = position, Url = _config.LustyMap.IconFile };
            Map?.Call("ApiAddPointUrl", _config.LustyMap.IconFile, _config.LustyMap.IconName, position);
        }

        private void RemoveMapPrivatePluginMarker(int uid)
        {
            if (Map == null || !Map.IsLoaded || !mapMarkers.ContainsKey(uid))
            {
                return;
            }

            var mapInfo = mapMarkers[uid];
            Map?.Call("ApiRemovePointUrl", mapInfo.Url, mapInfo.IconName, mapInfo.Position);
            mapMarkers.Remove(uid);
        }

        private void AddTemporaryLustyMarker(Vector3 pos, int uid)
        {
            if (LustyMap == null || !LustyMap.IsLoaded)
            {
                return;
            }

            string name = string.Format("{0}_{1}", _config.LustyMap.IconName, storedData.TotalEvents).ToLower();
            LustyMap?.Call("AddTemporaryMarker", pos.x, pos.z, name, _config.LustyMap.IconFile, _config.LustyMap.IconRotation);
            lustyMarkers[uid] = name;
        }

        private void RemoveTemporaryLustyMarker(int uid)
        {
            if (LustyMap == null || !LustyMap.IsLoaded || !lustyMarkers.ContainsKey(uid))
            {
                return;
            }

            LustyMap?.Call("RemoveTemporaryMarker", lustyMarkers[uid]);
            lustyMarkers.Remove(uid);
        }

        private void RemoveAllThirdPartyMarkers()
        {
            if (lustyMarkers.Count > 0)
            {
                var lusty = new Dictionary<int, string>(lustyMarkers);

                foreach (var entry in lusty)
                {
                    RemoveTemporaryLustyMarker(entry.Key);
                }

                lusty.Clear();
            }

            if (mapMarkers.Count > 0)
            {
                var maps = new Dictionary<int, MapInfo>(mapMarkers);

                foreach (var entry in maps)
                {
                    RemoveMapPrivatePluginMarker(entry.Key);
                }

                maps.Clear();
            }
        }

        private void StopMaintainCoroutine()
        {
            if (maintainCoroutine != null)
            {
                ServerMgr.Instance.StopCoroutine(maintainCoroutine);
                maintainCoroutine = null;
            }
        }

        private void StartMaintainCoroutine()
        {
            if (!maintainEnabled || _config.Settings.Maintained.Max <= 0)
            {
                return;
            }

            if (IsGridLoading)
            {
                timer.Once(1f, () => StartMaintainCoroutine());
                return;
            }

            StopMaintainCoroutine();

            timer.Once(0.2f, () =>
            {
                maintainCoroutine = ServerMgr.Instance.StartCoroutine(MaintainCoroutine());
            });
        }

        private bool IsLoadingMaintainedEvent;

        private IEnumerator MaintainCoroutine()
        {
            string message;

            while (!IsUnloading)
            {
                if (CanMaintainOpenEvent())
                {
                    if (!maintainEnabled || SaveRestore.IsSaving)
                    {
                        PrintDebugMessage(maintainEnabled ? "Server saving" : "Maintained events not enabled");

                        yield return CoroutineEx.waitForSeconds(15f);
                    }
                    else if (SpawnRandomBase(out message, RaidableType.Maintained, RaidableMode.Normal) != Vector3.zero)
                    {
                        IsLoadingMaintainedEvent = true;

                        if (_config.Settings.Maintained.Time > 0)
                        {
                            yield return CoroutineEx.waitForSeconds(_config.Settings.Maintained.Time);
                        }

                        yield return new WaitWhile(() => IsLoadingMaintainedEvent);
                    }
                    //else PostDebugMessage(message);
                }

                yield return CoroutineEx.waitForSeconds(1f);
            }

            maintainCoroutine = null;
        }

        private bool CanMaintainOpenEvent() => IsPasteAvailable && !IsGridLoading && _config.Settings.Maintained.Max > 0 && RaidableBase.Get(RaidableType.Maintained) < _config.Settings.Maintained.Max && BasePlayer.activePlayerList.Count >= _config.Settings.Maintained.PlayerLimit && CopyPaste != null && CopyPaste.IsLoaded;

        private void StopScheduleCoroutine()
        {
            if (scheduleCoroutine != null)
            {
                ServerMgr.Instance.StopCoroutine(scheduleCoroutine);
                scheduleCoroutine = null;
            }
        }

        private void StartScheduleCoroutine()
        {
            if (!scheduleEnabled || _config.Settings.Schedule.Max <= 0)
            {
                return;
            }

            if (IsGridLoading)
            {
                timer.Once(1f, () => StartScheduleCoroutine());
                return;
            }

            StopScheduleCoroutine();

            if (storedData.RaidTime == DateTime.MinValue.ToString())
            {
                ScheduleNextAutomatedEvent();
            }

            timer.Once(0.2f, () =>
            {
                scheduleCoroutine = ServerMgr.Instance.StartCoroutine(ScheduleCoroutine());
            });
        }

        private bool IsLoadingScheduledEvent;

        private IEnumerator ScheduleCoroutine()
        {
            string message;

            while (!IsUnloading)
            {
                if (CanScheduleOpenEvent())
                {
                    while (RaidableBase.Get(RaidableType.Scheduled) < _config.Settings.Schedule.Max && MaxOnce())
                    {
                        if (!scheduleEnabled || SaveRestore.IsSaving)
                        {
                            PrintDebugMessage(scheduleEnabled ? "Saving server" : "Scheduled events not enabled");

                            yield return CoroutineEx.waitForSeconds(15f);
                            continue;
                        }

                        var vector = SpawnRandomBase(out message, RaidableType.Scheduled, RaidableMode.Normal);

                        if (vector != Vector3.zero)
                        {
                            _maxOnce++;
                            IsLoadingScheduledEvent = true;

                            if (_config.Settings.Schedule.Time > 0)
                            {
                                yield return CoroutineEx.waitForSeconds(_config.Settings.Schedule.Time);
                            }

                            yield return new WaitWhile(() => IsLoadingScheduledEvent);
                        }
                        //else PostDebugMessage(message);

                        yield return CoroutineEx.waitForSeconds(1f);
                    }

                    ScheduleNextAutomatedEvent();
                }

                yield return CoroutineEx.waitForSeconds(1f);
            }

            scheduleCoroutine = null;
        }

        private void ScheduleNextAutomatedEvent()
        {
            var raidInterval = Core.Random.Range(_config.Settings.Schedule.IntervalMin, _config.Settings.Schedule.IntervalMax + 1);

            storedData.RaidTime = DateTime.Now.AddSeconds(raidInterval).ToString();
            _maxOnce = 0;

            Puts(Backbone.GetMessage("Next Automated Raid", null, FormatTime(raidInterval), storedData.RaidTime));
            SaveData();
        }

        private bool MaxOnce()
        {
            return _config.Settings.Schedule.MaxOnce <= 0 || _maxOnce < _config.Settings.Schedule.MaxOnce;
        }

        private static bool IsModeValid(RaidableMode mode) => mode != RaidableMode.Disabled && mode != RaidableMode.Random;

        private double GetRaidTime() => DateTime.Parse(storedData.RaidTime).Subtract(DateTime.Now).TotalSeconds;

        private bool CanScheduleOpenEvent() => GetRaidTime() <= 0 && _config.Settings.Schedule.Max > 0 && RaidableBase.Get(RaidableType.Scheduled) < _config.Settings.Schedule.Max && IsPasteAvailable && !IsGridLoading && BasePlayer.activePlayerList.Count >= _config.Settings.Schedule.PlayerLimit && CopyPaste != null && CopyPaste.IsLoaded;

        private void LoadData()
        {
            try
            {
                storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            }
            catch
            {
            }

            if (storedData?.Players == null)
            {
                storedData = new StoredData();
                SaveData();
            }
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);
        }

        public static string FormatGridReference(Vector3 position)
        {
            if (_config.Settings.ShowXZ)
            {
                return string.Format("{0} ({1} {2})", PositionToGrid(position), position.x.ToString("N2"), position.z.ToString("N2"));
            }

            return PositionToGrid(position);
        }

        private static string PositionToGrid(Vector3 position) // Credit: Whispers88/redBGDR/Dana
        {
            var r = new Vector2(World.Size / 2 + position.x, World.Size / 2 + position.z);
            var c = Mathf.Floor(r.x / 146.3f) / 26;
            var x = Mathf.Floor(r.x / 146.3f) % 26;
            var z = Mathf.Floor(World.Size / 146.3f) - Mathf.Floor(r.y / 146.3f);

            return $"{(c >= 1 ? (char)('A' + (c - 1)) : ' ')}{(char)('A' + x)}{z - 1}";
        }

        private static string FormatTime(double seconds)
        {
            if (seconds < 0)
            {
                return "0s";
            }

            var ts = TimeSpan.FromSeconds(seconds);
            string format = Backbone.GetMessage("TimeFormat");

            if (format == "TimeFormat")
            {
                format = "{0:D2}h {1:D2}m {2:D2}s";
            }

            return string.Format(format, ts.Hours, ts.Minutes, ts.Seconds);
        }

        #endregion

        #region Data files

        private void CreateDefaultFiles()
        {
            string folder = $"{Name}{Path.DirectorySeparatorChar}Profiles";
            string empty = $"{folder}{Path.DirectorySeparatorChar}_emptyfile";

            if (Interface.Oxide.DataFileSystem.ExistsDatafile(empty))
            {
                return;
            }

            Interface.Oxide.DataFileSystem.GetDatafile(empty);

            foreach (var building in DefaultBuildingOptions)
            {
                string filename = $"{Name}{Path.DirectorySeparatorChar}Profiles{Path.DirectorySeparatorChar}{building.Key}";

                if (!Interface.Oxide.DataFileSystem.ExistsDatafile(filename))
                {
                    SaveProfile(building.Key, building.Value);
                }
            }

            string lootFile = $"{Name}{Path.DirectorySeparatorChar}Default_Loot";

            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(lootFile))
            {
                Interface.Oxide.DataFileSystem.WriteObject(lootFile, DefaultLoot);
            }
        }

        protected void LoadProfiles()
        {
            string folder = $"{Name}{Path.DirectorySeparatorChar}Profiles";

            ConvertProfilesFromConfig();

            var files = Interface.Oxide.DataFileSystem.GetFiles(folder);

            foreach (string file in files)
            {
                try
                {
                    if (file.EndsWith("_emptyfile.json") || file.EndsWith("_empty_file.json"))
                    {
                        continue;
                    }

                    int index = file.LastIndexOf(Path.DirectorySeparatorChar) + 1;
                    string profileName = file.Substring(index, file.Length - index - 5);
                    string fullName = $"{folder}{Path.DirectorySeparatorChar}{profileName}";
                    var options = Interface.Oxide.DataFileSystem.ReadObject<BuildingOptions>(fullName);

                    if (options == null || !options.Enabled)
                    {
                        continue;
                    }

                    if (options.AdditionalBases == null)
                    {
                        options.AdditionalBases = new Dictionary<string, List<PasteOption>>();
                    }

                    options.Loot = null;

                    if (options.ProtectionRadius < Constants.CELL_SIZE)
                    {
                        options.ProtectionRadius = Constants.CELL_SIZE;
                    }

                    Buildings.Profiles[profileName] = new BaseProfile(options);
                }
                catch (Exception ex)
                {
                    Puts("Profile {0} is corrupted!\n{1}", file, ex.Message);
                }
            }

            foreach (var profile in Buildings.Profiles)
            {
                SaveProfile(profile.Key, profile.Value.Options);
            }

            LoadBaseTables();
        }

        protected void SaveProfile(string key, BuildingOptions options)
        {
            Interface.Oxide.DataFileSystem.WriteObject($"{Name}{Path.DirectorySeparatorChar}Profiles{Path.DirectorySeparatorChar}{key}", options);
        }

        protected void ConvertProfilesFromConfig()
        {
            if (_config.RaidableBases?.Buildings?.Count > 0)
            {
                CreateBackup();

                foreach (var building in _config.RaidableBases.Buildings)
                {
                    SaveProfile(building.Key, building.Value);
                }

                _config.RaidableBases.Buildings.Clear();
            }

            _config.RaidableBases.Buildings = null;
            _config.RaidableBases = null;
            SaveConfig();
        }

        protected void LoadTables()
        {
            ConvertTablesFromConfig();
            Buildings = new BuildingTables();
            _sb.Length = 0;
            _sb.AppendLine("-");

            List<TreasureItem> defaultList;

            Buildings.Default = defaultList = GetTable($"{Name}{Path.DirectorySeparatorChar}Default_Loot");

            defaultList.RemoveAll(ti => ti.amount == 0 && ti.amountMin == 0);

            if (defaultList.Count > 0)
            {
                _sb.AppendLine($"Loaded {defaultList.Count} items from RaidableBases/Default_Loot");

                defaultList.ForEach(ti =>
                {
                    if (ti.amount < ti.amountMin)
                    {
                        _sb.AppendLine(string.Format("Error >>> {0} has {1} amount / {2} amountMin in {3}.json", ti.shortname, ti.amount, ti.amountMin, "Default_Loot"));
                    }
                });
            }

            List<TreasureItem> normalList;

            Buildings.Normal = normalList = GetTable($"{Name}{Path.DirectorySeparatorChar}Difficulty_Loot{Path.DirectorySeparatorChar}Normal");

            normalList.RemoveAll(ti => ti.amount == 0 && ti.amountMin == 0);

            if (normalList.Count > 0)
            {
                _sb.AppendLine($"Loaded {normalList.Count} items from RaidableBases/Difficulty_Loot/Normal");

                normalList.ForEach(ti =>
                {
                    if (ti.amount < ti.amountMin)
                    {
                        _sb.AppendLine(string.Format("Error >>> {0} has {1} amount / {2} amountMin in {3}.json", ti.shortname, ti.amount, ti.amountMin, "Normal"));
                    }
                });
            }

            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
            {
                string file = $"{Name}{Path.DirectorySeparatorChar}Weekday_Loot{Path.DirectorySeparatorChar}{day}";
                List<TreasureItem> lootList;

                Buildings.Weekday[day] = lootList = GetTable(file);

                lootList.RemoveAll(ti => ti.amount == 0 && ti.amountMin == 0);

                if (lootList.Count > 0)
                {
                    _sb.AppendLine($"Loaded {lootList.Count} items from {file}");

                    lootList.ForEach(ti =>
                    {
                        if (ti.amount < ti.amountMin)
                        {
                            _sb.AppendLine(string.Format("Error >>> {0} has {1} amount / {2} amountMin in {3}.json", ti.shortname, ti.amount, ti.amountMin, file));
                        }
                    });
                }
            }
        }

        private void LoadBaseTables()
        {
            foreach (var entry in Buildings.Profiles)
            {
                string file = $"{Name}{Path.DirectorySeparatorChar}Base_Loot{Path.DirectorySeparatorChar}{entry.Key}";
                var lootList = GetTable(file);

                lootList.RemoveAll(ti => ti.amount == 0 && ti.amountMin == 0);

                entry.Value.BaseLootList = lootList;

                if (lootList.Count > 0)
                {
                    _sb.AppendLine($"Loaded {lootList.Count} items from {file}");

                    lootList.ForEach(ti =>
                    {
                        if (ti.amount < ti.amountMin)
                        {
                            _sb.AppendLine(string.Format("Error >>> {0} has {1} amount / {2} amountMin in {3}.json", ti.shortname, ti.amount, ti.amountMin, file));
                        }
                    });
                }
            }

            Interface.Oxide.LogInfo("{0}", _sb.ToString());
            _sb.Length = 0;
        }

        private List<TreasureItem> GetTable(string file)
        {
            var lootList = new List<TreasureItem>();

            try
            {
                lootList = Interface.Oxide.DataFileSystem.ReadObject<List<TreasureItem>>(file);
            }
            catch (Exception ex)
            {
                Puts("{0} --- {1}", file, ex.Message);
            }

            if (lootList == null)
            {
                Interface.Oxide.DataFileSystem.WriteObject(file, lootList = new List<TreasureItem>());
            }

            return lootList;
        }

        protected void ConvertTablesFromConfig()
        {
            if (_config.RaidableBases?.Buildings != null)
            {
                foreach (var building in _config.RaidableBases.Buildings)
                {
                    ConvertFromConfig(building.Value.Loot, $"Base_Loot{Path.DirectorySeparatorChar}{building.Key}");
                    building.Value.Loot = null;
                }
            }

            ConvertFromConfig(_config.Treasure.Loot, "Default_Loot");
            ConvertFromConfig(_config.Treasure.LootEasy, $"Difficulty_Loot{Path.DirectorySeparatorChar}Easy");
            ConvertFromConfig(_config.Treasure.LootMedium, $"Difficulty_Loot{Path.DirectorySeparatorChar}Medium");
            ConvertFromConfig(_config.Treasure.LootHard, $"Difficulty_Loot{Path.DirectorySeparatorChar}Hard");
            ConvertFromConfig(_config.Treasure.LootExpert, $"Difficulty_Loot{Path.DirectorySeparatorChar}Expert");
            ConvertFromConfig(_config.Treasure.LootNightmare, $"Difficulty_Loot{Path.DirectorySeparatorChar}Nightmare");
            ConvertFromConfig(_config.Treasure.DOWL_Monday, $"Weekday_Loot{Path.DirectorySeparatorChar}Monday");
            ConvertFromConfig(_config.Treasure.DOWL_Tuesday, $"Weekday_Loot{Path.DirectorySeparatorChar}Tuesday");
            ConvertFromConfig(_config.Treasure.DOWL_Wednesday, $"Weekday_Loot{Path.DirectorySeparatorChar}Wednesday");
            ConvertFromConfig(_config.Treasure.DOWL_Thursday, $"Weekday_Loot{Path.DirectorySeparatorChar}Thursday");
            ConvertFromConfig(_config.Treasure.DOWL_Friday, $"Weekday_Loot{Path.DirectorySeparatorChar}Friday");
            ConvertFromConfig(_config.Treasure.DOWL_Saturday, $"Weekday_Loot{Path.DirectorySeparatorChar}Saturday");
            ConvertFromConfig(_config.Treasure.DOWL_Sunday, $"Weekday_Loot{Path.DirectorySeparatorChar}Sunday");

            EnsureDeserialized();
            SaveConfig();
        }

        protected void ConvertFromConfig(List<TreasureItem> lootList, string key)
        {
            if (lootList?.Count > 0)
            {
                CreateBackup();
                Interface.Oxide.DataFileSystem.WriteObject($"{Name}{Path.DirectorySeparatorChar}{key}", lootList);
            }
        }

        protected void EnsureDeserialized()
        {
            _config.Treasure.Loot = null;
            _config.Treasure.LootEasy = null;
            _config.Treasure.LootMedium = null;
            _config.Treasure.LootHard = null;
            _config.Treasure.LootExpert = null;
            _config.Treasure.LootNightmare = null;
            _config.Treasure.DOWL_Monday = null;
            _config.Treasure.DOWL_Tuesday = null;
            _config.Treasure.DOWL_Wednesday = null;
            _config.Treasure.DOWL_Thursday = null;
            _config.Treasure.DOWL_Friday = null;
            _config.Treasure.DOWL_Saturday = null;
            _config.Treasure.DOWL_Sunday = null;
        }

        private void CreateBackup()
        {
            if (!_createdBackup)
            {
                string file = $"{Interface.Oxide.ConfigDirectory}{Path.DirectorySeparatorChar}{Name}.backup_old_system.{DateTime.Now:yyyy-MM-dd hh-mm-ss}.json";
                Config.WriteObject(_config, false, file);
                Puts("Created config backup of old system: {0}", file);
                _createdBackup = true;
            }
        }

        private bool _createdBackup;

        #endregion

        #region Configuration

        private Dictionary<string, Dictionary<string, string>> GetMessages()
        {
            return new Dictionary<string, Dictionary<string, string>>
            {
                {"No Permission", new Dictionary<string, string>() {
                    {"en", "You do not have permission to use this command."},
                }},
                {"Building is blocked!", new Dictionary<string, string>() {
                    {"en", "<color=#FF0000>Building is blocked near raidable bases!</color>"},
                }},
                {"Ladders are blocked!", new Dictionary<string, string>() {
                    {"en", "<color=#FF0000>Ladders are blocked in raidable bases!</color>"},
                }},
                {"Ladders Require Building Privilege!", new Dictionary<string, string>() {
                    {"en", "<color=#FF0000>You need building privilege to place ladders!</color>"},
                }},
                {"Profile Not Enabled", new Dictionary<string, string>() {
                    {"en", "This profile is not enabled: <color=#FF0000>{0}</color>."},
                }},
                {"Max Manual Events", new Dictionary<string, string>() {
                    {"en", "Maximum number of manual events <color=#FF0000>{0}</color> has been reached!"},
                }},
                {"Manual Event Failed", new Dictionary<string, string>() {
                    {"en", "Event failed to start! Unable to obtain a valid position. Please try again."},
                }},
                {"Help", new Dictionary<string, string>() {
                    {"en", "/{0} <tp> - start a manual event, and teleport to the position if TP argument is specified and you are an admin."},
                }},
                {"RaidOpenMessage", new Dictionary<string, string>() {
                    {"en", "<color=#C0C0C0>A {0} raidable base event has opened at <color=#FFFF00>{1}</color>! You are <color=#FFA500>{2}m</color> away. [{3}]</color>"},
                }},
                {"Next", new Dictionary<string, string>() {
                    {"en", "<color=#C0C0C0>No events are open. Next event in <color=#FFFF00>{0}</color></color>"},
                }},
                {"Wins", new Dictionary<string, string>()
                {
                    {"en", "<color=#C0C0C0>You have looted <color=#FFFF00>{0}</color> raid bases! View the ladder using <color=#FFA500>/{1} ladder</color> or <color=#FFA500>/{1} lifetime</color></color>"},
                }},
                {"RaidMessage", new Dictionary<string, string>() {
                    {"en", "Raidable Base {0}m [{1} players]"},
                }},
                {"Ladder", new Dictionary<string, string>()
                {
                    {"en", "<color=#FFFF00>[ Top 10 Raid Hunters (This Wipe) ]</color>:"},
                }},
                {"Ladder Total", new Dictionary<string, string>()
                {
                    {"en", "<color=#FFFF00>[ Top 10 Raid Hunters (Lifetime) ]</color>:"},
                }},
                {"Ladder Insufficient Players", new Dictionary<string, string>()
                {
                    {"en", "<color=#FFFF00>No players are on the ladder yet!</color>"},
                }},
                {"Next Automated Raid", new Dictionary<string, string>() {
                    {"en", "Next automated raid in {0} at {1}"},
                }},
                {"Not Enough Online", new Dictionary<string, string>() {
                    {"en", "Not enough players online ({0} minimum)"},
                }},
                {"Raid Base Distance", new Dictionary<string, string>() {
                    {"en", "<color=#C0C0C0>Raidable Base <color=#FFA500>{0}m</color>"},
                }},
                {"Destroyed Raid", new Dictionary<string, string>() {
                    {"en", "Destroyed a left over raid base at {0}"},
                }},
                {"Indestructible", new Dictionary<string, string>() {
                    {"en", "<color=#FF0000>Treasure chests are indestructible!</color>"},
                }},
                {"View Config", new Dictionary<string, string>() {
                    {"en", "Please view the config if you haven't already."},
                }},
                {"Log Stolen", new Dictionary<string, string>() {
                    {"en", "{0} ({1}) Raids {2}"},
                }},
                {"Log Granted", new Dictionary<string, string>() {
                    {"en", "Granted {0} ({1}) permission {2} for group {3}"},
                }},
                {"Log Saved", new Dictionary<string, string>() {
                    {"en", "Raid Hunters have been logged to: {0}"},
                }},
                {"Prefix", new Dictionary<string, string>() {
                    {"en", "[ <color=#406B35>Raidable Bases</color> ] "},
                }},
                {"RestartDetected", new Dictionary<string, string>()
                {
                    {"en", "Restart detected. Next event in {0} minutes."},
                }},
                {"EconomicsDeposit", new Dictionary<string, string>()
                {
                    {"en", "You have received <color=#FFFF00>${0}</color> for stealing the treasure!"},
                }},
                {"ServerRewardPoints", new Dictionary<string, string>()
                {
                    {"en", "You have received <color=#FFFF00>{0} RP</color> for stealing the treasure!"},
                }},
                {"InvalidItem", new Dictionary<string, string>()
                {
                    {"en", "Invalid item shortname: {0}. Use /{1} additem <shortname> <amount> [skin]"},
                }},
                {"AddedItem", new Dictionary<string, string>()
                {
                    {"en", "Added item: {0} amount: {1}, skin: {2}"},
                }},
                {"CustomPositionSet", new Dictionary<string, string>()
                {
                    {"en", "Custom event spawn location set to: {0}"},
                }},
                {"CustomPositionRemoved", new Dictionary<string, string>()
                {
                    {"en", "Custom event spawn location removed."},
                }},
                {"OpenedEvents", new Dictionary<string, string>()
                {
                    {"en", "Opened {0}/{1} events."},
                }},
                {"OnPlayerEntered", new Dictionary<string, string>()
                {
                    {"en", "<color=#FF0000>You have entered a raidable PVP base!</color>"},
                }},
                {"OnPlayerEnteredPVE", new Dictionary<string, string>()
                {
                    {"en", "<color=#FF0000>You have entered a raidable PVE base!</color>"},
                }},
                {"OnPlayerEntryRejected", new Dictionary<string, string>()
                {
                    {"en", "<color=#FF0000>You cannot enter an event that does not belong to you!</color>"},
                }},
                {"OnFirstPlayerEntered", new Dictionary<string, string>()
                {
                    {"en", "<color=#FFFF00>{0}</color> is the first to enter the raidable base at <color=#FFFF00>{1}</color>"},
                }},
                {"OnChestOpened", new Dictionary<string, string>() {
                    {"en", "<color=#FFFF00>{0}</color> is the first to see the treasures at <color=#FFFF00>{1}</color>!</color>"},
                }},
                {"OnRaidFinished", new Dictionary<string, string>() {
                    {"en", "The raid at <color=#FFFF00>{0}</color> has been unlocked!"},
                }},
                {"CannotBeMounted", new Dictionary<string, string>() {
                    {"en", "You cannot loot the treasure while mounted!"},
                }},
                {"CannotTeleport", new Dictionary<string, string>() {
                    {"en", "You are not allowed to teleport from this event."},
                }},
                {"MustBeAuthorized", new Dictionary<string, string>() {
                    {"en", "You must have building privilege to access this treasure!"},
                }},
                {"OwnerLocked", new Dictionary<string, string>() {
                    {"en", "This treasure belongs to someone else!"},
                }},
                {"CannotFindPosition", new Dictionary<string, string>() {
                    {"en", "Could not find a random position!"},
                }},
                {"PasteOnCooldown", new Dictionary<string, string>() {
                    {"en", "Paste is on cooldown!"},
                }},
                {"SpawnOnCooldown", new Dictionary<string, string>() {
                    {"en", "Try again, a manual spawn was already requested."},
                }},
                {"Thief", new Dictionary<string, string>() {
                    {"en", "<color=#FFFF00>The base at <color=#FFFF00>{0}</color> has been raided by <color=#FFFF00>{1}</color>!</color>"},
                }},
                {"TargetNotFoundId", new Dictionary<string, string>() {
                    {"en", "<color=#FFFF00>Target {0} not found, or not online.</color>"},
                }},
                {"TargetNotFoundNoId", new Dictionary<string, string>() {
                    {"en", "<color=#FFFF00>No steamid provided.</color>"},
                }},
                {"DestroyingBaseAt", new Dictionary<string, string>() {
                    {"en", "<color=#C0C0C0>Destroying raid base at <color=#FFFF00>{0}</color> in <color=#FFFF00>{1}</color> minutes!</color>"},
                }},
                {"PasteIsBlocked", new Dictionary<string, string>() {
                    {"en", "You cannot start a raid base event there!"},
                }},
                {"LookElsewhere", new Dictionary<string, string>() {
                    {"en", "Unable to find a position; look elsewhere."},
                }},
                {"BuildingNotConfigured", new Dictionary<string, string>() {
                    {"en", "You cannot spawn a base that is not configured."},
                }},
                {"NoValidBuildingsConfigured", new Dictionary<string, string>() {
                    {"en", "No valid buildings have been configured. Raidable Bases > Building Names in config."},
                }},
                {"DespawnBaseSuccess", new Dictionary<string, string>() {
                    {"en", "<color=#C0C0C0>Despawning the nearest raid base to you!</color>"},
                }},
                {"DespawnedAt", new Dictionary<string, string>() {
                    {"en", "{0} despawned a base manually at {1}"},
                }},
                {"DespawnedAll", new Dictionary<string, string>() {
                    {"en", "{0} despawned all bases manually"},
                }},
                {"ModeLevel", new Dictionary<string, string>() {
                    {"en", "level"},
                }},
                {"DespawnBaseNoneAvailable", new Dictionary<string, string>() {
                    {"en", "<color=#C0C0C0>You must be within 100m of a raid base to despawn it.</color>"},
                }},
                {"GridIsLoading", new Dictionary<string, string>() {
                    {"en", "The grid is loading; please wait until it has finished."},
                }},
                {"GridIsLoadingFormatted", new Dictionary<string, string>() {
                    {"en", "Grid is loading. The process has taken {0} seconds so far with {1} locations added on the grid."},
                }},
                {"TooPowerful", new Dictionary<string, string>() {
                    {"en", "<color=#FF0000>This place is guarded by a powerful spirit. You sheath your wand in fear!</color>"},
                }},
                {"TooPowerfulDrop", new Dictionary<string, string>() {
                    {"en", "<color=#FF0000>This place is guarded by a powerful spirit. You drop your wand in fear!</color>"},
                }},
                {"LoadCopyPaste", new Dictionary<string, string>() {
                    {"en", "CopyPaste is not loaded."},
                }},
                {"DoomAndGloom", new Dictionary<string, string>() {
                    {"en", "<color=#FF0000>You have left a {0} zone and can be attacked for another {1} seconds!</color>"},
                }},
                {"MaintainCoroutineFailedToday", new Dictionary<string, string>() {
                    {"en", "<color=#FF0000>Failed to start maintain coroutine; no difficulties are available today.</color>"},
                }},
                {"ScheduleCoroutineFailedToday", new Dictionary<string, string>() {
                    {"en", "<color=#FF0000>Failed to start scheduled coroutine; no difficulties are available today.</color>"},
                }},
                {"NoConfiguredLoot", new Dictionary<string, string>() {
                    {"en", "Error: No loot found in the config!"},
                }},
                {"NoContainersFound", new Dictionary<string, string>() {
                    {"en", "Error: No usable containers found for {0} @ {1}!"},
                }},
                {"NoBoxesFound", new Dictionary<string, string>() {
                    {"en", "Error: No usable boxes found for {0} @ {1}!"},
                }},
                {"NoLootSpawned", new Dictionary<string, string>() {
                    {"en", "Error: No loot was spawned!"},
                }},
                {"LoadedManual", new Dictionary<string, string>() {
                    {"en", "Loaded {0} manual spawns."},
                }},
                {"LoadedMaintained", new Dictionary<string, string>() {
                    {"en", "Loaded {0} maintained spawns."},
                }},
                {"LoadedScheduled", new Dictionary<string, string>() {
                    {"en", "Loaded {0} scheduled spawns."},
                }},
                {"InitializedGrid", new Dictionary<string, string>() {
                    {"en", "Grid initialization completed in {0} seconds and {1} milliseconds on a {2} size map. {3} locations are on the grid."},
                }},
                {"EntityCountMax", new Dictionary<string, string>() {
                    {"en", "Command disabled due to entity count being greater than 300k"},
                }},
                {"NotifyPlayerMessageFormat", new Dictionary<string, string>() {
                    {"en", "<color=#ADD8E6>{rank}</color>. <color=#C0C0C0>{name}</color> (<color=#FFFF00>{value}</color>)"},
                }},
                {"ConfigUseFormat", new Dictionary<string, string>() {
                    {"en", "Use: rb.config <{0}> [base] [subset]"},
                }},
                {"ConfigAddBaseSyntax", new Dictionary<string, string>() {
                    {"en", "Use: rb.config add nivex1 nivex4 nivex5 nivex6"},
                }},
                {"FileDoesNotExist", new Dictionary<string, string>() {
                    {"en", " > This file does not exist\n"},
                }},
                {"IsProfile", new Dictionary<string, string>() {
                    {"en", " > Profile\n"},
                }},
                {"ListingAll", new Dictionary<string, string>() {
                    {"en", "Listing all primary bases and their subsets:"},
                }},
                {"PrimaryBase", new Dictionary<string, string>() {
                    {"en", "Primary Base: "},
                }},
                {"AdditionalBase", new Dictionary<string, string>() {
                    {"en", "Additional Base: "},
                }},
                {"NoValidBuilingsWarning", new Dictionary<string, string>() {
                    {"en", "No valid buildings are configured with a valid file that exists. Did you configure valid files and reload the plugin?"},
                }},
                {"Adding", new Dictionary<string, string>() {
                    {"en", "Adding: {0}"},
                }},
                {"AddedPrimaryBase", new Dictionary<string, string>() {
                    {"en", "Added Primary Base: {0}"},
                }},
                {"AddedAdditionalBase", new Dictionary<string, string>() {
                    {"en", "Added Additional Base: {0}"},
                }},
                {"EntryAlreadyExists", new Dictionary<string, string>() {
                    {"en", "That entry already exists."},
                }},
                {"RemoveSyntax", new Dictionary<string, string>() {
                    {"en", "Use: rb.config remove nivex1"},
                }},
                {"RemovingAllBasesFor", new Dictionary<string, string>() {
                    {"en", "\nRemoving all bases for: {0}"},
                }},
                {"RemovedPrimaryBase", new Dictionary<string, string>() {
                    {"en", "Removed primary base: {0}"},
                }},
                {"RemovedAdditionalBase", new Dictionary<string, string>() {
                    {"en", "Removed additional base {0} from primary base {1}"},
                }},
                {"RemovedEntries", new Dictionary<string, string>() {
                    {"en", "Removed {0} entries"},
                }},
                {"LockedOut", new Dictionary<string, string>() {
                    {"en", "You are locked out from {0} raids for {1}"},
                }},
                {"PVPFlag", new Dictionary<string, string>() {
                    {"en", "[<color=#FF0000>PVP</color>] "},
                }},
                {"PVEFlag", new Dictionary<string, string>() {
                    {"en", "[<color=#008000>PVE</color>] "},
                }},
                {"PVP ZONE", new Dictionary<string, string>() {
                    {"en", "PVP ZONE"},
                }},
                {"PVE ZONE", new Dictionary<string, string>() {
                    {"en", "PVE ZONE"},
                }},
                {"OnPlayerExit", new Dictionary<string, string>()
                {
                    {"en", "<color=#FF0000>You have left a raidable PVP base!</color>"},
                }},
                {"OnPlayerExitPVE", new Dictionary<string, string>()
                {
                    {"en", "<color=#FF0000>You have left a raidable PVE base!</color>"},
                }},
                {"PasteIsBlockedStandAway", new Dictionary<string, string>() {
                    {"en", "You cannot start a raid base event there because you are too close to the spawn. Either move or use noclip."},
                }},
                {"ReloadConfig", new Dictionary<string, string>() {
                    {"en", "Reloading config..."},
                }},
                {"ReloadMaintainCo", new Dictionary<string, string>() {
                    {"en", "Stopped maintain coroutine."},
                }},
                {"ReloadScheduleCo", new Dictionary<string, string>() {
                    {"en", "Stopped schedule coroutine."},
                }},
                {"ReloadInit", new Dictionary<string, string>() {
                    {"en", "Initializing..."},
                }},
                {"YourCorpse", new Dictionary<string, string>() {
                    {"en", "Your Corpse"},
                }},
                {"NotAllowed", new Dictionary<string, string>() {
                    {"en", "<color=#FF0000>That action is not allowed in this zone.</color>"},
                }},
                {"BlockedZones", new Dictionary<string, string>() {
                    {"en", "Blocked spawn points in {0} zones."},
                }},
                {"UIFormat", new Dictionary<string, string>() {
                    {"en", "{0} C:{1} [{2}m]"},
                }},
                {"UIFormatContainers", new Dictionary<string, string>() {
                    {"en", "{0} C:{1}"},
                }},
                {"UIFormatMinutes", new Dictionary<string, string>() {
                    {"en", "{0} [{1}m]"},
                }},
                {"UIHelpText", new Dictionary<string, string>() {
                    {"en", "<color=#C0C0C0>You can toggle the UI by using <color=#FFA500>/{0} ui</color></color>"},
                }},
                {"HoggingFinishYourRaid", new Dictionary<string, string>() {
                    {"en", "<color=#FF0000>You must finish your last raid at {0} before joining another.</color>"},
                }},
                {"HoggingFinishYourRaidClan", new Dictionary<string, string>() {
                    {"en", "<color=#FF0000>Your clan mate `{0}` must finish their last raid at {1}.</color>"},
                }},
                {"HoggingFinishYourRaidTeam", new Dictionary<string, string>() {
                    {"en", "<color=#FF0000>Your team mate `{0}` must finish their last raid at {1}.</color>"},
                }},
                {"HoggingFinishYourRaidFriend", new Dictionary<string, string>() {
                    {"en", "<color=#FF0000>Your friend `{0}` must finish their last raid at {1}.</color>"},
                }},
                {"TimeFormat", new Dictionary<string, string>() {
                    {"en", "{0:D2}h {1:D2}m {2:D2}s"},
                }},
                {"TargetTooFar", new Dictionary<string, string>() {
                    {"en", "Your target is not close enough to a raid."},
                }},
                {"TooFar", new Dictionary<string, string>() {
                    {"en", "You are not close enough to a raid."},
                }},
                {"RaidLockedTo", new Dictionary<string, string>() {
                    {"en", "Raid has been locked to: {0}"},
                }},
                {"RaidOwnerCleared", new Dictionary<string, string>() {
                    {"en", "Raid owner has been cleared."},
                }},
                {"TooCloseToABuilding", new Dictionary<string, string>() {
                    {"en", "Too close to another building"},
                }},
                {"CommandNotAllowed", new Dictionary<string, string>() {
                    {"en", "You are not allowed to use this command right now."},
                }},
                {"Normal", new Dictionary<string, string>() {
                    {"en", "normal"},
                }},
            };
        }

        protected override void LoadDefaultMessages()
        {
            var compiledLangs = new Dictionary<string, Dictionary<string, string>>();

            foreach (var line in GetMessages())
            {
                foreach (var translate in line.Value)
                {
                    if (!compiledLangs.ContainsKey(translate.Key))
                        compiledLangs[translate.Key] = new Dictionary<string, string>();

                    compiledLangs[translate.Key][line.Key] = translate.Value;
                }
            }

            foreach (var cLangs in compiledLangs)
                lang.RegisterMessages(cLangs.Value, this, cLangs.Key);
        }

        private static Configuration _config;

        private static List<PasteOption> DefaultPasteOptions
        {
            get
            {
                return new List<PasteOption>
                {
                    new PasteOption() { Key = "stability", Value = "false" },
                    new PasteOption() { Key = "autoheight", Value = "false" },
                    new PasteOption() { Key = "height", Value = "1.0" }
                };
            }
        }

        private static Dictionary<string, BuildingOptions> DefaultBuildingOptions
        {
            get
            {
                return new Dictionary<string, BuildingOptions>()
                {
                    ["RaidBases"] = new BuildingOptions
                    {
                        AdditionalBases = new Dictionary<string, List<PasteOption>>
                        {
                            ["RaidBase1"] = DefaultPasteOptions,
                            ["RaidBase2"] = DefaultPasteOptions,
                            ["RaidBase3"] = DefaultPasteOptions,
                            ["RaidBase4"] = DefaultPasteOptions,
                            ["RaidBase5"] = DefaultPasteOptions
                        },
                        PasteOptions = DefaultPasteOptions
                    }
                };
            }
        }

        private static List<TreasureItem> DefaultLoot
        {
            get
            {
                return new List<TreasureItem>
                {
                    new TreasureItem { shortname = "ammo.pistol", amount = 40, skin = 0, amountMin = 40 },
                    new TreasureItem { shortname = "ammo.pistol.fire", amount = 40, skin = 0, amountMin = 40 },
                    new TreasureItem { shortname = "ammo.pistol.hv", amount = 40, skin = 0, amountMin = 40 },
                    new TreasureItem { shortname = "ammo.rifle", amount = 60, skin = 0, amountMin = 60 },
                    new TreasureItem { shortname = "ammo.rifle.explosive", amount = 60, skin = 0, amountMin = 60 },
                    new TreasureItem { shortname = "ammo.rifle.hv", amount = 60, skin = 0, amountMin = 60 },
                    new TreasureItem { shortname = "ammo.rifle.incendiary", amount = 60, skin = 0, amountMin = 60 },
                    new TreasureItem { shortname = "ammo.shotgun", amount = 24, skin = 0, amountMin = 24 },
                    new TreasureItem { shortname = "ammo.shotgun.slug", amount = 40, skin = 0, amountMin = 40 },
                    new TreasureItem { shortname = "surveycharge", amount = 20, skin = 0, amountMin = 20 },
                    new TreasureItem { shortname = "bucket.helmet", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "cctv.camera", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "coffeecan.helmet", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "explosive.timed", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "metal.facemask", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "metal.plate.torso", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "mining.quarry", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "pistol.m92", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "rifle.ak", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "rifle.bolt", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "rifle.lr300", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "shotgun.pump", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "shotgun.spas12", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "smg.2", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "smg.mp5", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "smg.thompson", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "supply.signal", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "targeting.computer", amount = 1, skin = 0, amountMin = 1 },
                    new TreasureItem { shortname = "metal.refined", amount = 150, skin = 0, amountMin = 150 },
                    new TreasureItem { shortname = "stones", amount = 15000, skin = 0, amountMin = 7500 },
                    new TreasureItem { shortname = "sulfur", amount = 7500, skin = 0, amountMin = 2500 },
                    new TreasureItem { shortname = "metal.fragments", amount = 7500, skin = 0, amountMin = 2500 },
                    new TreasureItem { shortname = "charcoal", amount = 5000, skin = 0, amountMin = 1000 },
                    new TreasureItem { shortname = "gunpowder", amount = 3500, skin = 0, amountMin = 1000 },
                    new TreasureItem { shortname = "scrap", amount = 150, skin = 0, amountMin = 100 },
                };
            }
        }

        public class PluginSettingsBaseBlocks
        {
            [JsonProperty(PropertyName = "Block Clans From Owning More Than One Raid")]
            public bool BlockClans { get; set; }

            [JsonProperty(PropertyName = "Block Friends From Owning More Than One Raid")]
            public bool BlockFriends { get; set; }

            [JsonProperty(PropertyName = "Block Teams From Owning More Than One Raid")]
            public bool BlockTeams { get; set; }

            public bool IsBlocking() => BlockClans || BlockFriends || BlockTeams;
        }

        public class PluginSettingsColors1
        {
            [JsonProperty(PropertyName = "Normal")]
            public string Normal { get; set; } = "000000";
        }

        public class PluginSettingsColors2
        {
            [JsonProperty(PropertyName = "Normal")]
            public string Normal { get; set; } = "00FF00";
        }

        public class PluginSettingsBaseManagementMountables
        {
            [JsonProperty(PropertyName = "All Controlled Mounts")]
            public bool ControlledMounts { get; set; }

            [JsonProperty(PropertyName = "Boats")]
            public bool Boats { get; set; }

            [JsonProperty(PropertyName = "Cars (Basic)")]
            public bool BasicCars { get; set; }

            [JsonProperty(PropertyName = "Cars (Modular)")]
            public bool ModularCars { get; set; }

            [JsonProperty(PropertyName = "Chinook")]
            public bool CH47 { get; set; }

            [JsonProperty(PropertyName = "Horses")]
            public bool Horses { get; set; }

            [JsonProperty(PropertyName = "MiniCopters")]
            public bool MiniCopters { get; set; }

            [JsonProperty(PropertyName = "Pianos")]
            public bool Pianos { get; set; } = true;

            [JsonProperty(PropertyName = "Scrap Transport Helicopters")]
            public bool Scrap { get; set; }
        }

        public class PluginSettingsBaseManagement
        {
            [JsonProperty(PropertyName = "Eject Mounts")]
            public PluginSettingsBaseManagementMountables Mounts { get; set; } = new PluginSettingsBaseManagementMountables();

            [JsonProperty(PropertyName = "Entities Not Allowed To Be Picked Up", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> BlacklistedPickupItems { get; set; } = new List<string> { "generator.small", "generator.static", "autoturret_deployed" };

            [JsonProperty(PropertyName = "Additional Containers To Include As Boxes", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Inherit { get; set; } = new List<string>();

            [JsonProperty(PropertyName = "Difficulty Colors (Border)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public PluginSettingsColors1 Colors1 { get; set; } = new PluginSettingsColors1();

            [JsonProperty(PropertyName = "Difficulty Colors (Inner)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public PluginSettingsColors2 Colors2 { get; set; } = new PluginSettingsColors2();

            [JsonProperty(PropertyName = "Allow Teleport")]
            public bool AllowTeleport { get; set; }

            [JsonProperty(PropertyName = "Allow Cupboard Loot To Drop")]
            public bool AllowCupboardLoot { get; set; } = true;

            [JsonProperty(PropertyName = "Allow Players To Build")]
            public bool AllowBuilding { get; set; } = true;

            [JsonProperty(PropertyName = "Allow Players To Use Ladders")]
            public bool AllowLadders { get; set; } = true;

            [JsonProperty(PropertyName = "Allow Player Bags To Be Lootable At PVP Bases")]
            public bool PlayersLootableInPVP { get; set; } = true;

            [JsonProperty(PropertyName = "Allow Player Bags To Be Lootable At PVE Bases")]
            public bool PlayersLootableInPVE { get; set; } = true;

            [JsonProperty(PropertyName = "Allow Traps To Drop Loot")]
            public bool DropLootTraps { get; set; }

            [JsonProperty(PropertyName = "Allow Players To Loot Traps")]
            public bool LootableTraps { get; set; }

            [JsonProperty(PropertyName = "Allow Raid Bases On Roads")]
            public bool AllowOnRoads { get; set; } = true;

            [JsonProperty(PropertyName = "Allow Raid Bases On Rivers")]
            public bool AllowOnRivers { get; set; } = true;

            [JsonProperty(PropertyName = "Allow Raid Bases On Building Topology")]
            public bool AllowOnBuildingTopology { get; set; } = true;

            [JsonProperty(PropertyName = "Allow Vending Machines To Broadcast")]
            public bool AllowBroadcasting { get; set; }

            [JsonProperty(PropertyName = "Allow Bases To Float Above Water")]
            public bool Submerged { get; set; }

            [JsonProperty(PropertyName = "Allow Bases To Spawn On The Seabed")]
            public bool Seabed { get; set; }

            [JsonProperty(PropertyName = "Prevent Bases From Floating Above Water By Also Checking Surrounding Area")]
            public bool SubmergedAreaCheck { get; set; }

            [JsonProperty(PropertyName = "Maximum Water Depth Level Used For Float Above Water Option")]
            public float WaterDepth { get; set; } = 1f;

            [JsonProperty(PropertyName = "Backpacks Can Be Opened At PVE Bases")]
            public bool BackpacksOpenPVE { get; set; } = true;

            [JsonProperty(PropertyName = "Backpacks Can Be Opened At PVP Bases")]
            public bool BackpacksOpenPVP { get; set; } = true;

            [JsonProperty(PropertyName = "Backpacks Drop At PVE Bases")]
            public bool BackpacksPVE { get; set; }

            [JsonProperty(PropertyName = "Backpacks Drop At PVP Bases")]
            public bool BackpacksPVP { get; set; }

            [JsonProperty(PropertyName = "Block Mounted Damage To Bases And Players")]
            public bool BlockMounts { get; set; }

            [JsonProperty(PropertyName = "Block RestoreUponDeath Plugin For PVP Bases")]
            public bool BlockRestorePVP { get; set; }

            [JsonProperty(PropertyName = "Block RestoreUponDeath Plugin For PVE Bases")]
            public bool BlockRestorePVE { get; set; }

            [JsonProperty(PropertyName = "Bypass Lock Treasure To First Attacker For PVE Bases")]
            public bool BypassUseOwnersForPVE { get; set; }

            [JsonProperty(PropertyName = "Bypass Lock Treasure To First Attacker For PVP Bases")]
            public bool BypassUseOwnersForPVP { get; set; }

            [JsonProperty(PropertyName = "Delay Between Entity Death While Despawning Base")]
            public float InvokeTime { get; set; } = 0.002f;

            [JsonProperty(PropertyName = "Despawn Spawned Mounts")]
            public bool DespawnMounts { get; set; } = true;

            [JsonProperty(PropertyName = "Do Not Destroy Player Built Deployables")]
            public bool DoNotDestroyDeployables { get; set; } = true;

            [JsonProperty(PropertyName = "Do Not Destroy Player Built Structures")]
            public bool DoNotDestroyStructures { get; set; } = true;

            [JsonProperty(PropertyName = "Divide Rewards Among All Raiders")]
            public bool DivideRewards { get; set; } = true;

            [JsonProperty(PropertyName = "Draw Corpse Time (Seconds)")]
            public float DrawTime { get; set; } = 300f;

            [JsonProperty(PropertyName = "Eject Sleepers Before Spawning Base")]
            public bool EjectSleepers { get; set; } = true;

            [JsonProperty(PropertyName = "Extra Distance To Spawn From Monuments")]
            public float MonumentDistance { get; set; }

            [JsonProperty(PropertyName = "Maximum Elevation Level")]
            public float Elevation { get; set; } = 2.5f;

            [JsonProperty(PropertyName = "Move Cookables Into Ovens")]
            public bool Cook { get; set; } = true;

            [JsonProperty(PropertyName = "Move Food Into BBQ Or Fridge")]
            public bool Food { get; set; } = true;

            [JsonProperty(PropertyName = "Move Resources Into Tool Cupboard")]
            public bool Cupboard { get; set; } = true;

            [JsonProperty(PropertyName = "Move Items Into Lockers")]
            public bool Lockers { get; set; } = true;

            [JsonProperty(PropertyName = "Lock Treasure To First Attacker")]
            public bool UseOwners { get; set; } = true;

            [JsonProperty(PropertyName = "Lock Treasure Max Inactive Time (Minutes)")]
            public float LockTime { get; set; } = 10f;

            [JsonProperty(PropertyName = "Minutes Until Despawn After Looting (min: 1)")]
            public int DespawnMinutes { get; set; } = 15;

            [JsonProperty(PropertyName = "Minutes Until Despawn After Inactive (0 = disabled)")]
            public int DespawnMinutesInactive { get; set; } = 45;

            [JsonProperty(PropertyName = "Minutes Until Despawn After Inactive Resets When Damaged")]
            public bool DespawnMinutesInactiveReset { get; set; } = true;

            [JsonProperty(PropertyName = "Mounts Can Take Damage From Players")]
            public bool MountDamageFromPlayers { get; set; }

            [JsonProperty(PropertyName = "Mounts Can Take Damage From SamSites")]
            public bool MountDamageFromSamSites { get; set; } = true;

            [JsonProperty(PropertyName = "Player Cupboard Detection Radius")]
            public float CupboardDetectionRadius { get; set; } = 75f;

            [JsonProperty(PropertyName = "Players With PVP Delay Can Damage Anything Inside Zone")]
            public bool PVPDelayDamageInside { get; set; }

            [JsonProperty(PropertyName = "Players With PVP Delay Can Damage Other Players With PVP Delay Anywhere")]
            public bool PVPDelayAnywhere { get; set; }

            [JsonProperty(PropertyName = "PVP Delay Between Zone Hopping")]
            public float PVPDelay { get; set; } = 10f;

            [JsonProperty(PropertyName = "Prevent Fire From Spreading")]
            public bool PreventFireFromSpreading { get; set; } = true;

            [JsonProperty(PropertyName = "Prevent Players From Hogging Raids")]
            public bool PreventHogging { get; set; } = true;

            [JsonProperty(PropertyName = "Require Cupboard To Be Looted Before Despawning")]
            public bool RequireCupboardLooted { get; set; }

            [JsonProperty(PropertyName = "Destroying The Cupboard Completes The Raid")]
            public bool EndWhenCupboardIsDestroyed { get; set; }

            [JsonProperty(PropertyName = "Require All Bases To Spawn Before Respawning An Existing Base")]
            public bool RequireAllSpawned { get; set; }

            [JsonProperty(PropertyName = "Turn Lights On At Night")]
            public bool Lights { get; set; } = true;

            [JsonProperty(PropertyName = "Turn Lights On Indefinitely")]
            public bool AlwaysLights { get; set; }

            [JsonProperty(PropertyName = "Traps And Turrets Ignore Users Using NOCLIP")]
            public bool IgnoreFlying { get; set; }

            [JsonProperty(PropertyName = "Use Random Codes On Code Locks")]
            public bool RandomCodes { get; set; } = true;

            [JsonProperty(PropertyName = "Wait To Start Despawn Timer When Base Takes Damage From Player")]
            public bool Engaged { get; set; }
        }

        public class PluginSettingsMapMarkers
        {
            [JsonProperty(PropertyName = "Marker Name")]
            public string MarkerName { get; set; } = "Raidable Base Event";

            [JsonProperty(PropertyName = "Radius")]
            public float Radius { get; set; } = 0.25f;

            [JsonProperty(PropertyName = "Use Vending Map Marker")]
            public bool UseVendingMarker { get; set; } = true;

            [JsonProperty(PropertyName = "Use Explosion Map Marker")]
            public bool UseExplosionMarker { get; set; }

            [JsonProperty(PropertyName = "Create Markers For Maintained Events")]
            public bool Maintained { get; set; } = true;

            [JsonProperty(PropertyName = "Create Markers For Scheduled Events")]
            public bool Scheduled { get; set; } = true;

            [JsonProperty(PropertyName = "Create Markers For Manual Events")]
            public bool Manual { get; set; } = true;
        }

        public class PluginSettings
        {
            [JsonProperty(PropertyName = "Raid Management")]
            public PluginSettingsBaseManagement Management { get; set; } = new PluginSettingsBaseManagement();

            [JsonProperty(PropertyName = "Map Markers")]
            public PluginSettingsMapMarkers Markers { get; set; } = new PluginSettingsMapMarkers();

            [JsonProperty(PropertyName = "Maintained Events")]
            public RaidableBaseSettingsMaintained Maintained { get; set; } = new RaidableBaseSettingsMaintained();

            [JsonProperty(PropertyName = "Manual Events")]
            public RaidableBaseSettingsManual Manual { get; set; } = new RaidableBaseSettingsManual();

            [JsonProperty(PropertyName = "Scheduled Events")]
            public RaidableBaseSettingsScheduled Schedule { get; set; } = new RaidableBaseSettingsScheduled();

            [JsonProperty(PropertyName = "Allowed Zone Manager Zones", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Inclusions { get; set; } = new List<string> { "pvp", "99999999" };

            [JsonProperty(PropertyName = "Blacklisted Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> BlacklistedCommands { get; set; } = new List<string>();

            [JsonProperty(PropertyName = "Amount Of Entities To Undo Per Batch (1 = Slowest But Better Performance)")]
            public int BatchLimit { get; set; } = 5;

            [JsonProperty(PropertyName = "Automatically Teleport Admins To Their Map Marker Positions")]
            public bool TeleportMarker { get; set; } = true;

            [JsonProperty(PropertyName = "Block Wizardry Plugin At Events")]
            public bool NoWizardry { get; set; }

            [JsonProperty(PropertyName = "Chat Steam64ID")]
            public ulong ChatID { get; set; }

            [JsonProperty(PropertyName = "Expansion Mode (Dangerous Treasures)")]
            public bool ExpansionMode { get; set; }

            [JsonProperty(PropertyName = "Remove Admins From Raiders List")]
            public bool RemoveAdminRaiders { get; set; }

            [JsonProperty(PropertyName = "Show X Z Coordinates")]
            public bool ShowXZ { get; set; }

            [JsonProperty(PropertyName = "Event Command")]
            public string EventCommand { get; set; } = "rbe";

            [JsonProperty(PropertyName = "Hunter Command")]
            public string HunterCommand { get; set; } = "rb";

            [JsonProperty(PropertyName = "Server Console Command")]
            public string ConsoleCommand { get; set; } = "rbevent";
        }

        public class EventMessageSettings
        {
            [JsonProperty(PropertyName = "Announce Raid Unlocked")]
            public bool AnnounceRaidUnlock { get; set; }

            [JsonProperty(PropertyName = "Announce Thief Message")]
            public bool AnnounceThief { get; set; } = true;

            [JsonProperty(PropertyName = "Announce PVE/PVP Enter/Exit Messages")]
            public bool AnnounceEnterExit { get; set; } = true;

            [JsonProperty(PropertyName = "Show Destroy Warning")]
            public bool ShowWarning { get; set; } = true;

            [JsonProperty(PropertyName = "Show Opened Message")]
            public bool Opened { get; set; } = true;

            [JsonProperty(PropertyName = "Show Prefix")]
            public bool Prefix { get; set; } = true;
        }

        public class GUIAnnouncementSettings
        {
            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled { get; set; }

            [JsonProperty(PropertyName = "Banner Tint Color")]
            public string TintColor { get; set; } = "Grey";

            [JsonProperty(PropertyName = "Maximum Distance")]
            public float Distance { get; set; } = 300f;

            [JsonProperty(PropertyName = "Text Color")]
            public string TextColor { get; set; } = "White";
        }

        public class LustyMapSettings
        {
            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled { get; set; }

            [JsonProperty(PropertyName = "Icon File")]
            public string IconFile { get; set; } = "http://i.imgur.com/XoEMTJj.png";

            [JsonProperty(PropertyName = "Icon Name")]
            public string IconName { get; set; } = "rbevent";

            [JsonProperty(PropertyName = "Icon Rotation")]
            public float IconRotation { get; set; }
        }

        public class NpcSettings
        {
            [JsonProperty(PropertyName = "Murderer Items", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> MurdererItems { get; set; } = new List<string> { "metal.facemask", "metal.plate.torso", "pants", "tactical.gloves", "boots.frog", "tshirt", "machete" };

            [JsonProperty(PropertyName = "Scientist Items", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> ScientistItems { get; set; } = new List<string> { "hazmatsuit_scientist", "rifle.ak" };

            [JsonProperty(PropertyName = "Murderer Kits", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> MurdererKits { get; set; } = new List<string> { "murderer_kit_1", "murderer_kit_2" };

            [JsonProperty(PropertyName = "Scientist Kits", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> ScientistKits { get; set; } = new List<string> { "scientist_kit_1", "scientist_kit_2" };

            [JsonProperty(PropertyName = "Random Names", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> RandomNames { get; set; } = new List<string>();

            //[JsonProperty(PropertyName = "Attempt To Mount Objects Inside Base")]
            //public bool Mount { get; set; }

            [JsonProperty(PropertyName = "Amount To Spawn")]
            public int SpawnAmount { get; set; } = 3;

            [JsonProperty(PropertyName = "Aggression Range")]
            public float AggressionRange { get; set; } = 70f;

            [JsonProperty(PropertyName = "Despawn Inventory On Death")]
            public bool DespawnInventory { get; set; } = true;

            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled { get; set; } = true;

            [JsonProperty(PropertyName = "Health For Murderers (100 min, 5000 max)")]
            public float MurdererHealth { get; set; } = 150f;

            [JsonProperty(PropertyName = "Health For Scientists (100 min, 5000 max)")]
            public float ScientistHealth { get; set; } = 150f;

            [JsonProperty(PropertyName = "Minimum Amount To Spawn")]
            public int SpawnMinAmount { get; set; } = 1;

            [JsonProperty(PropertyName = "Use Dangerous Treasures NPCs")]
            public bool UseExpansionNpcs { get; set; }

            [JsonProperty(PropertyName = "Spawn Murderers And Scientists")]
            public bool SpawnBoth { get; set; } = true;

            [JsonProperty(PropertyName = "Scientist Weapon Accuracy (0 - 100)")]
            public float Accuracy { get; set; } = 30f;

            [JsonProperty(PropertyName = "Spawn Murderers")]
            public bool SpawnMurderers { get; set; }

            [JsonProperty(PropertyName = "Spawn Random Amount")]
            public bool SpawnRandomAmount { get; set; }

            [JsonProperty(PropertyName = "Spawn Scientists Only")]
            public bool SpawnScientistsOnly { get; set; }

            [JsonProperty(PropertyName = "Player Traps And Turrets Ignore Npcs")]
            public bool IgnoreTrapsTurrets { get; set; }
        }

        public class PasteOption
        {
            [JsonProperty(PropertyName = "Option")]
            public string Key { get; set; }

            [JsonProperty(PropertyName = "Value")]
            public string Value { get; set; }
        }

        public class BuildingLevelOne
        {
            [JsonProperty(PropertyName = "Amount (0 = disabled)")]
            public int Amount { get; set; }

            [JsonProperty(PropertyName = "Chance To Play")]
            public float Chance { get; set; } = 0.5f;
        }

        public class BuildingLevels
        {
            [JsonProperty(PropertyName = "Level 1 - Play With Fire")]
            public BuildingLevelOne Level1 { get; set; } = new BuildingLevelOne();

            [JsonProperty(PropertyName = "Level 2 - Final Death")]
            public bool Level2 { get; set; }
        }

        public class BuildingGradeLevels
        {
            [JsonProperty(PropertyName = "Wooden")]
            public bool Wooden { get; set; }

            [JsonProperty(PropertyName = "Stone")]
            public bool Stone { get; set; }

            [JsonProperty(PropertyName = "Metal")]
            public bool Metal { get; set; }

            [JsonProperty(PropertyName = "HQM")]
            public bool HQM { get; set; }

            public bool Any() => Wooden || Stone || Metal || HQM;
        }

        public class BuildingOptionsAutoTurrets
        {
            [JsonProperty(PropertyName = "Aim Cone")]
            public float AimCone { get; set; } = 5f;

            [JsonProperty(PropertyName = "Minimum Damage Modifier")]
            public float Min { get; set; } = 1f;

            [JsonProperty(PropertyName = "Maximum Damage Modifier")]
            public float Max { get; set; } = 1f;

            [JsonProperty(PropertyName = "Start Health")]
            public float Health { get; set; } = 1000f;

            [JsonProperty(PropertyName = "Sight Range")]
            public float SightRange { get; set; } = 30f;

            [JsonProperty(PropertyName = "Double Sight Range When Shot")]
            public bool AutoAdjust { get; set; }

            [JsonProperty(PropertyName = "Set Hostile (False = Do Not Set Any Mode)")]
            public bool Hostile { get; set; } = true;

            [JsonProperty(PropertyName = "Requires Power Source")]
            public bool RequiresPower { get; set; }

            [JsonProperty(PropertyName = "Remove Equipped Weapon")]
            public bool RemoveWeapon { get; set; }

            [JsonProperty(PropertyName = "Random Weapons To Equip When Unequipped", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Shortnames { get; set; } = new List<string> { "rifle.ak" };
        }

        public class BuildingOptions
        {
            [JsonProperty(PropertyName = "Additional Bases For This Difficulty", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, List<PasteOption>> AdditionalBases { get; set; } = new Dictionary<string, List<PasteOption>>();

            [JsonProperty(PropertyName = "Paste Options", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<PasteOption> PasteOptions { get; set; } = new List<PasteOption>();

            [JsonProperty(PropertyName = "Arena Walls")]
            public RaidableBaseWallOptions ArenaWalls { get; set; } = new RaidableBaseWallOptions();

            [JsonProperty(PropertyName = "NPC Levels")]
            public BuildingLevels Levels { get; set; } = new BuildingLevels();

            [JsonProperty(PropertyName = "NPCs")]
            public NpcSettings NPC { get; set; } = new NpcSettings();

            [JsonProperty(PropertyName = "Rewards")]
            public RewardSettings Rewards { get; set; } = new RewardSettings();

            [JsonProperty(PropertyName = "Change Building Material Tier To")]
            public BuildingGradeLevels Tiers { get; set; } = new BuildingGradeLevels();

            [JsonProperty(PropertyName = "Auto Turrets")]
            public BuildingOptionsAutoTurrets AutoTurret { get; set; } = new BuildingOptionsAutoTurrets();

            [JsonProperty(PropertyName = "Loot (Empty List = Use Treasure Loot)", NullValueHandling = NullValueHandling.Ignore)]
            public List<TreasureItem> Loot { get; set; } = new List<TreasureItem>();

            [JsonProperty(PropertyName = "Profile Enabled")]
            public bool Enabled { get; set; } = true;

            [JsonProperty(PropertyName = "Add Code Lock To Unlocked Or KeyLocked Doors")]
            public bool DoorLock { get; set; } = true;

            [JsonProperty(PropertyName = "Add Code Lock To Tool Cupboards")]
            public bool LockPrivilege { get; set; }

            [JsonProperty(PropertyName = "Add Code Lock To Boxes")]
            public bool LockBoxes { get; set; }

            [JsonProperty(PropertyName = "Close Open Doors With No Door Controller Installed")]
            public bool CloseOpenDoors { get; set; } = true;

            [JsonProperty(PropertyName = "Allow Duplicate Items")]
            public bool AllowDuplicates { get; set; }

            [JsonProperty(PropertyName = "Allow Players To Pickup Deployables")]
            public bool AllowPickup { get; set; }

            [JsonProperty(PropertyName = "Allow Players To Deploy A Cupboard")]
            public bool AllowBuildingPriviledges { get; set; } = true;

            [JsonProperty(PropertyName = "Allow PVP")]
            public bool AllowPVP { get; set; } = true;

            [JsonProperty(PropertyName = "Allow Friendly Fire (Teams)")]
            public bool AllowFriendlyFire { get; set; } = true;

            [JsonProperty(PropertyName = "Minimum Amount Of Items To Spawn (0 = Use Max Value)")]
            public int MinTreasure { get; set; }

            [JsonProperty(PropertyName = "Amount Of Items To Spawn")]
            public int MaxTreasure { get; set; } = 30;

            [JsonProperty(PropertyName = "Flame Turret Health")]
            public float FlameTurretHealth { get; set; } = 300f;

            [JsonProperty(PropertyName = "Block Plugins Which Prevent Item Durability Loss")]
            public bool EnforceDurability { get; set; }

            [JsonProperty(PropertyName = "Block Damage Outside Of The Dome To Players Inside")]
            public bool BlockOutsideDamageToPlayersInside { get; set; }

            [JsonProperty(PropertyName = "Block Damage Outside Of The Dome To Bases Inside")]
            public bool BlockOutsideDamageToBaseInside { get; set; }

            [JsonProperty(PropertyName = "Block Damage Inside From Npcs To Players Outside")]
            public bool BlockNpcDamageToPlayersOutside { get; set; }

            [JsonProperty(PropertyName = "Building Blocks Are Immune To Damage")]
            public bool BlocksImmune { get; set; }

            [JsonProperty(PropertyName = "Boxes Are Invulnerable")]
            public bool Invulnerable { get; set; }

            [JsonProperty(PropertyName = "Spawn Silently (No Notifcation, No Dome, No Map Marker)")]
            public bool Silent { get; set; }

            [JsonProperty(PropertyName = "Divide Loot Into All Containers")]
            public bool DivideLoot { get; set; } = true;

            [JsonProperty(PropertyName = "Drop Container Loot X Seconds After It Is Looted")]
            public float DropTimeAfterLooting { get; set; }

            [JsonProperty(PropertyName = "Drop Container Loot Applies Only To Boxes And Cupboards")]
            public bool DropOnlyBoxesAndPrivileges { get; set; } = true;

            [JsonProperty(PropertyName = "Create Dome Around Event Using Spheres (0 = disabled, recommended = 5)")]
            public int SphereAmount { get; set; } = 5;

            [JsonProperty(PropertyName = "Empty All Containers Before Spawning Loot")]
            public bool EmptyAll { get; set; } = true;

            [JsonProperty(PropertyName = "Eject Corpses From Enemy Raids (Advanced Users Only)")]
            public bool EjectCorpses { get; set; } = true;

            [JsonProperty(PropertyName = "Eject Enemies From Locked PVE Raids")]
            public bool EjectLockedPVE { get; set; } = true;

            [JsonProperty(PropertyName = "Eject Enemies From Locked PVP Raids")]
            public bool EjectLockedPVP { get; set; }

            [JsonProperty(PropertyName = "Explosion Damage Modifier (0-999)")]
            public float ExplosionModifier { get; set; } = 100f;

            [JsonProperty(PropertyName = "Force All Boxes To Have Same Skin")]
            public bool SetSkins { get; set; } = true;

            [JsonProperty(PropertyName = "Ignore Containers That Spawn With Loot Already")]
            public bool IgnoreContainedLoot { get; set; }

            [JsonProperty(PropertyName = "Loot Amount Multiplier")]
            public float Multiplier { get; set; } = 1f;

            [JsonProperty(PropertyName = "Maximum Respawn Npc X Seconds After Death")]
            public float RespawnRateMax { get; set; }

            [JsonProperty(PropertyName = "Minimum Respawn Npc X Seconds After Death")]
            public float RespawnRateMin { get; set; }

            [JsonProperty(PropertyName = "Protection Radius")]
            public float ProtectionRadius { get; set; } = 50f;

            [JsonProperty(PropertyName = "Penalize Players On Death In PVE (ZLevels)")]
            public bool PenalizePVE { get; set; } = true;

            [JsonProperty(PropertyName = "Penalize Players On Death In PVP (ZLevels)")]
            public bool PenalizePVP { get; set; } = true;

            [JsonProperty(PropertyName = "Require Cupboard Access To Loot")]
            public bool RequiresCupboardAccess { get; set; }

            [JsonProperty(PropertyName = "Require Cupboard Access To Place Ladders")]
            public bool RequiresCupboardAccessLadders { get; set; }

            [JsonProperty(PropertyName = "Skip Treasure Loot And Use Loot In Base Only")]
            public bool SkipTreasureLoot { get; set; }

            [JsonProperty(PropertyName = "Always Spawn Base Loot Table")]
            public bool Prioritize { get; set; }

            public static BuildingOptions Clone(BuildingOptions options)
            {
                return options.MemberwiseClone() as BuildingOptions;
            }
        }

        public class RaidableBaseSettingsScheduled
        {
            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled { get; set; }

            [JsonProperty(PropertyName = "Chance To Randomly Spawn PVP Bases (0 = Ignore Setting)")]
            public double Chance { get; set; }

            [JsonProperty(PropertyName = "Convert PVE To PVP")]
            public bool ConvertPVE { get; set; }

            [JsonProperty(PropertyName = "Convert PVP To PVE")]
            public bool ConvertPVP { get; set; }

            [JsonProperty(PropertyName = "Every Min Seconds")]
            public double IntervalMin { get; set; } = 3600f;

            [JsonProperty(PropertyName = "Every Max Seconds")]
            public double IntervalMax { get; set; } = 7200f;

            [JsonProperty(PropertyName = "Include PVE Bases")]
            public bool IncludePVE { get; set; } = true;

            [JsonProperty(PropertyName = "Include PVP Bases")]
            public bool IncludePVP { get; set; } = true;

            [JsonProperty(PropertyName = "Ignore Safe Checks")]
            public bool Ignore { get; set; }

            [JsonProperty(PropertyName = "Ignore Player Entities At Custom Spawn Locations")]
            public bool Skip { get; set; }

            [JsonProperty(PropertyName = "Max Scheduled Events")]
            public int Max { get; set; } = 1;

            [JsonProperty(PropertyName = "Max To Spawn At Once (0 = Use Max Scheduled Events Amount)")]
            public int MaxOnce { get; set; }

            [JsonProperty(PropertyName = "Minimum Required Players Online")]
            public int PlayerLimit { get; set; } = 1;

            [JsonProperty(PropertyName = "Spawn Bases X Distance Apart")]
            public float Distance { get; set; } = 100f;

            [JsonProperty(PropertyName = "Spawns Database File (Optional)")]
            public string SpawnsFile { get; set; } = "none";

            [JsonProperty(PropertyName = "Time To Wait Between Spawns")]
            public float Time { get; set; } = 15f;
        }

        public class RaidableBaseSettingsMaintained
        {
            [JsonProperty(PropertyName = "Always Maintain Max Events")]
            public bool Enabled { get; set; }

            [JsonProperty(PropertyName = "Chance To Randomly Spawn PVP Bases (0 = Ignore Setting)")]
            public double Chance { get; set; }

            [JsonProperty(PropertyName = "Convert PVE To PVP")]
            public bool ConvertPVE { get; set; }

            [JsonProperty(PropertyName = "Convert PVP To PVE")]
            public bool ConvertPVP { get; set; }

            [JsonProperty(PropertyName = "Include PVE Bases")]
            public bool IncludePVE { get; set; } = true;

            [JsonProperty(PropertyName = "Include PVP Bases")]
            public bool IncludePVP { get; set; } = true;

            [JsonProperty(PropertyName = "Ignore Safe Checks")]
            public bool Ignore { get; set; }

            [JsonProperty(PropertyName = "Ignore Player Entities At Custom Spawn Locations")]
            public bool Skip { get; set; }

            [JsonProperty(PropertyName = "Minimum Required Players Online")]
            public int PlayerLimit { get; set; } = 1;

            [JsonProperty(PropertyName = "Max Maintained Events")]
            public int Max { get; set; } = 1;

            [JsonProperty(PropertyName = "Spawn Bases X Distance Apart")]
            public float Distance { get; set; } = 100f;

            [JsonProperty(PropertyName = "Spawns Database File (Optional)")]
            public string SpawnsFile { get; set; } = "none";

            [JsonProperty(PropertyName = "Time To Wait Between Spawns")]
            public float Time { get; set; } = 15f;
        }

        public class RaidableBaseSettingsManual
        {
            [JsonProperty(PropertyName = "Convert PVE To PVP")]
            public bool ConvertPVE { get; set; }

            [JsonProperty(PropertyName = "Convert PVP To PVE")]
            public bool ConvertPVP { get; set; }

            [JsonProperty(PropertyName = "Max Manual Events")]
            public int Max { get; set; } = 1;

            [JsonProperty(PropertyName = "Spawn Bases X Distance Apart")]
            public float Distance { get; set; } = 100f;

            [JsonProperty(PropertyName = "Spawns Database File (Optional)")]
            public string SpawnsFile { get; set; } = "none";
        }

        public class RaidableBaseSettings
        {
            [JsonProperty(PropertyName = "Buildings", NullValueHandling = NullValueHandling.Ignore)]
            public Dictionary<string, BuildingOptions> Buildings { get; set; }
        }

        public class RaidableBaseWallOptions
        {
            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled { get; set; } = true;

            [JsonProperty(PropertyName = "Extra Stacks")]
            public int Stacks { get; set; } = 1;

            [JsonProperty(PropertyName = "Use Stone Walls")]
            public bool Stone { get; set; } = true;

            [JsonProperty(PropertyName = "Use Iced Walls")]
            public bool Ice { get; set; }

            [JsonProperty(PropertyName = "Use Least Amount Of Walls")]
            public bool LeastAmount { get; set; } = true;

            [JsonProperty(PropertyName = "Use UFO Walls")]
            public bool UseUFOWalls { get; set; }

            [JsonProperty(PropertyName = "Radius")]
            public float Radius { get; set; } = 25f;
        }

        public class RaidableBaseEconomicsOptions
        {
            [JsonProperty(PropertyName = "Amount")]
            public double Amount { get; set; }

            [JsonIgnore]
            public bool Any
            {
                get
                {
                    return Amount > 0;
                }
            }
        }

        public class RaidableBaseServerRewardsOptions
        {
            [JsonProperty(PropertyName = "Amount")]
            public int Amount { get; set; }
            
            [JsonIgnore]
            public bool Any
            {
                get
                {
                    return Amount > 0;
                }
            }
        }

        public class RankedLadderSettings
        {
            [JsonProperty(PropertyName = "Award Top X Players On Wipe")]
            public int Amount { get; set; } = 3;

            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled { get; set; } = true;
        }

        public class RewardSettings
        {
            [JsonProperty(PropertyName = "Economics Money")]
            public double Money { get; set; }

            [JsonProperty(PropertyName = "ServerRewards Points")]
            public int Points { get; set; }
        }

        public class SkinSettingsDefault
        {
            [JsonProperty(PropertyName = "Include Workshop Skins")]
            public bool RandomWorkshopSkins { get; set; } = true;

            [JsonProperty(PropertyName = "Preset Skin")]
            public ulong PresetSkin { get; set; }

            [JsonProperty(PropertyName = "Use Random Skin")]
            public bool RandomSkins { get; set; } = true;
        }

        public class SkinSettingsLoot
        {
            [JsonProperty(PropertyName = "Include Workshop Skins")]
            public bool RandomWorkshopSkins { get; set; } = true;

            [JsonProperty(PropertyName = "Use Random Skin")]
            public bool RandomSkins { get; set; } = true;
        }

        public class SkinSettingsDeployables
        {
            [JsonProperty(PropertyName = "Partial Names", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Names { get; set; } = new List<string>
            {
                "door", "barricade", "chair", "fridge", "furnace", "locker", "reactivetarget", "rug", "sleepingbag", "table", "vendingmachine", "waterpurifier", "skullspikes", "skulltrophy", "summer_dlc", "sled"
            };

            [JsonProperty(PropertyName = "Include Workshop Skins")]
            public bool RandomWorkshopSkins { get; set; } = true;

            [JsonProperty(PropertyName = "Use Random Skin")]
            public bool RandomSkins { get; set; } = true;

            [JsonProperty(PropertyName = "Skin Everything")]
            public bool Everything { get; set; } = true;
        }

        public class SkinSettings
        {
            [JsonProperty(PropertyName = "Boxes")]
            public SkinSettingsDefault Boxes { get; set; } = new SkinSettingsDefault();

            [JsonProperty(PropertyName = "Loot Items")]
            public SkinSettingsLoot Loot { get; set; } = new SkinSettingsLoot();

            [JsonProperty(PropertyName = "Deployables")]
            public SkinSettingsDeployables Deployables { get; set; } = new SkinSettingsDeployables();

            [JsonProperty(PropertyName = "Randomize Npc Item Skins")]
            public bool Npcs { get; set; } = true;

            [JsonProperty(PropertyName = "Ignore If Skinned Already")]
            public bool IgnoreSkinned { get; set; } = true;
        }

        public class TreasureItem  //: IEquatable<TreasureItem>
        {
            public string shortname { get; set; }
            public int amount { get; set; }
            public ulong skin { get; set; }
            public int amountMin { get; set; }

            [JsonIgnore]
            public ItemDefinition definition { get; set; }

            [JsonIgnore]
            public bool isBlueprint { get; set; }

            [JsonIgnore]
            public bool modified { get; set; }

            //public override bool Equals(object obj)
            //{
            //    if (obj == null) return false;
            //    var other = obj as TreasureItem;
            //    if (other == null) return false;
            //    return Equals(other);
            //}

            //public override int GetHashCode()
            //{
            //    return amount + amountMin + (int)skin;
            //}

            //public bool Equals(TreasureItem other)
            //{
            //    return shortname == other.shortname && amount == other.amount && skin == other.skin && amountMin == other.amountMin;
            //}
        }

        public class TreasureSettings
        {
            [JsonProperty(PropertyName = "Resources Not Moved To Cupboards", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> ExcludeFromCupboard { get; set; } = new List<string>
            {
                "skull.human", "battery.small", "bone.fragments", "can.beans.empty", "can.tuna.empty", "water.salt", "water", "skull.wolf"
            };

            [JsonProperty(PropertyName = "Use Day Of Week Loot")]
            public bool UseDOWL { get; set; }
            
            [JsonProperty(PropertyName = "Day Of Week Loot Monday", NullValueHandling = NullValueHandling.Ignore)]
            public List<TreasureItem> DOWL_Monday { get; set; } = new List<TreasureItem>();

            [JsonProperty(PropertyName = "Day Of Week Loot Tuesday", NullValueHandling = NullValueHandling.Ignore)]
            public List<TreasureItem> DOWL_Tuesday { get; set; } = new List<TreasureItem>();

            [JsonProperty(PropertyName = "Day Of Week Loot Wednesday", NullValueHandling = NullValueHandling.Ignore)]
            public List<TreasureItem> DOWL_Wednesday { get; set; } = new List<TreasureItem>();

            [JsonProperty(PropertyName = "Day Of Week Loot Thursday", NullValueHandling = NullValueHandling.Ignore)]
            public List<TreasureItem> DOWL_Thursday { get; set; } = new List<TreasureItem>();

            [JsonProperty(PropertyName = "Day Of Week Loot Friday", NullValueHandling = NullValueHandling.Ignore)]
            public List<TreasureItem> DOWL_Friday { get; set; } = new List<TreasureItem>();

            [JsonProperty(PropertyName = "Day Of Week Loot Saturday", NullValueHandling = NullValueHandling.Ignore)]
            public List<TreasureItem> DOWL_Saturday { get; set; } = new List<TreasureItem>();

            [JsonProperty(PropertyName = "Day Of Week Loot Sunday", NullValueHandling = NullValueHandling.Ignore)]
            public List<TreasureItem> DOWL_Sunday { get; set; } = new List<TreasureItem>();

            [JsonProperty(PropertyName = "Loot (Easy Difficulty)", NullValueHandling = NullValueHandling.Ignore)]
            public List<TreasureItem> LootEasy { get; set; } = new List<TreasureItem>();

            [JsonProperty(PropertyName = "Loot (Medium Difficulty)", NullValueHandling = NullValueHandling.Ignore)]
            public List<TreasureItem> LootMedium { get; set; } = new List<TreasureItem>();

            [JsonProperty(PropertyName = "Loot (Hard Difficulty)", NullValueHandling = NullValueHandling.Ignore)]
            public List<TreasureItem> LootHard { get; set; } = new List<TreasureItem>();

            [JsonProperty(PropertyName = "Loot (Expert Difficulty)", NullValueHandling = NullValueHandling.Ignore)]
            public List<TreasureItem> LootExpert { get; set; } = new List<TreasureItem>();

            [JsonProperty(PropertyName = "Loot (Nightmare Difficulty)", NullValueHandling = NullValueHandling.Ignore)]
            public List<TreasureItem> LootNightmare { get; set; } = new List<TreasureItem>();

            [JsonProperty(PropertyName = "Loot", NullValueHandling = NullValueHandling.Ignore)]
            public List<TreasureItem> Loot { get; set; } = new List<TreasureItem>();

            [JsonProperty(PropertyName = "Do Not Duplicate Base Loot")]
            public bool UniqueBaseLoot { get; set; }

            [JsonProperty(PropertyName = "Do Not Duplicate Difficulty Loot")]
            public bool UniqueDifficultyLoot { get; set; }

            [JsonProperty(PropertyName = "Do Not Duplicate Default Loot")]
            public bool UniqueDefaultLoot { get; set; }

            [JsonProperty(PropertyName = "Use Stack Size Limit For Spawning Items")]
            public bool UseStackSizeLimit { get; set; }
        }

        public class TruePVESettings
        {
            [JsonProperty(PropertyName = "Allow PVP Server-Wide During Events")]
            public bool ServerWidePVP { get; set; }
        }
        
        public class UISettings
        {
            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled { get; set; } = true;

            [JsonProperty(PropertyName = "Anchor Min")]
            public string AnchorMin { get; set; } = "0.838 0.249";

            [JsonProperty(PropertyName = "Anchor Max")]
            public string AnchorMax { get; set; } = "0.986 0.284";

            [JsonProperty(PropertyName = "Font Size")]
            public int FontSize { get; set; } = 18;

            [JsonProperty(PropertyName = "Panel Alpha")]
            public float Alpha { get; set; } = 1f;

            [JsonProperty(PropertyName = "Panel Color")]
            public string PanelColor { get; set; } = "#000000";

            [JsonProperty(PropertyName = "PVP Color")]
            public string ColorPVP { get; set; } = "#FF0000";

            [JsonProperty(PropertyName = "PVE Color")]
            public string ColorPVE { get; set; } = "#008000";

            [JsonProperty(PropertyName = "Show Containers Left")]
            public bool Containers { get; set; }

            [JsonProperty(PropertyName = "Show Time Left")]
            public bool Time { get; set; } = true;
        }

        public class WeaponTypeStateSettings
        {
            [JsonProperty(PropertyName = "AutoTurret")]
            public bool AutoTurret { get; set; } = true;

            [JsonProperty(PropertyName = "FlameTurret")]
            public bool FlameTurret { get; set; } = true;

            [JsonProperty(PropertyName = "FogMachine")]
            public bool FogMachine { get; set; } = true;

            [JsonProperty(PropertyName = "GunTrap")]
            public bool GunTrap { get; set; } = true;

            [JsonProperty(PropertyName = "SamSite")]
            public bool SamSite { get; set; } = true;
        }

        public class WeaponTypeAmountSettings
        {
            [JsonProperty(PropertyName = "AutoTurret")]
            public int AutoTurret { get; set; } = 256;

            [JsonProperty(PropertyName = "FlameTurret")]
            public int FlameTurret { get; set; } = 256;

            [JsonProperty(PropertyName = "FogMachine")]
            public int FogMachine { get; set; } = 5;

            [JsonProperty(PropertyName = "GunTrap")]
            public int GunTrap { get; set; } = 128;

            [JsonProperty(PropertyName = "SamSite")]
            public int SamSite { get; set; } = 24;
        }

        public class WeaponSettingsTeslaCoil
        {
            [JsonProperty(PropertyName = "Requires A Power Source")]
            public bool RequiresPower { get; set; } = true;

            [JsonProperty(PropertyName = "Max Discharge Self Damage Seconds (0 = None, 120 = Rust default)")]
            public float MaxDischargeSelfDamageSeconds { get; set; }

            [JsonProperty(PropertyName = "Max Damage Output")]
            public float MaxDamageOutput { get; set; } = 35f;
        }

        public class WeaponSettings
        {
            [JsonProperty(PropertyName = "Infinite Ammo")]
            public WeaponTypeStateSettings InfiniteAmmo { get; set; } = new WeaponTypeStateSettings();

            [JsonProperty(PropertyName = "Ammo")]
            public WeaponTypeAmountSettings Ammo { get; set; } = new WeaponTypeAmountSettings();

            [JsonProperty(PropertyName = "Tesla Coil")]
            public WeaponSettingsTeslaCoil TeslaCoil { get; set; } = new WeaponSettingsTeslaCoil();

            [JsonProperty(PropertyName = "Fog Machine Allows Motion Toggle")]
            public bool FogMotion { get; set; } = true;

            [JsonProperty(PropertyName = "Fog Machine Requires A Power Source")]
            public bool FogRequiresPower { get; set; } = true;

            [JsonProperty(PropertyName = "SamSite Repairs Every X Minutes (0.0 = disabled)")]
            public float SamSiteRepair { get; set; } = 5f;

            [JsonProperty(PropertyName = "SamSite Range (350.0 = Rust default)")]
            public float SamSiteRange { get; set; } = 75f;
        }

        public class Configuration
        {
            [JsonProperty(PropertyName = "Settings")]
            public PluginSettings Settings = new PluginSettings();

            [JsonProperty(PropertyName = "Event Messages")]
            public EventMessageSettings EventMessages = new EventMessageSettings();

            [JsonProperty(PropertyName = "GUIAnnouncements")]
            public GUIAnnouncementSettings GUIAnnouncement = new GUIAnnouncementSettings();

            [JsonProperty(PropertyName = "Lusty Map")]
            public LustyMapSettings LustyMap = new LustyMapSettings();

            [JsonProperty(PropertyName = "Raidable Bases", NullValueHandling = NullValueHandling.Ignore)]
            public RaidableBaseSettings RaidableBases = new RaidableBaseSettings();

            [JsonProperty(PropertyName = "Ranked Ladder")]
            public RankedLadderSettings RankedLadder = new RankedLadderSettings();

            [JsonProperty(PropertyName = "Skins")]
            public SkinSettings Skins = new SkinSettings();

            [JsonProperty(PropertyName = "Treasure")]
            public TreasureSettings Treasure = new TreasureSettings();

            [JsonProperty(PropertyName = "TruePVE")]
            public TruePVESettings TruePVE = new TruePVESettings();

            [JsonProperty(PropertyName = "UI")]
            public UISettings UI = new UISettings();

            [JsonProperty(PropertyName = "Weapons")]
            public WeaponSettings Weapons = new WeaponSettings();
        }

        private bool configLoaded = false;

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<Configuration>();
            }
            catch (JsonException ex)
            {
                Puts(ex.Message);
                PrintError("Your configuration file contains a json error, shown above. Please fix this.");
                return;
            }
            catch (Exception ex)
            {
                Puts(ex.Message);
                LoadDefaultConfig();
            }

            if (_config == null)
            {
                Puts("Config is null");
                LoadDefaultConfig();
            }

            configLoaded = true;

            if (string.IsNullOrEmpty(_config.LustyMap.IconFile) || string.IsNullOrEmpty(_config.LustyMap.IconName))
            {
                _config.LustyMap.Enabled = false;
            }

            if (_config.GUIAnnouncement.TintColor.ToLower() == "black")
            {
                _config.GUIAnnouncement.TintColor = "grey";
            }

            Config.WriteObject(_config, false, $"{Interface.Oxide.ConfigDirectory}{Path.DirectorySeparatorChar}{Name}.new_backup_2.json");
            SaveConfig();
        }

        private const string rankLadderPermission = "raidablebases.th";
        private const string rankLadderGroup = "raidhunter";
        private const string adminPermission = "raidablebases.allow";
        private const string drawPermission = "raidablebases.ddraw";
        private const string mapPermission = "raidablebases.mapteleport";
        private const string canBypassPermission = "raidablebases.canbypass";
        private const string bypassBlockPermission = "raidablebases.blockbypass";
        private const string banPermission = "raidablebases.banned";
        private const string vipPermission = "raidablebases.vipcooldown";

        public static List<TreasureItem> TreasureLoot
        {
            get
            {
                List<TreasureItem> lootList;

                if (_config.Treasure.UseDOWL && Buildings.Weekday.TryGetValue(DateTime.Now.DayOfWeek, out lootList) && lootList.Count > 0)
                {
                    return new List<TreasureItem>(lootList);
                }

                return new List<TreasureItem>(Buildings.Default);
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        protected override void LoadDefaultConfig()
        {
            _config = new Configuration();
            Puts("Loaded default configuration file");
        }

        #endregion

        #region UI

        public class UI // Credits: Absolut & k1lly0u
        {
            private static CuiElementContainer CreateElementContainer(string panelName, string color, string aMin, string aMax, bool cursor = false, string parent = "Overlay")
            {
                var NewElement = new CuiElementContainer
                {
                    {
                        new CuiPanel
                        {
                            Image =
                            {
                                Color = color
                            },
                            RectTransform =
                            {
                                AnchorMin = aMin,
                                AnchorMax = aMax
                            },
                            CursorEnabled = cursor
                        },
                        new CuiElement().Parent = parent,
                        panelName
                    }
                };
                return NewElement;
            }

            public static void CreateButton(ref CuiElementContainer container, string panel, string color, string text, int size, string aMin, string aMax, string command, TextAnchor align = TextAnchor.MiddleCenter, string labelColor = "")
            {
                container.Add(new CuiButton
                {
                    Button =
                    {
                        Color = color,
                        Command = command,
                        FadeIn = 1.0f
                    },
                    RectTransform =
                    {
                        AnchorMin = aMin,
                        AnchorMax = aMax
                    },
                    Text =
                    {
                        Text = text,
                        FontSize = size,
                        Align = align,
                        Color = labelColor
                    }
                }, panel);
            }

            private static void CreateLabel(ref CuiElementContainer container, string panel, string color, string text, int size, string aMin, string aMax, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiLabel
                {
                    Text =
                    {
                        Color = color,
                        FontSize = size,
                        Align = align,
                        FadeIn = 1.0f,
                        Text = text
                    },
                    RectTransform =
                    {
                        AnchorMin = aMin,
                        AnchorMax = aMax
                    }
                }, panel);
            }

            private static string GetContrast(string hexColor)
            {
                hexColor = hexColor.TrimStart('#');
                int r = int.Parse(hexColor.Substring(0, 2), NumberStyles.AllowHexSpecifier);
                int g = int.Parse(hexColor.Substring(2, 2), NumberStyles.AllowHexSpecifier);
                int b = int.Parse(hexColor.Substring(4, 2), NumberStyles.AllowHexSpecifier);
                var color = ((r * 299) + (g * 587) + (b * 114)) / 1000 >= 128 ? "0 0 0 1" : "1 1 1 1";
                return color;

            }

            private static string Color(string hexColor, float a = 1.0f)
            {
                a = Mathf.Clamp(a, 0f, 1f);
                hexColor = hexColor.TrimStart('#');
                int r = int.Parse(hexColor.Substring(0, 2), NumberStyles.AllowHexSpecifier);
                int g = int.Parse(hexColor.Substring(2, 2), NumberStyles.AllowHexSpecifier);
                int b = int.Parse(hexColor.Substring(4, 2), NumberStyles.AllowHexSpecifier);
                return $"{(double)r / 255} {(double)g / 255} {(double)b / 255} {a}";
            }

            public static void DestroyStatusUI(BasePlayer player)
            {
                if (player.IsValid() && player.IsConnected && Players.Contains(player))
                {
                    CuiHelper.DestroyUi(player, StatusPanelName);
                    Players.Remove(player);
                    DestroyStatusUpdate(player);
                }
            }

            private static void Create(BasePlayer player, RaidableBase raid, string panelName, string text, string color, string panelColor, string aMin, string aMax)
            {
                var element = CreateElementContainer(panelName, panelColor, aMin, aMax, false, "Hud");

                CreateLabel(ref element, panelName, Color(color), text, _config.UI.FontSize, "0 0", "1 1");
                CuiHelper.AddUi(player, element);

                if (!Players.Contains(player))
                {
                    Players.Add(player);
                }
            }

            private static void Create(BasePlayer player, string panelName, string text, string color, string panelColor, string aMin, string aMax)
            {
                var element = CreateElementContainer(panelName, panelColor, aMin, aMax, false, "Hud");

                CreateLabel(ref element, panelName, Color(color), text, _config.UI.FontSize, "0 0", "1 1");
                CuiHelper.AddUi(player, element);
            }

            private static void ShowStatus(BasePlayer player)
            {
                var raid = RaidableBase.Get(player.transform.position);

                if (raid == null)
                {
                    return;
                }

                string zone = raid.AllowPVP ? Backbone.GetMessage("PVP ZONE") : Backbone.GetMessage("PVE ZONE");
                int lootAmount = 0;
                float seconds = raid.despawnTime - Time.realtimeSinceStartup;
                string despawnText = _config.Settings.Management.DespawnMinutesInactive > 0 && seconds > 0 ? Math.Floor(TimeSpan.FromSeconds(seconds).TotalMinutes).ToString() : null;
                string text;

                foreach (var x in raid._containers)
                {
                    if (x == null || x.IsDestroyed)
                    {
                        continue;
                    }

                    if (IsBox(x, true) || _config.Settings.Management.RequireCupboardLooted && x is BuildingPrivlidge)
                    {
                        lootAmount += x.inventory.itemList.Count;
                    }
                }

                if (_config.UI.Containers && _config.UI.Time && !string.IsNullOrEmpty(despawnText))
                {
                    text = Backbone.GetMessage("UIFormat", null, zone, lootAmount, despawnText);
                }
                else if (_config.UI.Containers)
                {
                    text = Backbone.GetMessage("UIFormatContainers", null, zone, lootAmount);
                }
                else if (_config.UI.Time && !string.IsNullOrEmpty(despawnText))
                {
                    text = Backbone.GetMessage("UIFormatMinutes", null, zone, despawnText);
                }
                else text = zone;

                Create(player, raid, StatusPanelName, text, raid.AllowPVP ? _config.UI.ColorPVP : _config.UI.ColorPVE, Color(_config.UI.PanelColor, _config.UI.Alpha), _config.UI.AnchorMin, _config.UI.AnchorMax);
            }

            public static void UpdateStatusUI(RaidableBase raid)
            {
                foreach (var p in raid.intruders)
                {
                    UI.UpdateStatusUI(p);
                }
            }

            public static void UpdateStatusUI(BasePlayer player)
            {
                Players.RemoveAll(x => x == null || !x.IsConnected);

                if (player == null || !player.IsConnected)
                {
                    return;
                }

                DestroyStatusUI(player);

                if (_config == null || !_config.UI.Enabled)
                {
                    return;
                }

                var uii = GetSettings(player.UserIDString);

                if (!uii.Enabled || !uii.Status)
                {
                    return;
                }

                ShowStatus(player);
                SetStatusUpdate(player);
            }

            private static void SetStatusUpdate(BasePlayer player)
            {
                var raid = RaidableBase.Get(player.transform.position);

                if (raid == null || raid.killed)
                {
                    return;
                }

                Timers timers;
                if (!InvokeTimers.TryGetValue(player.userID, out timers))
                {
                    InvokeTimers[player.userID] = timers = new Timers();
                }

                if (timers.Status == null || timers.Status.Destroyed)
                {
                    timers.Status = Backbone.Timer(60f, () => UpdateStatusUI(player));
                }
                else timers.Status.Reset();
            }

            public static void DestroyStatusUpdate(BasePlayer player)
            {
                Timers timers;
                if (!InvokeTimers.TryGetValue(player.userID, out timers))
                {
                    return;
                }

                if (timers.Status == null || timers.Status.Destroyed)
                {
                    return;
                }

                timers.Status.Destroy();
            }

            public static Info GetSettings(string playerId)
            {
                Info uii;
                if (!Backbone.Data.UI.TryGetValue(playerId, out uii))
                {
                    Backbone.Data.UI[playerId] = uii = new UI.Info();
                }

                return uii;
            }

            private const string StatusPanelName = "RB_UI_Status";
            private const string DifficultyPanelName = "RB_UI_Default";

            public static List<BasePlayer> Players { get; set; } = new List<BasePlayer>();
            public static Dictionary<ulong, Timers> InvokeTimers { get; set; } = new Dictionary<ulong, Timers>();

            public class Timers
            {
                public Timer Status;
            }

            public class Info
            {
                public bool Enabled { get; set; } = true;
                public bool Status { get; set; } = true;
            }
        }

        private void CommandUI(IPlayer p, string command, string[] args)
        {
            if (p.IsServer)
            {
                return;
            }

            var uii = UI.GetSettings(p.Id);
            var player = p.Object as BasePlayer;

            if (args.Length == 0)
            {
                uii.Enabled = !uii.Enabled;

                if (!uii.Enabled)
                {
                    UI.DestroyStatusUI(player);
                }
                else
                {
                    UI.UpdateStatusUI(player);
                }

                return;
            }

            switch (args[0].ToLower())
            {
                case "status":
                    {
                        uii.Status = !uii.Status;
                        UI.UpdateStatusUI(player);
                        return;
                    }
            }
        }

        #endregion UI
    }
}
