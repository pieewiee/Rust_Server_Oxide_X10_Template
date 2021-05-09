// #define DEBUG
using System.Collections.Generic;
using System;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Plugins;
using System.Text;
using System.Linq;
using Facepunch;
using Rust;

namespace Oxide.Plugins {
    [Info("Personal Heli", "Egor Blagov", "1.1.7")]
    [Description("Calls heli to player and his team, with loot/damage and minig lock")]
    class PersonalHeli : RustPlugin {
        #region CONSTANTS
        const string permUse = "personalheli.use";
        const string permConsole = "personalheli.console";
        const float HelicopterEntitySpawnRadius = 10.0f;
        #endregion
        #region DEPENDENCIES
        [PluginReference]
        Plugin Friends, Clans;
        #endregion
        #region CONFIG
        class PluginConfig {
            public bool UseFriends = true;
            public bool UseTeams = true;
            public bool UseClans = true;
            public int CooldownSeconds = 1800;
            public string ChatCommand = "callheli";
            public bool ResetCooldownsOnWipe = true;
            public bool MemorizeTeamOnCall = false;
            public bool RetireOnAllTeamDead = false;
            public bool DenyCratesLooting = true;
            public bool DenyGibsMining = true;
            public bool RemoveFireFromCrates = true;
        }
        private PluginConfig config;
        #endregion
        #region STORED DATA
        class StoredData {
            public Dictionary<ulong, CallData> CallDatas = new Dictionary<ulong, CallData>();

            public class CallData {
                public DateTime LastCall = DateTime.MinValue;
                public bool CanCallNow(int cooldown) {
                    return DateTime.Now.Subtract(LastCall).TotalSeconds > cooldown;
                }

                public int SecondsToWait(int cooldown) {
                    return (int)Math.Round(cooldown - DateTime.Now.Subtract(LastCall).TotalSeconds);
                }

                public void OnCall() {
                    LastCall = DateTime.Now;
                }
            }

