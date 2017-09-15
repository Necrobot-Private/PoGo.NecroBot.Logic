using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace PoGo.NecroBot.Logic.Model.Settings
{
    [JsonObject(Title = "Websockets Config", Description = "Set your websockets settings.", ItemRequired = Required.DisallowNull)]
    public class WebsocketsConfig  : BaseConfig
    {
        public WebsocketsConfig() : base()
        {
        }

        [DefaultValue(true)]
        [JsonProperty(Required = Required.DisallowNull, DefaultValueHandling = DefaultValueHandling.Populate, Order = 1)]
        [NecroBotConfig(Description = "Allow Visualizer to connect to bot using web socket", Position = 1)]
        public bool UseWebsocket { get; set; }

        [DefaultValue(14251)]
        [Range(1, 65535)]
        [NecroBotConfig(Description = "Specify web socket port. you need to set this port/neighbor port into visualizer", Position = 2)]
        [JsonProperty(Required = Required.DisallowNull, DefaultValueHandling = DefaultValueHandling.Populate, Order = 2)]
        public int WebSocketPort  { get; set; }
    }
}