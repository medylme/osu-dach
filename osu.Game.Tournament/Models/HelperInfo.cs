// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using Newtonsoft.Json;
using osu.Framework.Bindables;
using osu.Framework.Platform;
using osu.Game.Tournament.IO;

namespace osu.Game.Tournament.Models
{
    /// <summary>
    /// Stores configuration for the osu-tourney-data-reader helper integration,
    /// including the WebSocket endpoint and whether helper-based scoring is active.
    /// </summary>
    [Serializable]
    public class HelperInfo
    {
        [JsonIgnore]
        public Bindable<bool> HelperEnabled { get; } = new Bindable<bool>();

        [JsonProperty("Enabled")]
        public bool? HelperEnabledSerialised
        {
            get => HelperEnabled.Value;
            set => HelperEnabled.Value = value ?? false;
        }

        public string? WebsocketUrl { get; set; }

        public event Action? OnHelperInfoSaved;

        private const string config_path = "score_helper.json";

        private readonly Storage configStorage;

        public HelperInfo(TournamentStorage storage)
        {
            configStorage = storage.AllTournaments;

            if (!configStorage.Exists(config_path))
                return;

            using (Stream stream = configStorage.GetStream(config_path, FileAccess.Read, FileMode.Open))
            using (var sr = new StreamReader(stream))
            {
                JsonConvert.PopulateObject(sr.ReadToEnd(), this);
            }
        }

        public string Serialise()
        {
            return JsonConvert.SerializeObject(this,
                new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore,
                    DefaultValueHandling = DefaultValueHandling.Ignore,
                });
        }

        public void SaveChanges()
        {
            using (var stream = configStorage.CreateFileSafely(config_path))
            using (var sw = new StreamWriter(stream))
            {
                sw.Write(Serialise());
            }

            OnHelperInfoSaved?.Invoke();
        }
    }
}