            public CallData GetForPlayer(BasePlayer player) {
                if (!CallDatas.ContainsKey(player.userID)) {
                    CallDatas[player.userID] = new CallData();
                }

                return CallDatas[player.userID];
            }
        }
        private void SaveData() {
            if (storedData != null) {
                Interface.Oxide.DataFileSystem.WriteObject(Name, storedData, true);
            }
        }
        private void LoadData() {
            storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            if (storedData == null) {
                storedData = new StoredData();
                SaveData();
            }
        }
        private StoredData storedData;
        #endregion
        #region L10N
        protected override void LoadDefaultMessages() {
            lang.RegisterMessages(new Dictionary<string, string> {
                ["NoPermission"] = "You have no permission to use this command",
                ["Cooldown"] = "Helicopter call is on cooldown, time remaining: {0}",
                ["LootDenied"] = "You are forbidden to loot this crate, it belongs to: {0}",
                ["DamageDenied"] = "You are forbidden to damage this helicopter, it was called by: {0}",
                ["MiningDenied"] = "You are forbidden to mine this debris, it belongs to: {0}",
                ["Friends"] = "their friends",
                ["Team"] = "their team",
                ["Clan"] = "their clan",
                ["CmdUsage"] = "Invalid format, usage: personalheli.call {{steamId}}",
                ["InvalidSteamId"] = "{0} is invalid Steam ID",
                ["PlayerNotFound"] = "Player with id {0} was not found",
                ["PlayerCalled"] = "Personal helicopter is called for {0}"
            }, this);

            lang.RegisterMessages(new Dictionary<string, string> {
                ["NoPermission"] = "У Вас нет прав на использование этой команды",
                ["Cooldown"] = "Вызов вертолета в кулдауне, ждать осталось: {0}",
                ["HeliSuccess"] = "Вертолет был вызван",
                ["LootDenied"] = "Вам запрещено лутать этот ящик, его могут лутать: {0}",
                ["DamageDenied"] = "Вам запрещено наносить урон этому вертолету, его вызвал: {0}",
                ["MiningDenied"] = "Вам запрещено добывать эти обломки, их могут добывать: {0}",
                ["Friends"] = "его друзья",
                ["Team"] = "его команда",
                ["Clan"] = "его клан",
                ["CmdUsage"] = "Неправильный формат, использование: personalheli.call {{steamId}}",
                ["InvalidSteamId"] = "{0} не является Steam ID",
                ["PlayerNotFound"] = "Игрок с ID {0} не найден",
                ["PlayerCalled"] = "Вертолет был вызван для {0}"
            }, this, "ru");
        }
        private string _(string key, string userId, params object[] args) {
            return string.Format(lang.GetMessage(key, this, userId), args);
        }
        #endregion
        #region HOOKS
        private void Init() {
            permission.RegisterPermission(permUse, this);
            permission.RegisterPermission(permConsole, this);
            config = Config.ReadObject<PluginConfig>();
            Config.WriteObject(config);
            cmd.AddChatCommand(config.ChatCommand, this, CmdCallHeli);
            LoadData();
        }
        private void Unload() {
            foreach (var personal in UnityEngine.Object.FindObjectsOfType<PersonalComponent>()) {
                UnityEngine.Object.Destroy(personal);
            }
            SaveData();
        }
        protected override void LoadDefaultConfig() {
            Config.WriteObject(new PluginConfig(), true);
        }
        private void OnNewSave() {
            if (config.ResetCooldownsOnWipe) {
                storedData = new StoredData();
                SaveData();
            }
        }
        private void OnServerSave() {
            SaveData();
        }
        private void OnEntityKill(BaseEntity entity) {
            InvokePersonal<PersonalHeliComponent>(entity.gameObject, personalHeli => personalHeli.OnKill());
        }
        private object CanLootEntity(BasePlayer player, StorageContainer container) {
            return InvokePersonal<PersonalCrateComponent, object>(container?.gameObject, personalCrate => {
                var result = personalCrate.CanInterractWith(player);
                if (result == false) {
                    SendReply(player, _("LootDenied", player.UserIDString, GetPlayerOwnerDescription(player, personalCrate.Player)));
                    return false;
                }
                return null;
            });
        }
        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info) {
            if (!config.RetireOnAllTeamDead) {
                return;
            }

            if (!(entity is BasePlayer)) {
                return;
            }
            NextTick(() => {
                foreach (var heli in PersonalHeliComponent.ActiveHelis) {
                    heli.OnPlayerDied(entity as BasePlayer);
                }
            });
        }
        private object OnHelicopterTarget(HelicopterTurret turret, BaseCombatEntity entity) {
            return InvokePersonal<PersonalHeliComponent, object>(turret?._heliAI?.helicopterBase?.gameObject, personalHeli => {
                var result = personalHeli.CanInterractWith(entity);
                return result ? null : (object) false;
            });
        }
        private object OnPlayerAttack(BasePlayer attacker, HitInfo info) {
            if (info.HitEntity is ServerGib && info.WeaponPrefab is BaseMelee) {
                return InvokePersonal<PersonalGibComponent, object>(info?.HitEntity?.gameObject, personalGib => {
                    var result = personalGib.CanInterractWith(attacker);
                    if (result == false) {
                        SendReply(info.InitiatorPlayer, _("MiningDenied", info.InitiatorPlayer.UserIDString, GetPlayerOwnerDescription(info.InitiatorPlayer, personalGib.Player)));
                        return false;
                    }
                    return null;
                });
            }
            return InvokePersonal<PersonalHeliComponent, object>(info?.HitEntity?.gameObject, personalHeli => {
                var result = personalHeli.CanInterractWith(attacker);
                if (result == false) {
                    SendReply(info.InitiatorPlayer, _("DamageDenied", info.InitiatorPlayer.UserIDString, GetPlayerOwnerDescription(info.InitiatorPlayer, personalHeli.Player)));
                    return false;
                }
                return null;
            });
        }
        private object CanHelicopterStrafeTarget(PatrolHelicopterAI heliAi, BasePlayer target) {
            return InvokePersonal<PersonalHeliComponent, object>(heliAi?.helicopterBase?.gameObject, personalHeli => {
                return personalHeli.CanInterractWith(target) ? null : (object) false;
            });
        }
        private object CanHelicopterTarget(PatrolHelicopterAI heliAi, BasePlayer player) {
            return InvokePersonal<PersonalHeliComponent, object>(heliAi?.helicopterBase?.gameObject, personalHeli => {
                return personalHeli.CanInterractWith(player) ? null : (object) false;
            });
        }
        #endregion
        private bool CallHeliForPlayer(BasePlayer player) {
            var playerPos = player.transform.position;
            float mapWidth = (TerrainMeta.Size.x / 2) - 50f;
            var heliPos = new Vector3(
                playerPos.x < 0 ? -mapWidth : mapWidth,
                30,
                playerPos.z < 0 ? -mapWidth : mapWidth
            );

            BaseHelicopter heli = GameManager.server.CreateEntity("assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab", new Vector3(), new Quaternion(), true) as BaseHelicopter;
            if (!heli) return false;
            PatrolHelicopterAI heliAI = heli.GetComponent<PatrolHelicopterAI>();
            heli.Spawn();
            heli.transform.position = heliPos;
            var component = heli.gameObject.AddComponent<PersonalHeliComponent>();
            component.Init(this, player);
            foreach (var p in BasePlayer.activePlayerList) {
                SendReply(p, _("PlayerCalled", p.UserIDString, $"<color=#63ff64>{player.displayName}</color>"));
            }
            return true;
        }
        #region API
        private bool IsPersonal(BaseHelicopter heli) => InvokePersonal<PersonalHeliComponent, object>(heli?.gameObject, (comp) => true) == null ? false : true;

