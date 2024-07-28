using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace Sanctums.Sanctum;

public static class UponDeath
{
    [HarmonyPatch(typeof(Character), nameof(Character.SetHealth))]
    private static class Player_SetHealth_Patch
    {
        private static void Prefix(Character __instance, ref float health)
        {
            if (!__instance) return;
            if (__instance != Player.m_localPlayer) return;
            if (__instance is not Player player) return;
            if (health > 0.0) return;
            if (!player.GetSEMan().GetStatusEffects().Any(x => x is SanctumEffect)) return;
            if (SanctumsPlugin.PreventDeath())
            {
                float quarter = __instance.GetMaxHealth() / 4f;
                health = quarter;
                player.GetSEMan().AddStatusEffect("CorpseRun".GetStableHashCode());
            }
            SanctumManager.StopSanctumEffect();
            if (!Player.m_localPlayer.m_customData.TryGetValue(Behaviors.Sanctum.m_crumblePlayerKey, out string ID)) return;
            if (!uint.TryParse(ID, out uint uID)) return;
            MakeSanctumCrumble(uID);
        }
    }

    private static void MakeSanctumCrumble(uint ID)
    {
        foreach (Behaviors.Sanctum instance in Behaviors.Sanctum.m_instances)
        {
            if (!instance.m_nview.IsValid()) continue;
            if (instance.m_nview.GetZDO().m_uid.ID != ID) continue;
            instance.MakeCrumble();
        }
        
        if (ZDOMan.instance == null) return;
        List<ZDO> tempZDO = new();
        int index = 0;
        while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative("KingSanctum", tempZDO, ref index)) {}
        foreach (var zdo in tempZDO)
        {
            if (zdo.m_uid.ID != ID) continue;
            zdo.m_prefab = "KingSanctum_Crumbled".GetStableHashCode();
        }
    }
}