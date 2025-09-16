using UnityEngine;

/// <summary>
/// Place deux objets (watch root et boites) sur la table detectee une fois l OBB verrouillee.
/// Position sur la perpendiculaire du bord frontal (relative a la camera).
/// Prend en compte (option) la rotation de base du prefab comme offset.
/// </summary>
public class TableObjectPlacer : MonoBehaviour
{
    [Header("References")]
    public TableFromDownwardScan tableScan;
    public Camera userCamera;

    [Header("Prefabs")]
    public GameObject watchRootPrefab;
    public GameObject pieceBoxesPrefab;

    [Header("Depth (distance inside table from front edge)")]
    public float watchRootDepthInside = 0.22f;
    public float boxesDepthInside = 0.18f;

    [Header("Offsets")]
    [Tooltip("Offset lateral des boites (metres) le long de l axe lateral table (positif = droite).")]
    public float boxesLateralOffset = 0.25f;
    public float verticalOffset = 0.005f;

    [Header("Edge constraints")]
    public float edgeMargin = 0.04f;
    public float frontEdgeExtraInsetIfTooClose = 0.02f;

    [Header("Orientation")]
    public bool watchRootFacesUser = true;
    public bool boxesFaceUser = true;
    public bool alignInwardIfNotFacingUser = true;

    [Header("Prefab rotation offset")]
    [Tooltip("Si actif: multiplie l orientation calculee par la rotation originale du prefab.")]
    public bool applyWatchRootPrefabRotation = true;
    public bool applyBoxesPrefabRotation = true;

    [Header("Lifecycle")]
    public bool placeOnce = true;
    public bool rePlaceIfRelock = true;
    public bool clearPreviousOnRebuild = true;

    [Header("Debug")]
    public bool logInfo = true;
    public bool drawDebug = true;
    public Color debugColorWatchRoot = new Color(0.3f, 1f, 0.3f, 0.8f);
    public Color debugColorBoxes = new Color(0.3f, 0.6f, 1f, 0.8f);

    // Spawned instances
    GameObject watchRootInstance;
    GameObject boxesInstance;

    bool hasPlaced;
    bool lastLockedState;

    public GameObject WatchRootInstance => watchRootInstance;
    public GameObject BoxesInstance => boxesInstance;

    void Reset()
    {
        if (!userCamera) userCamera = Camera.main;
        if (!tableScan) tableScan = FindObjectOfType<TableFromDownwardScan>();
    }

    void Update()
    {
        if (!tableScan) return;
        if (!userCamera) userCamera = Camera.main;

        bool locked = tableScan.IsOBBLocked;

        if (locked && !lastLockedState)
        {
            if (!placeOnce || (placeOnce && !hasPlaced) || (hasPlaced && rePlaceIfRelock))
            {
                BuildOrRebuild();
                hasPlaced = true;
            }
        }

        lastLockedState = locked;
    }

