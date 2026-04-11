using UnityEngine;

/// <summary>
/// ArcadeCarController — À attacher sur le même GameObject que RCCP_CarController.
/// Gère : vitesse, frein, virage arcade, son moteur custom.
/// </summary>
[RequireComponent(typeof(RCCP_CarController))]
public class ArcadeCarController : MonoBehaviour
{
    // ──────────────────────────────────────────
    //  RÉFÉRENCES
    // ──────────────────────────────────────────
    private RCCP_CarController rccp;
    private Rigidbody rb;

    // ──────────────────────────────────────────
    //  VITESSE
    // ──────────────────────────────────────────
    [Header("=== VITESSE ===")]
    [Tooltip("Vitesse max en km/h")]
    public float maxSpeed = 180f;

    [Tooltip("Force d'accélération arcade (plus élevé = démarrage plus nerveux)")]
    public float accelerationForce = 4000f;

    [Tooltip("Multiplicateur de force en marche arrière")]
    public float reverseMultiplier = 0.5f;

    // ──────────────────────────────────────────
    //  FREIN
    // ──────────────────────────────────────────
    [Header("=== FREIN ===")]
    [Tooltip("Force de freinage (plus élevé = freinage brutal)")]
    public float brakeForce = 8000f;

    [Tooltip("Décélération naturelle sans input (frottement simulé)")]
    public float naturalDeceleration = 500f;

    // ──────────────────────────────────────────
    //  VIRAGE ARCADE
    // ──────────────────────────────────────────
    [Header("=== VIRAGE ARCADE ===")]
    [Tooltip("Vitesse de rotation dans les virages (degrés/sec)")]
    public float turnSpeed = 120f;

    [Tooltip("Le virage est plus facile à vitesse basse (0 = non, 1 = oui progressif)")]
    [Range(0f, 1f)]
    public float turnSpeedAtLowSpeed = 0.5f;

    [Tooltip("Grip latéral — réduit le dérapage (0 = glisse, 1 = grip total)")]
    [Range(0f, 1f)]
    public float lateralGrip = 0.85f;

    [Tooltip("Facteur de dérapage en oversteer (effet arcade)")]
    [Range(0f, 1f)]
    public float driftFactor = 0.15f;

    // ──────────────────────────────────────────
    //  SON
    // ──────────────────────────────────────────
    [Header("=== SON MOTEUR ===")]
    [Tooltip("AudioSource pour le son moteur (optionnel, remplace RCCP audio)")]
    public AudioSource engineAudioSource;

    [Tooltip("Pitch minimum au ralenti")]
    public float pitchMin = 0.8f;

    [Tooltip("Pitch maximum à pleine vitesse")]
    public float pitchMax = 2.5f;

    [Tooltip("Volume moteur")]
    [Range(0f, 1f)]
    public float engineVolume = 0.7f;

    // ──────────────────────────────────────────
    //  PRIVÉ
    // ──────────────────────────────────────────
    private float currentSpeed;     // km/h
    private float inputAccel;
    private float inputSteer;
    private bool inputBrake;

    // ──────────────────────────────────────────
    //  INIT
    // ──────────────────────────────────────────
    void Start()
    {
        rccp = GetComponent<RCCP_CarController>();
        rb = GetComponent<Rigidbody>();

        // On désactive le contrôle natif RCCP pour tout gérer ici
        // (garde CanControl = true pour que RCCP initialise bien ses composants)
        // On va juste injecter nos propres forces

        if (engineAudioSource != null)
        {
            engineAudioSource.loop = true;
            engineAudioSource.volume = engineVolume;
            engineAudioSource.Play();
        }

        Debug.Log("[ArcadeCarController] Initialisé !");
    }

    // ──────────────────────────────────────────
    //  LECTURE INPUTS
    // ──────────────────────────────────────────
    void Update()
    {
        inputAccel = Input.GetAxis("Vertical");    // W/S ou flèches
        inputSteer = Input.GetAxis("Horizontal");  // A/D ou flèches
        inputBrake = Input.GetKey(KeyCode.Space);

        UpdateEngineSound();
    }