        #endregion
        [ConsoleCommand("personalheli.call")]
        private void CmdCallHeliConsole(ConsoleSystem.Arg arg) {
            if (arg.Player() != null) {
                if (!permission.UserHasPermission(arg.Player().UserIDString, permConsole)) {
                    PrintToConsole(arg.Player(), _("NoPermission", arg.Player().UserIDString));
                    return;
                }
            }

            Action<string> printToConsole;
            if (arg.Player() == null) {
                printToConsole = (str) => Puts(str);
            } else {
                printToConsole = (str) => PrintToConsole(arg.Player(), str);
            }

            string UserId = arg.Player() == null ? "" : arg.Player().UserIDString;
            if (!arg.HasArgs()) {
                printToConsole(_("CmdUsage", UserId));
                return;
            }

            if (!arg.Args[0].IsSteamId()) {
                printToConsole(_("InvalidSteamId", UserId, arg.Args[0]));
                return;
            }

            var player = BasePlayer.FindByID(ulong.Parse(arg.Args[0]));
            if (player == null) {
                player = BasePlayer.FindSleeping(ulong.Parse(arg.Args[0]));
            }

            if (player == null) {
                printToConsole(_("PlayerNotFound", UserId, arg.Args[0]));
                return;
            }

            if (CallHeliForPlayer(player)) {
                printToConsole(_("PlayerCalled", UserId, player.displayName));
            }
        }

        private void CmdCallHeli(BasePlayer player, string cmd, string[] argv) {
            if (!permission.UserHasPermission(player.UserIDString, permUse)) {
                SendReply(player, _("NoPermission", player.UserIDString));
                return;
            }

            StoredData.CallData callData = storedData.GetForPlayer(player);
            if (!callData.CanCallNow(config.CooldownSeconds)) {
                SendReply(player, _("Cooldown", player.UserIDString, TimeSpan.FromSeconds(callData.SecondsToWait(config.CooldownSeconds))));
                return;
            }

            if (CallHeliForPlayer(player)) {
                callData.OnCall();
            }
        }
        private string GetPlayerOwnerDescription(BasePlayer player, BasePlayer playerOwner) {
            StringBuilder result = new StringBuilder($"<color=#63ff64>{playerOwner.displayName}</color>");
            if (config.UseFriends && Friends != null) {
                result.Append($", {_("Friends", player.UserIDString)}");
            }
            if (config.UseTeams) {
                result.Append($", {_("Team", player.UserIDString)}");
            }
            if (config.UseClans) {
                result.Append($", {_("Clan", player.UserIDString)}");
            }
            return result.ToString();
        }
        private T InvokePersonal<C, T>(GameObject obj, Func<C, T> action) where C : PersonalComponent {
            var comp = obj?.GetComponent<C>();
            if (comp == null) return default(T);
            return action(comp);
        }
        private void InvokePersonal<C>(GameObject obj, Action<C> action) where C : PersonalComponent => InvokePersonal<C, object>(obj, comp => { action(comp); return null; });
        abstract class PersonalComponent : FacepunchBehaviour {
            protected PersonalHeli Plugin;
            protected PluginConfig Config => Plugin.config;
            public List<BasePlayer> SavedTeam;
            public BasePlayer Player;
            public void Init(PersonalHeli plugin, BasePlayer player) {
                Player = player;
                Plugin = plugin;
                OnInitChild();
            }
            protected virtual void OnInitChild() { }

