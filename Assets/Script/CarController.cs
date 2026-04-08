using UnityEngine;

/// <summary>
/// Contrôleur de voiture pour "Prometheus"
/// Hiérarchie attendue :
///   Prometheus
///     Body
///     Wheels
///       Meshes
///         FrontLeftWheel, FrontRightWheel, RearLeftWheel, RearRightWheel
///       Colliders
///         FrontLeftWheel, FrontRightWheel, RearLeftWheel, RearRightWheel
///     Effects
///       LeftTireSmoke_PS, RightTireSmoke_PS
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PrometheusCarController : MonoBehaviour
{
    [Header("Références Roues - Colliders")]
    public WheelCollider WC_FrontLeft;
    public WheelCollider WC_FrontRight;
    public WheelCollider WC_RearLeft;
    public WheelCollider WC_RearRight;

    [Header("Références Roues - Meshes (visuels)")]
    public Transform Mesh_FrontLeft;
    public Transform Mesh_FrontRight;
    public Transform Mesh_RearLeft;
    public Transform Mesh_RearRight;

    [Header("Effets de fumée")]
    public ParticleSystem LeftTireSmoke_PS;
    public ParticleSystem RightTireSmoke_PS;

    [Header("Moteur")]
    [Tooltip("Couple moteur maximal (Nm)")]
    public float motorTorque = 1500f;
    [Tooltip("Couple de freinage (Nm)")]
    public float brakeTorque = 3000f;
    [Tooltip("Angle de braquage maximal (degrés)")]
    public float maxSteerAngle = 35f;
    [Tooltip("Vitesse maximale en km/h")]
    public float maxSpeed = 180f;

    [Header("Drift")]
    [Tooltip("Touche pour déclencher le drift")]
    public KeyCode driftKey = KeyCode.Space;
    [Tooltip("Rigidité latérale des roues arrière en mode drift (0 = glisse totale)")]
    [Range(0f, 1f)]
    public float driftRearStiffness = 0.3f;
    [Tooltip("Rigidité latérale normale des roues arrière")]
    [Range(0.5f, 2f)]
    public float normalRearStiffness = 1.2f;
    [Tooltip("Multiplicateur d'angle de braquage pendant le drift")]
    public float driftSteerMultiplier = 1.4f;

    [Header("Centre de gravité")]
    [Tooltip("Abaisse le centre de gravité pour plus de stabilité")]
    public Vector3 centerOfMassOffset = new Vector3(0f, -0.4f, 0f);

    // ── Privé ─────────────────────────────────────────────────────────────────
    private Rigidbody rb;
    private float currentMotorTorque;
    private float currentSteerAngle;
    private bool isDrifting;

    // Valeurs originales des frictions pour restaurer après le drift
    private WheelFrictionCurve originalRearSidewaysFriction;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass += centerOfMassOffset;

        // Sauvegarde de la friction arrière d'origine
        originalRearSidewaysFriction = WC_RearLeft.sidewaysFriction;
    }

    void FixedUpdate()
    {
        float verticalInput = 0f;
        float horizontalInput = 0f;

        // ── Entrées ZQSD ─────────────────────────────────────────────────────
        verticalInput = Input.GetAxis("Vertical");
        horizontalInput = Input.GetAxis("Horizontal");

        isDrifting = Input.GetKey(driftKey);

        HandleMotor(verticalInput);
        HandleSteering(horizontalInput);
        HandleDrift();
        UpdateWheelMeshes();
        HandleSmoke();
    }

    // ── Moteur & freins ───────────────────────────────────────────────────────
    void HandleMotor(float input)
    {
        float speedKmh = rb.linearVelocity.magnitude * 3.6f;
        bool atMaxSpeed = speedKmh >= maxSpeed && input > 0f;

        float torque = atMaxSpeed ? 0f : motorTorque * input;

        // Traction arrière (propulsion)
        WC_RearLeft.motorTorque = torque;
        WC_RearRight.motorTorque = torque;

        // Frein main si on appuie sur S et qu'on roule vers l'avant,
        // ou frein normal si input = 0
        float brake = 0f;
        if (input < 0f && rb.linearVelocity.magnitude > 0.5f)
        {
            float dot = Vector3.Dot(rb.linearVelocity, transform.forward);
            if (dot > 0f) brake = brakeTorque; // on freine en avant → ralentit
        }

        WC_FrontLeft.brakeTorque = brake;
        WC_FrontRight.brakeTorque = brake;
        WC_RearLeft.brakeTorque = isDrifting ? brakeTorque * 0.5f : 0f;
        WC_RearRight.brakeTorque = isDrifting ? brakeTorque * 0.5f : 0f;
    }

    // ── Direction ─────────────────────────────────────────────────────────────
    void HandleSteering(float input)
    {
        float multiplier = isDrifting ? driftSteerMultiplier : 1f;
        currentSteerAngle = maxSteerAngle * multiplier * input;
        WC_FrontLeft.steerAngle = currentSteerAngle;
        WC_FrontRight.steerAngle = currentSteerAngle;
    }

    // ── Drift ─────────────────────────────────────────────────────────────────
    void HandleDrift()
    {
        WheelFrictionCurve rearFriction = originalRearSidewaysFriction;

        if (isDrifting)
        {
            rearFriction.stiffness = driftRearStiffness;
        }
        else
        {
            rearFriction.stiffness = normalRearStiffness;
        }

        WC_RearLeft.sidewaysFriction = rearFriction;
        WC_RearRight.sidewaysFriction = rearFriction;
    }

    // ── Synchronisation visuelle des meshes ───────────────────────────────────
    void UpdateWheelMeshes()
    {
        UpdateSingleWheel(WC_FrontLeft, Mesh_FrontLeft);
        UpdateSingleWheel(WC_FrontRight, Mesh_FrontRight);
        UpdateSingleWheel(WC_RearLeft, Mesh_RearLeft);
        UpdateSingleWheel(WC_RearRight, Mesh_RearRight);
    }

    void UpdateSingleWheel(WheelCollider col, Transform mesh)
    {
        if (mesh == null) return;
        col.GetWorldPose(out Vector3 pos, out Quaternion rot);
        mesh.SetPositionAndRotation(pos, rot);
    }

    // ── Fumée des pneus ───────────────────────────────────────────────────────
    void HandleSmoke()
    {
        bool shouldSmoke = isDrifting && rb.linearVelocity.magnitude > 2f;

        SetSmoke(LeftTireSmoke_PS, shouldSmoke);
        SetSmoke(RightTireSmoke_PS, shouldSmoke);
    }

    void SetSmoke(ParticleSystem ps, bool active)
    {
        if (ps == null) return;
        if (active && !ps.isPlaying) ps.Play();
        if (!active && ps.isPlaying) ps.Stop();
    }

    // ── Gizmos debug (optionnel) ──────────────────────────────────────────────
    void OnDrawGizmosSelected()
    {
        if (rb == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(transform.TransformPoint(rb.centerOfMass), 0.1f);
    }
}