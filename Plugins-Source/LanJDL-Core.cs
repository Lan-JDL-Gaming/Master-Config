using System;
using System.Collections.Generic;
using System.IO;
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

        private class RemoteConfig
        {
            public float BoostDurationHours { get; set; }
            public float GatherMultiplierBoost { get; set; }
            public float GatherMultiplierNormal { get; set; }
            public float OfflineProtectionMultiplier { get; set; }
            public string WipeDate { get; set; } 
        }

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

        private void UpdateConfigFromGitHub()
        {
            webrequests.Enqueue(ConfigUrl, null, (code, response) =>
            {
                if (code != 200 || string.IsNullOrEmpty(response)) return;
                remoteSettings = JsonConvert.DeserializeObject<RemoteConfig>(response);
                Puts($"[LanJDL] Config GitHub synchronisée. Jour actuel : {GetDaysSinceWipe()}");
            }, this);
        }

        // --- CALCUL AUTOMATIQUE DU JOUR DE WIPE ---
        private int GetDaysSinceWipe()
        {
            DateTime wipeDate;

            // 1. On essaie de lire la date forcée sur GitHub
            if (remoteSettings != null && !string.IsNullOrEmpty(remoteSettings.WipeDate))
            {
                if (DateTime.TryParse(remoteSettings.WipeDate, out wipeDate))
                    return (DateTime.Now.Date - wipeDate.Date).Days + 1;
            }

            // 2. Sinon, on regarde la date de création du fichier de la map (.sav)
            try
            {
                string saveFolder = $"{ConVar.Server.root}/save/{ConVar.Server.identity}";
                if (Directory.Exists(saveFolder))
                {
                    var files = new DirectoryInfo(saveFolder).GetFiles("proceduralmap.*.sav");
                    if (files.Length > 0)
                    {
                        wipeDate = files[0].CreationTime;
                        return (DateTime.Now.Date - wipeDate.Date).Days + 1;
                    }
                }
            }
            catch { }

            return 1; // Par défaut Jour 1
        }

        // --- PROTECTION OFFLINE X5 ---
        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null || remoteSettings == null || !(entity is BuildingBlock)) return;

            var ownerId = entity.OwnerID;
            if (ownerId == 0) return;

            var targetPlayer = BasePlayer.FindByID(ownerId);
            if (targetPlayer == null || !targetPlayer.IsConnected)
            {
                info.damageTypes.ScaleAll(1f / remoteSettings.OfflineProtectionMultiplier);
            }
        }

        // --- PROGRESSION DES TIERS ---
        object CanInteract(BasePlayer player, Workbench workbench)
        {
            int day = GetDaysSinceWipe();
            if (workbench.Workbenchlevel == 2 && day < 4) 
            {
                player.ChatMessage("<color=#ff4444>[LanJDL]</color> Tier 2 bloqué jusqu'au Jour 4.");
                return false;
            }
            if (workbench.Workbenchlevel == 3 && day < 7) 
            {
                player.ChatMessage("<color=#ff4444>[LanJDL]</color> Tier 3 bloqué jusqu'au Jour 7.");
                return false;
            }
            return null;
        }

        object CanCraft(ItemCrafter itemCrafter, ItemBlueprint bp, int amount)
        {
            int day = GetDaysSinceWipe();
            if (bp.workbenchLevelRequired == 2 && day < 4) return false;
            if (bp.workbenchLevelRequired == 3 && day < 7) return false;
            return null;
        }

        // --- RÉCOLTE & BOOST ---
        void OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            var player = entity as BasePlayer;
            if (player == null || remoteSettings == null) return;
            float mult = IsBoostActive(player) ? remoteSettings.GatherMultiplierBoost : remoteSettings.GatherMultiplierNormal;
            item.amount = (int)(item.amount * mult);
        }

        private bool IsBoostActive(BasePlayer player)
        {
            string id = player.UserIDString;
            CheckAndResetDay(id);
            double total = playerStats[id].SecondsPlayedToday + (DateTime.Now - playerStats[id].LastLogin).TotalSeconds;
            return total < (remoteSettings.BoostDurationHours * 3600);
        }

        void OnPlayerConnected(BasePlayer player)
        {
            string id = player.UserIDString;
            if (!playerStats.ContainsKey(id)) 
                playerStats[id] = new PlayerSessionData { LastResetDate = DateTime.Now.ToString("yyyy-MM-dd") };
            playerStats[id].LastLogin = DateTime.Now;
            CheckAndResetDay(id);

            player.ChatMessage($"<color=#55ff55>Lan JDL - Jour {GetDaysSinceWipe()} du Wipe</color>");
            player.ChatMessage($"Boost x{remoteSettings?.GatherMultiplierBoost} actif: {GetRemainingBoostTime(player)} min restantes.");
        }

        void OnPlayerDisconnected(BasePlayer player)
        {
            if (playerStats.ContainsKey(player.UserIDString))
            {
                playerStats[player.UserIDString].SecondsPlayedToday += (DateTime.Now - playerStats[player.UserIDString].LastLogin).TotalSeconds;
                SaveData();
            }
        }

        private void CheckAndResetDay(string id)
        {
            if (playerStats[id].LastResetDate != DateTime.Now.ToString("yyyy-MM-dd"))
            {
                playerStats[id].SecondsPlayedToday = 0;
                playerStats[id].LastResetDate = DateTime.Now.ToString("yyyy-MM-dd");
                playerStats[id].LastLogin = DateTime.Now;
            }
        }

        private int GetRemainingBoostTime(BasePlayer player)
        {
            if (remoteSettings == null) return 0;
            double total = playerStats[player.UserIDString].SecondsPlayedToday + (DateTime.Now - playerStats[player.UserIDString].LastLogin).TotalSeconds;
            double remaining = (remoteSettings.BoostDurationHours * 3600) - total;
            return remaining > 0 ? (int)(remaining / 60) : 0;
        }

        void SaveData() => Interface.Oxide.DataFiles.WriteObject("LanJDL_PlayerData", playerStats);
        void LoadData() => playerStats = Interface.Oxide.DataFiles.ReadObject<Dictionary<string, PlayerSessionData>>("LanJDL_PlayerData");
    }
}
