using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using YamlDotNet.Serialization;

namespace Sanctums.Sanctum;

public static class SanctumManager
{
    private static readonly CustomSyncedValue<string> ServerData = new CustomSyncedValue<string>(SanctumsPlugin.ConfigSync, "SanctumServerData", "");
    public static readonly string m_folderPath = Paths.ConfigPath + Path.DirectorySeparatorChar + "SanctumConfigs";
    private static readonly string m_dataPath = m_folderPath + Path.DirectorySeparatorChar + "Data";
    private static Dictionary<string, Data> m_data = new();
    public static readonly Dictionary<string, SanctumEffect> m_sanctumEffects = new();
    public static readonly Dictionary<Heightmap.Biome, List<SanctumEffect>> m_biomeSanctumEffects = new()
    {
        [Heightmap.Biome.All] = new(),
        [Heightmap.Biome.None] = new(),
        [Heightmap.Biome.Meadows] = new(),
        [Heightmap.Biome.BlackForest] = new(),
        [Heightmap.Biome.Swamp] = new(),
        [Heightmap.Biome.Mountain] = new(),
        [Heightmap.Biome.Plains] = new(),
        [Heightmap.Biome.Mistlands] = new(),
        [Heightmap.Biome.AshLands] = new(),
        [Heightmap.Biome.DeepNorth] = new(),
        [Heightmap.Biome.Ocean] = new()
    };
    // private static readonly string m_customPlayerDataKey = "SanctumEffectCustomDataKey";
    private static readonly List<string> m_prefabsToSearch = new();
    private static readonly List<ZDO> m_tempZDOs = new();
    private static bool m_praying;
    private static float m_prayTimer;
    private static bool m_serverWatcherLoaded;
    private static bool m_firstInit = true;
    
    public static void AddPrefabToSearch(string prefabName)
    {
        if (m_prefabsToSearch.Contains(prefabName)) return;
        m_prefabsToSearch.Add(prefabName);
    }

    private static void InitCoroutine() => SanctumsPlugin._plugin.StartCoroutine(ForceSendSanctumZDO());
    private static IEnumerator ForceSendSanctumZDO()
    {
        for (;;)
        {
            if (!Game.instance || ZDOMan.instance == null || !ZNet.instance || !ZNet.instance.IsServer()) continue;
            m_tempZDOs.Clear();
            foreach (string prefab in m_prefabsToSearch)
            {
                int index = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, m_tempZDOs, ref index))
                {
                    yield return null;
                }
            }

            foreach (ZDO zdo in m_tempZDOs)
            {
                ZDOMan.instance.ForceSendZDO(zdo.m_uid);
            }