    // ──────────────────────────────────────────
    //  PHYSIQUE
    // ──────────────────────────────────────────
    void FixedUpdate()
    {
        currentSpeed = rb.linearVelocity.magnitude * 3.6f; // m/s → km/h

        ApplyAcceleration();
        ApplySteering();
        ApplyBrake();
        ApplyLateralGrip();
        ClampMaxSpeed();
    }

    // ── Accélération ──────────────────────────
    void ApplyAcceleration()
    {
        if (inputBrake) return;

        float direction = inputAccel >= 0 ? 1f : reverseMultiplier * -1f;
        float force = Mathf.Abs(inputAccel) * accelerationForce * direction;

        rb.AddForce(transform.forward * force, ForceMode.Force);

        // Décélération naturelle si pas d'input
        if (Mathf.Abs(inputAccel) < 0.05f)
        {
            rb.AddForce(-rb.linearVelocity.normalized * naturalDeceleration, ForceMode.Force);
        }
    }

    // ── Virage ────────────────────────────────
    void ApplySteering()
    {
        if (currentSpeed < 1f) return;

        // Le grip de virage augmente avec la vitesse (effet arcade)
        float speedFactor = Mathf.Clamp01(currentSpeed / maxSpeed);
        float effectiveTurn = turnSpeed * inputSteer * Time.fixedDeltaTime;

        // À basse vitesse on tourne un peu moins vite (plus naturel)
        effectiveTurn *= Mathf.Lerp(turnSpeedAtLowSpeed, 1f, speedFactor);

        // Rotation du rigidbody
        Quaternion turnRotation = Quaternion.Euler(0f, effectiveTurn, 0f);
        rb.MoveRotation(rb.rotation * turnRotation);
    }

    // ── Frein ─────────────────────────────────
    void ApplyBrake()
    {
        if (!inputBrake) return;

        Vector3 brakeVelocity = -rb.linearVelocity.normalized * brakeForce * Time.fixedDeltaTime;
        rb.AddForce(brakeVelocity, ForceMode.VelocityChange);
    }

    // ── Grip latéral (anti-dérapage arcade) ───
    void ApplyLateralGrip()
    {
        // Calcule la vélocité latérale (dérapage)
        Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
        float lateralSpeed = localVelocity.x;

        // Applique un grip pour réduire le dérapage
        float gripCorrection = -lateralSpeed * lateralGrip * (1f - driftFactor);
        rb.AddForce(transform.right * gripCorrection * rb.mass, ForceMode.Force);
    }

    // ── Limite la vitesse max ─────────────────
    void ClampMaxSpeed()
    {
        if (currentSpeed > maxSpeed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * (maxSpeed / 3.6f);
        }
    }

    // ──────────────────────────────────────────
    //  SON MOTEUR
    // ──────────────────────────────────────────
    void UpdateEngineSound()
    {
        if (engineAudioSource == null) return;

        float speedRatio = Mathf.Clamp01(currentSpeed / maxSpeed);

        // Le pitch monte avec la vitesse + un peu avec l'accélération
        float targetPitch = Mathf.Lerp(pitchMin, pitchMax, speedRatio)
                          + Mathf.Abs(inputAccel) * 0.2f;

        engineAudioSource.pitch = Mathf.Lerp(engineAudioSource.pitch, targetPitch, Time.deltaTime * 5f);
        engineAudioSource.volume = Mathf.Lerp(engineVolume * 0.6f, engineVolume, speedRatio + 0.2f);
    }

    // ──────────────────────────────────────────
    //  GIZMO DEBUG (visible dans Scene)
    // ──────────────────────────────────────────
    void OnDrawGizmosSelected()
    {
        if (rb == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawRay(transform.position, transform.forward * 3f);
        Gizmos.color = Color.red;
        Gizmos.DrawRay(transform.position, rb.linearVelocity.normalized * 3f);
    }
}