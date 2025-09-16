/*
Project: Cavin-Baudat X Acrotec
File: AimDelete.cs
Summary: Supprime l'objet vise par un XRRayInteractor si la cible (ou un parent) porte un tag autorise.
Option: Peut d'abord forcer la deselection si l'objet est en cours de grab.

Author: Nicolas Vial
Company: Cavin-Baudat
Last modified: 20.08.2025

Unity: 2022.3.47f1
*/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Supprime l'objet vise par le ray s'il correspond au tag configure.
/// A placer sur le controleur porteur du XRRayInteractor.
/// </summary>
public class AimDelete : MonoBehaviour
{
    [Header("XR")]
    [SerializeField] private XRRayInteractor rayInteractor;
    [SerializeField] private XRInteractionManager interactionManager;

    [Header("Input")]
    [SerializeField] private InputActionReference deleteAction;

    [Header("Options")]
    [Tooltip("Tag requis sur la cible (ou un de ses parents).")]
    public string deletableTag = "Deletable";
    [Tooltip("Ne supprimer que si le tag est present.")]
    public bool onlyIfHasTag = true;
    [Tooltip("Forcer la deselection avant suppression (utile si l'objet est grab).")]
    public bool cancelGrabBeforeDelete = true;
    [Tooltip("Tracer les actions dans la console.")]
    public bool logActions = true;

    private void Reset()
    {
        if (!rayInteractor) rayInteractor = GetComponent<XRRayInteractor>();
        if (!interactionManager) interactionManager = FindObjectOfType<XRInteractionManager>();
    }

    private void OnEnable()
    {
        if (deleteAction != null && deleteAction.action != null)
        {
            deleteAction.action.performed += OnDeletePerformed;
            deleteAction.action.Enable();
        }
    }

    private void OnDisable()
    {
        if (deleteAction != null && deleteAction.action != null)
        {
            deleteAction.action.performed -= OnDeletePerformed;
            deleteAction.action.Disable();
        }
    }

    private void OnDeletePerformed(InputAction.CallbackContext ctx)
    {
        if (rayInteractor == null || interactionManager == null) return;
        if (!rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit)) return;

        Transform t = hit.collider != null ? hit.collider.transform : null;
        if (t == null) return;

        GameObject target = ResolveTarget(t);
        if (target == null) return;

        if (cancelGrabBeforeDelete)
            ForceDeselectIfGrabbed(target);

        if (logActions) Debug.Log("[AimDelete] Destroy " + target.name);
        Destroy(target);
    }

    private GameObject ResolveTarget(Transform from)
    {
        // Remonte la hierarchie jusqu'au GameObject porteur du tag
        Transform current = from;
        while (current != null)
        {
            if (!onlyIfHasTag || current.CompareTag(deletableTag))
                return current.gameObject;
            current = current.parent;
        }
        return null;
    }

    private void ForceDeselectIfGrabbed(GameObject go)
    {
        var grab = go.GetComponentInParent<XRGrabInteractable>();
        if (grab == null) return;

        var interactors = new List<IXRSelectInteractor>(grab.interactorsSelecting);
        foreach (var inter in interactors)
        {
            try
            {
                interactionManager.SelectExit(inter, (IXRSelectInteractable)grab);
            }
            catch { }
        }
    }
}