using System.Collections.Generic;
using UnityEngine;

public class TableFromDownwardScan : MonoBehaviour
{
    [Header("Références")]
    public Camera arCamera;
    public LayerMask meshLayerMask = ~0;

    [Header("Zone d'échantillonnage (disque devant la caméra)")]
    public float scanCircleCenterForward = 0.30f;
    public float scanCircleRadius = 0.75f;
    public float scanMaxForward = 1.20f;
    public float scanMaxSide = 1.20f;
    public float areaUp = 0.2f;

    [Header("Grille de raycasts")]
    public int gridX = 25;
    public int gridZ = 25;
    public float rayStartAbove = 0.30f;
    public float rayLength = 1.5f;

    [Header("Filtrage (optionnel)")]
    public bool filterBySlope = true;
    public float topMaxSlopeDeg = 35f;

    [Header("Table: détection hauteur")]
    public float histBinSize = 0.02f;
    public float tableBandHalf = 0.03f;
    public int minTableHits = 20;

    public enum EdgeMode { DistanceToOBB, GridBorder }

    [Header("Bord - Mode")]
    public EdgeMode edgeMode = EdgeMode.GridBorder;

    [Header("Bord (DistanceToOBB)")]
    public float edgeBand = 0.05f;
    public bool useTrimmedExtents = true;
    [Range(0f, 0.2f)] public float obbTrimPercent = 0.04f;

    [Header("Bord (GridBorder)")]
    public float gridCellSize = 0.025f;
    [Range(0, 3)] public int edgeDilation = 1;
    public bool splatHits = true;
    [Range(0, 3)] public int splatRadiusCells = 1;
    [Range(1, 5)] public int borderThicknessCells = 1;

    [Header("Affichage des hits")]
    public bool showGreenHits = true;
    public bool showBlueHits = true;
    public bool showOrangeHits = true;
    public int poolSizePerColor = 2048;
    public float hitMarkerSize = 0.02f;
    public float hitMarkerLift = 0.006f;
    public Color greenColor = new Color(0f, 1f, 0.2f, 1f);
    public Color blueColor = new Color(0.2f, 0.55f, 1f, 1f);
    public Color orangeColor = new Color(1f, 0.5f, 0f, 1f);

    [Header("OBB (table stable)")]
    public bool enableOBB = true;
    public bool autoCreateOBBObject = true;
    public string obbObjectName = "DetectedTableOBB";
    public float obbThickness = 0.025f;
    public float obbYOffset = 0.0f;
    public bool obbUseExtentTrim = true;
    public int requiredStableFrames = 6;
    public float posStableTol = 0.015f;
    public float rotStableTolDeg = 4f;
    public float sizeStableTol = 0.025f;
    public bool lockStopsUpdates = true;
    public bool allowBigRelock = false;
    public float bigChangePos = 0.18f;
    public float bigChangeSize = 0.14f;
    public float bigChangeRotDeg = 22f;
    public int bigChangeNeededFrames = 3;

    [Header("OBB Stabilisation avancée")]
    public float obbPadding = 0.02f;
    public bool usePercentileExtents = false;
    [Range(0f, 0.2f)] public float lowPercentile = 0.02f;
    [Range(0.8f, 1f)] public float highPercentile = 0.98f;
    public float sizeGrowLerp = 10f;
    public float sizeShrinkLerp = 1.5f;
    public float posLerp = 12f;
    public float rotLerp = 10f;
    public float maxRotStepDeg = 3f;
    public int orientationLockFrames = 12;
    public bool freezeOrientationAfterLock = true;

    [Header("OBB Visuel")]
    public bool createOBBVisual = true;
    public Color obbVisualColor = new Color(0.9f, 0.45f, 0.15f, 1f);
    public Material obbMaterial;
    public bool obbVisualTopAtTableSurface = true;

    [Header("Accumulation initiale (meilleur fit orienté)")]
    public bool useInitialAccumulation = true;
    public float initialAccumSeconds = 1.5f;
    public int minAccumulatedHits = 120;
    public int maxAccumulatedHits = 10000;
    public bool lockImmediatelyAfterAccum = false;
    public bool showDuringAccum = true;

    public enum OrientationMode { PCA, HullMinArea }
    [Header("Orientation OBB")]
    public OrientationMode orientationMode = OrientationMode.HullMinArea;
    public bool fallbackToPCAIfUnstable = true;
    public int minHullPointsForCalipers = 6;

    public bool showOBBEdgesGizmos = true;
    public Color obbGizmoColor = new Color(0, 0.7f, 1f, 0.85f);

