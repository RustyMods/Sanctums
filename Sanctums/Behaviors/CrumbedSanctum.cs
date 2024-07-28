using System.Collections.Generic;
using UnityEngine;

namespace Sanctums.Behaviors;

public class CrumbedSanctum : MonoBehaviour
{
    private static readonly List<CrumbedSanctum> m_instances = new();
    public ZNetView m_nview = null!;
    public void Awake()
    {
        m_nview = GetComponent<ZNetView>();
        m_instances.Add(this);
    }
    public void OnDestroy()
    {
        m_instances.Remove(this);
    }
    public void Reset()
    {
        m_nview.GetZDO().Set(Sanctum.m_activeKey, false);
        m_nview.GetZDO().m_prefab = "KingSanctum".GetStableHashCode();
    }

    public static void ResetAllSanctums()
    {
        int count = 0;
        foreach (CrumbedSanctum instance in m_instances)
        {
            instance.Reset();
            ++count;
        }
        SanctumsPlugin.SanctumsLogger.LogInfo($"Reset {count} crumbled sanctums");
    }
}