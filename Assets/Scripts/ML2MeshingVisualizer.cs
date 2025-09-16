using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
#if UNITY_ANDROID
using MagicLeap.Android;
#endif

public class ML2MeshingBringupV2 : MonoBehaviour
{
    [Header("Refs")]
    public ARMeshManager meshMgr;
    public Camera arCamera;

    [Header("Visuals")]
    public bool visible = true;
    public bool enableCollision = true;

    [Header("Meshing")]
    [Range(0f, 1f)] public float density = 0.6f;
    public bool computeNormals = true;
    public float boundsHalfExtent = 4f;
    public float boundsUpdateInterval = 0.25f;

    GameObject prefabGO;
    float lastBoundsPush;
    bool permissionOk;
    bool subsystemReady;

    void Reset()
    {
        meshMgr = FindFirstObjectByType<ARMeshManager>();
        arCamera = Camera.main;
    }

    void OnEnable()
    {
        if (meshMgr != null) meshMgr.meshesChanged += OnMeshesChanged;
    }
    void OnDisable()
    {
        if (meshMgr != null) meshMgr.meshesChanged -= OnMeshesChanged;
    }

    IEnumerator Start()
    {
        if (meshMgr == null) { Debug.LogError("[Meshing] ARMeshManager missing."); enabled = false; yield break; }
        if (arCamera == null) arCamera = Camera.main;

        // XR loader
        yield return new WaitUntil(() =>
        {
            var m = XRGeneralSettings.Instance ? XRGeneralSettings.Instance.Manager : null;
            return m != null && m.activeLoader != null;
        });

        // ARSession state
        yield return new WaitUntil(() =>
            ARSession.state == ARSessionState.SessionInitializing ||
            ARSession.state == ARSessionState.SessionTracking ||
            ARSession.state == ARSessionState.Ready
        );

        // Permission (ML2)
#if UNITY_ANDROID
        permissionOk = false;
        Permissions.RequestPermission(Permissions.SpatialMapping,
            (p) => { permissionOk = true; Debug.Log("[Meshing] SpatialMapping permission granted."); },
            (p) => { permissionOk = false; Debug.LogError("[Meshing] SpatialMapping permission DENIED."); });
        for (int i = 0; i < 30 && !permissionOk; i++) yield return null;
#else
        permissionOk = true;
#endif
        if (!permissionOk) { Debug.LogError("[Meshing] Missing permission, abort."); yield break; }

        // Descriptors
        var descs = new List<XRMeshSubsystemDescriptor>();
        SubsystemManager.GetSubsystemDescriptors(descs);
        if (descs.Count == 0) { Debug.LogError("[Meshing] No XRMeshSubsystemDescriptor."); yield break; }

        // Force recreate
        meshMgr.enabled = false; yield return null; meshMgr.enabled = true;

        // Wait instance
        for (int i = 0; i < 60 && meshMgr.subsystem == null; i++) yield return null;
        if (meshMgr.subsystem == null)
        {
            var inst = new List<XRMeshSubsystem>();
            SubsystemManager.GetSubsystems(inst);
            Debug.LogError("[Meshing] ARMeshManager.subsystem is NULL.");
            yield break;
        }

        if (!meshMgr.subsystem.running)
        {
            try { meshMgr.subsystem.Start(); } catch { }
        }

        subsystemReady = true;

        // Prefab + settings
        EnsurePrefab();

        // meshPrefab can be Transform or MeshFilter depending on ARF version; set by reflection
        var prop = typeof(ARMeshManager).GetProperty("meshPrefab",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop == null)
        {
            Debug.LogError("[Meshing] ARMeshManager.meshPrefab property not found.");
        }
        else
        {
            var pt = prop.PropertyType;
            if (pt == typeof(Transform))
            {
                prop.SetValue(meshMgr, prefabGO.transform);
            }
            else if (pt == typeof(MeshFilter))
            {
                prop.SetValue(meshMgr, prefabGO.GetComponent<MeshFilter>());
            }
            else
            {
                Debug.LogWarning("[Meshing] meshPrefab unexpected type: " + pt.FullName);
            }
        }

        meshMgr.density = density;
        meshMgr.normals = computeNormals;

        // First bounding + visibility
        PushBounds();
        ApplyVisibility();
    }

