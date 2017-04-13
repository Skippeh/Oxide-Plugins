using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Wounded Screams", "Skipcast", "1.0.4", ResourceId = 2416)]
    [Description("Restores the screams when a player gets wounded.")]
    public class WoundedScreams : RustPlugin
    {
        private class EffectRepeater : MonoBehaviour
        {
            public float MinDelay;
            public float MaxDelay;
            public string EffectName;

            private float nextPlay = Time.time;

            private void Update()
            {
                if (Time.time >= nextPlay)
                {
                    nextPlay = Time.time + GetRandomDelay();
                    Effect.server.Run(EffectName, transform.position);
                }
            }

            private float GetRandomDelay()
            {
                return Random.Range(MinDelay, MaxDelay);
            }
        }

        private readonly Dictionary<BaseEntity, List<EffectRepeater>> effectRepeaters = new Dictionary<BaseEntity, List<EffectRepeater>>();

        void Unload()
        {
            foreach (var kv in effectRepeaters)
            {
                RemoveEffectRepeaters(kv.Key);
            }
        }

        private void AddEffectRepeater(BaseEntity entity, EffectRepeater repeater)
        {
            var list = GetEffectRepeaters(entity) ?? (effectRepeaters[entity] = new List<EffectRepeater>());
            list.Add(repeater);
        }
        
        private List<EffectRepeater> GetEffectRepeaters(BaseEntity entity)
        {
            if (effectRepeaters.ContainsKey(entity))
            {
                return effectRepeaters[entity] ?? (effectRepeaters[entity] = new List<EffectRepeater>());
            }

            return null;
        } 

        private void RemoveEffectRepeaters(BaseEntity entity)
        {
            var effects = GetEffectRepeaters(entity);

            if (effects == null)
                return;

            foreach (var effectRepeater in effects.ToList())
            {
                RemoveEffectRepeater(effectRepeater);
            }

            effectRepeaters.Remove(entity);
        }

        private void RemoveEffectRepeater(EffectRepeater effectRepeater)
        {
            GameObject.Destroy(effectRepeater);
        }

        #region Rust hooks

        void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            RemoveEffectRepeaters(entity);
        }

        void OnPlayerWound(BasePlayer player)
        {
            if (!player || !player.gameObject || player.IsDestroyed)
                return;

            var repeater = player.gameObject.AddComponent<EffectRepeater>();
            repeater.EffectName = "assets/bundled/prefabs/fx/player/beartrap_scream.prefab";
            repeater.MinDelay = 6;
            repeater.MaxDelay = 7;
            AddEffectRepeater(player, repeater);
        }

        void OnPlayerRecover(BasePlayer player)
        {
            RemoveEffectRepeaters(player);
        }

        void OnEntityKill(BaseNetworkable entity)
        {
            var baseEntity = entity as BaseEntity;

            if (baseEntity == null)
                return;

            RemoveEffectRepeaters(baseEntity);
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            RemoveEffectRepeaters(player);
        }

        #endregion
    }
}