            yield return new WaitForSeconds(10f);
        }
    }
    
    public static void LoadSanctumData()
    {
        if (!Directory.Exists(m_folderPath)) Directory.CreateDirectory(m_folderPath);
        if (!Directory.Exists(m_dataPath)) Directory.CreateDirectory(m_dataPath);
        string[] filePaths = Directory.GetFiles(m_dataPath, "*.yml");
        int count = 0;
        foreach (string path in filePaths)
        {
            try
            {
                if (ReadFile(path)) ++count;
            }
            catch
            {
                SanctumsPlugin.SanctumsLogger.LogWarning("Failed to parse file:");
                SanctumsPlugin.SanctumsLogger.LogWarning(path);
            }
        }
        SanctumsPlugin.SanctumsLogger.LogDebug($"Loaded {count} sanctum files");
    }

    public static void LoadFileWatcher()
    {
        FileSystemWatcher watcher = new FileSystemWatcher(m_dataPath, "*.yml");
        watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
        watcher.EnableRaisingEvents = true;
        watcher.Created += OnFileChange;
        watcher.Deleted += OnFileChange;
        watcher.Changed += OnFileChange;
    }

    private static void OnFileChange(object sender, FileSystemEventArgs args)
    {
        if (!ObjectDB.instance || !ZNet.instance || !ZNet.instance.IsServer()) return;
        ObjectDB.instance.m_StatusEffects.RemoveAll(x => x is SanctumEffect);
        m_sanctumEffects.Clear();
        foreach (var kvp in m_biomeSanctumEffects) kvp.Value.Clear();
        m_data.Clear();
        foreach (var filePath in Directory.GetFiles(m_dataPath))
        {
            ReadFile(filePath, true);
        }
        LoadServerData();
        if (!Player.m_localPlayer) return;
        foreach (var effect in Player.m_localPlayer.GetSEMan().GetStatusEffects())
        {
            if (effect is not SanctumEffect sanctumEffect) continue;
            sanctumEffect.Reload();
        }
    }

    private static bool ReadFile(string filePath, bool reload = false)
    {
        var deserializer = new DeserializerBuilder().Build();

        try
        {
            var file = File.ReadAllText(filePath);
            if (file.IsNullOrWhiteSpace()) return false;
            var data = deserializer.Deserialize<Data>(file);
            m_data[data.Name] = data;
            try
            {
                if (reload)
                {
                    if (ZNet.instance && ZNet.instance.IsServer())
                    {
                        GetSanctumData(data).Init();
                    }
                }
            }
            catch
            {
                //
            }
        }
        catch
        {
            SanctumsPlugin.SanctumsLogger.LogDebug("Failed to parse file");
            return false;
        }

        return true;
    }

    private static void LoadServerData()
    {
        if (!ZNet.instance || !ZNet.instance.IsServer()) return;
        ISerializer serializer = new SerializerBuilder().Build();
        string data = serializer.Serialize(m_data);
        ServerData.Value = data;
    }

    public static void LoadServerDataWatcher()
    {
        if (m_serverWatcherLoaded) return;
        ServerData.ValueChanged += () =>
        {
            if (!ZNet.instance || ZNet.instance.IsServer()) return;
            if (ServerData.Value.IsNullOrWhiteSpace()) return;
            try
            {
                SanctumsPlugin.SanctumsLogger.LogDebug("Received server sanctum data, loading");
                IDeserializer deserializer = new DeserializerBuilder().Build();
                Dictionary<string, Data> data = deserializer.Deserialize<Dictionary<string, Data>>(ServerData.Value);
                m_data = data;
                try
                {
                    if (Player.m_localPlayer && ObjectDB.instance && ZNetScene.instance)
                    {
                        m_sanctumEffects.Clear();
                        foreach (var kvp in m_biomeSanctumEffects) kvp.Value.Clear();
                        foreach (var info in m_data)
                        {
                            GetSanctumData(info.Value).Init();
                        }
                    }

                    foreach (StatusEffect? effect in Player.m_localPlayer.GetSEMan().GetStatusEffects())
                    {
                        if (effect is not SanctumEffect sanctumEffect) continue;
                        sanctumEffect.Reload();
                    }
                }
                catch
                {
                    // ignored
                }
            }
            catch
            {
                SanctumsPlugin.SanctumsLogger.LogDebug("Failed to parse server data");
            }
        };
        m_serverWatcherLoaded = true;
    }
    
    public static void PrayAnimation()
    {
        if (!Player.m_localPlayer) return;
        Player.m_localPlayer.m_zanim.SetTrigger("Praying");
        m_praying = true;
    }

    private static void UpdatePray(float dt)
    {
        if (!m_praying) return;
        m_prayTimer += dt;
        if (m_prayTimer < 60f) return;
        m_prayTimer = 0.0f;
        Player.m_localPlayer.m_zanim.SetTrigger("idle");
        m_praying = false;
    }
    

    [HarmonyPatch(typeof(Player), nameof(Player.FixedUpdate))]
    private static class Player_Pray_Update
    {
        private static void Postfix(Player __instance)
        {
            if (!__instance) return;
            UpdatePray(Time.fixedDeltaTime);
        }
    }

    // public static StatusEffect? GetSavedSanctumEffect()
    // {
    //     if (!Player.m_localPlayer || !ObjectDB.instance) return null;
    //     if (!Player.m_localPlayer.m_customData.TryGetValue(m_customPlayerDataKey, out string data)) return null;
    //     return ObjectDB.instance.GetStatusEffect(data.GetHashCode());
    // }

    // public static void SaveSanctumEffect(StatusEffect effect)
    // {
    //     if (!Player.m_localPlayer) return;
    //     Player.m_localPlayer.m_customData[m_customPlayerDataKey] = effect.name;
    // }

    public static void StopSanctumEffect()
    {
        // if (!Player.m_localPlayer) return;
        // if (!Player.m_localPlayer.m_customData.ContainsKey(m_customPlayerDataKey)) return;
        // Player.m_localPlayer.m_customData.Remove(m_customPlayerDataKey);
        foreach (var effect in Player.m_localPlayer.GetSEMan().GetStatusEffects())
        {
            if (effect is not SanctumEffect) continue;
            effect.Stop();
        }
        Player.m_localPlayer.GetSEMan().GetStatusEffects().RemoveAll(x => x is SanctumEffect);
    }

    private static SanctumData GetSanctumData(Data data)
    {
        SanctumData effectData = new SanctumData()
        {
            name = $"SE_{data.Name}",
            m_displayName = data.Name,
            m_startMessage = data.StartMessage,
            m_endMessage = data.StopMessage,
            m_tooltip = data.Tooltip,
            m_weight = data.Weight,
            m_text = data.Text,
            m_color = new UnityEngine.Color(data.Color.red, data.Color.green, data.Color.blue, 2f),
            m_modifiers = new Dictionary<EffectType, float>()
            {
                [EffectType.Speed] = data.SpeedModifier,
                [EffectType.SkillRaise] = data.ExperienceModifier,
                [EffectType.Damage] = data.DamageModifier,
                [EffectType.Loot] = data.LootModifier,
                [EffectType.Vitality] = data.Vitality,
                [EffectType.Stamina] = data.Stamina,
                [EffectType.Eitr] = data.Eitr,
                [EffectType.HealthRegen] = data.HealthRegen,
                [EffectType.StaminaRegen] = data.StaminaRegen,
                [EffectType.EitrRegen] = data.EitrRegen,
                [EffectType.CarryWeight] = data.CarryWeight,
                [EffectType.DamageReduction] = data.DamageReduction
            },
        };
        foreach (var kvp in data.Skills)
        {
            if (kvp.Value == 0f) continue;
            if (Enum.TryParse(kvp.Key, true, out Skills.SkillType skillType))
            {
                effectData.m_skillLevels[skillType] = kvp.Value;
            }
        }
        if (Enum.TryParse(data.Biome, true, out Heightmap.Biome biome))
        {
            effectData.m_biome = biome;
        }
        if (!data.IconFileName.IsNullOrWhiteSpace())
        {
            if (SpriteManager.m_customIcons.TryGetValue(data.IconFileName, out Sprite? icon))
            {
                effectData.m_icon = icon;
            }
        }

        if (Enum.TryParse(data.BluntResistance, true, out HitData.DamageModifier blunt))
        {
            effectData.m_resistances.Add(new HitData.DamageModPair()
            {
                m_type = HitData.DamageType.Blunt, m_modifier = blunt
            });
        }
        if (Enum.TryParse(data.SlashResistance, true, out HitData.DamageModifier slash))
        {
            effectData.m_resistances.Add(new HitData.DamageModPair()
            {
                m_type = HitData.DamageType.Slash, m_modifier = slash
            });
        }
        if (Enum.TryParse(data.PierceResistance, true, out HitData.DamageModifier pierce))
        {
            effectData.m_resistances.Add(new HitData.DamageModPair()
            {
                m_type = HitData.DamageType.Pierce, m_modifier = pierce
            });
        }
        if (Enum.TryParse(data.FireResistance, true, out HitData.DamageModifier fire))
        {
            effectData.m_resistances.Add(new HitData.DamageModPair()
            {
                m_type = HitData.DamageType.Fire, m_modifier = fire
            });
        }
        if (Enum.TryParse(data.FrostResistance, true, out HitData.DamageModifier frost))
        {
            effectData.m_resistances.Add(new HitData.DamageModPair()
            {
                m_type = HitData.DamageType.Frost, m_modifier = frost
            });
        }
        if (Enum.TryParse(data.LightningResistance, true, out HitData.DamageModifier lightning))
        {
            effectData.m_resistances.Add(new HitData.DamageModPair()
            {
                m_type = HitData.DamageType.Lightning, m_modifier = lightning
            });
        }
        if (Enum.TryParse(data.PoisonResistance, true, out HitData.DamageModifier poison))
        {
            effectData.m_resistances.Add(new HitData.DamageModPair()
            {
                m_type = HitData.DamageType.Poison, m_modifier = poison
            });
        }
        if (Enum.TryParse(data.SpiritResistance, true, out HitData.DamageModifier spirit))
        {
            effectData.m_resistances.Add(new HitData.DamageModPair()
            {
                m_type = HitData.DamageType.Spirit, m_modifier = spirit
            });
        }

        return effectData;
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    private static class ObjectDB_Awake_Patch
    {
        private static void Postfix(ObjectDB __instance)
        {
            if (!__instance || !ZNetScene.instance) return;
            foreach (Data data in m_data.Values) GetSanctumData(data).Init();
        }
    }

    private static void InitStatusEffects()
    {
        if (!m_firstInit) return;
        ObjectDB.instance.m_StatusEffects.RemoveAll(x => x is SanctumEffect);
        m_sanctumEffects.Clear();
        foreach (var kvp in m_biomeSanctumEffects) kvp.Value.Clear();
        foreach (Data data in m_data.Values) GetSanctumData(data).Init();
        foreach (var instance in Behaviors.Sanctum.m_instances) instance.GetSanctumEffect();
        m_firstInit = false;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    private static class Player_On_Spawned_Patch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
    
            // ReloadSanctumEffect();
            if (ZNet.instance && ZNet.instance.IsServer()) return;
            
            InitStatusEffects();
        }
    }
    
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
    private static class ZNet_Awake_Patch
    {
        private static void Postfix(ZNet __instance)
        {
            if (!__instance || !__instance.IsServer()) return;
            InitCoroutine();
            LoadServerData();
        }
    }

    [Serializable]
    public class Data
    {
        public string Name = "";
        public string Biome = "All";
        public float Weight = 1f;
        public Color Color = new Color();
        public string Text = "";
        public string StartMessage = "";
        public string StopMessage = "";
        public string IconFileName = "";
        public string Tooltip = "";
        public float SpeedModifier = 1f;
        public float ExperienceModifier = 1f;
        public float DamageModifier = 1f;
        public float LootModifier = 1f;
        public float HealthRegen = 1f;
        public float StaminaRegen = 1f;
        public float EitrRegen = 1f;
        public float DamageReduction = 1f;
        public float Vitality = 0f;
        public float Stamina = 0f;
        public float Eitr = 0f;
        public float CarryWeight = 0f;
        public string BluntResistance = "";
        public string SlashResistance = "";
        public string PierceResistance = "";
        public string FireResistance = "";
        public string FrostResistance = "";
        public string LightningResistance = "";
        public string PoisonResistance = "";
        public string SpiritResistance = "";
        public Dictionary<string, float> Skills = new();
    }

    [Serializable]
    public class Color
    {
        public float red;
        public float green;
        public float blue;
    }
}