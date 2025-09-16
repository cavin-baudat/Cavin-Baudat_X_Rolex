/*
Project: Cavin-Baudat X Acrotec
File: ArucoMarkerTracker.cs
Summary: Suit des marqueurs ArUco via MagicLeap Marker Understanding et met a jour une ancre (Transform) scene.
Option: Active/desactive l'ancre selon la detection, estimation optionnelle de la taille.

Author: Nicolas Vial
Company: Cavin-Baudat
Last modified: 20.08.2025

Unity: 2022.3.47f1
*/

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using MagicLeap.OpenXR.Features.MarkerUnderstanding;

/// <summary>
/// Cree un detecteur ArUco et positionne une ancre de scene au pose du marqueur.
/// </summary>
public class ArucoMarkerTracker : MonoBehaviour
{
    [Header("Marker Tracking Settings")]
    [SerializeField] private Transform markerAnchor;
    [SerializeField] private ArucoType arucoType = ArucoType.Dictionary_6x6_250;
    [SerializeField] private float markerLengthMeters = 0.1f;
    [SerializeField] private bool estimateLength = false;

    private MagicLeapMarkerUnderstandingFeature markerFeature;
    private MarkerDetector markerDetector;

    private void Start()
    {
        markerFeature = OpenXRSettings.Instance.GetFeature<MagicLeapMarkerUnderstandingFeature>();

        if (markerFeature == null || !markerFeature.enabled)
        {
            Debug.LogError("MagicLeapMarkerUnderstandingFeature non active ou non disponible.");
            enabled = false;
            return;
        }

        if (markerAnchor == null)
        {
            Debug.LogError("Marker Anchor non assigne.");
            enabled = false;
            return;
        }
        else
        {
            markerAnchor.gameObject.SetActive(false);
        }

        CreateArucoDetector();
    }

    private void CreateArucoDetector()
    {
        var settings = new MarkerDetectorSettings
        {
            MarkerDetectorProfile = MarkerDetectorProfile.Default,
            MarkerType = MarkerType.Aruco,
            ArucoSettings = new ArucoSettings
            {
                ArucoType = arucoType,
                ArucoLength = markerLengthMeters,
                EstimateArucoLength = estimateLength
            }
        };

        markerDetector = markerFeature.CreateMarkerDetector(settings);

        if (markerDetector == null)
        {
            Debug.LogError("Echec de creation du detecteur ArUco.");
            enabled = false;
            return;
        }

        Debug.Log("Detecteur ArUco cree avec succes.");
    }

    private void Update()
    {
        if (markerDetector == null)
            return;

        markerFeature.UpdateMarkerDetectors();

        bool markerDetected = false;

        foreach (var marker in markerDetector.Data)
        {
            if (marker.MarkerPose.HasValue && markerDetector.Settings.MarkerType == MarkerType.Aruco)
            {
                Pose pose = marker.MarkerPose.Value;
                markerAnchor.position = pose.position;
                markerAnchor.rotation = pose.rotation;

                if (!markerAnchor.gameObject.activeSelf)
                    markerAnchor.gameObject.SetActive(true);

                markerDetected = true;
                break; // Gestion d'un seul marqueur
            }
        }

        if (!markerDetected && markerAnchor.gameObject.activeSelf)
        {
            markerAnchor.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (markerDetector != null)
        {
            markerFeature.DestroyMarkerDetector(markerDetector);
            markerDetector = null;
        }
    }
}