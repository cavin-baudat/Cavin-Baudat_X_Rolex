using UnityEngine;

/// <summary>
/// Fournit un offset de distance (en avant de l'interactor) appliqu� uniquement lors
/// du tout premier grab. Une fois utilis�, 'consumed' passe � true et l'offset ne
/// sera plus r�appliqu� tant qu'on ne le r�initialise pas.
/// </summary>
[DisallowMultipleComponent]
public class FirstGrabOffsetFromInteractor : MonoBehaviour
{
    [Tooltip("Distance (m) devant l'interactor o� placer l'objet lors du premier grab.")]
    public float distance = 0.20f;

    [Tooltip("Devenu true apr�s la premi�re utilisation de l'offset.")]
    public bool consumed = false;

    /// <summary>Permet de r�activer l'offset pour un prochain grab.</summary>
    public void ResetOffset()
    {
        consumed = false;
    }
}