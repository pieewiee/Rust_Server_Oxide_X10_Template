using System;
using UnityEngine;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Area Nuke", "klauz24", "1.0.6")]
    [Description("Removes every object within a specified range")]
    class AreaNuke : HurtworldPlugin
    {
        List<string> _destroyedList = new List<string>();
        List<string> _erroredList = new List<string>();

        const string perm = "areanuke.admin";
        private void Init() => permission.RegisterPermission(perm, this);

        void Save()
        {
            Oxide.Core.Interface.GetMod().DataFileSystem.WriteObject("AreaNuke/DestroyedList", _destroyedList);
            Oxide.Core.Interface.GetMod().DataFileSystem.WriteObject("AreaNuke/ErroredList", _erroredList);
        }

        #region Config File
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"AreaNuke_Prefix", "<color=orange>[AreaNuke]</color> "},
                {"AreaNuke_NoPerm", "You do not have permission to use this command."},
                {"AreaNuke_InvalidRange", "The range must be a number."},
                {"AreaNuke_InvalidArgs", "Incorrect usage: /areanuke [range] (example; /areanuke 5)"},
                {"AreaNuke_Destroyed", "Destroyed {Count} objects"},
            }, this);
        }
        #endregion

        #region Chat Commands
        [ChatCommand("areanuke")]
        private void AreaNukeCommand(PlayerSession session, string command, string[] args)
        {
            if (permission.UserHasPermission(session.SteamId.ToString(), "areanuke.admin") || session.IsAdmin)
            {
                if (args.Length == 1)
                {
                    int count = 0;
                    int range = 0;
                    try { range = Convert.ToInt32(args[0]); }
                    catch
                    {
                        msgToPlayer(session, GetLang("AreaNuke_Prefix", session.SteamId.ToString()) + GetLang("AreaNuke_InvalidRange", session.SteamId.ToString()));
                        return;
                    }
                    foreach (GameObject obj in Resources.FindObjectsOfTypeAll<GameObject>())
                    {
                        if (Vector3.Distance(session.WorldPlayerEntity.transform.position, obj.transform.position) <= range)
                        {
                            HandleStructure(obj.transform.position);
                            HNetworkView nwv = obj.GetComponent<HNetworkView>();
                            if (nwv != null && nwv.isActiveAndEnabled)
                            {
                                if (obj.name.Contains("(Clone)") && !obj.name.Contains("Player"))
                                {
                                    HNetworkManager.Instance.NetDestroy(nwv);
                                    count++;
                                }
                            }
                        }
                    }
                    Save();
                    msgToPlayer(session, GetLang("AreaNuke_Prefix", session.SteamId.ToString()) + GetLang("AreaNuke_Destroyed", session.SteamId.ToString()).Replace("{Count}", count.ToString()));

                }
                else if (args.Length == 2)
                {
                    int count = 0;
                    int range = 0;
                    try { range = Convert.ToInt32(args[0]); }
                    catch
                    {
                        msgToPlayer(session, GetLang("AreaNuke_Prefix", session.SteamId.ToString()) + GetLang("AreaNuke_InvalidRange", session.SteamId.ToString()));
                        return;
                    }
                    foreach (GameObject obj in Resources.FindObjectsOfTypeAll<GameObject>())
                    {
                        if (obj.name.ToString().Contains(args[1].ToLower()))
                        {
                            if (Vector3.Distance(session.WorldPlayerEntity.transform.position, obj.transform.position) <= range)
                            {
                                HandleStructure(obj.transform.position);
                                HNetworkView nwv = obj.GetComponent<HNetworkView>();
                                if (nwv != null && nwv.isActiveAndEnabled)
                                {
                                    if (obj.name.Contains("(Clone)") && !obj.name.Contains("Player"))
                                    {
                                        HNetworkManager.Instance.NetDestroy(nwv);
                                        count++;
                                    }
                                }
                            }
                        }
                    }
                    msgToPlayer(session, GetLang("AreaNuke_Prefix", session.SteamId.ToString()) + GetLang("AreaNuke_Destroyed", session.SteamId.ToString()).Replace("{Count}", count.ToString()));
                }
                else
                    msgToPlayer(session, GetLang("AreaNuke_Prefix", session.SteamId.ToString()) + GetLang("AreaNuke_InvalidArgs", session.SteamId.ToString()));
            }
            else
                msgToPlayer(session, GetLang("AreaNuke_Prefix", session.SteamId.ToString()) + GetLang("AreaNuke_NoPerm", session.SteamId.ToString()));
        }
        #endregion

        #region Methods
        private void HandleStructure(Vector3 pos)
        {
            var structureManager = ConstructionManager.Instance.GetStructureCellManager(pos);
            if (structureManager != null)
            {
                var list = new List<AttachmentData>();
                foreach (var data in structureManager.DeploymentData)
                {
                    list.Add(data);
                }
                foreach (var data in list)
                {
                    data.Properties.Health = 0;
                    var structure = data.Structure as StructureManagerServer;
                    if (structure.UpdateAttachmentServer(data))
                    {
                        structure.ForceColliderUpdate();
                    }
                }
            }
        }
        #endregion

        #region Chat Formatting
        string GetLang(string key, object SteamId = null) => lang.GetMessage(key, this, SteamId == null ? null : SteamId.ToString());
        void msgToPlayer(PlayerSession session, string msg) => hurt.SendChatMessage(session, null, msg);
        #endregion
    }
}