using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Stash Warning", "haggbart", "1.3.4")]
    [Description("Logs suspicious stash activity and reports to admins ingame and on discord")]
    internal class StashWarning : RustPlugin
    {
        [PluginReference] Plugin DiscordMessages;
        private const string WEBHOOK_URL = "Webhook URL";
        private const string MESSAGE = "Warning message";
        private const string NO_RECENT_WARNING = "No recent warning";
        private const string IGNORE_SAME_TEAM = "Ignore players in the same team";
        
        private string _webhookUrl;
        private bool _discordEnabled;
        private Vector3 position;
        private string message;
        
        protected override void LoadDefaultConfig()
        {
            Puts("Creating a new configuration file");
            Config[WEBHOOK_URL] = "";
            Config[IGNORE_SAME_TEAM] = true;
        }
        
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [MESSAGE] = "{0} ({1}) found a stash that belongs to {2} ({3}) in {4} {5}.",
                [NO_RECENT_WARNING] = "No recent warning. No location to teleport to."
            }, this);
        }
        
        private void Init()
        {
            _webhookUrl = Config[WEBHOOK_URL].ToString();
            if (!string.IsNullOrEmpty(_webhookUrl))
            {
                _discordEnabled = true;
            }
        }
        
        private void CanSeeStash(BasePlayer player, StashContainer stash)
        {
            if (stash.inventory.itemList.Count == 0 || player.userID == stash.OwnerID) return;

            
            if ((bool) Config[IGNORE_SAME_TEAM] && IsTeamMember(player, stash))
            {
                if (player.IsAdmin)
                {
                    SendReply(player, "sameteam: " + IsTeamMember(player, stash));
                }
                return;
            }
            
            IPlayer iPlayerOwner = covalence.Players.FindPlayerById(stash.OwnerID.ToString());
            AddWarning(player, iPlayerOwner);
            foreach (BasePlayer target in BasePlayer.activePlayerList.Where(x => x.IsAdmin))
            {
                SendReply(target, message, player);
            }
            LogToFile(string.Empty, message, this); 
            if (_discordEnabled)
                DiscordMessages?.Call("API_SendTextMessage", _webhookUrl, message);
        }

        private bool IsTeamMember(BasePlayer player, StashContainer stash)
        {
            if (!(bool)Config[IGNORE_SAME_TEAM]) return false;
            RelationshipManager.PlayerTeam team = RelationshipManager.Instance.FindPlayersTeam(stash.OwnerID);
            
            if (team == null)
            {
                return false;
            }
            
            return team.teamID == player.currentTeam;
        }

        private void AddWarning(BasePlayer player, IPlayer iPlayerOwner, BasePlayer target = null)
        {
            position = player.transform.position;
            message = target == null ? lang.GetMessage(string.Format(MESSAGE), this) : lang.GetMessage(string.Format(MESSAGE), this, target.UserIDString);
            message = string.Format(message, player.displayName, player.userID, iPlayerOwner?.Name ?? "Unknown", iPlayerOwner?.Id ?? "Unknown", GridReference(position), position);
        }

        private static string GridReference(Vector3 pos)
        {
            int worldSize = ConVar.Server.worldsize;
            const float scale = 150f;
            float x = pos.x + worldSize/2f;
            float z = pos.z + worldSize/2f;
            var lat = (int)(x / scale);
            var latChar = (char)('A' + lat);
            var lon = (int)(worldSize/scale - z/scale);
            return $"{latChar}{lon}";
        }

        [ChatCommand("sw")] 
        private void TeleportLast(BasePlayer player)
        {
            if (!player.IsAdmin || !player.IsAlive()) return;
            if (message == null)
            {
                SendReply(player, lang.GetMessage(NO_RECENT_WARNING, this, player.UserIDString));
                return;
            }
            SendReply(player, message);
            player.Teleport(position);
        }
    }
}