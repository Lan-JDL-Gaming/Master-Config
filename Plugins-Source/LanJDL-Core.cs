using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("LanJDL-Core", "DevRust", "1.0.0")]
    public class LanJDLCore : RustPlugin
    {
        private string ConfigUrl = "https://raw.githubusercontent.com/Lan-JDL-Gaming/Master-Config/refs/heads/main/server_settings.json";
        private RemoteConfig remoteSettings;
        private Dictionary<string, PlayerSessionData> playerStats = new Dictionary<string, PlayerSessionData>();
        private bool tierAnnouncedToday = false;

        private class RemoteConfig
        {
            public float BoostDurationHours { get; set; }
            public float GatherMultiplierBoost { get; set; }
            public float GatherMultiplierNormal { get; set; }
            public float OfflineProtectionMultiplier { get; set; }
            public string WipeDate { get; set; }
            public string DiscordWebhookUrl { get; set; } // Nouvelle variable
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

        // --- OPTION 2 : ALERTE STATUT SERVEUR (Démarrage) ---
        void OnServerInitialized()
        {
            timer.Once(5f, () => {
                if (remoteSettings != null)
                {
                    PostToDiscord("🚀 **Système LAN JDL en ligne**\nLe serveur est prêt. Bon jeu à tous !");
                    CheckAndAnnounceTier();
                }
            });
        }

        private void UpdateConfigFromGitHub()
        {
            webrequests.Enqueue(ConfigUrl, null, (code, response) =>
            {
                if (code != 200 || string.IsNullOrEmpty(response)) return;
                remoteSettings = JsonConvert.DeserializeObject<RemoteConfig>(response);
                Puts($"[LanJDL] Config synchronisée.");
            }, this);
        }

        // --- OPTION 1 : ANNONCE DES TIERS ---
        private void CheckAndAnnounceTier()
        {
            int day = GetDaysSinceWipe();
            string message = "";

            if (day == 1) message = "📅 **Wipe Day !**\nL'aventure commence. Tier 1 débloqué !";
            else if (day == 4) message = "🛡️ **Progression : Tier 2 Débloqué !**\nLes nouveaux établis et crafts sont désormais disponibles.";
            else if (day == 7) message = "⚔️ **Progression : Tier 3 Débloqué !**\nC'est l'heure du endgame. Bonne chance !";

            if (!string.IsNullOrEmpty(message))
            {
                PostToDiscord(message);
            }
        }

        // --- LOGIQUE WEBHOOK DISCORD ---
        private void PostToDiscord(string message)
        {
            if (remoteSettings == null || string.IsNullOrEmpty(remoteSettings.DiscordWebhookUrl)) return;

            var payload = new { content = message };
            var json = JsonConvert.SerializeObject(payload);

            webrequests.Enqueue(remoteSettings.DiscordWebhookUrl, json, (code, response) => {}, this, RequestMethod.POST, new Dictionary<string, string> {
                { "Content-Type", "application/json" }
            });
        }

        // --- CALCUL DU JOUR (Automatique) ---
        private int GetDaysSinceWipe()
        {
            try {
                string saveFolder = $"{ConVar.Server.root}/save/{ConVar.Server.identity}";
                var files = new System.IO.DirectoryInfo(saveFolder).GetFiles("proceduralmap.*.sav");
                if (files.Length > 0) {
                    DateTime wipeDate = files[0].CreationTime;
                    return (DateTime.Now.Date - wipeDate.Date).Days + 1;
                }
            } catch { }
            return 1;
        }

        // --- RÉCOLTE & PROTECTION (Inchangé) ---
        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null || remoteSettings == null || !(entity is BuildingBlock)) return;
            if (entity.OwnerID == 0) return;
            var target = BasePlayer.FindByID(entity.OwnerID);
            if (target == null || !target.IsConnected) info.damageTypes.ScaleAll(1f / remoteSettings.OfflineProtectionMultiplier);
        }

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
            if (!playerStats.ContainsKey(id)) playerStats[id] = new PlayerSessionData { LastResetDate = DateTime.Now.ToString("yyyy-MM-dd") };
            playerStats[id].LastLogin = DateTime.Now;
            CheckAndResetDay(id);
            player.ChatMessage($"<color=#55ff55>Lan JDL - Jour {GetDaysSinceWipe()}</color>");
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

        void SaveData() => Interface.Oxide.DataFiles.WriteObject("LanJDL_PlayerData", playerStats);
        void LoadData() => playerStats = Interface.Oxide.DataFiles.ReadObject<Dictionary<string, PlayerSessionData>>("LanJDL_PlayerData");
    }
}
