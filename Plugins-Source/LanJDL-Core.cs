using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("LanJDL-Core", "DevRust", "1.2.0")]
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
            public string WipeDate { get; set; } // Format: "YYYY-MM-DD"
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
                Puts($"[LanJDL] Config chargée. Wipe le: {remoteSettings.WipeDate}");
            }, this);
        }

        // --- PROTECTION OFFLINE X5 ---
        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null || remoteSettings == null) return;
            if (!(entity is BuildingBlock)) return; // On ne protège que les structures

            var ownerId = entity.OwnerID;
            if (ownerId == 0) return;

            var targetPlayer = BasePlayer.FindByID(ownerId);
            if (targetPlayer == null || !targetPlayer.IsConnected)
            {
                // Le joueur (ou le chef de clan) est hors-ligne
                info.damageTypes.ScaleAll(1f / remoteSettings.OfflineProtectionMultiplier);
            }
        }

        // --- PROGRESSION DES TIERS (BLOCAGE WORKBENCH) ---
        object CanInteract(BasePlayer player, Workbench workbench)
        {
            if (remoteSettings == null) return null;

            int daysSinceWipe = GetDaysSinceWipe();
            int benchLevel = workbench.Workbenchlevel;

            if (benchLevel == 2 && daysSinceWipe < 4)
            {
                player.ChatMessage("<color=#ff4444>[LanJDL]</color> Le Tier 2 sera débloqué au Jour 4.");
                return false;
            }
            if (benchLevel == 3 && daysSinceWipe < 7)
            {
                player.ChatMessage("<color=#ff4444>[LanJDL]</color> Le Tier 3 sera débloqué au Jour 7.");
                return false;
            }
            return null;
        }

        // Bloque aussi le craft direct si le niveau requis n'est pas atteint
        object CanCraft(ItemCrafter itemCrafter, ItemBlueprint bp, int amount)
        {
            if (remoteSettings == null) return null;
            int daysSinceWipe = GetDaysSinceWipe();

            if (bp.workbenchLevelRequired == 2 && daysSinceWipe < 4) return false;
            if (bp.workbenchLevelRequired == 3 && daysSinceWipe < 7) return false;
            return null;
        }

        private int GetDaysSinceWipe()
        {
            DateTime wipe;
            if (DateTime.TryParse(remoteSettings.WipeDate, out wipe))
            {
                return (DateTime.Now.Date - wipe.Date).Days + 1;
            }
            return 1;
        }

        // --- LOGIQUE DE RÉCOLTE (BOOST 3H) ---
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
            double totalToday = playerStats[id].SecondsPlayedToday + (DateTime.Now - playerStats[id].LastLogin).TotalSeconds;
            return totalToday < (remoteSettings.BoostDurationHours * 3600);
        }

        void OnPlayerConnected(BasePlayer player)
        {
            string id = player.UserIDString;
            if (!playerStats.ContainsKey(id)) 
                playerStats[id] = new PlayerSessionData { LastResetDate = DateTime.Now.ToString("yyyy-MM-dd") };
            
            playerStats[id].LastLogin = DateTime.Now;
            CheckAndResetDay(id);

            int days = GetDaysSinceWipe();
            player.ChatMessage($"<color=#55ff55>Lan JDL - Jour {days} du Wipe</color>");
            player.ChatMessage($"Boost x{remoteSettings?.GatherMultiplierBoost} actif: {GetRemainingBoostTime(player)} min restantes.");
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
            double totalToday = playerStats[player.UserIDString].SecondsPlayedToday + (DateTime.Now - playerStats[player.UserIDString].LastLogin).TotalSeconds;
            double remaining = (remoteSettings.BoostDurationHours * 3600) - totalToday;
            return remaining > 0 ? (int)(remaining / 60) : 0;
        }

        void SaveData() => Interface.Oxide.DataFiles.WriteObject("LanJDL_PlayerData", playerStats);
        void LoadData() => playerStats = Interface.Oxide.DataFiles.ReadObject<Dictionary<string, PlayerSessionData>>("LanJDL_PlayerData");
    }
}
