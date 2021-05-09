using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Magic Wipe Panel", "MJSU", "1.0.5")]
    [Description("Displays days to wipe in magic panel")]
    public class MagicWipePanel : RustPlugin
    {
        #region Class Fields
        [PluginReference] private readonly Plugin MagicPanel, WipeInfoApi;
        private PluginConfig _pluginConfig; //Plugin Config
        private int _daysTillWipe;
        
        private string _textFormat;

        private enum UpdateEnum { All = 1, Panel = 2, Image = 3, Text = 4 }
        #endregion

        #region Setup & Loading
        private void Init()
        {
            _textFormat = _pluginConfig.Panel.Text.Text;
        }
        
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [LangKeys.Today] = "Today",
                [LangKeys.OneDay] = "1 Day",
                [LangKeys.Days] = "{0} Days"
            }, this);
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
                    Padding = config.Panel?.Image?.Padding ?? new TypePadding(0.05f, 0.0f, 0.1f, 0.1f)
                },
                Text = new PanelText
                {
                    Enabled = config.Panel?.Text?.Enabled ?? true,
                    Color = config.Panel?.Text?.Color ?? "#FFFFFFFF",  
                    Order = config.Panel?.Text?.Order ?? 1,
                    Width = config.Panel?.Text?.Width ?? 1f,
                    FontSize = config.Panel?.Text?.FontSize ?? 14,
                    Padding = config.Panel?.Text?.Padding ?? new TypePadding(0.05f, 0.05f, 0.05f, 0.05f),
                    TextAnchor = config.Panel?.Text?.TextAnchor ?? TextAnchor.MiddleCenter,
                    Text = config.Panel?.Text?.Text ?? "Wipe: {0}",
                }
            };
            config.PanelSettings = new PanelRegistration
            {
                BackgroundColor = config.PanelSettings?.BackgroundColor ?? "#FFF2DF08",
                Dock = config.PanelSettings?.Dock ?? "centerupper",
                Order = config.PanelSettings?.Order ?? 10,
                Width = config.PanelSettings?.Width ?? 0.08f
            };
            return config;
        }

        private void OnServerInitialized()
        {
            if (MagicPanel == null)
            {
                PrintError("Missing plugin dependency MagicPanel: https://umod.org/plugins/magic-panel");
                return;
            }
            
            if (WipeInfoApi == null)
            {
                PrintError("Missing plugin dependency WipeInfoApi: https://umod.org/plugins/wipe-info-api");
                return;
            }
            
            MagicPanelRegisterPanels();
            OnWipeCalculated();
        }
        
        private void OnWipeCalculated()
        {
            if (WipeInfoApi == null || MagicPanel == null)
            {
                return;
            }
            
            _daysTillWipe = WipeInfoApi.Call<int>("GetDaysTillWipe");
            MagicPanel.Call("UpdatePanel", Name, (int)UpdateEnum.Text);
        }

        private void MagicPanelRegisterPanels()
        {
            MagicPanel.Call("RegisterGlobalPanel", this, Name, JsonConvert.SerializeObject(_pluginConfig.PanelSettings), nameof(GetPanel));
        }
        #endregion

        #region MagicPanel Hook

        private Hash<string, object> GetPanel()
        {
            Panel panel = _pluginConfig.Panel;
            PanelText text = panel.Text;
            if (text != null)
            {
                string message;
                if (_daysTillWipe <= 0)
                {
                    message = Lang(LangKeys.Today);
                }
                else if (_daysTillWipe == 1)
                {
                    message = Lang(LangKeys.OneDay);
                }
                else
                {
                    message = Lang(LangKeys.Days, _daysTillWipe);
                }

                text.Text = string.Format(_textFormat, message);
            }

            return panel.ToHash();
        }
        
        private string Lang(string key, params object[] args) => string.Format(lang.GetMessage(key, this), args);
        #endregion

        #region Classes
        private class PluginConfig
        {
            [JsonProperty(PropertyName = "Panel Settings")]
            public PanelRegistration PanelSettings { get; set; }

            [JsonProperty(PropertyName = "Panel Layout")]
            public Panel Panel { get; set; }
        }
        
        private class LangKeys
        {
            public const string Today = "Today";
            public const string OneDay = "OneDay";
            public const string Days = "Days";
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
