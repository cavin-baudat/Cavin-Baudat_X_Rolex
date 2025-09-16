/*
Project: Cavin-Baudat X Acrotec
File: GrabReleaseStabilizer.cs
Summary: Stabilise l'etat du Rigidbody pendant et apres le grab pour eviter flottement/impulsions indesirables.
Option: Kinematic pendant le grab et/ou apres release, remise a zero des vitesses, desactivation du throw.

Author: Nicolas Vial
Company: Cavin-Baudat
Last modified: 20.08.2025

Unity: 2022.3.47f1
*/

using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(Rigidbody))]
public class GrabReleaseStabilizer : MonoBehaviour
{
    [Header("Kinematic")]
    [Tooltip("Si true, l'objet reste kinematic pendant le grab.")]
    public bool kinematicWhileGrabbed = false;
    [Tooltip("Si true, l'objet devient kinematic apres release.")]
    public bool kinematicOnRelease = true;

    [Header("Detachement")]
    [Tooltip("Desactive l'impulsion de lancer au detachement.")]
    public bool disableThrowOnDetach = true;
    [Tooltip("Reinitialise les vitesses lineaire et angulaire au release.")]
    public bool zeroVelocityOnRelease = true;
    [Tooltip("Delai avant l'application de l'etat post-release.")]
    public float postReleaseDelay = 0.0f;

    private XRGrabInteractable grab;
    private Rigidbody rb;
    private bool originalKinematic;
    private bool originalUseGravity;

    private void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();

        originalKinematic = rb.isKinematic;
        originalUseGravity = rb.useGravity;

        if (disableThrowOnDetach)
            grab.throwOnDetach = false;
    }

    private void OnEnable()
    {
        grab.selectEntered.AddListener(OnSelectEntered);
        grab.selectExited.AddListener(OnSelectExited);
    }

    private void OnDisable()
    {
        grab.selectEntered.RemoveListener(OnSelectEntered);
        grab.selectExited.RemoveListener(OnSelectExited);
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        // Etat pendant le grab
        if (kinematicWhileGrabbed)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        else
        {
            rb.isKinematic = false;
            // En AR, on laisse souvent useGravity a false
        }
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        if (postReleaseDelay > 0f)
            StartCoroutine(ApplyReleaseStateDelayed());
        else
            ApplyReleaseState();
    }

    private IEnumerator ApplyReleaseStateDelayed()
    {
        yield return new WaitForSeconds(postReleaseDelay);
        ApplyReleaseState();
    }

    private void ApplyReleaseState()
    {
        if (zeroVelocityOnRelease)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (kinematicOnRelease)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        else
        {
            rb.isKinematic = originalKinematic;
            rb.useGravity = originalUseGravity;
        }
    }
}