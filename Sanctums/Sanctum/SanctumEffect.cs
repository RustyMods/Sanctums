using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Sanctums.Sanctum;

public class SanctumData
{
    public string name = null!;
    public string m_displayName = "";
    public Sprite? m_icon;
    public string m_startMessage = "";
    public string m_endMessage = "";
    public string m_tooltip = "";
    public Color m_color;
    public string m_text = "";
    public Heightmap.Biome m_biome = Heightmap.Biome.All;
    public float m_weight = 1f;
    public readonly List<HitData.DamageModPair> m_resistances = new();
    public Dictionary<EffectType, float> m_modifiers = new()
    {
        [EffectType.Speed] = 1f,
        [EffectType.SkillRaise] = 1f,
        [EffectType.Damage] = 1f,
        [EffectType.Loot] = 1f,
        [EffectType.Vitality] = 0f,
        [EffectType.CarryWeight] = 0f,
        [EffectType.Stamina] = 0f,
        [EffectType.Eitr] = 0f,
        [EffectType.HealthRegen] = 1f,
        [EffectType.StaminaRegen] = 1f,
        [EffectType.EitrRegen] = 1f,
        [EffectType.DamageReduction] = 1f ,
    };

    public readonly Dictionary<Skills.SkillType, float> m_skillLevels = new();

    public void Init()
    {
        if (name.IsNullOrWhiteSpace()) return;
        if (!ObjectDB.instance) return;

        SanctumEffect effect = ScriptableObject.CreateInstance<SanctumEffect>();
        effect.name = name;
        effect.m_name = m_displayName;
        effect.m_icon = m_icon;
        effect.m_startMessage = m_startMessage;
        effect.m_stopMessage = m_endMessage;
        effect.m_tooltip = m_tooltip;
        effect.data = this;
        StatusEffect? match = ObjectDB.instance.m_StatusEffects.Find(x => x.name == name);
        if (match != null) ObjectDB.instance.m_StatusEffects.Remove(match);
        if (!ObjectDB.instance.m_StatusEffects.Contains(effect)) ObjectDB.instance.m_StatusEffects.Add(effect);
        SanctumManager.m_sanctumEffects[name] = effect;
        if (m_biome is Heightmap.Biome.All)
        {
            foreach (Heightmap.Biome biome in Enum.GetValues(typeof(Heightmap.Biome)))
            {
                if (SanctumManager.m_biomeSanctumEffects[biome].Contains(effect)) continue;
                SanctumManager.m_biomeSanctumEffects[biome].Add(effect);
            }
        }
        else
        {
            if (SanctumManager.m_biomeSanctumEffects[m_biome].Contains(effect)) return;
            SanctumManager.m_biomeSanctumEffects[m_biome].Add(effect);
        }
    }
}

public enum EffectType
{
    Speed,
    SkillRaise,
    Damage,
    Vitality,
    Stamina,
    Eitr,
    CarryWeight,
    Loot,
    HealthRegen,
    StaminaRegen,
    EitrRegen,
    DamageReduction
}

public class SanctumEffect : StatusEffect
{
    public SanctumData data = null!;

    public override void Setup(Character character)
    {
        var fx_Fader_Spin = ZNetScene.instance.GetPrefab("fx_Fader_Spin");
        var fx_Fader_CorpseExplosion = ZNetScene.instance.GetPrefab("fx_Fader_CorpseExplosion");
        var sfx_dverger_heal_finish = ZNetScene.instance.GetPrefab("sfx_dverger_heal_finish");
        if (fx_Fader_Spin && fx_Fader_CorpseExplosion && sfx_dverger_heal_finish)
        {
            m_stopEffects = new EffectList()
            {
                m_effectPrefabs = new[]
                {
                    new EffectList.EffectData()
                    {
                        m_prefab = fx_Fader_Spin
                    },
                    new EffectList.EffectData()
                    {
                        m_prefab = fx_Fader_CorpseExplosion
                    },
                    new EffectList.EffectData()
                    {
                        m_prefab = sfx_dverger_heal_finish
                    }
                }
            };
        }
        
        base.Setup(character);
    }