    bool obbLocked;
    int stableCounter;
    int bigChangeCounter;
    bool haveLastOBBFit;
    int orientationFrameCount;
    Vector3 lastCenter;
    Quaternion lastRot;
    float lastSizeX, lastSizeZ;
    Vector3 smoothCenter;
    Quaternion smoothRot;
    float smoothSizeX, smoothSizeZ;
    bool haveSmoothed;
    float persistMinX, persistMaxX, persistMinZ, persistMaxZ;
    bool havePersistent;
    bool accumulating;
    bool accumulationDone;
    float accumStartTime;
    readonly List<Vector3> accumulatedTableHits = new List<Vector3>(12000);

    GameObject obbRoot;
    Transform obbVisual;

    Transform greenRoot, blueRoot, orangeRoot;
    readonly List<Renderer> greenMarkers = new List<Renderer>(4096);
    readonly List<Renderer> blueMarkers = new List<Renderer>(4096);
    readonly List<Renderer> orangeMarkers = new List<Renderer>(4096);
    int activeGreen, activeBlue, activeOrange;

    readonly List<Vector3> allHits = new List<Vector3>(8192);
    readonly List<Vector3> tableHits = new List<Vector3>(8192);
    readonly List<float> projX = new List<float>(8192);
    readonly List<float> projZ = new List<float>(8192);

    Vector3 curOBBCenter;
    Quaternion curOBBRot;
    float curOBBSizeX;
    float curOBBSizeZ;
    bool haveCurrentOBB;

    readonly List<Vector2> hullPoints = new List<Vector2>(2048);
    readonly List<Vector2> hull = new List<Vector2>(512);

    void Reset() { arCamera = Camera.main; }
    void Start()
    {
        if (!arCamera) arCamera = Camera.main;
        EnsurePools(poolSizePerColor);
        if (useInitialAccumulation) StartAccumulation();
    }

    void StartAccumulation()
    {
        accumulating = true;
        accumulationDone = false;
        accumulatedTableHits.Clear();
        accumStartTime = Time.time;
    }

    void Update()
    {
        if (!arCamera) { arCamera = Camera.main; if (!arCamera) return; }
        ScanAndShowHits();
        if (enableOBB) UpdateOBBGameObject();
    }

    void EnsureOBBRoot()
    {
        if (obbRoot || !autoCreateOBBObject) return;
        obbRoot = new GameObject(obbObjectName);
        var bc = obbRoot.AddComponent<BoxCollider>();
        bc.size = new Vector3(0.5f, obbThickness, 0.5f);
        float half = obbThickness * 0.5f;
        bc.center = new Vector3(0f, obbVisualTopAtTableSurface ? -half : half, 0f);

        var rb = obbRoot.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        if (createOBBVisual) CreateOrUpdateOBBVisual(true);
    }

