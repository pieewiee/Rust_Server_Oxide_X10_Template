using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("Magic Ping Panel", "MJSU", "1.0.5")]
    [Description("Displays players ping in magic panel")]
    public class MagicPingPanel : RustPlugin
    {
        #region Class Fields
        [PluginReference] private Plugin MagicPanel;

        private PluginConfig _pluginConfig; //Plugin Config

        private readonly Hash<ulong, int> _playerPing = new Hash<ulong, int>();
        private string _defaultColor;

        private Coroutine _updateRoutine;

        private enum UpdateEnum { All = 1, Panel = 2, Image = 3, Text = 4 }
        
        private string _text;
        #endregion

        #region Setup & Loading
        private void Init()
        {
            _text = _pluginConfig.Panel.Text.Text;
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Loading Default Config");
        }

        protected override void LoadConfig()
        {
            string path = $"{Manager.ConfigPath}/MagicPanel/{Name}.json";
            DynamicConfigFile newConfig = new DynamicConfigFile(path);
            if (!newConfig.Exists())
            {
                LoadDefaultConfig();
                newConfig.Save();
            }
            try
            {
                newConfig.Load();
            }
            catch (Exception ex)
            {
                RaiseError("Failed to load config file (is the config file corrupt?) (" + ex.Message + ")");
                return;
            }
            
            newConfig.Settings.DefaultValueHandling = DefaultValueHandling.Populate;
            _pluginConfig = AdditionalConfig(newConfig.ReadObject<PluginConfig>());
            newConfig.WriteObject(_pluginConfig);
        }

        private PluginConfig AdditionalConfig(PluginConfig config)
        {
            config.Panel = new Panel
            {
                Image = new PanelImage
                {
                    Enabled = config.Panel?.Image?.Enabled ?? false,
                    Color = config.Panel?.Image?.Color ?? "#FFFFFFFF",
                    Order = config.Panel?.Image?.Order ?? 0,
                    Width = config.Panel?.Image?.Width ?? 0f,
                    Url = config.Panel?.Image?.Url ?? "",
                    Padding = config.Panel?.Image?.Padding ?? new TypePadding(0.05f, 0.0f, 0.05f, 0.05f)
                },
                Text = new PanelText
                {
                    Enabled = config.Panel?.Text?.Enabled ?? true,
                    Color = config.Panel?.Text?.Color ?? "#FFFFFFFF",  
                    Order = config.Panel?.Text?.Order ?? 1,
                    Width = config.Panel?.Text?.Width ?? 1,
                    FontSize = config.Panel?.Text?.FontSize ?? 14,
                    Padding = config.Panel?.Text?.Padding ?? new TypePadding(0.05f, 0.05f, 0.05f, 0.05f),
                    TextAnchor = config.Panel?.Text?.TextAnchor ?? TextAnchor.MiddleCenter,
                    Text = config?.Panel?.Text?.Text ?? "Ping: {0}ms",
                }
            };
            config.PanelSettings = new PanelRegistration
            {
                BackgroundColor = config.PanelSettings?.BackgroundColor ?? "#FFF2DF08",
                Dock = config.PanelSettings?.Dock ?? "leftmiddle",
                Order = config.PanelSettings?.Order ?? 5,
                Width = config.PanelSettings?.Width ?? 0.055f
            };

            config.PingColors = new Hash<int, string>
            {
                [0] = "#00ff00",
                [50] = "#ffff00",
                [100] = "#ffa500",
                [150] = "#ff0000",
                [200] = "#000000"
            };
            
            return config;
        }

        private void OnServerInitialized()
        {
            _defaultColor = _pluginConfig.Panel.Text.Color;
            MagicPanelRegisterPanels();
        }

        private void MagicPanelRegisterPanels()
        {
            if (MagicPanel == null)
            {
                PrintError("Missing plugin dependency MagicPanel: https://umod.org/plugins/magic-panel");
                UnsubscribeAll();
                return;
            }
        
            MagicPanel?.Call("RegisterPlayerPanel", this, Name, JsonConvert.SerializeObject(_pluginConfig.PanelSettings), nameof(GetPanel));
            InvokeHandler.Instance.InvokeRepeating(UpdatePlayerPing, Random.Range(0, _pluginConfig.UpdateRate), _pluginConfig.UpdateRate);
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                OnPlayerConnected(player);
            }
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            _playerPing[player.userID] = CheckPing(player);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            _playerPing.Remove(player.userID);
        }

        private void Unload()
        {
            InvokeHandler.Instance.CancelInvoke(UpdatePlayerPing);
            if (_updateRoutine != null)
            {
                InvokeHandler.Instance.StopCoroutine(_updateRoutine);
            }
        }
        
        private void UnsubscribeAll()
        {
            Unsubscribe(nameof(OnPlayerConnected));
            Unsubscribe(nameof(OnPlayerDisconnected));
        }
        #endregion
        
        #region Player Ping Update
        private void UpdatePlayerPing()
        {
            _updateRoutine = InvokeHandler.Instance.StartCoroutine(HandleUpdatePlayerPing());
        }

        private IEnumerator HandleUpdatePlayerPing()
        {
            for (int i = 0; i < BasePlayer.activePlayerList.Count; i++)
            {
                BasePlayer player = BasePlayer.activePlayerList[i];
                int ping = CheckPing(player);
                int lastPing = _playerPing[player.userID];
                
                yield return null;
                if (ping == lastPing)
                {
                    continue;
                }

                _playerPing[player.userID] = ping;
                MagicPanel?.Call("UpdatePanel", player, Name, (int)UpdateEnum.Text);
            }
        }
        #endregion

        #region MagicPanel Hook
        private Hash<string, object> GetPanel(BasePlayer player)
        {
            Panel panel = _pluginConfig.Panel;
            PanelText text = panel.Text;
            if (text != null)
            {
                float ping = _playerPing[player.userID];
                string color = _defaultColor;
                if (_pluginConfig.EnablePingColors)
                {
                    foreach (KeyValuePair<int,string> pingColor in _pluginConfig.PingColors.OrderByDescending(p => p.Key))
                    {
                        if (pingColor.Key < ping)
                        {
                            color = pingColor.Value;
                            break;
                        }
                    }
                }

                
                text.Text = string.Format(_text, $"<color={color}>{ping}</color>");
            }

            return panel.ToHash();
        }
        #endregion

        #region Helper Methods
        private int CheckPing(BasePlayer player)
        {
            return Net.sv.GetAveragePing(player.net.connection);
        }
        #endregion

        #region Classes

        private class PluginConfig
        {
            [DefaultValue(5f)]
            [JsonProperty(PropertyName = "Update Rate (Seconds)")]
            public float UpdateRate { get; set; }
            
            [DefaultValue(true)]
            [JsonProperty(PropertyName = "Enable ping colors")]
            public bool EnablePingColors { get; set; }
            
            [JsonProperty(PropertyName = "Ping Colors")]
            public Hash<int, string> PingColors { get; set; }

            [JsonProperty(PropertyName = "Panel Settings")]
            public PanelRegistration PanelSettings { get; set; }

            [JsonProperty(PropertyName = "Panel Layout")]
            public Panel Panel { get; set; }
        }

        private class PanelRegistration
        {
            public string Dock { get; set; }
            public float Width { get; set; }
            public int Order { get; set; }
            public string BackgroundColor { get; set; }
        }

        private class Panel
        {
            public PanelImage Image { get; set; }
            public PanelText Text { get; set; }
            
            public Hash<string, object> ToHash()
            {
                return new Hash<string, object>
                {
                    [nameof(Image)] = Image.ToHash(),
                    [nameof(Text)] = Text.ToHash()
                };
            }
        }

        private abstract class PanelType
        {
            public bool Enabled { get; set; }
            public string Color { get; set; }
            public int Order { get; set; }
            public float Width { get; set; }
            public TypePadding Padding { get; set; }
            
            public virtual Hash<string, object> ToHash()
            {
                return new Hash<string, object>
                {
                    [nameof(Enabled)] = Enabled,
                    [nameof(Color)] = Color,
                    [nameof(Order)] = Order,
                    [nameof(Width)] = Width,
                    [nameof(Padding)] = Padding.ToHash(),
                };
            }
        }

        private class PanelImage : PanelType
        {
            public string Url { get; set; }
            
            public override Hash<string, object> ToHash()
            {
                Hash<string, object> hash = base.ToHash();
                hash[nameof(Url)] = Url;
                return hash;
            }
        }

        private class PanelText : PanelType
        {
            public string Text { get; set; }
            public int FontSize { get; set; }

            [JsonConverter(typeof(StringEnumConverter))]
            public TextAnchor TextAnchor { get; set; }
            
            public override Hash<string, object> ToHash()
            {
                Hash<string, object> hash = base.ToHash();
                hash[nameof(Text)] = Text;
                hash[nameof(FontSize)] = FontSize;
                hash[nameof(TextAnchor)] = TextAnchor;
                return hash;
            }
        }

        private class TypePadding
        {
            public float Left { get; set; }
            public float Right { get; set; }
            public float Top { get; set; }
            public float Bottom { get; set; }

            public TypePadding(float left, float right, float top, float bottom)
            {
                Left = left;
                Right = right;
                Top = top;
                Bottom = bottom;
            }
            
            public Hash<string, object> ToHash()
            {
                return new Hash<string, object>
                {
                    [nameof(Left)] = Left,
                    [nameof(Right)] = Right,
                    [nameof(Top)] = Top,
                    [nameof(Bottom)] = Bottom
                };
            }
        }
        #endregion
    }
}