            public virtual bool CanInterractWith(BaseEntity target) {
                if (Config.MemorizeTeamOnCall && SavedTeam != null) {
                    return SavedTeam.Contains(target as BasePlayer);
                }

                if (!(target is BasePlayer) || target is NPCPlayer || target is HTNPlayer) {
                    return false;
                }

                if (target == Player) {
                    return true;
                }

                if (Plugin.config.UseFriends) {
                    if (AreFriends(target as BasePlayer)) {
                        return true;
                    }
                }

                if (Plugin.config.UseTeams) {
                    if (AreSameTeam(target as BasePlayer)) {
                        return true;
                    }
                }

                if (Plugin.config.UseClans) {
                    if (AreSameClan(target as BasePlayer)) {
                        return true;
                    }
                }

                return false;
            }

            protected bool AreSameClan(BasePlayer basePlayer) {
                if (Plugin.Clans == null) {
                    return false;
                }
                var playerClan = Plugin.Clans.Call<string>("GetClanOf", Player);
                var otherPlayerClan = Plugin.Clans.Call<string>("GetClanOf", basePlayer);
                if (playerClan == null || otherPlayerClan == null) {
                    return false;
                }

                return playerClan == otherPlayerClan;
            }

            protected bool AreSameTeam(BasePlayer otherPlayer) {
                if (Player.currentTeam == 0UL || otherPlayer.currentTeam == 0UL) {
                    return false;
                }

                return Player.currentTeam == otherPlayer.currentTeam;
            }

            protected bool AreFriends(BasePlayer otherPlayer) {
                if (Plugin.Friends == null) {
                    return false;
                }

                return Plugin.Friends.Call<bool>("AreFriends", Player.userID, otherPlayer.userID);
            }

