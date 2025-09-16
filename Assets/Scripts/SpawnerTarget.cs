/*
Project: Cavin-Baudat X Acrotec
File: SpawnerTarget.cs
Summary: Porte les parametres du contenu a instancier lorsque vise (prefab, alignement surface, offset).
Option: Reglages d'orientation additionnelle et miniature optionnelle.

Author: Nicolas Vial
Company: Cavin-Baudat
Last modified: 20.08.2025

Unity: 2022.3.47f1
*/

using UnityEngine;

/// <summary>
/// Decrit le contenu a spawner lorsqu'il est vise/choisi.
/// </summary>
public class SpawnerTarget : MonoBehaviour
{
    [Header("Objet a instancier")]
    [Tooltip("Prefab instancie (recommande: XRGrabInteractable + Rigidbody).")]
    public GameObject prefab;

    [Header("Options de pose sur la surface")]
    [Tooltip("Decalage le long de la normale pour eviter le clipping.")]
    public float surfaceOffset = 0.01f;
    [Tooltip("Aligne l'orientation a la normale de la surface touchee.")]
    public bool alignToSurfaceNormal = true;
    [Tooltip("Ajustement Euler additionnel apres alignement.")]
    public Vector3 additionalEuler;

    [Header("Miniature (visuelle, optionnelle)")]
    public Transform miniature;
}