    public void Reload()
    {
        if (!SanctumManager.m_sanctumEffects.TryGetValue(name, out SanctumEffect sanctumEffect)) return;
        data = sanctumEffect.data;
        m_tooltip = sanctumEffect.m_tooltip;
        m_icon = sanctumEffect.m_icon;
        m_startMessage = sanctumEffect.m_startMessage;
        m_stopMessage = sanctumEffect.m_stopMessage;
        m_name = sanctumEffect.m_name;
    }

    public override void ModifySkillLevel(Skills.SkillType skill, ref float level)
    {
        if (data.m_skillLevels.TryGetValue(Skills.SkillType.All, out float allAmount))
        {
            level += allAmount;
        }
        if (data.m_skillLevels.TryGetValue(skill, out float amount)) level += amount;
    }

    public override void ModifySpeed(float baseSpeed, ref float speed, Character character, Vector3 dir)
    {
        speed *= data.m_modifiers[EffectType.Speed];
    }

    public override void ModifyRaiseSkill(Skills.SkillType skill, ref float value)
    {
        value *= data.m_modifiers[EffectType.SkillRaise];
    }

    public override void OnDamaged(HitData hit, Character attacker)
    {
        hit.ApplyModifier(data.m_modifiers[EffectType.DamageReduction]);
    }

    public override void ModifyAttack(Skills.SkillType skill, ref HitData hitData)
    {
        hitData.ApplyModifier(data.m_modifiers[EffectType.Damage]);
    }

    public override void ModifyMaxCarryWeight(float baseLimit, ref float limit)
    {
        limit += data.m_modifiers[EffectType.CarryWeight];
    }

    public override void ModifyDamageMods(ref HitData.DamageModifiers modifiers)
    {
        modifiers.Apply(data.m_resistances);
    }

    public override void ModifyEitrRegen(ref float eitrRegen)
    {
        eitrRegen *= data.m_modifiers[EffectType.EitrRegen];
    }

    public override void ModifyStaminaRegen(ref float staminaRegen)
    {
        staminaRegen *= data.m_modifiers[EffectType.StaminaRegen];
    }

    public override void ModifyHealthRegen(ref float regenMultiplier)
    {
        regenMultiplier *= data.m_modifiers[EffectType.HealthRegen];
    }

