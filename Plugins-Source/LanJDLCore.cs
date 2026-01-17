using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("LanJDL-Core", "DevRust", "1.7.3")]
    public class LanJDLCore : RustPlugin
    {
        // On récupère manuellement la bibliothèque WebRequests pour éviter l'erreur "does not exist"
        private WebRequests webrequests = Interface.Oxide.GetLibrary<WebRequests>();

        private string ConfigUrl = "https://raw.githubusercontent.com/Lan-JDL-Gaming/Master-Config/refs/heads/main/server_settings.json";
        private RemoteConfig remoteSettings;
        private Dictionary<string, PlayerSessionData> playerStats = new Dictionary<string, PlayerSessionData>();
        private bool maintenanceWarned = false;

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

        void OnServerInitialized() 
        { 
            LoadData(); 
            timer.Once(10f, () => {
                PostEmbed("🚀 LAN JDL - SERVER ONLINE", "Le serveur est désormais en ligne et prêt à accueillir les joueurs.", 3066993, false);
            });
            timer.Repeat(60f, 0, () => CheckMaintenanceTime());
        }

        private void UpdateConfigFromGitHub()
        {
            // Utilisation de la référence locale 'webrequests' déclarée plus haut
            webrequests.Enqueue(ConfigUrl, null, (code, response) => {
                if (code == 200 && !string.IsNullOrEmpty(response))
                {
                    remoteSettings = JsonConvert.DeserializeObject<RemoteConfig>(response);
                    Puts("[LanJDL] Config GitHub synchronisée avec succès.");
                }
            }, this);
        }

        private void CheckMaintenanceTime()
        {
            var now = DateTime.Now;
            if (now.Hour == 4 && now.Minute == 25 && !maintenanceWarned)
            {
                maintenanceWarned = true;
                PostEmbed("⚠️ ALERTE MAINTENANCE", "🛠️ **Le serveur va redémarrer dans 5 minutes** (4h30) pour la maintenance quotidienne.", 15105570, false);
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "say <color=#ff4444>MAINTENANCE : Redémarrage dans 5 minutes !</color>");
            }
            if (now.Hour == 5) maintenanceWarned = false;
        }

        private void PostEmbed(string title, string description, int color, bool isAdmin)
        {
            string url = isAdmin ? remoteSettings?.AdminWebhookUrl : remoteSettings?.DiscordWebhookUrl;
            if (string.IsNullOrEmpty(url)) return;

            var embed = new {
                title = title,
                description = description,
                color = color,
                footer = new { text = $"Lan JDL System - {DateTime.Now:HH:mm}" }
            };

            var payload = new { embeds = new[] { embed } };
            var json = JsonConvert.SerializeObject(payload);

            webrequests.Enqueue(url, json, (code, response) => {}, this, RequestMethod.POST, new Dictionary<string, string> { { "Content-Type", "application/json" } });
        }

        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null || remoteSettings == null || !(entity is BuildingBlock)) return;
            if (entity.OwnerID == 0 || info.InitiatorPlayer == null) return;
            var owner = BasePlayer.FindByID(entity.OwnerID);
            if (owner == null || !owner.IsConnected) {
                info.damageTypes.ScaleAll(1f / remoteSettings.OfflineProtectionMultiplier);
                if (info.damageTypes.Has(Rust.DamageType.Explosion))
                    PostEmbed("⚠️ ALERTE RAID OFFLINE", $"**Cible :** {owner?.displayName ?? "Inconnu"}\nStructure protégée (x{remoteSettings.OfflineProtectionMultiplier}).", 15158332, true);
            }
        }

        void OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            var player = entity as BasePlayer;
            if (player == null || remoteSettings == null || !playerStats.ContainsKey(player.UserIDString)) return;
            double total = playerStats[player.UserIDString].SecondsPlayedToday + (DateTime.Now - playerStats[player.UserIDString].LastLogin).TotalSeconds;
            float mult = (total < (remoteSettings.BoostDurationHours * 3600)) ? remoteSettings.GatherMultiplierBoost : remoteSettings.GatherMultiplierNormal;
            item.amount = (int)(item.amount * mult);
        }

        void OnPlayerConnected(BasePlayer player) {
            if (!playerStats.ContainsKey(player.UserIDString)) playerStats[player.UserIDString] = new PlayerSessionData { LastResetDate = DateTime.Now.ToString("yyyy-MM-dd") };
            playerStats[player.UserIDString].LastLogin = DateTime.Now;
        }

        void SaveData() => Interface.Oxide.DataFileSystem.WriteObject("LanJDL_PlayerData", playerStats);
        void LoadData() => playerStats = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, PlayerSessionData>>("LanJDL_PlayerData");
    }
}
