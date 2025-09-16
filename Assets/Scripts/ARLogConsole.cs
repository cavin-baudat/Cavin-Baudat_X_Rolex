/*
Project: Cavin-Baudat X Acrotec
File: ARLogConsole.cs
Summary: Console legere in-app pour afficher les logs Unity dans une TextMeshProUGUI avec limite de lignes.
Option: Ignore les repetitions consecutives et tronque la file au-dela de maxLines.
Filtrage: N'affiche que les Debug.Log (LogType.Log). Ignore warnings/erreurs/asserts/exceptions.

Author: Nicolas Vial
Company: Cavin-Baudat
Last modified: 20.08.2025

Unity: 2022.3.47f1
*/

using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Affiche les messages de log Unity dans une TextMeshProUGUI, avec memorisation limitee,
/// filtrage des repetitions consecutives et filtrage par type (Debug.Log uniquement).
/// </summary>
public class ARLogConsole : MonoBehaviour
{
    /// <summary>Zone de texte cible (TMP).</summary>
    public TextMeshProUGUI logText;

    /// <summary>Nombre maximum de lignes conservees.</summary>
    public int maxLines = 20;

    private readonly Queue<string> logLines = new Queue<string>();
    private string lastMessage = "";

    private void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        // Filtre: on ne garde que les Debug.Log
        if (type != LogType.Log)
            return;

        // Ignorer les messages repetitifs consecutifs
        if (logString == lastMessage)
            return;

        lastMessage = logString;

        if (logLines.Count >= maxLines)
            logLines.Dequeue();

        logLines.Enqueue(logString);

        if (logText != null)
            logText.text = string.Join("\n", logLines.ToArray());
    }
}