    public override string GetTooltipString()
    {
        StringBuilder stringBuilder = new StringBuilder();
        if (!m_tooltip.IsNullOrWhiteSpace()) stringBuilder.Append(m_tooltip + "\n");
        if (Math.Abs(data.m_modifiers[EffectType.Speed] - 1f) > 0.01f)
        {
            stringBuilder.AppendFormat("$item_movement_modifier: <color=orange>{0:+0;-0}%</color>\n",
                data.m_modifiers[EffectType.Speed] * 100f - 100);
        }

        if (Math.Abs(data.m_modifiers[EffectType.SkillRaise] - 1f) > 0.01f)
        {
            stringBuilder.AppendFormat("$se_skillraise: <color=orange>{0:+0;-0}%</color>\n",
                data.m_modifiers[EffectType.SkillRaise] * 100f - 100);
        }

        if (Math.Abs(data.m_modifiers[EffectType.Damage] - 1f) > 0.01f)
        {
            stringBuilder.AppendFormat("$se_damageincrease: <color=orange>{0:+0;-0}%</color>\n",
                data.m_modifiers[EffectType.Damage] * 100f - 100);
        }

        if (Math.Abs(data.m_modifiers[EffectType.DamageReduction] - 1f) > 0.01f)
        {
            stringBuilder.AppendFormat("$se_damagereduction: <color=orange>{0:+0;-0}%</color>\n",
                data.m_modifiers[EffectType.DamageReduction] * 100f - 100);
        }

        if (data.m_modifiers[EffectType.CarryWeight] != 0f)
        {
            stringBuilder.AppendFormat("$se_max_carryweight: <color=orange>{0:+0;-0}</color>\n",
                data.m_modifiers[EffectType.CarryWeight]);
        }

        if (Math.Abs(data.m_modifiers[EffectType.EitrRegen] - 1f) > 0.01f)
        {
            stringBuilder.AppendFormat("$se_eitrregen: <color=orange>{0:+0;-0}%</color>\n",
                data.m_modifiers[EffectType.EitrRegen] * 100f - 100);
        }

        if (Math.Abs(data.m_modifiers[EffectType.HealthRegen] - 1f) > 0.01f)
        {
            stringBuilder.AppendFormat("$se_healthregen: <color=orange>{0:+0;-0}%</color>\n",
                data.m_modifiers[EffectType.HealthRegen] * 100f - 100);
        }

        if (Math.Abs(data.m_modifiers[EffectType.StaminaRegen] - 1f) > 0.01f)
        {
            stringBuilder.AppendFormat("$se_staminaregen: <color=orange>{0:+0;-0}%</color>\n",
                data.m_modifiers[EffectType.StaminaRegen] * 100f - 100);
        }

        if (data.m_modifiers[EffectType.Vitality] != 0f)
        {
            stringBuilder.AppendFormat("$se_health: <color=orange>{0:+0;-0}</color>\n",
                data.m_modifiers[EffectType.Vitality]);
        }

        if (data.m_modifiers[EffectType.Stamina] != 0f)
        {
            stringBuilder.AppendFormat("$se_stamina: <color=orange>{0:+0;-0}</color>\n",
                data.m_modifiers[EffectType.Stamina]);
        }

        if (data.m_modifiers[EffectType.Eitr] != 0f)
        {
            stringBuilder.AppendFormat("$se_eitr: <color=orange>{0:+0;-0}</color>\n", 
                data.m_modifiers[EffectType.Eitr]);
        }

        if (Math.Abs(data.m_modifiers[EffectType.Loot] - 1f) > 0.01f)
        {
            stringBuilder.AppendFormat("$se_loot: <color=orange>{0:+0;-0}%</color>\n",
                data.m_modifiers[EffectType.Loot] * 100f - 100);
        }

        foreach (KeyValuePair<Skills.SkillType, float> kvp in data.m_skillLevels)
        {
            if (kvp.Value == 0f) continue;
            stringBuilder.AppendFormat("{0}: <color=orange>{1:+0;-0}</color>\n", "$skill_" + kvp.Key.ToString().ToLower(), kvp.Value);
        }
        
        if (data.m_resistances.Count > 0)
        {
            stringBuilder.Append(SE_Stats.GetDamageModifiersTooltipString(data.m_resistances));
            stringBuilder.Append("\n");
        }

        return Localization.instance.Localize(stringBuilder.ToString());
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetMaxEitr))]
    private static class Player_SetMaxEitr_Patch
    {
        private static void Prefix(Player __instance, ref float eitr)
        {
            if (__instance != Player.m_localPlayer) return;
            foreach (var effect in __instance.GetSEMan().GetStatusEffects())
            {
                if (effect is not SanctumEffect sanctumEffect) continue;
                eitr += sanctumEffect.data.m_modifiers[EffectType.Eitr];
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetMaxStamina))]
    private static class Player_SetMaxStamina_Patch
    {
        private static void Prefix(Player __instance, ref float stamina)
        {
            if (__instance != Player.m_localPlayer) return;
            foreach (var effect in __instance.GetSEMan().GetStatusEffects())
            {
                if (effect is not SanctumEffect sanctumEffect) continue;
                stamina += sanctumEffect.data.m_modifiers[EffectType.Stamina];
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetMaxHealth))]
    private static class Player_SetMaxHealth_Patch
    {
        private static void Prefix(Player __instance, ref float health)
        {
            if (__instance != Player.m_localPlayer) return;
            foreach (var effect in __instance.GetSEMan().GetStatusEffects())
            {
                if (effect is not SanctumEffect sanctumEffect) continue;
                health += sanctumEffect.data.m_modifiers[EffectType.Vitality];
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    private static class Character_OnDeath_Patch
    {
        private static void Prefix(Character __instance)
        {
            if (!__instance) return;
            if (!Player.m_localPlayer.GetSEMan().GetStatusEffects().Any(x => x is SanctumEffect)) return;
            if (!__instance.TryGetComponent(out CharacterDrop component)) return;
            float modifier = 0f;
            foreach (var effect in Player.m_localPlayer.GetSEMan().GetStatusEffects())
            {
                if (effect is not SanctumEffect sanctumEffect) continue;
                modifier += sanctumEffect.data.m_modifiers[EffectType.Loot];
            }

            if (modifier < 1f) return;
            foreach (var drop in component.m_drops)
            {
                drop.m_amountMax += (int)modifier;
                drop.m_amountMin += (int)modifier;
            }
        }
    }
}