    void CreateOrUpdateOBBVisual(bool forceCreate = false)
    {
        if (!createOBBVisual || !obbRoot) return;
        if (forceCreate || !obbVisual)
        {
            if (obbVisual) Destroy(obbVisual.gameObject);
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var c = visual.GetComponent<Collider>(); if (c) Destroy(c);
            visual.name = "OBB_Visual";
            visual.transform.SetParent(obbRoot.transform, false);
            var mr = visual.GetComponent<MeshRenderer>();
            if (!obbMaterial)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (!sh) sh = Shader.Find("Standard");
                obbMaterial = new Material(sh) { color = obbVisualColor };
            }
            mr.sharedMaterial = obbMaterial;
            obbVisual = visual.transform;
        }
        if (obbVisual)
        {
            float half = obbThickness * 0.5f;
            obbVisual.localPosition = new Vector3(0f, obbVisualTopAtTableSurface ? -half : half, 0f);
            obbVisual.localRotation = Quaternion.identity;
            obbVisual.localScale = new Vector3(Mathf.Max(0.01f, curOBBSizeX),
                                               obbThickness,
                                               Mathf.Max(0.01f, curOBBSizeZ));
            if (obbMaterial) obbMaterial.color = obbVisualColor;
        }
    }

    void UpdateOBBGameObject()
    {
        if (!haveCurrentOBB) return;
        EnsureOBBRoot();
        if (!obbRoot) return;
        if (obbLocked && lockStopsUpdates) { CreateOrUpdateOBBVisual(); return; }

        obbRoot.transform.SetPositionAndRotation(curOBBCenter, curOBBRot);
        var collider = obbRoot.GetComponent<BoxCollider>();
        if (collider)
        {
            float half = obbThickness * 0.5f;
            collider.size = new Vector3(Mathf.Max(0.01f, curOBBSizeX), obbThickness, Mathf.Max(0.01f, curOBBSizeZ));
            collider.center = new Vector3(0f, obbVisualTopAtTableSurface ? -half : half, 0f);
        }
        CreateOrUpdateOBBVisual();
    }

    void ScanAndShowHits()
    {
        BeginMarkers();
        allHits.Clear(); tableHits.Clear(); projX.Clear(); projZ.Clear();
        Transform cam = arCamera.transform;
        Vector3 up = Vector3.up;
        Vector3 right = cam.right.normalized;
        Vector3 forwardGround = Vector3.ProjectOnPlane(cam.forward, up).normalized;
        if (forwardGround.sqrMagnitude < 1e-6f) forwardGround = Vector3.forward;
        Vector3 center = cam.position + forwardGround * scanCircleCenterForward + up * areaUp;
        float rFwdLimit = Mathf.Max(0.05f, scanMaxForward - scanCircleCenterForward);
        float allowedRadius = Mathf.Min(scanCircleRadius, rFwdLimit, scanMaxSide);
        float r2 = allowedRadius * allowedRadius;
        int gX = Mathf.Max(1, gridX);
        int gZ = Mathf.Max(1, gridZ);

        for (int ix = 0; ix < gX; ix++)
        {
            float u = (gX == 1) ? 0f : (ix / (float)(gX - 1)) * 2f - 1f;
            for (int iz = 0; iz < gZ; iz++)
            {
                float v = (gZ == 1) ? 0f : (iz / (float)(gZ - 1)) * 2f - 1f;
                float dx = u * allowedRadius;
                float dz = v * allowedRadius;
                if (dx * dx + dz * dz > r2) continue;
                Vector3 p = center + right * dx + forwardGround * dz;
                Vector3 ro = p + up * rayStartAbove;
                if (Physics.Raycast(ro, Vector3.down, out RaycastHit hit, rayLength, meshLayerMask, QueryTriggerInteraction.Ignore))
                {
                    if (filterBySlope)
                    {
                        float ang = Vector3.Angle(hit.normal, up);
                        if (ang > topMaxSlopeDeg) continue;
                    }
                    allHits.Add(hit.point);
                }
            }
        }
        if (allHits.Count == 0) { EndMarkers(); return; }

        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
        for (int i = 0; i < allHits.Count; i++)
        {
            float y = allHits[i].y;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
        }
        int bins = Mathf.Max(1, Mathf.CeilToInt((maxY - minY) / Mathf.Max(0.001f, histBinSize)));
        int bestBin = -1, bestCount = 0;
        var counts = new int[bins];
        for (int i = 0; i < allHits.Count; i++)
        {
            int bi = Mathf.Clamp(Mathf.FloorToInt((allHits[i].y - minY) / histBinSize), 0, bins - 1);
            int c = ++counts[bi]; if (c > bestCount) { bestCount = c; bestBin = bi; }
        }
        if (bestBin < 0) { DrawAllAsOrange(); return; }
        float tableCenterY = minY + (bestBin + 0.5f) * histBinSize;
        float keepMinY = tableCenterY - tableBandHalf;
        float keepMaxY = tableCenterY + tableBandHalf;

        for (int i = 0; i < allHits.Count; i++)
        {
            float y = allHits[i].y;
            if (y >= keepMinY && y <= keepMaxY) tableHits.Add(allHits[i]);
        }
        if (tableHits.Count < minTableHits)
        {
            if (accumulating && showDuringAccum) ShowSimpleBand(allHits, keepMinY, keepMaxY);
            EndMarkers(); return;
        }

        if (useInitialAccumulation && accumulating && !accumulationDone)
        {
            if (accumulatedTableHits.Count < maxAccumulatedHits)
            {
                int left = maxAccumulatedHits - accumulatedTableHits.Count;
                if (tableHits.Count <= left) accumulatedTableHits.AddRange(tableHits);
                else for (int i = 0; i < left; i++) accumulatedTableHits.Add(tableHits[i]);
            }
            float elapsed = Time.time - accumStartTime;
            bool enoughTime = elapsed >= initialAccumSeconds;
            bool enoughHits = accumulatedTableHits.Count >= minAccumulatedHits;
            if (showDuringAccum) ShowSimpleBand(allHits, keepMinY, keepMaxY);
            if (enoughTime && enoughHits)
            {
                accumulationDone = true;
                accumulating = false;
                tableHits.Clear();
                tableHits.AddRange(accumulatedTableHits);
                havePersistent = false;
                haveSmoothed = false;
                haveLastOBBFit = false;
                stableCounter = 0;
            }
            else { EndMarkers(); return; }
        }

        Vector3 centroid = Vector3.zero;
        for (int i = 0; i < tableHits.Count; i++) centroid += tableHits[i];
        centroid /= tableHits.Count;

        Vector3 axisX, axisZ;
        if (obbLocked && freezeOrientationAfterLock)
        {
            axisZ = curOBBRot * Vector3.forward;
            axisX = curOBBRot * Vector3.right;
        }
        else
        {
            switch (orientationMode)
            {
                case OrientationMode.HullMinArea:
                    if (!ComputeHullMinAreaAxes(tableHits, centroid, out axisX, out axisZ))
                        ComputePCAAxes(tableHits, centroid, out axisX, out axisZ);
                    break;
                default:
                    ComputePCAAxes(tableHits, centroid, out axisX, out axisZ);
                    break;
            }
            if (haveSmoothed && !obbLocked)
            {
                Quaternion rawRot = Quaternion.LookRotation(axisZ, Vector3.up);
                Quaternion targetRot = rawRot;
                float angDelta = Quaternion.Angle(smoothRot, rawRot);
                if (angDelta > maxRotStepDeg)
                {
                    float t = maxRotStepDeg / Mathf.Max(angDelta, 1e-6f);
                    targetRot = Quaternion.Slerp(smoothRot, rawRot, t);
                }
                float kRot = 1f - Mathf.Exp(-rotLerp * Time.deltaTime);
                smoothRot = Quaternion.Slerp(smoothRot, targetRot, kRot);
                axisZ = smoothRot * Vector3.forward;
                axisX = smoothRot * Vector3.right;
                orientationFrameCount++;
            }
            else if (!haveSmoothed)
            {
                smoothRot = Quaternion.LookRotation(axisZ, Vector3.up);
            }
        }

        projX.Clear(); projZ.Clear();
        float rawMinX = float.PositiveInfinity, rawMaxX = float.NegativeInfinity;
        float rawMinZ = float.PositiveInfinity, rawMaxZ = float.NegativeInfinity;
        for (int i = 0; i < tableHits.Count; i++)
        {
            Vector3 d = tableHits[i] - centroid;
            float px = Vector3.Dot(d, axisX);
            float pz = Vector3.Dot(d, axisZ);
            projX.Add(px); projZ.Add(pz);
            if (px < rawMinX) rawMinX = px; if (px > rawMaxX) rawMaxX = px;
            if (pz < rawMinZ) rawMinZ = pz; if (pz > rawMaxZ) rawMaxZ = pz;
        }

        float usedMinX = rawMinX, usedMaxX = rawMaxX;
        float usedMinZ = rawMinZ, usedMaxZ = rawMaxZ;
        if (usePercentileExtents && tableHits.Count >= 8)
        {
            (usedMinX, usedMaxX) = PercentileRange(projX, lowPercentile, highPercentile);
            (usedMinZ, usedMaxZ) = PercentileRange(projZ, lowPercentile, highPercentile);
        }
        else if (obbUseExtentTrim && obbTrimPercent > 0f && tableHits.Count >= 8)
        {
            (usedMinX, usedMaxX) = PercentileRange(projX, obbTrimPercent, 1f - obbTrimPercent);
            (usedMinZ, usedMaxZ) = PercentileRange(projZ, obbTrimPercent, 1f - obbTrimPercent);
        }

        usedMinX -= obbPadding; usedMaxX += obbPadding;
        usedMinZ -= obbPadding; usedMaxZ += obbPadding;

        if (!havePersistent)
        {
            persistMinX = usedMinX; persistMaxX = usedMaxX;
            persistMinZ = usedMinZ; persistMaxZ = usedMaxZ;
            havePersistent = true;
        }
        if (!obbLocked)
        {
            persistMinX = Mathf.Min(persistMinX, usedMinX);
            persistMaxX = Mathf.Max(persistMaxX, usedMaxX);
            persistMinZ = Mathf.Min(persistMinZ, usedMinZ);
            persistMaxZ = Mathf.Max(persistMaxZ, usedMaxZ);

            float shrinkK = 1f - Mathf.Exp(-sizeShrinkLerp * Time.deltaTime);
            persistMinX = Mathf.Lerp(persistMinX, usedMinX, shrinkK);
            persistMaxX = Mathf.Lerp(persistMaxX, usedMaxX, shrinkK);
            persistMinZ = Mathf.Lerp(persistMinZ, usedMinZ, shrinkK);
            persistMaxZ = Mathf.Lerp(persistMaxZ, usedMaxZ, shrinkK);
        }

        float finalMinX = persistMinX;
        float finalMaxX = persistMaxX;
        float finalMinZ = persistMinZ;
        float finalMaxZ = persistMaxZ;

        float targetSizeX = Mathf.Max(0.02f, finalMaxX - finalMinX);
        float targetSizeZ = Mathf.Max(0.02f, finalMaxZ - finalMinZ);
        Vector3 rawCenter = centroid + axisX * (finalMinX + finalMaxX) * 0.5f + axisZ * (finalMinZ + finalMaxZ) * 0.5f;
        rawCenter.y = tableCenterY + obbYOffset;
        Quaternion rawRotQ = obbLocked && freezeOrientationAfterLock ? curOBBRot : Quaternion.LookRotation(axisZ, Vector3.up);

        float growK2 = 1f - Mathf.Exp(-sizeGrowLerp * Time.deltaTime);
        float shrinkK2 = 1f - Mathf.Exp(-sizeShrinkLerp * Time.deltaTime);
        if (!haveSmoothed)
        {
            smoothCenter = rawCenter;
            smoothSizeX = targetSizeX;
            smoothSizeZ = targetSizeZ;
            smoothRot = rawRotQ;
            haveSmoothed = true;
        }
        else if (!obbLocked)
        {
            float kp = 1f - Mathf.Exp(-posLerp * Time.deltaTime);
            smoothCenter = Vector3.Lerp(smoothCenter, rawCenter, kp);
            smoothSizeX = (targetSizeX > smoothSizeX) ? Mathf.Lerp(smoothSizeX, targetSizeX, growK2) : Mathf.Lerp(smoothSizeX, targetSizeX, shrinkK2);
            smoothSizeZ = (targetSizeZ > smoothSizeZ) ? Mathf.Lerp(smoothSizeZ, targetSizeZ, growK2) : Mathf.Lerp(smoothSizeZ, targetSizeZ, shrinkK2);

            Quaternion desired = rawRotQ;
            float angDelta = Quaternion.Angle(smoothRot, desired);
            if (angDelta > maxRotStepDeg)
            {
                float t = maxRotStepDeg / Mathf.Max(angDelta, 1e-6f);
                desired = Quaternion.Slerp(smoothRot, desired, t);
            }
            float kRot = 1f - Mathf.Exp(-rotLerp * Time.deltaTime);
            smoothRot = Quaternion.Slerp(smoothRot, desired, kRot);
        }

        if (accumulationDone && !obbLocked && lockImmediatelyAfterAccum)
        {
            obbLocked = true;
            haveLastOBBFit = true;
            lastCenter = smoothCenter; lastRot = smoothRot; lastSizeX = smoothSizeX; lastSizeZ = smoothSizeZ;
        }
        else
        {
            ProcessOBBStability(smoothCenter, smoothRot, smoothSizeX, smoothSizeZ);
        }

        curOBBCenter = smoothCenter;
        curOBBRot = smoothRot;
        curOBBSizeX = smoothSizeX;
        curOBBSizeZ = smoothSizeZ;
        haveCurrentOBB = true;

        ClassifyAndRender(allHits, keepMinY, keepMaxY, centroid,
            curOBBRot * Vector3.right, curOBBRot * Vector3.forward,
            finalMinX, finalMaxX, finalMinZ, finalMaxZ, tableCenterY);

        EndMarkers();
    }

    void ComputePCAAxes(List<Vector3> pts, Vector3 centroid, out Vector3 axisX, out Vector3 axisZ)
    {
        float sxx = 0f, szz = 0f, sxz = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 d = pts[i] - centroid;
            float x = d.x; float z = d.z;
            sxx += x * x; szz += z * z; sxz += x * z;
        }
        float invN = 1f / pts.Count;
        sxx *= invN; szz *= invN; sxz *= invN;
        float trace = sxx + szz;
        float det = sxx * szz - sxz * sxz;
        float tmp = Mathf.Sqrt(Mathf.Max(0f, trace * trace * 0.25f - det));
        float l1 = trace * 0.5f + tmp;
        Vector2 v1 = new Vector2(sxz, l1 - sxx);
        if (v1.sqrMagnitude < 1e-8f) v1 = new Vector2(1f, 0f);
        v1.Normalize();
        axisX = new Vector3(v1.x, 0f, v1.y).normalized;
        axisZ = Vector3.Cross(Vector3.up, axisX).normalized;
    }

    bool ComputeHullMinAreaAxes(List<Vector3> pts, Vector3 centroid, out Vector3 axisX, out Vector3 axisZ)
    {
        axisX = Vector3.right; axisZ = Vector3.forward;
        if (pts.Count < minHullPointsForCalipers) return false;
        hullPoints.Clear();
        for (int i = 0; i < pts.Count; i++)
            hullPoints.Add(new Vector2(pts[i].x, pts[i].z));
        hull.Clear();
        hullPoints.Sort((a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        var lower = new List<Vector2>();
        for (int i = 0; i < hullPoints.Count; i++)
        {
            while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], hullPoints[i]) <= 0f)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(hullPoints[i]);
        }
        var upper = new List<Vector2>();
        for (int i = hullPoints.Count - 1; i >= 0; i--)
        {
            while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], hullPoints[i]) <= 0f)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(hullPoints[i]);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        hull.AddRange(lower);
        hull.AddRange(upper);
        if (hull.Count < 3) return false;
        float bestArea = float.PositiveInfinity;
        Vector2 bestU = Vector2.right;
        Vector2 bestV = Vector2.up;
        for (int i = 0; i < hull.Count; i++)
        {
            Vector2 p0 = hull[i];
            Vector2 p1 = hull[(i + 1) % hull.Count];
            Vector2 edge = (p1 - p0);
            float len = edge.magnitude;
            if (len < 1e-6f) continue;
            Vector2 u = edge / len;
            Vector2 v = new Vector2(-u.y, u.x);
            float minU = float.PositiveInfinity, maxU = float.NegativeInfinity;
            float minV = float.PositiveInfinity, maxV = float.NegativeInfinity;
            for (int k = 0; k < hull.Count; k++)
            {
                Vector2 hp = hull[k] - p0;
                float du = Vector2.Dot(hp, u);
                float dv = Vector2.Dot(hp, v);
                if (du < minU) minU = du; if (du > maxU) maxU = du;
                if (dv < minV) minV = dv; if (dv > maxV) maxV = dv;
            }
            float area = (maxU - minU) * (maxV - minV);
            if (area < bestArea)
            {
                bestArea = area;
                bestU = u;
                bestV = v;
            }
        }
        axisX = new Vector3(bestU.x, 0f, bestU.y).normalized;
        axisZ = new Vector3(bestV.x, 0f, bestV.y).normalized;
        if (axisX.sqrMagnitude < 1e-6f || axisZ.sqrMagnitude < 1e-6f) return false;
        axisZ = Vector3.Cross(Vector3.up, axisX).normalized;
        return true;
    }

    float Cross(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }

    void ShowSimpleBand(List<Vector3> hits, float keepMinY, float keepMaxY)
    {
        for (int i = 0; i < hits.Count; i++)
        {
            var p = hits[i];
            bool inBand = p.y >= keepMinY && p.y <= keepMaxY;
            if (inBand)
            {
                if (showGreenHits) PlaceGreenMarker(p + Vector3.up * hitMarkerLift);
            }
            else
            {
                if (showOrangeHits) PlaceOrangeMarker(p + Vector3.up * hitMarkerLift);
            }
        }
    }

    void ClassifyAndRender(List<Vector3> hits, float kMinY, float kMaxY,
        Vector3 centroid, Vector3 axisX, Vector3 axisZ,
        float minX, float maxX, float minZ, float maxZ, float tableY)
    {
        bool[,] edgeMask = null;
        int nx = 0, nz = 0;
        float cell = Mathf.Max(0.005f, gridCellSize);

        if (edgeMode == EdgeMode.GridBorder)
        {
            var projAllX = new List<float>(tableHits.Count);
            var projAllZ = new List<float>(tableHits.Count);
            for (int i = 0; i < tableHits.Count; i++)
            {
                Vector3 d = tableHits[i] - centroid;
                projAllX.Add(Vector3.Dot(d, axisX));
                projAllZ.Add(Vector3.Dot(d, axisZ));
            }
            nx = Mathf.Min(512, Mathf.CeilToInt((maxX - minX) / cell) + 1);
            nz = Mathf.Min(512, Mathf.CeilToInt((maxZ - minZ) / cell) + 1);
            if (nx >= 2 && nz >= 2)
            {
                bool[,] occ = new bool[nx, nz];
                for (int i = 0; i < projAllX.Count; i++)
                {
                    int ix = Mathf.Clamp((int)((projAllX[i] - minX) / cell), 0, nx - 1);
                    int iz = Mathf.Clamp((int)((projAllZ[i] - minZ) / cell), 0, nz - 1);
                    occ[ix, iz] = true;
                }
                bool[,] current = (bool[,])occ.Clone();
                for (int iter = 0; iter < borderThicknessCells; iter++)
                {
                    bool[,] next = new bool[nx, nz];
                    for (int ix = 0; ix < nx; ix++)
                        for (int iz = 0; iz < nz; iz++)
                        {
                            if (!current[ix, iz]) continue;
                            bool nL = (ix > 0) && current[ix - 1, iz];
                            bool nR = (ix + 1 < nx) && current[ix + 1, iz];
                            bool nD = (iz > 0) && current[ix, iz - 1];
                            bool nU = (iz + 1 < nz) && current[ix, iz + 1];
                            if (nL && nR && nD && nU) next[ix, iz] = true;
                        }
                    current = next;
                }
                bool[,] interior = current;
                edgeMask = new bool[nx, nz];
                for (int ix = 0; ix < nx; ix++)
                    for (int iz = 0; iz < nz; iz++)
                        if (occ[ix, iz] && !interior[ix, iz]) edgeMask[ix, iz] = true;
                for (int pass = 0; pass < edgeDilation; pass++)
                {
                    bool[,] dil = new bool[nx, nz];
                    int[] dx8 = { -1, 0, 1, -1, 1, -1, 0, 1 };
                    int[] dz8 = { -1, -1, -1, 0, 0, 1, 1, 1 };
                    for (int ix = 0; ix < nx; ix++)
                        for (int iz = 0; iz < nz; iz++)
                        {
                            if (edgeMask[ix, iz]) { dil[ix, iz] = true; continue; }
                            bool near = false;
                            for (int k = 0; k < 8; k++)
                            {
                                int jx = ix + dx8[k];
                                int jz = iz + dz8[k];
                                if (jx < 0 || jx >= nx || jz < 0 || jz >= nz) continue;
                                if (edgeMask[jx, jz]) { near = true; break; }
                            }
                            if (near) dil[ix, iz] = true;
                        }
                    edgeMask = dil;
                }
            }
            else edgeMode = EdgeMode.DistanceToOBB;
        }

        for (int i = 0; i < hits.Count; i++)
        {
            Vector3 p = hits[i];
            bool isTable = (p.y >= kMinY && p.y <= kMaxY);
            if (!isTable)
            {
                if (showOrangeHits) PlaceOrangeMarker(p + Vector3.up * hitMarkerLift);
                continue;
            }
            Vector3 d = p - centroid;
            float px = Vector3.Dot(d, axisX);
            float pz = Vector3.Dot(d, axisZ);
            bool isEdge = false;
            if (edgeMode == EdgeMode.DistanceToOBB)
            {
                float dist = Mathf.Min(
                    Mathf.Min(px - minX, maxX - px),
                    Mathf.Min(pz - minZ, maxZ - pz)
                );
                if (dist < 0f) dist = 0f;
                isEdge = dist <= edgeBand;
            }
            else if (edgeMode == EdgeMode.GridBorder && edgeMask != null)
            {
                int ix = Mathf.Clamp((int)((px - minX) / gridCellSize), 0, nx - 1);
                int iz = Mathf.Clamp((int)((pz - minZ) / gridCellSize), 0, nz - 1);
                isEdge = edgeMask[ix, iz];
            }
            if (isEdge)
            {
                if (showBlueHits) PlaceBlueMarker(p + Vector3.up * hitMarkerLift);
            }
            else
            {
                if (showGreenHits) PlaceGreenMarker(p + Vector3.up * hitMarkerLift);
            }
        }
    }

    void ProcessOBBStability(Vector3 c, Quaternion r, float sx, float sz)
    {
        curOBBCenter = c; curOBBRot = r; curOBBSizeX = sx; curOBBSizeZ = sz; haveCurrentOBB = true;
        if (!enableOBB) return;
        if (obbLocked && !allowBigRelock) return;
        if (obbLocked && allowBigRelock)
        {
            float dp = Vector3.Distance(c, lastCenter);
            float dr = Quaternion.Angle(r, lastRot);
            float ds = Mathf.Max(Mathf.Abs(sx - lastSizeX), Mathf.Abs(sz - lastSizeZ));
            if (dp > bigChangePos || dr > bigChangeRotDeg || ds > bigChangeSize)
            {
                bigChangeCounter++;
                if (bigChangeCounter >= bigChangeNeededFrames)
                {
                    lastCenter = c; lastRot = r; lastSizeX = sx; lastSizeZ = sz;
                    stableCounter = 0; bigChangeCounter = 0;
                    obbLocked = false;
                }
            }
            else bigChangeCounter = 0;
            return;
        }
        if (!haveLastOBBFit)
        {
            lastCenter = c; lastRot = r; lastSizeX = sx; lastSizeZ = sz;
            haveLastOBBFit = true;
            stableCounter = 0;
            return;
        }
        bool stable =
            Vector3.Distance(c, lastCenter) <= posStableTol &&
            Quaternion.Angle(r, lastRot) <= rotStableTolDeg &&
            Mathf.Abs(sx - lastSizeX) <= sizeStableTol &&
            Mathf.Abs(sz - lastSizeZ) <= sizeStableTol;
        stableCounter = stable ? (stableCounter + 1) : 0;
        lastCenter = c; lastRot = r; lastSizeX = sx; lastSizeZ = sz;
        if (!obbLocked && stableCounter >= requiredStableFrames)
        {
            obbLocked = true;
            if (freezeOrientationAfterLock) orientationFrameCount = orientationLockFrames;
        }
    }

    void EnsurePools(int countPerColor)
    {
        if (!greenRoot)
        {
            greenRoot = new GameObject("DownScan_HitMarkers_Green").transform;
            greenRoot.SetParent(transform, false);
            CreatePool(greenMarkers, greenRoot, countPerColor, greenColor);
        }
        if (!blueRoot)
        {
            blueRoot = new GameObject("DownScan_HitMarkers_Blue").transform;
            blueRoot.SetParent(transform, false);
            CreatePool(blueMarkers, blueRoot, countPerColor, blueColor);
        }
        if (!orangeRoot)
        {
            orangeRoot = new GameObject("DownScan_HitMarkers_Orange").transform;
            orangeRoot.SetParent(transform, false);
            CreatePool(orangeMarkers, orangeRoot, countPerColor, orangeColor);
        }
    }

    void CreatePool(List<Renderer> list, Transform root, int count, Color color)
    {
        for (int i = 0; i < count; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = go.GetComponent<Collider>(); if (col) Destroy(col);
            var mr = go.GetComponent<MeshRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (!sh) sh = Shader.Find("Unlit/Color");
            if (!sh) sh = Shader.Find("Standard");
            mr.sharedMaterial = new Material(sh) { color = color };
            go.transform.localScale = Vector3.one * hitMarkerSize;
            go.SetActive(false);
            go.transform.SetParent(root, false);
            list.Add(mr);
        }
    }

    void BeginMarkers()
    {
        activeGreen = activeBlue = activeOrange = 0;
        if (!greenRoot || !blueRoot || !orangeRoot ||
            greenMarkers.Count < poolSizePerColor ||
            blueMarkers.Count < poolSizePerColor ||
            orangeMarkers.Count < poolSizePerColor)
        {
            EnsurePools(poolSizePerColor);
        }
    }

    void PlaceGreenMarker(Vector3 p)
    {
        if (activeGreen >= greenMarkers.Count) return;
        var r = greenMarkers[activeGreen++]; var go = r.gameObject;
        go.transform.position = p; if (!go.activeSelf) go.SetActive(true);
    }

    void PlaceBlueMarker(Vector3 p)
    {
        if (activeBlue >= blueMarkers.Count) return;
        var r = blueMarkers[activeBlue++]; var go = r.gameObject;
        go.transform.position = p; if (!go.activeSelf) go.SetActive(true);
    }

    void PlaceOrangeMarker(Vector3 p)
    {
        if (activeOrange >= orangeMarkers.Count) return;
        var r = orangeMarkers[activeOrange++]; var go = r.gameObject;
        go.transform.position = p; if (!go.activeSelf) go.SetActive(true);
    }

    void EndMarkers()
    {
        for (int i = activeGreen; i < greenMarkers.Count; i++)
            if (greenMarkers[i].gameObject.activeSelf) greenMarkers[i].gameObject.SetActive(false);
        for (int i = activeBlue; i < blueMarkers.Count; i++)
            if (blueMarkers[i].gameObject.activeSelf) blueMarkers[i].gameObject.SetActive(false);
        for (int i = activeOrange; i < orangeMarkers.Count; i++)
            if (orangeMarkers[i].gameObject.activeSelf) orangeMarkers[i].gameObject.SetActive(false);
    }

    void OnDrawGizmos()
    {
        if (!showOBBEdgesGizmos || !haveCurrentOBB) return;
        Gizmos.color = obbGizmoColor;
        Quaternion r = curOBBRot;
        Vector3 c = curOBBCenter;
        Vector3 hx = r * Vector3.right * (curOBBSizeX * 0.5f);
        Vector3 hz = r * Vector3.forward * (curOBBSizeZ * 0.5f);
        Vector3 p0 = c - hx - hz;
        Vector3 p1 = c + hx - hz;
        Vector3 p2 = c + hx + hz;
        Vector3 p3 = c - hx + hz;
        Gizmos.DrawLine(p0, p1);
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p0);
    }

    // -------------------- Helpers manquants (ajoutés) --------------------
    (float min, float max) PercentileRange(List<float> values, float low, float high)
    {
        if (values == null || values.Count == 0) return (0f, 0f);
        var tmp = new List<float>(values);
        tmp.Sort();
        int n = tmp.Count;
        int i0 = Mathf.Clamp(Mathf.RoundToInt(low * (n - 1)), 0, n - 1);
        int i1 = Mathf.Clamp(Mathf.RoundToInt(high * (n - 1)), 0, n - 1);
        return (tmp[i0], tmp[i1]);
    }

    void DrawAllAsOrange()
    {
        if (!showOrangeHits) return;
        for (int i = 0; i < allHits.Count; i++)
            PlaceOrangeMarker(allHits[i] + Vector3.up * hitMarkerLift);
        EndMarkers();
    }
    // ---------------------------------------------------------------

    public bool IsOBBLocked => obbLocked;
    public Vector3 OBBCenter => curOBBCenter;
    public Vector3 OBBSize => new Vector3(curOBBSizeX, obbThickness, curOBBSizeZ);
    public Quaternion OBBRotation => curOBBRot;

    public void ForceUnlock()
    {
        obbLocked = false;
        stableCounter = 0;
        haveLastOBBFit = false;
        orientationFrameCount = 0;
        haveSmoothed = false;
        havePersistent = false;
        accumulationDone = false;
        if (useInitialAccumulation) StartAccumulation();
    }
}