            private void OnDestroy() {
                OnDestroyChild();
            }
            protected virtual void OnDestroyChild() { }
        }
        class PersonalCrateComponent : PersonalComponent {
            private StorageContainer Crate;
            private void Awake() {
                Crate = GetComponent<StorageContainer>();
            }
            protected override void OnDestroyChild() {
                if (Crate != null && Crate.IsValid() && !Crate.IsDestroyed) {
                    Crate.Kill();
                }
            }
        }
        class PersonalGibComponent : PersonalComponent {
            private HelicopterDebris Gib;
            private void Awake() {
                Gib = GetComponent<HelicopterDebris>();
            }
            protected override void OnDestroyChild() {
                if (Gib != null && Gib.IsValid() && !Gib.IsDestroyed) {
                    Gib.Kill();
                }
            }
        }
        class PersonalHeliComponent : PersonalComponent {
            private const int MaxHeliDistanceToPlayer = 140;
            public static List<PersonalHeliComponent> ActiveHelis = new List<PersonalHeliComponent>();
            private BaseHelicopter Heli;
            private PatrolHelicopterAI HeliAi => Heli.GetComponent<PatrolHelicopterAI>();
            private void Awake() {
                Heli = this.GetComponent<BaseHelicopter>();
            }
            protected override void OnInitChild() {
                HeliAi.State_Move_Enter(Player.transform.position + new Vector3(UnityEngine.Random.Range(10f, 50f), 20f, UnityEngine.Random.Range(10f, 50f)));
                InvokeRepeating(new Action(UpdateTargets), 5.0f, 5.0f);
                if (Config.MemorizeTeamOnCall) {
                    SavedTeam = GetAllPlayersInTeam();
                }
                ActiveHelis.Add(this);
#if DEBUG
                InvokeRepeating(new Action(TraceState), 5.0f, 5.0f);
#endif
            }
#if DEBUG
            private void TraceState() {
                Plugin.Server.Broadcast($"helicopter: {Heli.transform.position}: {HeliAi._currentState.ToString()}");
                Plugin.Server.Broadcast(string.Join(", ", HeliAi._targetList.Select(tg => tg.ply.displayName)));
                Plugin.Server.Broadcast($"heli at destionation {Vector3.Distance(Heli.transform.position, HeliAi.destination)}");
            }
#endif
            private void UpdateTargets() {
                if (HeliAi._targetList.Count == 0) {
                    List<BasePlayer> team = Config.MemorizeTeamOnCall ? SavedTeam : GetAllPlayersInTeam();
                    foreach (var player in team) {
                        if (player != null && player.IsConnected) {
                            HeliAi._targetList.Add(new PatrolHelicopterAI.targetinfo(Player, Player));
                        }
                    }
                }

                if (HeliAi._targetList.Count == 1 && HeliAi._targetList[0].ply == Player &&
                    Vector3Ex.Distance2D(Heli.transform.position, Player.transform.position) > MaxHeliDistanceToPlayer) {
                    if (HeliAi._currentState != PatrolHelicopterAI.aiState.MOVE || Vector3Ex.Distance2D(HeliAi.destination, Player.transform.position) > MaxHeliDistanceToPlayer) {
                        HeliAi.ExitCurrentState();
                        var heliTarget = Player.transform.position.XZ() + Vector3.up * 250;
                        RaycastHit hit;
                        if (Physics.SphereCast(Player.transform.position.XZ() + Vector3.up * 600, 50, Vector3.down, out hit, 1500, Layers.Solid)) {
                            heliTarget = hit.point + Vector3.up * 20;
                        }
#if DEBUG
                        Plugin.Server.Broadcast($"Forcing helicopter {Heli.transform.position} to player {Player.displayName}, pos {heliTarget}");
#endif
                        HeliAi.State_Move_Enter(heliTarget);
                    }
                }
            }
            protected override void OnDestroyChild() {
                CancelInvoke(new Action(UpdateTargets));
#if DEBUG
                CancelInvoke(new Action(TraceState));
#endif
                if (Heli != null && Heli.IsValid() && !Heli.IsDestroyed) {
                    Heli.Kill();
                }

                ActiveHelis.Remove(this);
            }
            private List<BasePlayer> GetAllPlayersInTeam() {
                var fullTeam = new List<BasePlayer>();
                foreach (var player in BasePlayer.activePlayerList) {
                    if (player == Player) {
                        fullTeam.Add(player);
                    } else if ((Config.UseFriends && AreFriends(player)) ||
                            Plugin.config.UseClans && AreSameClan(player) ||
                            Plugin.config.UseTeams && AreSameTeam(player)) {
                        fullTeam.Add(player);
                    }
                }

                return fullTeam;
            }
            public void OnKill() {
                if (Config.DenyCratesLooting) {
                    var crates = Facepunch.Pool.GetList<LootContainer>();
                    Vis.Entities(Heli.transform.position, HelicopterEntitySpawnRadius, crates);
                    foreach (var crate in crates) {
                        var component = crate.gameObject.AddComponent<PersonalCrateComponent>();
                        component.Init(Plugin, Player);
                        if (Config.MemorizeTeamOnCall) {
                            component.SavedTeam = SavedTeam;
                        }
                        if (Config.RemoveFireFromCrates) {
                            if (crate is LockedByEntCrate) {
                                (crate as LockedByEntCrate).lockingEnt?.ToBaseEntity()?.Kill();
                            }
                        }
                    }
                    Facepunch.Pool.FreeList(ref crates);
                }
                if (Config.DenyGibsMining) {
                    var gibs = Facepunch.Pool.GetList<HelicopterDebris>();
                    Vis.Entities(Heli.transform.position, HelicopterEntitySpawnRadius, gibs);
                    foreach (var gib in gibs) {
                        var component = gib.gameObject.AddComponent<PersonalGibComponent>();
                        component.Init(Plugin, Player);
                        if (Config.MemorizeTeamOnCall) {
                            component.SavedTeam = SavedTeam;
                        }
                    }
                    Facepunch.Pool.FreeList(ref gibs);
                }
            }

            public void OnPlayerDied(BasePlayer player) {
                if (!Config.RetireOnAllTeamDead) {
                    return;
                }
                if (CanInterractWith(player)) {
                    bool allTeamDied = true;
                    List<BasePlayer> team = Config.MemorizeTeamOnCall ? SavedTeam : GetAllPlayersInTeam();
                    foreach (var member in team) {
                        if (!member.IsDead()) {
                            allTeamDied = false;
                            break;
                        }
                    }
                    if (allTeamDied) {
                        CancelInvoke(new Action(UpdateTargets));
                        HeliAi.Retire();
                    }
                }
            }
        }
    }
}
