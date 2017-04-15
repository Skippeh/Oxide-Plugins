using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Wounded Screams", "Skipcast", "2.0.0", ResourceId = 2416)]
    [Description("Restores the screams when a player gets wounded.")]
    public class WoundedScreams : RustPlugin
    {
        private class PluginConfig
        {
            public bool ScreamOnDemand = false;
        }

        private const string effectName = "assets/bundled/prefabs/fx/player/beartrap_scream.prefab";

        private class Scream
        {
            public float NextPlay = Time.time;
            private float GetRandomDelay() => Random.Range(6f, 7f);

            public void ApplyDelay()
            {
                NextPlay = Time.time + GetRandomDelay();
            }
        }

        private readonly Dictionary<BasePlayer, Scream> screams = new Dictionary<BasePlayer, Scream>();

        private PluginConfig config;

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating default configuration");

            config = new PluginConfig();
            Config.Clear();
            Config.WriteObject(config);
        }

        void Init()
        {
            config = Config.ReadObject<PluginConfig>();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }

        private void AddPlayerScream(BasePlayer player)
        {
            if (screams.ContainsKey(player))
            {
                Debug.LogWarning("Trying to add more than 1 scream to player.");
                return;
            }

            var scream = new Scream();
            screams.Add(player, scream);
        }

        private void RemovePlayerScream(BasePlayer player)
        {
            if (screams.ContainsKey(player))
                screams.Remove(player);
        }

        #region Rust hooks

        void OnTick()
        {
            foreach (var kv in screams)
            {
                if (Time.time >= kv.Value.NextPlay)
                {
                    if (!kv.Key || kv.Key.IsDestroyed || !kv.Key.IsConnected || !kv.Key.IsWounded())
                    {
                        continue;
                    }

                    Vector3 position = kv.Key.GetNetworkPosition();
                    Effect.server.Run(effectName, position);
                    kv.Value.ApplyDelay();
                }
            }
        }

        void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            var player = entity as BasePlayer;

            if (player != null)
                RemovePlayerScream(player);
        }

        void OnPlayerWound(BasePlayer player)
        {
            if (!player || !player.gameObject || player.IsDestroyed)
                return;

            AddPlayerScream(player);
        }

        void OnPlayerRecover(BasePlayer player)
        {
            RemovePlayerScream(player);
        }

        void OnEntityKill(BaseNetworkable entity)
        {
            var player = entity as BasePlayer;

            if (player != null)
                RemovePlayerScream(player);
        }

        void OnPlayerInit(BasePlayer player)
        {
            if (player.IsWounded())
            {
                AddPlayerScream(player);
            }
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            RemovePlayerScream(player);
        }

        #endregion
    }
}