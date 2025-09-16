using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class WatchAssemblyManager : MonoBehaviour
{
    [Header("Sequence")]
    public WatchAssemblySequence sequence;
    public Transform watchRoot;

    [Header("Hierarchy")]
    [Tooltip("Parent hiérarchique des pièces placées. Si null, fallback sur watchRoot.")]
    public Transform watchParent;

    [Header("Pieces discovery")]
    public bool autoDiscoverPiecesOnStart = true;
    public bool includeInactiveInDiscovery = false;

    [Header("Ghost feedback")]
    public float ghostBlinkFrequency = 1.5f;
    public float ghostAlphaMin = 0.15f;
    public float ghostAlphaMax = 0.55f;
    public float ghostScale = 1f;
    public float snapLerpDuration = 0.12f;

    [Header("Ghost Layering")]
    [Tooltip("Si vrai: force tous les ghosts à être placés sur le layer indiqué.")]
    public bool overrideGhostLayer = false;
    [Tooltip("Nom du layer à appliquer aux ghosts (si override activé).")]
    public string ghostLayerName = "AssemblyGhost";

    [Header("Bad piece feedback")]
    public Color wrongGrabColor = new Color(1f, 0.3f, 0.3f, 1f);
    public float wrongFlashDuration = 0.25f;

    [Header("Ray Filtering (placed non-undo)")]
    public bool hideNonUndoPlacedFromRay = true;
    public string lockedRayLayerName = "AssemblyLocked";

    [Header("Disassembly chain comfort")]
    [Tooltip("Masquer (layer) les pieces deja undo pour pouvoir facilement grab la precedente.")]
    public bool hideUndonePiecesFromRay = true;
    public string undoneRayLayerName = "AssemblyUndone";
    [Tooltip("Desactiver aussi leurs colliders pendant qu elles sont undo.")]
    public bool disableUndoneColliders = true;

    [Header("Debug")]
    public bool logEvents = true;
    public bool drawTargetGizmos = true;
    public Color targetGizmoColor = new Color(0f, 0.8f, 0.4f, 0.8f);

    [Header("Interaction libre")]
    [Tooltip("Si vrai: toutes les pieces non placees (et non cachees undo) sont toujours grabbables, pas seulement celle de l etape courante.")]
    public bool freeGrabAllNonPlacedPieces = true;

    readonly List<AssemblyPiece> allPieces = new List<AssemblyPiece>();
    readonly List<AssemblyPiece> placedStack = new List<AssemblyPiece>();

    int currentStepIndex;
    AssemblyPiece grabbedPiece;
    GameObject activeGhost;
    Material ghostRuntimeMaterial;
    WatchAssemblyStep currentStep;
    bool currentValid;
    int currentStableFrames;

    float wrongFlashTimer;
    struct RendererOriginalSet { public Renderer renderer; public Material[] originalShared; }
    readonly List<RendererOriginalSet> wrongFlashOriginals = new List<RendererOriginalSet>();

    readonly List<Quaternion> acceptedRotations = new List<Quaternion>();
    Vector3 snapStartPos;
    Quaternion snapStartRot;
    float snapTimer;
    bool snapping;

    static readonly int ColorProp = Shader.PropertyToID("_Color");

    int lockedRayLayer = -1;
    int undoneRayLayer = -1;
    int ghostLayer = -1;

    // === Nouveaux acces / evenement pour les bacs ===
    public System.Action AssemblyStateChanged;
    public IReadOnlyList<AssemblyPiece> AllPieces => allPieces;
    public WatchAssemblyStep CurrentStep => currentStep;
    void NotifyAssemblyStateChanged() { AssemblyStateChanged?.Invoke(); }

    // Helper: ancre de référence pour le calcul des poses (watchParent si dispo sinon watchRoot)
    Transform GetAnchor() => watchParent != null ? watchParent : watchRoot;

    void Start()
    {
        if (!watchRoot)
        {
            Debug.LogError("[WatchAssemblyManager] WatchRoot manquant.");
            enabled = false;
            return;
        }
        if (!watchParent) watchParent = watchRoot;

        if (hideNonUndoPlacedFromRay)
        {
            lockedRayLayer = LayerMask.NameToLayer(lockedRayLayerName);
            if (lockedRayLayer < 0)
                Debug.LogWarning("[WatchAssemblyManager] Layer '" + lockedRayLayerName + "' introuvable.");
        }
        if (hideUndonePiecesFromRay)
        {
            undoneRayLayer = LayerMask.NameToLayer(undoneRayLayerName);
            if (undoneRayLayer < 0)
                Debug.LogWarning("[WatchAssemblyManager] Layer '" + undoneRayLayerName + "' introuvable.");
        }
        if (overrideGhostLayer)
        {
            ghostLayer = LayerMask.NameToLayer(ghostLayerName);
            if (ghostLayer < 0)
                Debug.LogWarning("[WatchAssemblyManager] Layer '" + ghostLayerName + "' introuvable pour ghosts.");
        }

        if (autoDiscoverPiecesOnStart)
            DiscoverPieces();

        PrepareStep(0);
        UpdateGrabbabilityAndLayers();

        // Harmoniser le parent des pieces deja marquees comme placees
        EnforcePlacedPiecesParent();

        NotifyAssemblyStateChanged();
    }

    public void DiscoverPieces()
    {
        allPieces.Clear();
        var pieces = FindObjectsOfType<AssemblyPiece>(includeInactiveInDiscovery);
        for (int i = 0; i < pieces.Length; i++)
        {
            pieces[i].RegisterManager(this);
            allPieces.Add(pieces[i]);
        }
        if (logEvents) Debug.Log("[WatchAssemblyManager] DiscoverPieces -> " + allPieces.Count);
        UpdateGrabbabilityAndLayers();
        // Harmonise aussi après une découverte
        EnforcePlacedPiecesParent();
        NotifyAssemblyStateChanged();
    }

    void PrepareStep(int index)
    {
        CleanupGhost();
        currentValid = false;
        currentStableFrames = 0;
        snapping = false;

        currentStepIndex = index;
        currentStep = (sequence != null) ? sequence.GetStep(index) : null;

        if (currentStep == null)
        {
            if (logEvents) Debug.Log("[WatchAssemblyManager] Fin sequence.");
            NotifyAssemblyStateChanged();
            return;
        }

        currentStep.BuildAcceptedRotations(acceptedRotations);

        // Si la piece de ce step a ete precedemment placee puis undo et est masquee, on la reactive
        if (hideUndonePiecesFromRay)
        {
            var undone = FindUndonePieceForStep(currentStep);
            if (undone != null && undone.isCurrentlyHiddenUndo)
            {
                undone.SetUndoHidden(false, undoneRayLayer, disableUndoneColliders);
                // Toujours grabbable après undo
                undone.SetGrabEnabled(true);
                // S assurer que la physique est bien redevenue dynamique
                if (!undone.isPlaced)
                    undone.SetPhysicsPlaced(false);
            }
            else if (undone != null && !undone.isPlaced)
            {
                // Cas: piece undo deja visible -> garantir physique dynamique
                undone.SetPhysicsPlaced(false);
            }
        }

        if (logEvents) Debug.Log("[WatchAssemblyManager] Step " + index + " -> " + (currentStep != null ? currentStep.stepId : "null"));
        UpdateGrabbabilityAndLayers();
        NotifyAssemblyStateChanged();
    }

    AssemblyPiece FindUndonePieceForStep(WatchAssemblyStep step)
    {
        for (int i = 0; i < allPieces.Count; i++)
        {
            var p = allPieces[i];
            if (!p) continue;
            if (!p.isPlaced && p.wasEverPlaced && p.MatchesStep(step))
                return p;
        }
        return null;
    }

    public void NotifyPieceGrabbed(AssemblyPiece piece)
    {
        if (piece == null) return;

        // Undo chain via grab de la derniere piece encore en place
        if (piece.isPlaced && placedStack.Count > 0 && piece == placedStack[placedStack.Count - 1])
        {
            var step = ResolveStep(piece);
            if (step != null && step.allowUndo)
            {
                if (logEvents) Debug.Log("[WatchAssemblyManager] Undo via grab piece " + piece.GetDebugName());
                placedStack.RemoveAt(placedStack.Count - 1);
                piece.isPlaced = false;
                piece.SetPhysicsPlaced(false);

                // Etat undo -> masquer si option
                piece.wasEverPlaced = true;
                if (hideUndonePiecesFromRay)
                    piece.SetUndoHidden(true, undoneRayLayer, disableUndoneColliders);
                else
                    piece.SetUndoHidden(false, undoneRayLayer, disableUndoneColliders);

                int idx = sequence.IndexOf(step);
                // Préparer re-assemblage éventuel si on veut remonter plus loin:
                PrepareStep(idx); // PrepareStep emettra l'evenement
                grabbedPiece = piece; // Peut la re-poser tout de suite si désiré
                SetupGhostForCurrentStep();
                return;
            }
        }

        if (currentStep == null) return;

        if (!piece.MatchesStep(currentStep))
        {
            FlashWrongPiece(piece);
            return;
        }

        grabbedPiece = piece;

        // Si elle etait masquee en mode undo, on la remet visible / colliders
        if (piece.isCurrentlyHiddenUndo)
            piece.SetUndoHidden(false, undoneRayLayer, disableUndoneColliders);

        SetupGhostForCurrentStep();
    }

    WatchAssemblyStep ResolveStep(AssemblyPiece piece)
    {
        if (piece.stepData) return piece.stepData;
        if (sequence == null) return null;
        for (int i = 0; i < sequence.StepCount; i++)
        {
            var st = sequence.GetStep(i);
            if (piece.MatchesStep(st)) return st;
        }
        return null;
    }

    public void NotifyPieceReleased(AssemblyPiece piece)
    {
        if (piece != grabbedPiece) return;
        if (snapping) return;

        if (currentValid)
            DoFinalSnap();
        else
        {
            // Pas valide => on s assure que la piece redevient dynamique (gravity ON)
            if (grabbedPiece != null && !grabbedPiece.isPlaced)
                grabbedPiece.SetPhysicsPlaced(false);
            CleanupGhost();
            grabbedPiece = null;
            // Etat global pas modifie => pas d'evenement necessaire
        }
    }

    void Update()
    {
        UpdateWrongFlash();
        UpdateGhostBlink();
        if (grabbedPiece != null && activeGhost != null && currentStep != null && !snapping)
            EvaluatePlacement();
        if (snapping)
            UpdateSnapping();
    }

    // --- Wrong piece flash ---
    void FlashWrongPiece(AssemblyPiece piece)
    {
        ClearWrongFlash();
        var renderers = piece.GetComponentsInChildren<MeshRenderer>();
        if (renderers == null || renderers.Length == 0) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (!r) continue;
            var orig = r.sharedMaterials;
            var rec = new RendererOriginalSet { renderer = r, originalShared = orig != null ? (Material[])orig.Clone() : null };
            wrongFlashOriginals.Add(rec);

            if (orig != null && orig.Length > 0)
            {
                var instArray = new Material[orig.Length];
                for (int m = 0; m < orig.Length; m++)
                {
                    var src = orig[m];
                    var inst = src ? new Material(src) : new Material(Shader.Find("Standard"));
                    if (inst.HasProperty(ColorProp)) inst.color = wrongGrabColor;
                    instArray[m] = inst;
                }
                r.materials = instArray;
            }
            else
            {
                var sh = Shader.Find("Standard");
                var inst = new Material(sh) { color = wrongGrabColor };
                r.material = inst;
            }
        }
        wrongFlashTimer = wrongFlashDuration;
    }

    void ClearWrongFlash()
    {
        if (wrongFlashOriginals.Count == 0) return;
        for (int i = 0; i < wrongFlashOriginals.Count; i++)
        {
            var rec = wrongFlashOriginals[i];
            if (rec.renderer && rec.originalShared != null)
                rec.renderer.sharedMaterials = rec.originalShared;
        }
        wrongFlashOriginals.Clear();
        wrongFlashTimer = 0f;
    }

    void UpdateWrongFlash()
    {
        if (wrongFlashTimer > 0f)
        {
            wrongFlashTimer -= Time.deltaTime;
            if (wrongFlashTimer <= 0f) ClearWrongFlash();
        }
    }

    // --- Ghost ---
    void SetupGhostForCurrentStep()
    {
        CleanupGhost();
        if (currentStep == null) return;

        Transform parent = GetAnchor();
        Vector3 localPos = currentStep.targetLocalPosition;
        Quaternion localRot = currentStep.GetBaseTargetLocalRotation();

        if (currentStep.ghostPrefab)
            activeGhost = Instantiate(currentStep.ghostPrefab, parent);
        else if (grabbedPiece != null)
        {
            activeGhost = new GameObject("Ghost_" + currentStep.stepId);
            activeGhost.transform.SetParent(parent, false);
            CopyRenderable(grabbedPiece.gameObject, activeGhost);
        }

        if (!activeGhost) return;
        activeGhost.transform.localPosition = localPos;
        activeGhost.transform.localRotation = localRot;
        activeGhost.transform.localScale = Vector3.one * ghostScale;

        // Appliquer éventuellement le layer dédié aux ghosts
        ApplyGhostLayerIfRequested();

        if (currentStep.ghostMaterial)
            ApplyUniformGhostMaterialToHierarchy(activeGhost, currentStep.ghostMaterial, out ghostRuntimeMaterial);
        else
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (!sh) sh = Shader.Find("Standard");
            var temp = new Material(sh) { color = new Color(0.9f, 0.9f, 1f, ghostAlphaMax) };
            ApplyUniformGhostMaterialToHierarchy(activeGhost, temp, out ghostRuntimeMaterial);
        }
    }

    void CopyRenderable(GameObject source, GameObject target)
    {
        var rens = source.GetComponentsInChildren<MeshRenderer>();
        for (int i = 0; i < rens.Length; i++)
        {
            var r = rens[i];
            var mf = r.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) continue;

            var child = new GameObject("GhostPart_" + r.gameObject.name);
            child.transform.SetParent(target.transform, false);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;

            var cmf = child.AddComponent<MeshFilter>();
            cmf.sharedMesh = mf.sharedMesh;
            var cmr = child.AddComponent<MeshRenderer>();

            var src = r.sharedMaterials;
            if (src != null && src.Length > 0)
            {
                var arr = new Material[src.Length];
                for (int m = 0; m < src.Length; m++) arr[m] = src[m];
                cmr.sharedMaterials = arr;
            }
            else
                cmr.sharedMaterial = r.sharedMaterial;
        }
    }

    void ApplyUniformGhostMaterialToHierarchy(GameObject root, Material template, out Material runtimeMat)
    {
        runtimeMat = null;
        var rens = root.GetComponentsInChildren<MeshRenderer>();
        for (int i = 0; i < rens.Length; i++)
        {
            var r = rens[i];
            int slotCount = r.sharedMaterials != null && r.sharedMaterials.Length > 0
                ? r.sharedMaterials.Length
                : (r.GetComponent<MeshFilter>() && r.GetComponent<MeshFilter>().sharedMesh != null
                    ? r.GetComponent<MeshFilter>().sharedMesh.subMeshCount
                    : 1);
            if (slotCount <= 0) slotCount = 1;
            if (runtimeMat == null) runtimeMat = new Material(template);
            var arr = new Material[slotCount];
            for (int s = 0; s < slotCount; s++) arr[s] = runtimeMat;
            r.sharedMaterials = arr;
        }
    }

    void CleanupGhost()
    {
        if (activeGhost) Destroy(activeGhost);
        activeGhost = null;
        ghostRuntimeMaterial = null;
        currentValid = false;
        currentStableFrames = 0;
    }

    void UpdateGhostBlink()
    {
        if (!activeGhost || ghostRuntimeMaterial == null || currentValid) return;
        if (ghostRuntimeMaterial.HasProperty(ColorProp))
        {
            float t = Time.time * ghostBlinkFrequency;
            float k = 0.5f * (1f + Mathf.Sin(t * Mathf.PI * 2f));
            float a = Mathf.Lerp(ghostAlphaMin, ghostAlphaMax, k);
            var c = ghostRuntimeMaterial.color;
            c.a = a;
            ghostRuntimeMaterial.color = c;
        }
    }

    // --- Placement evaluation ---
    void EvaluatePlacement()
    {
        if (grabbedPiece == null || currentStep == null) return;

        var anchor = GetAnchor();

        Vector3 targetWorldPos = anchor.TransformPoint(currentStep.targetLocalPosition);
        Quaternion targetWorldRotBase = anchor.rotation * currentStep.GetBaseTargetLocalRotation();

        Vector3 piecePos = grabbedPiece.transform.position;
        Quaternion pieceRot = grabbedPiece.transform.rotation;

        float posErr = Vector3.Distance(piecePos, targetWorldPos);
        float bestAngle = 9999f;

        for (int i = 0; i < acceptedRotations.Count; i++)
        {
            Quaternion candidate = anchor.rotation * acceptedRotations[i];
            float ang = RotationErrorConsideringFreeAxes(candidate, pieceRot, currentStep);
            if (ang < bestAngle) bestAngle = ang;
        }

        bool okPos = posErr <= currentStep.positionTolerance;
        bool okRot = bestAngle <= currentStep.rotationToleranceDeg;

        if (okPos && okRot)
        {
            currentStableFrames++;
            if (currentStableFrames >= currentStep.stableFramesRequired && !currentValid)
            {
                currentValid = true;
                BecomeGhostValid();
                if (logEvents) Debug.Log("[WatchAssemblyManager] Piece valide " + currentStep.stepId);
            }
        }
        else
        {
            currentStableFrames = 0;
            if (currentValid)
            {
                currentValid = false;
                RevertGhostToInvalid();
            }
        }
    }

    float RotationErrorConsideringFreeAxes(Quaternion target, Quaternion current, WatchAssemblyStep step)
    {
        Quaternion delta = Quaternion.Inverse(target) * current;
        delta = NormalizeQuaternion(delta);

        if (step.freeYaw) delta = RemoveTwistQuaternion(delta, Vector3.up);
        if (step.freePitch) delta = RemoveTwistQuaternion(delta, Vector3.right);
        if (step.freeRoll) delta = RemoveTwistQuaternion(delta, Vector3.forward);

        return Quaternion.Angle(Quaternion.identity, delta);
    }

    Quaternion RemoveTwistQuaternion(Quaternion q, Vector3 axis)
    {
        axis = axis.normalized;
        Vector3 v = new Vector3(q.x, q.y, q.z);
        Vector3 proj = Vector3.Project(v, axis);
        Quaternion twist = new Quaternion(proj.x, proj.y, proj.z, q.w);
        float magSq = twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + q.w * q.w;
        if (magSq < 1e-12f) return q;
        twist = NormalizeQuaternion(twist);
        Quaternion swing = q * Quaternion.Inverse(twist);
        return NormalizeQuaternion(swing);
    }

    Quaternion NormalizeQuaternion(Quaternion q)
    {
        float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        if (mag > 1e-8f)
        {
            float inv = 1f / mag;
            q.x *= inv; q.y *= inv; q.z *= inv; q.w *= inv;
        }
        else q = Quaternion.identity;
        if (q.w < 0f) { q.x = -q.x; q.y = -q.y; q.z = -q.z; q.w = -q.w; }
        return q;
    }

    void BecomeGhostValid()
    {
        if (currentStep != null && currentStep.ghostValidMaterial != null)
            ApplyUniformGhostMaterialToHierarchy(activeGhost, currentStep.ghostValidMaterial, out ghostRuntimeMaterial);
        else if (ghostRuntimeMaterial != null && ghostRuntimeMaterial.HasProperty(ColorProp))
        {
            var c = ghostRuntimeMaterial.color;
            c = new Color(0.2f, 1f, 0.2f, Mathf.Max(c.a, 0.65f));
            ghostRuntimeMaterial.color = c;
        }
    }

    void RevertGhostToInvalid()
    {
        if (currentStep != null && currentStep.ghostMaterial != null)
            ApplyUniformGhostMaterialToHierarchy(activeGhost, currentStep.ghostMaterial, out ghostRuntimeMaterial);
        else if (ghostRuntimeMaterial != null && ghostRuntimeMaterial.HasProperty(ColorProp))
        {
            var c = ghostRuntimeMaterial.color;
            c = new Color(0.9f, 0.9f, 1f, ghostAlphaMax);
            ghostRuntimeMaterial.color = c;
        }
    }

    void DoFinalSnap()
    {
        if (grabbedPiece == null || currentStep == null) return;
        snapStartPos = grabbedPiece.transform.position;
        snapStartRot = grabbedPiece.transform.rotation;
        snapping = true;
        snapTimer = 0f;

        grabbedPiece.isPlaced = true;
        grabbedPiece.wasEverPlaced = true;
        if (!placedStack.Contains(grabbedPiece))
            placedStack.Add(grabbedPiece);

        // S assurer qu elle n est pas en mode undo hidden
        if (grabbedPiece.isCurrentlyHiddenUndo)
            grabbedPiece.SetUndoHidden(false, undoneRayLayer, disableUndoneColliders);

        UpdateGrabbabilityAndLayers();
        NotifyAssemblyStateChanged();
    }

    void UpdateSnapping()
    {
        if (!snapping || grabbedPiece == null || currentStep == null) return;
        snapTimer += Time.deltaTime;
        float t = (snapLerpDuration <= 0f) ? 1f : Mathf.Clamp01(snapTimer / snapLerpDuration);

        var anchor = GetAnchor();

        Vector3 targetPos = anchor.TransformPoint(currentStep.targetLocalPosition);

        Quaternion finalRot = anchor.rotation * currentStep.GetBaseTargetLocalRotation();
        float bestAngle = 9999f;
        for (int i = 0; i < acceptedRotations.Count; i++)
        {
            Quaternion cand = anchor.rotation * acceptedRotations[i];
            float ang = RotationErrorConsideringFreeAxes(cand, grabbedPiece.transform.rotation, currentStep);
            if (ang < bestAngle)
            {
                bestAngle = ang;
                finalRot = cand;
            }
        }

        grabbedPiece.transform.position = Vector3.Lerp(snapStartPos, targetPos, t);
        grabbedPiece.transform.rotation = Quaternion.Slerp(snapStartRot, finalRot, t);

        if (t >= 1f)
        {
            snapping = false;

            // Reparent sous watchParent en conservant la pose monde
            if (watchParent) grabbedPiece.transform.SetParent(watchParent, true);
            else grabbedPiece.transform.SetParent(watchRoot, true);

            grabbedPiece.SetPhysicsPlaced(true);
            CleanupGhost();
            AdvanceToNextStep();
            grabbedPiece = null;
        }
    }

    void AdvanceToNextStep()
    {
        int next = currentStepIndex + 1;
        if (sequence != null && next < sequence.StepCount)
            PrepareStep(next);
        else
        {
            currentStep = null;
            if (logEvents) Debug.Log("[WatchAssemblyManager] Assemblage complet.");
            NotifyAssemblyStateChanged();
        }
    }

    void UpdateGrabbabilityAndLayers()
    {
        for (int i = 0; i < allPieces.Count; i++)
        {
            var p = allPieces[i];
            if (!p) continue;

            if (!p.isPlaced)
            {
                // Non placée:
                if (p.isCurrentlyHiddenUndo)
                {
                    // Masquée en mode undo => non grabbable
                    p.SetGrabEnabled(false);
                }
                else
                {
                    if (freeGrabAllNonPlacedPieces)
                        p.SetGrabEnabled(true);
                    else
                    {
                        bool isCurrentStepPiece = currentStep != null && p.MatchesStep(currentStep);
                        p.SetGrabEnabled(isCurrentStepPiece);
                    }
                }
                // Pas de lock layer "placed"
                if (hideNonUndoPlacedFromRay && lockedRayLayer >= 0)
                    p.SetRaycastLockedPlaced(false, lockedRayLayer);
                continue;
            }

            bool isLastPlaced = (placedStack.Count > 0 && p == placedStack[placedStack.Count - 1]);
            bool allowUndo = false;
            var st = ResolveStep(p);
            if (st != null) allowUndo = st.allowUndo;

            p.SetGrabEnabled(isLastPlaced && allowUndo);

            if (hideNonUndoPlacedFromRay && lockedRayLayer >= 0)
            {
                bool lockIt = !(isLastPlaced && allowUndo);
                p.SetRaycastLockedPlaced(lockIt, lockedRayLayer);
            }
        }
    }

    void EnforcePlacedPiecesParent()
    {
        var parentToUse = watchParent ? watchParent : watchRoot;
        if (!parentToUse) return;

        for (int i = 0; i < allPieces.Count; i++)
        {
            var p = allPieces[i];
            if (!p || !p.isPlaced) continue;
            if (p.transform.parent != parentToUse)
                p.transform.SetParent(parentToUse, true);
        }
    }

    public bool CanUndoLast()
    {
        if (placedStack.Count == 0) return false;
        var lastPiece = placedStack[placedStack.Count - 1];
        var st = ResolveStep(lastPiece);
        return st != null && st.allowUndo;
    }

    public void ForceUndoLast()
    {
        if (!CanUndoLast()) return;
        var piece = placedStack[placedStack.Count - 1];
        placedStack.RemoveAt(placedStack.Count - 1);
        piece.isPlaced = false;
        piece.SetPhysicsPlaced(false);
        piece.wasEverPlaced = true;

        if (hideUndonePiecesFromRay)
            piece.SetUndoHidden(true, undoneRayLayer, disableUndoneColliders);

        int idx = sequence.IndexOf(ResolveStep(piece));
        PrepareStep(idx >= 0 ? idx : 0);
        UpdateGrabbabilityAndLayers();
        NotifyAssemblyStateChanged();
    }

    // --- Gizmos ---
    void OnDrawGizmos()
    {
        if (!drawTargetGizmos) return;
        var anchor = GetAnchor();
        if (sequence == null || anchor == null) return;
        Gizmos.color = targetGizmoColor;
        for (int i = 0; i < sequence.StepCount; i++)
        {
            var s = sequence.GetStep(i);
            if (s == null) continue;
            Vector3 wp = anchor.TransformPoint(s.targetLocalPosition);
            Gizmos.DrawSphere(wp, 0.0035f);
            Quaternion wr = anchor.rotation * s.GetBaseTargetLocalRotation();
            Vector3 f = wr * Vector3.forward * 0.02f;
            Gizmos.DrawLine(wp, wp + f);
        }
    }

    // --- Ghost Layer helpers ---
    [ContextMenu("Refresh Ghost Layer Now")]
    public void RefreshGhostLayerNow()
    {
        // Re-résoudre l'index du layer si nécessaire
        ghostLayer = LayerMask.NameToLayer(ghostLayerName);
        if (ghostLayer < 0)
        {
            Debug.LogWarning("[WatchAssemblyManager] Layer '" + ghostLayerName + "' introuvable pour ghosts.");
            return;
        }
        ApplyGhostLayerIfRequested();
    }

    void ApplyGhostLayerIfRequested()
    {
        if (!activeGhost) return;
        if (!overrideGhostLayer) return;
        if (ghostLayer < 0)
        {
            // Tenter une résolution paresseuse si l'index n'est pas encore connu
            ghostLayer = LayerMask.NameToLayer(ghostLayerName);
            if (ghostLayer < 0) return;
        }
        SetLayerRecursively(activeGhost.transform, ghostLayer);
    }

    void SetLayerRecursively(Transform root, int layerIndex)
    {
        if (!root) return;
        var stack = new Stack<Transform>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            t.gameObject.layer = layerIndex;
            for (int i = 0; i < t.childCount; i++)
                stack.Push(t.GetChild(i));
        }
    }
}