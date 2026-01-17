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
            // Annonce de mise en ligne
            timer.Once(5f, () => {
                PostEmbed("🚀 LAN JDL - SERVER ONLINE", "Le serveur est désormais en ligne et prêt à accueillir les joueurs.", 3066993, false);
            });
            // Surveillance de l'heure pour la maintenance de 4h30
            timer.Repeat(60f, 0, () => CheckMaintenanceTime());
        }

        // Se déclenche lors d'un arrêt ou d'un reload
        void Unload()
        {
            SaveData();
            PostEmbed("🛑 LAN JDL - SERVER OFFLINE", "Le serveur redémarre ou s'éteint pour maintenance.", 15158332, false);
        }

        private void UpdateConfig
