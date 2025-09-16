using System;
using UnityEngine;
using UnityEngine.Events;

public class HandOrControllerSwitcher : MonoBehaviour
{
    public enum Mode { None, Controller, Hand }

    [Header("Rig roots à activer/désactiver")]
    [SerializeField] private GameObject controllerRoot;   // ray + line + scripts côté contrôleur
    [SerializeField] private GameObject rightHandRoot;         // ray + line + scripts côté main

    [Header("Démarrage")]
    [Tooltip("Mode actif au lancement (si 'persisterLeChoix' est vrai, on ignore cette valeur la 2e fois).")]
    [SerializeField] private Mode defaultMode = Mode.Controller;

    [Tooltip("Enregistrer le choix (PlayerPrefs) et le restaurer au prochain lancement.")]
    [SerializeField] private bool persisterLeChoix = true;

    [Serializable] public class ModeEvent : UnityEvent<Mode> { }
    [SerializeField] private ModeEvent onModeChanged;

    [SerializeField] private GameObject useHandTrackingButtonGO;
    [SerializeField] private GameObject useControllerButtonGO;

    public Mode ActiveMode { get; private set; } = Mode.None;

    const string PrefKey = "HandOrControllerSwitcher.ActiveMode";

    void Awake()
    {
        if (persisterLeChoix && PlayerPrefs.HasKey(PrefKey))
            defaultMode = (Mode)PlayerPrefs.GetInt(PrefKey);
    }

    void OnEnable() => SetMode(defaultMode);

    // --- Méthodes à brancher sur l'UI ------------------------------

    // Bouton "Contrôleur"
    public void UseController() => SetMode(Mode.Controller);

    // Bouton "Main"
    public void UseHand() => SetMode(Mode.Hand);

    // Bouton "Toggle" (alterne entre les deux)
    public void ToggleMode()
    {
        if (ActiveMode == Mode.Controller) SetMode(Mode.Hand);
        else SetMode(Mode.Controller);
    }

    // (Option) pour binder un Dropdown/ToggleGroup, etc.
    public void SetModeFromInt(int mode) => SetMode((Mode)mode);

    // ---------------------------------------------------------------

    public void SetMode(Mode m)
    {
        if (m == Mode.Controller)
        {
            useControllerButtonGO.SetActive(false);
            useHandTrackingButtonGO.SetActive(true);
        }
        else if (m == Mode.Hand)
        {
            useControllerButtonGO.SetActive(true);
            useHandTrackingButtonGO.SetActive(false);
        }
        if (ActiveMode == m) return;
        ActiveMode = m;

        controllerRoot.SetActive(m == Mode.Controller);
        rightHandRoot.SetActive(m == Mode.Hand);

        if (persisterLeChoix)
            PlayerPrefs.SetInt(PrefKey, (int)m);

        onModeChanged?.Invoke(m);
        // Debug.Log($"Input mode -> {m}");
    }
}
