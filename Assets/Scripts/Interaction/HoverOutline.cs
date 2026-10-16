using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Hover outline for any interactable: duplicates its mesh renderers (and skinned renderers,
/// so NPCs work too) with the inverted-hull outline material. Built once per object on first
/// hover, then just toggled. Statically batched meshes are skipped, so interactables should
/// not be batching-static.
/// </summary>
public static class HoverOutline
{
    static readonly Dictionary<ClickInteractable, GameObject[]> cache = new Dictionary<ClickInteractable, GameObject[]>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => cache.Clear();

    public static void Set(ClickInteractable target, bool on, Material material)
    {
        if (target == null || material == null) return;
        if (!cache.TryGetValue(target, out var parts))
        {
            parts = Build(target, material);
            cache[target] = parts;
        }
        foreach (var p in parts)
            if (p != null) p.SetActive(on);
    }

    static GameObject[] Build(ClickInteractable target, Material material)
    {
        var parts = new List<GameObject>();

        foreach (var mr in target.GetComponentsInChildren<MeshRenderer>())
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || mr.isPartOfStaticBatch || IsBlobShadow(mr)) continue;
            var go = NewPart(mr.transform);
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            Setup(go.AddComponent<MeshRenderer>(), material, mf.sharedMesh.subMeshCount);
            parts.Add(go);
        }

        foreach (var sr in target.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (sr.sharedMesh == null) continue;
            var go = NewPart(sr.transform);
            var copy = go.AddComponent<SkinnedMeshRenderer>();
            copy.sharedMesh = sr.sharedMesh;
            copy.bones = sr.bones;
            copy.rootBone = sr.rootBone;
            copy.localBounds = sr.localBounds;
            Setup(copy, material, sr.sharedMesh.subMeshCount);
            parts.Add(go);
        }

        foreach (var p in parts) p.SetActive(false);
        return parts.ToArray();
    }

    static GameObject NewPart(Transform source)
    {
        var go = new GameObject("HoverOutline");
        go.transform.SetParent(source, false);
        return go;
    }

    static void Setup(Renderer r, Material material, int subMeshes)
    {
        var mats = new Material[Mathf.Max(1, subMeshes)];
        for (int i = 0; i < mats.Length; i++) mats[i] = material;
        r.sharedMaterials = mats;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    static bool IsBlobShadow(Renderer r) =>
        r.name.Contains("BlobShadow") || (r.sharedMaterial != null && r.sharedMaterial.name.Contains("BlobShadow"));
}
