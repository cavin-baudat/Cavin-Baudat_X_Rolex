using System.Collections.Generic;
using UnityEngine;

public class PieceBinsController : MonoBehaviour
{
    [Header("Refs")]
    public WatchAssemblyManager manager;
    [Tooltip("Si vide: recherche automatique dans la hierarchie.")]
    public List<PieceBin> bins = new List<PieceBin>();

    [Header("Options")]
    [Tooltip("Rafraichir aussi a intervalle regulier en plus des evenements.")]
    public bool pollEveryFrame = false;

    // Docking options
    [Header("Dock current bin near WatchRoot")]
    public bool dockCurrentBinNearWatchRoot = true;
    [Tooltip("Local offset relative to WatchRoot: x=right, y=up, z=forward.")]
    public Vector3 dockLocalOffset = new Vector3(0.18f, 0.0f, 0.0f);
    [Tooltip("Align docked bin rotation to WatchRoot rotation.")]
    public bool alignDockRotationToWatchRoot = true;
    [Tooltip("If true, updates docked pose every frame to follow WatchRoot.")]
    public bool followWhileDocked = true;

    readonly List<AssemblyPiece> tmpPieces = new List<AssemblyPiece>(256);
    float pollTimer;

    // Original transforms store
    struct OriginalPose
    {
        public Transform parent;
        public Vector3 localPos;
        public Quaternion localRot;
        public Vector3 localScale;
    }
    readonly Dictionary<PieceBin, OriginalPose> originalPoses = new Dictionary<PieceBin, OriginalPose>(32);
    PieceBin dockedBin; // currently docked bin (if any)

    void Awake()
    {
        if (!manager) manager = FindObjectOfType<WatchAssemblyManager>();
        if (bins == null || bins.Count == 0)
        {
            bins = new List<PieceBin>(GetComponentsInChildren<PieceBin>(true));
        }
        CacheOriginalPoses();
    }

    void OnEnable()
    {
        Subscribe();
        RefreshVisuals();
        // Ensure docking state is correct on enable
        if (dockCurrentBinNearWatchRoot) UpdateDockingForCurrentStep();
    }

    void OnDisable()
    {
        Unsubscribe();
        // Optional: restore docked bin on disable
        // if (dockedBin != null) RestoreBin(dockedBin);
    }

    void Update()
    {
        // Blink animation for bins
        float dt = Time.deltaTime;
        for (int i = 0; i < bins.Count; i++)
        {
            var b = bins[i];
            if (b != null) b.Tick(dt);
        }

        // Follow anchor if requested
        if (followWhileDocked && dockedBin != null && dockCurrentBinNearWatchRoot)
            UpdateDockedPoseImmediate();

        if (pollEveryFrame)
        {
            pollTimer += dt;
            if (pollTimer >= 0.2f) // 5 Hz
            {
                pollTimer = 0f;
                RefreshVisuals();
            }
        }
    }

    void Subscribe()
    {
        if (manager != null)
            manager.AssemblyStateChanged += OnAssemblyStateChanged;
    }

    void Unsubscribe()
    {
        if (manager != null)
            manager.AssemblyStateChanged -= OnAssemblyStateChanged;
    }

    void OnAssemblyStateChanged()
    {
        RefreshVisuals();
        if (dockCurrentBinNearWatchRoot)
            UpdateDockingForCurrentStep();
    }

    void CacheOriginalPoses()
    {
        originalPoses.Clear();
        if (bins == null) return;
        for (int i = 0; i < bins.Count; i++)
        {
            var bin = bins[i];
            if (bin == null) continue;
            var t = bin.transform;
            if (!originalPoses.ContainsKey(bin))
            {
                originalPoses[bin] = new OriginalPose
                {
                    parent = t.parent,
                    localPos = t.localPosition,
                    localRot = t.localRotation,
                    localScale = t.localScale
                };
            }
        }
    }

    public void RefreshVisuals()
    {
        if (manager == null || bins == null) return;

        // Get pieces from manager
        tmpPieces.Clear();
        var all = manager.AllPieces;
        if (all != null)
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null) tmpPieces.Add(all[i]);

        var cur = manager.CurrentStep;
        string curId = cur != null ? cur.stepId : null;

