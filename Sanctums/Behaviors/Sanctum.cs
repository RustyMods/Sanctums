using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using Sanctums.Sanctum;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sanctums.Behaviors;

public class Sanctum : MonoBehaviour, Hoverable, Interactable
{
    private static readonly int GlowMax = Shader.PropertyToID("_glowMax");
    private static readonly int Emissive = Shader.PropertyToID("_EmissionColor");
    public static readonly string m_crumblePlayerKey = "CrumbledID";
    public readonly int m_activeKey = "SanctumActivated".GetStableHashCode();
    public readonly int m_effectKey = "SanctumEffect".GetStableHashCode();
    public readonly int m_oldUID = "SanctumUID".GetStableHashCode();
    public static readonly List<Sanctum> m_instances = new();
    public ZNetView m_nview = null!;
    public WearNTear m_wearNTear = null!;
    public readonly Dictionary<Switch, bool> m_switches = new();
    private readonly Dictionary<int, Switch> m_switchID = new();
    public EffectList? m_switchEffect;
    public EffectList? m_completeEffect;
    public EffectList? m_playerEffect;
    public Material[]? m_crystalMaterials;
    public GameObject? m_crystal;
    public List<Material> m_materials = new();
    public GameObject? m_activeEffects;
    public List<int> m_code = new();
    public List<int> m_currentCode = new();
    public float m_codeTimer;
    public bool m_coroutineRunning;
    public SanctumEffect? m_effect;
    public FloatingCrystal? m_floatingCrystal;
    public void Awake()
    {
        m_nview = GetComponent<ZNetView>();
        m_wearNTear = GetComponent<WearNTear>();
        
        m_nview.Register<float>(nameof(RPC_Crumble),RPC_Crumble);
        m_nview.Register<bool>(nameof(RPC_PingCode),RPC_PingCode);
        GetEffects();
        GetMaterials();
        SetSwitches();
        CheckOldID();
        m_activeEffects = Utils.FindChild(transform, "active_effects").gameObject;
        m_activeEffects.SetActive(IsActivated());
        SetGlow(IsActivated() ? 5f : 0f);
        GenerateCode();
        GetSanctumEffect();
        SetColor();
        AddFloatingBehavior();

        m_instances.Add(this);
    }

    private void AddFloatingBehavior()
    {
        if (m_crystal != null && m_nview.IsValid())
        {
            m_floatingCrystal = m_crystal.AddComponent<FloatingCrystal>();
        }
        if (m_floatingCrystal != null) m_floatingCrystal.Enable(IsActivated());
    }

    private void CheckOldID()
    {
        if (!m_nview.IsValid()) return;
        int oldID = m_nview.GetZDO().GetInt(m_oldUID, -1);
        if (oldID != -1 && m_nview.GetZDO().m_uid.ID != oldID)
        {
            Reset();
        }  
    }

    private void Reset()
    {
        if (!m_nview.IsValid()) return;
        m_nview.GetZDO().Set(m_activeKey, false);
        m_nview.GetZDO().Set(m_oldUID, m_nview.GetZDO().m_uid.ID);
    }

    public void Update()
    {
        float dt = Time.fixedDeltaTime;
        updateCode(dt);
    }

    public void OnDestroy()
    {
        m_instances.Remove(this);
    }

    public void MakeCrumble()
    {
        if (!m_nview.IsValid()) return;
        m_nview.InvokeRPC(nameof(RPC_Crumble), 0.24f);
        // m_wearNTear.m_healthPercentage = 0.24f;
        // if (m_activeEffects != null) m_activeEffects.SetActive(false);
        // m_nview.GetZDO().m_prefab = "KingSanctum_Crumbled".GetStableHashCode();
    }

    public void RPC_Crumble(long sender, float health)
    {
        m_wearNTear.m_healthPercentage = health;
        if (m_activeEffects != null) m_activeEffects.SetActive(false);
        m_nview.GetZDO().m_prefab = "KingSanctum_Crumbled".GetStableHashCode();
    }
    public void updateCode(float dt)
    {
        if (!SanctumsPlugin.CompleteChallenge()) return;
        if (GetActivatedSwitchesCount() == m_switches.Count) return;
        if (m_coroutineRunning) return;
        m_codeTimer += dt;
        if (m_codeTimer < 60f) return;
        m_codeTimer = 0.0f;
        ResetSwitches();
        StartCoroutine(PingCode());
    }

