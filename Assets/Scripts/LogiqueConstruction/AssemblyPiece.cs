using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(XRGrabInteractable))]
public class AssemblyPiece : MonoBehaviour
{
    [Header("Config")]
    public WatchAssemblyStep stepData;
    public string stepIdOverride;

    [Header("Runtime State")]
    public bool isPlaced;
    public bool isCurrentTarget;
    public bool wasEverPlaced;          // Devient vrai après première pose (sert à différencier une pièce jamais posée d'une pièce undo)
    public bool isCurrentlyHiddenUndo;  // True si masquée (layer undo) pour faciliter chaîne d'undo

    XRGrabInteractable grab;
    WatchAssemblyManager manager;
    Rigidbody rb;
    Collider[] cols;

    InteractionLayerMask originalInteractionLayers;
    bool originalInteractionCaptured;

    // Cache pour layers
    bool layerCacheCaptured;
    readonly List<Transform> layerCache = new List<Transform>(32);
    readonly List<int> originalLayers = new List<int>(32);

    void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();
        cols = GetComponentsInChildren<Collider>();
        if (grab && !originalInteractionCaptured)
        {
            originalInteractionLayers = grab.interactionLayers;
            originalInteractionCaptured = true;
        }
    }

    void OnEnable()
    {
        if (grab != null)
        {
            grab.selectEntered.AddListener(OnSelectEntered);
            grab.selectExited.AddListener(OnSelectExited);
        }
    }

    void OnDisable()
    {
        if (grab != null)
        {
            grab.selectEntered.RemoveListener(OnSelectEntered);
            grab.selectExited.RemoveListener(OnSelectExited);
        }
    }

    public void RegisterManager(WatchAssemblyManager m) => manager = m;

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (manager) manager.NotifyPieceGrabbed(this);
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (manager) manager.NotifyPieceReleased(this);
    }

    public void SetPhysicsPlaced(bool placed)
    {
        if (!rb) return;
        rb.isKinematic = placed;
        rb.useGravity = !placed;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    public void SetGrabEnabled(bool value)
    {
        if (!grab) return;
        grab.interactionLayers = value ? originalInteractionLayers : (InteractionLayerMask)0;
    }

    public bool MatchesStep(WatchAssemblyStep step)
    {
        if (!step) return false;
        if (stepData == step) return true;
        if (!string.IsNullOrEmpty(stepIdOverride) && step.stepId == stepIdOverride) return true;
        if (stepData && !string.IsNullOrEmpty(stepData.stepId) && step.stepId == stepData.stepId) return true;
        return false;
    }

    public string GetDebugName()
    {
        if (stepData) return stepData.stepId;
        if (!string.IsNullOrEmpty(stepIdOverride)) return stepIdOverride;
        return name;
    }

    void CaptureLayerCache()
    {
        if (layerCacheCaptured) return;
        layerCache.Clear();
        originalLayers.Clear();
        GetComponentsInChildren<Transform>(true, layerCache);
        for (int i = 0; i < layerCache.Count; i++)
            originalLayers.Add(layerCache[i].gameObject.layer);
        layerCacheCaptured = true;
    }

    void ApplyLayer(int layerIndex)
    {
        if (!layerCacheCaptured) CaptureLayerCache();
        if (layerIndex < 0) return;
        for (int i = 0; i < layerCache.Count; i++)
            layerCache[i].gameObject.layer = layerIndex;
    }

    void RestoreOriginalLayers()
    {
        if (!layerCacheCaptured) return;
        for (int i = 0; i < layerCache.Count && i < originalLayers.Count; i++)
            layerCache[i].gameObject.layer = originalLayers[i];
    }

    public void SetRaycastLockedPlaced(bool lockIt, int lockedLayer)
    {
        // Masquage "placed mais pas undoable"
        if (lockIt && lockedLayer >= 0)
            ApplyLayer(lockedLayer);
        else if (!isCurrentlyHiddenUndo) // Ne restaure pas si la pièce est en mode undo hidden
            RestoreOriginalLayers();
    }

    public void SetUndoHidden(bool hide, int undoLayer, bool disableColliders)
    {
        if (hide)
        {
            if (undoLayer >= 0) ApplyLayer(undoLayer);
            if (disableColliders && cols != null)
                for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;
            isCurrentlyHiddenUndo = true;
        }
        else
        {
            RestoreOriginalLayers();
            if (cols != null)
                for (int i = 0; i < cols.Length; i++) cols[i].enabled = true;
            isCurrentlyHiddenUndo = false;
        }
    }

    public void EnsureColliders(bool enable)
    {
        if (cols == null) return;
        for (int i = 0; i < cols.Length; i++) cols[i].enabled = enable;
    }
}