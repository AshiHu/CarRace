using UnityEngine;

/// <summary>
/// ArcadeCamera — Caméra style arcade avec FOV dynamique, lag de rotation et légère inclinaison dans les virages.
/// À attacher sur la caméra principale, assigner la cible (le véhicule).
/// </summary>
public class ArcadeCamera : MonoBehaviour
{
    // ──────────────────────────────────────────
    //  CIBLE
    // ──────────────────────────────────────────
    [Header("=== CIBLE ===")]
    [Tooltip("Transform du véhicule à suivre")]
    public Transform carTarget;

    // ──────────────────────────────────────────
    //  POSITION / DISTANCE
    // ──────────────────────────────────────────
    [Header("=== POSITION ===")]
    [Tooltip("Distance derrière la voiture")]
    public float followDistance = 8f;

    [Tooltip("Hauteur de la caméra")]
    public float followHeight = 3f;

    [Tooltip("Lissage du suivi de position (plus petit = plus smooth)")]
    public float positionSmoothTime = 0.15f;

    // ──────────────────────────────────────────
    //  ROTATION / LAG
    // ──────────────────────────────────────────
    [Header("=== ROTATION ===")]
    [Tooltip("Vitesse de rotation de la caméra pour suivre la voiture")]
    public float rotationSmoothSpeed = 5f;

    [Tooltip("Décalage de regard vers l'avant de la voiture")]
    public float lookAheadDistance = 5f;

    // ──────────────────────────────────────────
    //  FOV DYNAMIQUE
    // ──────────────────────────────────────────
    [Header("=== FOV DYNAMIQUE ===")]
    [Tooltip("FOV de base")]
    public float baseFOV = 65f;

    [Tooltip("FOV max à pleine vitesse")]
    public float maxFOV = 85f;

    [Tooltip("Vitesse à laquelle le FOV max est atteint (km/h)")]
    public float fovMaxSpeed = 150f;

    [Tooltip("Lissage du FOV")]
    public float fovSmoothSpeed = 3f;

    // ──────────────────────────────────────────
    //  INCLINAISON EN VIRAGE (Camera Roll)
    // ──────────────────────────────────────────
    [Header("=== INCLINAISON VIRAGE ===")]
    [Tooltip("Angle d'inclinaison max dans les virages (degrés)")]
    public float maxRollAngle = 6f;

    [Tooltip("Lissage de l'inclinaison")]
    public float rollSmoothSpeed = 4f;

    // ──────────────────────────────────────────
    //  SHAKE À L'ACCÉLÉRATION (optionnel)
    // ──────────────────────────────────────────
    [Header("=== CAMERA SHAKE ===")]
    [Tooltip("Intensité du shake à haute vitesse")]
    public float shakeIntensity = 0.02f;

    [Tooltip("Vitesse minimale pour déclencher le shake (km/h)")]
    public float shakeMinSpeed = 120f;

    // ──────────────────────────────────────────
    //  PRIVÉ
    // ──────────────────────────────────────────
    private Camera cam;
    private Rigidbody carRigidbody;
    private Vector3 currentVelocity;        // pour SmoothDamp
    private float currentRoll = 0f;
    private float currentFOV;

    // ──────────────────────────────────────────
    //  INIT
    // ──────────────────────────────────────────
    void Start()
    {
        cam = GetComponent<Camera>();

        if (carTarget != null)
            carRigidbody = carTarget.GetComponent<Rigidbody>();

        currentFOV = baseFOV;

        if (cam != null)
            cam.fieldOfView = baseFOV;
    }

    // ──────────────────────────────────────────
    //  UPDATE CAMÉRA (LateUpdate pour être après la physique)
    // ──────────────────────────────────────────
    void LateUpdate()
    {
        if (carTarget == null) return;

        float carSpeed = carRigidbody != null
            ? carRigidbody.linearVelocity.magnitude * 3.6f
            : 0f;

        UpdatePosition();
        UpdateRotation();
        UpdateFOV(carSpeed);
        UpdateRoll();
        UpdateShake(carSpeed);
    }

    // ── Suivi de position ─────────────────────
    void UpdatePosition()
    {
        // Position cible : derrière la voiture + hauteur
        Vector3 targetPos = carTarget.position
                          - carTarget.forward * followDistance
                          + Vector3.up * followHeight;

        // SmoothDamp pour un suivi fluide
        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPos,
            ref currentVelocity,
            positionSmoothTime
        );
    }

    // ── Rotation vers la voiture ──────────────
    void UpdateRotation()
    {
        // Point de regard = position voiture + un peu devant
        Vector3 lookTarget = carTarget.position + carTarget.forward * lookAheadDistance;
        Vector3 direction = lookTarget - transform.position;

        if (direction == Vector3.zero) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * rotationSmoothSpeed
        );
    }

    // ── FOV dynamique ─────────────────────────
    void UpdateFOV(float speed)
    {
        if (cam == null) return;

        float speedRatio = Mathf.Clamp01(speed / fovMaxSpeed);
        float targetFOV = Mathf.Lerp(baseFOV, maxFOV, speedRatio);

        currentFOV = Mathf.Lerp(currentFOV, targetFOV, Time.deltaTime * fovSmoothSpeed);
        cam.fieldOfView = currentFOV;
    }

    // ── Inclinaison dans les virages ──────────
    void UpdateRoll()
    {
        float steerInput = Input.GetAxis("Horizontal");
        float targetRoll = -steerInput * maxRollAngle;

        currentRoll = Mathf.Lerp(currentRoll, targetRoll, Time.deltaTime * rollSmoothSpeed);

        // Applique le roll sur l'axe Z de la rotation actuelle
        Vector3 euler = transform.eulerAngles;
        transform.eulerAngles = new Vector3(euler.x, euler.y, currentRoll);
    }

    // ── Shake à haute vitesse ─────────────────
    void UpdateShake(float speed)
    {
        if (speed < shakeMinSpeed) return;

        float shakeAmount = shakeIntensity * ((speed - shakeMinSpeed) / (fovMaxSpeed - shakeMinSpeed));
        transform.position += new Vector3(
            Random.Range(-shakeAmount, shakeAmount),
            Random.Range(-shakeAmount, shakeAmount),
            0f
        );
    }

    // ──────────────────────────────────────────
    //  MÉTHODES PUBLIQUES UTILES
    // ──────────────────────────────────────────

    /// <summary>Téléporte la caméra directement derrière la voiture (utile au respawn)</summary>
    public void SnapToTarget()
    {
        if (carTarget == null) return;
        transform.position = carTarget.position
                           - carTarget.forward * followDistance
                           + Vector3.up * followHeight;
        transform.LookAt(carTarget.position);
    }
}