    private void ResetSwitches()
    {
        foreach (Switch key in m_switches.Keys.ToList())
        {
            m_switches[key] = false;
        }

        m_currentCode.Clear();
    }

    public void GetSanctumEffect()
    {
        if (!m_nview.IsValid()) return;
        string? effectName = m_nview.GetZDO().GetString(m_effectKey);

        if (!effectName.IsNullOrWhiteSpace())
        {
            if (SanctumManager.m_sanctumEffects.TryGetValue(effectName, out SanctumEffect sanctumEffect))
            {
                m_effect = sanctumEffect;
            }
        }

        if (m_effect == null)
        {
            Heightmap.Biome biome = WorldGenerator.instance.GetBiome(transform.position);
            if (SanctumManager.m_biomeSanctumEffects.TryGetValue(biome, out List<SanctumEffect> effects))
            {
                var totalWeights = effects.Sum(x => x.data.m_weight);
                float num1 = Random.Range(0.0f, totalWeights);
                float num2 = 0.0f;
                foreach (var effect in effects)
                {
                    num2 += effect.data.m_weight;
                    if (num2 >= num1)
                    {
                        m_effect = effect;
                        break;
                    }
                }

                if (m_effect == null)
                {
                    m_effect = effects[Random.Range(0, effects.Count)];
                }
            }
        }

        if (m_effect == null) return;
        SaveSanctumEffect(m_effect.name);
    }

    private void SaveSanctumEffect(string effectName)
    {
        if (!m_nview.IsValid()) return;
        m_nview.GetZDO().Set(m_effectKey, effectName);
    }

    public void RPC_PingCode(long sender, bool enable)
    {
        m_codeTimer = 0.0f;
        if (enable)
        {
            StartCoroutine(nameof(PingCode));
        }
        else
        {
            StopCoroutine(nameof(PingCode));
        }
    }
    private IEnumerator PingCode()
    {
        m_coroutineRunning = true;
        foreach (var number in m_code)
        {
            if (m_switchID.TryGetValue(number, out Switch component))
            {
                m_switchEffect?.Create(component.transform.position - new Vector3(0f, 2f, 0f), Quaternion.identity);
            }

            yield return new WaitForSeconds(2f);
        }

        m_coroutineRunning = false;
    }

    private void SetColor()
    {
        if (m_crystalMaterials == null) return;
        if (m_effect == null) return;
        foreach (Material material in m_crystalMaterials)
        {
            material.color = m_effect.data.m_color;
        }

        foreach (Material material in m_materials)
        {
            material.SetColor(Emissive, m_effect.data.m_color);
        }
    }

    private void GenerateCode()
    {
        List<int> switchIndices = new List<int>();
        for (int i = 0; i < m_switches.Count; i++)
        {
            switchIndices.Add(i);
        }

        List<int> result = new();
        System.Random random = new System.Random();
        for (int index = 0; index < m_switches.Count; ++index)
        {
            int randomIndex = random.Next(0, switchIndices.Count);
            int number = switchIndices[randomIndex];
            switchIndices.RemoveAt(randomIndex);
            result.Add(number);
        } 
        m_code = result;
    }

    private void SetSwitches()
    {
        foreach (Switch component in GetComponentsInChildren<Switch>())
        {
            m_switches[component] = false;
            if (int.TryParse(component.m_name, out int index))
            {
                m_switchID[index] = component;
            }
        }
        
        foreach (Switch component in m_switches.Keys)
        {
            component.m_onHover += OnSwitchHover;
            component.m_onUse += OnSwitchRune;
        }
    }

    private void SetGlow(float amount)
    {
        if (m_crystalMaterials == null) return;
        foreach (var material in m_crystalMaterials)
        {
            if (material.HasProperty(GlowMax))
            {
                material.SetFloat(GlowMax, amount);
            }
        }
    }
    private void GetMaterials()
    {
        Transform SwitchCrystal = Utils.FindChild(transform, "SwitchCrystal");
        m_crystal = SwitchCrystal.gameObject;
        if (!SwitchCrystal) return;
        if (SwitchCrystal.TryGetComponent(out MeshRenderer renderer))
        {
            m_crystalMaterials = renderer.materials;
        }

        Transform fountain = transform.Find("model/Fountain");
        if (!fountain) return;
        foreach (var render in fountain.GetComponentsInChildren<MeshRenderer>(true))
        {
            m_materials.AddRange(render.materials);
        }
    }