        for (int i = 0; i < bins.Count; i++)
        {
            var bin = bins[i];
            if (bin == null) continue;
            string binId = bin.GetStepId();
            if (string.IsNullOrEmpty(binId))
            {
                bin.SetState(PieceBin.VisualState.Normal);
                continue;
            }

            int total = 0, placed = 0;
            for (int p = 0; p < tmpPieces.Count; p++)
            {
                var piece = tmpPieces[p];
                if (MatchesStepId(piece, binId))
                {
                    total++;
                    if (piece.isPlaced) placed++;
                }
            }

            bool hasAny = total > 0;
            bool anyPlaced = placed > 0;
            bool isCurrent = !anyPlaced && hasAny && !string.IsNullOrEmpty(curId) && binId == curId;

            if (anyPlaced)
                bin.SetState(PieceBin.VisualState.Completed); // Vert si au moins 1 piece posee
            else if (isCurrent)
                bin.SetState(PieceBin.VisualState.Active);     // Bleu blink si aucune posee et etape courante
            else
                bin.SetState(PieceBin.VisualState.Normal);     // Sinon normal
        }
    }

    // Docking logic
    void UpdateDockingForCurrentStep()
    {
        if (manager == null) return;

        string currentId = manager.CurrentStep != null ? manager.CurrentStep.stepId : null;

        // Find target bin for current step
        PieceBin target = null;
        if (!string.IsNullOrEmpty(currentId))
        {
            for (int i = 0; i < bins.Count; i++)
            {
                var b = bins[i];
                if (b == null) continue;
                string id = b.GetStepId();
                if (!string.IsNullOrEmpty(id) && id == currentId)
                {
                    target = b;
                    break;
                }
            }
        }

        if (dockedBin == target)
        {
            // If following is on, keep it aligned
            if (dockedBin != null && followWhileDocked)
                UpdateDockedPoseImmediate();
            return;
        }

        // Restore previous docked bin
        if (dockedBin != null)
            RestoreBin(dockedBin);

        // Dock new one
        if (target != null)
            DockBin(target);
    }

    void DockBin(PieceBin bin)
    {
        if (bin == null) return;
        // Ensure we know its original pose
        if (!originalPoses.ContainsKey(bin))
        {
            var t = bin.transform;
            originalPoses[bin] = new OriginalPose
            {
                parent = t.parent,
                localPos = t.localPosition,
                localRot = t.localRotation,
                localScale = t.localScale
            };
        }

        dockedBin = bin;
        UpdateDockedPoseImmediate();
    }

    void RestoreBin(PieceBin bin)
    {
        if (bin == null) return;
        if (originalPoses.TryGetValue(bin, out var org))
        {
            var t = bin.transform;
            // Restore under original parent with original local TRS
            if (t.parent != org.parent) t.SetParent(org.parent, false);
            t.localPosition = org.localPos;
            t.localRotation = org.localRot;
            t.localScale = org.localScale;
        }
        dockedBin = null;
    }

    void UpdateDockedPoseImmediate()
    {
        if (dockedBin == null) return;

        Transform anchor = null;
        if (manager != null) anchor = manager.watchRoot;
        if (anchor == null) anchor = transform; // fallback

        Vector3 worldPos =
            anchor.position +
            anchor.right * dockLocalOffset.x +
            anchor.up * dockLocalOffset.y +
            anchor.forward * dockLocalOffset.z;

        var t = dockedBin.transform;
        t.position = worldPos;

        if (alignDockRotationToWatchRoot && anchor != null)
            t.rotation = anchor.rotation;
        else
        {
            // Keep original world rotation if not aligning
            if (originalPoses.TryGetValue(dockedBin, out var org))
            {
                var parentRot = org.parent != null ? org.parent.rotation : Quaternion.identity;
                t.rotation = parentRot * org.localRot;
            }
        }
    }

    bool MatchesStepId(AssemblyPiece piece, string id)
    {
        if (piece == null) return false;
        if (!string.IsNullOrEmpty(piece.stepIdOverride))
            return piece.stepIdOverride == id;
        if (piece.stepData != null && !string.IsNullOrEmpty(piece.stepData.stepId))
            return piece.stepData.stepId == id;
        return false;
    }
}