using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace PoGo.NecroBot.Logic.Model.Settings
{
    /// <summary>
    /// Google has some limitations for free use
    /// 2,500 free requests per day, calculated as the sum of client-side and server-side queries; enable billing to access higher daily quotas, billed at $0.50 USD / 1000 additional requests, up to 100,000 requests daily.
    /// With cache enabled, we can optimize the use.
    ///  </summary>
    [JsonObject(Title = "Google Walk Config", Description = "Set your google walk settings (set \"GoogleAPIKey\" if you have a key, nowadays a single contract is $16.000,00 USD. With a key you can deactivate Cache.", ItemRequired = Required.DisallowNull)]
    public class GoogleWalkConfig  : BaseConfig
    {
        public GoogleWalkConfig() : base()
        {
        }

        internal enum GoogleWalkTravelModes
        {
            driving,
            walking,
            bicycling,
            transit
        }

        [NecroBotConfig(Description = "Use Google's API to resolve paths for destinations", Position = 1)]
        [DefaultValue(true)]
        [JsonProperty(Required = Required.DisallowNull, DefaultValueHandling = DefaultValueHandling.Populate, Order = 1)]
        public bool UseGoogleWalk { get; set; }

        [NecroBotConfig(Description = "Set step length in met for 1 step", Position = 2)]
        [DefaultValue(1.3d)]
        [JsonProperty(Required = Required.DisallowNull, DefaultValueHandling = DefaultValueHandling.Populate, Order = 2)]
        public double DefaultStepLength { get; set; }

        [NecroBotConfig(Description = "Set heuristic for Google's Algorithm Detection - driving, walking, bicycling, transit", Position = 3)]
        [DefaultValue("walking")]
        [EnumDataType(typeof(GoogleWalkTravelModes))]
        [JsonProperty(Required = Required.DisallowNull, DefaultValueHandling = DefaultValueHandling.Populate, Order = 3)]
        //https://developers.google.com/maps/documentation/directions/intro?hl=pt-br#TravelModes
        public string GoogleHeuristic { get; set; }

        [NecroBotConfig(Description = "API Key to be used to connect to Google's API Service", Position = 4)]
        [DefaultValue(null)]
        [MinLength(0)]
        [MaxLength(64)]
        [JsonProperty(Required = Required.Default, DefaultValueHandling = DefaultValueHandling.Populate, Order = 4)]
        // If you have a key, nowadays a single contract is $16.000,00 USD. With a key you can deactivate Cache
        public string GoogleAPIKey { get; set; }

        [NecroBotConfig(Description = "Allows bot to use cache ", Position = 5)]
        [DefaultValue(true)]
        [JsonProperty("Cache", Required = Required.DisallowNull, DefaultValueHandling = DefaultValueHandling.Populate, Order = 5)]
        public bool Cache { get; set; }

        [NecroBotConfig(Description = "API key to be used to connect to Google's Elevation API Service (Not the Same as Google API Key)", Position = 6)]
        [DefaultValue(null)]
        [MinLength(0)]
        [MaxLength(64)]
        [JsonProperty(Required = Required.Default, DefaultValueHandling = DefaultValueHandling.Populate, Order = 6)]
        // This can be the same as the GoogleAPIKey, but if so then you need to activate Elevation API for the key.
        public string GoogleElevationAPIKey { get; set; }
    }
}