    private void GetEffects()
    {
        StatusEffect? pingSE = ObjectDB.instance.GetStatusEffect("Wishbone".GetStableHashCode());
        if (pingSE is SE_Finder finder)
        {
            m_switchEffect = finder.m_pingEffectNear;
        }

        m_completeEffect = new EffectList()
        {
            m_effectPrefabs = new[]
            {
                new EffectList.EffectData()
                {
                    m_prefab = ZNetScene.instance.GetPrefab("fx_DvergerMage_Nova_ring")
                }
            }
        };

        m_playerEffect = new EffectList()
        {
            m_effectPrefabs = new[]
            {
                new EffectList.EffectData()
                {
                    m_prefab = ZNetScene.instance.GetPrefab("fx_DvergerMage_Support_start"),
                    m_attach = true
                }
            }
        };
    }

    private bool OnSwitchRune(Switch caller, Humanoid user, ItemDrop.ItemData item)
    {
        if (!SanctumsPlugin.CompleteChallenge()) return false;
        if (IsActivated()) return false;
        int index = m_currentCode.Count;
        if (!int.TryParse(caller.m_name, out int ID)) return false;
        m_nview.InvokeRPC(nameof(RPC_PingCode), false);
        try
        {
            int number = m_code[index];
            if (number != ID)
            {
                user.Message(MessageHud.MessageType.Center, "$msg_wrongrune");
                m_currentCode.Clear();
                ResetSwitches();
                return false;
            }

            m_currentCode.Add(number);
            m_switches[caller] = true;
            m_switchEffect?.Create(caller.transform.position - new Vector3(0f, 2f, 0f), Quaternion.identity);
            if (m_switches.Values.All(x => x))
            {
                if (m_crystal != null) m_completeEffect?.Create(m_crystal.transform.position, Quaternion.identity);
            }
            // StopCoroutine(PingCode());
            // m_codeTimer = 0.0f;
            m_coroutineRunning = false;
            return true;
        }
        catch
        {
            user.Message(MessageHud.MessageType.Center, "$msg_wrongrune");
            m_currentCode.Clear();
            ResetSwitches();
            return false;
        }
    }

    private string OnSwitchHover()
    {
        if (!SanctumsPlugin.CompleteChallenge()) return "";
        return IsActivated() ? "" : Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] $piece_use");
    }

    private int GetActivatedSwitchesCount() => m_switches.Count(kvp => kvp.Value);

    private string GetName()
    {
        if (m_effect == null) return "";
        return "$hud_sanctumprefix " + m_effect.m_name;
    }

    public string GetHoverText()
    {
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.Append(GetName() + "\n");
        if (IsActivated()) return Localization.instance.Localize(stringBuilder.ToString());
        if (SanctumsPlugin.CompleteChallenge())
        {
            stringBuilder.AppendFormat("{0}/{1}", GetActivatedSwitchesCount(), m_switches.Count);
            stringBuilder.Append(" Activated Runes\n");
        }
        stringBuilder.Append("[<color=yellow><b>$KEY_Use</b></color>] $piece_use");
        return Localization.instance.Localize(stringBuilder.ToString());
    }

    public string GetHoverName() => GetName();

    private bool IsActivated() => m_nview.IsValid() && m_nview.GetZDO().GetBool(m_activeKey);

    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        SanctumManager.PrayAnimation();
        if (user.GetSEMan().GetStatusEffects().Any(x => x is SanctumEffect))
        {
            user.Message(MessageHud.MessageType.Center, "$msg_alreadyhaveeffect");
            return false;
        }
        if (m_activeEffects == null) return false;
        if (!m_nview.IsValid()) return false;
        if (IsActivated()) return false;
        if (m_effect == null) return false;
        if (GetActivatedSwitchesCount() != m_switches.Count && SanctumsPlugin.CompleteChallenge()) return false;
        m_activeEffects.SetActive(true);
        SetGlow(5);
        m_nview.GetZDO().Set(m_activeKey, true);
        user.GetSEMan().AddStatusEffect(m_effect);
        m_playerEffect?.Create(user.GetCenterPoint(), Quaternion.identity, user.transform);
        // SanctumManager.SaveSanctumEffect(m_effect);
        Player.m_localPlayer.m_customData[m_crumblePlayerKey] = m_nview.GetZDO().m_uid.ID.ToString();
        m_nview.GetZDO().Set(m_oldUID, (int)m_nview.GetZDO().m_uid.ID);
        if (m_floatingCrystal != null) m_floatingCrystal.Enable(IsActivated());
        return false;
    }

    public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
}