    void Update()
    {
        if (!subsystemReady) return;

        if (!Mathf.Approximately(meshMgr.density, density))
            meshMgr.density = density;
        if (meshMgr.normals != computeNormals)
            meshMgr.normals = computeNormals;

        if (Time.time - lastBoundsPush > boundsUpdateInterval)
            PushBounds();
    }

    void EnsurePrefab()
    {
        if (prefabGO != null) return;

        prefabGO = new GameObject("ML2MeshChunk");
        var mf = prefabGO.AddComponent<MeshFilter>();
        var mr = prefabGO.AddComponent<MeshRenderer>();

        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        var mat = new Material(sh);
        mat.color = new Color(0.1f, 0.8f, 1f, 1f);
        mr.sharedMaterial = mat;

        if (enableCollision)
        {
            var mc = prefabGO.AddComponent<MeshCollider>();
            mc.convex = false;
        }

        // Layer robuste + log
        int worldMeshLayer = LayerMask.NameToLayer("WorldMesh");
        if (worldMeshLayer < 0)
        {
            Debug.LogWarning("[Meshing] Layer 'WorldMesh' introuvable. Les chunks seront sur Default. "
                + "Créez le layer 'WorldMesh' ou mettez votre LayerMask sur Everything pour tester.");
            worldMeshLayer = 0; // Default
        }
        prefabGO.layer = worldMeshLayer;
    }

    void ApplyVisibility()
    {
        foreach (var f in FindObjectsOfType<MeshFilter>())
        {
            if (!f || !f.gameObject.name.StartsWith("ML2MeshChunk")) continue;
            var r = f.GetComponent<MeshRenderer>();
            if (r) r.enabled = visible;
        }
    }

    void OnMeshesChanged(ARMeshesChangedEventArgs e)
    {
        if (e.added != null)
        {
            int worldMeshLayer = LayerMask.NameToLayer("WorldMesh");
            if (worldMeshLayer < 0) worldMeshLayer = 0; // Default

            foreach (var mf in e.added)
            {
                // Force le layer sur chaque chunk au cas où
                mf.gameObject.layer = worldMeshLayer;

                var r = mf.GetComponent<MeshRenderer>();
                if (r) r.enabled = visible;

                var mc = mf.GetComponent<MeshCollider>();
                if (enableCollision)
                {
                    if (!mc) mf.gameObject.AddComponent<MeshCollider>();
                }
                else
                {
                    if (mc) mc.enabled = false;
                }

            }
        }
    }

    void PushBounds()
    {
        if (!subsystemReady || arCamera == null) return;

        var center = arCamera.transform.position;
        var ext = new Vector3(boundsHalfExtent, boundsHalfExtent, boundsHalfExtent);

        bool ok = InvokeBounding(center, ext);
        lastBoundsPush = Time.time;
    }

    bool InvokeBounding(Vector3 center, Vector3 extents)
    {
        var sub = meshMgr.subsystem;
        if (sub == null) return false;

        var t = sub.GetType();

        var m = t.GetMethod("SetBoundingVolume", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            null, new System.Type[] { typeof(Vector3), typeof(Vector3) }, null);
        if (m != null)
        {
            object r = m.Invoke(sub, new object[] { center, extents });
            return r is bool ? (bool)r : true;
        }

        m = t.GetMethod("TrySetBoundingVolume", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new System.Type[] { typeof(Vector3), typeof(Vector3) }, null);
        if (m != null)
        {
            object r = m.Invoke(sub, new object[] { center, extents });
            return r is bool ? (bool)r : true;
        }

        m = t.GetMethod("SetBoundingVolume", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new System.Type[] { typeof(Bounds) }, null);
        if (m != null)
        {
            Bounds b = new Bounds(center, extents * 2f);
            object r = m.Invoke(sub, new object[] { b });
            return r is bool ? (bool)r : true;
        }

        m = t.GetMethod("TrySetBoundingVolume", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new System.Type[] { typeof(Bounds) }, null);
        if (m != null)
        {
            Bounds b = new Bounds(center, extents * 2f);
            object r = m.Invoke(sub, new object[] { b });
            return r is bool ? (bool)r : true;
        }

        return false;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!arCamera) return;
        Gizmos.color = new Color(0f, 1f, 1f, 0.6f);
        var c = arCamera.transform.position;
        var s = Vector3.one * (boundsHalfExtent * 2f);
        Gizmos.DrawWireCube(c, s);
    }
#endif
}
