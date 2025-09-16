/*
Project: Cavin-Baudat X Acrotec
File: NetworkLauncher.cs
Summary: Demarre un NetworkRunner Fusion en mode Shared et charge la scene active.
Option: ProvideInput active, nom de session fixe.

Author: Nicolas Vial
Company: Cavin-Baudat
Last modified: 20.08.2025

Unity: 2022.3.47f1
*/

using Fusion;
using UnityEngine;

/// <summary>
/// Lance un runner Fusion (mode Shared) et gere le chargement de scene.
/// </summary>
public class NetworkLauncher : MonoBehaviour
{
    private NetworkRunner _runner;

    private async void Start()
    {
        Debug.Log("Starting NetworkRunner...");
        _runner = gameObject.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;

        await _runner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = "Room_001",
            Scene = SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex),
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });
    }
}