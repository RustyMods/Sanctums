using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AlmanacClasses.LoadAssets;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using LocalizationManager;
using PieceManager;
using Sanctums.Behaviors;
using Sanctums.Managers;
using Sanctums.Sanctum;
using ServerSync;
using UnityEngine;
using CraftingTable = PieceManager.CraftingTable;

namespace Sanctums
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class SanctumsPlugin : BaseUnityPlugin
    {
        internal const string ModName = "Sanctums";
        internal const string ModVersion = "1.0.0";
        internal const string Author = "RustyMods";
        private const string ModGUID = Author + "." + ModName;
        private static readonly string ConfigFileName = ModGUID + ".cfg";
        private static readonly string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
        internal static string ConnectionError = "";
        private readonly Harmony _harmony = new(ModGUID);
        public static readonly ManualLogSource SanctumsLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
        public static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };
        public enum Toggle { On = 1, Off = 0 }

        public static SanctumsPlugin _plugin = null!;
        public static readonly AssetBundle AssetBundle = GetAssetBundle("sanctumbundle");
        public static AssetLoaderManager m_assetLoaderManager = null!;

        private static ConfigEntry<Toggle> _serverConfigLocked = null!;
        private static ConfigEntry<Toggle> c_completeChallenge = null!;
        private static ConfigEntry<Toggle> c_preventDeath = null!;
        private static ConfigEntry<Toggle> c_usePrayAnimation = null!;
        private static ConfigEntry<int> c_locationAmount = null!;
        private static ConfigEntry<Heightmap.Biome> c_biomes = null!;
        private void LoadConfigs()
        {
            _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On,
                "If on, the configuration is locked and can be changed by server admins only.");
            _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);

            c_completeChallenge = config("2 - Settings", "Complete Challenge", Toggle.On, "If on, requires to complete challenge before activating sanctum");
            c_preventDeath = config("2 - Settings", "Prevent Death", Toggle.On, "If on, if user has sanctum effect, they will survive death and lose effect, else lose effect");
            c_usePrayAnimation = config("2 - Settings", "Use Pray Animation", Toggle.On, "If on, when user interacts with sanctum, they start praying");
            c_locationAmount = config("3 - Location", "Amount", 100, "Set amount of sanctums locations to attempt to generate");
            c_biomes = config("3 - Location", "Biomes", Heightmap.Biome.All, "Set biomes sanctums can spawn in");
            
        }

        public static bool UsePrayAnimation() => c_usePrayAnimation.Value is Toggle.On;
        public static bool PreventDeath() => c_preventDeath.Value is Toggle.On;
        public static bool CompleteChallenge() => c_completeChallenge.Value is Toggle.On;
        public void Awake()
        {
            Localizer.Load();
            _plugin = this;
            m_assetLoaderManager = new AssetLoaderManager(_plugin.Info.Metadata);
            LoadConfigs();
            LoadPieces();
            LoadLocations();
            SpriteManager.LoadIcons();
            SanctumManager.LoadSanctumData();
            LoadAnimations();
            SanctumManager.LoadServerDataWatcher();
            Commands.LoadServerLocationChange();
            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
            SetupWatcher();
            SanctumManager.LoadFileWatcher();
        }

        private void LoadAnimations()
        {
            AnimationReplaceManager.AddAnimationSet(AssetBundle, "Praying");
        }

        private void LoadLocations()
        {
            LocationManager.LocationData sanctum =
                new LocationManager.LocationData("SanctumLocation", AssetBundle, "KingSanctum")                {
                    m_data =
                    {
                        m_biome = c_biomes.Value,
                        m_quantity = c_locationAmount.Value,
                        m_group = "Sanctums",
                        m_prefabName = "SanctumLocation",
                        m_prioritized = false,
                        m_minDistanceFromSimilar = 1000f,
                        m_surroundCheckVegetation = true,
                        m_surroundCheckDistance = 10f,
                    }
                };
        }

        private void LoadPieces()
        {
            BuildPiece sanctum = new BuildPiece(AssetBundle, "KingSanctum");
            sanctum.Name.English("Kings Sanctum");
            sanctum.Description.English("");
            sanctum.Crafting.Set(CraftingTable.ArtisanTable);
            sanctum.Category.Set(BuildPieceCategory.Misc);
            sanctum.RequiredItems.Add("SwordCheat", 1, false);
            sanctum.SpecialProperties = new SpecialProperties()
            {
                AdminOnly = true, NoConfig = true
            };
            sanctum.Prefab.AddComponent<Behaviors.Sanctum>();
            MaterialReplacer.RegisterGameObjectForShaderSwap(sanctum.Prefab.transform.Find("model/Fountain").gameObject, MaterialReplacer.ShaderType.PieceShader);
            sanctum.PlaceEffects = new() { "vfx_Place_workbench", "sfx_build_hammer_stone" };
            sanctum.DestroyedEffects = new() { "vfx_RockDestroyed", "sfx_rock_destroyed" };
            sanctum.HitEffects = new() { "vfx_RockHit" };
            sanctum.SwitchEffects = new() { "vfx_Place_throne02" };
            SanctumManager.AddPrefabToSearch(sanctum.Prefab.name);

            BuildPiece crumbled = new BuildPiece(AssetBundle, "KingSanctum_Crumbled");
            crumbled.Name.English("Crumbled Sanctum");
            crumbled.Description.English("");
            crumbled.Crafting.Set(CraftingTable.ArtisanTable);
            crumbled.Category.Set(BuildPieceCategory.Misc);
            crumbled.RequiredItems.Add("SwordCheat", 1, false);
            crumbled.SpecialProperties = new SpecialProperties()
            {
                AdminOnly = true, NoConfig = true
            };
            crumbled.Prefab.AddComponent<CrumbedSanctum>();
            MaterialReplacer.RegisterGameObjectForShaderSwap(crumbled.Prefab.transform.Find("broken/Fountain").gameObject, MaterialReplacer.ShaderType.PieceShader);
            crumbled.PlaceEffects = new() { "vfx_Place_workbench", "sfx_build_hammer_stone" };
            crumbled.DestroyedEffects = new() { "vfx_RockDestroyed", "sfx_rock_destroyed" };
            crumbled.HitEffects = new() { "vfx_RockHit" };
            crumbled.SwitchEffects = new() { "vfx_Place_throne02" };
        }
        
        private static AssetBundle GetAssetBundle(string fileName)
        {
            Assembly execAssembly = Assembly.GetExecutingAssembly();
            string resourceName = execAssembly.GetManifestResourceNames().Single(str => str.EndsWith(fileName));
            using Stream? stream = execAssembly.GetManifestResourceStream(resourceName);
            return AssetBundle.LoadFromStream(stream);
        }

        private void OnDestroy()
        {
            Config.Save();
        }

        private void SetupWatcher()
        {
            FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
            watcher.Changed += ReadConfigValues;
            watcher.Created += ReadConfigValues;
            watcher.Renamed += ReadConfigValues;
            watcher.IncludeSubdirectories = true;
            watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            watcher.EnableRaisingEvents = true;
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(ConfigFileFullPath)) return;
            try
            {
                SanctumsLogger.LogDebug("ReadConfigValues called");
                Config.Reload();
            }
            catch
            {
                SanctumsLogger.LogError($"There was an issue loading your {ConfigFileName}");
                SanctumsLogger.LogError("Please check your config entries for spelling and format!");
            }
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description,
            bool synchronizedSetting = true)
        {
            ConfigDescription extendedDescription =
                new(
                    description.Description +
                    (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
                    description.AcceptableValues, description.Tags);
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
            //var configEntry = Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description,
            bool synchronizedSetting = true)
        {
            return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
        }

        private class ConfigurationManagerAttributes
        {
            [UsedImplicitly] public int? Order;
            [UsedImplicitly] public bool? Browsable;
            [UsedImplicitly] public string? Category;
            [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer;
        }
    }
}