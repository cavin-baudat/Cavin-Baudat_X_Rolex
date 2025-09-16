/*
Project: Cavin-Baudat X Acrotec
File: MenuManager.cs
Summary: Gestion d'actions de menu simples (ex. Quitter l'application).
Option: Dans l'editeur, arrete le PlayMode; en build, ferme l'application.

Author: Nicolas Vial
Company: Cavin-Baudat
Last modified: 20.08.2025

Unity: 2022.3.47f1
*/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Methodes utilitaires declenchees par l'UI du menu.
/// </summary>
public class MenuManager : MonoBehaviour
{
    private void Start()
    {
    }

    private void Update()
    {
    }

    /// <summary>Ferme l'application (ou stop PlayMode dans l'editeur).</summary>
    public void QuitAppOnClick()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
    }
}