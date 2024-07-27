using System.Collections.Generic;
using System.Linq;
using BepInEx;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using YamlDotNet.Serialization;

namespace Sanctums.Sanctum;

public static class Commands
{
    private static readonly CustomSyncedValue<string> m_sanctumLocations = new CustomSyncedValue<string>(SanctumsPlugin.ConfigSync, "CustomSyncedSanctumLocations", "");
    
    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.GenerateLocationsIfNeeded))]
    private static class RegisterGeneratedSanctums
    {
        private static void Postfix() => UpdateServerLocationData();
    }
    
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.Awake))]
    private static class Terminal_Awake_Patch
    {
        private static void Postfix()
        {
            Terminal.ConsoleCommand commands = new("sanctum", "use help to list out command options",
                (Terminal.ConsoleEventFailable)(
                    args =>
                    {
                        if (args.Length < 2) return false;

                        switch (args[1])
                        {
                            case "help":
                                foreach (string info in new List<string>()
                                         {
                                             "clear: removes sanctum effect from player without removing all other effects",
                                             "reveal: reveals all sanctum locations on the map",
                                             "pray: starts the pray animation",
                                             "remove_pins: removes revealed pins from map"
                                         })
                                {
                                    SanctumsPlugin.SanctumsLogger.LogInfo(info);
                                }
                                break;
                            case "clear":
                                if (!Player.m_localPlayer) return false;
                                SanctumManager.StopSanctumEffect();
                                break;
                            case "reveal":
                                RevealAllSanctums(args);
                                break;
                            case "pray":
                                SanctumManager.PrayAnimation();
                                break;
                            case "remove_pins":
                                if (!Minimap.instance) break;
                                foreach (var pin in args.Context.m_findPins)
                                {
                                    Minimap.instance.RemovePin(pin);
                                }
                                args.Context.m_findPins.Clear();
                                break;
                        }
                        return true;
                    }), optionsFetcher: ()=>new(){"help", "clear", "reveal", "pray", "remove_pins"});
        }
    }
    private static void RevealAllSanctums(Terminal.ConsoleEventArgs args)
    {
        if (!Player.m_localPlayer) return;
        if (!Terminal.m_cheat)
        {
            Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Only admin can use this command");
            return;
        }

        int count = 0;
        if (ZNet.instance.IsServer())
        {
            List<ZoneSystem.LocationInstance> sanctums = ZoneSystem.instance.GetLocationList().Where(location => location.m_location.m_prefab.Name.ToLower().Contains("sanctum")).ToList();

            foreach (Minimap.PinData pin in args.Context.m_findPins) Minimap.instance.RemovePin(pin);
            args.Context.m_findPins.Clear();
            foreach (ZoneSystem.LocationInstance sanctum in sanctums)
            {
                args.Context.m_findPins.Add(Minimap.instance.AddPin(sanctum.m_position, Minimap.PinType.Icon0, "Sanctum", false, false));
                ++count;
            }
        }
        else
        {
            if (!m_sanctumLocations.Value.IsNullOrWhiteSpace())
            {
                var deserializer = new DeserializerBuilder().Build();
                var list = deserializer.Deserialize<List<string>>(m_sanctumLocations.Value);
                foreach (string? position in list)
                {
                    if (!GetVector(position, out Vector3 pos)) continue;
                    args.Context.m_findPins.Add(Minimap.instance.AddPin(pos, Minimap.PinType.Icon0, "Sanctum", false, false));
                    ++count;
                }
            }
        }
        SanctumsPlugin.SanctumsLogger.LogInfo($"Revealed {count} sanctums on map");
    }
    
    private static void UpdateServerLocationData()
    {
        if (!ZNet.instance || !ZNet.instance.IsServer()) return;
    
        List<ZoneSystem.LocationInstance> sanctums = ZoneSystem.instance.GetLocationList().Where(location => location.m_location.m_prefab.Name.ToLower().Contains("sanctum")).ToList();
        int count = 0;
        List<string> data = new List<string>();
        foreach (ZoneSystem.LocationInstance sanctum in sanctums)
        {
            data.Add(FormatPosition(sanctum.m_position));
            ++count;
        }

        var serializer = new SerializerBuilder().Build();
        m_sanctumLocations.Value = serializer.Serialize(data);
        SanctumsPlugin.SanctumsLogger.LogDebug($"Registered {count} sanctum locations on the server");
    }

    private static string FormatPosition(Vector3 position) => $"{position.x},{position.y},{position.z}";

    private static bool GetVector(string input, out Vector3 output)
    {
        output = Vector3.zero;
        string[] info = input.Split(',');
        if (info.Length != 3) return false;
        float x = float.Parse(info[0]);
        float y = float.Parse(info[1]);
        float z = float.Parse(info[2]);
        output = new Vector3(x, y, z);
        return true;
    }
}