using UnityEngine;

/// <summary>
/// Fournit un offset de distance (en avant de l'interactor) appliqué uniquement lors
/// du tout premier grab. Une fois utilisé, 'consumed' passe à true et l'offset ne
/// sera plus réappliqué tant qu'on ne le réinitialise pas.
/// </summary>
[DisallowMultipleComponent]
public class FirstGrabOffsetFromInteractor : MonoBehaviour
{
    [Tooltip("Distance (m) devant l'interactor où placer l'objet lors du premier grab.")]
    public float distance = 0.20f;

    [Tooltip("Devenu true après la première utilisation de l'offset.")]
    public bool consumed = false;

    /// <summary>Permet de réactiver l'offset pour un prochain grab.</summary>
    public void ResetOffset()
    {
        consumed = false;
    }
}