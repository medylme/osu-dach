// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Newtonsoft.Json;

namespace osu.Game.Tournament.Models
{
    public class RoundBeatmap
    {
        public int ID;
        public string Mods = string.Empty;

        /// <summary>
        /// Custom score multipliers for EZ mod combinations.
        /// When null or disabled, no multipliers are applied.
        /// </summary>
        public CustomModMultipliers? CustomModMultipliers;

        [JsonProperty("BeatmapInfo")]
        public TournamentBeatmap? Beatmap;
    }

    public class CustomModMultipliers
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
        public bool Enabled;

        public double EZ = 1.0;
        public double EZHD = 1.0;
    }
}
