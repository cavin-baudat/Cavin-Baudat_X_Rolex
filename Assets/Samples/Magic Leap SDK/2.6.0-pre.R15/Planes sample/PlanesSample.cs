// %BANNER_BEGIN%
// ---------------------------------------------------------------------
// %COPYRIGHT_BEGIN%
// Copyright (c) (2024) Magic Leap, Inc.
// %COPYRIGHT_END%
// ---------------------------------------------------------------------
// %BANNER_END%
using MagicLeap.Android;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using MagicLeap.OpenXR.Features.Planes;
using MagicLeap.OpenXR.Subsystems;

/// <summary>
/// Version réduite: ne gère QUE le sol.
/// (Toute la logique de table / proxy a été retirée)
/// </summary>
public class PlanesSample : MonoBehaviour
{
    // ===== Event sol =====
    public static event Action<float> FloorHeightChanged;

    [Header("Références")]
    [SerializeField] private GameObject ground;
    [SerializeField] private Transform cameraTransform;

    [Header("Query")]
    [SerializeField, Tooltip("Max planes per query")] private uint maxResults = 50;
    [SerializeField, Tooltip("Min plane area m^2")] private float minPlaneArea = 0.15f;

    [Header("Floor Stability")]
    [Tooltip("Min delta (m) avant adoption d'un nouveau candidat")]
    public float groundHeightChangeThreshold = 0.02f;
    [Tooltip("Temps de stabilité requis (s)")]
    public float groundStabilityTime = 1.0f;
    [Tooltip("Vitesse de lerp vers la hauteur cible")]
    public float groundLerpSpeed = 3f;
    [Tooltip("Verrouiller après le premier sol stable")]
    public bool lockAfterFirstStable = false;
    [Tooltip("Continuer à scanner après détection")]
    public bool keepScanningAfterFloorFound = true;

    [Header("Debug")]
    public bool debugLogs = false;

    // ===== Internes =====
    private ARPlaneManager planeManager;
    private MagicLeapPlanesFeature planeFeature;
    private Camera mainCamera;
    private bool permissionGranted;

    // Sol
    private bool floorLocked;
    private float currentGroundY;
    private float targetGroundY;
    private float candidateGroundY;
    private float candidateTimer;
    private bool hasInitializedGround;

    [Header("Nettoyage ARPlanes")]
    public bool removePlaneColliders = true;
    public string arPlaneLayerNameToIgnore = "ARPlanes"; // crée un layer optionnel et mets-le en collision OFF avec tes objets

    [Header("Filtrage sol avancé")]
    [Tooltip("Ignore toute 'détection de sol' au-dessus de ce delta par rapport au plus bas plan vu.")]
    public float maxDeltaAboveLowestForFloor = 0.05f;
    [Tooltip("Exiger d'avoir vu un plan plus bas au moins une fois avant de verrouiller")]
    public bool requireSeeingLowerBeforeLock = true;

    private float lowestPlaneEverY = float.PositiveInfinity;
    private bool sawLowerThanCandidate;

    public float GroundY => currentGroundY;

    private IEnumerator Start()
    {
        mainCamera = Camera.main;
        yield return new WaitUntil(AreSubsystemsLoaded);

        planeManager = FindObjectOfType<ARPlaneManager>();
        if (!planeManager)
        {
            Debug.LogError("[PlanesSample] ARPlaneManager non trouvé. Désactivation.");
            enabled = false;
            yield break;
        }

        planeManager.enabled = false;
        permissionGranted = false;
        Permissions.RequestPermission(Permissions.SpatialMapping, OnPermissionGranted, OnPermissionDenied);
    }

    public bool AreSubsystemsLoaded()
    {
        if (XRGeneralSettings.Instance == null ||
            XRGeneralSettings.Instance.Manager == null ||
            XRGeneralSettings.Instance.Manager.activeLoader == null)
            return false;

        return XRGeneralSettings.Instance.Manager.activeLoader.GetLoadedSubsystem<XRPlaneSubsystem>() != null;
    }

    private void OnPermissionGranted(string permission)
    {
        permissionGranted = true;
        planeManager.enabled = true;
        planeFeature = OpenXRSettings.Instance.GetFeature<MagicLeapPlanesFeature>();

        if (planeManager.requestedDetectionMode == PlaneDetectionMode.None)
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal; // Horizontal nécessaire pour sol

        if (debugLogs) Debug.Log("[PlanesSample] Permission accordée. Scan sol démarré.");
    }

    private void OnPermissionDenied(string permission)
    {
        Debug.LogError("[PlanesSample] Permission refusée. Désactivation.");
        enabled = false;
    }

    private void Update()
    {
        if (!permissionGranted || planeManager == null || !planeManager.enabled) return;

        UpdateQuery();
        EvaluateFloorCandidate();
        UpdateGroundSmoothing();
    }

    private void UpdateQuery()
    {
        if (!planeManager || !planeManager.enabled) return;

        // Horizontal + semantic floor uniquement
        var flags = MLXrPlaneSubsystem.MLPlanesQueryFlags.Horizontal |
                    MLXrPlaneSubsystem.MLPlanesQueryFlags.SemanticFloor;

        var camT = (cameraTransform != null ? cameraTransform : mainCamera.transform);

        var q = new MLXrPlaneSubsystem.PlanesQuery
        {
            Flags = flags,
            BoundsCenter = camT.position,
            BoundsRotation = camT.rotation,
            BoundsExtents = Vector3.one * 20f,
            MaxResults = maxResults,
            MinPlaneArea = minPlaneArea
        };

        MLXrPlaneSubsystem.Query = q;
    }

