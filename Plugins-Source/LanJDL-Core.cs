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

        private class RemoteConfig
        {
            public float BoostDurationHours { get; set; }
            public float GatherMultiplierBoost { get; set; }
            public float GatherMultiplierNormal { get; set; }
            public float OfflineProtectionMultiplier { get; set; }
            public string DiscordWebhookUrl { get; set; }
            public string AdminWebhookUrl { get; set; }
        }

        private class PlayerSessionData
        {
            public DateTime LastLogin;
            public double SecondsPlayedToday;
            public string LastResetDate;
        }

        void Init() => UpdateConfigFromGitHub();
        void OnServerInitialized() { LoadData(); timer.Once(5f, () => PostEmbed("Lan JDL Système", "🚀 Le serveur est désormais en ligne et prêt à accueillir les joueurs.", 3066993, false)); }

        private void UpdateConfigFromGitHub()
        {
            webrequests.Enqueue(ConfigUrl, null, (code, response) => {
                if (code == 200 && !string.IsNullOrEmpty(response))
                    remoteSettings = JsonConvert.DeserializeObject<RemoteConfig>(response);
            }, this);
        }

        // --- ALERTE RAID OFFLINE (STYLISÉE) ---
        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null || remoteSettings == null || !(entity is BuildingBlock)) return;
            if (entity.OwnerID == 0 || info.InitiatorPlayer == null) return;

            var owner = BasePlayer.FindByID(entity.OwnerID);
            if (owner == null || !owner.IsConnected)
            {
                info.damageTypes.ScaleAll(1f / remoteSettings.OfflineProtectionMultiplier);
                if (info.damageTypes.Has(Rust.DamageType.Explosion))
                {
                    PostEmbed("⚠️ ALERTE RAID OFFLINE", $"**Attaquant :** {info.InitiatorPlayer.displayName}\n**Cible :** {owner?.displayName ?? "Inconnu"}\n**Action :** Utilisation d'explosifs sur structure protégée (x{remoteSettings.OfflineProtectionMultiplier}).", 15158332, true);
                }
            }
        }

        // --- LOGIQUE WEBHOOK AVEC EMBEDS ---
        private void PostEmbed(string title, string description, int color, bool isAdmin)
        {
            string url = isAdmin ? remoteSettings?.AdminWebhookUrl : remoteSettings?.DiscordWebhookUrl;
            if (string.IsNullOrEmpty(url)) return;

            var embed = new {
                title = title,
                description = description,
                color = color,
                footer = new { text = $"Lan JDL - {DateTime.Now:HH:mm}" }
            };

            var payload = new { embeds = new[] { embed } };
            var json = JsonConvert.SerializeObject(payload);

            webrequests.Enqueue(url, json, (code, response) => {}, this, RequestMethod.POST, new Dictionary<string, string> { { "Content-Type", "application/json" } });
        }

        // --- RESTE DU CODE (Calcul jour, Boost, etc.) ---
        private int GetDaysSinceWipe() {
            try {
                string path = $"{ConVar.Server.root}/save/{ConVar.Server.identity}";
                var files = new System.IO.DirectoryInfo(path).GetFiles("proceduralmap.*.sav");
                if (files.Length > 0) return (DateTime.Now.Date - files[0].CreationTime.Date).Days + 1;
            } catch { }
            return 1;
        }

        void OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            var player = entity as BasePlayer;
            if (player == null || remoteSettings == null) return;
            bool active = IsBoostActive(player);
            item.amount = (int)(item.amount * (active ? remoteSettings.GatherMultiplierBoost : remoteSettings.GatherMultiplierNormal));
        }

        private bool IsBoostActive(BasePlayer player) {
            if (!playerStats.ContainsKey(player.UserIDString)) return false;
            double total = playerStats[player.UserIDString].SecondsPlayedToday + (DateTime.Now - playerStats[player.UserIDString].LastLogin).TotalSeconds;
            return total < (remoteSettings.BoostDurationHours * 3600);
        }

        void OnPlayerConnected(BasePlayer player) {
            string id = player.UserIDString;
            if (!playerStats.ContainsKey(id)) {
                playerStats[id] = new PlayerSessionData { LastResetDate = DateTime.Now.ToString("yyyy-MM-dd") };
                PostEmbed("🆕 Nouveau Citoyen", $"{player.displayName} a rejoint l'aventure Lan JDL !", 3447003, true);
            }
            playerStats[id].LastLogin = DateTime.Now;
        }

        void SaveData() => Interface.Oxide.DataFiles.WriteObject("LanJDL_PlayerData", playerStats);
        void LoadData() => playerStats = Interface.Oxide.DataFiles.ReadObject<Dictionary<string, PlayerSessionData>>("LanJDL_PlayerData");
    }
}
