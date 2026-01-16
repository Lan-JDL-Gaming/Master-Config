using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("LanJDL-Core", "DevRust", "1.0.0")]
    public class LanJDLCore : RustPlugin
    {
        private string ConfigUrl = "https://raw.githubusercontent.com/Lan-JDL-Gaming/Master-Config/refs/heads/main/server_settings.json";
        
        private RemoteConfig remoteSettings;
        private Dictionary<string, PlayerSessionData> playerStats = new Dictionary<string, PlayerSessionData>();

        // Structure pour la configuration GitHub
        private class RemoteConfig
        {
            public float BoostDurationHours { get; set; }
            public float GatherMultiplierBoost { get; set; }
            public float GatherMultiplierNormal { get; set; }
            public float OfflineProtectionMultiplier { get; set; }
        }

        // Données de session du joueur
        private class PlayerSessionData
        {
            public DateTime LastLogin;
            public double SecondsPlayedToday;
            public string LastResetDate;
        }

        void Init()
        {
            LoadData();
            UpdateConfigFromGitHub();
        }

        // --- Logique de Récupération GitHub ---
        private void UpdateConfigFromGitHub()
        {
            webrequests.Enqueue(ConfigUrl, null, (code, response) =>
            {
                if (code != 200 || string.IsNullOrEmpty(response)) {
                    Puts("Erreur : Impossible de lire la config GitHub. On garde les valeurs par défaut.");
                    return;
                }
                remoteSettings = JsonConvert.DeserializeObject<RemoteConfig>(response);
                Puts($"Config Lan JDL chargée : Boost de {remoteSettings.BoostDurationHours}h actif.");
            }, this);
        }

        // --- Logique de Récolte Dynamique ---
        void OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            var player = entity as BasePlayer;
            if (player == null || remoteSettings == null) return;

            if (IsBoostActive(player))
            {
                item.amount = (int)(item.amount * remoteSettings.GatherMultiplierBoost);
            }
            else
            {
                item.amount = (int)(item.amount * remoteSettings.GatherMultiplierNormal);
            }
        }

        private bool IsBoostActive(BasePlayer player)
        {
            string id = player.UserIDString;
            CheckAndResetDay(id);

            // Calcul du temps écoulé depuis la connexion actuelle
            double sessionSeconds = (DateTime.Now - playerStats[id].LastLogin).TotalSeconds;
            double totalToday = playerStats[id].SecondsPlayedToday + sessionSeconds;

            return totalToday < (remoteSettings.BoostDurationHours * 3600);
        }

        // --- Gestion des Données Joueurs ---
        void OnPlayerConnected(BasePlayer player)
        {
            string id = player.UserIDString;
            if (!playerStats.ContainsKey(id)) 
                playerStats[id] = new PlayerSessionData { LastResetDate = DateTime.Now.ToString("yyyy-MM-dd") };
            
            playerStats[id].LastLogin = DateTime.Now;
            CheckAndResetDay(id);

            player.ChatMessage($"Bienvenue sur Lan JDL ! Boost de récolte x{remoteSettings?.GatherMultiplierBoost} actif pour encore {(GetRemainingBoostTime(player))} minutes.");
        }

        void OnPlayerDisconnected(BasePlayer player)
        {
            string id = player.UserIDString;
            if (playerStats.ContainsKey(id))
            {
                playerStats[id].SecondsPlayedToday += (DateTime.Now - playerStats[id].LastLogin).TotalSeconds;
                SaveData();
            }
        }

        private void CheckAndResetDay(string id)
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (playerStats[id].LastResetDate != today)
            {
                playerStats[id].SecondsPlayedToday = 0;
                playerStats[id].LastResetDate = today;
                playerStats[id].LastLogin = DateTime.Now;
            }
        }

        private int GetRemainingBoostTime(BasePlayer player)
        {
            if (remoteSettings == null) return 0;
            double sessionSeconds = (DateTime.Now - playerStats[player.UserIDString].LastLogin).TotalSeconds;
            double totalToday = playerStats[player.UserIDString].SecondsPlayedToday + sessionSeconds;
            double remaining = (remoteSettings.BoostDurationHours * 3600) - totalToday;
            return remaining > 0 ? (int)(remaining / 60) : 0;
        }

        void SaveData() => Interface.Oxide.DataFiles.WriteObject("LanJDL_PlayerData", playerStats);
        void LoadData() => playerStats = Interface.Oxide.DataFiles.ReadObject<Dictionary<string, PlayerSessionData>>("LanJDL_PlayerData");
    }
}
