using UnityEngine;

[CreateAssetMenu(fileName = "WatchAssemblyStep", menuName = "Watch/Assembly Step", order = 10)]
public class WatchAssemblyStep : ScriptableObject
{
    [Header("Identification")]
    public string stepId;
    [Tooltip("Ordre (0-based) si tu veux debugger. L ordre final est celui dans la sequence.")]
    public int debugOrderIndex;

    [Header("Piece prefab physique (instanciee par ailleurs)")]
    [Tooltip("Reference non obligatoire si la piece existe deja dans la scene (ex: spawn par TableObjectPlacer).")]
    public GameObject physicalPrefab;

    [Header("Ghost (si null => reutilise le mesh de la piece avec material ghost)")]
    public GameObject ghostPrefab;
    public Material ghostMaterial;
    public Material ghostValidMaterial;

    [Header("Pose cible (locale au WatchRoot)")]
    public Vector3 targetLocalPosition;
    public Vector3 targetLocalEuler;

    [Header("Tolerances")]
    [Tooltip("Distance max (m) pour valider placement.")]
    public float positionTolerance = 0.004f;
    [Tooltip("Angle max (deg) quaternion.Angle sur la rotation retenue.")]
    public float rotationToleranceDeg = 6f;
    [Tooltip("Nombre de frames consecutifs dans la tolerance avant etat valide.")]
    public int stableFramesRequired = 6;

    [Header("Orientations alternatives")]
    [Tooltip("Liste d orientations additionnelles acceptees (Eulers ajoutes a la rotation cible de base).")]
    public Vector3[] alternativeEulerOffsets;

    [Header("Axes libres (optionnel)")]
    public bool freeYaw;
    public bool freePitch;
    public bool freeRoll;

    [Header("Options")]
    [Tooltip("Si vrai: peut etre retiree (undo) apres pose (par defaut vrai).")]
    public bool allowUndo = true;

    public Quaternion GetBaseTargetLocalRotation()
    {
        return Quaternion.Euler(targetLocalEuler);
    }

    public void BuildAcceptedRotations(System.Collections.Generic.List<Quaternion> buffer)
    {
        buffer.Clear();
        var baseQ = GetBaseTargetLocalRotation();
        buffer.Add(baseQ);
        if (alternativeEulerOffsets != null)
        {
            for (int i = 0; i < alternativeEulerOffsets.Length; i++)
                buffer.Add(baseQ * Quaternion.Euler(alternativeEulerOffsets[i]));
        }
    }
}