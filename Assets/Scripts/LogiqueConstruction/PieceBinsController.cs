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

    readonly List<AssemblyPiece> tmpPieces = new List<AssemblyPiece>(256);
    float pollTimer;

    void Awake()
    {
        if (!manager) manager = FindObjectOfType<WatchAssemblyManager>();
        if (bins == null || bins.Count == 0)
        {
            bins = new List<PieceBin>(GetComponentsInChildren<PieceBin>(true));
        }
    }

    void OnEnable()
    {
        Subscribe();
        RefreshVisuals();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void Update()
    {
        // Animation blink des bacs actifs
        float dt = Time.deltaTime;
        for (int i = 0; i < bins.Count; i++)
        {
            var b = bins[i];
            if (b != null) b.Tick(dt);
        }

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
            manager.AssemblyStateChanged += RefreshVisuals;
    }

    void Unsubscribe()
    {
        if (manager != null)
            manager.AssemblyStateChanged -= RefreshVisuals;
    }

    public void RefreshVisuals()
    {
        if (manager == null || bins == null) return;

        // Recupere pieces depuis le manager (plus fiable que FindObjectsOfType)
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
                bin.SetState(PieceBin.VisualState.Completed); // Vert si au moins 1 pièce posée
            else if (isCurrent)
                bin.SetState(PieceBin.VisualState.Active);     // Bleu blink si aucune posée et étape courante
            else
                bin.SetState(PieceBin.VisualState.Normal);     // Sinon normal
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