    public void BuildOrRebuild()
    {
        if (!tableScan || !tableScan.IsOBBLocked) return;

        if (clearPreviousOnRebuild)
            ClearSpawned();

        if (!watchRootPrefab && !pieceBoxesPrefab)
        {
            if (logInfo) Debug.LogWarning("[TableObjectPlacer] No prefabs assigned.");
            return;
        }

        Vector3 center = tableScan.OBBCenter;
        Quaternion rot = tableScan.OBBRotation;
        Vector3 size = tableScan.OBBSize; // x width, z depth

        Vector3 axR = rot * Vector3.right; axR.y = 0; axR.Normalize();
        Vector3 axF = rot * Vector3.forward; axF.y = 0; axF.Normalize();

        Vector3 camPos = userCamera.transform.position;
        Vector3 toCam = camPos - center; toCam.y = 0;
        if (toCam.sqrMagnitude < 1e-6f) toCam = axF;
        toCam.Normalize();

        Vector3[] candidates = { axF, -axF, axR, -axR };
        float bestDot = float.NegativeInfinity;
        Vector3 frontNormal = axF;
        float halfX = size.x * 0.5f;
        float halfZ = size.z * 0.5f;
        float frontHalfExtent = halfZ;

        foreach (var n in candidates)
        {
            float d = Vector3.Dot(n, toCam);
            if (d > bestDot)
            {
                bestDot = d;
                frontNormal = n;
                frontHalfExtent = (ApproximatelySameDirection(n, axF) || ApproximatelySameDirection(n, -axF)) ? halfZ : halfX;
            }
        }

        Vector3 frontEdgeCenter = center + frontNormal * frontHalfExtent;
        Vector3 inward = -frontNormal;
        bool frontIsForward = (ApproximatelySameDirection(frontNormal, axF) || ApproximatelySameDirection(frontNormal, -axF));
        Vector3 lateralAxis = frontIsForward ? axR : axF;
        float lateralHalfExtent = frontIsForward ? halfX : halfZ;
        float insideHalfExtent = frontIsForward ? halfZ : halfX;

        float wrDepth = Mathf.Clamp(watchRootDepthInside, frontEdgeExtraInsetIfTooClose, insideHalfExtent - edgeMargin);
        float bxDepth = Mathf.Clamp(boxesDepthInside, frontEdgeExtraInsetIfTooClose, insideHalfExtent - edgeMargin);

        Vector3 camProj = camPos; camProj.y = center.y;
        Vector3 toCamProj = camProj - center;
        float camLateral = Vector3.Dot(toCamProj, lateralAxis);
        float clampedCamLateral = Mathf.Clamp(camLateral, -lateralHalfExtent + edgeMargin, lateralHalfExtent - edgeMargin);

        Vector3 wrPos = frontEdgeCenter + inward * wrDepth + lateralAxis * clampedCamLateral;
        wrPos.y = center.y + verticalOffset;

        float boxesLat = clampedCamLateral + boxesLateralOffset;
        boxesLat = Mathf.Clamp(boxesLat, -lateralHalfExtent + edgeMargin, lateralHalfExtent - edgeMargin);
        Vector3 bxPos = frontEdgeCenter + inward * bxDepth + lateralAxis * boxesLat;
        bxPos.y = center.y + verticalOffset;

        Quaternion wrBase = ComputeSpawnRotation(watchRootFacesUser, alignInwardIfNotFacingUser, inward, wrPos);
        Quaternion bxBase = ComputeSpawnRotation(boxesFaceUser, alignInwardIfNotFacingUser, inward, bxPos);

        Quaternion wrFinal = ComposeWithPrefabRotation(watchRootPrefab, wrBase, applyWatchRootPrefabRotation);
        Quaternion bxFinal = ComposeWithPrefabRotation(pieceBoxesPrefab, bxBase, applyBoxesPrefabRotation);

        if (watchRootPrefab)
            watchRootInstance = Instantiate(watchRootPrefab, wrPos, wrFinal, transform);

        if (pieceBoxesPrefab)
            boxesInstance = Instantiate(pieceBoxesPrefab, bxPos, bxFinal, transform);

        if (logInfo)
            Debug.Log($"[TableObjectPlacer] Spawn watchRoot={(watchRootInstance != null)} boxes={(boxesInstance != null)} locked={tableScan.IsOBBLocked}");

        // Exemple: notifier manager d assemblage si present dans watchRootInstance
        // var mgr = watchRootInstance ? watchRootInstance.GetComponentInChildren<WatchAssemblyManager>() : null;
        // if (mgr) mgr.DiscoverPieces();
    }

    Quaternion ComposeWithPrefabRotation(GameObject prefab, Quaternion baseRot, bool applyOffset)
    {
        if (!prefab || !applyOffset) return baseRot;
        // La rotation du prefab reference (asset) correspond a son pivot de base.
        // On multiplie pour conserver son offset d auteur.
        return baseRot * prefab.transform.rotation;
    }

    Quaternion ComputeSpawnRotation(bool faceUserFlag, bool inwardFallback, Vector3 inward, Vector3 spawnPos)
    {
        if (!userCamera) return Quaternion.LookRotation(inward, Vector3.up);
        if (faceUserFlag)
        {
            Vector3 toCam = userCamera.transform.position - spawnPos;
            toCam.y = 0;
            if (toCam.sqrMagnitude < 1e-6f) toCam = -inward;
            return Quaternion.LookRotation(toCam.normalized, Vector3.up);
        }
        if (inwardFallback)
            return Quaternion.LookRotation(inward, Vector3.up);
        return Quaternion.LookRotation(inward, Vector3.up);
    }

    bool ApproximatelySameDirection(Vector3 a, Vector3 b)
    {
        float d = Vector3.Dot(a.normalized, b.normalized);
        return d > 0.985f;
    }

    public void ClearSpawned()
    {
        if (watchRootInstance)
        {
#if UNITY_EDITOR
            DestroyImmediate(watchRootInstance);
#else
            Destroy(watchRootInstance);
#endif
        }
        if (boxesInstance)
        {
#if UNITY_EDITOR
            DestroyImmediate(boxesInstance);
#else
            Destroy(boxesInstance);
#endif
        }
        watchRootInstance = null;
        boxesInstance = null;
        hasPlaced = false;
    }

    void OnDrawGizmos()
    {
        if (!drawDebug) return;
        if (watchRootInstance)
        {
            Gizmos.color = debugColorWatchRoot;
            Gizmos.DrawSphere(watchRootInstance.transform.position, 0.025f);
        }
        if (boxesInstance)
        {
            Gizmos.color = debugColorBoxes;
            Gizmos.DrawSphere(boxesInstance.transform.position, 0.025f);
        }
    }
}