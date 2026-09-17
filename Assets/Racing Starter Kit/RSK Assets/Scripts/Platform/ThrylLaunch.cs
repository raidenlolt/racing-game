using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// what the THRYL platform tells the game when it opens the hosted page. the parent adds these
    /// as query parameters on index.html, and the single player guide fixes the names, the
    /// fallbacks and the priorities:
    ///   token      over usertoken
    ///   playerId   over player_id
    ///   name       over username, default Guest
    ///   timer      minutes, default 5, clamped 1..120, not enforced as a countdown
    ///   game_highest_score   positive integer or 0
    /// custom_game_id is the one value that must go back in the score request, so it is kept as the
    /// raw string and also parsed, because the contract shows a number while the reference
    /// implementation sends a string.
    /// </summary>
    [Serializable]
    public class ThrylLaunch
    {
        public string gameType = "";
        public string customGameId = "";
        public int customGameIdNumber = -1;
        public string token = "";
        public string playerId = "";
        public string playerName = "Guest";
        public int timerMinutes = 5;
        public int highestScore;
        public string sourceUrl = "";

        public bool HasToken { get { return !string.IsNullOrEmpty(token); } }
        public bool HasGameId { get { return !string.IsNullOrEmpty(customGameId); } }
        /// <summary>enough to submit a score: a token and a game id</summary>
        public bool CanSubmit { get { return HasToken && HasGameId; } }
        public bool IsSinglePlayer { get { return string.IsNullOrEmpty(gameType) || gameType == "single"; } }

        public static ThrylLaunch Parse(string url)
        {
            var launch = new ThrylLaunch { sourceUrl = url ?? "" };
            var query = Query(url);

            launch.gameType = Get(query, "game_type");
            launch.customGameId = Get(query, "custom_game_id");
            int id;
            if (int.TryParse(launch.customGameId, out id)) launch.customGameIdNumber = id;

            // "Bearer" must not be in the URL value, but strip it if a platform sends it anyway
            launch.token = First(query, "token", "usertoken");
            if (launch.token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                launch.token = launch.token.Substring(7).Trim();

            launch.playerId = First(query, "playerId", "player_id");

            var name = First(query, "name", "username");
            launch.playerName = string.IsNullOrEmpty(name) ? "Guest" : name;

            int timer;
            launch.timerMinutes = int.TryParse(Get(query, "timer"), out timer) ? Mathf.Clamp(timer, 1, 120) : 5;

            int best;
            launch.highestScore = int.TryParse(Get(query, "game_highest_score"), out best) && best > 0 ? best : 0;

            return launch;
        }

        /// <summary>the query string as a dictionary, decoded, first occurrence of a key wins</summary>
        private static Dictionary<string, string> Query(string url)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(url)) return result;

            var start = url.IndexOf('?');
            if (start < 0) return result;
            var query = url.Substring(start + 1);
            var hash = query.IndexOf('#');
            if (hash >= 0) query = query.Substring(0, hash);

            foreach (var pair in query.Split('&'))
            {
                if (pair.Length == 0) continue;
                var eq = pair.IndexOf('=');
                var key = Decode(eq < 0 ? pair : pair.Substring(0, eq));
                var value = eq < 0 ? "" : Decode(pair.Substring(eq + 1));
                if (key.Length > 0 && !result.ContainsKey(key)) result[key] = value;
            }
            return result;
        }

        private static string Decode(string s)
        {
            try { return Uri.UnescapeDataString(s.Replace('+', ' ')); }
            catch { return s; }
        }

        private static string Get(Dictionary<string, string> query, string key)
        {
            string value;
            return query.TryGetValue(key, out value) ? value.Trim() : "";
        }

        private static string First(Dictionary<string, string> query, params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = Get(query, key);
                if (!string.IsNullOrEmpty(value)) return value;
            }
            return "";
        }

        public override string ToString()
        {
            return "game " + customGameId + " player " + playerName + (string.IsNullOrEmpty(playerId) ? "" : " (" + playerId + ")")
                   + " token " + (HasToken ? "yes" : "no") + " timer " + timerMinutes + "m best " + highestScore;
        }
    }
}