    private void EvaluateFloorCandidate()
    {
        if (floorLocked) return;

        ARPlane bestFloor = null;
        float lowestY = float.PositiveInfinity;

        foreach (var plane in planeManager.trackables)
        {
            if (!plane.enabled) continue;
            if (plane.alignment != PlaneAlignment.HorizontalUp) continue;

            // On choisit la plus basse (ou classification Floor si ML la marque)
            float y = plane.center.y;
            if (plane.classification == PlaneClassification.Floor)
            {
                if (bestFloor == null || y < lowestY)
                {
                    bestFloor = plane;
                    lowestY = y;
                }
            }
            else
            {
                if (bestFloor == null && y < lowestY)
                {
                    bestFloor = plane;
                    lowestY = y;
                }
            }

            lowestPlaneEverY = Mathf.Min(lowestPlaneEverY, y);
        }

        if (!bestFloor) return;
        float newCandidateY = bestFloor.center.y;

        if (!hasInitializedGround)
        {
            hasInitializedGround = true;
            currentGroundY = targetGroundY = candidateGroundY = newCandidateY;
            ApplyGroundPositionImmediate(newCandidateY);
            if (debugLogs) Debug.Log("[PlanesSample] Sol initialisé Y=" + newCandidateY.ToString("F3"));
            FloorHeightChanged?.Invoke(newCandidateY);

            if (!keepScanningAfterFloorFound)
            {
                planeManager.enabled = false;
                if (debugLogs) Debug.Log("[PlanesSample] Scan stoppé (option).");
            }
            return;
        }

        if (Mathf.Abs(newCandidateY - targetGroundY) < groundHeightChangeThreshold)
        {
            candidateTimer = 0f;
            return;
        }

        if (Mathf.Abs(newCandidateY - candidateGroundY) > 0.001f)
        {
            candidateGroundY = newCandidateY;
            candidateTimer = 0f;
            if (debugLogs) Debug.Log("[PlanesSample] Nouveau candidat sol Y=" + candidateGroundY.ToString("F3"));
        }
        else
        {
            candidateTimer += Time.deltaTime;
            if (candidateTimer >= groundStabilityTime)
            {
                if (requireSeeingLowerBeforeLock && !sawLowerThanCandidate)
                {
                    candidateTimer = 0f;
                    return;
                }

                if ((candidateGroundY - lowestPlaneEverY) > maxDeltaAboveLowestForFloor)
                {
                    candidateGroundY = targetGroundY;
                    candidateTimer = 0f;
                    if (debugLogs) Debug.Log("[PlanesSample] Rejet candidat (trop haut par rapport au plus bas).");
                    return;
                }

                targetGroundY = candidateGroundY;
                candidateTimer = 0f;
                if (debugLogs) Debug.Log("[PlanesSample] Sol adopté Y=" + targetGroundY.ToString("F3"));
                FloorHeightChanged?.Invoke(targetGroundY);

                if (lockAfterFirstStable)
                {
                    floorLocked = true;
                    if (debugLogs) Debug.Log("[PlanesSample] Sol verrouillé.");
                }
            }
        }

        if (candidateGroundY < lowestPlaneEverY - 0.01f) sawLowerThanCandidate = true;
    }

    private void UpdateGroundSmoothing()
    {
        if (!hasInitializedGround) return;
        if (!ground) return;

        float k = 1f - Mathf.Exp(-groundLerpSpeed * Time.deltaTime);
        currentGroundY = Mathf.Lerp(currentGroundY, targetGroundY, k);

        var pos = ground.transform.position;
        pos.y = currentGroundY;
        ground.transform.position = pos;
    }

    private void ApplyGroundPositionImmediate(float y)
    {
        if (!ground) return;
        var p = ground.transform.position;
        p.y = y;
        ground.transform.position = p;
        currentGroundY = targetGroundY = y;
    }

    private void OnEnable()
    {
        var pm = FindObjectOfType<ARPlaneManager>();
        if (pm) pm.planesChanged += OnPlanesChanged;
    }
    private void OnDisable()
    {
        var pm = FindObjectOfType<ARPlaneManager>();
        if (pm) pm.planesChanged -= OnPlanesChanged;
    }

    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        if (!removePlaneColliders) return;
        ApplyPlaneColliderRemoval(args.added);
        ApplyPlaneColliderRemoval(args.updated);
    }

    void ApplyPlaneColliderRemoval(System.Collections.Generic.List<ARPlane> planes)
    {
        if (planes == null) return;
        int layer = -1;
        if (!string.IsNullOrEmpty(arPlaneLayerNameToIgnore))
        {
            int l = LayerMask.NameToLayer(arPlaneLayerNameToIgnore);
            if (l >= 0) layer = l;
        }
        foreach (var p in planes)
        {
            if (!p) continue;
            var mc = p.GetComponent<MeshCollider>();
            if (mc) Destroy(mc); // supprime le collider
            // Option: aussi supprimer tout autre collider résiduel
            var cols = p.GetComponents<Collider>();
            foreach (var c in cols) Destroy(c);

            if (layer >= 0) p.gameObject.layer = layer;
        }
    }
}

// (Extension conservée si besoin futur)
public static class PlaneDetectionModeExtensionsML
{
    public static MLXrPlaneSubsystem.MLPlanesQueryFlags ToMLXrQueryFlags(this PlaneDetectionMode mode)
    {
        var flags = MLXrPlaneSubsystem.MLPlanesQueryFlags.None;
        if ((mode & PlaneDetectionMode.Horizontal) != 0)
            flags |= MLXrPlaneSubsystem.MLPlanesQueryFlags.Horizontal;
        if ((mode & PlaneDetectionMode.Vertical) != 0)
            flags |= MLXrPlaneSubsystem.MLPlanesQueryFlags.Vertical;
        return flags;
    }
}