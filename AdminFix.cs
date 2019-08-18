using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core.Libraries;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Admin Fix", "August", "1.1.4")]

    internal class AdminFix : RustPlugin
    {
        #region Initialization/Configuration

        private const string Perm = "adminfix.override";
        private List<BasePlayer> OnlineAdmins = new List<BasePlayer>();
        private Dictionary<BasePlayer, bool> Godmode = new Dictionary<BasePlayer, bool>();
        private Dictionary<BasePlayer, int> Offenses = new Dictionary<BasePlayer, int>(); 
        private Configuration config;

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(new Configuration(), true);
        }
        private class Configuration
        {
            [JsonProperty("Webhook URL (for messages)")]
            public string WebhookUrl { get; set; }
            
            [JsonProperty("Time between each check (seconds)")]
            public int Interval { get; set; } = 15;

            [JsonProperty("Cancel attack if admin is in God Mode")]
            public bool CancelAttack { get; set; } = true;

            [JsonProperty("Autoturrets do not toggle admins in god mode")]
            public bool CanTargetGodmodeAdmin { get; set; } = true;

            [JsonProperty("How many times admins can attack (while in GM) before being logged to discord?")]
            public int AttacksBefore { get; set; } = 10;
            
            [JsonProperty("Kick admins after attacking x amount of times (while in god mode)")]
            public bool KickAfterAttacking { get; set; } = true;

            [JsonProperty("Number of attacks admin can try before being kicked.")]
            public int AttackCount { get; set; } = 15;
        }

        private void SaveConfig()
        {
            Config.WriteObject(config, true);
        }
        
        private void Init()
        {
            permission.RegisterPermission(Perm, this);
            
            config = Config.ReadObject<Configuration>();

            Offenses.Clear();
            
            if (!config.CancelAttack) { Unsubscribe(nameof(OnPlayerAttack)); }
            else { Subscribe(nameof(OnPlayerAttack)); }
            
            if (!config.CanTargetGodmodeAdmin) { Unsubscribe(nameof(CanBeTargeted)); }
            else { Subscribe(nameof(CanBeTargeted)); }

            SaveConfig();
            
            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p.IsAdmin)
                {
                    EditAdminList(p, p.IsImmortal());
                }
            }

            // God Mode checker
            timer.Every(config.Interval, () =>
            {
                foreach (var admin in OnlineAdmins)
                {
                    GodChanged(admin, admin.IsImmortal());
                }
            });
        }
        #endregion
        
        #region Hooks

        private void OnPlayerInit(BasePlayer p)
        {
            if (p.IsAdmin) { EditAdminList(p, p.IsImmortal()); }
        }

        private void OnPlayerDisconnected(BasePlayer p, string reason)
        {
            if (p.IsAdmin) EditAdminList(p, true,false);
        }
        
        private object OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            if (!attacker.IsAdmin) {return null;}

            if (permission.UserHasPermission(attacker.UserIDString, Perm)) {return null;}
            
            BasePlayer victim = info?.HitEntity as BasePlayer;

            if (victim == null || victim.IsNpc) {return null;}

            if (attacker.IsImmortal())
            {
                OnAdminAttackInGM(attacker, victim);
                return false;
            }
            return null;
        }
        
        private object CanBeTargeted(BasePlayer p, MonoBehaviour behaviour)
        {
            if (!p.IsAdmin) {return null;}
            
            return !(p.IsImmortal());
        }
        
        #endregion
        
        #region God Mode

        private void EditAdminList(BasePlayer player, bool result, bool add = true)
        {
            if (add)
            {
                OnlineAdmins.Add(player);
                Godmode.Add(player, result);  
                
                Subscribe(nameof(OnPlayerAttack));
                Subscribe(nameof(CanBeTargeted));
            }
            else
            {
                OnlineAdmins.Remove(player);
                Godmode.Remove(player);
            }

            if (OnlineAdmins.Count == 0)
            {
                Unsubscribe(nameof(OnPlayerAttack));
                Unsubscribe(nameof(CanBeTargeted));
            }
        }
        
        private void GodChanged(BasePlayer player, bool result)
        {
            if (Godmode[player] != result)
            {
                PrintWarning($"{player.displayName}/{player.userID} has changed their god mode status to {result} within the last {config.Interval} seconds!");
                
                OnGodmodeRecentlyToggled(player, player.IsImmortal());
                
                Godmode[player] = result;
            }
        }
        #endregion

        #region Methods
        private void OnGodmodeRecentlyToggled(BasePlayer player, bool newState)
        {
            SendDiscordMessage($"`[Admin Fix]: {player.displayName}/{player.UserIDString} toggled god mode to {newState} in the last {config.Interval} seconds`");
        }

        private void OnAdminAttackInGM(BasePlayer player, BasePlayer victim)
        {
            if (!Offenses.ContainsKey(player))
            {
                Offenses.Add(player, 0);
            }
            
            Offenses[player]++;
            
            if (Offenses[player] >= config.AttacksBefore && Offenses[player] % 5 == 0)
            {
                SendDiscordMessage($"```[Admin Fix]: {player.displayName}/{player.UserIDString} tried to attack {victim.displayName}/{victim.UserIDString} while in god mode. \n" +
                                   $"\nThey have done this {Offenses[player]} times. \n" +
                                   $"\nVictim Position: {victim.transform.position}```");
            }
            else
            {
                player.ChatMessage("[<color=red>Warning</color>]: You are PvPing while in God Mode!");
            }

            if (Offenses[player] >= config.AttackCount && config.KickAfterAttacking)
            {
                player.Kick("Stop attacking players while in God Mode!!");
                Offenses[player] = 0;
            }
        }
        #endregion
        
        #region Discord

        private void SendDiscordMessage(string msg)
        {
            if (string.IsNullOrEmpty(config.WebhookUrl))
            {
                PrintError("Warning: There is no webhook url in the config!");
                return;
            }
            webrequest.Enqueue(config.WebhookUrl, new DiscordMessage(msg).ToJson(), (code, response) =>
            {
                if (code != 204)
                {
                    Puts(code.ToString());
                }
            }, this, RequestMethod.POST);
        }

        private class DiscordMessage
        {
            public DiscordMessage(string content)
            {
                Content = content;
            }

            [JsonProperty("content")] public string Content { get; set; }

            public string ToJson() => JsonConvert.SerializeObject(this);
        }
        
        #endregion
        
        void OnServerShutdown() => SaveConfig();
        
    }
}